using System.Collections.Generic;
using System.IO;
using Codice.Client.Common.GameUI;
using OpenWorldToolkit.SpatialHash;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets;
using System;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using System.Linq;

namespace OpenWorldToolkit.LevelStreaming.Editor
{
    [EditorWindowTab(1, "Streaming")]
    public class LevelStreamerTab : EditorWindowTabBase
    {
        private int cellSize;
        private LevelStreamerSettingsAsset settings;

        private Transform rootTransform;

        public override void Dispose() { }
        public override void Initialize() { }

        int _tabIndex;
        string[] tabs = { "Settings", "Baker" };

        public override void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                settings = (LevelStreamerSettingsAsset)EditorGUILayout.ObjectField("Streamer Asset", settings, typeof(LevelStreamerSettingsAsset), false);
                if (GUILayout.Button("New", GUILayout.Width(60)))
                {
                    CreateSettingsFile();
                }
            }
            if (!settings)
                return;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _tabIndex = GUILayout.Toolbar(_tabIndex, tabs, GUILayout.Height(24));
            }

            if (tabs[_tabIndex] == "Settings")
            {
                if (settings != null)
                {
                    var so = new SerializedObject(settings);
                    var pCell = so.FindProperty("cellSize");

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(pCell, new GUIContent("Cell Size"));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(settings, "Change Cell Size");
                        so.ApplyModifiedProperties();         // write to the object
                        EditorUtility.SetDirty(settings);     // mark asset dirty so it will be saved
                        AssetDatabase.SaveAssets();           // optional: write to disk immediately
                    }
                }
            }

            if (tabs[_tabIndex] == "Baker")
            {
                rootTransform = (Transform)EditorGUILayout.ObjectField("World Root", rootTransform, typeof(Transform), true);
                if (GUILayout.Button("Bake"))
                {
                    if (EditorUtility.DisplayDialog(
                        "Are you sure?",
                        "This will permanantley alter your scene. This action cannot be undone. You should always make a backup before performing this action",
                        "Yes", "No"))
                    {
                        BakeScene();
                    }
                }
            }
        }

        private readonly Dictionary<SpatialCell, Transform> cells = new Dictionary<SpatialCell, Transform>();

        // --- NEW: Unity-folder creation (AssetDatabase-safe)
        private static void EnsureUnityFolder(string folderAssetsPath)
        {
            folderAssetsPath = (folderAssetsPath ?? "Assets").Replace('\\', '/');
            if (folderAssetsPath == "Assets") return;

            var parts = folderAssetsPath.Split('/');
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private void BakeScene()
        {
            if (rootTransform == null) return;

            // 1) Snapshot ONLY the original direct children (no recursion)
            int n = rootTransform.childCount;
            var originals = new List<Transform>(n);
            for (int i = 0; i < n; i++)
                originals.Add(rootTransform.GetChild(i));

            // 2) Put all cell containers under a dedicated parent so they don't get mixed with originals
            var cellsRoot = new GameObject("CellsRoot").transform;
            cellsRoot.SetParent(rootTransform, worldPositionStays: false);

            // 3) Reparent from the snapshot
            cells.Clear(); // if you re-bake, start fresh
            int cellSizeLocal = settings.cellSize;

            foreach (var tr in originals)
            {
                if (tr == null) continue;

                var cell = SpatialCell.FromVector3(tr.position, cellSizeLocal);

                if (!cells.TryGetValue(cell, out Transform bucket))
                {
                    var go = new GameObject(cell.ToString());
                    go.transform.position = new Vector3(
                        (cell.x * cellSizeLocal) + (cellSizeLocal * 0.5f),
                        0f,
                        (cell.z * cellSizeLocal) + (cellSizeLocal * 0.5f)
                    );
                    go.transform.SetParent(cellsRoot, true);
                    bucket = go.transform;
                    cells.Add(cell, bucket);
                }

                tr.SetParent(bucket, true);
            }

            // 4) Save each cell group as a prefab under the Scene's folder /Chunks,
            //    or fall back to Assets/Chunks if the scene isn't saved.
            foreach (KeyValuePair<SpatialCell,Transform> kvp in cells)
            {
                var t = kvp.Value;
                if (t == null) continue;

                string scenePath = t.gameObject.scene.path; // e.g., "Assets/Scenes/City.unity" or "" if unsaved
                string sceneDir = string.IsNullOrEmpty(scenePath) ? "Assets" : (Path.GetDirectoryName(scenePath) ?? "Assets");
                sceneDir = sceneDir.Replace('\\', '/');

                // Use "Chunks" folder next to the scene
                string chunksDir = $"{sceneDir}/Chunks";
                EnsureUnityFolder(chunksDir);

                // Prefab path must be under Assets and end with .prefab
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{chunksDir}/{t.name}.prefab");

                bool success;
                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(t.gameObject, prefabPath, InteractionMode.AutomatedAction, out success);
                if (!success || prefab == null)
                {
                    Debug.LogError($"Failed to save prefab for '{t.name}' at {prefabPath}");
                    continue;
                }
               

                // Mark the prefab as Addressable (keeps your defaults)
                var entry = MarkAddressable(prefabPath,kvp.Key.ToString());
                var assetReference = new AssetReference(entry.guid);
                settings.assets.Add(new StreamableAssetReference()
                {
                    position = kvp.Value.position,
                    asset = assetReference
                });
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public void CreateSettingsFile()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create Streamer Asset", "LevelStreamer", "asset", "Save Streamer asset");
            if (string.IsNullOrEmpty(path)) return;

            var asset = ScriptableObject.CreateInstance<LevelStreamerSettingsAsset>();
            asset.cellSize = cellSize;
            asset.assets = new List<StreamableAssetReference>();

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            settings = asset;
            Selection.activeObject = asset;
        }

        public static AddressableAssetEntry MarkAddressable(
            string assetPath,
            string groupName = "Default Local Group",
            string address = null,
            string[] labels = null)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("MarkAddressable: assetPath is null/empty.");
                return null;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"MarkAddressable: No asset at path: {assetPath}");
                return null;
            }

            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Debug.LogError("MarkAddressable: Could not get/create AddressableAssetSettings.");
                return null;
            }

            var group = settings.FindGroup(groupName);
            if (group == null)
            {
                group = settings.CreateGroup(
                    groupName,
                    setAsDefaultGroup: false,
                    readOnly: false,
                    postEvent: true,
                    schemasToCopy: null,
                    typeof(BundledAssetGroupSchema),
                    typeof(ContentUpdateGroupSchema)
                );

                if (group == null)
                {
                    Debug.LogError($"MarkAddressable: Failed to create group '{groupName}'.");
                    return null;
                }
            }

            var entry = settings.CreateOrMoveEntry(guid, group, readOnly: false, postEvent: true);
            if (entry == null)
            {
                Debug.LogError("MarkAddressable: Failed to create/move entry.");
                return null;
            }

            entry.SetAddress(string.IsNullOrEmpty(address)
                ? System.IO.Path.GetFileNameWithoutExtension(assetPath)
                : address);

            if (labels != null && labels.Length > 0)
            {
                var existing = settings.GetLabels();
                foreach (var l in labels.Where(l => !string.IsNullOrEmpty(l)))
                {
                    if (!existing.Contains(l)) settings.AddLabel(l);
                    entry.SetLabel(l, true, true);
                }
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Addressable OK: '{entry.address}' → Group '{groupName}' ({assetPath})");

            return entry;
        }

        public override void Open()
        {
           // throw new NotImplementedException();
        }

        public override void Close()
        {
            //throw new NotImplementedException();
        }
    }
}
