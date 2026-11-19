using System;
using UnityEditor;
using UnityEditor.SceneManagement;

// Enters Play mode in an empty scene and quits once the probe finished.
[InitializeOnLoad]
public static class CollisionRunner
{
    static CollisionRunner()
    {
        if (Environment.GetEnvironmentVariable("PARTICLE_COLLISION_OUT") == null) return;
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlaying && CollisionProbe.Finished) EditorApplication.ExitPlaymode();
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && CollisionProbe.Finished) EditorApplication.Exit(0);
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
