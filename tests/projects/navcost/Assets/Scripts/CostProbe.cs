using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMesh path costs, raycast masks and agent placement, run identically in the Unity Editor (the
/// reference) and in Unbound when <c>NAVCOST_OUT</c> names the JSON file to write (rigs: <see cref="Rigs"/>).
/// <list type="bullet">
/// <item><c>strip.*</c>, <c>bake.*</c>, <c>carve.*</c>: sweeps of an area cost or of an end of
/// the path. A route is its interior corners (NavMesh vertices, fixed while the ends move); each
/// sweep lists its first route and every change of route, bisected to the parameter where it
/// happens. One cost model must give every threshold. <c>carve.*.mesh</c>: the carved NavMesh's
/// vertices and area; <c>fans.*</c>: its triangles.</item>
/// <item><c>raycast</c>, <c>raycast.agent</c>: NavMesh.Raycast and NavMeshAgent.Raycast stopped by
/// areas the mask excludes, and by a border.</item>
/// <item><c>place.*</c>: where standing agents are (transform, next position) when enabled by the
/// scene, by AddComponent, by activation or re-enabling, after Warp, a script's move or turning
/// updatePosition on, from Awake and in each Update and LateUpdate; <c>walk.*</c>: the same for
/// walking agents.</item>
/// </list>
/// </summary>
public class CostProbe : MonoBehaviour
{
    static string Out => Environment.GetEnvironmentVariable("NAVCOST_OUT");
    const int Grid = 60;
    const int Bisections = 24;
    readonly List<(string Name, NavMeshAgent Agent)> m_Agents = new List<(string, NavMeshAgent)>();
    int m_Frame;
    NavMeshAgent m_RayAgent;

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static void Scenario(string name, Action body)
    {
        ProbeLog.Begin(name);
        try { body(); }
        catch (Exception e)
        {
            ProbeLog.Error(e);
            Debug.LogException(e);
        }
    }

    void Awake()
    {
        if (string.IsNullOrEmpty(Out))
        {
            enabled = false;
            return;
        }
        Time.captureDeltaTime = 0.02f;
        foreach (var scene in Rigs.SceneAgents) m_Agents.Add((scene.Name, GameObject.Find(scene.Name).GetComponent<NavMeshAgent>()));
        Agent("awakeAdded", new Vector3(-4f, 0f, 0.5f));
        LogAgents("place.awake");
    }

    // ------------------------------------------------------------------ routes

    /// <summary>A path's route: its status and interior corners relative to the rig.</summary>
    static string Route(Vector3 origin, Vector3 a, Vector3 b)
    {
        var path = new NavMeshPath();
        if (!NavMesh.CalculatePath(origin + a, origin + b, NavMesh.AllAreas, path)) return "none";
        var corners = path.corners;
        var s = new StringBuilder(path.status == NavMeshPathStatus.PathComplete ? "" : path.status + " ");
        for (int i = 1; i + 1 < corners.Length; i++)
        {
            var c = corners[i] - origin;
            s.Append(i > 1 ? " " : "").Append(F(c.x)).Append(',').Append(F(c.z));
        }
        return s.Length == 0 ? "direct" : s.ToString();
    }

    /// <summary>Routes over <paramref name="lo"/>..<paramref name="hi"/>: the first one, and each
    /// change bisected to where it happens (the last parameter of the old route), every change
    /// within a grid cell included.</summary>
    static void Sweep(string key, float lo, float hi, Func<float, string> route)
    {
        string previous = route(lo);
        ProbeLog.Value(key + ".lo", previous);
        float previousAt = lo;
        int events = 0;
        for (int i = 1; i <= Grid; i++)
        {
            float t = lo + (hi - lo) * i / Grid;
            string current = route(t);
            // Each change between previousAt and t, the first one first.
            while (current != previous)
            {
                float a = previousAt, b = t;
                for (int k = 0; k < Bisections; k++)
                {
                    float m = (a + b) * 0.5f;
                    if (route(m) == previous) a = m;
                    else b = m;
                }
                string next = route(b);
                ProbeLog.Value(key + ".e" + events, a);
                ProbeLog.Value(key + ".e" + events + ".to", next);
                events++;
                if (next == previous || b >= t) break;
                previous = next;
                previousAt = b;
            }
            previous = current;
            previousAt = t;
        }
        ProbeLog.Value(key + ".events", events);
    }

    static string WithCost(int area, float cost, Func<string> body)
    {
        float old = NavMesh.GetAreaCost(area);
        NavMesh.SetAreaCost(area, cost);
        try { return body(); }
        finally { NavMesh.SetAreaCost(area, old); }
    }

    static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

    /// <summary>Across an area strip or around its open end: the area's cost swept from 1 to 30
    /// for several crossings, then where the crossing point moves at two fixed costs.</summary>
    static void Strip(string rigName, int area)
    {
        var o = Rigs.Get(rigName).Origin;
        foreach (float x in new[] { -4f, -1.5f, 0f, 2f, 3f })
            Sweep("x" + F(x), 1f, 30f, c => WithCost(area, c, () => Route(o, P(x, -4f), P(x, 4f))));
        Sweep("back0", 1f, 30f, c => WithCost(area, c, () => Route(o, P(0f, 4f), P(0f, -4f))));
        Sweep("diag", 1f, 30f, c => WithCost(area, c, () => Route(o, P(-4f, -4f), P(2f, 4f))));
        foreach (float c in new[] { 2f, 6f })
            Sweep("end.c" + F(c), -5f, 5f, x => WithCost(area, c, () => Route(o, P(0f, -4f), P(x, 4f))));
    }

    /// <summary>Round an obstacle: the far end swept across, the near end swept across, the way
    /// back, and a diagonal.</summary>
    static void Around(string rigName)
    {
        var o = Rigs.Get(rigName).Origin;
        Sweep("end", -3f, 3f, x => Route(o, P(0f, -4f), P(x, 4f)));
        Sweep("start", -3f, 3f, x => Route(o, P(x, -4f), P(0f, 4f)));
        Sweep("back", -3f, 3f, x => Route(o, P(0f, 4f), P(x, -4f)));
        Sweep("near", -3f, 3f, x => Route(o, P(0f, -2.5f), P(x, 2.5f)));
        Sweep("side", -3f, 3f, z => Route(o, P(-4f, 0f), P(4f, z)));
    }

    /// <summary>A rig's NavMesh: its vertices, area per NavMesh area, and (listed apart, as the
    /// first vertex of each polygon's fan is not part of the NavMesh's shape) its triangles.</summary>
    static void Mesh(string rigName, bool triangles)
    {
        var o = Rigs.Get(rigName).Origin;
        var t = NavMesh.CalculateTriangulation();
        var list = new List<string>();
        var vertices = new SortedSet<string>(StringComparer.Ordinal);
        var areas = new SortedDictionary<int, float>();
        for (int i = 0; i + 2 < t.indices.Length; i += 3)
        {
            var a = t.vertices[t.indices[i]] - o;
            var b = t.vertices[t.indices[i + 1]] - o;
            var c = t.vertices[t.indices[i + 2]] - o;
            var mid = (a + b + c) / 3f;
            if (Mathf.Abs(mid.x) > Rigs.Size * 0.5f || Mathf.Abs(mid.z) > Rigs.Size * 0.5f) continue;
            list.Add(F(a.x) + "," + F(a.z) + " " + F(b.x) + "," + F(b.z) + " " + F(c.x) + "," + F(c.z) + " a" + t.areas[i / 3]);
            foreach (var v in new[] { a, b, c }) vertices.Add(F(v.x) + "," + F(v.z));
            areas.TryGetValue(t.areas[i / 3], out float sum);
            areas[t.areas[i / 3]] = sum + Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
        if (!triangles)
        {
            ProbeLog.Value("vertices", string.Join(" ", vertices));
            foreach (var pair in areas) ProbeLog.Value("area" + pair.Key, pair.Value);
            return;
        }
        list.Sort(string.CompareOrdinal);
        ProbeLog.Value("triangles", list.Count);
        for (int i = 0; i < list.Count; i++) ProbeLog.Value("tri" + i, list[i]);
    }

    // ------------------------------------------------------------------ raycasts

    static void Raycasts()
    {
        var o = Rigs.Get("rays").Origin;
        void Ray(string key, Vector3 a, Vector3 b, int mask)
        {
            bool blocked = NavMesh.Raycast(o + a, o + b, out var hit, mask);
            ProbeLog.Value(key + ".hit", blocked);
            ProbeLog.Value(key, hit.position - o);
            ProbeLog.Value(key + ".normal", hit.normal);
            ProbeLog.Value(key + ".distance", hit.distance);
            ProbeLog.Value(key + ".mask", hit.mask);
        }
        int all = NavMesh.AllAreas, mud = 1 << Rigs.Mud, water = 1 << Rigs.Water;
        // Into Mud the mask excludes (from the south, and from the north over the allowed Water).
        Ray("noMud", P(-3f, -3f), P(-3f, 3f), all & ~mud);
        Ray("noMud.overWater", P(0f, 3f), P(0f, -3f), all & ~mud);
        // Over the allowed Mud into Water the mask excludes, and both excluded.
        Ray("noWater.overMud", P(0f, -3f), P(0f, 3f), all & ~water);
        Ray("noMudWater", P(0f, -3f), P(0f, 3f), all & ~(mud | water));
        // Starting in excluded Mud; only Mud allowed; a border; clear.
        Ray("fromMud", P(-3f, 0f), P(-3f, 3f), all & ~mud);
        Ray("fromWater", P(0f, 0.8f), P(0f, 3f), all & ~water);
        Ray("onlyMud", P(-3f, 0f), P(3f, 0f), mud);
        Ray("border", P(0f, -3f), P(0f, -8f), all & ~mud);
        Ray("clear", P(-4f, -3f), P(4f, 3f), all);
        Ray("clearIntoMud", P(-3f, -3f), P(-3f, 0f), all);
    }

    // ------------------------------------------------------------------ placement

    // Frames (LateUpdate count from the first frame) when the probe adds agents at runtime, changes
    // them, and turns one agent's updatePosition on; every frame up to PlaceFrames is logged.
    const int CreateFrame = 5, ChangeFrame = 7, UpdatePositionFrame = 9, PlaceFrames = 14;

    // Agents that walk (a path or a velocity): logged apart, per frame, from the agents that stand.
    static readonly HashSet<string> Walking = new HashSet<string> { "move", "moveScripted", "later", "velocity" };

    /// <summary>An agent's transform and next position, relative to the agents rig (so that the
    /// probe's tolerance is absolute), and whether it is on the NavMesh.</summary>
    static void LogAgent(string key, NavMeshAgent agent)
    {
        var o = Rigs.Get("agents").Origin;
        ProbeLog.Value(key + ".position", agent.transform.position - o);
        ProbeLog.Value(key + ".next", agent.nextPosition - o);
        ProbeLog.Value(key + ".onNavMesh", agent.isOnNavMesh);
    }

    void LogAgents(string scenario, bool? walking = null)
    {
        ProbeLog.Begin(scenario);
        foreach (var (name, agent) in m_Agents)
            if (walking == null || Walking.Contains(name) == walking.Value) LogAgent(name, agent);
    }

    void LogFrame(string phase, int frame)
    {
        LogAgents("place." + phase + frame, false);
        LogAgents("walk." + phase + frame, true);
    }

    NavMeshAgent Agent(string name, Vector3 local, Action<NavMeshAgent> setup = null, bool active = true)
    {
        var go = new GameObject(name);
        go.SetActive(active);
        go.transform.position = Rigs.Get("agents").Origin + local;
        var agent = go.AddComponent<NavMeshAgent>();
        setup?.Invoke(agent);
        m_Agents.Add((name, agent));
        return agent;
    }

    NavMeshAgent Find(string name) => m_Agents.Find(e => e.Name == name).Agent;

    /// <summary>Agents added by script once the scene runs, each in its own lane.</summary>
    void CreateAgents()
    {
        var o = Rigs.Get("agents").Origin;
        Agent("idle", P(-2f, -4f));
        Agent("idleHigh", new Vector3(-2f, 0.3f, -2.5f));
        Agent("idleLow", new Vector3(-2f, -0.05f, -1f));
        Agent("move", P(-4f, 2f), a => a.SetDestination(o + P(4f, 2f)));
        Agent("selfDestination", P(-2f, 0.5f), a => a.SetDestination(a.transform.position));
        Agent("stopped", P(0f, -4f), a => { a.isStopped = true; a.SetDestination(o + P(4f, -4f)); });
        Agent("warp", P(0f, -2.5f));
        Agent("warpHigh", P(0f, -1f));
        Agent("baseOffset", P(0f, 0.5f), a => a.baseOffset = 1f);
        Agent("scripted", P(2f, -2.5f));
        Agent("moveZero", P(2f, -1f));
        Agent("later", P(2f, 0.5f));
        Agent("velocity", P(4f, -4f));
        Agent("reenabled", P(4f, -2.5f));
        Agent("activated", new Vector3(4f, 0.3f, -1f), active: false);
        Agent("idleNoUpdate", new Vector3(4f, 0.3f, 0.5f), a => a.updatePosition = false, active: false);
        Agent("scriptedUpdate", P(-2f, 4f));
        Agent("warpUpdate", P(0f, 4f));
        Agent("moveScripted", P(-4f, 5f), a => a.SetDestination(Rigs.Get("agents").Origin + P(4f, 5f)));
        LogAgents("place.created");
        // NavMeshAgent.Raycast with the agent's area mask (it excludes Mud), in the raycast rig.
        var ray = new GameObject("rayAgent");
        ray.transform.position = Rigs.Get("rays").Origin + P(-3f, -3f);
        m_RayAgent = ray.AddComponent<NavMeshAgent>();
        m_RayAgent.areaMask = NavMesh.AllAreas & ~(1 << Rigs.Mud);
    }

    /// <summary>Changes after the frame's values are logged, seen from the next frame on.</summary>
    void ChangeAgents()
    {
        var o = Rigs.Get("agents").Origin;
        ProbeLog.Begin("place.changed");
        ProbeLog.Value("warp.returned", Find("warp").Warp(o + P(0f, -2.5f)));
        ProbeLog.Value("warpHigh.returned", Find("warpHigh").Warp(o + new Vector3(0f, 0.5f, -1f)));
        LogAgent("warp", Find("warp"));
        LogAgent("warpHigh", Find("warpHigh"));
        Find("scripted").transform.position = o + new Vector3(2f, 0.5f, -2.5f);
        Find("moveZero").Move(Vector3.zero);
        LogAgent("moveZero", Find("moveZero"));
        Find("later").SetDestination(o + P(3f, 0.5f));
        Find("velocity").velocity = new Vector3(1f, 0f, 0f);
        var reenabled = Find("reenabled");
        reenabled.enabled = false;
        reenabled.transform.position = o + P(4f, -2.5f);
        reenabled.enabled = true;
        LogAgent("reenabled", reenabled);
        Find("activated").gameObject.SetActive(true);
        LogAgent("activated", Find("activated"));
        Find("idleNoUpdate").gameObject.SetActive(true);
        LogAgent("idleNoUpdate", Find("idleNoUpdate"));
        ProbeLog.Begin("raycast.agent");
        bool blocked = m_RayAgent.Raycast(Rigs.Get("rays").Origin + P(-3f, 3f), out var hit);
        ProbeLog.Value("hit", blocked);
        ProbeLog.Value("position", hit.position - Rigs.Get("rays").Origin);
        ProbeLog.Value("mask", hit.mask);
    }

    void Update()
    {
        if (m_Frame < PlaceFrames) LogFrame("u", m_Frame + 1);
        // Changes in Update (before the agents' update after the scripts), seen from LateUpdate.
        if (m_Frame + 1 == ChangeFrame)
        {
            var o = Rigs.Get("agents").Origin;
            Find("scriptedUpdate").transform.position = o + new Vector3(-2f, 0.5f, 4f);
            var walking = Find("moveScripted").transform;
            walking.position = new Vector3(walking.position.x, 0.5f, walking.position.z - 0.4f);
            ProbeLog.Begin("place.changedInUpdate");
            ProbeLog.Value("warpUpdate.returned", Find("warpUpdate").Warp(o + new Vector3(0f, 0.5f, 4f)));
            LogAgent("warpUpdate", Find("warpUpdate"));
            LogAgent("scriptedUpdate", Find("scriptedUpdate"));
            LogAgent("moveScripted", Find("moveScripted"));
        }
    }

    void LateUpdate()
    {
        m_Frame++;
        if (m_Frame > PlaceFrames) return;
        LogFrame("l", m_Frame);
        if (m_Frame == CreateFrame) CreateAgents();
        if (m_Frame == ChangeFrame) ChangeAgents();
        if (m_Frame == UpdatePositionFrame) Find("idleNoUpdate").updatePosition = true;
    }

    // ------------------------------------------------------------------ run

    IEnumerator Start()
    {
        if (string.IsNullOrEmpty(Out)) yield break;
        LogAgents("place.start");
        Scenario("strip.strip1", () => Strip("strip1", Rigs.Mud));
        Scenario("strip.strip2", () => Strip("strip2", Rigs.Mud));
        Scenario("strip.rays", () => Strip("rays", Rigs.Mud));
        Scenario("strip.rays.water", () => Strip("rays", Rigs.Water));
        Scenario("bake.box", () => Around("box"));
        Scenario("bake.bar", () => Around("bar"));
        Scenario("bake.turned", () => Around("turned"));
        Scenario("raycast", Raycasts);

        // Carving obstacles: the box rig's 2 m box and the bar rig's bar, cut at once.
        foreach (var (rig, center, size) in new[]
        {
            ("carvebox", Vector3.zero, new Vector3(2f, 2f, 2f)),
            ("carvebar", new Vector3(0.37f, 0f, 0.21f), new Vector3(1f, 2f, 3f)),
        })
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = rig + "/Carver";
            go.transform.position = Rigs.Get(rig).Origin + center + new Vector3(0f, 1f, 0f);
            go.transform.localScale = size;
            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;
        }
        for (int i = 0; i < 4; i++) yield return null;
        Scenario("carve.carvebox", () => Around("carvebox"));
        Scenario("carve.carvebar", () => Around("carvebar"));
        Scenario("carve.carvebar.mud", () =>
        {
            var o = Rigs.Get("carvebar").Origin;
            Sweep("x0", 1f, 30f, c => WithCost(Rigs.Mud, c, () => Route(o, P(0f, -4f), P(0f, 4f))));
            Sweep("x-1", 1f, 30f, c => WithCost(Rigs.Mud, c, () => Route(o, P(-1f, -4f), P(1f, 4f))));
            Sweep("end.c3", -3f, 3f, x => Route(o, P(0f, -4f), P(x, 4f)));
        });
        Scenario("carve.carvebox.mesh", () => Mesh("carvebox", false));
        Scenario("carve.carvebar.mesh", () => Mesh("carvebar", false));
        Scenario("fans.carvebox", () => Mesh("carvebox", true));
        Scenario("fans.carvebar", () => Mesh("carvebar", true));

        while (m_Frame < PlaceFrames) yield return null;

        File.WriteAllText(Out, ProbeLog.Json());
        Debug.Log("NavMesh cost probe written");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
