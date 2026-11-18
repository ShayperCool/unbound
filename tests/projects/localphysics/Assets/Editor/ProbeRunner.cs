using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    // Authors the committed Sim scene (a 3D and a 2D floor at the origin) and the build list.
    public static void MakeScenes()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var floor = new GameObject("simFloor");
        floor.AddComponent<BoxCollider>().size = new Vector3(4, 1, 4);
        var floor2 = new GameObject("simFloor2");
        floor2.AddComponent<BoxCollider2D>().size = new Vector2(4, 1);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Sim.unity");
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Sim.unity", true),
        };
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
