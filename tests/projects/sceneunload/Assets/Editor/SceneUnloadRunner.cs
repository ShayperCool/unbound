using System;
using UnityEditor;
using UnityEditor.SceneManagement;

// Enters Play mode with the probe's scenes in the build settings and quits once the probe finished.
[InitializeOnLoad]
public static class SceneUnloadRunner
{
    static SceneUnloadRunner()
    {
        if (Environment.GetEnvironmentVariable("SCENE_UNLOAD_OUT") == null) return;
        SceneUnloadProbe.SetForceUnload = force => EditorSettings.forceAssetUnloadAndGCOnSceneLoad = force;
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlaying && SceneUnloadProbe.Finished) EditorApplication.ExitPlaymode();
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SceneUnloadProbe.Finished) EditorApplication.Exit(0);
        };
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        int warmup = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++warmup < 30) return;
            EditorApplication.update -= wait;
            EditorSettings.forceAssetUnloadAndGCOnSceneLoad = true;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.OpenScene("Assets/Scenes/Empty.unity");
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
}
