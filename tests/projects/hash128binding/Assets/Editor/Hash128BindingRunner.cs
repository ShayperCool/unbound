using UnityEditor;
using UnityEditor.SceneManagement;

public static class Hash128BindingRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
