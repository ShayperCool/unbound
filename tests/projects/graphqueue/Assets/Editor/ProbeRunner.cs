using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `-executeMethod ProbeRunner.Create` makes the probe's inputs once: a transparent URP Unlit
/// Shader Graph without material override (as URP's "Unlit Shader Graph" menu makes one, surface
/// Transparent), materials of it saved in the states <see cref="GraphQueueProbe"/> reads, and the
/// probe scene with a quad drawn by one of them. `-executeMethod ProbeRunner.Record` plays the scene.
/// </summary>
public static class ProbeRunner
{
    const string Graph = "Assets/Graphs/Transparent.shadergraph";

    static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);

    static object Block(string group, string name) =>
        Find("UnityEditor.ShaderGraph.BlockFields+" + group).GetField(name).GetValue(null);

    public static void Create()
    {
        foreach (var path in new[] { "Assets/Graphs", "Assets/Resources", "Assets/Scenes/Probe.unity" })
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Graphs");
        Directory.CreateDirectory("Assets/Resources");

        var targetType = Find("UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget");
        var target = Activator.CreateInstance(targetType, true);
        targetType.GetMethod("TrySetActiveSubTarget").Invoke(target, new object[] { Find("UnityEditor.Rendering.Universal.ShaderGraph.UniversalUnlitSubTarget") });
        var surface = targetType.GetProperty("surfaceType");
        surface.SetValue(target, Enum.Parse(surface.PropertyType, "Transparent"));
        var targets = Array.CreateInstance(Find("UnityEditor.ShaderGraph.Target"), 1);
        targets.SetValue(target, 0);
        var blockType = Find("UnityEditor.ShaderGraph.BlockFieldDescriptor");
        var blocks = Array.CreateInstance(blockType, 5);
        blocks.SetValue(Block("VertexDescription", "Position"), 0);
        blocks.SetValue(Block("VertexDescription", "Normal"), 1);
        blocks.SetValue(Block("VertexDescription", "Tangent"), 2);
        blocks.SetValue(Block("SurfaceDescription", "BaseColor"), 3);
        blocks.SetValue(Block("SurfaceDescription", "Alpha"), 4);
        var action = ScriptableObject.CreateInstance(Find("UnityEditor.ShaderGraph.NewGraphAction"));
        action.GetType().GetProperty("targets").SetValue(action, targets);
        action.GetType().GetProperty("blocks").SetValue(action, blocks);
        action.GetType().GetMethod("Action").Invoke(action, new object[] { 0, Graph, null });
        AssetDatabase.ImportAsset(Graph);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Graph);

        // Saved as a material whose stored queue already includes its offset (3000 - 50), one with
        // a stale stored queue, one whose queue the user controls, and the scene's.
        Save(shader, "OffsetStored", -50, 0, 2950);
        Save(shader, "OffsetStale", -50, 0, 2500);
        Save(shader, "NoOffsetStale", 0, 0, 2000);
        Save(shader, "UserQueue", -50, 1, 2100);
        var sceneMaterial = Save(shader, "SceneStale", -50, 0, 2500, "Assets/Graphs");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "SceneQuad";
        quad.GetComponent<MeshRenderer>().sharedMaterial = sceneMaterial;
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -3);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static Material Save(Shader shader, string name, float offset, float control, int queue, string folder = "Assets/Resources")
    {
        var material = new Material(shader);
        material.SetFloat("_QueueOffset", offset);
        material.SetFloat("_QueueControl", control);
        material.renderQueue = queue;
        AssetDatabase.CreateAsset(material, folder + "/" + name + ".mat");
        return material;
    }

    /// <summary>Imports the graph, then the materials again, as a project opened with its imports
    /// done has them (a fresh copy imports the materials before the graph's shader exists), renders
    /// the scene once and plays it.</summary>
    public static void Record()
    {
        AssetDatabase.ImportAsset(Graph, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("graph queue probe: shader queue " + AssetDatabase.LoadAssetAtPath<Shader>(Graph).renderQueue);
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Resources", "Assets/Graphs" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        // As an Editor that has drawn its views before Play mode: the scene's camera renders once
        // (the render pipeline is made and the quad's shader compiled).
        var camera = Camera.main;
        var target = new RenderTexture(64, 64, 24);
        camera.targetTexture = target;
        bool async = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        camera.Render();
        ShaderUtil.allowAsyncCompilation = async;
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("graph queue probe: after a render, pipeline tag " + Shader.globalRenderPipeline + ", shader queue " + AssetDatabase.LoadAssetAtPath<Shader>(Graph).renderQueue);
        EditorApplication.EnterPlaymode();
    }
}
