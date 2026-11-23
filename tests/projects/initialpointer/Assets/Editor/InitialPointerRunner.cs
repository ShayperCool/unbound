using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Enters Play mode in the probe scene in a windowed Editor, where InitialPointerProbe
/// records the mouse and exits the Editor.</summary>
public static class InitialPointerRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(960, 540, "Probe");
        EditorApplication.EnterPlaymode();
    }
}
