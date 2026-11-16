using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -batchmode -projectPath &lt;copy&gt; -executeMethod ProbeRunner.Record` with
/// <c>MATERIAL_VALIDATION_OUT</c> set: opens the empty probe scene and enters Play mode, where
/// <see cref="MaterialValidationProbe"/> records and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
