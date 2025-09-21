using System.Collections.Generic;
using NUnit.Framework;
using OpenWorldToolkit.SpatialHash;
using UnityEngine;
using UnityEngine.AddressableAssets;

[System.Serializable]
public struct StreamableAssetReference : ISpatialItem
{
    public Vector3 position;
    public AssetReference asset;

    public Vector3 GetPosition()
    {
        return position;
    }
}
public class LevelStreamerSettingsAsset : ScriptableObject
{
    public int cellSize;
    public List<StreamableAssetReference> assets;
}
