using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>`-executeMethod LightingDataNanSetup.Create`: builds and bakes the probe scene (CPU
/// lightmapper, no filtering or compression). Every static object contributes to global
/// illumination; the ground is lightmapped, the quads receive it from light probes. The flat quad's
/// lightmap UVs are all zero, so the bake gives it a non-finite lightmap scale and offset. A baked
/// reflection probe (clear colour, captures nothing) holds one mirror sphere; another sphere
/// outside it reflects the baked sky.</summary>
public static class LightingDataNanSetup
{
    static Mesh Quad(string name, bool flatLightmapUvs)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
        var uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        mesh.uv = uv;
        mesh.uv2 = flatLightmapUvs ? new Vector2[4] : uv;
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"Assets/Scenes/{name}.asset");
        return mesh;
    }

    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 2.5f, -7f);
        camera.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

        var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        sun.type = LightType.Directional;
        sun.lightmapBakeType = LightmapBakeType.Baked;

        var standard = Shader.Find("Standard");
        Material Mat(string name, Color color, float metallic, float smoothness)
        {
            var m = new Material(standard) { color = color };
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Glossiness", smoothness);
            AssetDatabase.CreateAsset(m, $"Assets/Scenes/{name}.mat");
            return m;
        }
        var matte = Mat("Matte", new Color(0.7f, 0.6f, 0.5f), 0f, 0.3f);
        var mirror = Mat("Mirror", Color.white, 1f, 0.95f);
        GameObject Static(string name, Mesh mesh, Vector3 position, Vector3 scale, ReceiveGI receive)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = matte;
            renderer.receiveGI = receive;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
            return go;
        }
        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        Static("Ground", cube, new Vector3(0f, -0.05f, 0f), new Vector3(12f, 0.1f, 8f), ReceiveGI.Lightmaps);
        Static("Quad Probes", Quad("QuadProbes", false), new Vector3(-2.5f, 0.3f, -1f), Vector3.one, ReceiveGI.LightProbes);
        Static("Quad Flat Lightmap UVs", Quad("QuadFlatLightmapUvs", true), new Vector3(-1.2f, 0.3f, -1f), Vector3.one, ReceiveGI.LightProbes);

        var probe = new GameObject("Reflection Probe", typeof(ReflectionProbe)).GetComponent<ReflectionProbe>();
        probe.transform.position = new Vector3(1.5f, 1f, 0f);
        probe.mode = ReflectionProbeMode.Baked;
        probe.size = new Vector3(3f, 3f, 3f);
        probe.resolution = 32;
        probe.hdr = true;
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.2f, 0.8f, 0.3f, 1f);
        probe.cullingMask = 0;
        foreach (var (name, x) in new[] { ("Mirror In Probe", 1.5f), ("Mirror In Sky", -3.8f) })
        {
            var sphere = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            sphere.transform.position = new Vector3(x, 0.8f, 0.5f);
            sphere.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            sphere.GetComponent<MeshRenderer>().sharedMaterial = mirror;
        }

        var probes = new GameObject("Light Probe Group", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
        var positions = new System.Collections.Generic.List<Vector3>();
        foreach (var x in new[] { -5f, 5f })
            foreach (var y in new[] { 0.1f, 2.5f })
                foreach (var z in new[] { -3f, 3f })
                    positions.Add(new Vector3(x, y, z));
        probes.probePositions = positions.ToArray();

        var settings = new LightingSettings
        {
            name = "LightingDataNan",
            bakedGI = true,
            realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = 4f,
            lightmapPadding = 2,
            lightmapMaxSize = 256,
            directionalityMode = LightmapsMode.NonDirectional,
            lightmapCompression = LightmapCompression.None,
            ao = false,
            directSampleCount = 16,
            indirectSampleCount = 32,
            environmentSampleCount = 32,
            maxBounces = 1,
            filteringMode = LightingSettings.FilterMode.None,
            denoiserTypeDirect = LightingSettings.DenoiserType.None,
            denoiserTypeIndirect = LightingSettings.DenoiserType.None,
            denoiserTypeAO = LightingSettings.DenoiserType.None,
        };
        AssetDatabase.CreateAsset(settings, "Assets/Scenes/LightingDataNan.lighting");
        Lightmapping.lightingSettings = settings;
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        if (!Lightmapping.Bake()) Debug.LogError("baking failed");
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
