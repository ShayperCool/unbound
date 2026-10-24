using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/baked -executeMethod BakedRunner.Record`
/// with <c>BAKED_PROBE_OUT</c> set: opens the probe scene and enters Play mode, where
/// <see cref="BakedProbe"/> records the frame and exits the Editor.</summary>
public static class BakedRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Baked.unity");
        EditorApplication.EnterPlaymode();
    }
}
