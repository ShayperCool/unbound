using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>`run.py setup` (`-executeMethod ShadowmaskSetup.Create`): the probe scene, baked in the
/// Shadowmask Lighting Mode with the CPU lightmapper (no denoising, filtering or compression):
/// a static ground, static blocks near the camera and beyond the shadow distance, a mixed sun
/// and a mixed point light with hard shadows, light probes, and dynamic spheres near and far.</summary>
public static class ShadowmaskSetup
{
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.SetPositionAndRotation(new Vector3(0f, 4f, -6f), Quaternion.Euler(18f, 0f, 0f));
        var cam = camera.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.12f, 0.18f, 1f);
        cam.farClipPlane = 200f;

        var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
        sun.type = LightType.Directional;
        sun.intensity = 1.1f;
        sun.shadows = LightShadows.Hard;
        sun.lightmapBakeType = LightmapBakeType.Mixed;

        var point = new GameObject("Point Light", typeof(Light)).GetComponent<Light>();
        point.transform.position = new Vector3(2.5f, 1.4f, 2f);
        point.type = LightType.Point;
        point.color = new Color(1f, 0.45f, 0.2f);
        point.intensity = 2.5f;
        point.range = 6f;
        point.shadows = LightShadows.Hard;
        point.lightmapBakeType = LightmapBakeType.Mixed;

        var standard = Shader.Find("Standard");
        Material Mat(string name, Color color)
        {
            var m = new Material(standard) { color = color };
            m.SetFloat("_Glossiness", 0.2f);
            AssetDatabase.CreateAsset(m, $"Assets/Scenes/{name}.mat");
            return m;
        }
        GameObject Block(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool isStatic)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
            return go;
        }
        var ground = Mat("Ground", new Color(0.7f, 0.7f, 0.7f));
        var blue = Mat("Blue", new Color(0.2f, 0.4f, 0.9f));
        var sand = Mat("Sand", new Color(0.9f, 0.85f, 0.6f));
        var white = Mat("White", Color.white);
        Block("Ground", PrimitiveType.Plane, new Vector3(0f, 0f, 20f), new Vector3(4f, 1f, 5f), ground, true);
        // Near the camera (inside the shadow distance) and far beyond it.
        Block("Near Block", PrimitiveType.Cube, new Vector3(-1.5f, 0.75f, 2f), new Vector3(1f, 1.5f, 1f), blue, true);
        Block("Near Column", PrimitiveType.Cube, new Vector3(1.2f, 1f, 4f), new Vector3(0.5f, 2f, 0.5f), sand, true);
        Block("Far Block", PrimitiveType.Cube, new Vector3(-3f, 1.5f, 30f), new Vector3(2f, 3f, 2f), blue, true);
        Block("Far Column", PrimitiveType.Cube, new Vector3(3f, 2f, 34f), new Vector3(1f, 4f, 1f), sand, true);
        // Dynamic objects: lit by probes, casting realtime shadows only.
        Block("Near Sphere", PrimitiveType.Sphere, new Vector3(0.2f, 0.6f, 1f), Vector3.one * 1.2f, white, false);
        Block("Far Sphere", PrimitiveType.Sphere, new Vector3(0f, 1f, 29f), Vector3.one * 2f, white, false);
        // Dynamic spheres in the sun's shadow of a static wall or block: the near one inside the
        // shadow distance, the far one beyond it; a light probe at each one's center gives it the
        // probe's occlusion.
        Block("Wall", PrimitiveType.Cube, new Vector3(-3f, 1.25f, 1.1f), new Vector3(0.2f, 2.5f, 1f), sand, true);
        Block("Shadowed Sphere", PrimitiveType.Sphere, new Vector3(-4.2f, 0.3f, 2.6f), Vector3.one * 0.6f, white, false);
        Block("Far Shadowed Sphere", PrimitiveType.Sphere, new Vector3(-4.3f, 0.3f, 31.8f), Vector3.one * 0.6f, white, false);

        var probes = new GameObject("Light Probe Group", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
        var positions = new List<Vector3>();
        for (int x = -4; x <= 4; x += 4)
            for (int y = 0; y <= 3; y += 3)
                for (int z = -2; z <= 38; z += 4)
                    positions.Add(new Vector3(x, y + 0.2f, z));
        positions.Add(new Vector3(-4.2f, 0.3f, 2.6f));
        positions.Add(new Vector3(-4.3f, 0.3f, 31.8f));
        probes.probePositions = positions.ToArray();

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.25f, 0.3f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.2f, 0.2f, 0.2f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.08f, 0.06f);

        var settings = new LightingSettings
        {
            name = "ShadowmaskLighting",
            bakedGI = true,
            realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = 4f,
            lightmapPadding = 2,
            lightmapMaxSize = 512,
            directionalityMode = LightmapsMode.NonDirectional,
            lightmapCompression = LightmapCompression.None,
            ao = false,
            directSampleCount = 32,
            indirectSampleCount = 64,
            environmentSampleCount = 64,
            maxBounces = 1,
            filteringMode = LightingSettings.FilterMode.None,
            denoiserTypeDirect = LightingSettings.DenoiserType.None,
            denoiserTypeIndirect = LightingSettings.DenoiserType.None,
            denoiserTypeAO = LightingSettings.DenoiserType.None,
            mixedBakeMode = MixedLightingMode.Shadowmask,
        };
        AssetDatabase.CreateAsset(settings, "Assets/Scenes/ShadowmaskLighting.lighting");
        Lightmapping.lightingSettings = settings;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Shadowmask.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Shadowmask.unity", true) };
        if (!Lightmapping.Bake()) Debug.LogError("baking failed");
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
