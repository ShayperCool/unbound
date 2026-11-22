using UnityEditor;
using UnityEditor.SceneManagement;

public static class ImguiContainerRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
