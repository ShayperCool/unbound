using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>`Unity -batchmode -projectPath tests/projects/lightprobes -executeMethod LightProbesSetup.Create`:
/// builds and bakes the probe's two scenes with the CPU lightmapper (no denoising, filtering or
/// compression) in Shadowmask mode, so their light probes carry probe occlusion.
/// Main: a static ground and wall, a mixed directional and a mixed point light, 18 light probes on
/// a jittered grid (no four on a sphere, so their tetrahedralization is unique). Extra: 8 more
/// probes beside Main's and a mixed point light, loaded additively at run time.</summary>
public static class LightProbesSetup
{
    // Fixed jitter, so no five probes are co-spherical.
    static readonly float[] Jitter =
    {
        0.13f, -0.07f, 0.11f, -0.17f, 0.05f, 0.19f, 0.03f, -0.12f, -0.09f, 0.16f, -0.04f, 0.08f,
        -0.15f, 0.12f, 0.02f, 0.07f, -0.18f, -0.06f, 0.17f, 0.01f, -0.11f, -0.02f, 0.14f, -0.13f,
        0.09f, -0.16f, 0.06f, 0.18f, -0.08f, -0.03f, 0.04f, 0.15f, -0.19f, -0.01f, 0.10f, -0.14f,
        0.12f, 0.03f, -0.05f, -0.10f, 0.16f, 0.07f, -0.12f, 0.19f, 0.05f, 0.02f, -0.17f, 0.11f,
        -0.06f, 0.13f, -0.15f, 0.08f, 0.01f, -0.09f, 0.17f, -0.02f, 0.14f, -0.18f, 0.06f, 0.10f,
        -0.13f, 0.04f, 0.18f, -0.07f, 0.09f, -0.11f, 0.15f, -0.04f, -0.16f, 0.12f, 0.03f, -0.08f,
        0.07f, 0.16f, -0.01f, -0.14f, 0.11f, 0.05f, -0.19f, 0.02f, 0.13f,
    };

    static LightingSettings Settings(string name)
    {
        var settings = new LightingSettings
        {
            name = name,
            bakedGI = true,
            realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = 4f,
            lightmapPadding = 2,
            lightmapMaxSize = 256,
            directionalityMode = LightmapsMode.NonDirectional,
            lightmapCompression = LightmapCompression.None,
            ao = false,
            directSampleCount = 32,
            indirectSampleCount = 64,
            environmentSampleCount = 32,
            maxBounces = 1,
            filteringMode = LightingSettings.FilterMode.None,
            denoiserTypeDirect = LightingSettings.DenoiserType.None,
            denoiserTypeIndirect = LightingSettings.DenoiserType.None,
            denoiserTypeAO = LightingSettings.DenoiserType.None,
            mixedBakeMode = MixedLightingMode.Shadowmask,
        };
        AssetDatabase.CreateAsset(settings, $"Assets/Scenes/{name}.lighting");
        return settings;
    }

    static void Probes(Vector3[] grid, int jitterStart)
    {
        var probes = new GameObject("Light Probe Group", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
        var positions = new List<Vector3>();
        for (int i = 0; i < grid.Length; i++)
        {
            int j = (jitterStart + 3 * i) % Jitter.Length;
            positions.Add(grid[i] + new Vector3(Jitter[j], Jitter[(j + 1) % Jitter.Length], Jitter[(j + 2) % Jitter.Length]));
        }
        probes.probePositions = positions.ToArray();
    }

    static Light MixedLight(string name, LightType type, Color color, float intensity)
    {
        var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.shadows = LightShadows.Hard;
        light.lightmapBakeType = LightmapBakeType.Mixed;
        return light;
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

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var standard = Shader.Find("Standard");
        var grey = new Material(standard) { color = new Color(0.7f, 0.7f, 0.7f) };
        grey.SetFloat("_Glossiness", 0.1f);
        AssetDatabase.CreateAsset(grey, "Assets/Scenes/Grey.mat");

        // Main.
        var main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 1.2f, -6f);
        var cam = camera.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);

        var sun = MixedLight("Sun", LightType.Directional, new Color(1f, 0.95f, 0.85f), 1.1f);
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var lamp = MixedLight("Lamp", LightType.Point, new Color(1f, 0.4f, 0.2f), 2.5f);
        lamp.transform.position = new Vector3(-1.4f, 1.4f, -0.8f);
        lamp.range = 5f;

        Static(PrimitiveType.Plane, Vector3.zero, Vector3.one, grey);
        Static(PrimitiveType.Cube, new Vector3(0.6f, 1f, 0.3f), new Vector3(0.4f, 2f, 2.2f), grey);

        var grid = new List<Vector3>();
        for (int x = -2; x <= 2; x += 2)
            for (int y = 0; y <= 1; y++)
                for (int z = -2; z <= 2; z += 2)
                    grid.Add(new Vector3(x, 0.3f + 2f * y, z));
        Probes(grid.ToArray(), 0);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.25f, 0.3f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.2f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.08f, 0.06f);
        Lightmapping.lightingSettings = Settings("MainLighting");
        EditorSceneManager.SaveScene(main, "Assets/Scenes/Main.unity");
        if (!Lightmapping.Bake()) Debug.LogError("baking Main failed");
        EditorSceneManager.SaveScene(main);

        // Extra.
        var extra = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var green = MixedLight("Green Lamp", LightType.Point, new Color(0.3f, 1f, 0.4f), 2f);
        green.transform.position = new Vector3(5f, 1.5f, -0.2f);
        green.range = 4f;
        Static(PrimitiveType.Cube, new Vector3(4.6f, 0.5f, 0.4f), new Vector3(0.3f, 1f, 1.2f), grey);
        var extraGrid = new List<Vector3>();
        for (int x = 4; x <= 6; x += 2)
            for (int y = 0; y <= 1; y++)
                for (int z = -1; z <= 1; z += 2)
                    extraGrid.Add(new Vector3(x, 0.3f + 2f * y, z));
        Probes(extraGrid.ToArray(), 54);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.25f, 0.3f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.2f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.08f, 0.06f);
        Lightmapping.lightingSettings = Settings("ExtraLighting");
        EditorSceneManager.SaveScene(extra, "Assets/Scenes/Extra.unity");
        if (!Lightmapping.Bake()) Debug.LogError("baking Extra failed");
        EditorSceneManager.SaveScene(extra);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Extra.unity", true),
        };
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
