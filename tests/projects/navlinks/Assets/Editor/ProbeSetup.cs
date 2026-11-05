using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Creates the probe's level (the lanes of <see cref="Lanes"/>) and bakes its NavMesh (once:
/// <c>-executeMethod ProbeSetup.Create -quit</c>); the scene and the NavMeshData are committed.
/// The links of two lanes are serialized in the scene (a NavMeshLink and a legacy OffMeshLink);
/// the others are added by the probe at run time.
/// </summary>
public static class ProbeSetup
{
    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        return go;
    }

    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var level = new GameObject("Level").transform;
        for (int i = 0; i < Lanes.All.Length; i++)
        {
            var (name, kind) = Lanes.All[i];
            var lane = new GameObject("Lane " + name).transform;
            lane.SetParent(level, false);
            lane.localPosition = Lanes.Origin(i);
            float depth = kind == Lanes.Kind.Gap || kind == Lanes.Kind.Drop ? 6f : 10f;
            float hb = Lanes.HeightB(kind);
            Box(lane, "A", new Vector3(-6f, -0.5f, 0f), new Vector3(10f, 1f, depth));
            Box(lane, "B", new Vector3(6f, hb - 0.5f, 0f), new Vector3(10f, 1f, depth));
            if (kind == Lanes.Kind.Bridge) Box(lane, "Bridge", new Vector3(0f, -0.5f, 4f), new Vector3(2.2f, 1f, 2f));
        }

        // The serialized NavMeshLink (lane "serialized"): local points around its own transform.
        {
            var lane = level.Find("Lane serialized");
            var link = new GameObject("Link").AddComponent<NavMeshLink>();
            link.transform.SetParent(lane, false);
            link.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            link.startPoint = new Vector3(-2f, 0f, -0.5f);
            link.endPoint = new Vector3(2f, 0f, -0.5f);
            link.width = 0f;
            link.bidirectional = true;
        }
        // The legacy OffMeshLink between two transforms (lane "legacy").
        {
            var lane = level.Find("Lane legacy");
            var start = new GameObject("LinkStart").transform;
            start.SetParent(lane, false);
            start.localPosition = new Vector3(-2f, 0f, 0f);
            var end = new GameObject("LinkEnd").transform;
            end.SetParent(lane, false);
            end.localPosition = new Vector3(2f, 0f, 0f);
#pragma warning disable CS0618
            var link = lane.gameObject.AddComponent<OffMeshLink>();
            link.startTransform = start;
            link.endTransform = end;
            link.biDirectional = true;
#pragma warning restore CS0618
        }

        var surface = level.gameObject.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.BuildNavMesh();
        AssetDatabase.CreateAsset(surface.navMeshData, "Assets/Scenes/NavMesh.asset");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
