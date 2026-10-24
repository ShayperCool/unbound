using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh scenarios, run identically in the Unity Editor (the reference) and in Unbound when
/// <c>NAVMESH_PROBE_OUT</c> names the JSON file to write: the baked NavMesh's triangulation, queries
/// (samples, raycasts, edges, paths with area costs and masks), runtime builds, and agents moved
/// frame by frame at a fixed 0.02 s step (paths, avoidance, an off-mesh link, a carving obstacle).
/// <c>NAVMESH_PROBE_ONLY</c> limits the run to scenarios whose name starts with it.
/// </summary>
public class NavProbe : MonoBehaviour
{
    const int Frames = 250;
    static string Only => Environment.GetEnvironmentVariable("NAVMESH_PROBE_ONLY") ?? "";
    readonly List<(string Name, NavMeshAgent Agent)> m_Agents = new List<(string, NavMeshAgent)>();
    int m_Frame;
    NavMeshObstacle m_Carver;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVMESH_PROBE_OUT"))) return;
        new GameObject("NavProbe").AddComponent<NavProbe>();
    }

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

    static void Scenario(string name, Action body)
    {
        if (!name.StartsWith(Only)) return;
        ProbeLog.Begin(name);
        try { body(); }
        catch (Exception e)
        {
            ProbeLog.Error(e);
            Debug.LogException(e);
        }
    }

    void Start()
    {
        Time.captureDeltaTime = 0.02f;
        Scenario("settings", Settings);
        Scenario("baked", () => Triangulation("baked"));
        Scenario("sample", Sample);
        Scenario("raycast", Raycasts);
        Scenario("edges", Edges);
        Scenario("paths", Paths);
        Scenario("build.sources", BuildFromSources);
        Scenario("build.surface", BuildSurface);
        if ("agents".StartsWith(Only) || Only.StartsWith("agents")) StartAgents();
        else Finish();
    }

    // ------------------------------------------------------------------ static scenarios

    static void Settings()
    {
        ProbeLog.Value("count", NavMesh.GetSettingsCount());
        var s = NavMesh.GetSettingsByIndex(0);
        ProbeLog.Value("agentTypeID", s.agentTypeID);
        ProbeLog.Value("agentRadius", s.agentRadius);
        ProbeLog.Value("agentHeight", s.agentHeight);
        ProbeLog.Value("agentSlope", s.agentSlope);
        ProbeLog.Value("agentClimb", s.agentClimb);
        ProbeLog.Value("voxelSize", s.voxelSize);
        ProbeLog.Value("tileSize", s.tileSize);
        ProbeLog.Value("minRegionArea", s.minRegionArea);
        ProbeLog.Value("name", NavMesh.GetSettingsNameFromID(s.agentTypeID));
        ProbeLog.Value("water", NavMesh.GetAreaFromName("Water"));
        ProbeLog.Value("waterCost", NavMesh.GetAreaCost(3));
        ProbeLog.Value("avoidancePredictionTime", NavMesh.avoidancePredictionTime);
        ProbeLog.Value("pathfindingIterationsPerFrame", NavMesh.pathfindingIterationsPerFrame);
    }

    static void Triangulation(string key)
    {
        var t = NavMesh.CalculateTriangulation();
        ProbeLog.Value(key + ".vertices", t.vertices.Length);
        ProbeLog.Value(key + ".indices", t.indices.Length);
        var triangles = new List<string>();
        for (int i = 0; i + 2 < t.indices.Length; i += 3)
        {
            var corners = new[] { t.vertices[t.indices[i]], t.vertices[t.indices[i + 1]], t.vertices[t.indices[i + 2]] }.Select(V).ToList();
            corners.Sort(string.CompareOrdinal);
            triangles.Add(string.Join(" ", corners) + " a" + t.areas[i / 3]);
        }
        triangles.Sort(string.CompareOrdinal);
        for (int i = 0; i < triangles.Count; i++) ProbeLog.Value(key + ".tri" + i, triangles[i]);
        float area = 0f;
        for (int i = 0; i + 2 < t.indices.Length; i += 3)
            area += Vector3.Cross(t.vertices[t.indices[i + 1]] - t.vertices[t.indices[i]], t.vertices[t.indices[i + 2]] - t.vertices[t.indices[i]]).magnitude * 0.5f;
        ProbeLog.Value(key + ".area", area);
    }

    static void Sample()
    {
        for (float z = -9f; z <= 9f; z += 1.5f)
        for (float x = -9f; x <= 17f; x += 1.5f)
        {
            var from = new Vector3(x, 2f, z);
            bool ok = NavMesh.SamplePosition(from, out var hit, 2.5f, NavMesh.AllAreas);
            ProbeLog.Value("s" + F(x) + "," + F(z), ok ? V(hit.position) + " d" + F(hit.distance) + " m" + hit.mask : "none");
        }
        bool water = NavMesh.SamplePosition(new Vector3(0f, 0f, 6f), out var w, 1f, 1 << 3);
        ProbeLog.Value("waterOnly", water ? V(w.position) + " m" + w.mask : "none");
        bool notWater = NavMesh.SamplePosition(new Vector3(0f, 0f, 6f), out var nw, 3f, ~(1 << 3));
        ProbeLog.Value("notWater", notWater ? V(nw.position) + " m" + nw.mask : "none");
    }

    static void Raycasts()
    {
        var pairs = new[]
        {
            (new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f)),
            (new Vector3(0f, 0f, -8f), new Vector3(0f, 0f, 8f)),
            (new Vector3(-8f, 0f, 5f), new Vector3(8f, 0f, 5f)),
            (new Vector3(5f, 0f, -8f), new Vector3(5f, 0f, 8f)),
            (new Vector3(8f, 0f, 0f), new Vector3(15f, 0f, 0f)),
        };
        foreach (var (a, b) in pairs)
        {
            bool blocked = NavMesh.Raycast(a, b, out var hit, NavMesh.AllAreas);
            ProbeLog.Value(V(a) + ">" + V(b), (blocked ? "blocked " : "clear ") + V(hit.position) + " n" + V(hit.normal) + " d" + F(hit.distance) + " m" + hit.mask);
        }
    }

    static void Edges()
    {
        foreach (var p in new[] { new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0f, 1f), new Vector3(-4f, 0f, 2f), new Vector3(8f, 0f, 8f), new Vector3(-6f, 1.5f, -6f) })
        {
            bool ok = NavMesh.FindClosestEdge(p, out var hit, NavMesh.AllAreas);
            ProbeLog.Value(V(p), ok ? V(hit.position) + " n" + V(hit.normal) + " d" + F(hit.distance) : "none");
        }
    }

    static void Path(string key, Vector3 a, Vector3 b, int mask = NavMesh.AllAreas)
    {
        var path = new NavMeshPath();
        bool ok = NavMesh.CalculatePath(a, b, mask, path);
        ProbeLog.Value(key + ".ok", ok);
        ProbeLog.Value(key + ".status", path.status.ToString());
        ProbeLog.Value(key + ".corners", string.Join(" ", path.corners.Select(V)));
    }

    static void Paths()
    {
        Path("diagonal", new Vector3(-8f, 0f, -8f), new Vector3(8f, 0f, 8f));
        Path("aroundObstacle", new Vector3(0f, 0f, 0f), new Vector3(6f, 0f, 0.5f));
        Path("platform", new Vector3(2f, 0f, -6f), new Vector3(-6f, 1.5f, -6f));
        Path("step", new Vector3(8f, 0f, -8f), new Vector3(6f, 0.3f, -6f));
        Path("island", new Vector3(0f, 0f, 0f), new Vector3(16f, 0f, 1f));
        Path("throughWater", new Vector3(0f, 0f, 3.5f), new Vector3(0f, 0f, 8.5f));
        Path("noWater", new Vector3(0f, 0f, 3.5f), new Vector3(0f, 0f, 8.5f), ~(1 << 3));
        NavMesh.SetAreaCost(3, 20f);
        Path("expensiveWater", new Vector3(0f, 0f, 3.5f), new Vector3(0f, 0f, 8.5f));
        NavMesh.SetAreaCost(3, 3f);
        Path("mud", new Vector3(3f, 0f, 3f), new Vector3(7f, 0f, 7f));
        Path("behindWall", new Vector3(-4f, 0f, 1f), new Vector3(-4f, 0f, 5f));
        Path("unreachable", new Vector3(0f, 0f, 0f), new Vector3(3f, 0.5f, 0f));
        Path("offMesh", new Vector3(0f, 0f, 0f), new Vector3(30f, 0f, 30f));
    }

    static void BuildFromSources()
    {
        var surface = FindFirstObjectByType<NavMeshSurface>();
        surface.RemoveData();
        var sources = new List<NavMeshBuildSource>();
        var bounds = new Bounds(new Vector3(4f, 0f, 0f), new Vector3(30f, 10f, 24f));
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.RenderMeshes, 0, new List<NavMeshBuildMarkup>(), sources);
        ProbeLog.Value("sources", sources.Count);
        var kinds = sources.Select(s => s.shape + " a" + s.area + " " + (s.component != null ? s.component.name : "-")).ToList();
        kinds.Sort(string.CompareOrdinal);
        for (int i = 0; i < kinds.Count; i++) ProbeLog.Value("source" + i, kinds[i]);
        // A box and a mesh given directly.
        sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(4f, 1f, 4f), transform = Matrix4x4.Translate(new Vector3(-6f, 0f, 6f)), area = 0 });
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
        ProbeLog.Value("dataBounds", V(data.sourceBounds.center) + " " + V(data.sourceBounds.size));
        var instance = NavMesh.AddNavMeshData(data);
        ProbeLog.Value("valid", instance.valid);
        Triangulation("built");
        NavMesh.RemoveNavMeshData(instance);
        Triangulation("removed");
        surface.AddData();
    }

    static void BuildSurface()
    {
        var surface = FindFirstObjectByType<NavMeshSurface>();
        var baked = surface.navMeshData;
        surface.BuildNavMesh();
        Triangulation("surface");
        // The agents walk the baked NavMesh, the same data in both engines.
        surface.RemoveData();
        surface.navMeshData = baked;
        surface.AddData();
    }

    // ------------------------------------------------------------------ agents over frames

    NavMeshAgent Agent(string name, Vector3 position, Vector3 destination, float speed = 3.5f, int priority = 50)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var agent = go.AddComponent<NavMeshAgent>();
        agent.speed = speed;
        agent.avoidancePriority = priority;
        agent.SetDestination(destination);
        m_Agents.Add((name, agent));
        return agent;
    }

    void StartAgents()
    {
        Agent("walker", new Vector3(-8f, 0f, -8f), new Vector3(8f, 0f, 8f));
        Agent("left", new Vector3(-6f, 0f, -2.5f), new Vector3(6f, 0f, -2.5f), 3f, 40);
        Agent("right", new Vector3(6f, 0f, -2.3f), new Vector3(-6f, 0f, -2.3f), 3f, 60);
        Agent("linker", new Vector3(7f, 0f, 0.5f), new Vector3(16f, 0f, 0.5f));
        Agent("climber", new Vector3(3f, 0f, -6f), new Vector3(-6f, 1.5f, -6f), 2.5f);
        var blocked = Agent("blocked", new Vector3(-8f, 0f, 7.5f), new Vector3(8f, 0f, 7.5f), 2f);
        blocked.acceleration = 4f;
        blocked.angularSpeed = 360f;
        ProbeLog.Begin("agents.setup");
        foreach (var (name, agent) in m_Agents)
        {
            ProbeLog.Value(name + ".pathPending", agent.pathPending);
            ProbeLog.Value(name + ".hasPath", agent.hasPath);
            ProbeLog.Value(name + ".isOnNavMesh", agent.isOnNavMesh);
            ProbeLog.Value(name + ".position", V(agent.transform.position));
            ProbeLog.Value(name + ".nextPosition", V(agent.nextPosition));
            ProbeLog.Value(name + ".radius", agent.radius);
            ProbeLog.Value(name + ".height", agent.height);
            ProbeLog.Value(name + ".baseOffset", agent.baseOffset);
            ProbeLog.Value(name + ".stoppingDistance", agent.stoppingDistance);
        }
    }

    void LateUpdate()
    {
        if (m_Agents.Count == 0) return;
        m_Frame++;
        if (m_Frame == 60)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Carver";
            go.transform.position = new Vector3(2f, 0.5f, 7.5f);
            go.transform.localScale = new Vector3(1f, 1f, 3f);
            m_Carver = go.AddComponent<NavMeshObstacle>();
            m_Carver.carving = true;
            m_Carver.carveOnlyStationary = false;
        }
        ProbeLog.Begin("agents.f" + m_Frame);
        ProbeLog.Value("dt", Time.deltaTime);
        if (m_Frame <= 3)
        {
            ProbeLog.Value("time", Time.time);
            ProbeLog.Value("smoothDt", Time.smoothDeltaTime);
        }
        foreach (var (name, agent) in m_Agents)
        {
            ProbeLog.Value(name, V(agent.transform.position) + " v" + V(agent.velocity) + " r" + F(agent.remainingDistance)
                + (agent.hasPath ? " path" : "") + (agent.pathPending ? " pending" : "") + (agent.isOnOffMeshLink ? " link" : "")
                + " " + agent.pathStatus + " c" + agent.path.corners.Length);
        }
        // NAVMESH_PROBE_STEER=<frames>: each agent's steering in full precision over the first
        // frames, for working on the agents (not part of the reference).
        if (int.TryParse(Environment.GetEnvironmentVariable("NAVMESH_PROBE_STEER"), out int steer) && m_Frame <= steer)
        {
            foreach (var (name, agent) in m_Agents)
                ProbeLog.Value(name + ".steer", "p" + P(agent.transform.position) + " v" + P(agent.velocity) + " dv" + P(agent.desiredVelocity)
                    + " st" + P(agent.steeringTarget) + " n" + P(agent.nextPosition) + " r" + agent.remainingDistance.ToString("R", CultureInfo.InvariantCulture));
        }
        if (m_Frame >= Frames) Finish();
    }

    static string P(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture)
        + "," + v.z.ToString("R", CultureInfo.InvariantCulture);

    void Finish()
    {
        File.WriteAllText(Environment.GetEnvironmentVariable("NAVMESH_PROBE_OUT"), ProbeLog.Json());
        Debug.Log("NavMesh probe written");
        enabled = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
