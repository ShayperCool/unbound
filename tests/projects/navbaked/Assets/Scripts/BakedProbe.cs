using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// A scene's legacy-baked NavMesh (NavMeshSettings.m_NavMeshData), measured identically in the
/// Unity Editor (the reference) and in Unbound when <c>NAVBAKED_OUT</c> names the JSON file to
/// write: whether it is there in Awake, its triangulation, samples, raycasts and paths (area cost
/// and mask, a step, a one-way OffMeshLink up and the bake's drop links down), an agent walked
/// up and back down at a fixed 0.02 s step, then the additive load of a second baked scene and
/// its unload; then where in the player loop asynchronous operations complete and the coroutines
/// waiting for them resume (<see cref="ResumeTiming"/>).
/// </summary>
public class BakedProbe : MonoBehaviour
{
    static string Out => Environment.GetEnvironmentVariable("NAVBAKED_OUT");
    static readonly Vector3 Home = new Vector3(-6f, 0f, -3f);
    static readonly Vector3 Top = new Vector3(-7f, 1.5f, 4f);
    const int Mud = 4;
    NavMeshAgent m_Agent;

    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

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
        m_Agent = GameObject.Find("Agent").GetComponent<NavMeshAgent>();
        Scenario("awake", () =>
        {
            ProbeLog.Value("agent.isOnNavMesh", m_Agent.isOnNavMesh);
            Sample("home", Home);
        });
    }

    IEnumerator Start()
    {
        if (string.IsNullOrEmpty(Out)) yield break;
        Scenario("areas", () =>
        {
            ProbeLog.Value("mud", NavMesh.GetAreaFromName("Mud"));
            ProbeLog.Value("mud.cost", NavMesh.GetAreaCost(Mud));
            ProbeLog.Value("settings", NavMesh.GetSettingsCount());
        });
        Scenario("baked", () => Triangulation("baked"));
        Scenario("sample", () => Grid(-10f, 10f));
        Scenario("raycast", Raycasts);
        Scenario("paths", Paths);

        ProbeLog.Begin("agent");
        ProbeLog.Value("start", m_Agent.transform.position);
        m_Agent.SetDestination(Top);
        for (int frame = 1; frame <= 500; frame++)
        {
            if (frame == 250) m_Agent.SetDestination(Home);
            yield return null;
            if (frame % 5 != 0) continue;
            var key = "f" + frame;
            ProbeLog.Value(key, m_Agent.transform.position);
            ProbeLog.Value(key + ".link", m_Agent.isOnOffMeshLink);
            if (m_Agent.isOnOffMeshLink) ProbeLog.Value(key + ".linkType", m_Agent.currentOffMeshLinkData.linkType.ToString());
            if (frame == 5 || frame == 255)
            {
                ProbeLog.Value(key + ".status", m_Agent.pathStatus.ToString());
                Corners(key + ".corners", m_Agent.path);
            }
        }

        // Player-loop markers for the resume.* scenarios (ResumeTiming), from here on.
        ResumeTiming.Install();
        yield return ResumeTiming.Load();
        ResumeTiming.Resumed("load");
        Scenario("additive", () =>
        {
            Triangulation("both");
            Grid(10f, 22f);
            ProbeLog.Value("agent2.isOnNavMesh", GameObject.Find("Agent2").GetComponent<NavMeshAgent>().isOnNavMesh);
            Path("across", Home, new Vector3(17f, 0f, 0f), NavMesh.AllAreas);
            Path("second", new Vector3(13f, 0f, -3f), new Vector3(21f, 0f, 3f), NavMesh.AllAreas);
        });
        Scenario("agent2", () => ProbeLog.Value("position", GameObject.Find("Agent2").transform.position));
        // Two frames more, for the agent's transform in resume.load.
        yield return null;
        yield return null;
        yield return ResumeTiming.Unload();
        ResumeTiming.Resumed("unload");
        yield return null;
        ResumeTiming.End();
        Scenario("unloaded", () =>
        {
            Triangulation("left");
            Sample("second", new Vector3(17f, 0f, 0f));
            Sample("home", Home);
            Path("across", Home, new Vector3(17f, 0f, 0f), NavMesh.AllAreas);
        });

        yield return ResumeTiming.Controls(this);

        // The jump-across links the bake generated: paths across a 2 m gap each way, level and
        // up or down 0.5 m.
        var jump = SceneManager.LoadSceneAsync("Jump", LoadSceneMode.Additive);
        while (!jump.isDone) yield return null;
        yield return null;
        Scenario("jump", () =>
        {
            Path("level.ab", new Vector3(-5f, 0f, 30f), new Vector3(5f, 0f, 30f), NavMesh.AllAreas);
            Path("level.ba", new Vector3(5f, 0f, 30f), new Vector3(-5f, 0f, 30f), NavMesh.AllAreas);
            Path("up", new Vector3(-5f, 0f, 40f), new Vector3(5f, 0.5f, 40f), NavMesh.AllAreas);
            Path("down", new Vector3(5f, 0.5f, 40f), new Vector3(-5f, 0f, 40f), NavMesh.AllAreas);
        });

        // A NavMesh baked with Build Height Mesh: ramp, stairs and a bump. Where the queries and
        // agents put a point in height (SamplePosition, Raycast, agents placed, warped and walking),
        // and, as controls, path corners and the closest edge.
        var height = SceneManager.LoadSceneAsync("Height", LoadSceneMode.Additive);
        while (!height.isDone) yield return null;
        yield return null;
        Scenario("height", () =>
        {
            for (float x = -9.5f; x <= 8.01f; x += 0.25f)
            {
                bool ok = NavMesh.SamplePosition(new Vector3(x, 2f, 60.3f), out var hit, 2.5f, NavMesh.AllAreas);
                ProbeLog.Value("s" + F(x), ok ? V(hit.position) + " " + F(hit.distance) : "none");
            }
            foreach (var (key, x, z) in new[] { ("bump.c", -1f, 63f), ("bump.e", -1.45f, 63.2f), ("bump.o", -1.7f, 63.2f) })
            {
                bool ok = NavMesh.SamplePosition(new Vector3(x, 1f, z), out var hit, 2.5f, NavMesh.AllAreas);
                ProbeLog.Value(key, ok ? V(hit.position) + " " + F(hit.distance) : "none");
            }
            void Ray(string key, Vector3 from, Vector3 to)
            {
                bool hit = NavMesh.Raycast(from, to, out var h, NavMesh.AllAreas);
                ProbeLog.Value(key, hit + " " + V(h.position) + " " + F(h.distance));
            }
            Ray("ray.stairs", new Vector3(0f, 0f, 60.5f), new Vector3(3.3f, 0.6f, 60.5f));
            Ray("ray.stairs.wall", new Vector3(0f, 0f, 60.5f), new Vector3(3.3f, 0.6f, 62f));
            Ray("ray.ramp", new Vector3(-10f, 0f, 59.5f), new Vector3(-5.5f, 1f, 59.5f));
            Ray("ray.ramp.wall", new Vector3(-10f, 0f, 59.5f), new Vector3(-5.5f, 1f, 57f));
            Path("path.stairs", new Vector3(-1f, 0f, 58f), new Vector3(7.5f, 1f, 60f), NavMesh.AllAreas);
            Path("path.ramp", new Vector3(-10.5f, 0f, 62f), new Vector3(-4f, 1.2f, 60f), NavMesh.AllAreas);
            bool edge = NavMesh.FindClosestEdge(new Vector3(3.5f, 0.6f, 61f), out var e, NavMesh.AllAreas);
            ProbeLog.Value("edge", edge + " " + V(e.position) + " " + F(e.distance));
        });
        ProbeLog.Begin("height.agents");
        var agents = new List<(string, NavMeshAgent)>();
        foreach (var (key, from, to) in new[]
        {
            ("stairs", new Vector3(-1f, 0f, 60f), new Vector3(7.5f, 1f, 60f)),
            ("ramp", new Vector3(-10.5f, 0f, 60.2f), new Vector3(-3f, 1.4f, 60.2f)),
            ("bump", new Vector3(-3f, 0f, 63f), new Vector3(1f, 0f, 63f)),
        })
        {
            var go = new GameObject("height." + key);
            go.transform.position = from;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.speed = 2f;
            ProbeLog.Value(key + ".placed", agent.nextPosition);
            agent.SetDestination(to);
            agents.Add((key, agent));
        }
        for (int f = 1; f <= 250; f++)
        {
            yield return null;
            if (f == 2)
            {
                foreach (var (key, agent) in agents)
                {
                    ProbeLog.Value(key + ".destination", agent.destination);
                    var corners = agent.path.corners;
                    ProbeLog.Value(key + ".corners", corners.Length);
                    for (int i = 0; i < corners.Length; i++) ProbeLog.Value(key + ".corner" + i, corners[i]);
                    ProbeLog.Value(key + ".steer", agent.steeringTarget);
                }
            }
            if (f % 5 != 0) continue;
            foreach (var (key, agent) in agents)
            {
                ProbeLog.Value(key + ".f" + f, agent.nextPosition);
                ProbeLog.Value(key + ".f" + f + ".vy", agent.velocity.y);
                ProbeLog.Value(key + ".f" + f + ".ty", agent.transform.position.y);
            }
        }
        var stairs = agents[0].Item2;
        stairs.Warp(new Vector3(3.3f, 0f, 60.2f));
        ProbeLog.Value("warp.next", stairs.nextPosition);
        yield return null;
        ProbeLog.Value("warp.transform", stairs.transform.position);
        // Moved by script along the NavMesh: nextPosition set onto the next stair, Move down a stair.
        stairs.nextPosition = new Vector3(4.1f, 0f, 60.4f);
        ProbeLog.Value("set.next", stairs.nextPosition);
        ProbeLog.Value("set.transform", stairs.transform.position);
        stairs.Move(new Vector3(-0.8f, 0f, 0f));
        ProbeLog.Value("move.next", stairs.nextPosition);
        yield return null;
        ProbeLog.Value("move.transform", stairs.transform.position);
        foreach (var (_, agent) in agents) Destroy(agent.gameObject);

        File.WriteAllText(Out, ProbeLog.Json());
        Debug.Log("NavMesh baked probe written");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Update() => ResumeTiming.Update();

    static void Triangulation(string key)
    {
        var t = NavMesh.CalculateTriangulation();
        ProbeLog.Value(key + ".vertices", t.vertices.Length);
        ProbeLog.Value(key + ".indices", t.indices.Length);
        var triangles = new List<string>();
        float area = 0f;
        for (int i = 0; i + 2 < t.indices.Length; i += 3)
        {
            var a = t.vertices[t.indices[i]];
            var b = t.vertices[t.indices[i + 1]];
            var c = t.vertices[t.indices[i + 2]];
            var corners = new[] { V(a), V(b), V(c) }.ToList();
            corners.Sort(string.CompareOrdinal);
            triangles.Add(string.Join(" ", corners) + " a" + t.areas[i / 3]);
            area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
        triangles.Sort(string.CompareOrdinal);
        for (int i = 0; i < triangles.Count; i++) ProbeLog.Value(key + ".tri" + i, triangles[i]);
        ProbeLog.Value(key + ".area", area);
    }

    static void Sample(string key, Vector3 at)
    {
        bool ok = NavMesh.SamplePosition(at + Vector3.up * 2f, out var hit, 2.5f, NavMesh.AllAreas);
        ProbeLog.Value(key + ".hit", ok);
        if (!ok) return;
        ProbeLog.Value(key, hit.position);
        ProbeLog.Value(key + ".mask", hit.mask);
    }

    static void Grid(float fromX, float toX)
    {
        for (float z = -6f; z <= 6f; z += 2f)
        for (float x = fromX; x <= toX; x += 2f)
            Sample("s" + F(x) + "," + F(z), new Vector3(x, 0f, z));
    }

    static void Raycasts()
    {
        void Ray(string key, Vector3 from, Vector3 to, int mask)
        {
            bool hit = NavMesh.Raycast(from, to, out var h, mask);
            ProbeLog.Value(key + ".hit", hit);
            ProbeLog.Value(key, h.position);
            ProbeLog.Value(key + ".mask", h.mask);
        }
        Ray("mud", Home, new Vector3(6f, 0f, -3f), NavMesh.AllAreas);
        Ray("mud.masked", Home, new Vector3(6f, 0f, -3f), NavMesh.AllAreas & ~(1 << Mud));
        Ray("wall", Home, new Vector3(-7f, 0f, 4f), NavMesh.AllAreas);
        Ray("step", new Vector3(3f, 0f, 3.5f), new Vector3(8f, 0.3f, 3.5f), NavMesh.AllAreas);
    }

    static void Corners(string key, NavMeshPath path)
    {
        var corners = path.corners;
        ProbeLog.Value(key, corners.Length);
        for (int i = 0; i < corners.Length; i++) ProbeLog.Value(key + i, corners[i]);
    }

    static void Path(string key, Vector3 from, Vector3 to, int mask)
    {
        var path = new NavMeshPath();
        ProbeLog.Value(key + ".found", NavMesh.CalculatePath(from, to, mask, path));
        ProbeLog.Value(key + ".status", path.status.ToString());
        Corners(key + ".corners", path);
    }

    static void Paths()
    {
        Path("mud", Home, new Vector3(6f, 0f, -3f), NavMesh.AllAreas);
        Path("mud.masked", Home, new Vector3(6f, 0f, -3f), NavMesh.AllAreas & ~(1 << Mud));
        Path("step", new Vector3(-2f, 0f, 4.5f), new Vector3(7f, 0.3f, 3.5f), NavMesh.AllAreas);
        Path("up", Home, Top, NavMesh.AllAreas);
        Path("down", Top, Home, NavMesh.AllAreas);
        Path("second", Home, new Vector3(17f, 0f, 0f), NavMesh.AllAreas);
    }
}
