using System;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]
public static class DependencyRunner
{
    static DependencyRunner()
    {
        if (Environment.GetEnvironmentVariable("FLARE_DEPENDENCY_OUT") == null) return;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode) DependencyProbe.Finished = () => EditorApplication.Exit(0);
        };
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Probe.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }
}
