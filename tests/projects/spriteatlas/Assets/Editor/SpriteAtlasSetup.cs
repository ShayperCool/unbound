using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>Authors the probe's textures, sprites, Sprite Atlas V1 assets and scene (Sprite Packer
/// mode "Sprite Atlas V1 - Always Enabled"), and records it in Play mode.</summary>
public static class SpriteAtlasSetup
{
    // name, width, height, shape (0 rect, 1 ellipse, 2 triangle, 3 ring, 4 inset), mesh (0 Full Rect, 1 Tight)
    static readonly (string n, int w, int h, int shape, int mesh)[] Shapes = {
        ("Big", 100, 60, 1, 1), ("Wide", 90, 20, 0, 0), ("Tall", 18, 70, 2, 1), ("Small", 12, 12, 0, 0),
        ("Ring", 48, 48, 3, 1), ("Tri", 40, 30, 2, 1), ("Odd", 33, 17, 1, 0), ("Tiny", 5, 7, 0, 1),
        ("Inset", 64, 40, 4, 1),
    };

    /// <summary>Each atlas set: its folder of textures (a copy of the shapes) and settings.</summary>
    static readonly (string name, int padding, bool rotate, bool tight, int max, bool dilate, bool mips, bool compressed, int blockOffset, bool compressedSources)[] Sets = {
        ("Tight", 2, true, true, 256, false, false, false, 1, false),
        ("Paged", 4, false, false, 128, false, false, false, 1, false),
        ("FullRect", 2, true, true, 256, false, false, false, 1, false),
        ("Sources", 2, false, false, 256, false, false, false, 1, true),
        ("Dilated", 8, true, true, 256, true, true, true, 1, false),
        ("Blocks", 2, true, true, 256, false, false, false, 2, false),
        ("Late", 2, true, true, 256, false, false, false, 1, false),
    };

    static Color32 Pixel(int x, int y, int w, int h, int shape, int seed)
    {
        float u = (x + .5f) / w * 2 - 1, v = (y + .5f) / h * 2 - 1;
        bool inside = shape switch
        {
            1 => u * u + v * v <= 1,
            2 => v >= -1 && Math.Abs(u) <= (1 - v) * .5f,
            3 => u * u + v * v <= 1 && u * u + v * v >= .3f,
            4 => x >= 9 && x < w - 5 && y >= 4 && y < h - 11 && (x + y) % 7 != 0,
            _ => true,
        };
        if (!inside) return new Color32(0, 0, 0, 0);
        return new Color32((byte)(x * 7 + seed * 31), (byte)(y * 11 + seed * 17), (byte)((x ^ y) * 5 + seed), 255);
    }

    static void WriteTexture(string path, int w, int h, Func<int, int, Color32> f)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
        t.SetPixels32(px);
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
    }

    static TextureImporter SpriteImporter(string path, SpriteImportMode mode, int mesh, bool compressed)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = mode;
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.textureCompression = compressed ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
        var st = new TextureImporterSettings();
        imp.ReadTextureSettings(st);
        st.spriteMeshType = (SpriteMeshType)mesh;
        st.spriteExtrude = 1;
        imp.SetTextureSettings(st);
        return imp;
    }

    static SpriteAtlas Atlas(string name, int padding, bool rotate, bool tight, int max, bool dilate, bool mips, bool compressed, int blockOffset, FilterMode filter, UnityEngine.Object[] packables)
    {
        var a = new SpriteAtlas();
        a.SetPackingSettings(new SpriteAtlasPackingSettings { blockOffset = blockOffset, padding = padding, enableRotation = rotate, enableTightPacking = tight, enableAlphaDilation = dilate });
        a.SetTextureSettings(new SpriteAtlasTextureSettings { readable = true, generateMipMaps = mips, sRGB = true, filterMode = filter });
        var ps = a.GetPlatformSettings("DefaultTexturePlatform");
        ps.maxTextureSize = max;
        ps.textureCompression = compressed ? TextureImporterCompression.Compressed : TextureImporterCompression.Uncompressed;
        a.SetPlatformSettings(ps);
        AssetDatabase.CreateAsset(a, $"Assets/Atlases/{name}.spriteatlas");
        if (packables.Length > 0) a.Add(packables);
        return a;
    }

    /// <summary>A Sprite Atlas V2 asset (`.spriteatlasv2`) with its importer's settings.</summary>
    static SpriteAtlas AtlasV2(string name, int padding, bool rotate, bool tight, int max, bool includeInBuild, UnityEngine.Object[] packables)
    {
        var path = $"Assets/Atlases/{name}.spriteatlasv2";
        var asset = new SpriteAtlasAsset();
        asset.Add(packables);
        SpriteAtlasAsset.Save(asset, path);
        AssetDatabase.ImportAsset(path);
        var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(path);
        importer.packingSettings = new SpriteAtlasPackingSettings { blockOffset = 1, padding = padding, enableRotation = rotate, enableTightPacking = tight, enableAlphaDilation = false };
        importer.textureSettings = new SpriteAtlasTextureSettings { readable = true, generateMipMaps = false, sRGB = true, filterMode = FilterMode.Bilinear };
        var ps = importer.GetPlatformSettings("DefaultTexturePlatform");
        ps.maxTextureSize = max;
        ps.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformSettings(ps);
        importer.includeInBuild = includeInBuild;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
    }

    /// <summary>The set to author: `v1` (Sprite Atlas V1) or `v2` (Sprite Atlas V2).</summary>
    static string Set => Environment.GetEnvironmentVariable("SPRITE_ATLAS_SET") ?? "v1";

    public static void Author()
    {
        if (Set == "v2")
        {
            AuthorV2();
            return;
        }
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            Directory.CreateDirectory("Assets/Atlases");
            int seed = 0;
            foreach (var set in Sets)
            {
                Directory.CreateDirectory($"Assets/{set.name}");
                foreach (var s in Shapes)
                {
                    int sd = ++seed;
                    WriteTexture($"Assets/{set.name}/{set.name}{s.n}.png", s.w, s.h, (x, y) => Pixel(x, y, s.w, s.h, s.shape, sd));
                }
            }
            // Two 64 x 64 sheets of four 32 x 32 cells each (equal areas across two textures), a
            // folder of textures and two single sprites as packables, and a 9-sliced sprite.
            Directory.CreateDirectory("Assets/Sheets");
            foreach (var sheet in new[] { "SheetA", "SheetB" })
            {
                int sd = ++seed;
                WriteTexture($"Assets/Sheets/{sheet}.png", 64, 64, (x, y) =>
                {
                    int q = (x / 32) + 2 * (y / 32);
                    return Pixel(x % 32, y % 32, 32, 32, q % 4, sd + q);
                });
            }
            Directory.CreateDirectory("Assets/Folder/Nested");
            foreach (var (path, w, h, shape) in new[] { ("Assets/Folder/FolderBig.png", 70, 50, 1), ("Assets/Folder/Nested/FolderTri.png", 30, 44, 2) })
            {
                int sd = ++seed;
                WriteTexture(path, w, h, (x, y) => Pixel(x, y, w, h, shape, sd));
            }
            Directory.CreateDirectory("Assets/Singles");
            foreach (var (path, w, h, shape) in new[] { ("Assets/Singles/PickedRing.png", 40, 40, 3), ("Assets/Singles/PickedOdd.png", 25, 31, 1), ("Assets/Singles/Unpicked.png", 20, 20, 0) })
            {
                int sd = ++seed;
                WriteTexture(path, w, h, (x, y) => Pixel(x, y, w, h, shape, sd));
            }
            WriteTexture("Assets/Singles/Sliced.png", 48, 32, (x, y) => Pixel(x, y, 48, 32, 0, 99));
            // In no atlas: a Tight sprite with a transparent margin (a trimmed texture rect).
            WriteTexture("Assets/Singles/Loose.png", 64, 40, (x, y) => Pixel(x, y, 64, 40, 4, 77));
            AssetDatabase.Refresh();
            foreach (var set in Sets)
                foreach (var s in Shapes)
                {
                    var imp = SpriteImporter($"Assets/{set.name}/{set.name}{s.n}.png", SpriteImportMode.Single,
                        set.name == "FullRect" ? 0 : s.mesh, set.compressedSources);
                    if (s.n == "Inset")
                    {
                        // An off-centre pivot and a transparent margin: the Tight texture rect is
                        // smaller than the rect.
                        var st = new TextureImporterSettings();
                        imp.ReadTextureSettings(st);
                        st.spriteAlignment = (int)SpriteAlignment.Custom;
                        st.spritePivot = new Vector2(.3f, .8f);
                        st.spritePixelsPerUnit = 64;
                        imp.SetTextureSettings(st);
                    }
                    imp.SaveAndReimport();
                }
            foreach (var sheet in new[] { "SheetA", "SheetB" })
            {
                var imp = SpriteImporter($"Assets/Sheets/{sheet}.png", SpriteImportMode.Multiple, 1, false);
#pragma warning disable 618
                imp.spritesheet = Enumerable.Range(0, 4).Select(q => new SpriteMetaData { name = sheet + "Cell" + q, rect = new Rect((q % 2) * 32, (q / 2) * 32, 32, 32), pivot = new Vector2(.5f, .5f), alignment = 0 }).ToArray();
#pragma warning restore 618
                imp.SaveAndReimport();
            }
            foreach (var path in new[] { "Assets/Folder/FolderBig.png", "Assets/Folder/Nested/FolderTri.png", "Assets/Singles/PickedRing.png", "Assets/Singles/PickedOdd.png", "Assets/Singles/Unpicked.png", "Assets/Singles/Loose.png" })
                SpriteImporter(path, SpriteImportMode.Single, 1, false).SaveAndReimport();
            {
                var imp = SpriteImporter("Assets/Singles/Sliced.png", SpriteImportMode.Single, 0, false);
                imp.spriteBorder = new Vector4(6, 5, 9, 7);
                imp.SaveAndReimport();
            }
            Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var atlases = new List<SpriteAtlas>();
            foreach (var set in Sets)
                atlases.Add(Atlas(set.name, set.padding, set.rotate, set.tight, set.max, set.dilate, set.mips, set.compressed, set.blockOffset, FilterMode.Bilinear,
                    Shapes.Select(s => (UnityEngine.Object)Tex($"Assets/{set.name}/{set.name}{s.n}.png")).ToArray()));
            atlases.Add(Atlas("Sheets", 2, true, true, 128, false, false, false, 1, FilterMode.Bilinear,
                new UnityEngine.Object[] { Tex("Assets/Sheets/SheetA.png"), Tex("Assets/Sheets/SheetB.png") }));
            atlases.Add(Atlas("Folder", 2, false, false, 256, false, false, false, 1, FilterMode.Point,
                new[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Folder") }));
            atlases.Add(Atlas("Picked", 2, false, false, 128, false, false, false, 1, FilterMode.Bilinear,
                new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Singles/PickedRing.png"), AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Singles/PickedOdd.png"),
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Singles/Sliced.png") }));
            var paged = atlases.First(a => a.name == "Paged");
            var variant = Atlas("PagedHalf", 4, false, false, 128, false, false, false, 1, FilterMode.Bilinear, new UnityEngine.Object[0]);
            variant.SetIsVariant(true);
            variant.SetMasterAtlas(paged);
            variant.SetVariantScale(0.5f);
            atlases.Add(variant);
            var late = atlases.First(a => a.name == "Late");
            late.SetIncludeInBuild(false);
            EditorSettings.spritePackerMode = SpritePackerMode.AlwaysOnAtlas;
            AssetDatabase.SaveAssets();

            // Drawn: a flipped and a tightly packed sprite, a Full Rect sprite on a second page, a
            // folder sprite on a point-filtered page and a late-binding sprite.
            Scene(atlases, late, new[] { "SheetACell2", "TightInset", "PagedTiny", "FolderBig", "LateRing", "TightBig" });
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    /// <summary>Sprite Atlas V2: three atlases (tight with rotation, two pages, late binding) of
    /// the shapes, packer mode "Sprite Atlas V2 - Enabled".</summary>
    static void AuthorV2()
    {
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
            Directory.CreateDirectory("Assets/Atlases");
            var sets = new[] { ("Tight", 2, true, true, 256, true), ("Paged", 4, false, false, 128, true), ("Late", 2, true, true, 256, false) };
            int seed = 200;
            foreach (var (name, _, _, _, _, _) in sets)
            {
                Directory.CreateDirectory($"Assets/{name}");
                foreach (var s in Shapes)
                {
                    int sd = ++seed;
                    WriteTexture($"Assets/{name}/{name}{s.n}.png", s.w, s.h, (x, y) => Pixel(x, y, s.w, s.h, s.shape, sd));
                }
            }
            AssetDatabase.Refresh();
            foreach (var (name, _, _, _, _, _) in sets)
                foreach (var s in Shapes)
                {
                    var imp = SpriteImporter($"Assets/{name}/{name}{s.n}.png", SpriteImportMode.Single, s.mesh, false);
                    if (s.n == "Inset")
                    {
                        var st = new TextureImporterSettings();
                        imp.ReadTextureSettings(st);
                        st.spriteAlignment = (int)SpriteAlignment.Custom;
                        st.spritePivot = new Vector2(.3f, .8f);
                        st.spritePixelsPerUnit = 64;
                        imp.SetTextureSettings(st);
                    }
                    imp.SaveAndReimport();
                }
            var atlases = new List<SpriteAtlas>();
            foreach (var (name, padding, rotate, tight, max, include) in sets)
                atlases.Add(AtlasV2(name, padding, rotate, tight, max, include,
                    Shapes.Select(s => (UnityEngine.Object)AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/{name}/{name}{s.n}.png")).ToArray()));
            AssetDatabase.SaveAssets();
            Scene(atlases, atlases.Last(), new[] { "TightInset", "PagedTiny", "LateRing", "TightBig" });
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    /// <summary>The probe scene: the probe with every sprite of the project and the atlases, a
    /// camera and renderers of the `drawn` sprites.</summary>
    static void Scene(List<SpriteAtlas> atlases, SpriteAtlas late, string[] drawn)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("Probe").AddComponent<SpriteAtlasProbe>();
        var sprites = new List<Sprite>();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }).OrderBy(AssetDatabase.GUIDToAssetPath, StringComparer.Ordinal))
            sprites.AddRange(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal));
        probe.sprites = sprites.ToArray();
        probe.atlases = atlases.ToArray();
        probe.lateAtlas = late;
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 1.2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(12, 18, 27, 255);
        camera.allowHDR = false;
        camera.allowMSAA = false;
        for (int i = 0; i < drawn.Length; i++)
        {
            var renderer = new GameObject(drawn[i] + "Renderer").AddComponent<SpriteRenderer>();
            renderer.sprite = sprites.First(s => s.name == drawn[i]);
            renderer.transform.position = new Vector3(-1.5f + 1.0f * (i % 4), i < 4 ? .5f : -.6f, 0);
        }
        EditorSceneManager.SaveScene(scene, "Assets/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Probe.unity", true) };
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        QualitySettings.antiAliasing = 0;
        AssetDatabase.SaveAssets();
    }

    /// <summary>Opens the scene and enters Play mode; the probe writes its capture there and exits.</summary>
    public static void Record()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Probe.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
