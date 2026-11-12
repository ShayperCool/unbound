using System;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class QuitControlRunner
{
    static QuitControlRunner()
    {
        if (Environment.GetEnvironmentVariable("QUIT_CONTROL_OUT") == null) return;
        if (SessionState.GetBool("QuitControl.complete", false) || SessionState.GetBool("QuitControl.finished", false))
        {
            EditorApplication.delayCall += Finish;
            return;
        }
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("QuitControl.finished", false))
                Finish();
        };
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int warmup = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++warmup < 30) return;
            EditorApplication.update -= wait;
            if (Environment.GetEnvironmentVariable("QUIT_CONTROL_NO_RELOAD") == "1")
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            }
            EditorSceneManager.OpenScene("Assets/Scenes/Empty.unity");
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
    static void Finish()
    {
        QuitControl.CompleteCapture();
        EditorApplication.Exit(0);
    }
}
