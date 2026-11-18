using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod ProbeRunner.Record` makes Android the active build target (the split
/// alpha sprite's platform), then plays the empty probe scene in a 320x180 game view, where
/// <see cref="UiAlphaBordersProbe"/> records and exits.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        // The reference must use the real shader variants, not async compilation's placeholders.
        EditorSettings.asyncShaderCompilation = false;
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "UiAlphaBordersProbe");
        EditorApplication.EnterPlaymode();
    }
}
