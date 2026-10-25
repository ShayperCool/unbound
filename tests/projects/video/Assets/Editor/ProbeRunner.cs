using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/video -executeMethod ProbeRunner.Record`
/// with <c>VIDEO_PROBE_OUT</c> set: a new empty scene in Play mode, where
/// <see cref="VideoProbe"/> records the simulations and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
