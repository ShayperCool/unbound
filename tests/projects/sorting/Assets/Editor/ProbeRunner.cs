using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/sorting -executeMethod ProbeRunner.Record` with
/// <c>SORTING_PROBE_OUT</c> set: opens the probe scene and enters Play mode, where
/// <see cref="SortingProbe"/> records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
