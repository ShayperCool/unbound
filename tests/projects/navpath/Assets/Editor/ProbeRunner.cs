using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/navpath -executeMethod ProbeRunner.Record` with
/// <c>NAVPATH_PROBE_OUT</c> set: opens the probe scene and enters Play mode, where
/// <see cref="PathProbe"/> records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
