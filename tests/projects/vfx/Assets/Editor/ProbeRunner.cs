using UnityEditor;
using UnityEditor.SceneManagement;

public static class ProbeRunner
{
    /// <summary>Authors the graphs and the scene, then plays the scene: the probe records.</summary>
    public static void Record()
    {
        GraphAuthoring.Author();
        EditorSceneManager.OpenScene(GraphAuthoring.Scene);
        EditorApplication.EnterPlaymode();
    }
}
