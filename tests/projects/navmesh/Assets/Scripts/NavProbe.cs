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
    NavMeshObstacle m_Parked;
    string m_UpdateHoles;
    // A carving obstacle with the default settings (carveOnlyStationary, 0.5 s to stationary,
    // 0.1 m threshold), away from the agents: carved once stationary, moved below and above the
    // threshold, then its carving turned off.
    static readonly Vector3 CarverAt = new Vector3(2f, 0.5f, 7.5f);
    static readonly Vector3 ParkedAt = new Vector3(-8.5f, 0.5f, 5f);
    static readonly Vector3 ParkedMoved = new Vector3(-7.25f, 0.5f, 5f);

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
        BuildBoundsControls();
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

    static void BuildBoundsControls()
    {
        foreach (string kind in new[] { "padded", "tight", "clipped", "translated", "modifier", "degenerate", "translated.negative", "translated.swap", "translated.elevated" })
            Scenario("build.bounds." + kind, () => BuildBoundsControl(kind));
    }

    static void BuildBoundsControl(string kind)
    {
        var surface = FindFirstObjectByType<NavMeshSurface>();
        surface.RemoveData();
        var offset = kind == "translated" ? new Vector3(1.37f, 0f, -2.19f)
            : kind == "translated.negative" ? new Vector3(-1.37f, 0f, 2.19f)
            : kind == "translated.swap" ? new Vector3(-2.19f, 0f, 1.37f)
            : kind == "translated.elevated" ? new Vector3(1.37f, 2.4f, -2.19f)
            : Vector3.zero;
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(12f, 0.4f, 10f), transform = Matrix4x4.Translate(offset + new Vector3(-2f, -0.2f, -1f)), area = 0 },
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(2f, 3f, 4f), transform = Matrix4x4.TRS(offset + new Vector3(-2f, 1.5f, -1f), Quaternion.Euler(0f, 23f, 0f), Vector3.one), area = 0 },
        };
        Mesh mesh = null;
        if (kind == "modifier")
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, size = new Vector3(18.1f, 1f, 16.1f), transform = Matrix4x4.Translate(new Vector3(-2f, 0.5f, -1f)), area = 0 });
        if (kind == "degenerate")
        {
            mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-2f, 0f, -1f), new Vector3(-2f, 0f, -1f), new Vector3(-2f, 0f, -1f) };
            mesh.triangles = new[] { 0, 1, 2 };
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = Matrix4x4.identity, area = 0 });
        }
        var bounds = new Bounds(offset + new Vector3(-2f, 0f, -1f), kind == "tight" ? new Vector3(12f, 8f, 10f) : kind == "clipped" ? new Vector3(7f, 8f, 6f) : new Vector3(24f, 8f, 20f));
        var settings = NavMesh.GetSettingsByID(0);
        settings.overrideTileSize = true;
        settings.tileSize = 64;
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        var instance = NavMesh.AddNavMeshData(data);
        try { Triangulation("mesh"); }
        finally
        {
            NavMesh.RemoveNavMeshData(instance);
            DestroyImmediate(data);
            if (mesh != null) DestroyImmediate(mesh);
            surface.AddData();
        }
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

    /// <summary>Whether the NavMesh is there (not carved away) at the carving obstacles' places:
    /// the carver, the parked obstacle's first and second place.</summary>
    static string Holes()
    {
        static char Open(Vector3 p) => NavMesh.SamplePosition(new Vector3(p.x, 0.054f, p.z), out _, 0.05f, NavMesh.AllAreas) ? '1' : '0';
        return new string(new[] { Open(CarverAt), Open(ParkedAt), Open(ParkedMoved) });
    }

    /// <summary>A carved hole's outline: rays from 16 directions around <paramref name="center"/>
    /// towards it stop at its edge; then the NavMesh vertices within <paramref name="reach"/>.</summary>
    static void HoleShape(string name, Vector3 center, float reach)
    {
        ProbeLog.Begin(name);
        center.y = 0.054f;
        for (int i = 0; i < 16; i++)
        {
            float a = i * 22.5f * Mathf.Deg2Rad;
            var from = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * reach;
            if (!NavMesh.SamplePosition(from, out var start, 0.3f, NavMesh.AllAreas))
            {
                ProbeLog.Value("ray" + i, "off");
                continue;
            }
            bool hit = NavMesh.Raycast(start.position, center, out var h, NavMesh.AllAreas);
            ProbeLog.Value("ray" + i, V(start.position) + (hit ? " hit " : " clear ") + V(h.position) + " n" + V(h.normal));
        }
        var t = NavMesh.CalculateTriangulation();
        var near = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var v in t.vertices)
            if (Mathf.Abs(v.x - center.x) <= reach && Mathf.Abs(v.z - center.z) <= reach) near.Add(V(v));
        ProbeLog.Value("vertices", string.Join(" ", near));
    }

    void Update()
    {
        if (m_Agents.Count == 0) return;
        m_UpdateHoles = Holes();
    }

    void LateUpdate()
    {
        if (m_Agents.Count == 0) return;
        m_Frame++;
        if (m_Frame == 60)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Carver";
            go.transform.position = CarverAt;
            go.transform.localScale = new Vector3(1f, 1f, 3f);
            m_Carver = go.AddComponent<NavMeshObstacle>();
            m_Carver.carving = true;
            m_Carver.carveOnlyStationary = false;
        }
        if (m_Frame == 70) HoleShape("carve.carver", CarverAt, 2.6f);
        if (m_Frame == 100)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Parked";
            go.transform.position = ParkedAt;
            go.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            m_Parked = go.AddComponent<NavMeshObstacle>();
            m_Parked.carving = true;
        }
        if (m_Frame == 150) HoleShape("carve.parked", ParkedAt, 1.6f);
        // Below the move threshold, then well above it.
        if (m_Frame == 160) m_Parked.transform.position = ParkedAt + new Vector3(0.05f, 0f, 0f);
        if (m_Frame == 180) m_Parked.transform.position = ParkedMoved;
        if (m_Frame == 230) m_Parked.carving = false;
        ProbeLog.Begin("agents.f" + m_Frame);
        ProbeLog.Value("dt", Time.deltaTime);
        if (m_Frame >= 58) ProbeLog.Value("holes", "u" + m_UpdateHoles + " l" + Holes());
        if (m_Frame <= 3)
        {
            ProbeLog.Value("time", Time.time);
            ProbeLog.Value("smoothDt", Time.smoothDeltaTime);
        }
        foreach (var (name, agent) in m_Agents)
        {
            ProbeLog.Value(name, V(agent.transform.position) + " v" + V(agent.velocity) + " r" + F(agent.remainingDistance)
                + (agent.hasPath ? " path" : "") + (agent.pathPending ? " pending" : "") + (agent.isOnOffMeshLink ? " link" : "")
                + (agent.isPathStale ? " stale" : "") + " " + agent.pathStatus + " c" + agent.path.corners.Length);
        }
        if (m_Frame == 62 || m_Frame == 100 || m_Frame == 200)
            ProbeLog.Value("blocked.corners", string.Join(" ", m_Agents[5].Agent.path.corners.Select(V)));
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
