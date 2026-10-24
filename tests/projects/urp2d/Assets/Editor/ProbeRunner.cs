using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/urp2d -executeMethod ProbeRunner.Record`
/// with <c>URP2D_PROBE_DIR</c> set: opens the probe scene and enters Play mode, where
/// <see cref="Urp2DProbe"/> records every scenario and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        // The reference must use the real shader variants, not async compilation's placeholders.
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
