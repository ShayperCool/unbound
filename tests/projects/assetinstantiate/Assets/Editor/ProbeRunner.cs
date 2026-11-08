using UnityEditor;
using UnityEditor.SceneManagement;
public static class ProbeRunner
{
    public static void Record()
    {
        if (System.Environment.GetEnvironmentVariable("ASSET_INSTANTIATE_FOLLOWUPS") == "1") FollowupAuthor.Create();
        EditorSceneManager.OpenScene(System.Environment.GetEnvironmentVariable("ASSET_INSTANTIATE_FOLLOWUPS") == "1" ? "Assets/Scenes/Followup.unity" : "Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
