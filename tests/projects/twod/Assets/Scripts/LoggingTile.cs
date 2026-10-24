using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>A tile that logs every call the tilemap makes into it, to record when and in which
/// order Unity calls tiles.</summary>
public class LoggingTile : TileBase
{
    public static readonly List<string> Log = new List<string>();
    public Sprite sprite;
    public Color color = Color.white;
    public TileFlags flags = TileFlags.LockColor;
    public GameObject instanced;
    public bool refreshNeighbours;

    static string P(Vector3Int p) => p.x + "," + p.y + "," + p.z;

    public override void RefreshTile(Vector3Int position, ITilemap tilemap)
    {
        Log.Add(name + ".RefreshTile(" + P(position) + ")");
        base.RefreshTile(position, tilemap);
        if (refreshNeighbours)
        {
            tilemap.RefreshTile(position + Vector3Int.right);
            tilemap.RefreshTile(position + Vector3Int.left);
        }
    }

    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        Log.Add(name + ".GetTileData(" + P(position) + ") in " + tileData.sprite + " " + tileData.color + " " + tileData.flags + " " + tileData.colliderType);
        tileData.sprite = sprite;
        tileData.color = color;
        tileData.flags = flags;
        tileData.gameObject = instanced;
        tileData.colliderType = Tile.ColliderType.Grid;
    }

    public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go)
    {
        Log.Add(name + ".StartUp(" + P(position) + ", " + (go != null ? go.name + " parent " + go.transform.parent.name + " at " + go.transform.localPosition : "null") + ")");
        return true;
    }

    public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData)
    {
        Log.Add(name + ".GetTileAnimationData(" + P(position) + ")");
        return false;
    }
}
