using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
#pragma warning disable 618 // the legacy bake is the simplest way to commit a baked NavMesh

/// <summary>
/// <c>Unity -batchmode -quit -projectPath tests/projects/navcost -executeMethod ProbeSetup.Create</c>:
/// makes the probe's scene and bakes its NavMesh the legacy way (UnityEditor.AI.NavMeshBuilder,
/// which writes <c>Assets/Scenes/Cost/NavMesh.asset</c>). Nine 12 m rigs, each on its own ground
/// (all within one NavMesh tile), described by <see cref="Rigs"/>: area strips, baked obstacles,
/// places for carving obstacles, a raycast field and an agent field with scene-placed agents.
/// </summary>
public static class ProbeSetup
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (var rig in Rigs.All)
        {
            Box(rig.Name + "/Ground", rig.Origin + new Vector3(0f, -0.5f, 0f), new Vector3(Rigs.Size, 1f, Rigs.Size), 0f, 0);
            foreach (var patch in rig.Patches)
                Box(rig.Name + "/" + patch.Name, rig.Origin + patch.Center + new Vector3(0f, patch.Height * 0.5f, 0f),
                    patch.Size + new Vector3(0f, patch.Height, 0f), patch.Yaw, patch.Area);
        }
        foreach (var agent in Rigs.SceneAgents)
        {
            var go = new GameObject(agent.Name);
            go.transform.position = agent.Position;
            go.AddComponent<NavMeshAgent>();
        }
        new GameObject("Probe").AddComponent<CostProbe>();
        const string path = "Assets/Scenes/Cost.unity";
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, path);
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
        EditorSceneManager.SaveScene(scene, path);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        AssetDatabase.SaveAssets();
    }

    static void Box(string name, Vector3 center, Vector3 size, float yaw, int area)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
        GameObjectUtility.SetNavMeshArea(go, area);
    }
}
