using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Authors and bakes the probe's scene (see README.md).
/// <see cref="Create"/> (batch mode, `run.py create`) makes the room: a floor, a red and a green
/// side wall, a white back wall and a roof over the red half, all static and lit by the probe
/// volume, a baked directional light under the procedural sky, a global Adaptive Probe Volume and
/// its baking set. <see cref="Bake"/> (`run.py bake`, the Vulkan Editor: the probe volume bake
/// needs its GPU path) bakes it with Lightmapping.Bake.</summary>
public static class ApvLightingSetup
{
    public const string Folder = "Assets/Scenes";
    public const string Scene = Folder + "/Lighting.unity";

    static Material Mat(string name, Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0f);
        AssetDatabase.CreateAsset(material, $"Assets/Materials/{name}.mat");
        return material;
    }

    static void Block(string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        // Static and contributing to the bake, but lit by the probe volume like the dynamic
        // objects the capture adds (no lightmap: the bake writes probes only).
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
        renderer.receiveGI = ReceiveGI.LightProbes;
    }

    public static void Create()
    {
        try
        {
            AssetDatabase.CreateFolder("Assets", "Scenes");
            AssetDatabase.CreateFolder("Assets", "Materials");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var settings = new LightingSettings
            {
                name = "Lighting",
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                bakedGI = true,
                realtimeGI = false,
                lightmapResolution = 4f,
                lightmapMaxSize = 256,
                directionalityMode = LightmapsMode.NonDirectional,
                lightmapCompression = LightmapCompression.None,
                mixedBakeMode = MixedLightingMode.IndirectOnly,
            };
            AssetDatabase.CreateAsset(settings, $"{Folder}/Lighting.lighting");
            Lightmapping.lightingSettings = settings;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                light.name = "Sun";
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.intensity = 1.5f;
                light.bounceIntensity = 1.25f;
                light.shadows = LightShadows.Hard;
                light.transform.rotation = Quaternion.Euler(45f, 30f, 0f);
            }
            var grey = Mat("Grey", new Color(0.6f, 0.6f, 0.6f, 1f));
            Block("Floor", new Vector3(0f, -0.1f, 0f), new Vector3(8f, 0.2f, 8f), grey);
            Block("Red Wall", new Vector3(-4.1f, 1.5f, 0f), new Vector3(0.2f, 3f, 8f), Mat("Red", new Color(0.8f, 0.1f, 0.1f, 1f)));
            Block("Green Wall", new Vector3(4.1f, 1.5f, 0f), new Vector3(0.2f, 3f, 8f), Mat("Green", new Color(0.1f, 0.7f, 0.15f, 1f)));
            Block("Back Wall", new Vector3(0f, 1.5f, 4.1f), new Vector3(8f, 3f, 0.2f), Mat("White", new Color(0.9f, 0.9f, 0.9f, 1f)));
            Block("Roof", new Vector3(-2f, 3.1f, 0f), new Vector3(4.2f, 0.2f, 8f), grey);
            var volume = new GameObject("Probe Volume").AddComponent<ProbeVolume>();
            volume.mode = ProbeVolume.Mode.Global;
            EditorSceneManager.SaveScene(scene, Scene);
            var bakingSet = ScriptableObject.CreateInstance<ProbeVolumeBakingSet>();
            bakingSet.name = "Lighting Baking Set";
            AssetDatabase.CreateAsset(bakingSet, $"{Folder}/Lighting Baking Set.asset");
            bakingSet.TryAddScene(AssetDatabase.AssetPathToGUID(Scene));
            EditorUtility.SetDirty(bakingSet);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static void Bake()
    {
        try
        {
            EditorSceneManager.OpenScene(Scene);
            // The probe volume system starts with the first frame the pipeline renders.
            if (Camera.main != null)
            {
                var rt = new RenderTexture(16, 16, 16);
                Camera.main.targetTexture = rt;
                Camera.main.Render();
                Camera.main.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            bool baked = false;
            // As tests/projects/apvbake: the GPU bake may fail to start on a first attempt.
            for (int attempt = 0; attempt < 3 && !baked; attempt++)
                baked = Lightmapping.Bake();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            if (!baked) Debug.LogError("apvlighting probe: Lightmapping.Bake failed");
            EditorApplication.Exit(baked ? 0 : 1);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
