using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshObstacle, run identically in the Unity Editor (the reference) and in Unbound when
/// <c>NAVOBSTACLE_OUT</c> names the JSON-lines file to write. Every lane is a flat 16 m cell with
/// obstacles (carving or not, boxes and capsules, still or moved by the probe) and usually an agent
/// walking from x = -6 to x = 6 through them, at a fixed 0.02 s step for 250 frames. Recorded per
/// frame: the agent ("a": position, next position, velocities, steering target, remaining distance,
/// path flags and corners), the obstacle ("o": position, velocity), NavMesh queries across the cell
/// from Update ("u", after the lane's scripted action) and LateUpdate ("l"): SamplePosition near the
/// obstacle, CalculatePath and Raycast from x = -6 to x = 6; and at some frames the NavMesh's
/// triangulation around the cell's centre ("t": sorted vertices and triangle count).
/// </summary>
public class ObstacleProbe : MonoBehaviour
{
    const int Frames = 250;
    const int Columns = 5;
    const float Spacing = 30f;
    static readonly int[] TriangulationFrames = { 1, 2, 3, 11, 12, 13, 30, 41, 42, 43, 61, 62, 63, 90, 150, 250 };

    public static readonly string[] Lanes =
    {
        "carve_box", "carve_rot", "carve_capsule", "carve_offset", "carve_late",
        "carve_enable", "carve_stationary_move", "carve_always_move", "carve_threshold", "carve_time",
        "carve_walkinto", "carve_on_agent", "carve_block", "avoid_box", "avoid_capsule",
        "avoid_rot", "avoid_side", "avoid_long", "avoid_moving", "avoid_velocity",
        "avoid_radius1", "avoid_lowq", "defaults",
    };

    public static int LaneCount => Lanes.Length;

    public static Vector3 Origin(int lane) => new Vector3((lane % Columns) * Spacing, 0f, (lane / Columns) * Spacing);

    sealed class Lane
    {
        public string Name;
        public Vector3 O;
        public NavMeshAgent Agent;
        public NavMeshObstacle Obstacle;
        public Action<int> Act;
    }

    static readonly Vector3 From = new Vector3(-6f, 0f, 0f);
    static readonly Vector3 To = new Vector3(6f, 0f, 0f);

    readonly List<Lane> m_Lanes = new List<Lane>();
    readonly StringBuilder m_Out = new StringBuilder();
    int m_Frame;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVOBSTACLE_OUT"))) return;
        new GameObject("ObstacleProbe").AddComponent<ObstacleProbe>();
    }

    void Awake() => Time.captureDeltaTime = 0.02f;

    // ------------------------------------------------------------------ setup

    NavMeshObstacle Obstacle(Lane lane, Vector3 at, Action<NavMeshObstacle> configure, float yaw = 0f, bool enabled = true)
    {
        var go = new GameObject(lane.Name + ".o");
        go.transform.SetPositionAndRotation(lane.O + at, Quaternion.Euler(0f, yaw, 0f));
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.enabled = enabled;
        configure?.Invoke(obstacle);
        lane.Obstacle = obstacle;
        return obstacle;
    }

    static Action<NavMeshObstacle> Box(Vector3 size, bool carve, Vector3 center = default) => o =>
    {
        o.shape = NavMeshObstacleShape.Box;
        o.size = size;
        o.center = center;
        o.carving = carve;
    };

    static Action<NavMeshObstacle> Capsule(float radius, float height, bool carve) => o =>
    {
        o.shape = NavMeshObstacleShape.Capsule;
        o.radius = radius;
        o.height = height;
        o.center = Vector3.zero;
        o.carving = carve;
    };

    NavMeshAgent Agent(Lane lane, Vector3 at, Vector3? to, Action<NavMeshAgent> configure = null)
    {
        var go = new GameObject(lane.Name + ".a");
        go.transform.position = lane.O + at;
        var agent = go.AddComponent<NavMeshAgent>();
        configure?.Invoke(agent);
        if (to.HasValue) agent.SetDestination(lane.O + to.Value);
        lane.Agent = agent;
        return agent;
    }

    static Action<int> Move(Lane lane, int from, int to, Vector3 step) => f =>
    {
        if (f >= from && f < to) lane.Obstacle.transform.position += step;
    };

    void Start()
    {
        NavMesh.pathfindingIterationsPerFrame = 100000;
        for (int i = 0; i < Lanes.Length; i++)
        {
            var lane = new Lane { Name = Lanes[i], O = Origin(i) };
            m_Lanes.Add(lane);
            Setup(lane);
        }
        foreach (var lane in m_Lanes)
        {
            if (lane.Obstacle != null)
            {
                var o = lane.Obstacle;
                Line(lane.Name, "o", 0, "\"shape\":" + Text(o.shape.ToString()) + ",\"size\":" + V(o.size) + ",\"center\":" + V(o.center)
                    + ",\"radius\":" + N(o.radius) + ",\"height\":" + N(o.height) + ",\"carving\":" + B(o.carving)
                    + ",\"stationary\":" + B(o.carveOnlyStationary) + ",\"threshold\":" + N(o.carvingMoveThreshold)
                    + ",\"time\":" + N(o.carvingTimeToStationary) + ",\"vel\":" + V(o.velocity));
            }
            if (lane.Agent != null)
                Line(lane.Name, "a", 0, "\"on\":" + B(lane.Agent.isOnNavMesh) + ",\"p\":" + V(lane.Agent.transform.position)
                    + ",\"pp\":" + B(lane.Agent.pathPending) + ",\"hp\":" + B(lane.Agent.hasPath));
            Queries(lane, "s", 0);
        }
    }

    void Setup(Lane lane)
    {
        var box = new Vector3(0f, 0.5f, 0f);
        switch (lane.Name)
        {
            case "carve_box":
                Obstacle(lane, box, Box(Vector3.one, true));
                Agent(lane, From, To);
                break;
            case "carve_rot":
                Obstacle(lane, box, Box(new Vector3(2f, 1f, 1f), true), 30f);
                Agent(lane, From, To);
                break;
            case "carve_capsule":
                Obstacle(lane, new Vector3(0f, 1f, 0f), Capsule(0.5f, 2f, true));
                Agent(lane, From, To);
                break;
            case "carve_offset":
                Obstacle(lane, new Vector3(0f, 1f, 0.5f), Box(new Vector3(1f, 2f, 3f), true, new Vector3(0.5f, 0f, 0f)));
                Agent(lane, From, To);
                break;
            case "carve_late":
                Obstacle(lane, box, Box(Vector3.one, false));
                Agent(lane, From, To);
                lane.Act = f => { if (f == 10) lane.Obstacle.carving = true; };
                break;
            case "carve_enable":
                Obstacle(lane, box, Box(Vector3.one, true), 0f, false);
                Agent(lane, From, To);
                lane.Act = f => { if (f == 10) lane.Obstacle.enabled = true; };
                break;
            case "carve_stationary_move":
                Obstacle(lane, new Vector3(0f, 0.5f, -4f), Box(Vector3.one, true));
                lane.Act = Move(lane, 10, 60, new Vector3(0f, 0f, 0.08f));
                break;
            case "carve_always_move":
                Obstacle(lane, new Vector3(0f, 0.5f, -3f), o => { Box(Vector3.one, true)(o); o.carveOnlyStationary = false; });
                lane.Act = Move(lane, 10, 60, new Vector3(0f, 0f, 0.03f));
                break;
            case "carve_threshold":
                Obstacle(lane, new Vector3(0f, 0.5f, -3f), o => { Box(Vector3.one, true)(o); o.carveOnlyStationary = false; o.carvingMoveThreshold = 0.5f; });
                lane.Act = Move(lane, 10, 60, new Vector3(0f, 0f, 0.03f));
                break;
            case "carve_time":
                Obstacle(lane, new Vector3(0f, 0.5f, -3f), o => { Box(Vector3.one, true)(o); o.carvingTimeToStationary = 0.1f; });
                lane.Act = Move(lane, 10, 30, new Vector3(0f, 0f, 0.05f));
                break;
            case "carve_walkinto":
                Agent(lane, From, To);
                lane.Act = f => { if (f == 40) Obstacle(lane, new Vector3(2f, 0.5f, 0f), Box(Vector3.one, true)); };
                break;
            case "carve_on_agent":
                Agent(lane, Vector3.zero, null);
                lane.Act = f => { if (f == 10) Obstacle(lane, box, Box(Vector3.one, true)); };
                break;
            case "carve_block":
                Obstacle(lane, box, Box(new Vector3(1f, 1f, 20f), true));
                Agent(lane, From, To);
                break;
            case "avoid_box":
                Obstacle(lane, box, Box(Vector3.one, false));
                Agent(lane, From, To);
                break;
            case "avoid_capsule":
                Obstacle(lane, new Vector3(0f, 1f, 0f), Capsule(0.5f, 2f, false));
                Agent(lane, From, To);
                break;
            case "avoid_rot":
                Obstacle(lane, box, Box(new Vector3(2f, 1f, 1f), false), 30f);
                Agent(lane, From, To);
                break;
            case "avoid_side":
                Obstacle(lane, new Vector3(0f, 0.5f, 0.9f), Box(Vector3.one, false));
                Agent(lane, From, To);
                break;
            case "avoid_long":
                Obstacle(lane, box, Box(new Vector3(1f, 1f, 3f), false));
                Agent(lane, From, To);
                break;
            case "avoid_moving":
                Obstacle(lane, new Vector3(1f, 0.5f, 4f), Box(Vector3.one, false));
                Agent(lane, From, To);
                lane.Act = Move(lane, 1, 250, new Vector3(0f, 0f, -0.04f));
                break;
            case "avoid_velocity":
                Obstacle(lane, new Vector3(1.5f, 0.5f, 1.5f), o => { Box(Vector3.one, false)(o); o.velocity = new Vector3(0f, 0f, -2f); });
                Agent(lane, From, To);
                break;
            case "avoid_radius1":
                Obstacle(lane, box, Box(Vector3.one, false));
                Agent(lane, From, To, g => g.radius = 1f);
                break;
            case "avoid_lowq":
                Obstacle(lane, box, Box(Vector3.one, false));
                Agent(lane, From, To, g => g.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance);
                break;
            case "defaults":
                Obstacle(lane, box, null);
                break;
        }
    }

    // ------------------------------------------------------------------ frames

    void Update()
    {
        if (m_Done) return;
        int f = m_Frame + 1;
        foreach (var lane in m_Lanes)
        {
            lane.Act?.Invoke(f);
            Queries(lane, "u", f);
        }
    }

    void LateUpdate()
    {
        if (m_Done) return;
        m_Frame++;
        int f = m_Frame;
        NavMeshTriangulation tri = default;
        bool triangulate = Array.IndexOf(TriangulationFrames, f) >= 0;
        if (triangulate) tri = NavMesh.CalculateTriangulation();
        foreach (var lane in m_Lanes)
        {
            if (lane.Agent != null)
            {
                var g = lane.Agent;
                var c = new StringBuilder();
                var corners = g.path.corners;
                for (int k = 0; k < corners.Length; k++) c.Append(k > 0 ? "," : "").Append(V(corners[k] - lane.O));
                Line(lane.Name, "a", f, "\"p\":" + V(g.transform.position - lane.O) + ",\"n\":" + V(g.nextPosition - lane.O)
                    + ",\"v\":" + V(g.velocity) + ",\"dv\":" + V(g.desiredVelocity) + ",\"st\":" + V(g.steeringTarget - lane.O)
                    + ",\"rd\":" + N(g.remainingDistance) + ",\"hp\":" + B(g.hasPath) + ",\"pp\":" + B(g.pathPending)
                    + ",\"ps\":" + Text(g.pathStatus.ToString()) + ",\"on\":" + B(g.isOnNavMesh) + ",\"c\":[" + c + "]");
            }
            if (lane.Obstacle != null)
                Line(lane.Name, "o", f, "\"p\":" + V(lane.Obstacle.transform.position - lane.O) + ",\"vel\":" + V(lane.Obstacle.velocity)
                    + ",\"on\":" + B(lane.Obstacle.isActiveAndEnabled));
            Queries(lane, "l", f);
            if (triangulate) Triangulation(lane, tri, f);
        }
        if (f >= Frames) Finish(0);
    }

    void Queries(Lane lane, string tag, int f)
    {
        var o = lane.O;
        bool sampled = NavMesh.SamplePosition(o + new Vector3(0f, 0f, 0f), out var hit, 3f, NavMesh.AllAreas);
        var path = new NavMeshPath();
        bool found = NavMesh.CalculatePath(o + From, o + To, NavMesh.AllAreas, path);
        var c = new StringBuilder();
        var corners = path.corners;
        for (int k = 0; k < corners.Length; k++) c.Append(k > 0 ? "," : "").Append(V(corners[k] - o));
        bool blocked = NavMesh.Raycast(o + From, o + To, out var ray, NavMesh.AllAreas);
        Line(lane.Name, tag, f, "\"s\":" + B(sampled) + ",\"sp\":" + V(sampled ? hit.position - o : Vector3.zero) + ",\"sd\":" + N(sampled ? hit.distance : 0f)
            + ",\"path\":" + B(found) + ",\"ps\":" + Text(path.status.ToString()) + ",\"c\":[" + c + "]"
            + ",\"ray\":" + B(blocked) + ",\"rp\":" + V(ray.position - o) + ",\"rn\":" + V(ray.normal));
    }

    void Triangulation(Lane lane, NavMeshTriangulation tri, int f)
    {
        // The triangles with a vertex within 4 m of the cell's centre: their vertices (sorted) and count.
        var verts = new SortedSet<(float, float, float)>();
        int count = 0;
        for (int t = 0; t + 2 < tri.indices.Length; t += 3)
        {
            bool near = false;
            for (int k = 0; k < 3; k++)
            {
                var v = tri.vertices[tri.indices[t + k]] - lane.O;
                if (Mathf.Abs(v.x) <= 4f && Mathf.Abs(v.z) <= 4f) near = true;
            }
            if (!near) continue;
            count++;
            for (int k = 0; k < 3; k++)
            {
                var v = tri.vertices[tri.indices[t + k]] - lane.O;
                verts.Add(((float)Math.Round(v.x, 3), (float)Math.Round(v.z, 3), (float)Math.Round(v.y, 3)));
            }
        }
        var s = new StringBuilder();
        foreach (var (x, z, y) in verts) s.Append(s.Length > 0 ? "," : "").Append(V(new Vector3(x, y, z)));
        Line(lane.Name, "t", f, "\"tris\":" + count + ",\"verts\":[" + s + "]");
    }

    // ------------------------------------------------------------------ output

    void Line(string lane, string agent, int frame, string body) =>
        m_Out.Append("{\"lane\":").Append(Text(lane)).Append(",\"agent\":").Append(Text(agent)).Append(",\"f\":").Append(frame)
            .Append(',').Append(body).Append("}\n");

    void Finish(int code)
    {
        m_Done = true;
        var path = Environment.GetEnvironmentVariable("NAVOBSTACLE_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, m_Out.ToString());
        Debug.Log("[navobstacle] written " + path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    static string N(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? "\"" + v.ToString(CultureInfo.InvariantCulture) + "\"" : v.ToString("R", CultureInfo.InvariantCulture);

    static string V(Vector3 v) => "[" + N(v.x) + "," + N(v.y) + "," + N(v.z) + "]";

    static string B(bool b) => b ? "true" : "false";

    static string Text(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "null")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
