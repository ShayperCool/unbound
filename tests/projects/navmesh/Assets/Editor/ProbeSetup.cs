using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates the probe's level and bakes its NavMesh (run once in the Editor:
/// <c>-executeMethod ProbeSetup.Create -quit</c>); the scene and the baked NavMeshData are
/// committed, so the Editor and Unbound load the same data.
/// </summary>
public static class ProbeSetup
{
    static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        return go;
    }

    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var level = new GameObject("Level").transform;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(level, false);
        ground.transform.localScale = new Vector3(2f, 1f, 2f);

        Box(level, "Obstacle", new Vector3(3f, 0.5f, 0f), new Vector3(2f, 1f, 4f));
        Box(level, "Wall", new Vector3(-4f, 1f, 3f), new Vector3(6f, 2f, 0.5f));
        Box(level, "Platform", new Vector3(-6f, 0.75f, -6f), new Vector3(4f, 1.5f, 4f));
        Box(level, "Ramp", new Vector3(-2.3f, 0.65f, -6f), new Vector3(4.4f, 0.2f, 2f), new Vector3(0f, 0f, -20f));
        Box(level, "Step", new Vector3(6f, 0.15f, -6f), new Vector3(2f, 0.3f, 2f));
        Box(level, "Pillar", new Vector3(-1f, 1f, -1.5f), new Vector3(0.8f, 2f, 0.8f), new Vector3(0f, 30f, 0f));

        var island = GameObject.CreatePrimitive(PrimitiveType.Plane);
        island.name = "Island";
        island.transform.SetParent(level, false);
        island.transform.localPosition = new Vector3(16f, 0f, 0f);
        island.transform.localScale = new Vector3(0.6f, 1f, 0.6f);

        var water = new GameObject("Water").AddComponent<NavMeshModifierVolume>();
        water.transform.SetParent(level, false);
        water.transform.localPosition = new Vector3(0f, 0f, 6f);
        water.size = new Vector3(4f, 2f, 3f);
        water.area = 3;

        var mud = Box(level, "Mud", new Vector3(5f, 0.05f, 5f), new Vector3(3f, 0.1f, 3f));
        var modifier = mud.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = 4;

        var link = new GameObject("Link").AddComponent<NavMeshLink>();
        link.transform.SetParent(level, false);
        link.startPoint = new Vector3(9.5f, 0f, 0f);
        link.endPoint = new Vector3(13.5f, 0f, 0f);
        link.width = 1f;
        link.bidirectional = true;

        var surface = level.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();
        AssetDatabase.CreateAsset(surface.navMeshData, "Assets/Scenes/NavMesh.asset");

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
