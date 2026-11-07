using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/lightingassets -executeMethod LightingAssetsSetup.Create`
/// makes the probe's assets:
/// <list type="bullet">
/// <item><c>Settings/Custom.lighting</c>: every LightingSettings property away from its default,
/// assigned to no scene.</item>
/// <item><c>Scenes/Plain.unity</c>: no LightingSettings, no lighting data; a holder object that
/// references the assets the probe reads.</item>
/// <item><c>Scenes/Lit.unity</c>: static geometry baked (CPU lightmapper, baked indirect,
/// non-directional, no compression) with a mixed directional light and a baked point light. After
/// the bake its <c>Settings/Lit.lighting</c> is changed without baking again (baked GI off,
/// subtractive, directional, compressed, realtime GI on), so the scene's settings disagree with its
/// lighting data.</item>
/// <item><c>Scenes/Realtime.unity</c>: Enlighten realtime GI precomputed for a box lit by a
/// realtime point light (<c>Settings/Realtime.lighting</c>); <c>Scenes/RealtimeOff.unity</c>: the
/// same box without global illumination.</item>
/// <item><c>Settings/Warmup.shadervariants</c> (built by the API) and
/// <c>Settings/Authored.shadervariants</c> (written as a file: a shader without variants, a
/// missing shader, keywords out of order, a duplicate); the latter is a preloaded collection of
/// the Graphics settings; <c>Dupes</c> and <c>Missing</c> (files too) list a variant twice and a
/// missing shader.</item>
/// </list>
/// </summary>
public static class LightingAssetsSetup
{
    const string Settings = "Assets/Settings";
    const string Scenes = "Assets/Scenes";

    public static void Create()
    {
        Directory.CreateDirectory(Settings);
        Directory.CreateDirectory(Scenes);
        AssetDatabase.Refresh();
        var variants = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Variants.shader");

        var custom = new LightingSettings
        {
            name = "Custom",
            bakedGI = false,
            realtimeGI = true,
            realtimeEnvironmentLighting = false,
            mixedBakeMode = MixedLightingMode.Subtractive,
            albedoBoost = 2.5f,
            indirectScale = 1.75f,
            lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
            lightmapMaxSize = 256,
            lightmapResolution = 12.5f,
            lightmapPadding = 5,
            lightmapCompression = LightmapCompression.LowQuality,
            ao = true,
            aoMaxDistance = 2.25f,
            aoExponentIndirect = 0.75f,
            aoExponentDirect = 0.5f,
            extractAO = true,
            directionalityMode = LightmapsMode.NonDirectional,
            indirectResolution = 0.5f,
            sampling = LightingSettings.Sampling.Fixed,
            directSampleCount = 48,
            indirectSampleCount = 300,
            maxBounces = 4,
            minBounces = 1,
            prioritizeView = false,
            filteringMode = LightingSettings.FilterMode.Advanced,
            denoiserTypeDirect = LightingSettings.DenoiserType.None,
            denoiserTypeIndirect = LightingSettings.DenoiserType.Optix,
            denoiserTypeAO = LightingSettings.DenoiserType.None,
            filterTypeDirect = LightingSettings.FilterType.ATrous,
            filterTypeIndirect = LightingSettings.FilterType.None,
            filterTypeAO = LightingSettings.FilterType.ATrous,
            filteringGaussianRadiusDirect = 2.5f,
            filteringGaussianRadiusIndirect = 3.5f,
            filteringGaussianRadiusAO = 1.5f,
            filteringAtrousPositionSigmaDirect = 0.25f,
            filteringAtrousPositionSigmaIndirect = 1.5f,
            filteringAtrousPositionSigmaAO = 0.75f,
            environmentImportanceSampling = false,
            environmentSampleCount = 200,
            lightProbeSampleCountMultiplier = 2f,
            respectSceneVisibilityWhenBakingGI = true,
        };
        AssetDatabase.CreateAsset(custom, Settings + "/Custom.lighting");

        CreateCollections(variants);
        CreatePlain(custom);
        CreateLit();
        CreateRealtime("Realtime", true);
        CreateRealtime("RealtimeOff", false);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(Scenes + "/Plain.unity", true),
            new EditorBuildSettingsScene(Scenes + "/Lit.unity", true),
            new EditorBuildSettingsScene(Scenes + "/Realtime.unity", true),
            new EditorBuildSettingsScene(Scenes + "/RealtimeOff.unity", true),
        };
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    static void CreateCollections(Shader variants)
    {
        var warmup = new ShaderVariantCollection { name = "Warmup" };
        warmup.Add(new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal));
        warmup.Add(new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "PLAIN_ON"));
        warmup.Add(new ShaderVariantCollection.ShaderVariant(variants, PassType.Normal, "RED_ON", "PLAIN_ON"));
        warmup.Add(new ShaderVariantCollection.ShaderVariant(variants, PassType.ForwardBase, "DIRECTIONAL", "RED_ON"));
        warmup.Add(new ShaderVariantCollection.ShaderVariant(variants, PassType.ShadowCaster, "SHADOWS_DEPTH"));
        warmup.Add(new ShaderVariantCollection.ShaderVariant(Shader.Find("Standard"), PassType.ForwardBase, "DIRECTIONAL"));
        warmup.Add(new ShaderVariantCollection.ShaderVariant(Shader.Find("Unlit/Color"), PassType.Normal));
        AssetDatabase.CreateAsset(warmup, Settings + "/Warmup.shadervariants");

        // As collections of real projects hold them: a shader with no variants, a missing shader,
        // keywords not in name order, a variant listed twice.
        string guid = AssetDatabase.AssetPathToGUID("Assets/Shaders/Variants.shader");
        File.WriteAllText(Settings + "/Authored.shadervariants",
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!200 &20000000\nShaderVariantCollection:\n" +
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
            "  m_PrefabAsset: {fileID: 0}\n  m_Name: Authored\n  m_Shaders:\n" +
            "  - first: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}\n    second:\n      variants: []\n" +
            "  - first: {fileID: 4800000, guid: " + guid + ", type: 3}\n    second:\n      variants:\n" +
            "      - keywords: RED_ON PLAIN_ON\n        passType: 0\n" +
            "      - keywords: \n        passType: 0\n" +
            "      - keywords: RED_ON PLAIN_ON\n        passType: 0\n" +
            "      - keywords: DIRECTIONAL BLUE_ON\n        passType: 4\n" +
            "  - first: {fileID: 4800000, guid: 0123456789abcdef0123456789abcdef, type: 3}\n    second:\n      variants:\n" +
            "      - keywords: \n        passType: 0\n");
        AssetDatabase.ImportAsset(Settings + "/Authored.shadervariants");
        // The same variant listed twice and in two keyword orders.
        File.WriteAllText(Settings + "/Dupes.shadervariants",
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!200 &20000000\nShaderVariantCollection:\n" +
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
            "  m_PrefabAsset: {fileID: 0}\n  m_Name: Dupes\n  m_Shaders:\n" +
            "  - first: {fileID: 4800000, guid: " + guid + ", type: 3}\n    second:\n      variants:\n" +
            "      - keywords: PLAIN_ON\n        passType: 0\n" +
            "      - keywords: PLAIN_ON\n        passType: 0\n" +
            "      - keywords: RED_ON PLAIN_ON\n        passType: 0\n" +
            "      - keywords: PLAIN_ON RED_ON\n        passType: 0\n");
        AssetDatabase.ImportAsset(Settings + "/Dupes.shadervariants");
        // A shader that does not exist next to one that does.
        File.WriteAllText(Settings + "/Missing.shadervariants",
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!200 &20000000\nShaderVariantCollection:\n" +
            "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
            "  m_PrefabAsset: {fileID: 0}\n  m_Name: Missing\n  m_Shaders:\n" +
            "  - first: {fileID: 4800000, guid: 0123456789abcdef0123456789abcdef, type: 3}\n    second:\n      variants:\n" +
            "      - keywords: \n        passType: 0\n      - keywords: A B\n        passType: 4\n" +
            "  - first: {fileID: 10755, guid: 0000000000000000f000000000000000, type: 0}\n    second:\n      variants:\n" +
            "      - keywords: \n        passType: 0\n");
        AssetDatabase.ImportAsset(Settings + "/Missing.shadervariants");

        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        var preloaded = graphics.FindProperty("m_PreloadedShaders");
        preloaded.arraySize = 1;
        preloaded.GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(Settings + "/Authored.shadervariants");
        graphics.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreatePlain(LightingSettings custom)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Camera(new Color(0.1f, 0.12f, 0.18f));
        var holder = new GameObject("Holder").AddComponent<LightingAssetsHolder>();
        holder.custom = custom;
        holder.warmup = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(Settings + "/Warmup.shadervariants");
        holder.authored = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(Settings + "/Authored.shadervariants");
        holder.dupes = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(Settings + "/Dupes.shadervariants");
        holder.missing = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(Settings + "/Missing.shadervariants");
        holder.variants = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/Variants.shader");
        EditorSceneManager.SaveScene(scene, Scenes + "/Plain.unity");
    }

    static void Camera(Color background)
    {
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 2.2f, -6f);
        camera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
        var cam = camera.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
    }

    static Material Mat(string name, Color color)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", 0.2f);
        AssetDatabase.CreateAsset(m, $"{Scenes}/{name}.mat");
        return m;
    }

    static GameObject Static(PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
        return go;
    }

    static LightingSettings Deterministic(string name)
    {
        var settings = new LightingSettings
        {
            name = name,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = 8f,
            lightmapPadding = 2,
            lightmapMaxSize = 512,
            directionalityMode = LightmapsMode.NonDirectional,
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
        };
        AssetDatabase.CreateAsset(settings, $"{Settings}/{name}.lighting");
        return settings;
    }

    static void CreateLit()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Camera(new Color(0.1f, 0.12f, 0.18f));
        var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(45f, -40f, 0f);
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Hard;
        sun.lightmapBakeType = LightmapBakeType.Mixed;
        var point = new GameObject("Point Light", typeof(Light)).GetComponent<Light>();
        point.transform.position = new Vector3(1.5f, 1.2f, -1f);
        point.type = LightType.Point;
        point.color = new Color(1f, 0.35f, 0.2f);
        point.intensity = 3f;
        point.range = 5f;
        point.lightmapBakeType = LightmapBakeType.Baked;
        Static(PrimitiveType.Plane, Vector3.zero, Vector3.one, Mat("Ground", new Color(0.7f, 0.7f, 0.7f)));
        Static(PrimitiveType.Cube, new Vector3(-1.3f, 0.5f, 0.2f), Vector3.one, Mat("Cube", new Color(0.2f, 0.4f, 0.9f)));
        Static(PrimitiveType.Cube, new Vector3(0.6f, 1f, 1.2f), new Vector3(0.5f, 2f, 0.5f), Mat("Column", new Color(0.9f, 0.85f, 0.6f)));
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.25f, 0.3f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.2f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.08f, 0.06f);

        var settings = Deterministic("Lit");
        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
        Lightmapping.lightingSettings = settings;
        EditorSceneManager.SaveScene(scene, Scenes + "/Lit.unity");
        if (!Lightmapping.Bake()) Debug.LogError("baking Lit failed");
        EditorSceneManager.SaveScene(scene);

        // The settings change after the bake; the lighting data stays the bake's.
        settings.bakedGI = false;
        settings.realtimeGI = true;
        settings.realtimeEnvironmentLighting = false;
        settings.mixedBakeMode = MixedLightingMode.Subtractive;
        settings.directionalityMode = LightmapsMode.CombinedDirectional;
        settings.lightmapCompression = LightmapCompression.HighQuality;
        settings.indirectScale = 4f;
        settings.albedoBoost = 3f;
        EditorUtility.SetDirty(settings);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    /// <summary>A box lit by a realtime point light, with Enlighten realtime GI precomputed
    /// (<paramref name="realtimeGI"/>) or with no global illumination (the control).</summary>
    static void CreateRealtime(string name, bool realtimeGI)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Camera(new Color(0f, 0f, 0f));
        var point = new GameObject("Point Light", typeof(Light)).GetComponent<Light>();
        point.transform.position = new Vector3(1.6f, 1.6f, 0.5f);
        point.type = LightType.Point;
        point.color = new Color(1f, 0.2f, 0.1f);
        point.intensity = 4f;
        point.range = 8f;
        point.lightmapBakeType = LightmapBakeType.Realtime;
        point.bounceIntensity = 2f;
        var white = Mat(name + "White", Color.white);
        Static(PrimitiveType.Plane, Vector3.zero, Vector3.one, white);
        Static(PrimitiveType.Cube, new Vector3(-2f, 1.5f, 0f), new Vector3(0.2f, 3f, 6f), white);
        Static(PrimitiveType.Cube, new Vector3(0f, 1.5f, 2.5f), new Vector3(6f, 3f, 0.2f), white);
        Static(PrimitiveType.Cube, new Vector3(0.5f, 0.6f, 0f), new Vector3(0.6f, 1.2f, 0.6f), Mat(name + "Green", new Color(0.2f, 0.8f, 0.3f)));
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;

        var settings = Deterministic(name);
        settings.bakedGI = false;
        settings.realtimeGI = realtimeGI;
        settings.realtimeEnvironmentLighting = true;
        settings.indirectResolution = 2f;
        Lightmapping.lightingSettings = settings;
        EditorSceneManager.SaveScene(scene, $"{Scenes}/{name}.unity");
        if (realtimeGI && !Lightmapping.Bake()) Debug.LogError("precomputing " + name + " failed");
        EditorSceneManager.SaveScene(scene);
    }
}
