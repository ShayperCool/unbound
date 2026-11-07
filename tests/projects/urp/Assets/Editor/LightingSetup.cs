using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>`Unity -batchmode -projectPath tests/projects/urp -executeMethod LightingSetup.Create`:
/// authors the light cookie and cubemap textures under <c>Assets/Lighting</c> and three scenes
/// baked with the CPU lightmapper (no denoising, filtering or compression):
/// <c>Reflections</c> (a room captured by a baked box-projected reflection probe beside a custom
/// one, mirror spheres in and between them), <c>Shadowmask</c> (directional lightmaps, a mixed
/// sun in Shadowmask mode, a baked point light, light probes) and <c>Subtractive</c> (the same
/// content non-directional in Subtractive mode). Adds them to the build after Probe.</summary>
public static class LightingSetup
{
    const string Dir = "Assets/Lighting";

    public static void Create()
    {
        Directory.CreateDirectory(Dir);
        System.Func<int, int, Color> pattern = (x, y) =>
        {
            bool cell = ((x / 8) + (y / 8)) % 2 == 0;
            float dx = x - 31.5f, dy = y - 31.5f;
            bool ring = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 20f) < 3f;
            float v = ring ? 0.1f : cell ? 1f : 0.35f;
            return new Color(v, v, v, 1f);
        };
        var cookieGray = WriteTexture("CookieGray", 64, 64, pattern, importer => DirectionalCookie(importer, TextureImporterAlphaSource.FromGrayScale));
        var cookieOpaque = WriteTexture("CookieOpaque", 64, 64, pattern, importer => DirectionalCookie(importer, TextureImporterAlphaSource.FromInput));

        var cookieColor = WriteTexture("CookieColor", 64, 64, (x, y) =>
        {
            // Colored quadrants inside a soft black border (a spot cookie's edge).
            float edge = Mathf.Min(Mathf.Min(x, 63 - x), Mathf.Min(y, 63 - y));
            float fade = Mathf.Clamp01((edge - 2f) / 6f);
            var c = x < 32 ? (y < 32 ? new Color(1f, 0.2f, 0.2f) : new Color(0.2f, 1f, 0.2f))
                           : (y < 32 ? new Color(0.3f, 0.4f, 1f) : new Color(1f, 1f, 1f));
            if ((x / 4) % 4 == 0) c *= 0.3f;
            return new Color(c.r * fade, c.g * fade, c.b * fade, 1f);
        }, importer =>
        {
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
        });
        Color[] faces = { new Color(1f, 0.3f, 0.2f), new Color(0.2f, 1f, 0.3f), new Color(0.3f, 0.4f, 1f),
                          new Color(1f, 1f, 0.3f), new Color(1f, 0.3f, 1f), new Color(0.3f, 1f, 1f) };
        var cookieCube = WriteTexture("CookieCube", 6 * 32, 32, (x, y) =>
        {
            int face = x / 32, u = x % 32;
            bool bar = Mathf.Abs(u - 15.5f) < 3f || Mathf.Abs(y - 15.5f) < 3f;
            return bar ? Color.white * 0.15f + new Color(0f, 0f, 0f, 1f) : faces[face];
        }, importer =>
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
        });
        var customCube = WriteTexture("CustomReflection", 6 * 64, 64, (x, y) =>
        {
            int face = x / 64, u = x % 64;
            bool stripe = ((u / 8) + (y / 8)) % 2 == 0;
            return stripe ? faces[face] : faces[(face + 3) % 6] * 0.5f + new Color(0f, 0f, 0f, 0.5f);
        }, importer =>
        {
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.TextureCube;
            importer.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
        });
        // Loaded by the probe script (Resources).
        var holder = ScriptableObject.CreateInstance<LightingTextures>();
        holder.cookieGray = cookieGray;
        holder.cookieOpaque = cookieOpaque;
        holder.cookieColor = cookieColor;
        holder.cookieCube = (Cubemap)cookieCube;
        Directory.CreateDirectory("Assets/Resources");
        AssetDatabase.CreateAsset(holder, "Assets/Resources/LightingTextures.asset");
        _ = customCube;

        CreateReflections((Cubemap)AssetDatabase.LoadAssetAtPath<Texture>($"{Dir}/CustomReflection.png"));
        CreateBaked("Shadowmask", MixedLightingMode.Shadowmask, LightmapsMode.CombinedDirectional);
        CreateBaked("Subtractive", MixedLightingMode.Subtractive, LightmapsMode.NonDirectional);

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var name in new[] { "Reflections", "Shadowmask", "Subtractive" })
            scenes.Add(new EditorBuildSettingsScene($"Assets/Scenes/{name}.unity", true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    /// <summary>A repeating Cookie texture for a directional light, its alpha from
    /// <paramref name="alpha"/>.</summary>
    static void DirectionalCookie(TextureImporter importer, TextureImporterAlphaSource alpha)
    {
        importer.textureType = TextureImporterType.Cookie;
        importer.alphaSource = alpha;
        importer.wrapMode = TextureWrapMode.Repeat;
        // The Light Type a Cookie texture is imported for (1: Directional) has no public setter.
        var serialized = new SerializedObject(importer);
        var lightType = serialized.FindProperty("m_CookieLightType");
        if (lightType == null) throw new System.InvalidOperationException("TextureImporter has no m_CookieLightType");
        lightType.intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static Texture WriteTexture(string name, int width, int height, System.Func<int, int, Color> pixel, System.Action<TextureImporter> configure)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                texture.SetPixel(x, y, pixel(x, y));
        texture.Apply();
        string path = $"{Dir}/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        configure(importer);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }

    static Material Mat(string scene, string name, string shader, Color color, float smoothness = 0.3f, float metallic = 0f)
    {
        var m = new Material(Shader.Find(shader));
        m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(m, $"{Dir}/{scene}{name}.mat");
        return m;
    }

    static GameObject Prim(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, StaticEditorFlags flags)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (flags != 0) GameObjectUtility.SetStaticEditorFlags(go, flags);
        return go;
    }

    static Camera MakeCamera(Vector3 position, float pitch)
    {
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        camera.tag = "MainCamera";
        camera.transform.position = position;
        camera.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
        var cam = camera.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.2f, 0.3f, 0.5f, 1f);
        return cam;
    }

    static LightingSettings Settings(string name, MixedLightingMode mixed, LightmapsMode directionality)
    {
        var settings = new LightingSettings
        {
            name = name,
            bakedGI = true,
            realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = 8f,
            lightmapPadding = 2,
            lightmapMaxSize = 256,
            directionalityMode = directionality,
            lightmapCompression = LightmapCompression.None,
            ao = false,
            directSampleCount = 32,
            indirectSampleCount = 128,
            environmentSampleCount = 64,
            maxBounces = 2,
            filteringMode = LightingSettings.FilterMode.None,
            denoiserTypeDirect = LightingSettings.DenoiserType.None,
            denoiserTypeIndirect = LightingSettings.DenoiserType.None,
            denoiserTypeAO = LightingSettings.DenoiserType.None,
            mixedBakeMode = mixed,
        };
        AssetDatabase.CreateAsset(settings, $"Assets/Scenes/{name}.lighting");
        return settings;
    }

    static void Bake(UnityEngine.SceneManagement.Scene scene, string name)
    {
        EditorSceneManager.SaveScene(scene, $"Assets/Scenes/{name}.unity");
        // The CPU lightmapper's process occasionally crashes; one more try.
        if (!Lightmapping.Bake() && !Lightmapping.Bake()) throw new System.InvalidOperationException("baking " + name + " failed");
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>A room (floor and three coloured walls, Reflection Probe Static) seen by a baked
    /// box-projected probe on the left and a custom-cubemap probe (intensity 2, importance 2) on
    /// the right, overlapping in the middle; mirror spheres in each, in the overlap, outside both
    /// (the skybox's reflection) and with reflectionProbeUsage Off and Simple.</summary>
    static void CreateReflections(Cubemap custom)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        MakeCamera(new Vector3(0f, 1.6f, -5.5f), 10f);
        var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        sun.type = LightType.Directional;
        sun.intensity = 1f;
        sun.lightmapBakeType = LightmapBakeType.Realtime;

        const StaticEditorFlags captured = StaticEditorFlags.ReflectionProbeStatic;
        Prim(PrimitiveType.Plane, "Floor", Vector3.zero, Vector3.one * 0.8f, Mat("Reflections", "Floor", "Universal Render Pipeline/Lit", new Color(0.6f, 0.6f, 0.6f)), captured);
        Prim(PrimitiveType.Cube, "Back Wall", new Vector3(0f, 1.5f, 2.5f), new Vector3(8f, 3f, 0.2f), Mat("Reflections", "Back", "Universal Render Pipeline/Lit", new Color(0.9f, 0.2f, 0.15f)), captured);
        Prim(PrimitiveType.Cube, "Left Wall", new Vector3(-3.5f, 1.5f, 0f), new Vector3(0.2f, 3f, 5f), Mat("Reflections", "Left", "Universal Render Pipeline/Lit", new Color(0.15f, 0.8f, 0.2f)), captured);
        Prim(PrimitiveType.Cube, "Pillar", new Vector3(-1.6f, 1f, 1.4f), new Vector3(0.4f, 2f, 0.4f), Mat("Reflections", "Pillar", "Universal Render Pipeline/Lit", new Color(1f, 0.85f, 0.2f)), captured);

        var baked = new GameObject("Baked Probe", typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();
        baked.transform.position = new Vector3(-1.5f, 1f, 0f);
        baked.mode = ReflectionProbeMode.Baked;
        baked.size = new Vector3(4f, 3f, 5f);
        baked.boxProjection = true;
        baked.blendDistance = 1f;
        baked.importance = 1;
        baked.resolution = 64;
        baked.hdr = true;
        baked.clearFlags = ReflectionProbeClearFlags.SolidColor;
        baked.backgroundColor = new Color(0.3f, 0.5f, 0.8f, 1f);
        GameObjectUtility.SetStaticEditorFlags(baked.gameObject, captured);

        var customProbe = new GameObject("Custom Probe", typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();
        customProbe.transform.position = new Vector3(1.5f, 1f, 0f);
        customProbe.mode = ReflectionProbeMode.Custom;
        customProbe.customBakedTexture = custom;
        customProbe.size = new Vector3(4f, 3f, 5f);
        customProbe.boxProjection = false;
        customProbe.blendDistance = 0.5f;
        customProbe.importance = 2;
        customProbe.intensity = 2f;

        var mirror = Mat("Reflections", "Mirror", "Universal Render Pipeline/Lit", Color.white, 0.92f, 1f);
        void Sphere(string name, Vector3 position, ReflectionProbeUsage usage)
        {
            var go = Prim(PrimitiveType.Sphere, name, position, Vector3.one * 0.8f, mirror, 0);
            go.GetComponent<MeshRenderer>().reflectionProbeUsage = usage;
        }
        Sphere("Baked Only", new Vector3(-2.2f, 0.6f, 0f), ReflectionProbeUsage.BlendProbes);
        Sphere("Overlap", new Vector3(0f, 0.6f, 0f), ReflectionProbeUsage.BlendProbes);
        Sphere("Custom Only", new Vector3(2.2f, 0.6f, 0f), ReflectionProbeUsage.BlendProbes);
        Sphere("Outside", new Vector3(0f, 1.9f, -3.2f), ReflectionProbeUsage.BlendProbesAndSkybox);
        Sphere("Off", new Vector3(-1.1f, 0.5f, -1.8f), ReflectionProbeUsage.Off);
        Sphere("Simple", new Vector3(0.6f, 0.5f, -1.4f), ReflectionProbeUsage.Simple);

        Lightmapping.lightingSettings = Settings("Reflections", MixedLightingMode.IndirectOnly, LightmapsMode.NonDirectional);
        Bake(scene, "Reflections");
    }

    /// <summary>Static ground, wall (Simple Lit) and block (Baked Lit) lit by a mixed sun and a baked
    /// point light; a light probe grid; dynamic Lit, Simple Lit and Baked Lit spheres and a dynamic
    /// cube casting a realtime shadow; a sphere using a Light Probe Proxy Volume.</summary>
    static void CreateBaked(string name, MixedLightingMode mixed, LightmapsMode directionality)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        MakeCamera(new Vector3(0f, 2.4f, -6f), 15f);
        var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(45f, -40f, 0f);
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Hard;
        sun.lightmapBakeType = LightmapBakeType.Mixed;

        var point = new GameObject("Point Light", typeof(Light)).GetComponent<Light>();
        point.transform.position = new Vector3(1.6f, 1.2f, -0.8f);
        point.type = LightType.Point;
        point.color = new Color(1f, 0.4f, 0.2f);
        point.intensity = 3f;
        point.range = 5f;
        point.shadows = LightShadows.Hard;
        point.lightmapBakeType = LightmapBakeType.Baked;

        const StaticEditorFlags lightmapped = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
        Prim(PrimitiveType.Plane, "Ground", Vector3.zero, Vector3.one, Mat(name, "Ground", "Universal Render Pipeline/Lit", new Color(0.7f, 0.7f, 0.7f)), lightmapped);
        Prim(PrimitiveType.Cube, "Wall", new Vector3(0.3f, 1f, 1.6f), new Vector3(3f, 2f, 0.3f), Mat(name, "Wall", "Universal Render Pipeline/Simple Lit", new Color(0.3f, 0.5f, 0.9f)), lightmapped);
        Prim(PrimitiveType.Cube, "Block", new Vector3(-1.6f, 0.5f, 0.2f), Vector3.one, Mat(name, "Block", "Universal Render Pipeline/Baked Lit", new Color(0.95f, 0.75f, 0.3f)), lightmapped);

        Prim(PrimitiveType.Sphere, "Lit Sphere", new Vector3(0.9f, 0.5f, -1.2f), Vector3.one * 0.8f, Mat(name, "LitSphere", "Universal Render Pipeline/Lit", Color.white, 0.4f), 0);
        Prim(PrimitiveType.Sphere, "Simple Lit Sphere", new Vector3(-0.4f, 0.5f, -1.6f), Vector3.one * 0.7f, Mat(name, "SimpleSphere", "Universal Render Pipeline/Simple Lit", new Color(0.9f, 0.9f, 0.6f)), 0);
        Prim(PrimitiveType.Sphere, "Baked Lit Sphere", new Vector3(2.3f, 0.5f, 0.2f), Vector3.one * 0.7f, Mat(name, "BakedSphere", "Universal Render Pipeline/Baked Lit", new Color(0.6f, 0.9f, 0.9f)), 0);
        Prim(PrimitiveType.Cube, "Dynamic Cube", new Vector3(-0.5f, 0.3f, -0.2f), Vector3.one * 0.6f, Mat(name, "DynamicCube", "Universal Render Pipeline/Lit", new Color(0.8f, 0.3f, 0.6f)), 0);

        var proxy = Prim(PrimitiveType.Sphere, "Proxy Sphere", new Vector3(-2.4f, 1.6f, -0.6f), Vector3.one * 0.7f, Mat(name, "ProxySphere", "Universal Render Pipeline/Lit", Color.white, 0.2f), 0);
        proxy.AddComponent<LightProbeProxyVolume>();
        proxy.GetComponent<MeshRenderer>().lightProbeUsage = LightProbeUsage.UseProxyVolume;

        var probes = new GameObject("Light Probe Group", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
        var positions = new List<Vector3>();
        // A jittered 3x2x3 grid: no five probes on one sphere, so the tetrahedralization is unique.
        int i = 0;
        for (int x = -3; x <= 3; x += 3)
            for (int y = 0; y <= 3; y += 3)
                for (int z = -3; z <= 3; z += 3, i++)
                    positions.Add(new Vector3(x + 0.07f * (i % 3), y + 0.1f + 0.05f * (i % 4), z + 0.06f * (i % 5)));
        probes.probePositions = positions.ToArray();

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.25f, 0.3f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.2f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.08f, 0.06f);
        if (mixed == MixedLightingMode.Subtractive) RenderSettings.subtractiveShadowColor = new Color(0.35f, 0.3f, 0.45f);

        Lightmapping.lightingSettings = Settings(name, mixed, directionality);
        Bake(scene, name);
    }
}
