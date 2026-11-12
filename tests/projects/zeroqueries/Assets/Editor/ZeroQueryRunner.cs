using System;
using UnityEditor;
using UnityEditor.SceneManagement;
[InitializeOnLoad]
public static class ZeroQueryRunner
{
    static ZeroQueryRunner()
    {
        if (Environment.GetEnvironmentVariable("ZERO_QUERY_OUT") == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        int warm = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () => {
            if (++warm < 30) return;
            EditorApplication.update -= wait;
            EditorSceneManager.OpenScene("Assets/Scenes/Empty.unity");
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
}
