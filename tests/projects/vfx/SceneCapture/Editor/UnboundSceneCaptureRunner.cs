using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>The Editor side of UnboundSceneCapture: opens the scene the run names
/// (UNBOUND_SCENE_CAPTURE_SCENE) and plays it once the Editor has settled (the packages may
/// bring a copied project up to date in its first updates, which would otherwise fall between
/// the first frames). A script reload while it settles (such an update may recompile) waits
/// again from the start; scripts that do not compile end the run at once.</summary>
[InitializeOnLoad]
public static class UnboundSceneCaptureRunner
{
    const int SettleUpdates = 5;
    /// <summary>Set (for the Editor's session, which outlives a script reload) while the run
    /// waits to play.</summary>
    const string WaitingKey = "UnboundSceneCapture.Waiting";
    static int s_Updates;

    static UnboundSceneCaptureRunner()
    {
        if (SessionState.GetBool(WaitingKey, false)) Wait();
    }

    public static void Record()
    {
        var scene = Environment.GetEnvironmentVariable("UNBOUND_SCENE_CAPTURE_SCENE");
        if (string.IsNullOrEmpty(scene) || !File.Exists(scene))
        {
            Debug.LogError("UnboundSceneCapture: no scene (UNBOUND_SCENE_CAPTURE_SCENE): " + scene);
            EditorApplication.Exit(1);
            return;
        }
        EditorSceneManager.OpenScene(scene);
        SessionState.SetBool(WaitingKey, true);
        Wait();
    }

    static void Wait()
    {
        s_Updates = 0;
        EditorApplication.update -= PlayOnceSettled;
        EditorApplication.update += PlayOnceSettled;
    }

    static void PlayOnceSettled()
    {
        if (EditorApplication.isCompiling || ++s_Updates < SettleUpdates) return;
        EditorApplication.update -= PlayOnceSettled;
        SessionState.EraseBool(WaitingKey);
        if (EditorUtility.scriptCompilationFailed)
        {
            Debug.LogError("UnboundSceneCapture: the project's scripts do not compile; not playing");
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.EnterPlaymode();
    }
}
