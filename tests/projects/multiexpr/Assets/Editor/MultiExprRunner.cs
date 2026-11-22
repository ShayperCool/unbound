using UnityEditor;
using UnityEditor.SceneManagement;

public static class MultiExprRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
