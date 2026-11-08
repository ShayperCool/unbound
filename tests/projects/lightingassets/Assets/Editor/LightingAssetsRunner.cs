using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod LightingAssetsRunner.Record` with <c>LIGHTING_ASSETS_OUT</c> set: opens
/// the first scene and enters Play mode, where <see cref="LightingAssetsProbe"/> records and exits.</summary>
public static class LightingAssetsRunner
{
    public static void PrepareWarmup()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/WarmupOnly.unity");
        EditorApplication.Exit(0);
    }
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene(System.Environment.GetEnvironmentVariable("SHADER_WARMUP_MODE") == null ? "Assets/Scenes/Plain.unity" : "Assets/Scenes/WarmupOnly.unity");
        EditorApplication.EnterPlaymode();
    }
}
