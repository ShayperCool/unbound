using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Opens the probe scene and enters Play mode, where BindingsProbe imports the documents
/// again (recording the importer's warnings and errors), records the bindings and exits the Editor.</summary>
public static class UitkBindingsRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
