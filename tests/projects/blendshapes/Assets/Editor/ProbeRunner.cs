using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>`Unity -batchmode -quit -projectPath tests/projects/blendshapes -executeMethod
/// ProbeRunner.Create` makes the scene and the mesh asset with blend shapes;
/// `-executeMethod ProbeRunner.Record` with <c>BLEND_PROBE_OUT</c> set enters Play mode, where
/// <see cref="BlendProbe"/> records and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        // The model's mesh readable, for its vertices and deltas.
        var importer = (ModelImporter)AssetImporter.GetAtPath("Assets/Resources/Blob.fbx");
        importer.isReadable = true;
        importer.SaveAndReimport();
        var mesh = BlendProbe.ScriptedMesh();
        mesh.name = "Shaped";
        AssetDatabase.CreateAsset(mesh, "Assets/Resources/Shaped.asset");
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
