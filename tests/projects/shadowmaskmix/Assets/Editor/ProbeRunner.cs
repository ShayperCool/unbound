using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`run.py record`: opens the probe scene and enters Play mode, where
/// <see cref="ShadowmaskProbe"/> records its cases and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Shadowmask.unity");
        EditorApplication.EnterPlaymode();
    }
}
