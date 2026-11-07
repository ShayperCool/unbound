using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod LightingAssetsRunner.Record` with <c>LIGHTING_ASSETS_OUT</c> set: opens
/// the first scene and enters Play mode, where <see cref="LightingAssetsProbe"/> records and exits.</summary>
public static class LightingAssetsRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Plain.unity");
        EditorApplication.EnterPlaymode();
    }
}
