using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Which project files a Play mode session writes when it changes texture assets and TextMeshPro's
/// dynamic font assets, and when the quit callbacks run. `Create` (run once, its outputs are
/// committed) makes the texture assets of Assets/Tex and the font assets of Assets/Fonts; `Record`
/// enters Play mode, changes each texture one way and adds characters to a dynamic font asset whose
/// atlas is empty, records which assets are dirty (`op.*`, `font.*`), leaves Play mode and quits the
/// Editor, recording the quit callbacks in their order (`quit.*`). run.py adds `exit.<path>` for
/// every file after the process ended.
/// </summary>
[InitializeOnLoad]
public static class TmpAtlasProbe
{
    const string Variable = "TMP_ATLAS_OUT";
    static int s_Order;

    /// <summary>The texture assets: a 4x4 RGBA32 texture each (one linear), changed one way in Play mode.</summary>
    static readonly string[] s_Textures =
    {
        "Untouched", "SetPixel", "Apply", "LoadRaw", "Filter", "Wrap", "Aniso", "MipBias", "Rename", "Reinit", "ReinitSame", "ReinitAlpha8",
        "ReinitRGB24", "ReinitRFloat", "ReinitR8", "ReinitUNorm", "ReinitSRGB", "LinearReinitRGBA32",
    };

    static TmpAtlasProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        Application.quitting += () => Quit("Application.quitting");
        EditorApplication.wantsToQuit += () => { Quit("EditorApplication.wantsToQuit"); return true; };
        EditorApplication.quitting += () => Quit("EditorApplication.quitting");
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Played();
            if (state == PlayModeStateChange.ExitingPlayMode) Quit("ExitingPlayMode");
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                Quit("EnteredEditMode");
                EditorApplication.Exit(0);
            }
        };
    }

    static void Put(string key, object value)
    {
        var text = value is bool b ? (b ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);
        File.AppendAllText(Environment.GetEnvironmentVariable(Variable),
            "{\"key\": \"" + key + "\", \"value\": \"" + text + "\"}\n");
    }

    static void Quit(string callback) => Put("quit." + (++s_Order) + "." + callback, "playing=" + EditorApplication.isPlaying);

    /// <summary>TMP_FontAsset.clearDynamicDataOnBuild (internal to TextMeshPro).</summary>
    static readonly PropertyInfo s_Clear = typeof(TMP_FontAsset).GetProperty("clearDynamicDataOnBuild",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    static bool Clears(TMP_FontAsset asset) => (bool)s_Clear.GetValue(asset);

    static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Tex/" + name + ".asset");
    static TMP_FontAsset Font(string name) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/" + name + ".asset");

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Tex");
        foreach (var name in s_Textures)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear: name.StartsWith("Linear", StringComparison.Ordinal));
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)(i * 16), 128, (byte)(255 - i * 16), 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, "Assets/Tex/" + name + ".asset");
        }
        // A material asset renamed in Play mode (whether a rename dirties other assets than textures).
        AssetDatabase.CreateAsset(new Material(Shader.Find("Unlit/Color")), "Assets/Tex/RenamedMaterial.mat");
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Probe.ttf");
        // A dynamic font asset with an empty atlas, and two saved with characters in their atlas
        // (made with clearing off; run.py turns it on in Filled's file).
        foreach (var name in new[] { "Dynamic SDF", "Filled SDF", "Kept SDF" })
        {
            var asset = TMP_FontAsset.CreateFontAsset(font, 36, 5, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
            asset.name = name;
            AssetDatabase.CreateAsset(asset, "Assets/Fonts/" + name + ".asset");
            asset.atlasTexture.name = name + " Atlas";
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            if (name != "Dynamic SDF")
            {
                s_Clear.SetValue(asset, false);
                asset.TryAddCharacters("a", out _);
            }
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(asset.atlasTexture);
        }
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Played()
    {
        foreach (var name in s_Textures) Put("load." + name + ".dirty", EditorUtility.IsDirty(Texture(name)));
        Texture("SetPixel").SetPixel(0, 0, Color.red);
        var apply = Texture("Apply");
        apply.SetPixel(0, 0, Color.red);
        apply.Apply();
        Texture("LoadRaw").LoadRawTextureData(new byte[64]);
        Texture("Filter").filterMode = FilterMode.Point;
        Texture("Wrap").wrapMode = TextureWrapMode.Clamp;
        Texture("Aniso").anisoLevel = 4;
        Texture("MipBias").mipMapBias = 0.5f;
        Texture("Rename").name = "Renamed";
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Tex/RenamedMaterial.mat");
        material.name = "Renamed";
        Put("op.RenamedMaterial.dirty", EditorUtility.IsDirty(material));
        Texture("Reinit").Reinitialize(8, 8);
        Texture("ReinitSame").Reinitialize(4, 4);
        Texture("ReinitAlpha8").Reinitialize(2, 2, TextureFormat.Alpha8, false);
        Texture("ReinitRGB24").Reinitialize(2, 2, TextureFormat.RGB24, false);
        Texture("ReinitRFloat").Reinitialize(2, 2, TextureFormat.RFloat, false);
        Texture("ReinitR8").Reinitialize(2, 2, TextureFormat.R8, false);
        Texture("ReinitUNorm").Reinitialize(2, 2, GraphicsFormat.R8G8B8A8_UNorm, false);
        Texture("ReinitSRGB").Reinitialize(2, 2, GraphicsFormat.R8G8B8A8_SRGB, false);
        Texture("LinearReinitRGBA32").Reinitialize(2, 2, TextureFormat.RGBA32, false);
        foreach (var name in s_Textures)
        {
            var texture = Texture(name);
            Put("op." + name + ".dirty", EditorUtility.IsDirty(texture));
            Put("op." + name + ".graphicsFormat", texture.graphicsFormat);
            Put("op." + name + ".isDataSRGB", texture.isDataSRGB);
        }
        var dynamic = Font("Dynamic SDF");
        Put("font.Dynamic.before", "width=" + dynamic.atlasTexture.width + " clear=" + Clears(dynamic));
        bool added = dynamic.TryAddCharacters("abc", out var missing);
        Put("font.Dynamic.added", added + " missing=" + missing);
        Put("font.Dynamic.after", "width=" + dynamic.atlasTexture.width + " atlas=" + EditorUtility.IsDirty(dynamic.atlasTexture)
            + " font=" + EditorUtility.IsDirty(dynamic) + " material=" + EditorUtility.IsDirty(dynamic.material));
        foreach (var name in new[] { "Filled SDF", "Kept SDF" })
        {
            var asset = Font(name);
            Put("font." + name.Split(' ')[0] + ".loaded", "width=" + asset.atlasTexture.width + " clear=" + Clears(asset)
                + " glyphs=" + asset.glyphTable.Count + " atlas=" + EditorUtility.IsDirty(asset.atlasTexture));
        }
        Put("complete", "1");
        EditorApplication.ExitPlaymode();
    }
}
