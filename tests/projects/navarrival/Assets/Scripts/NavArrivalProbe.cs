using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

// Where NavMeshAgents arrive. On a flat runtime NavMesh, agents walk straight to destinations at
// several speeds and accelerations, with and without autoBraking, over distances that leave
// different remainders before the last step. Each lane records the frame its velocity becomes zero
// and, for its last three frames, hasPath, the path's corner count and whether remainingDistance is
// positive. Straight paths between points from 0 to 1 mm apart record their corner counts. Only
// discrete values are recorded.
public class NavArrivalProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("NavArrivalProbe").AddComponent<NavArrivalProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    readonly List<(string name, NavMeshAgent agent)> lanes = new List<(string, NavMeshAgent)>();
    readonly Dictionary<string, List<string>> states = new Dictionary<string, List<string>>();
    readonly Dictionary<string, int> arrivals = new Dictionary<string, int>();
    int frame;
    const int Frames = 400;

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    static string R(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("NAV_ARRIVAL_OUT"); if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        Time.captureDeltaTime = 0.02f;
        var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(40f, 0.2f, 200f), transform = Matrix4x4.Translate(new Vector3(0f, -0.1f, 0f)), area = 0 } };
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, new Vector3(42f, 4f, 202f)), Vector3.zero, Quaternion.identity);
        NavMesh.AddNavMeshData(data);

        NavMesh.SamplePosition(new Vector3(1.3f, 0f, 2.7f), out var hit, 1f, NavMesh.AllAreas);
        var a = hit.position;
        foreach (float d in new[] { 0f, 1e-7f, 3e-7f, 1e-6f, 2e-6f, 4e-6f, 8e-6f, 1.6e-5f, 3e-5f, 5e-5f, 6e-5f, 7e-5f, 1e-4f, 1e-3f })
            foreach (var (dirName, dir) in new[] { ("x", Vector3.right), ("z", Vector3.forward), ("xz", new Vector3(0.6f, 0f, 0.8f)) })
            {
                var p = new NavMeshPath();
                bool ok = NavMesh.CalculatePath(a, a + dir * d, NavMesh.AllAreas, p);
                Put("query.d" + R(d) + "." + dirName, ok + " " + p.status + " " + p.corners.Length);
            }

        float z = -95f;
        int k = 0;
        foreach (var (speed, accel) in new[] { (3.5f, 60f), (1f, 8f), (2.3f, 8f), (5f, 20f) })
            for (int i = 0; i < 15; i++, k++)
                foreach (bool braking in new[] { true, false })
                    Lane($"k{k}.b{(braking ? 1 : 0)}", speed, accel, braking, 4f + i * 0.0137f, ref z);
        // Without braking, steps that end a few micrometres before the destination.
        Lane("fast.b0", 6f, 60f, false, 6f, ref z);
        Lane("slow.b0", 1f, 60f, false, 6f, ref z);
        Put("lanes", lanes.Count);
    }

    void Lane(string name, float speed, float accel, bool braking, float dist, ref float z)
    {
        var go = new GameObject(name);
        var start = new Vector3(-dist / 2f - 3f, 0f, z);
        go.transform.position = start;
        var agent = go.AddComponent<NavMeshAgent>();
        agent.speed = speed;
        agent.acceleration = accel;
        agent.autoBraking = braking;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        agent.SetDestination(start + new Vector3(dist, 0f, 0f));
        lanes.Add((name, agent));
        states[name] = new List<string>();
        z += 1.5f;
    }

    void LateUpdate()
    {
        if (output == null) return;
        frame++;
        foreach (var (name, agent) in lanes)
        {
            if (arrivals.ContainsKey(name)) continue;
            var list = states[name];
            list.Add("hp" + (agent.hasPath ? 1 : 0) + " c" + agent.path.corners.Length + " r" + (agent.remainingDistance > 0f ? "+" : "0"));
            if (list.Count > 3) list.RemoveAt(0);
            if (agent.velocity == Vector3.zero && frame > 3) arrivals[name] = frame;
        }
        if (frame < Frames) return;
        foreach (var (name, _) in lanes)
            Put("lane." + name, (arrivals.TryGetValue(name, out int f) ? "f" + f : "moving") + " | " + string.Join(" / ", states[name]));
        Put("complete", 1);
        output.Dispose();
        output = null;
        EditorApplication.Exit(0);
    }
}
