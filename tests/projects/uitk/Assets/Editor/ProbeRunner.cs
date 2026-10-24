using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/uitk -executeMethod ProbeRunner.Record` with
/// <c>UITK_PROBE_OUT</c> set: opens the probe scene and enters Play mode, where <see cref="UitkProbe"/>
/// records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
