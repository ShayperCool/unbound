using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Creates the probe's empty scene and makes it the build's first scene.</summary>
public static class ProbeSetup
{
    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
