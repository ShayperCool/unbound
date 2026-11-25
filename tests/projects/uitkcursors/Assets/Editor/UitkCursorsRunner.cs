using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Opens the probe scene and enters Play mode, where CursorsProbe imports the style sheet
/// again (recording the importer's warnings), records the cursors and exits the Editor.</summary>
public static class UitkCursorsRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
