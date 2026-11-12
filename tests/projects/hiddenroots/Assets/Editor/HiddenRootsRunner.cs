using System;
using UnityEditor;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public static class HiddenRootsRunner
{
    static HiddenRootsRunner()
    {
        if (Environment.GetEnvironmentVariable("HIDDEN_ROOTS_OUT") == null) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !HiddenRootsControl.Finished) return;
            HiddenRootsControl.AfterPlay();
            EditorApplication.Exit(0);
        };
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int warmup = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++warmup < 30) return;
            EditorApplication.update -= wait;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.OpenScene("Assets/Scenes/Empty.unity");
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
}
