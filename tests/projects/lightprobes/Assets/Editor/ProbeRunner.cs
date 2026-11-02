using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/lightprobes -executeMethod ProbeRunner.Record`
/// with <c>LIGHT_PROBES_PROBE_OUT</c> set: opens the Main scene and enters Play mode, where
/// <see cref="LightProbesProbe"/> records and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
