using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Plays the probe scene (SceneColorProbe records and exits).</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        // The reference must use the real shader variants, not async compilation's placeholders.
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        // A game view of the probe's size.
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "SceneColorProbe");
        EditorApplication.EnterPlaymode();
    }
}
