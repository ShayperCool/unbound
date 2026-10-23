using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/physics -executeMethod ProbeRunner.Record` with
/// <c>PHYSICS_PROBE_OUT</c> set: opens the probe scene and enters Play mode, where
/// <see cref="ProbeEntry"/> records the reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
