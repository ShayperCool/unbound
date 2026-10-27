using System;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `-executeMethod ProbeRunner.Create` saves the empty probe scene. With <c>GAMEPAD_PROBE_OUT</c>
/// set, a windowed Editor (on an X server of its own, with write access to /dev/uinput) opens it
/// and enters Play mode, where <see cref="GamepadProbe"/> plugs in a virtual gamepad and plays its
/// steps; the Editor quits when the record is written.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        if (Environment.GetEnvironmentVariable("GAMEPAD_PROBE_OUT") == null || UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool("GamepadProbe.started", false)) return;
        EditorApplication.delayCall += () =>
        {
            SessionState.SetBool("GamepadProbe.started", true);
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
            EditorApplication.EnterPlaymode();
        };
    }

    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
    }
}
