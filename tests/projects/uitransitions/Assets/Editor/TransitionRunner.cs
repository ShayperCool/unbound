using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class TransitionRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Transition probe").AddComponent<TransitionProbe>();
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
    }
    public static void Record() { TransitionProbe.Run(); EditorApplication.Exit(0); }
}
