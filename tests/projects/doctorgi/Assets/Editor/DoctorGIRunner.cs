using UnityEditor;
using UnityEditor.SceneManagement;

public static class DoctorGIRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/RealtimeOff.unity");
        EditorApplication.EnterPlaymode();
    }
}
