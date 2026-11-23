using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Enters Play mode in the probe scene, where PlayerSettingsValuesBehaviour records its
/// values and exits the Editor.</summary>
public static class PlayerSettingsValuesRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
