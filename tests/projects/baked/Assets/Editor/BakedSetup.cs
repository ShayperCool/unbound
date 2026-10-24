using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>`Unity -batchmode -projectPath tests/projects/baked -executeMethod BakedSetup.Create`:
/// builds the probe scene (static ground, cube and column lit by a baked directional and a baked
/// point light, a light probe grid and a dynamic sphere it lights) and bakes it with the CPU
/// lightmapper, no denoising, filtering or compression, so the bake is repeatable.</summary>
public static class BakedSetup
{
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 2.2f, -6f);
        camera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
        var cam = camera.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.12f, 0.18f, 1f);

        var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(45f, -40f, 0f);
        sun.type = LightType.Directional;
        sun.intensity = 1.2f;
        sun.shadows = LightShadows.Hard;
        sun.lightmapBakeType = LightmapBakeType.Baked;

        var point = new GameObject("Point Light", typeof(Light)).GetComponent<Light>();
        point.transform.position = new Vector3(1.5f, 1.2f, -1f);
        point.type = LightType.Point;
        point.color = new Color(1f, 0.35f, 0.2f);
        point.intensity = 3f;
        point.range = 5f;
        point.shadows = LightShadows.Hard;
        point.lightmapBakeType = LightmapBakeType.Baked;

        var standard = Shader.Find("Standard");
        Material Mat(string name, Color color)
        {
            var m = new Material(standard) { color = color };
            m.SetFloat("_Glossiness", 0.2f);
            AssetDatabase.CreateAsset(m, $"Assets/Scenes/{name}.mat");
            return m;
        }
        GameObject Static(PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            return go;
        }
        Static(PrimitiveType.Plane, Vector3.zero, Vector3.one, Mat("Ground", new Color(0.7f, 0.7f, 0.7f)));
        Static(PrimitiveType.Cube, new Vector3(-1.3f, 0.5f, 0.2f), Vector3.one, Mat("Cube", new Color(0.2f, 0.4f, 0.9f)));
        Static(PrimitiveType.Cube, new Vector3(0.6f, 1f, 1.2f), new Vector3(0.5f, 2f, 0.5f), Mat("Column", new Color(0.9f, 0.85f, 0.6f)));

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Dynamic Sphere";
        sphere.transform.position = new Vector3(1.6f, 0.6f, -0.6f);
        sphere.transform.localScale = Vector3.one * 0.8f;
        sphere.GetComponent<MeshRenderer>().sharedMaterial = Mat("Sphere", Color.white);

        var probes = new GameObject("Light Probe Group", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
        var positions = new System.Collections.Generic.List<Vector3>();
        for (int x = -2; x <= 2; x += 2)
            for (int y = 0; y <= 2; y += 2)
                for (int z = -2; z <= 2; z += 2)
                    positions.Add(new Vector3(x, y + 0.1f, z));
        probes.probePositions = positions.ToArray();

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.25f, 0.3f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.2f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.08f, 0.06f);

        var settings = new LightingSettings
        {
            name = "BakedLighting",
            bakedGI = true,
            realtimeGI = false,
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
            mixedBakeMode = MixedLightingMode.IndirectOnly,
        };
        AssetDatabase.CreateAsset(settings, "Assets/Scenes/BakedLighting.lighting");
        Lightmapping.lightingSettings = settings;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Baked.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Baked.unity", true) };
        if (!Lightmapping.Bake()) Debug.LogError("baking failed");
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
