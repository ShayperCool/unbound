using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class TransitionCallbackRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Transition probe").AddComponent<TransitionCallbackProbe>();
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
    }
    public static void Record() { TransitionCallbackProbe.Run(); EditorApplication.Exit(0); }
}
