using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// <c>-executeMethod ProbeRunner.Create</c> makes the probe's assets (the solid material, the
/// Resources material, ScriptableObject and prefab the probe unloads, the pipeline asset, set as
/// the project's default pipeline) and the probe scene, then exits (1 on failure).
/// <c>-executeMethod ProbeRunner.Record</c> with <c>DROPPED_PARAMS_PROBE_OUT</c> set opens the
/// scene and enters Play mode, where the probe records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        try
        {
            var solid = new Material(Shader.Find("Probe/Solid"));
            AssetDatabase.CreateAsset(solid, "Assets/Shaders/Solid.mat");
            var unloadMaterial = new Material(Shader.Find("Probe/Solid")) { color = Color.white };
            AssetDatabase.CreateAsset(unloadMaterial, "Assets/Resources/UnloadMat.mat");
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ProbeData>(), "Assets/Resources/UnloadData.asset");
            var prefab = new GameObject("UnloadPrefab");
            PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Resources/UnloadPrefab.prefab");
            UnityEngine.Object.DestroyImmediate(prefab);
            var pipeline = ScriptableObject.CreateInstance<ProbePipelineAsset>();
            AssetDatabase.CreateAsset(pipeline, "Assets/Settings/ProbePipeline.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var probe = new GameObject("DroppedParamsProbe").AddComponent<DroppedParamsProbe>();
            probe.dimsShaders = new[] { Shader.Find("Probe/Dims"), Shader.Find("Probe/DimsLower") };
            probe.solid = solid;
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("ProbeRunner.Create failed: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
