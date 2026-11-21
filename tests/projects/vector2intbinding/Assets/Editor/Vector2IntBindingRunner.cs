using UnityEditor;
using UnityEditor.SceneManagement;

public static class Vector2IntBindingRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
