using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Creates the probe's assets and scene (run once in the Editor:
/// <c>-executeMethod ProbeSetup.Create -quit</c>); the results are committed, so the Editor and
/// Unbound read the same files. A sprite sheet of eight 16x16 tiles, tile assets of every kind
/// (plain, tinted, transformed, rule, animated) and a scene with grids and painted tilemaps.
/// </summary>
public static class ProbeSetup
{
    const int T = 16;

    public static void Create()
    {
        AssetDatabase.DeleteAsset("Assets/Tiles");
        AssetDatabase.DeleteAsset("Assets/Scenes/Probe.unity");
        Directory.CreateDirectory("Assets/Art");
        Directory.CreateDirectory("Assets/Tiles");
        Directory.CreateDirectory("Assets/Scenes");
        var sprites = MakeTileSheet();
        var names = MakeTiles(sprites).Keys.ToList();
        AssetDatabase.SaveAssets();
        MakeScene(names);
        AssetDatabase.SaveAssets();
    }

    static Color32[] TilePixels(int index)
    {
        var px = new Color32[T * T];
        for (int y = 0; y < T; y++)
        for (int x = 0; x < T; x++)
        {
            Color32 c;
            bool border = x == 0 || y == 0 || x == T - 1 || y == T - 1;
            switch (index)
            {
                case 0: c = border ? new Color32(90, 0, 0, 255) : new Color32(230, 40, 30, 255); break;
                case 1: c = (x + y) % 4 < 2 ? new Color32(30, 200, 60, 255) : new Color32(10, 120, 30, 255); break;
                case 2: c = ((x / 4) + (y / 4)) % 2 == 0 ? new Color32(40, 80, 230, 255) : new Color32(200, 220, 255, 255); break;
                case 3: c = x + y < T ? new Color32(250, 220, 20, 255) : new Color32(0, 0, 0, 0); break;
                case 4: c = border ? new Color32(160, 160, 160, 255) : new Color32(255, 255, 255, 255); break;
                case 5: c = x > 3 && x < 12 && y > 3 && y < 12 ? new Color32(255, 255, 255, 255) : new Color32(200, 0, 200, 255); break;
                case 6: c = x < 8 ? new Color32(0, 220, 220, 255) : new Color32(0, 90, 90, 255); break;
                default: c = y < 8 ? new Color32(255, 140, 0, 255) : new Color32(120, 60, 0, 255); break;
            }
            px[y * T + x] = c;
        }
        return px;
    }

    static Dictionary<string, Sprite> MakeTileSheet()
    {
        const string path = "Assets/Art/tiles.png";
        var texture = new Texture2D(T * 8, T, TextureFormat.RGBA32, false);
        for (int i = 0; i < 8; i++) texture.SetPixels32(i * T, 0, T, T, TilePixels(i));
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = T;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteGenerateFallbackPhysicsShape = false;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var rects = new List<SpriteRect>();
        for (int i = 0; i < 8; i++)
        {
            rects.Add(new SpriteRect
            {
                name = "tile_" + i,
                rect = new Rect(i * T, 0, T, T),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = GUID.Generate(),
            });
        }
        provider.SetSpriteRects(rects.ToArray());
        var physics = provider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
        // Custom physics shapes (pixels from the rect centre): an inset square on the green tile,
        // the opaque triangle of the yellow one, an L on the orange one.
        physics.SetOutlines(rects[1].spriteID, new List<Vector2[]> { new[] { new Vector2(-6, -6), new Vector2(6, -6), new Vector2(6, 6), new Vector2(-6, 6) } });
        physics.SetOutlines(rects[3].spriteID, new List<Vector2[]> { new[] { new Vector2(-8, -8), new Vector2(8, -8), new Vector2(-8, 8) } });
        physics.SetOutlines(rects[7].spriteID, new List<Vector2[]> { new[] { new Vector2(-8, -8), new Vector2(8, -8), new Vector2(8, 0), new Vector2(0, 0), new Vector2(0, 8), new Vector2(-8, 8) } });
        provider.Apply();
        importer.SaveAndReimport();

        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
    }

    static TAsset Save<TAsset>(TAsset asset, string name) where TAsset : Object
    {
        AssetDatabase.CreateAsset(asset, "Assets/Tiles/" + name + ".asset");
        return asset;
    }

    static Dictionary<string, TileBase> MakeTiles(Dictionary<string, Sprite> s)
    {
        var tiles = new Dictionary<string, TileBase>();
        // Configured before saving: later changes to the in-memory asset are not written.
        Tile Plain(string name, string sprite, Tile.ColliderType collider, System.Action<Tile> configure = null)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = s[sprite];
            t.colliderType = collider;
            configure?.Invoke(t);
            tiles[name] = Save(t, name);
            return t;
        }
        Plain("red", "tile_0", Tile.ColliderType.Grid);
        Plain("green", "tile_1", Tile.ColliderType.Sprite);
        Plain("blue", "tile_2", Tile.ColliderType.None);
        Plain("tri", "tile_3", Tile.ColliderType.Sprite);
        Plain("ell", "tile_7", Tile.ColliderType.Sprite);
        Plain("white", "tile_4", Tile.ColliderType.Grid, w =>
        {
            w.flags = TileFlags.None;
            w.color = new Color(1f, 0.5f, 0.5f, 1f);
        });
        Plain("rotated", "tile_0", Tile.ColliderType.Grid, r =>
        {
            r.transform = Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), new Vector3(1f, 0.5f, 1f));
            r.flags = TileFlags.LockTransform;
        });
        Plain("tinted", "tile_4", Tile.ColliderType.None, t => t.color = new Color(0.25f, 0.75f, 1f, 1f));

        var rule = ScriptableObject.CreateInstance<RuleTile>();
        rule.m_DefaultSprite = s["tile_5"];
        rule.m_DefaultColliderType = Tile.ColliderType.Grid;
        var vertical = new RuleTile.TilingRule();
        vertical.m_Sprites[0] = s["tile_7"];
        vertical.ApplyNeighbors(new Dictionary<Vector3Int, int> { { Vector3Int.up, RuleTile.TilingRuleOutput.Neighbor.This }, { Vector3Int.down, RuleTile.TilingRuleOutput.Neighbor.This } });
        var leftEnd = new RuleTile.TilingRule();
        leftEnd.m_Sprites[0] = s["tile_6"];
        leftEnd.m_RuleTransform = RuleTile.TilingRuleOutput.Transform.MirrorX;
        leftEnd.ApplyNeighbors(new Dictionary<Vector3Int, int> { { Vector3Int.right, RuleTile.TilingRuleOutput.Neighbor.This }, { Vector3Int.left, RuleTile.TilingRuleOutput.Neighbor.NotThis } });
        var rotatedEnd = new RuleTile.TilingRule();
        rotatedEnd.m_Sprites[0] = s["tile_1"];
        rotatedEnd.m_RuleTransform = RuleTile.TilingRuleOutput.Transform.Rotated;
        rotatedEnd.m_ColliderType = Tile.ColliderType.Sprite;
        rotatedEnd.ApplyNeighbors(new Dictionary<Vector3Int, int> { { Vector3Int.up, RuleTile.TilingRuleOutput.Neighbor.This }, { Vector3Int.down, RuleTile.TilingRuleOutput.Neighbor.NotThis } });
        rule.m_TilingRules.Add(vertical);
        rule.m_TilingRules.Add(leftEnd);
        rule.m_TilingRules.Add(rotatedEnd);
        tiles["rule"] = Save(rule, "rule");

        var animated = ScriptableObject.CreateInstance<AnimatedTile>();
        animated.m_AnimatedSprites = new[] { s["tile_0"], s["tile_1"], s["tile_2"] };
        animated.m_MinSpeed = 2f;
        animated.m_MaxSpeed = 2f;
        animated.m_TileColliderType = Tile.ColliderType.Grid;
        tiles["animated"] = Save(animated, "animated");

        var marker = new GameObject("Marker");
        marker.AddComponent<SpriteRenderer>().sprite = s["tile_2"];
        var prefab = PrefabUtility.SaveAsPrefabAsset(marker, "Assets/Tiles/marker.prefab");
        Object.DestroyImmediate(marker);
        Plain("withObject", "tile_4", Tile.ColliderType.None, w =>
        {
            w.gameObject = prefab;
            w.flags = TileFlags.None;
        });
        return tiles;
    }

    static Tilemap Layer(Transform grid, string name, int order, TilemapRenderer.Mode mode)
    {
        var go = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        go.transform.SetParent(grid, false);
        var renderer = go.GetComponent<TilemapRenderer>();
        renderer.sortingOrder = order;
        renderer.mode = mode;
        return go.GetComponent<Tilemap>();
    }

    static void MakeScene(List<string> names)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // A new scene unloads assets only managed code references: load them afresh.
        var t = names.ToDictionary(n => n, n => AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tiles/" + n + ".asset"));
        var s = AssetDatabase.LoadAllAssetsAtPath("Assets/Art/tiles.png").OfType<Sprite>().ToDictionary(x => x.name);

        var assets = new GameObject("ProbeAssets").AddComponent<ProbeAssets>();
        assets.tiles = t.Values.OfType<Tile>().OrderBy(x => x.name).ToArray();
        assets.sprites = s.Values.OrderBy(x => x.name).ToArray();
        assets.rule = (RuleTile)t["rule"];
        assets.animated = (AnimatedTile)t["animated"];
        assets.tilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Tiles/marker.prefab");

        var grid = new GameObject("Grid", typeof(Grid)).transform;

        var ground = Layer(grid, "Ground", 0, TilemapRenderer.Mode.Chunk);
        for (int x = -6; x <= 5; x++) ground.SetTile(new Vector3Int(x, -3, 0), t["red"]);
        for (int y = -2; y <= 0; y++) ground.SetTile(new Vector3Int(-6, y, 0), t["green"]);
        ground.SetTile(new Vector3Int(4, -2, 0), t["tri"]);
        ground.SetTile(new Vector3Int(5, -2, 0), t["ell"]);
        ground.SetTile(new Vector3Int(0, -2, 0), t["blue"]);
        ground.SetTile(new Vector3Int(1, -2, 0), t["white"]);
        ground.SetColor(new Vector3Int(1, -2, 0), new Color(1f, 0.5f, 0f, 1f));
        ground.SetTile(new Vector3Int(2, -2, 0), t["rotated"]);
        ground.SetTile(new Vector3Int(-2, -2, 0), t["tinted"]);
        ground.SetTile(new Vector3Int(-3, -1, 0), t["animated"]);
        ground.SetTile(new Vector3Int(-1, 0, 0), t["red"]);
        ground.SetTile(new Vector3Int(-1, 1, 0), t["red"]);
        ground.SetTile(new Vector3Int(3, 0, 0), t["withObject"]);
        var groundBody = ground.gameObject.AddComponent<Rigidbody2D>();
        groundBody.bodyType = RigidbodyType2D.Static;
        var groundCollider = ground.gameObject.AddComponent<TilemapCollider2D>();
        groundCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
        var composite = ground.gameObject.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Outlines;

        var rules = Layer(grid, "Rules", 1, TilemapRenderer.Mode.Individual);
        for (int x = -4; x <= -1; x++) rules.SetTile(new Vector3Int(x, 3, 0), t["rule"]);
        for (int y = 1; y <= 3; y++) rules.SetTile(new Vector3Int(2, y, 0), t["rule"]);
        rules.SetTile(new Vector3Int(5, 3, 0), t["rule"]);
        rules.gameObject.AddComponent<TilemapCollider2D>();

        var tint = Layer(grid, "Tint", 2, TilemapRenderer.Mode.Chunk);
        tint.color = new Color(0.5f, 0.5f, 1f, 0.8f);
        tint.tileAnchor = new Vector3(0f, 0f, 0f);
        tint.SetTile(new Vector3Int(-5, 1, 0), t["white"]);
        tint.SetTile(new Vector3Int(-4, 1, 0), t["red"]);
        tint.SetTile(new Vector3Int(-4, 0, 0), t["blue"]);

        var hexGrid = new GameObject("HexGrid", typeof(Grid));
        hexGrid.transform.position = new Vector3(3f, 0f, 0f);
        var hex = hexGrid.GetComponent<Grid>();
        hex.cellLayout = GridLayout.CellLayout.Hexagon;
        hex.cellSize = new Vector3(1f, 1f, 0f);
        var hexMap = Layer(hexGrid.transform, "Hex", 3, TilemapRenderer.Mode.Chunk);
        hexMap.SetTile(new Vector3Int(0, 0, 0), t["blue"]);
        hexMap.SetTile(new Vector3Int(1, 1, 0), t["green"]);
        hexMap.SetTile(new Vector3Int(0, 2, 0), t["red"]);

        var isoGrid = new GameObject("IsoGrid", typeof(Grid));
        isoGrid.transform.position = new Vector3(-2f, -1f, 0f);
        var iso = isoGrid.GetComponent<Grid>();
        iso.cellLayout = GridLayout.CellLayout.Isometric;
        iso.cellSize = new Vector3(1f, 0.5f, 1f);
        var isoMap = Layer(isoGrid.transform, "Iso", 4, TilemapRenderer.Mode.Individual);
        isoMap.GetComponent<TilemapRenderer>().sortOrder = TilemapRenderer.SortOrder.TopRight;
        isoMap.SetTile(new Vector3Int(0, 0, 0), t["white"]);
        isoMap.SetTile(new Vector3Int(1, 0, 0), t["tri"]);
        isoMap.SetTile(new Vector3Int(0, 1, 0), t["green"]);

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
    }
}
