using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Opens the probe scene and enters Play mode, where TemplatesProbe imports the documents
/// again (recording the importer's warnings and errors), records the cloned templates and exits the
/// Editor.</summary>
public static class UxmlTemplatesRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
