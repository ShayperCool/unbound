using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class SceneLoopRunner
{
    static SceneLoopRunner()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null || Environment.GetEnvironmentVariable("SCENE_LOAD_LOOP_OUT") == null) return;
        Application.logMessageReceived += (condition, stack, type) => {
            if (type == LogType.Exception) EditorApplication.Exit(1);
        };
        EditorApplication.update += () => {
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            if (EditorApplication.isPlaying && SceneLoadLoop.Finished)
            {
                SessionState.SetBool("SceneLoop.finished", true);
                EditorApplication.ExitPlaymode();
            }
        };
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("SceneLoop.finished", false)) EditorApplication.Exit(0);
        };
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        PlayModeWindow.SetPlayModeFocused(true);
        EditorApplication.EnterPlaymode();
    }

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Prefabs");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = new GameObject("PrefabRoot");
        prefab.AddComponent<SceneLoadItem>();
        var child = new GameObject("PrefabChild");
        child.transform.SetParent(prefab.transform, false);
        child.AddComponent<SceneLoadItem>();
        PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Prefabs/Item.prefab");
        UnityEngine.Object.DestroyImmediate(prefab);
        Target("First", 0);
        Target("Second", 2);
        var boot = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var driver = new GameObject("Driver");
        driver.AddComponent<SceneLoadLoop>();
        var camera = new GameObject("Camera");
        camera.transform.SetParent(driver.transform, false);
        camera.AddComponent<Camera>();
        EditorSceneManager.SaveScene(boot, "Assets/Scenes/Boot.unity");
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/Scenes/Boot.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/First.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true)
        };
        AssetDatabase.SaveAssets();
    }

    static void Target(string name, int prefabIndex)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("PlainA").AddComponent<SceneLoadItem>();
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Item.prefab"));
        new GameObject("PlainB").AddComponent<SceneLoadItem>();
        root.transform.SetSiblingIndex(prefabIndex);
        var inactive = new GameObject("Inactive");
        inactive.AddComponent<SceneLoadItem>();
        inactive.SetActive(false);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + name + ".unity");
    }
}
