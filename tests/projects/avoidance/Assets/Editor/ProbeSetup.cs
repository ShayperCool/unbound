using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates the probe's level, a flat 1000 m square, and bakes its NavMesh (run once:
/// <c>-executeMethod ProbeSetup.Create -quit</c>); the scene and the NavMeshData are committed.
/// </summary>
public static class ProbeSetup
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var level = new GameObject("Level");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(level.transform, false);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        var surface = level.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();
        AssetDatabase.CreateAsset(surface.navMeshData, "Assets/Scenes/NavMesh.asset");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
        CreateWalls();
    }

    /// <summary>The same plane with a wall in each of the probe's 80 m cells: 20 m along x,
    /// 1 m thick, centred 3 m ahead (+z) of the cell's origin (its walkable edge at z = 2).</summary>
    public static void CreateWalls()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var level = new GameObject("Level");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(level.transform, false);
        ground.transform.localScale = new Vector3(100f, 1f, 100f);
        for (int slot = 0; slot < 100; slot++)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall" + slot;
            wall.transform.SetParent(level.transform, false);
            wall.transform.localPosition = new Vector3((slot % 10) * 80f - 400f, 1f, (slot / 10) * 80f - 400f + 3f);
            wall.transform.localScale = new Vector3(20f, 2f, 1f);
        }
        var surface = level.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();
        AssetDatabase.CreateAsset(surface.navMeshData, "Assets/Scenes/WallsNavMesh.asset");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Walls.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true), new EditorBuildSettingsScene("Assets/Scenes/Walls.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
