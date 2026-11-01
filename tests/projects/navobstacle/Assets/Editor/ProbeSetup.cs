using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates the probe's level (once: <c>-executeMethod ProbeSetup.Create -quit</c>): one flat
/// 16 m square platform (top at y = 0) per lane of <see cref="ObstacleProbe"/>, 30 m apart, and bakes
/// its NavMesh. The scene and the NavMeshData are committed; obstacles and agents are added by the
/// probe at run time.
/// </summary>
public static class ProbeSetup
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var level = new GameObject("Level").transform;
        for (int i = 0; i < ObstacleProbe.LaneCount; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Cell " + i;
            go.transform.SetParent(level, false);
            go.transform.localPosition = ObstacleProbe.Origin(i) + new Vector3(0f, -0.5f, 0f);
            go.transform.localScale = new Vector3(16f, 1f, 16f);
        }

        var surface = level.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
        surface.BuildNavMesh();
        AssetDatabase.CreateAsset(surface.navMeshData, "Assets/Scenes/NavMesh.asset");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
