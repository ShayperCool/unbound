using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh path-cost, query and agent-placement scenarios on runtime-built levels, run identically
/// in the Unity Editor (the reference) and in Unbound when <c>NAVPATH_PROBE_OUT</c> names the JSON
/// file to write. <c>NAVPATH_PROBE_ONLY</c> limits the run to scenarios whose name starts with it.
///
/// Every level is a floor of box sources with area boxes cut into it (area 1 = a hole, other
/// areas = costs), so the route a query picks only depends on the NavMesh and on the path search.
/// </summary>
public class PathProbe : MonoBehaviour
{
    const int Water = 3;
    const int Mud = 4;
    const int Blocked = 1;
    static readonly float[] DefaultCosts = { 1f, 1f, 2f, 3f, 2f };

    static string Only => Environment.GetEnvironmentVariable("NAVPATH_PROBE_ONLY") ?? "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVPATH_PROBE_OUT"))) return;
        new GameObject("PathProbe").AddComponent<PathProbe>();
    }

    // ------------------------------------------------------------------ helpers

    struct Part
    {
        public Vector3 Center, Size;
        public int Area;
        public float Yaw;

        public Part(float x, float z, float w, float d, int area, float yaw = 0f)
        {
            Center = new Vector3(x, 0f, z);
            Size = new Vector3(w, 0f, d);
            Area = area;
            Yaw = yaw;
        }
    }

    // Parts sit off the voxel grid (cells of 1/6 m): a box edge exactly on a cell boundary is
    // rasterized differently by the builders and would hide the routes.
    static readonly Vector2 Off = new Vector2(0.037f, 0.053f);

    /// <summary>A flat floor (top at y = 0) of the given half extent, with parts whose top is
    /// level with it (a cost area) or sticks out of it (a hole, area 1).</summary>
    static NavMeshDataInstance Level(float half, params Part[] parts)
    {
        var sources = new List<NavMeshBuildSource>
        {
            Source(new Vector3(0f, -0.05f, 0f), new Vector3(half * 2f, 0.1f, half * 2f), 0, 0f),
        };
        foreach (var p in parts)
        {
            bool hole = p.Area == Blocked;
            float height = hole ? 1f : 0.1f;
            sources.Add(Source(new Vector3(p.Center.x + Off.x, hole ? 0.5f : -0.05f, p.Center.z + Off.y), new Vector3(p.Size.x, height, p.Size.z), p.Area, p.Yaw));
        }
        var bounds = new Bounds(Vector3.zero, new Vector3(half * 2f + 4f, 10f, half * 2f + 4f));
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity);
        var instance = NavMesh.AddNavMeshData(data);
        LogMesh();
        return instance;
    }

    static int s_Levels;

    /// <summary>The NavMesh's polygons (a fan of triangles from CalculateTriangulation is one
    /// polygon), each as its vertices in order, sorted: how a carved hole splits and merges them.</summary>
    static void LogPolygons(string key)
    {
        var t = NavMesh.CalculateTriangulation();
        var polygons = new List<string>();
        var current = new List<int>();
        for (int i = 0; i + 2 < t.indices.Length; i += 3)
        {
            int a = t.indices[i], b = t.indices[i + 1], c = t.indices[i + 2];
            if (current.Count > 0 && current[0] == a && current[current.Count - 1] == b)
            {
                current.Add(c);
                continue;
            }
            if (current.Count > 0) polygons.Add(Poly(t.vertices, current));
            current.Clear();
            current.Add(a);
            current.Add(b);
            current.Add(c);
        }
        if (current.Count > 0) polygons.Add(Poly(t.vertices, current));
        polygons.Sort(string.CompareOrdinal);
        ProbeLog.Value(key + ".count", polygons.Count);
        for (int i = 0; i < polygons.Count; i++) ProbeLog.Value(key + ".p" + i, polygons[i]);
    }

    static string Poly(Vector3[] vertices, List<int> indices)
    {
        var list = new List<string>();
        foreach (int i in indices) list.Add(Name(vertices[i].x) + "," + Name(vertices[i].z));
        // Start at the smallest vertex so the same polygon reads the same.
        int first = 0;
        for (int i = 1; i < list.Count; i++) if (string.CompareOrdinal(list[i], list[first]) < 0) first = i;
        var rotated = new List<string>();
        for (int i = 0; i < list.Count; i++) rotated.Add(list[(first + i) % list.Count]);
        return string.Join(" ", rotated);
    }

    /// <summary>The NavMesh just built: its triangle count, and its vertices (sorted), so a route
    /// that differs can be told from a mesh that differs.</summary>
    static void LogMesh()
    {
        var t = NavMesh.CalculateTriangulation();
        var vertices = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var v in t.vertices)
            vertices.Add(Name(v.x) + "," + Name(v.y) + "," + Name(v.z));
        string key = "mesh" + s_Levels++;
        ProbeLog.Value(key + ".triangles", t.indices.Length / 3);
        ProbeLog.Value(key + ".vertices", string.Join(" ", vertices));
    }

    static NavMeshBuildSource Source(Vector3 center, Vector3 size, int area, float yaw) => new NavMeshBuildSource
    {
        shape = NavMeshBuildSourceShape.Box,
        size = size,
        transform = Matrix4x4.TRS(center, Quaternion.Euler(0f, yaw, 0f), Vector3.one),
        area = area,
    };

    static void ResetCosts()
    {
        for (int i = 0; i < DefaultCosts.Length; i++) NavMesh.SetAreaCost(i, DefaultCosts[i]);
    }

    static string Name(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    static void Path(string key, Vector3 a, Vector3 b, int mask = NavMesh.AllAreas)
    {
        var path = new NavMeshPath();
        bool ok = NavMesh.CalculatePath(a, b, mask, path);
        ProbeLog.Value(key + ".ok", ok);
        ProbeLog.Value(key + ".status", path.status.ToString());
        var corners = path.corners;
        ProbeLog.Value(key + ".n", corners.Length);
        for (int i = 0; i < corners.Length; i++) ProbeLog.Value(key + ".c" + i, corners[i]);
    }

    IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }

    // Agents sampled again in LateUpdate (after the frame's NavMesh update), keyed by prefix.
    readonly List<(string, NavMeshAgent)> m_Late = new List<(string, NavMeshAgent)>();
    int m_LateFrame;

    // Run once at the next LateUpdate, before the agents are sampled there.
    readonly List<Action> m_LateActions = new List<Action>();

    void LateUpdate()
    {
        if (m_LateActions.Count > 0)
        {
            var actions = m_LateActions.ToArray();
            m_LateActions.Clear();
            foreach (var action in actions) action();
        }
        if (m_Late.Count == 0) return;
        m_LateFrame++;
        foreach (var (k, agent) in m_Late) Log(k + ".late" + m_LateFrame, agent);
        if (m_LateFrame >= 3) m_Late.Clear();
    }

    void Watch(IEnumerable<(string, NavMeshAgent)> agents)
    {
        m_Late.Clear();
        m_LateFrame = 0;
        m_Late.AddRange(agents);
    }

    // ------------------------------------------------------------------ run

    IEnumerator Start()
    {
        Time.captureDeltaTime = 0.02f;
        yield return null;
        foreach (var (name, body) in new (string, Func<IEnumerator>)[]
        {
            ("cost.pool", CostPool),
            ("cost.offset", CostOffset),
            ("cost.strips", CostStrips),
            ("cost.mixed", CostMixed),
            ("hole.symmetric", HoleSymmetric),
            ("hole.shapes", HoleShapes),
            ("carve.sides", CarveSides),
            ("carve.mud", CarveMud),
            ("ray.area", RayArea),
            ("snap.enable", SnapEnable),
            ("snap.reach", SnapReach),
            ("snap.reachtype", SnapReachType),
            ("snap.warp", SnapWarp),
            ("snap.destination", SnapDestination),
            ("sync.updatePosition", SyncUpdatePosition),
            ("link.snap", LinkSnap),
            ("link.wide", LinkWide),
            ("link.wideoff", LinkWideOff),
            ("link.repath", LinkRepath),
            ("link.widetile", LinkWideTile),
            ("link.slope", LinkSlope),
            ("carve.shortcut", CarveShortcut),
            ("carve.rules", CarveRules),
        })
        {
            if (!name.StartsWith(Only)) continue;
            ProbeLog.Begin(name);
            var steps = body();
            while (true)
            {
                bool more;
                try { more = steps.MoveNext(); }
                catch (Exception e)
                {
                    ProbeLog.Error(e);
                    Debug.LogException(e);
                    break;
                }
                if (!more) break;
                yield return steps.Current;
            }
            ResetCosts();
            NavMesh.RemoveAllNavMeshData();
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("NAVPATH_PROBE_OUT"), ProbeLog.Json());
        Debug.Log("NavPath probe written");
        enabled = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ path costs

    /// <summary>A water pool in the middle of a wide floor: through it or around it, as its cost
    /// grows (the sides tie at about 1.47).</summary>
    IEnumerator CostPool()
    {
        Level(14f, new Part(0f, 0f, 6f, 8f, Water));
        foreach (float cost in new[] { 1.2f, 1.4f, 1.45f, 1.5f, 1.6f, 2f, 3f, 5f, 20f })
        {
            NavMesh.SetAreaCost(Water, cost);
            string c = "c" + Name(cost);
            Path(c + ".across", new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f));
            Path(c + ".high", new Vector3(-8f, 0f, 1.5f), new Vector3(8f, 0f, 1.5f));
            Path(c + ".low", new Vector3(-8f, 0f, -3f), new Vector3(8f, 0f, 2f));
            Path(c + ".short", new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0.5f));
            Path(c + ".from", new Vector3(0f, 0f, -2f), new Vector3(9f, 0f, 6f));
        }
        yield break;
    }

    /// <summary>Pools of other sizes and places, and a start inside the pool.</summary>
    IEnumerator CostOffset()
    {
        Level(14f, new Part(3f, 2f, 10f, 3f, Water), new Part(-5f, -4f, 2f, 7f, Water));
        foreach (float cost in new[] { 1.5f, 2f, 4f, 10f })
        {
            NavMesh.SetAreaCost(Water, cost);
            string c = "c" + Name(cost);
            Path(c + ".a", new Vector3(-9f, 0f, 2f), new Vector3(10f, 0f, 2f));
            Path(c + ".b", new Vector3(-9f, 0f, -4f), new Vector3(3f, 0f, 6f));
            Path(c + ".c", new Vector3(3f, 0f, 2f), new Vector3(-9f, 0f, 2f));
            Path(c + ".d", new Vector3(-5f, 0f, -4f), new Vector3(8f, 0f, -4f));
            Path(c + ".e", new Vector3(-12f, 0f, -8f), new Vector3(12f, 0f, 6f));
        }
        yield break;
    }

    /// <summary>Full-width strips of two cost areas that cannot be avoided: only where the path
    /// crosses them can change.</summary>
    IEnumerator CostStrips()
    {
        Level(12f, new Part(-4f, 0f, 3f, 24f, Water), new Part(4f, 0f, 2f, 24f, Mud));
        foreach (float cost in new[] { 1.5f, 5f })
        {
            NavMesh.SetAreaCost(Water, cost);
            NavMesh.SetAreaCost(Mud, 7f - cost);
            string c = "c" + Name(cost);
            Path(c + ".a", new Vector3(-9f, 0f, -6f), new Vector3(9f, 0f, 6f));
            Path(c + ".b", new Vector3(-9f, 0f, 6f), new Vector3(9f, 0f, -6f));
            Path(c + ".c", new Vector3(-2f, 0f, 9f), new Vector3(8f, 0f, -9f));
        }
        yield break;
    }

    /// <summary>A pool with a hole in it and a mud strip: costs and a wall together.</summary>
    IEnumerator CostMixed()
    {
        Level(14f, new Part(0f, 0f, 8f, 10f, Water), new Part(0f, 0f, 2f, 3f, Blocked), new Part(-9f, 4f, 2f, 12f, Mud));
        foreach (float cost in new[] { 1.5f, 2.5f, 8f })
        {
            NavMesh.SetAreaCost(Water, cost);
            string c = "c" + Name(cost);
            Path(c + ".a", new Vector3(-12f, 0f, 0f), new Vector3(12f, 0f, 0f));
            Path(c + ".b", new Vector3(-12f, 0f, 9f), new Vector3(12f, 0f, -9f));
            Path(c + ".c", new Vector3(-6f, 0f, -3f), new Vector3(6f, 0f, 3f));
            Path(c + ".d", new Vector3(0f, 0f, -4f), new Vector3(0f, 0f, 4f));
        }
        yield break;
    }

    // ------------------------------------------------------------------ holes

    static readonly (Vector3, Vector3)[] CrossPairs =
    {
        (new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f)),
        (new Vector3(-10f, 0f, -3f), new Vector3(10f, 0f, 3f)),
        (new Vector3(-10f, 0f, 2f), new Vector3(10f, 0f, 2f)),
        (new Vector3(-7f, 0f, 5f), new Vector3(8f, 0f, -6f)),
        (new Vector3(0f, 0f, -9f), new Vector3(0f, 0f, 9f)),
    };

    /// <summary>One box hole moved a little off the line between the two ends: the shorter side
    /// wins, an exact tie keeps one fixed side.</summary>
    IEnumerator HoleSymmetric()
    {
        foreach (float z in new[] { 0f, 0.1f, 0.5f, -0.5f, 2f })
        {
            Level(14f, new Part(0f, z, 2f, 2f, Blocked));
            string k = "z" + Name(z);
            Path(k + ".a", CrossPairs[0].Item1, CrossPairs[0].Item2);
            Path(k + ".b", CrossPairs[1].Item1, CrossPairs[1].Item2);
            NavMesh.RemoveAllNavMeshData();
        }
        foreach (float w in new[] { 1f, 4f, 9f })
        {
            Level(14f, new Part(0f, 0.3f, 2f, w, Blocked));
            string k = "w" + Name(w);
            Path(k + ".a", CrossPairs[0].Item1, CrossPairs[0].Item2);
            Path(k + ".b", CrossPairs[2].Item1, CrossPairs[2].Item2);
            NavMesh.RemoveAllNavMeshData();
        }
        yield break;
    }

    /// <summary>Several holes: a wall with a gap, two boxes, a turned box, an L.</summary>
    IEnumerator HoleShapes()
    {
        var variants = new (string, Part[])[]
        {
            ("wallgap", new[] { new Part(0f, -6f, 2f, 10f, Blocked), new Part(0f, 7f, 2f, 8f, Blocked) }),
            ("two", new[] { new Part(-3f, 1f, 2f, 2f, Blocked), new Part(3f, -1.5f, 3f, 2f, Blocked) }),
            ("turned", new[] { new Part(0f, 0.4f, 5f, 2f, Blocked, 30f) }),
            ("ell", new[] { new Part(0f, 0f, 2f, 8f, Blocked), new Part(2.5f, 3f, 5f, 2f, Blocked) }),
            ("pocket", new[] { new Part(2f, 0f, 6f, 6f, Blocked), new Part(2f, 0f, 2f, 2f, Water) }),
        };
        foreach (var (name, parts) in variants)
        {
            Level(14f, parts);
            for (int i = 0; i < CrossPairs.Length; i++)
                Path(name + ".p" + i, CrossPairs[i].Item1, CrossPairs[i].Item2);
            NavMesh.RemoveAllNavMeshData();
        }
        yield break;
    }

    // ------------------------------------------------------------------ carving

    static NavMeshObstacle Carver(float x, float z, Vector3 size)
    {
        var go = new GameObject("Carver");
        go.transform.position = new Vector3(x, 0.5f, z);
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = size;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        return obstacle;
    }

    /// <summary>A carved box on the line between two ends, moved a little: the side the path
    /// takes round the hole.</summary>
    IEnumerator CarveSides()
    {
        Level(14f);
        yield return Frames(2);
        foreach (var (name, x, z, size) in new[]
        {
            ("a", 2f, 0f, new Vector3(1f, 1f, 3f)),
            ("b", 2f, 0.25f, new Vector3(1f, 1f, 3f)),
            ("c", 2f, 1f, new Vector3(1f, 1f, 3f)),
            ("d", -3f, -0.5f, new Vector3(3f, 1f, 1f)),
            ("e", 0f, 0f, new Vector3(4f, 1f, 4f)),
        })
        {
            var carver = Carver(x, z, size);
            yield return Frames(4);
            LogPolygons("poly" + name);
            Path(name + ".a", new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f));
            Path(name + ".b", new Vector3(-8f, 0f, -2f), new Vector3(8f, 0f, 2.5f));
            Path(name + ".c", new Vector3(8f, 0f, 0.5f), new Vector3(-8f, 0f, 0.5f));
            Path(name + ".d", new Vector3(0f, 0f, -8f), new Vector3(0f, 0f, 8f));
            Destroy(carver.gameObject);
            yield return Frames(4);
        }
    }

    /// <summary>The original blocked-lane setting: a carved box beside a mud strip.</summary>
    IEnumerator CarveMud()
    {
        Level(14f, new Part(3f, 4f, 4f, 3f, Mud));
        yield return Frames(2);
        foreach (float cost in new[] { 1.5f, 3f })
        {
            NavMesh.SetAreaCost(Mud, cost);
            foreach (var (name, z) in new[] { ("a", 7.5f), ("b", 6.5f), ("c", 5.5f), ("d", 4f) })
            {
                var carver = Carver(2f, z, new Vector3(1f, 1f, 3f));
                yield return Frames(4);
                if (cost == 1.5f) LogPolygons("poly" + name);
                string k = "c" + Name(cost) + name;
                Path(k + ".a", new Vector3(-8f, 0f, z), new Vector3(8f, 0f, z));
                Path(k + ".b", new Vector3(-8f, 0f, 9f), new Vector3(8f, 0f, 2f));
                Destroy(carver.gameObject);
                yield return Frames(4);
            }
        }
    }

    // ------------------------------------------------------------------ raycast area masks

    static void Ray(string key, Vector3 a, Vector3 b, int mask)
    {
        bool blocked = NavMesh.Raycast(a, b, out var hit, mask);
        ProbeLog.Value(key + ".blocked", blocked);
        ProbeLog.Value(key + ".position", hit.position);
        ProbeLog.Value(key + ".normal", hit.normal);
        ProbeLog.Value(key + ".distance", hit.distance);
        ProbeLog.Value(key + ".mask", hit.mask);
        ProbeLog.Value(key + ".hit", hit.hit);
    }

    /// <summary>Rays that cross a water strip and a mud strip, with the area masks excluding them,
    /// with a wall and with the mask excluding the start's own area.</summary>
    IEnumerator RayArea()
    {
        Level(12f, new Part(0f, 0f, 3f, 24f, Water), new Part(6f, 0f, 2f, 10f, Mud), new Part(-7f, 2f, 1f, 6f, Blocked));
        int all = NavMesh.AllAreas;
        int noWater = ~(1 << Water);
        int noMud = ~(1 << Mud);
        int onlyWater = 1 << Water;
        int onlyWalkable = 1;
        int walkMud = 1 | (1 << Mud);
        foreach (var (name, mask) in new[] { ("all", all), ("noWater", noWater), ("noMud", noMud), ("onlyWalk", onlyWalkable), ("walkMud", walkMud), ("onlyWater", onlyWater) })
        {
            Ray(name + ".across", new Vector3(-6f, 0f, -3f), new Vector3(10f, 0f, -3f), mask);
            Ray(name + ".mud", new Vector3(2.5f, 0f, 0f), new Vector3(9f, 0f, 1f), mask);
            Ray(name + ".diag", new Vector3(-5f, 0f, -8f), new Vector3(4f, 0f, 5f), mask);
            Ray(name + ".wall", new Vector3(-10f, 0f, 2f), new Vector3(-4f, 0f, 2f), mask);
            Ray(name + ".inside", new Vector3(0f, 0f, 3f), new Vector3(-6f, 0f, 3f), mask);
            Ray(name + ".end", new Vector3(-9f, 0f, -6f), new Vector3(0.5f, 0f, -6f), mask);
            Ray(name + ".short", new Vector3(-1.2f, 0f, 6f), new Vector3(-0.2f, 0f, 6f), mask);
        }
        // A start far from the NavMesh, and one just above an excluded polygon.
        Ray("far", new Vector3(30f, 0f, 0f), new Vector3(0f, 0f, 0f), all);
        Ray("farMasked", new Vector3(-6f, 0f, 20f), new Vector3(-6f, 0f, 0f), noWater);
        // The filter variants take the same masks.
        var filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = noWater };
        bool b = NavMesh.Raycast(new Vector3(-6f, 0f, 6f), new Vector3(10f, 0f, 6f), out var h, filter);
        ProbeLog.Value("filter.blocked", b);
        ProbeLog.Value("filter.position", h.position);
        ProbeLog.Value("filter.mask", h.mask);
        yield break;
    }

    // ------------------------------------------------------------------ agent placement

    static void Log(string key, NavMeshAgent agent)
    {
        ProbeLog.Value(key + ".position", agent.transform.position);
        ProbeLog.Value(key + ".next", agent.nextPosition);
        ProbeLog.Value(key + ".onMesh", agent.isOnNavMesh);
    }

    static readonly float[] Heights = { 0f, 0.4f, 1.2f, 1.9f, -0.3f };

    /// <summary>Agents enabled at heights above and below the floor and beside its edge: where the
    /// transform is right after enabling, then in the next updates.</summary>
    IEnumerator SnapEnable()
    {
        Level(10f, new Part(0f, 0f, 2f, 2f, Water));
        yield return Frames(1);
        var made = new List<(string, NavMeshAgent)>();
        for (int i = 0; i < Heights.Length; i++)
        {
            string k = "h" + Name(Heights[i]);
            var go = new GameObject(k);
            go.transform.position = new Vector3(-6f + i * 2f, Heights[i], -4f);
            var agent = go.AddComponent<NavMeshAgent>();
            Log(k + ".added", agent);
            made.Add((k, agent));
        }
        // Outside the NavMesh's edge, within and beyond the agent radius.
        foreach (float dx in new[] { 0.2f, 0.6f })
        {
            string k = "edge" + Name(dx);
            var go = new GameObject(k);
            go.transform.position = new Vector3(10f + dx, 0.3f, 5f);
            var agent = go.AddComponent<NavMeshAgent>();
            Log(k + ".added", agent);
            made.Add((k, agent));
        }
        // Disabled game objects activated later, and agents toggled.
        {
            var go = new GameObject("inactive");
            go.SetActive(false);
            go.transform.position = new Vector3(4f, 1.2f, -4f);
            var agent = go.AddComponent<NavMeshAgent>();
            go.SetActive(true);
            Log("inactive.activated", agent);
            made.Add(("inactive", agent));
        }
        {
            var go = new GameObject("toggled");
            go.transform.position = new Vector3(6f, 1.2f, -4f);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.enabled = false;
            go.transform.position = new Vector3(6f, 0.9f, -4f);
            agent.enabled = true;
            Log("toggled.enabled", agent);
            made.Add(("toggled", agent));
        }
        {
            var go = new GameObject("offset");
            go.transform.position = new Vector3(8f, 0.8f, -4f);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.baseOffset = 0.5f;
            Log("offset.set", agent);
            made.Add(("offset", agent));
        }
        // Copies of an active and of a disabled template.
        {
            var template = new GameObject("template");
            template.transform.position = new Vector3(-8f, 0.9f, 4f);
            var tAgent = template.AddComponent<NavMeshAgent>();
            var copy = Instantiate(template);
            copy.transform.position = new Vector3(-8f, 0.9f, 6f);
            var cAgent = copy.GetComponent<NavMeshAgent>();
            Log("copy.instantiated", cAgent);
            made.Add(("copy", cAgent));
            var off = new GameObject("offTemplate");
            off.SetActive(false);
            off.AddComponent<NavMeshAgent>();
            off.transform.position = new Vector3(-6f, 0.9f, 6f);
            var copy2 = Instantiate(off);
            Log("copyOff.instantiated", copy2.GetComponent<NavMeshAgent>());
            copy2.SetActive(true);
            Log("copyOff.activated", copy2.GetComponent<NavMeshAgent>());
            made.Add(("copyOff", copy2.GetComponent<NavMeshAgent>()));
            made.Add(("template", tAgent));
        }
        Watch(made);
        for (int f = 1; f <= 3; f++)
        {
            yield return null;
            foreach (var (k, agent) in made) Log(k + ".f" + f, agent);
        }
    }

    /// <summary>How far from the NavMesh an agent can still be placed on it, sideways and in height.</summary>
    IEnumerator SnapReach()
    {
        Level(10f);
        yield return Frames(1);
        int n = 0;
        foreach (float dx in new[] { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 4f, 6f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f, 18f, 20f, 50f })
        {
            var go = new GameObject("side" + n);
            go.transform.position = new Vector3(9.5f + dx, 0f, -4f + (n % 3) * 3f + (n / 3) * 0.1f);
            var agent = go.AddComponent<NavMeshAgent>();
            string k = "side" + Name(dx);
            ProbeLog.Value(k + ".onMesh", agent.isOnNavMesh);
            ProbeLog.Value(k + ".x", agent.nextPosition.x);
            n++;
            Destroy(go);
        }
        n = 0;
        foreach (float dy in new[] { 1f, 2f, 2.5f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 30f, -1f, -2f, -3f, -4f, -5f, -6f, -7f, -8f, -9f, -10f, -30f })
        {
            var go = new GameObject("up" + n);
            go.transform.position = new Vector3(-4f + (n % 4) * 2f, dy, 2f + (n / 4) * 2f);
            var agent = go.AddComponent<NavMeshAgent>();
            string k = "up" + Name(dy);
            ProbeLog.Value(k + ".onMesh", agent.isOnNavMesh);
            ProbeLog.Value(k + ".y", agent.nextPosition.y);
            n++;
            Destroy(go);
        }
        // Warp reaches as far (the same placement).
        var w = new GameObject("w").AddComponent<NavMeshAgent>();
        foreach (float dx in new[] { 1f, 3f, 6f, 9f, 11f, 20f })
        {
            bool ok = w.Warp(new Vector3(9.5f + dx, 0f, 0f));
            ProbeLog.Value("warp" + Name(dx) + ".ok", ok);
            ProbeLog.Value("warp" + Name(dx) + ".onMesh", w.isOnNavMesh);
            ProbeLog.Value("warp" + Name(dx) + ".x", w.nextPosition.x);
        }
        yield break;
    }

    const int BigType = 1479372276;

    /// <summary>The reach again for an agent type with a larger radius and height (project settings
    /// "Big": radius 2, height 4), and for default-type agents whose own radius and height differ:
    /// whether the distance an agent is still placed from the NavMesh scales with the type, the
    /// agent's radius or its height.</summary>
    IEnumerator SnapReachType()
    {
        var big = NavMesh.GetSettingsByID(BigType);
        ProbeLog.Value("big.radius", big.agentRadius);
        ProbeLog.Value("big.height", big.agentHeight);
        var sources = new List<NavMeshBuildSource> { Source(new Vector3(0f, -0.05f, 0f), new Vector3(28f, 0.1f, 28f), 0, 0f) };
        var bounds = new Bounds(Vector3.zero, new Vector3(32f, 10f, 32f));
        NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(big, sources, bounds, Vector3.zero, Quaternion.identity));
        NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, bounds, Vector3.zero, Quaternion.identity));
        yield return Frames(1);
        foreach (var (label, type, radius, height) in new[]
        {
            ("bigtype", BigType, 0.5f, 2f), ("r2h2", 0, 2f, 2f), ("r05h4", 0, 0.5f, 4f), ("r2h4", 0, 2f, 4f), ("r1h2", 0, 1f, 2f),
        })
        {
            int n = 0;
            foreach (float dx in new[] { 0f, 8f, 9f, 10f, 11f, 12f, 19f, 20f, 21f, 22f, 38f, 40f, 42f, 50f })
            {
                var go = new GameObject("side");
                go.SetActive(false);
                go.transform.position = new Vector3(14f + dx, 0f, -8f + (n % 4) * 5f);
                var agent = go.AddComponent<NavMeshAgent>();
                agent.agentTypeID = type;
                agent.radius = radius;
                agent.height = height;
                go.SetActive(true);
                string k = label + ".side" + Name(dx);
                ProbeLog.Value(k + ".onMesh", agent.isOnNavMesh);
                n++;
                Destroy(go);
            }
            n = 0;
            foreach (float dy in new[] { 4f, 7f, 8f, 10f, 14f, 15f, 16f, 20f, 29f, 30f, 31f, 40f, -7f, -8f, -15f, -16f, -30f })
            {
                var go = new GameObject("up");
                go.SetActive(false);
                go.transform.position = new Vector3(-8f + (n % 4) * 5f, dy, 4f + (n / 4) * 3f);
                var agent = go.AddComponent<NavMeshAgent>();
                agent.agentTypeID = type;
                agent.radius = radius;
                agent.height = height;
                go.SetActive(true);
                string k = label + ".up" + Name(dy);
                ProbeLog.Value(k + ".onMesh", agent.isOnNavMesh);
                n++;
                Destroy(go);
            }
        }
    }

    /// <summary>Warp to positions above the floor, off it, and the same position twice.</summary>
    IEnumerator SnapWarp()
    {
        Level(10f);
        yield return Frames(1);
        var go = new GameObject("warp");
        go.transform.position = new Vector3(-8f, 0f, 0f);
        var agent = go.AddComponent<NavMeshAgent>();
        yield return Frames(1);
        int n = 0;
        foreach (var target in new[]
        {
            new Vector3(-4f, 1.0f, 0f), new Vector3(0f, -0.4f, 0f), new Vector3(4f, 0f, 3f), new Vector3(4f, 1.5f, 3f),
            new Vector3(12.3f, 0f, 0f), new Vector3(9.8f, 0.7f, 0f), new Vector3(9.8f, 0.7f, 0f),
        })
        {
            string k = "w" + n++;
            bool ok = agent.Warp(target);
            ProbeLog.Value(k + ".ok", ok);
            Log(k + ".warped", agent);
            Watch(new[] { (k, agent) });
            yield return null;
            Log(k + ".f1", agent);
            yield return null;
            Log(k + ".f2", agent);
        }
        // Warp while the agent has a path, then the next frames.
        agent.Warp(new Vector3(-8f, 1.1f, 0f));
        agent.SetDestination(new Vector3(8f, 0f, 0f));
        yield return null;
        Log("walk.f1", agent);
        agent.Warp(new Vector3(-2f, 0.8f, 1f));
        Log("walk.warped", agent);
        yield return null;
        Log("walk.f2", agent);
        ProbeLog.Value("walk.hasPath", agent.hasPath);
        ProbeLog.Value("walk.pending", agent.pathPending);
    }

    /// <summary>The first SetDestination, a destination with no path, and a moving agent's height:
    /// when the transform moves onto the NavMesh.</summary>
    IEnumerator SnapDestination()
    {
        Level(10f, new Part(0f, 0f, 2f, 6f, Blocked));
        yield return Frames(1);
        var made = new List<(string, NavMeshAgent)>();
        float[] heights = { 0f, 0.8f, 1.5f };
        for (int i = 0; i < heights.Length; i++)
        {
            string k = "h" + Name(heights[i]);
            var go = new GameObject(k);
            go.transform.position = new Vector3(-8f, heights[i], -6f + i * 3f);
            var agent = go.AddComponent<NavMeshAgent>();
            made.Add((k, agent));
            Log(k + ".added", agent);
        }
        yield return null;
        foreach (var (k, agent) in made) Log(k + ".idle1", agent);
        yield return null;
        foreach (var (k, agent) in made) Log(k + ".idle2", agent);
        for (int i = 0; i < made.Count; i++)
        {
            var (k, agent) = made[i];
            bool ok = agent.SetDestination(new Vector3(8f, 0f, -6f + i * 3f));
            ProbeLog.Value(k + ".setDestination", ok);
            Log(k + ".dest", agent);
            ProbeLog.Value(k + ".pending", agent.pathPending);
        }
        for (int f = 1; f <= 6; f++)
        {
            yield return null;
            foreach (var (k, agent) in made) Log(k + ".m" + f, agent);
        }
    }
    // ------------------------------------------------------------------ transform synchronisation

    /// <summary>One standing agent per lane z, at x = -6 on a plain floor.</summary>
    static NavMeshAgent Lane(string name, float z)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(-6f, 0f, z);
        return go.AddComponent<NavMeshAgent>();
    }

    static void Moved(NavMeshAgent agent, float dx, float y = 0f)
    {
        var p = agent.transform.position;
        agent.transform.position = new Vector3(p.x + dx, y, p.z);
    }

    /// <summary>updatePosition turned off, the transform or the simulation moved meanwhile (in
    /// Update or LateUpdate), then turned on again: where the transform and nextPosition are right
    /// after, in that LateUpdate and in the next frame. Then transforms moved by script with
    /// updatePosition on, in Update and in LateUpdate: when they are put on the NavMesh.</summary>
    IEnumerator SyncUpdatePosition()
    {
        Level(12f);
        yield return Frames(1);
        var a = Lane("a", -10f);   // transform moved in Update while off
        var b = Lane("b", -8f);    // transform moved in LateUpdate while off
        var c = Lane("c", -6f);    // transform and nextPosition moved while off
        var d = Lane("d", -4f);    // walking while off, transform untouched
        var e = Lane("e", -2f);    // moved while off, turned on, moved again in the same Update
        var f = Lane("f", 0f);     // moved while on, turned off at once, moved again while off
        var g = Lane("g", 2f);     // on: moved in LateUpdate above the floor
        var h = Lane("h", 4f);     // on: moved in Update above the floor
        var i = Lane("i", 6f);     // off: moved above the floor, then on
        var all = new[] { ("a", a), ("b", b), ("c", c), ("d", d), ("e", e), ("f", f), ("g", g), ("h", h), ("i", i) };
        yield return Frames(2);
        d.SetDestination(new Vector3(10f, 0f, -4f));
        yield return Frames(15);

        // Frame 0: turned off and moved.
        foreach (var x in new[] { a, b, c, d, e, i }) x.updatePosition = false;
        Moved(a, 3f);
        m_LateActions.Add(() => Moved(b, 3f));
        Moved(c, 3f);
        c.nextPosition = new Vector3(-4f, 0f, -6f);
        Moved(e, 3f);
        Moved(f, 3f);
        f.updatePosition = false;
        Moved(f, 2f);
        Moved(h, 2f, 0.8f);
        m_LateActions.Add(() => Moved(g, 2f, 0.8f));
        Moved(i, 2f, 0.8f);
        foreach (var (k, x) in all) Log(k + ".off", x);
        Watch(all);
        yield return null;
        foreach (var (k, x) in all) Log(k + ".f1", x);
        yield return null;
        foreach (var (k, x) in all) Log(k + ".f2", x);

        // Frame 3: turned on again.
        foreach (var x in new[] { a, b, c, d, e, f, i }) x.updatePosition = true;
        foreach (var (k, x) in all) Log(k + ".on", x);
        Moved(e, 1f);
        Log("e.movedOn", e);
        var again = new List<(string, NavMeshAgent)>();
        foreach (var (k, x) in all) again.Add((k + ".on", x));
        Watch(again);
        for (int n = 1; n <= 3; n++)
        {
            yield return null;
            foreach (var (k, x) in all) Log(k + ".after" + n, x);
        }
    }

    // ------------------------------------------------------------------ links over a gap

    /// <summary>The agents earlier scenarios left standing, gone before a link scenario (so its
    /// agents avoid no one).</summary>
    static void ClearAgents()
    {
        foreach (var agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            DestroyImmediate(agent.gameObject);
    }

    /// <summary>The floor (agent type 0, and the big type) cut by a 2 m strip at x = 0 that only
    /// links cross, plus any further parts.</summary>
    static void Gap(int agentType, params Part[] parts)
    {
        var all = new List<Part> { new Part(0f, 0f, 2f, 40f, Blocked) };
        all.AddRange(parts);
        var sources = new List<NavMeshBuildSource>
        {
            Source(new Vector3(0f, -0.05f, 0f), new Vector3(28f, 0.1f, 28f), 0, 0f),
        };
        foreach (var p in all)
            sources.Add(Source(new Vector3(p.Center.x + Off.x, 0.5f, p.Center.z + Off.y), new Vector3(p.Size.x, 1f, p.Size.z), p.Area, p.Yaw));
        var bounds = new Bounds(Vector3.zero, new Vector3(32f, 10f, 32f));
        NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(agentType), sources, bounds, Vector3.zero, Quaternion.identity));
    }

    static NavMeshLinkInstance Link(Vector3 start, Vector3 end, int agentType = 0, float width = 0f)
    {
        var data = new NavMeshLinkData
        {
            startPosition = start,
            endPosition = end,
            width = width,
            bidirectional = true,
            costModifier = -1f,
            area = 0,
            agentTypeID = agentType,
        };
        return NavMesh.AddLink(data);
    }

    static void TypedPath(string key, Vector3 a, Vector3 b, int agentType)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
        var path = new NavMeshPath();
        bool ok = NavMesh.CalculatePath(a, b, filter, path);
        ProbeLog.Value(key + ".ok", ok);
        ProbeLog.Value(key + ".status", path.status.ToString());
        var corners = path.corners;
        ProbeLog.Value(key + ".n", corners.Length);
        for (int i = 0; i < corners.Length; i++) ProbeLog.Value(key + ".c" + i, corners[i]);
    }

    /// <summary>How far from the NavMesh a link's end may lie and still connect: link ends at
    /// distances either side of the agent radius (0.5 m for type 0, 2 m for the big type) in x,
    /// and above the floor either side of the climb (0.75 m), each crossed by a path.</summary>
    IEnumerator LinkSnap()
    {
        ClearAgents();
        yield return null;
        Gap(0);
        Gap(BigType);
        LogMesh();
        var links = new List<NavMeshLinkInstance>();
        // The NavMesh ends at x = -1.5 and 1.667 for type 0, at -3.333 and 3.333 for the big type.
        foreach (var (name, x0, x1, y, z, type) in new[]
        {
            ("r05in", -1.1f, 1.267f, 0f, -10f, 0), ("r05out", -0.95f, 1.117f, 0f, -6f, 0),
            ("climbin", -1.7f, 1.9f, 0.6f, -2f, 0), ("climbout", -1.7f, 1.9f, 0.95f, 2f, 0),
            ("r2in", -1.633f, 1.633f, 0f, 6f, BigType), ("r2out", -1.233f, 1.233f, 0f, 10f, BigType),
        })
        {
            links.Add(Link(new Vector3(x0, y, z), new Vector3(x1, y, z), type));
            yield return Frames(1);
            TypedPath(name, new Vector3(-8f, 0f, z), new Vector3(8f, 0f, z), type);
        }
        foreach (var l in links) NavMesh.RemoveLink(l);
    }

    /// <summary>A 4 m wide link over the gap: paths straight across it at several lateral offsets
    /// (inside it, beyond its width), and agents crossing it (the link's ends they are given and
    /// cross between); a second one whose start lies partly off the NavMesh (a hole beside the
    /// gap covers the start for z above about 6), so a crossing there is held to the part over
    /// the NavMesh.</summary>
    IEnumerator LinkWide()
    {
        ClearAgents();
        yield return null;
        Gap(0, new Part(-2.6f, 11f, 3f, 9f, Blocked));
        LogMesh();
        var a = Link(new Vector3(-1.7f, 0f, -6f), new Vector3(1.7f, 0f, -6f), 0, 4f);
        var b = Link(new Vector3(-1.7f, 0f, 6f), new Vector3(1.7f, 0f, 6f), 0, 4f);
        yield return Frames(1);
        foreach (float dz in new[] { 0f, 1.2f, -1.7f, 3f })
            Path("a" + Name(dz), new Vector3(-8f, 0f, -6f + dz), new Vector3(8f, 0f, -6f + dz));
        foreach (float dz in new[] { -1.2f, 0f, 1.2f })
            Path("b" + Name(dz), new Vector3(-8f, 0f, 6f + dz), new Vector3(8f, 0f, 6f + dz));
        // One agent at a time, so they do not avoid each other.
        foreach (var (name, z) in new[] { ("ga1.2", -4.8f), ("ga3", -3f), ("gb1.2", 7.2f) })
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(-6f, 0f, z);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.SetDestination(new Vector3(6f, 0f, z));
            yield return Frames(2);
            var next = agent.nextOffMeshLinkData;
            ProbeLog.Value(name + ".next.valid", next.valid);
            ProbeLog.Value(name + ".next.start", next.startPos);
            ProbeLog.Value(name + ".next.end", next.endPos);
            for (int f = 0; f < 200 && !agent.isOnOffMeshLink; f++) yield return null;
            var cur = agent.currentOffMeshLinkData;
            ProbeLog.Value(name + ".cur.start", cur.startPos);
            ProbeLog.Value(name + ".cur.end", cur.endPos);
            ProbeLog.Value(name + ".cur.at", agent.nextPosition);
            yield return Frames(120);
            ProbeLog.Value(name + ".end", agent.nextPosition);
            Destroy(go);
            yield return Frames(1);
        }
        NavMesh.RemoveLink(a);
        NavMesh.RemoveLink(b);
    }

    /// <summary>A 4 m wide link whose ends lie 0.2 m off the NavMesh across the gap (inside it), and
    /// a path over the gap: whether a wide end attaches to the NavMesh near it.</summary>
    IEnumerator LinkWideOff()
    {
        ClearAgents();
        yield return null;
        Gap(0);
        LogMesh();
        var link = Link(new Vector3(-1.3f, 0f, 0f), new Vector3(1.467f, 0f, 0f), 0, 4f);
        yield return Frames(1);
        Path("off", new Vector3(-8f, 0f, 0.5f), new Vector3(8f, 0f, 0.5f));
        NavMesh.RemoveLink(link);
    }

    /// <summary>A stale partial path searched again (autoRepath): three agents walk to the gap,
    /// each over its own inactive link; at frame 30 the links open. "near" (autoRepath on) and
    /// "off" (autoRepath off) start 4.5 m from the gap, "far" 10.5 m; each frame logs the path's
    /// status and isPathStale.</summary>
    IEnumerator LinkRepath()
    {
        ClearAgents();
        yield return null;
        Gap(0);
        LogMesh();
        var lanes = new[] { ("near", -8f, true, -6f), ("off", 0f, false, -6f), ("far", 8f, true, -12f) };
        var links = new List<NavMeshLinkInstance>();
        var agents = new List<(string, NavMeshAgent)>();
        foreach (var (name, z, repath, x) in lanes)
        {
            var link = Link(new Vector3(-1.7f, 0f, z), new Vector3(1.9f, 0f, z));
            NavMesh.SetLinkActive(link, false);
            links.Add(link);
            var go = new GameObject(name);
            go.transform.position = new Vector3(x, 0f, z);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.autoRepath = repath;
            agent.SetDestination(new Vector3(8f, 0f, z));
            agents.Add((name, agent));
        }
        for (int f = 1; f <= 160; f++)
        {
            if (f == 30)
                foreach (var link in links) NavMesh.SetLinkActive(link, true);
            yield return null;
            foreach (var (name, agent) in agents)
                ProbeLog.Value(name + ".f" + f, agent.pathStatus + " " + agent.isPathStale);
        }
        foreach (var (_, agent) in agents) Destroy(agent.gameObject);
        foreach (var link in links) NavMesh.RemoveLink(link);
    }

    /// <summary>A 4 m wide link over the gap whose ends cross the NavMesh's tile edge at z = 0 (its
    /// middle at z = 0.3): paths across it either side of the edge, and an agent crossing it below
    /// the edge.</summary>
    IEnumerator LinkWideTile()
    {
        ClearAgents();
        yield return null;
        Gap(0);
        LogMesh();
        var link = Link(new Vector3(-1.7f, 0f, 0.3f), new Vector3(1.7f, 0f, 0.3f), 0, 4f);
        yield return Frames(1);
        foreach (float z in new[] { 1.5f, -1.2f })
            Path("t" + Name(z), new Vector3(-8f, 0f, z), new Vector3(8f, 0f, z));
        var go = new GameObject("tile");
        go.transform.position = new Vector3(-6f, 0f, -1.2f);
        var agent = go.AddComponent<NavMeshAgent>();
        agent.SetDestination(new Vector3(6f, 0f, -1.2f));
        yield return Frames(2);
        var next = agent.nextOffMeshLinkData;
        ProbeLog.Value("agent.next.valid", next.valid);
        ProbeLog.Value("agent.next.start", next.startPos);
        ProbeLog.Value("agent.next.end", next.endPos);
        yield return Frames(200);
        ProbeLog.Value("agent.end", agent.nextPosition);
        Destroy(go);
        NavMesh.RemoveLink(link);
    }

    /// <summary>Whether an agent takes a shortcut that opens away from its path: 5.33 m tiles; a
    /// wall at x = 0 from z = -14 to 9 with a gap at z = -9.5 to -7 that a carving box closes; an
    /// agent from (-12, -12) to (12, -12) goes round the wall's north end. At frame 60 the box is
    /// disabled (the gap opens in tiles its path does not cross); every 10 frames the agent's
    /// position, corners and remaining distance.</summary>
    IEnumerator CarveShortcut()
    {
        ClearAgents();
        yield return null;
        var settings = NavMesh.GetSettingsByID(0);
        settings.overrideTileSize = true;
        settings.tileSize = 32;
        var sources = new List<NavMeshBuildSource>
        {
            Source(new Vector3(Off.x, -0.05f, Off.y), new Vector3(28f, 0.1f, 28f), 0, 0f),
            Source(new Vector3(Off.x, 0.5f, -11.75f + Off.y), new Vector3(1f, 1f, 4.5f), Blocked, 0f),
            Source(new Vector3(Off.x, 0.5f, 1f + Off.y), new Vector3(1f, 1f, 16f), Blocked, 0f),
        };
        NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(settings, sources, new Bounds(Vector3.zero, new Vector3(32f, 10f, 32f)), Vector3.zero, Quaternion.identity));
        var carver = Carver(Off.x, -8.25f + Off.y, new Vector3(1f, 1f, 3f));
        yield return Frames(4);
        var go = new GameObject("walker");
        go.transform.position = new Vector3(-12f, 0f, -12f);
        var agent = go.AddComponent<NavMeshAgent>();
        agent.SetDestination(new Vector3(12f, 0f, -12f));
        for (int f = 1; f <= 260; f++)
        {
            if (f == 60) carver.enabled = false;
            yield return null;
            if (f % 10 != 0) continue;
            ProbeLog.Value("f" + f + ".at", agent.nextPosition);
            ProbeLog.Value("f" + f + ".corners", agent.path.corners.Length);
            ProbeLog.Value("f" + f + ".stale", agent.isPathStale);
            ProbeLog.Value("f" + f + ".remaining", agent.remainingDistance);
        }
        Destroy(go);
        Destroy(carver.gameObject);
    }

    /// <summary>A 4 m wide link from the floor (y = 0) up to a platform 1.5 m higher across a 2 m
    /// gap, crossed by oblique paths: where a path's crossing points onto and off the link lie when
    /// the path climbs as it crosses.</summary>
    IEnumerator LinkSlope()
    {
        ClearAgents();
        yield return null;
        var sources = new List<NavMeshBuildSource>
        {
            Source(new Vector3(-7.5f + Off.x, -0.05f, Off.y), new Vector3(13f, 0.1f, 28f), 0, 0f),
            Source(new Vector3(7.5f + Off.x, 0.75f, Off.y), new Vector3(13f, 1.5f, 28f), 0, 0f),
        };
        NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, new Vector3(32f, 10f, 32f)), Vector3.zero, Quaternion.identity));
        LogMesh();
        var link = Link(new Vector3(-1.7f, 0f, 0.4f), new Vector3(1.9f, 1.5f, 0.4f), 0, 4f);
        yield return Frames(1);
        Path("up", new Vector3(-8f, 0f, -1.8f), new Vector3(8f, 1.5f, 1.9f));
        Path("down", new Vector3(8f, 1.5f, -1.2f), new Vector3(-8f, 0f, 1.9f));
        NavMesh.RemoveLink(link);
    }

    /// <summary>The NavMesh is at (x, z) on the floor: a sample within 5 cm of it.</summary>
    static bool OnFloor(float x, float z) => NavMesh.SamplePosition(new Vector3(x, 0.0667f, z), out _, 0.05f, NavMesh.AllAreas);

    /// <summary>Carving rules: capsule holes (upright, turned 22.5 degrees, lying on its side), a
    /// hole with corners sharper than a right angle, which
    /// boxes above or below the floor carve (bottom within or beyond the agent height above it, top
    /// within the climb below it), and when turning or scaling a stationary-only box counts as
    /// moving (the hole goes, then comes back once it stands still).</summary>
    IEnumerator CarveRules()
    {
        ClearAgents();
        yield return null;
        Level(14f);
        yield return Frames(2);
        var made = new List<GameObject>();
        NavMeshObstacle Make(Vector3 at, NavMeshObstacleShape shape, Vector3 size, float yaw, float roll, bool stationary)
        {
            var go = new GameObject("rule");
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, roll));
            var o = go.AddComponent<NavMeshObstacle>();
            o.shape = shape;
            if (shape == NavMeshObstacleShape.Capsule)
            {
                o.radius = size.x;
                o.height = size.y;
            }
            else o.size = size;
            o.carving = true;
            o.carveOnlyStationary = stationary;
            made.Add(go);
            return o;
        }
        // Capsules: radius 1, upright, turned, lying along x.
        Make(new Vector3(-8f, 1f, -8f), NavMeshObstacleShape.Capsule, new Vector3(1f, 2f, 0f), 0f, 0f, false);
        Make(new Vector3(-8f, 1f, 0f), NavMeshObstacleShape.Capsule, new Vector3(1f, 2f, 0f), 22.5f, 0f, false);
        Make(new Vector3(-8f, 1f, 8f), NavMeshObstacleShape.Capsule, new Vector3(1f, 4f, 0f), 0f, 90f, false);
        // Heights (agent height 2, climb 0.75): top 0.3 below the floor; bottom 1.9 and 2.1 above.
        Make(new Vector3(0f, -0.8f, -8f), NavMeshObstacleShape.Box, Vector3.one, 0f, 0f, false);
        Make(new Vector3(0f, 2.4f, 0f), NavMeshObstacleShape.Box, Vector3.one, 0f, 0f, false);
        Make(new Vector3(0f, 2.6f, 8f), NavMeshObstacleShape.Box, Vector3.one, 0f, 0f, false);
        // A flat square tilted 60 degrees about its diagonal: a rhombus from above, its corners at
        // the long diagonal turning by about 127 degrees (sharper than the octagon's 45).
        var tilted = Make(new Vector3(8f, 0.5f, 0f), NavMeshObstacleShape.Box, new Vector3(3f, 0.01f, 3f), 0f, 0f, false);
        tilted.transform.rotation = Quaternion.AngleAxis(60f, Vector3.right) * Quaternion.Euler(0f, 45f, 0f);
        // Stationary-only boxes to turn by 5.7 degrees and to scale by 8 % (threshold 0.1), and
        // controls turned by 3 degrees and scaled by 4 % (under it).
        var turned = Make(new Vector3(8f, 0.5f, -6f), NavMeshObstacleShape.Box, Vector3.one, 0f, 0f, true);
        var scaled = Make(new Vector3(8f, 0.5f, 6f), NavMeshObstacleShape.Box, Vector3.one, 0f, 0f, true);
        var turnedLess = Make(new Vector3(8f, 0.5f, -11f), NavMeshObstacleShape.Box, Vector3.one, 0f, 0f, true);
        var scaledLess = Make(new Vector3(8f, 0.5f, 11f), NavMeshObstacleShape.Box, Vector3.one, 0f, 0f, true);
        yield return Frames(4);
        LogPolygons("poly");
        ProbeLog.Value("below", OnFloor(0f, -8f));
        ProbeLog.Value("float1.9", OnFloor(0f, 0f));
        ProbeLog.Value("float2.1", OnFloor(0f, 8f));
        turned.transform.rotation = Quaternion.Euler(0f, 5.7f, 0f);
        scaled.transform.localScale = Vector3.one * 1.08f;
        turnedLess.transform.rotation = Quaternion.Euler(0f, 3f, 0f);
        scaledLess.transform.localScale = Vector3.one * 1.04f;
        for (int f = 1; f <= 40; f++)
        {
            yield return null;
            ProbeLog.Value("f" + f + ".turned", OnFloor(8f, -6f));
            ProbeLog.Value("f" + f + ".scaled", OnFloor(8f, 6f));
            ProbeLog.Value("f" + f + ".turned3", OnFloor(8f, -11f));
            ProbeLog.Value("f" + f + ".scaled4", OnFloor(8f, 11f));
        }
        foreach (var go in made) Destroy(go);
        yield return Frames(2);
    }
}
