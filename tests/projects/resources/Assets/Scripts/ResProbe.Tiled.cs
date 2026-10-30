using UnityEngine;

/// <summary>Sprites drawn Tiled (continuous and adaptive, with and without borders, smaller than
/// their borders, flipped, a tight mesh) and Sliced below the border size. The tile sprite's pixels
/// all differ, so the image shows which texel lands where.</summary>
public partial class ResProbe
{
    void TiledCase(string name, string sprite, Vector2 size, SpriteDrawMode mode, SpriteTileMode tileMode = SpriteTileMode.Continuous,
        float threshold = 0.5f, bool flipX = false, bool flipY = false, Vector3? position = null, Vector3? scale = null)
    {
        var camera = Camera2D(s_Bg);
        var r = SpriteAt(sprite, position ?? Vector3.zero, Color.white);
        r.drawMode = mode;
        r.size = size;
        r.tileMode = tileMode;
        r.adaptiveModeThreshold = threshold;
        r.flipX = flipX;
        r.flipY = flipY;
        if (scale.HasValue) r.transform.localScale = scale.Value;
        Value(name + ".bounds", "[" + V3(r.bounds.center) + "," + V3(r.bounds.size) + "]");
        Value(name + ".size", "[" + F(r.size.x) + "," + F(r.size.y) + "]");
        Capture(camera, name);
        Clear();
    }

    /// <summary>Four borderless tiled sprites, one tile high, 2.2, 2.4, 2.6 and 2.8 tiles wide (top
    /// to bottom), adaptive with the given threshold, set before or after the size.</summary>
    void Thresholds(string name, float threshold, bool thresholdFirst)
    {
        var camera = Camera2D(s_Bg);
        for (int i = 0; i < 4; i++)
        {
            var r = SpriteAt("TileNoBorder", new Vector3(0f, 1.8f - 1.2f * i, 0f), Color.white);
            if (thresholdFirst)
            {
                r.tileMode = SpriteTileMode.Adaptive;
                r.adaptiveModeThreshold = threshold;
            }
            r.drawMode = SpriteDrawMode.Tiled;
            r.size = new Vector2(0.75f * (2.2f + 0.2f * i), 0.625f);
            r.tileMode = SpriteTileMode.Adaptive;
            r.adaptiveModeThreshold = threshold;
            if (i == 0 || i == 19)
                Value(name + ".row" + i, "[" + V3(r.bounds.center) + "," + V3(r.bounds.size) + "," + V3(r.transform.lossyScale) + "," + F(r.size.x) + "," + F(r.sprite.pixelsPerUnit) + "," + F(r.sprite.bounds.size.x) + "]");
        }
        Capture(camera, name);
        Clear();
    }

    /// <summary>Twenty borderless adaptive sprites, 2.0125 to 2.9625 tiles wide (no fraction on a
    /// threshold: Mono computes the sizes in double precision), squashed into rows.</summary>
    void FineThresholds(string name, float threshold, float ppu = 8f)
    {
        var camera = Camera2D(s_Bg);
        var source = LoadSprite("TileNoBorder");
        var sprite = ppu == 8f ? source : Add(Sprite.Create(source.texture, source.rect, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect));
        float w = 6f / ppu, h = 5f / ppu;
        for (int i = 0; i < 20; i++)
        {
            var r = SpriteAt("TileNoBorder", new Vector3(0f, 2.3f - 0.23f * i, 0f), Color.white, scale: new Vector3(0.75f / w, 0.3f * 0.625f / h, 1f));
            r.sprite = sprite;
            r.drawMode = SpriteDrawMode.Tiled;
            r.size = new Vector2(w * (2.0125f + 0.05f * i), h);
            r.tileMode = SpriteTileMode.Adaptive;
            r.adaptiveModeThreshold = threshold;
            if (i == 0 || i == 19)
                Value(name + ".row" + i, "[" + V3(r.bounds.center) + "," + V3(r.bounds.size) + "," + V3(r.transform.lossyScale) + "," + F(r.size.x) + "," + F(r.sprite.pixelsPerUnit) + "," + F(r.sprite.bounds.size.x) + "]");
        }
        Capture(camera, name);
        Clear();
    }

    string RState(SpriteRenderer r) => "[" + V3(r.transform.localScale) + ",[" + F(r.size.x) + "," + F(r.size.y) + "]]";

    /// <summary>What a renderer's size and its transform's scale do as sprites and draw modes change
    /// (the Editor keeps a sprite's look when its draw mode leaves Simple).</summary>
    void SpriteSizes()
    {
        var a = LoadSprite("TileNoBorder");
        var b = Add(Sprite.Create(a.texture, a.rect, new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect));
        var go = Add(new GameObject("e1"));
        go.transform.localScale = new Vector3(0.5f, 2f, 1f);
        var r = go.AddComponent<SpriteRenderer>();
        Value("size.e1.new", RState(r));
        r.sprite = a; Value("size.e1.a", RState(r));
        r.sprite = b; Value("size.e1.b", RState(r));
        r.drawMode = SpriteDrawMode.Sliced; Value("size.e1.sliced", RState(r));
        r.drawMode = SpriteDrawMode.Tiled; Value("size.e1.tiled", RState(r));
        r.drawMode = SpriteDrawMode.Simple; Value("size.e1.simple", RState(r));
        r.sprite = a; Value("size.e1.a2", RState(r));
        r.drawMode = SpriteDrawMode.Tiled; Value("size.e1.tiled2", RState(r));
        r.sprite = b; Value("size.e1.b2", RState(r));
        r.size = new Vector2(2f, 3f); Value("size.e1.set", RState(r));
        r.drawMode = SpriteDrawMode.Simple; Value("size.e1.simple2", RState(r));
        r.size = new Vector2(5f, 1f); Value("size.e1.setsimple", RState(r));
        r.drawMode = SpriteDrawMode.Sliced; Value("size.e1.sliced2", RState(r));
        var go2 = Add(new GameObject("e2"));
        var r2 = go2.AddComponent<SpriteRenderer>();
        r2.drawMode = SpriteDrawMode.Tiled; Value("size.e2.tiled", RState(r2));
        r2.sprite = b; Value("size.e2.b", RState(r2));
        r2.sprite = null; Value("size.e2.null", RState(r2));
        r2.drawMode = SpriteDrawMode.Simple; Value("size.e2.simple", RState(r2));
        r2.sprite = a; Value("size.e2.a", RState(r2));
        r2.sprite = null; Value("size.e2.null2", RState(r2));
        r2.sprite = b; Value("size.e2.b2", RState(r2));
        Clear();
    }

    void Tiled()
    {
        SpriteSizes();
        // Adaptive tiles: a tile is added once the size's fraction of a tile reaches the threshold
        // times the tile's size in units (measured with tiles of 0.375, 0.75 and 1.5 units).
        FineThresholds("tiled_fine_50", 0.5f);
        FineThresholds("tiled_fine4_60", 0.6f, 4f);
        FineThresholds("tiled_fine16_60", 0.6f, 16f);
        Thresholds("tiled_threshold_1", 0.1f, false);
        Thresholds("tiled_threshold_7", 0.7f, true);
        TiledCase("tiled_continuous_short", "Tile", new Vector2(1.2f, 1.0f), SpriteDrawMode.Tiled, scale: new Vector3(2f, 2f, 1f));
        TiledCase("tiled_adaptive_short", "Tile", new Vector2(1.0f, 0.8f), SpriteDrawMode.Tiled, SpriteTileMode.Adaptive, scale: new Vector3(2f, 2f, 1f));
        TiledCase("tiled_continuous", "Tile", new Vector2(4.1f, 3.3f), SpriteDrawMode.Tiled);
        TiledCase("tiled_continuous_noborder", "TileNoBorder", new Vector2(3.1f, 2.2f), SpriteDrawMode.Tiled);
        TiledCase("tiled_adaptive_05", "Tile", new Vector2(4.1f, 3.3f), SpriteDrawMode.Tiled, SpriteTileMode.Adaptive, 0.5f);
        TiledCase("tiled_adaptive_09", "Tile", new Vector2(4.1f, 3.3f), SpriteDrawMode.Tiled, SpriteTileMode.Adaptive, 0.9f);
        TiledCase("tiled_adaptive_01", "Tile", new Vector2(4.1f, 3.3f), SpriteDrawMode.Tiled, SpriteTileMode.Adaptive, 0.1f);
        TiledCase("tiled_adaptive_noborder", "TileNoBorder", new Vector2(3.1f, 2.2f), SpriteDrawMode.Tiled, SpriteTileMode.Adaptive, 0.5f);
        TiledCase("tiled_adaptive_exact", "Tile", new Vector2(3.6f, 3.0f), SpriteDrawMode.Tiled, SpriteTileMode.Adaptive, 0.5f);
        TiledCase("tiled_small", "Tile", new Vector2(0.5f, 0.4f), SpriteDrawMode.Tiled, scale: new Vector3(4f, 4f, 1f));
        TiledCase("tiled_flip", "Tile", new Vector2(4.1f, 3.3f), SpriteDrawMode.Tiled, flipX: true, flipY: true);
        TiledCase("tiled_tight", "TileTight", new Vector2(4.1f, 3.3f), SpriteDrawMode.Tiled);
        TiledCase("tiled_offset", "Tile", new Vector2(2.3f, 1.9f), SpriteDrawMode.Tiled, position: new Vector3(0.7f, -0.4f, 0f), scale: new Vector3(1.3f, 1.6f, 1f));
        TiledCase("sliced_small", "Tile", new Vector2(0.5f, 0.4f), SpriteDrawMode.Sliced, scale: new Vector3(4f, 4f, 1f));
        TiledCase("sliced_large", "Tile", new Vector2(4.1f, 3.3f), SpriteDrawMode.Sliced);
        TiledCase("sliced_flip", "Tile", new Vector2(4.1f, 3.3f), SpriteDrawMode.Sliced, flipX: true);
        TiledCase("sliced_tight", "TileTight", new Vector2(4.1f, 3.3f), SpriteDrawMode.Sliced);
    }
}
