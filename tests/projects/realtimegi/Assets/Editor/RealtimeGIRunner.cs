using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod RealtimeGIRunner.Record` with <c>REALTIME_GI_OUT</c> set: opens the
/// first scene and enters Play mode, where <see cref="RealtimeGIProbe"/> records and exits.</summary>
public static class RealtimeGIRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Room.unity");
        EditorApplication.EnterPlaymode();
    }
}
