using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod ProbeRunner.Record` plays the probe scene in a 320x180 game view, where
/// <see cref="UrpOverlayUiProbe"/> records.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        // The reference must use the real shader variants, not async compilation's placeholders.
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "UrpOverlayUiProbe");
        EditorApplication.EnterPlaymode();
    }
}
