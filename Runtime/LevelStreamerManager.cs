using System.Collections.Generic;
using OpenWorldToolkit.SpatialHash;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[ExecuteAlways]
public class LevelStreamerManager : MonoBehaviour
{
    public LevelStreamerSettingsAsset streamerSettings;

    private Transform probe;
    private int cellSize;

    // Your existing mapping: one Addressable per cell
    public Dictionary<SpatialCell, AssetReference> cells = new Dictionary<SpatialCell, AssetReference>();

    // --- Robust streaming state ---
    // Loaded instances
    private readonly Dictionary<SpatialCell, GameObject> _instances = new Dictionary<SpatialCell, GameObject>();
    // In-flight loads
    private readonly Dictionary<SpatialCell, AsyncOperationHandle<GameObject>> _loading = new Dictionary<SpatialCell, AsyncOperationHandle<GameObject>>();
    // Last computed desired set (3x3 neighborhood)
    private readonly HashSet<SpatialCell> _desired = new HashSet<SpatialCell>();
    // Generation token to invalidate late completions
    private int _streamGen = 0;
    private SpatialCell _lastCenter;
    private bool _hasCenter;

    void OnEnable()
    {
        if (streamerSettings == null)
            return;

        probe = Camera.main ? Camera.main.transform : null;
        cellSize = streamerSettings.cellSize;

        cells.Clear();
        foreach (var asset in streamerSettings.assets)
        {
            var cell = SpatialCell.FromVector3(asset.position, cellSize);
            if (!cells.ContainsKey(cell))
            {
                cells.Add(cell, new AssetReference(asset.asset.AssetGUID));
            }
        }
        streamerSettings = null;

        _instances.Clear();
        _loading.Clear();
        _desired.Clear();
        _hasCenter = false;
        _streamGen++; // bump generation
    }

    void OnDisable()
    {
        UnloadAll();
    }

    private void Update()
    {
        if (probe == null || cellSize <= 0 || cells.Count == 0)
            return;

        var center = SpatialCell.FromVector3(probe.position, cellSize);
        if (_hasCenter && center.x == _lastCenter.x && center.z == _lastCenter.z)
            return;

        Stream3x3(center);
        _lastCenter = center;
        _hasCenter = true;
    }

    // -------- Streaming core (authoritative per frame) --------

    private void Stream3x3(SpatialCell center)
    {
        _streamGen++;                 // new desired world state
        _desired.Clear();

        // Build desired 3x3
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                _desired.Add(new SpatialCell { x = center.x + dx, z = center.z + dz });

        // 1) Unload anything NOT desired (covers both loaded and loading)
        //    Use temp lists to avoid modifying collections while iterating
        var toRelease = new List<SpatialCell>();
        foreach (var kv in _instances)
            if (!_desired.Contains(kv.Key)) toRelease.Add(kv.Key);
        foreach (var cell in toRelease) UnloadCell(cell);

        var toCancel = new List<SpatialCell>();
        foreach (var kv in _loading)
            if (!_desired.Contains(kv.Key)) toCancel.Add(kv.Key);
        foreach (var cell in toCancel) CancelLoad(cell);

        // 2) For desired cells: if not loaded and not loading, start loading
        foreach (var cell in _desired)
        {
            if (_instances.ContainsKey(cell) || _loading.ContainsKey(cell))
                continue;

            if (!cells.TryGetValue(cell, out var aref) || aref == null || string.IsNullOrEmpty(aref.AssetGUID))
                continue; // nothing mapped for this cell

            // Spawn at cell center (keeps your structure unchanged)
            Vector3 spawnPos = new Vector3(
                cell.x * cellSize + cellSize * 0.5f,
                0f,
                cell.z * cellSize + cellSize * 0.5f
            );

            int genAtStart = _streamGen; // capture generation for this request
            var handle = aref.InstantiateAsync(spawnPos, Quaternion.identity);
            _loading[cell] = handle;

            handle.Completed += op =>
            {
                // If this request is stale or cancelled, drop it
                if (!_loading.TryGetValue(cell, out var h) || !h.Equals(op))
                {
                    // Someone else replaced/cancelled this; if it produced a GO, release it.
                    if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
                        Addressables.ReleaseInstance(op.Result);
                    return;
                }
                // Remove from loading either way
                _loading.Remove(cell);

                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                    return;

                // If the world moved on (generation changed) or cell no longer desired, unload immediately
                if (genAtStart != _streamGen || !_desired.Contains(cell))
                {
                    Addressables.ReleaseInstance(op.Result);
                    return;
                }

                // Otherwise, track loaded instance
                _instances[cell] = op.Result;
            };
        }
    }

    private void UnloadCell(SpatialCell cell)
    {
        // If it is still loading, cancel first
        if (_loading.TryGetValue(cell, out var h))
        {
            Addressables.Release(h); // cancels/decrements ref count; ok pre-completion
            _loading.Remove(cell);
        }

        if (_instances.TryGetValue(cell, out var go) && go != null)
        {
            Addressables.ReleaseInstance(go);
        }
        _instances.Remove(cell);
    }

    private void CancelLoad(SpatialCell cell)
    {
        if (_loading.TryGetValue(cell, out var h))
        {
            Addressables.Release(h);
            _loading.Remove(cell);
        }
    }

    private void UnloadAll()
    {
        // Cancel all in-flight
        foreach (var kv in _loading)
            Addressables.Release(kv.Value);
        _loading.Clear();

        // Release all instances
        foreach (var kv in _instances)
            if (kv.Value != null) Addressables.ReleaseInstance(kv.Value);
        _instances.Clear();

        _desired.Clear();
        _streamGen++;
    }
}
