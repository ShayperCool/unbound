using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Plays NavMesh agent configurations and records each agent's state frame by frame in full
/// precision, for working out Unity's agent avoidance. <c>AVOIDANCE_CONFIG</c> holds one
/// configuration per line: <c>frames;agent;agent;...</c>, an agent being
/// <c>x,z,destX,destZ,speed,radius,quality,priority,velX,velZ,acceleration</c> (no destination
/// when destX is "-"). Configurations play in batches of 100, each on its own 80 m cell of the
/// plane (the cells' agents are out of each other's reach); <c>AVOIDANCE_OUT</c> gets one line per
/// configuration, agent and frame.
/// </summary>
public class AvoidanceProbe : MonoBehaviour
{
    const int BatchSize = 100;
    const float Cell = 80f;

    sealed class Config
    {
        internal int Id, Frames;
        internal List<float[]> Agents = new List<float[]>();
        internal List<NavMeshAgent> Live = new List<NavMeshAgent>();
    }

    readonly List<Config> m_Configs = new List<Config>();
    readonly StringBuilder m_Out = new StringBuilder();
    int m_Batch = -1, m_Frame, m_BatchFrames;

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static string R(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => R(v.x) + "," + R(v.y) + "," + R(v.z);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (Environment.GetEnvironmentVariable("AVOIDANCE_CONFIG") == null) return;
        new GameObject("AvoidanceProbe").AddComponent<AvoidanceProbe>();
    }

    void Start()
    {
        Time.captureDeltaTime = 0.02f;
        // Every agent of a batch gets its path in the first frame.
        NavMesh.pathfindingIterationsPerFrame = 1000000;
        int id = 0;
        foreach (var raw in File.ReadAllLines(Environment.GetEnvironmentVariable("AVOIDANCE_CONFIG")))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var parts = line.Split(';');
            var c = new Config { Id = id++, Frames = int.Parse(parts[0], CultureInfo.InvariantCulture) };
            for (int i = 1; i < parts.Length; i++)
            {
                var f = parts[i].Split(',');
                var a = new float[f.Length];
                for (int k = 0; k < f.Length; k++) a[k] = f[k] == "-" ? float.NaN : F(f[k]);
                c.Agents.Add(a);
            }
            m_Configs.Add(c);
        }
    }

    void StartBatch()
    {
        m_Batch++;
        m_Frame = 0;
        m_BatchFrames = 0;
        for (int i = m_Batch * BatchSize; i < Math.Min(m_Configs.Count, (m_Batch + 1) * BatchSize); i++)
        {
            var c = m_Configs[i];
            int slot = i - m_Batch * BatchSize;
            var origin = new Vector3((slot % 10) * Cell - 400f, 0f, (slot / 10) * Cell - 400f);
            m_BatchFrames = Math.Max(m_BatchFrames, c.Frames);
            for (int k = 0; k < c.Agents.Count; k++)
            {
                var a = c.Agents[k];
                var go = new GameObject($"c{c.Id}a{k}");
                go.transform.position = origin + new Vector3(a[0], 0f, a[1]);
                var agent = go.AddComponent<NavMeshAgent>();
                agent.speed = a[4];
                agent.radius = a[5];
                agent.obstacleAvoidanceType = (ObstacleAvoidanceType)(int)a[6];
                agent.avoidancePriority = (int)a[7];
                if (a.Length > 10) agent.acceleration = a[10];
                if (!float.IsNaN(a[2])) agent.SetDestination(origin + new Vector3(a[2], 0f, a[3]));
                if (a[8] != 0f || a[9] != 0f) agent.velocity = new Vector3(a[8], 0f, a[9]);
                c.Live.Add(agent);
            }
        }
    }

    void LateUpdate()
    {
        if (m_Batch < 0 || m_Frame >= m_BatchFrames)
        {
            if (m_Batch >= 0)
            {
                foreach (var c in m_Configs)
                {
                    foreach (var a in c.Live) Destroy(a.gameObject);
                    c.Live.Clear();
                }
            }
            if ((m_Batch + 1) * BatchSize >= m_Configs.Count)
            {
                File.WriteAllText(Environment.GetEnvironmentVariable("AVOIDANCE_OUT"), m_Out.ToString());
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#else
                Application.Quit();
#endif
                enabled = false;
                return;
            }
            StartBatch();
            return;
        }
        m_Frame++;
        for (int i = m_Batch * BatchSize; i < Math.Min(m_Configs.Count, (m_Batch + 1) * BatchSize); i++)
        {
            var c = m_Configs[i];
            if (m_Frame > c.Frames) continue;
            int slot = i - m_Batch * BatchSize;
            var origin = new Vector3((slot % 10) * Cell - 400f, 0f, (slot / 10) * Cell - 400f);
            for (int k = 0; k < c.Live.Count; k++)
            {
                var a = c.Live[k];
                m_Out.Append(c.Id).Append(' ').Append(k).Append(' ').Append(m_Frame)
                    .Append(" p").Append(V(a.transform.position - origin)).Append(" v").Append(V(a.velocity))
                    .Append(" dv").Append(V(a.desiredVelocity)).Append(" n").Append(V(a.nextPosition - origin))
                    .Append(" st").Append(V(a.steeringTarget - origin)).Append('\n');
            }
        }
    }
}
