using UnityEditor;
using UnityEditor.SceneManagement;

public static class BindTreeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
