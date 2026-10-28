using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/avoidance -executeMethod ProbeRunner.Record` with
/// <c>AVOIDANCE_CONFIG</c> and <c>AVOIDANCE_OUT</c> set: opens the probe scene and enters Play
/// mode (in <c>AVOIDANCE_SCENE</c>, by default the flat plane), where <see cref="AvoidanceProbe"/> plays the configurations and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene(System.Environment.GetEnvironmentVariable("AVOIDANCE_SCENE") ?? "Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
