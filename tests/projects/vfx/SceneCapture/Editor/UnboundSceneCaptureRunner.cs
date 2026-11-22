using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>The Editor side of UnboundSceneCapture: opens the scene capture.json's run names
/// (UNBOUND_SCENE_CAPTURE_SCENE) and plays it once the Editor has settled (the packages may
/// bring a copied project up to date in its first updates, which would otherwise fall
/// between the first frames).</summary>
public static class UnboundSceneCaptureRunner
{
    const int SettleUpdates = 5;
    static int s_Updates;

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
        s_Updates = 0;
        EditorApplication.update += PlayOnceSettled;
    }

    static void PlayOnceSettled()
    {
        if (++s_Updates < SettleUpdates) return;
        EditorApplication.update -= PlayOnceSettled;
        EditorApplication.EnterPlaymode();
    }
}
