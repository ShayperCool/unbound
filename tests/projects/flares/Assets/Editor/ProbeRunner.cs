using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the flare probe's assets and scene (<see cref="Create"/>, batchmode) and records the probe
/// in a windowed Editor (an X server of its own, the GPU through VirtualGL): with
/// <c>FLARES_PROBE_OUT</c> set, the Editor closes its scene views (a scene view would render the
/// flares too), opens the probe scene, enters Play mode and quits when <see cref="FlareProbe"/>
/// has written its record (nonzero on failure). A watchdog quits with code 4 when Play mode does
/// not finish in time.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;
    const string Resources = "Assets/Resources/";

    static Texture2D WriteTexture(string name, int width, int height, Func<int, int, Color32> pixel)
    {
        var image = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = pixel(x, y);
        image.SetPixels32(pixels);
        image.Apply();
        string path = Resources + name + ".png";
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>One flare element: image, position, size, color and the rotate/zoom/fade/light
    /// color switches.</summary>
    public struct Element
    {
        public int image;
        public float position, size;
        public Color color;
        public bool lightColor, rotate, zoom, fade;
        public Element(int image, float position, float size, Color color, bool lightColor = false, bool rotate = false, bool zoom = false, bool fade = false)
        {
            this.image = image; this.position = position; this.size = size; this.color = color;
            this.lightColor = lightColor; this.rotate = rotate; this.zoom = zoom; this.fade = fade;
        }
    }

    static void WriteFlare(string name, Texture texture, int layout, bool useFog, params Element[] elements)
    {
        var flare = new Flare();
        AssetDatabase.CreateAsset(flare, Resources + name + ".flare");
        var so = new SerializedObject(flare);
        so.FindProperty("m_FlareTexture").objectReferenceValue = texture;
        so.FindProperty("m_TextureLayout").intValue = layout;
        so.FindProperty("m_UseFog").boolValue = useFog;
        var list = so.FindProperty("m_Elements");
        list.arraySize = elements.Length;
        for (int i = 0; i < elements.Length; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("m_ImageIndex").intValue = elements[i].image;
            e.FindPropertyRelative("m_Position").floatValue = elements[i].position;
            e.FindPropertyRelative("m_Size").floatValue = elements[i].size;
            e.FindPropertyRelative("m_Color").colorValue = elements[i].color;
            e.FindPropertyRelative("m_UseLightColor").boolValue = elements[i].lightColor;
            e.FindPropertyRelative("m_Rotate").boolValue = elements[i].rotate;
            e.FindPropertyRelative("m_Zoom").boolValue = elements[i].zoom;
            e.FindPropertyRelative("m_Fade").boolValue = elements[i].fade;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(flare);
    }

    static GameObject Inactive(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.SetActive(false);
        return go;
    }

    static void Set(UnityEngine.Object target, Action<SerializedObject> edit)
    {
        var so = new SerializedObject(target);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void HaloObject(string name, Vector3 position, float size, Color color)
    {
        var go = Inactive(name, position);
        var halo = go.AddComponent(typeof(Light).Assembly.GetType("UnityEngine.Halo"));
        Set(halo, so =>
        {
            so.FindProperty("m_Size").floatValue = size;
            so.FindProperty("m_Color").colorValue = color;
        });
    }

    static Light HaloLight(string name, Vector3 position, LightType type, float range, Color color, float intensity, bool halo)
    {
        var go = Inactive(name, position);
        var light = go.AddComponent<Light>();
        light.type = type;
        light.range = range;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        Set(light, so => so.FindProperty("m_DrawHalo").boolValue = halo);
        return light;
    }

    static void LensFlareObject(string name, Vector3 position, Flare flare, bool directional, int ignoreLayers)
    {
        var go = Inactive(name, position);
        var lens = go.AddComponent<LensFlare>();
        lens.flare = flare;
        Set(lens, so =>
        {
            so.FindProperty("m_Directional").boolValue = directional;
            so.FindProperty("m_IgnoreLayers").intValue = ignoreLayers;
        });
    }

    /// <summary>Image counts of the flare texture layouts (1 Large 4 Small, 1 Large 2 Medium 8 Small,
    /// 1 Texture, 2x2, 3x3, 4x4).</summary>
    public static readonly int[] LayoutImages = { 5, 11, 1, 4, 9, 16 };

    /// <summary>Makes the probe's textures, flares and scenes (batchmode, -quit).</summary>
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory(Resources);
        // Texture coordinates as colors (red u, green v, blue and alpha 1), read back exactly.
        var uv = WriteTexture("Uv", 256, 256, (x, y) => new Color32((byte)x, (byte)y, 255, 255));
        // A halo texture whose alpha rises along u.
        WriteTexture("HaloRamp", 64, 64, (x, y) => new Color32(255, 255, 255, (byte)(x * 4 + 2)));

        var white = Color.white;
        var tint = new Color(1f, 0.5f, 0.25f, 0.5f);
        foreach (var size in new[] { 5, 10, 20, 40 })
            WriteFlare("S" + size, uv, 2, true, new Element(0, 0f, size, white));
        WriteFlare("S20NoFog", uv, 2, false, new Element(0, 0f, 20f, white));
        WriteFlare("Pp05", uv, 2, true, new Element(0, 0.5f, 10f, white));
        WriteFlare("Pp1", uv, 2, true, new Element(0, 1f, 10f, white));
        WriteFlare("Pp2", uv, 2, true, new Element(0, 2f, 10f, white));
        WriteFlare("Pm05", uv, 2, true, new Element(0, -0.5f, 10f, white));
        WriteFlare("Rot20", uv, 2, true, new Element(0, 0f, 20f, white, rotate: true));
        WriteFlare("RotP05", uv, 2, true, new Element(0, 0.5f, 20f, white, rotate: true));
        WriteFlare("Zoom20", uv, 2, true, new Element(0, 0f, 20f, white, zoom: true));
        WriteFlare("Fade20", uv, 2, true, new Element(0, 0f, 20f, white, fade: true));
        WriteFlare("Light20", uv, 2, true, new Element(0, 0f, 20f, white, lightColor: true));
        WriteFlare("Color20", uv, 2, true, new Element(0, 0f, 20f, tint));
        for (int layout = 0; layout < LayoutImages.Length; layout++)
            for (int image = 0; image < LayoutImages[layout]; image++)
                WriteFlare("L" + layout + "_" + image, uv, layout, true, new Element(image, 0f, 40f, white));
        WriteFlare("L3_20", uv, 3, true, new Element(20, 0f, 40f, white));
        WriteFlare("Combo", uv, 1, true,
            new Element(0, 0f, 30f, white, lightColor: true),
            new Element(1, 0.4f, 12f, tint, rotate: true),
            new Element(3, 0.9f, 8f, new Color(0.3f, 0.6f, 1f, 1f), zoom: true),
            new Element(7, 1.4f, 6f, white, fade: true),
            new Element(2, -0.3f, 10f, new Color(0.5f, 1f, 0.5f, 1f), lightColor: true, rotate: true, zoom: true, fade: true));
        // Four separate elements along the line: plain, light color, fade, light color and fade.
        WriteFlare("Quad4", uv, 2, true,
            new Element(0, 0f, 8f, white),
            new Element(0, 0.6f, 8f, white, lightColor: true),
            new Element(0, 1.2f, 8f, white, fade: true),
            new Element(0, 1.8f, 8f, white, lightColor: true, fade: true));
        AssetDatabase.SaveAssets();
        var s20 = AssetDatabase.LoadAssetAtPath<Flare>(Resources + "S20.flare");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        HaloObject("HaloWhite", Vector3.zero, 1f, white);
        HaloObject("HaloColor", Vector3.zero, 0.7f, tint);
        HaloLight("HaloPoint", Vector3.zero, LightType.Point, 2f, new Color(1f, 0.5f, 0.25f), 1f, true);
        HaloLight("HaloSpot", Vector3.zero, LightType.Spot, 3f, white, 1f, true);
        HaloLight("HaloDirectional", Vector3.zero, LightType.Directional, 2f, white, 1f, true);
        LensFlareObject("Lens", Vector3.zero, s20, false, 0);
        LensFlareObject("LensIgnore", Vector3.zero, s20, false, 1 << 8);
        LensFlareObject("LensDirectional", Vector3.zero, s20, true, 0);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");

        // The scene halo texture (Lighting window, RenderSettings m_HaloTexture), set in the
        // scene file: there is no scripting API for it.
        var second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        HaloObject("HaloWhite", Vector3.zero, 1f, white);
        HaloLight("HaloPoint", Vector3.zero, LightType.Point, 2f, white, 1f, true);
        EditorSceneManager.SaveScene(second, "Assets/Scenes/HaloTexture.unity");
        string guid = AssetDatabase.AssetPathToGUID(Resources + "HaloRamp.png");
        string text = File.ReadAllText("Assets/Scenes/HaloTexture.unity");
        const string none = "m_HaloTexture: {fileID: 0}";
        if (!text.Contains(none)) throw new Exception("no m_HaloTexture in the scene");
        File.WriteAllText("Assets/Scenes/HaloTexture.unity", text.Replace(none, "m_HaloTexture: {fileID: 2800000, guid: " + guid + ", type: 3}"));
        AssetDatabase.ImportAsset("Assets/Scenes/HaloTexture.unity");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/HaloTexture.unity", true),
        };
        AssetDatabase.SaveAssets();
    }

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("FLARES_PROBE_OUT");
        // Unbound runs the probe by itself and quits through EditorApplication.Exit.
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("FlareProbe.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt("FlareProbe.code", 5));
            return;
        }
        if (EditorApplication.isPlaying)
        {
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup - start < WatchdogSeconds) return;
                File.WriteAllText(Path.Combine(output, "watchdog.txt"), "Play mode did not finish\n");
                EditorApplication.Exit(4);
            };
            FlareProbe.finished = code =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("FlareProbe.finished", true);
                SessionState.SetInt("FlareProbe.code", code);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(code);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("FlareProbe.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("FlareProbe.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            try
            {
                EditorSettings.asyncShaderCompilation = false;
                foreach (var view in new List<SceneView>(SceneView.sceneViews.Cast<SceneView>())) view.Close();
                EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                PlayModeWindow.SetCustomRenderingResolution(128, 128, "FlareProbe");
                PlayModeWindow.SetPlayModeFocused(true);
                Debug.Log("[flares] entering Play mode");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(6);
            }
        };
        EditorApplication.update += wait;
    }
}

static class SceneViewList
{
    public static IEnumerable<T> Cast<T>(this System.Collections.ArrayList list)
    {
        foreach (var item in list) yield return (T)item;
    }
}
