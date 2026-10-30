using System;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>`ProbeRunner.Create` makes the empty probe scene. With <c>DISPLAY_PROBE_RUN=1</c> the
/// windowed Editor (on an X server of its own) opens it at a 320x180 Game view on Display 1 and
/// enters Play mode, where <see cref="DisplayProbe"/> captures the view and exits.</summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    static ProbeRunner()
    {
        if (Environment.GetEnvironmentVariable("DISPLAY_PROBE_RUN") != "1" || Application.isBatchMode) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool("DisplayProbe.started", false)) return;
        SessionState.SetBool("DisplayProbe.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
            PlayModeWindow.SetCustomRenderingResolution(320, 180, "Probe");
            var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.maximized = true;
            EditorApplication.EnterPlaymode();
        };
        EditorApplication.update += wait;
    }
}
