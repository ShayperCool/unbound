using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
#pragma warning disable 618 // the legacy bake and OffMeshLink are what this probe measures

/// <summary>
/// <c>Unity -batchmode -quit -projectPath tests/projects/navbaked -executeMethod ProbeSetup.Create</c>:
/// makes the probe's two scenes and bakes each one's NavMesh the legacy way
/// (UnityEditor.AI.NavMeshBuilder.BuildNavMesh, as the Navigation window does), which writes
/// <c>Assets/Scenes/&lt;Scene&gt;/NavMesh.asset</c> and references it from the scene's
/// NavMeshSettings. Baked: a ground with a Mud strip (area 4), a 0.3 m step, a 1.5 m platform
/// reached by a one-way OffMeshLink and left by the bake's generated drop links, an agent and the
/// probe. Second (loaded additively by the probe): a ground 2 m beyond the first one's, an agent.
/// <c>-executeMethod ProbeSetup.CreateJump</c> makes only the third scene, Jump (loaded additively
/// last): grounds 2 m apart, level and with the far one 0.5 m higher, baked with a 3 m jump
/// distance so the bake generates jump-across links.
/// </summary>
public static class ProbeSetup
{
    public static void Create()
    {
        Make("Assets/Scenes/Baked.unity", true);
        Make("Assets/Scenes/Second.unity", false);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Baked.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true),
        };
        AssetDatabase.SaveAssets();
    }

    public static void CreateJump()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var settings = new SerializedObject(UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject);
        settings.FindProperty("m_BuildSettings.ledgeDropHeight").floatValue = 2f;
        settings.FindProperty("m_BuildSettings.maxJumpAcrossDistance").floatValue = 3f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        Box("LevelA", new Vector3(-4f, -0.5f, 30f), new Vector3(6f, 1f, 6f), 0, true);
        Box("LevelB", new Vector3(4f, -0.5f, 30f), new Vector3(6f, 1f, 6f), 0, true);
        Box("LowC", new Vector3(-4f, -0.5f, 40f), new Vector3(6f, 1f, 6f), 0, true);
        Box("HighD", new Vector3(4f, 0f, 40f), new Vector3(6f, 1f, 6f), 0, true);
        const string path = "Assets/Scenes/Jump.unity";
        EditorSceneManager.SaveScene(scene, path);
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
        EditorSceneManager.SaveScene(scene, path);
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Baked.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true),
            new EditorBuildSettingsScene(path, true),
        };
        AssetDatabase.SaveAssets();
    }

    static GameObject Box(string name, Vector3 center, Vector3 size, int area, bool generateLinks)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = center;
        go.transform.localScale = size;
        var flags = StaticEditorFlags.NavigationStatic | (generateLinks ? StaticEditorFlags.OffMeshLinkGeneration : 0);
        GameObjectUtility.SetStaticEditorFlags(go, flags);
        GameObjectUtility.SetNavMeshArea(go, area);
        return go;
    }

    static void Agent(string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.AddComponent<NavMeshAgent>();
    }

    static void Make(string path, bool first)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var settings = new SerializedObject(UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject);
        settings.FindProperty("m_BuildSettings.ledgeDropHeight").floatValue = 2f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        if (first)
        {
            Box("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 12f), 0, true);
            Box("Mud", new Vector3(0f, 0f, -1f), new Vector3(4f, 0.04f, 10f), 4, false);
            Box("Step", new Vector3(7f, 0.15f, 3.5f), new Vector3(4f, 0.3f, 3f), 0, false);
            Box("Platform", new Vector3(-7f, 0.75f, 4f), new Vector3(4f, 1.5f, 3f), 0, true);
            var link = new GameObject("Link");
            var start = new GameObject("LinkStart").transform;
            var end = new GameObject("LinkEnd").transform;
            start.position = new Vector3(-7f, 0f, 1.2f);
            end.position = new Vector3(-7f, 1.5f, 3.5f);
            start.SetParent(link.transform, true);
            end.SetParent(link.transform, true);
            var offMeshLink = link.AddComponent<OffMeshLink>();
            offMeshLink.startTransform = start;
            offMeshLink.endTransform = end;
            offMeshLink.biDirectional = false;
            Agent("Agent", new Vector3(-6f, 0f, -3f));
            new GameObject("Probe").AddComponent<BakedProbe>();
        }
        else
        {
            Box("Ground2", new Vector3(17f, -0.5f, 0f), new Vector3(10f, 1f, 10f), 0, false);
            Agent("Agent2", new Vector3(17f, 0f, 2f));
        }
        EditorSceneManager.SaveScene(scene, path);
        UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
        EditorSceneManager.SaveScene(scene, path);
    }
}
