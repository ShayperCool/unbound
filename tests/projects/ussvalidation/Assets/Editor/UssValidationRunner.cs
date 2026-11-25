using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Opens the probe scene and enters Play mode, where UssValidationProbe imports the style
/// sheets again (recording the importer's warnings), records the styles and exits the Editor.</summary>
public static class UssValidationRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
