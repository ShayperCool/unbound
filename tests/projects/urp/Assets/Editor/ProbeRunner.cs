using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/urp -executeMethod ProbeRunner.Record` with
/// <c>URP_PROBE_OUT</c> set: opens the probe scene and enters Play mode, where <see cref="UrpProbe"/>
/// records the frame and exits the Editor.</summary>
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
