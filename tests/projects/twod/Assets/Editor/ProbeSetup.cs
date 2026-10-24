using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

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
        AddShapesAndSkin();
    }

    /// <summary>(Re)makes the sprite shapes and the skinned sprite, their assets and their objects
    /// in the probe scene, leaving the tilemaps as they are.</summary>
    public static void AddShapesAndSkin()
    {
        AssetDatabase.DeleteAsset("Assets/Shapes");
        Directory.CreateDirectory("Assets/Shapes");
        var edges = MakeEdgeSprites();
        var fill = MakeFillTexture();
        var profile = ScriptableObject.CreateInstance<SpriteShape>();
        profile.fillTexture = fill;
        profile.angleRanges.Add(new AngleRange { start = -50f, end = 50f, order = 1, sprites = new List<Sprite> { edges["edge_top"] } });
        profile.angleRanges.Add(new AngleRange { start = 50f, end = 180f, order = 0, sprites = new List<Sprite> { edges["edge_side"] } });
        profile.angleRanges.Add(new AngleRange { start = -180f, end = -50f, order = 0, sprites = new List<Sprite> { edges["edge_side"] } });
        AssetDatabase.CreateAsset(profile, "Assets/Shapes/profile.asset");
        MakeLimbSprite();
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        foreach (var name in new[] { "Shapes", "Skinned" })
        {
            var old = GameObject.Find(name);
            if (old != null) Object.DestroyImmediate(old);
        }
        profile = AssetDatabase.LoadAssetAtPath<SpriteShape>("Assets/Shapes/profile.asset");
        var root = new GameObject("Shapes").transform;
        root.position = new Vector3(40f, 0f, 0f);

        var hill = NewShape(root, "Hill", profile, false, new[]
        {
            new Vector3(-3f, -1.5f), new Vector3(-3f, 0.5f), new Vector3(0f, 2f), new Vector3(3f, 0.5f), new Vector3(3f, -1.5f),
        });
        hill.spline.SetTangentMode(2, ShapeTangentMode.Continuous);
        hill.spline.SetLeftTangent(2, new Vector3(-1.2f, 0f, 0f));
        hill.spline.SetRightTangent(2, new Vector3(1.2f, 0f, 0f));
        hill.spline.SetHeight(3, 0.6f);
        hill.gameObject.AddComponent<PolygonCollider2D>();

        var path = NewShape(root, "Path", profile, true, new[]
        {
            new Vector3(-3.5f, -3f), new Vector3(-1f, -2.5f), new Vector3(1.5f, -3.2f), new Vector3(3.5f, -2.6f),
        });
        path.gameObject.AddComponent<EdgeCollider2D>();

        var tinted = NewShape(root, "Tinted", profile, false, new[]
        {
            new Vector3(2f, 0f), new Vector3(2.5f, 1.5f), new Vector3(3.8f, 0.2f),
        });
        tinted.spriteShapeRenderer.color = new Color(1f, 0.6f, 0.6f, 0.8f);
        tinted.spriteShapeRenderer.sortingOrder = 1;

        var limb = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/limb.png");
        var skinned = new GameObject("Skinned");
        skinned.transform.position = new Vector3(50f, -1.5f, 0f);
        skinned.AddComponent<SpriteRenderer>().sprite = limb;
        var skin = skinned.AddComponent<SpriteSkin>();
        var bones = limb.GetBones();
        var transforms = new Transform[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            var bone = new GameObject(bones[i].name).transform;
            bone.SetParent(bones[i].parentId >= 0 ? transforms[bones[i].parentId] : skinned.transform, false);
            bone.localPosition = bones[i].position;
            bone.localRotation = bones[i].rotation;
            transforms[i] = bone;
        }
        skin.SetRootBone(transforms[0]);
        skin.SetBoneTransforms(transforms);

        EditorSceneManager.SaveScene(scene);
    }

    static SpriteShapeController NewShape(Transform root, string name, SpriteShape profile, bool open, Vector3[] points)
    {
        var go = new GameObject(name, typeof(SpriteShapeController));
        go.transform.SetParent(root, false);
        // Out of the tilemap scenarios' queries: the colliders hold placeholder points until the
        // controllers' first LateUpdate.
        go.layer = 2;
        var controller = go.GetComponent<SpriteShapeController>();
        controller.spriteShape = profile;
        controller.fillPixelsPerUnit = 32f;
        var spline = controller.spline;
        spline.Clear();
        for (int i = 0; i < points.Length; i++) spline.InsertPointAt(i, points[i]);
        spline.isOpenEnded = open;
        return controller;
    }

    static TextureImporter ImportArt(string path, Texture2D texture, TextureImporterType type, SpriteImportMode mode, float pixelsPerUnit,
        TextureWrapMode wrap, int alignment = (int)SpriteAlignment.Center)
    {
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = type;
        if (type == TextureImporterType.Sprite)
        {
            importer.spriteImportMode = mode;
            importer.spritePixelsPerUnit = pixelsPerUnit;
        }
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = wrap;
        if (type == TextureImporterType.Sprite)
        {
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = alignment;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
        }
        importer.SaveAndReimport();
        return importer;
    }

    static ISpriteEditorDataProvider Provider(TextureImporter importer)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        return provider;
    }

    /// <summary>Two 64x16 edge sprites: a grass top (dirt below, striped every 8 pixels) and a
    /// striped side.</summary>
    static Dictionary<string, Sprite> MakeEdgeSprites()
    {
        const string path = "Assets/Art/edge.png";
        var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 64; x++)
        {
            Color32 c;
            if (y >= 16)
            {
                int v = y - 16;
                c = v < 6 ? new Color32(70, 40, 10, 255) : (x / 8) % 2 == 0 ? new Color32(60, 200, 70, 255) : new Color32(150, 240, 120, 255);
                if (v == 15) c = new Color32(20, 90, 20, 255);
            }
            else
                c = (x / 4) % 2 == 0 ? new Color32(130, 80, 40, 255) : new Color32(90, 50, 20, 255);
            texture.SetPixel(x, y, c);
        }
        var importer = ImportArt(path, texture, TextureImporterType.Sprite, SpriteImportMode.Multiple, 16, TextureWrapMode.Clamp);
        var provider = Provider(importer);
        provider.SetSpriteRects(new[]
        {
            new SpriteRect { name = "edge_top", rect = new Rect(0, 16, 64, 16), alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f), spriteID = GUID.Generate() },
            new SpriteRect { name = "edge_side", rect = new Rect(0, 0, 64, 16), alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f), spriteID = GUID.Generate() },
        });
        provider.Apply();
        importer.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(x => x.name);
    }

    /// <summary>A 32x32 repeating checker for the fill.</summary>
    static Texture2D MakeFillTexture()
    {
        const string path = "Assets/Art/fill.png";
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
            texture.SetPixel(x, y, ((x / 8) + (y / 8)) % 2 == 0 ? new Color32(60, 90, 200, 255) : new Color32(120, 160, 240, 255));
        ImportArt(path, texture, TextureImporterType.Default, SpriteImportMode.None, 0, TextureWrapMode.Repeat);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>A 32x128 limb with two bones (upper, lower) and a mesh of five rows: the bottom two
    /// follow the upper bone, the middle one both, the top two the lower bone.</summary>
    static void MakeLimbSprite()
    {
        const string path = "Assets/Art/limb.png";
        var texture = new Texture2D(32, 128, TextureFormat.RGBA32, false);
        for (int y = 0; y < 128; y++)
        for (int x = 0; x < 32; x++)
        {
            Color32 c = x < 2 || x > 29 ? new Color32(40, 20, 0, 255)
                : (y / 16) % 2 == 0 ? new Color32(240, 150, 40, 255) : new Color32(250, 220, 90, 255);
            if (y == 64) c = new Color32(200, 0, 0, 255);
            texture.SetPixel(x, y, c);
        }
        var importer = ImportArt(path, texture, TextureImporterType.Sprite, SpriteImportMode.Single, 32, TextureWrapMode.Clamp,
            (int)SpriteAlignment.BottomCenter);
        var provider = Provider(importer);
        var id = provider.GetSpriteRects()[0].spriteID;
        provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(id, new List<SpriteBone>
        {
            new SpriteBone { name = "upper", guid = "5c7d0f1e2a3b4c5d6e7f8091a2b3c4d5", position = new Vector3(16f, 8f, 0f), rotation = Quaternion.Euler(0f, 0f, 90f), length = 56f, parentId = -1, color = new Color32(255, 0, 0, 255) },
            new SpriteBone { name = "lower", guid = "6d8e1f2a3b4c5d6e7f8091a2b3c4d5e6", position = new Vector3(56f, 0f, 0f), rotation = Quaternion.identity, length = 56f, parentId = 0, color = new Color32(0, 0, 255, 255) },
        });
        var vertices = new Vertex2DMetaData[10];
        for (int r = 0; r < 5; r++)
        for (int c = 0; c < 2; c++)
        {
            var weight = r < 2 ? new BoneWeight { boneIndex0 = 0, weight0 = 1f }
                : r == 2 ? new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f }
                : new BoneWeight { boneIndex0 = 1, weight0 = 1f };
            vertices[r * 2 + c] = new Vertex2DMetaData { position = new Vector2(c * 32f, r * 32f), boneWeight = weight };
        }
        var indices = new List<int>();
        var edges = new List<Vector2Int>();
        for (int r = 0; r < 4; r++)
        {
            int a = r * 2;
            indices.AddRange(new[] { a, a + 1, a + 3, a, a + 3, a + 2 });
            edges.Add(new Vector2Int(a, a + 2));
            edges.Add(new Vector2Int(a + 1, a + 3));
        }
        edges.Add(new Vector2Int(0, 1));
        edges.Add(new Vector2Int(8, 9));
        var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
        mesh.SetVertices(id, vertices);
        mesh.SetIndices(id, indices.ToArray());
        mesh.SetEdges(id, edges.ToArray());
        provider.Apply();
        importer.SaveAndReimport();
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
