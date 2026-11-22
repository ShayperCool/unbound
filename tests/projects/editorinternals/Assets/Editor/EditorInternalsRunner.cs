using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Enters Play mode in the probe scene, where EditorInternalsBehaviour records its values
/// and exits the Editor.</summary>
public static class EditorInternalsRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
