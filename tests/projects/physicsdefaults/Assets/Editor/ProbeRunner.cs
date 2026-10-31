using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (bool active in new[] { true, false })
        {
            var go = new GameObject(active ? "SceneActive" : "SceneInactive");
            go.SetActive(active);
            var rb = go.AddComponent<Rigidbody>();
            rb.solverIterations = 23; rb.solverVelocityIterations = 8;
            rb.sleepThreshold = .125f; rb.maxAngularVelocity = 17; rb.maxDepenetrationVelocity = 7;
            go.AddComponent<BoxCollider>().contactOffset = .125f;
        }
        var prefab = new GameObject("Prefab");
        prefab.SetActive(false);
        var prb = prefab.AddComponent<Rigidbody>();
        prb.solverIterations = 27; prb.solverVelocityIterations = 9;
        prb.sleepThreshold = .25f; prb.maxAngularVelocity = 19; prb.maxDepenetrationVelocity = 9;
        prefab.AddComponent<BoxCollider>().contactOffset = .25f;
        PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Resources/Body.prefab");
        Object.DestroyImmediate(prefab);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
