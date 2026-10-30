using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>`Unity -batchmode -projectPath tests/projects/resources -executeMethod ProbeRunner.Record`
/// with <c>RESOURCES_PROBE_OUT</c> set: the probe scene in Play mode, where <c>ResProbe</c>
/// records and exits the Editor. <see cref="Create"/> made the scene and the assets.</summary>
public static class ProbeRunner
{
    static void Png(string path, int width, int height, Func<int, int, Color> pixel)
    {
        var t = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                t.SetPixel(x, y, pixel(x, y));
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
    }

    /// <summary>A point-filtered, uncompressed sprite without mips.</summary>
    static TextureImporter Sprite(string path, int width, int height, Func<int, int, Color> pixel, float pixelsPerUnit,
        Vector4 border = default, SpriteMeshType mesh = SpriteMeshType.FullRect, Action<TextureImporter> more = null)
    {
        Png(path, width, height, pixel);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = mesh;
        settings.spriteBorder = border;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        more?.Invoke(importer);
        importer.SaveAndReimport();
        return importer;
    }

    static TextureImporter Plain(string path, int width, int height, Func<int, int, Color> pixel, Action<TextureImporter> settings)
    {
        Png(path, width, height, pixel);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        settings(importer);
        importer.SaveAndReimport();
        return importer;
    }

    static Color Texel(int x, int y, bool border) => new Color32((byte)(16 + 18 * x), (byte)(16 + 20 * y), (byte)(border ? 64 : 200), 255);

    static Color Hue(int i) => Color.HSVToRGB((i % 12) / 12f, 0.8f, 1f);

    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Sprites: a white square, a circle, a soft disc (alpha falls off from the centre), a bar,
        // a bordered tile whose pixels are all different, the same without borders.
        Sprite("Assets/Resources/Square.png", 16, 16, (x, y) => Color.white, 16);
        Sprite("Assets/Resources/Circle.png", 32, 32, (x, y) =>
            Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) < 15f ? Color.white : new Color(1f, 1f, 1f, 0f), 16,
            mesh: SpriteMeshType.Tight);
        Sprite("Assets/Resources/Soft.png", 32, 32, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(16f, 16f)) / 16f;
            return new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d));
        }, 16);
        Sprite("Assets/Resources/Bar.png", 64, 8, (x, y) => Color.white, 16);
        // Tile sprites: each texel's red is its column and green its row (16 + 18x, 16 + 20y), blue
        // 64 on the border and 200 inside, so an image shows which texel lands where.
        Sprite("Assets/Resources/Tile.png", 12, 10, (x, y) => Texel(x, y, x < 3 || x >= 10 || y < 2 || y >= 8), 8, new Vector4(3, 2, 2, 2));
        Sprite("Assets/Resources/TileNoBorder.png", 6, 5, (x, y) => Texel(x, y, false), 8);
        Sprite("Assets/Resources/TileTight.png", 12, 10, (x, y) => (x + y) % 3 == 0 ? new Color(0, 0, 0, 0) : Texel(x, y, x < 3 || x >= 10 || y < 2 || y >= 8), 8,
            new Vector4(3, 2, 2, 2), SpriteMeshType.Tight);

        // Secondary textures: a lit sprite with a normal map and a mask.
        Plain("Assets/Resources/LitNormal.png", 16, 16, (x, y) => new Color(x / 15f, y / 15f, 1f, 1f), i =>
        {
            i.textureType = TextureImporterType.NormalMap;
            i.filterMode = FilterMode.Point;
            i.mipmapEnabled = false;
        });
        Plain("Assets/Resources/LitMask.png", 16, 16, (x, y) => new Color(x < 8 ? 1f : 0f, y < 8 ? 1f : 0f, 0.5f, 1f), i =>
        {
            i.filterMode = FilterMode.Point;
            i.mipmapEnabled = false;
        });
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/LitNormal.png");
        var mask = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/LitMask.png");
        Sprite("Assets/Resources/Lit.png", 16, 16, (x, y) => new Color(0.8f, 0.8f, 0.8f, 1f), 16, more: i =>
        {
            i.secondarySpriteTextures = new[]
            {
                new SecondarySpriteTexture { name = "_NormalMap", texture = normal },
                new SecondarySpriteTexture { name = "_MaskTex", texture = mask },
            };
        });

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void CreateMipAssets()
    {
        Plain("Assets/Resources/MipImport.png", 8, 4, (x, y) => new Color(x / 7f, y / 3f, 0.25f, 1f), i =>
        {
            i.sRGBTexture = false;
            i.isReadable = true;
            i.mipmapEnabled = true;
            i.filterMode = FilterMode.Point;
            i.npotScale = TextureImporterNPOTScale.None;
        });
        const string path = "Assets/Resources/MipAsset.asset";
        var t = new Texture2D(8, 4, TextureFormat.RGBA32, true, true) { name = "MipAsset" };
        for (int mip = 0; mip < t.mipmapCount; mip++)
        {
            var pixels = new Color[Math.Max(1, 8 >> mip) * Math.Max(1, 4 >> mip)];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(mip * 0.25f, i * 0.03f, 0.5f, 1f);
            t.SetPixels(pixels, mip);
        }
        t.Apply(false);
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(t, path);
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
