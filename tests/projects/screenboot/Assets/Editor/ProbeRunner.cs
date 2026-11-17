using UnityEditor;
using UnityEditor.SceneManagement;

public static class ProbeRunner
{
    /// <summary>Opens the probe scene, gives the Game view a rendering resolution of 960x540 (the
    /// engine's window: run.py's --size) and enters Play mode.</summary>
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetViewType(PlayModeWindow.PlayModeViewTypes.GameView);
        PlayModeWindow.SetCustomRenderingResolution(960, 540, "probe");
        EditorApplication.EnterPlaymode();
    }
}
