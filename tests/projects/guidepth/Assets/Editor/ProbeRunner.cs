using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    /// <summary>Makes the probe's scene: the controller, then behaviours A and B.</summary>
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Controller").AddComponent<GuiDepthProbe>();
        new GameObject("A").AddComponent<GuiDepthBehaviour>();
        new GameObject("B").AddComponent<GuiDepthBehaviour>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
