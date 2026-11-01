using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ResetPlaybackRunner {
    public static void Record() {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("ResetPlaybackProbe").AddComponent<ResetPlaybackProbe>();
        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/ResetPlayback.unity");
        EditorApplication.EnterPlaymode();
    }
}
