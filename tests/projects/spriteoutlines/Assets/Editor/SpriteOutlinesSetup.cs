using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors the probe's textures (readable, unreadable, without a generated physics shape,
/// Full Rect) and scene, and records it in Play mode.</summary>
public static class SpriteOutlinesSetup
{
    static Color32 Pixel(int x, int y, int w, int h, int shape)
    {
        float u = (x + .5f) / w * 2 - 1, v = (y + .5f) / h * 2 - 1;
        bool inside = shape switch
        {
            1 => u * u + v * v <= 1,
            2 => v >= -1 && Math.Abs(u) <= (1 - v) * .5f,
            3 => u * u + v * v <= 1 && u * u + v * v >= .3f,
            _ => true,
        };
        return inside ? new Color32((byte)(x * 7), (byte)(y * 11), 90, 255) : new Color32(0, 0, 0, 0);
    }

    static void Write(string path, int w, int h, int shape)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = Pixel(x, y, w, h, shape);
        t.SetPixels32(px);
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
    }

    public static void Author()
    {
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            Directory.CreateDirectory("Assets/Textures");
            foreach (var (n, w, h, s) in new[] { ("Ellipse", 64, 40, 1), ("Tri", 48, 48, 2), ("Ring", 64, 64, 3) })
                foreach (var (suffix, readable, physics, mesh) in new[] { ("R", true, true, 1), ("N", false, true, 1), ("NoPhys", true, false, 1), ("NoPhysFull", true, false, 0) })
                {
                    var path = $"Assets/Textures/{n}{suffix}.png";
                    Write(path, w, h, s);
                    AssetDatabase.ImportAsset(path);
                    var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.isReadable = readable;
                    imp.mipmapEnabled = false;
                    imp.textureCompression = TextureImporterCompression.Uncompressed;
                    var st = new TextureImporterSettings();
                    imp.ReadTextureSettings(st);
                    st.spriteGenerateFallbackPhysicsShape = physics;
                    st.spriteMeshType = (SpriteMeshType)mesh;
                    imp.SetTextureSettings(st);
                    imp.SaveAndReimport();
                }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var probe = new GameObject("Probe").AddComponent<SpriteOutlinesProbe>();
            probe.textures = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures" }).Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal).Select(AssetDatabase.LoadAssetAtPath<Texture2D>).ToArray();
            probe.sprites = probe.textures.Select(t => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(t)).OfType<Sprite>().First()).ToArray();
            EditorSceneManager.SaveScene(scene, "Assets/Probe.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Probe.unity", true) };
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
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
