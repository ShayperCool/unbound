using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Enters Play mode in the probe scene, where PanelUtilsProbe records the conversions and
/// exits the Editor.</summary>
public static class UitkPanelUtilsRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
