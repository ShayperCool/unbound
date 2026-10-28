using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/lifecycle -executeMethod ProbeRunner.Record`
/// with <c>LIFECYCLE_PROBE_OUT</c> set: a new empty scene in Play mode, where
/// <see cref="LifecycleProbe"/> records 8 frames and exits the Editor.</summary>
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

    /// <summary>The prefab LifecycleProbe instantiates (Resources/Validated).</summary>
    public static void CreatePrefab()
    {
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var go = new UnityEngine.GameObject("Validated");
        go.AddComponent<Validated>();
        PrefabUtility.SaveAsPrefabAsset(go, "Assets/Resources/Validated.prefab");
        UnityEngine.Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
