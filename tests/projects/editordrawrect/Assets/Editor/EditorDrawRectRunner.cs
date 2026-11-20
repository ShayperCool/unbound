using UnityEditor;
using UnityEditor.SceneManagement;

public static class EditorDrawRectRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "EditorDrawRect");
        EditorApplication.EnterPlaymode();
    }
}
