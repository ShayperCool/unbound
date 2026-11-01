using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>`Unity -batchmode -projectPath &lt;copy&gt; -executeMethod ProbeRunner.Record` with
/// <c>MODULES_PROBE_OUT</c> set: the probe scene in Play mode, where <see cref="ModulesProbe"/>
/// records what the scene's Physics components do while the project's manifest has no
/// <c>com.unity.modules.physics</c>, and exits the Editor.</summary>
public static class ProbeRunner
{
    /// <summary>Made the probe scene: a falling box (Rigidbody and BoxCollider, both from the
    /// disabled Physics module). This Editor-only assembly compiles against every module.</summary>
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var body = new GameObject("Body");
        body.transform.position = new Vector3(0, 10, 0);
        body.AddComponent<BoxCollider>();
        body.AddComponent<Rigidbody>();
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
