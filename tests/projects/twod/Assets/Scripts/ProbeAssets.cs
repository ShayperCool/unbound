using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>The probe's assets, referenced from the scene so scenarios can use them.</summary>
public class ProbeAssets : MonoBehaviour
{
    public Tile[] tiles;
    public Sprite[] sprites;
    public RuleTile rule;
    public AnimatedTile animated;
    public GameObject tilePrefab;

    public Tile Tile(string name)
    {
        foreach (var t in tiles) if (t.name == name) return t;
        throw new System.ArgumentException("no tile " + name);
    }
}
