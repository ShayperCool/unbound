using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Opens the empty probe scene and enters Play mode, where the probe script records its
/// values and exits the Editor.</summary>
public static class WaitForSecondsRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
