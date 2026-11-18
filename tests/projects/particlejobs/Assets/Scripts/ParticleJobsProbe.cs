using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace ParticleJobsProbe
{
    /// <summary>Copies what a job sees of the first particles.</summary>
    struct ReadJob : IJobParticleSystem
    {
        public const int Stride = 32, Particles = 3;
        public NativeArray<float> Values;
        public NativeArray<uint> Seeds;
        public NativeArray<int> Info;

        static void Put3(NativeArray<float> o, int at, ParticleSystemNativeArray3 a, int i)
        {
            o[at] = a.x[i]; o[at + 1] = a.y[i]; o[at + 2] = a.z[i];
        }

        public void Execute(ParticleSystemJobData d)
        {
            Info[0] = d.count;
            Info[1] = d.customData1.x.IsCreated ? 1 : 0;
            Info[2] = d.customData1.x.Length;
            Info[3] = d.customData2.x.IsCreated ? 1 : 0;
            Info[4] = d.customData2.x.Length;
            Info[5] = d.positions.x.Length;
            Info[6] = d.meshIndices.IsCreated ? 1 : 0;
            Info[7] = d.startColors.Length;
            for (int i = 0; i < Mathf.Min(d.count, Particles); i++)
            {
                int at = i * Stride;
                Put3(Values, at, d.positions, i);
                Put3(Values, at + 3, d.velocities, i);
                Put3(Values, at + 6, d.axisOfRotations, i);
                Put3(Values, at + 9, d.rotations, i);
                Put3(Values, at + 12, d.rotationalSpeeds, i);
                Put3(Values, at + 15, d.sizes, i);
                var c = d.startColors[i];
                Values[at + 18] = c.r; Values[at + 19] = c.g; Values[at + 20] = c.b; Values[at + 21] = c.a;
                Values[at + 22] = d.aliveTimePercent[i];
                Values[at + 23] = d.inverseStartLifetimes[i];
                if (d.customData1.x.IsCreated)
                {
                    var v = d.customData1[i];
                    Values[at + 24] = v.x; Values[at + 25] = v.y; Values[at + 26] = v.z; Values[at + 27] = v.w;
                }
                if (d.customData2.x.IsCreated)
                {
                    var v = d.customData2[i];
                    Values[at + 28] = v.x; Values[at + 29] = v.y; Values[at + 30] = v.z; Values[at + 31] = v.w;
                }
                Seeds[i] = d.randomSeeds[i];
                Info[8 + i] = d.meshIndices.IsCreated ? d.meshIndices[i] : -1;
            }
        }
    }

    /// <summary>Writes known values into every particle.</summary>
    struct WriteJob : IJobParticleSystem
    {
        public void Execute(ParticleSystemJobData d)
        {
            var positions = d.positions;
            var velocities = d.velocities;
            var axes = d.axisOfRotations;
            var rotations = d.rotations;
            var speeds = d.rotationalSpeeds;
            var sizes = d.sizes;
            var colors = d.startColors;
            var alive = d.aliveTimePercent;
            var inverse = d.inverseStartLifetimes;
            var seeds = d.randomSeeds;
            var custom = d.customData1;
            for (int i = 0; i < d.count; i++)
            {
                positions[i] = new Vector3(10 + i, 20 + i, 30 + i);
                velocities[i] = new Vector3(i, -1f, 0.5f);
                axes[i] = new Vector3(1f, 0f, 0f);
                rotations[i] = new Vector3(0.25f, 0.5f, 1f);
                speeds[i] = new Vector3(0f, 0f, 2f);
                sizes[i] = new Vector3(2f, 3f, 4f + i);
                colors[i] = new Color32(200, 100, 50, (byte)(25 + i));
                alive[i] = 25f + 10f * i;
                if (i == 2) inverse[i] = 0.125f;
                seeds[i] = 999u + (uint)i;
                if (custom.x.IsCreated) custom[i] = new Vector4(9f, 8f, 7f, 6f + i);
            }
        }
    }

    struct ChainFirstJob : IJobParticleSystem
    {
        public void Execute(ParticleSystemJobData d)
        {
            var x = d.positions.x;
            x[0] = 5f;
        }
    }

    struct ChainSecondJob : IJobParticleSystem
    {
        public NativeArray<float> Seen;

        public void Execute(ParticleSystemJobData d)
        {
            var x = d.positions.x;
            Seen[0] = x[0];
            x[0] = x[0] + 1f;
        }
    }

    struct ParallelJob : IJobParticleSystemParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Hits;
        [NativeDisableParallelForRestriction] public NativeArray<int> Counts;

        public void Execute(ParticleSystemJobData d, int index)
        {
            Hits[index] = Hits[index] + 1;
            Counts[index] = d.count;
        }
    }

    struct BatchJob : IJobParticleSystemParallelForBatch
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> Batches;

        public void Execute(ParticleSystemJobData d, int startIndex, int count) => Batches[startIndex] = count;
    }

    struct KillJob : IJobParticleSystem
    {
        public void Execute(ParticleSystemJobData d)
        {
            var alive = d.aliveTimePercent;
            alive[0] = 100f;
            alive[1] = 150f;
        }
    }

    struct CountJob : IJobParticleSystem
    {
        public NativeArray<int> Count;

        public void Execute(ParticleSystemJobData d) => Count[0] = d.count;
    }

    /// <summary>Receives the message on a probe system's object and hands it to the probe.</summary>
    public class JobCallback : MonoBehaviour
    {
        public string label;

        void OnParticleUpdateJobScheduled() => ParticleJobsBehaviour.Scheduled(this);
    }

    /// <summary>The message with the parameter UniTask's trigger declares.</summary>
    public class ParameterCallback : MonoBehaviour
    {
        void OnParticleUpdateJobScheduled(ParticleSystemJobData data) => ParticleJobsBehaviour.ScheduledWithData(data);
    }

    /// <summary>
    /// The particle job API (IJobParticleSystem, IJobParticleSystemParallelFor,
    /// IJobParticleSystemParallelForBatch) and MonoBehaviour.OnParticleUpdateJobScheduled: when the
    /// message comes (which systems, which scripts, against Update and LateUpdate), what a job sees
    /// of the particles, what its writes do, chained jobs, the parallel ranges, a job's kill, and
    /// scheduling outside the message.
    /// </summary>
    public class ParticleJobsBehaviour : MonoBehaviour
    {
        const string Variable = "PARTICLE_JOBS_OUT";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static ParticleJobsBehaviour s_Probe;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly HashSet<string> m_Keys = new HashSet<string>();
        readonly List<string> m_Frame = new List<string>();
        readonly Dictionary<string, int> m_Calls = new Dictionary<string, int>();
        readonly List<string> m_Logs = new List<string>();

        ParticleSystem m_A, m_Other, m_Empty, m_Stopped, m_Paused, m_Simulated, m_StopClearLater, m_StopLater, m_NeverPlayed;
        readonly Dictionary<string, string> m_Phases = new Dictionary<string, string>();
        JobCallback m_ACallback;
        int m_Phase;
        string m_Mode = "";
        bool m_InSimulate;
        string m_SimulateCalls = "";
        JobHandle m_Handle;
        NativeArray<float> m_Values, m_Seen;
        NativeArray<uint> m_Seeds;
        NativeArray<int> m_Info, m_Hits, m_Counts, m_Batches, m_Count;
        int m_ParameterCalls;
        int m_ParameterCount = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
            new GameObject("probe").AddComponent<ParticleJobsBehaviour>();
        }

        void Put(string key, object value)
        {
            if (!m_Keys.Add(key)) throw new InvalidOperationException("duplicate key " + key);
            m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(value is bool b ? (b ? "true" : "false") : Json(Text(value))).Append("}\n");
        }

        static string Text(object value) => value switch
        {
            float f => f.ToString("R", Inv),
            Vector3 v => v.x.ToString("R", Inv) + " " + v.y.ToString("R", Inv) + " " + v.z.ToString("R", Inv),
            Vector4 v => v.x.ToString("R", Inv) + " " + v.y.ToString("R", Inv) + " " + v.z.ToString("R", Inv) + " " + v.w.ToString("R", Inv),
            Color32 c => c.r + " " + c.g + " " + c.b + " " + c.a,
            _ => Convert.ToString(value, Inv),
        };

        static string Json(string text)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static string Try(Func<JobHandle> schedule)
        {
            try
            {
                schedule().Complete();
                return "ok";
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        ParticleSystem MakeSystem(string name, bool customData, Vector3 at, bool neverPlay = false)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            if (neverPlay) go.SetActive(false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            if (neverPlay) main.playOnAwake = false;
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            main.playOnAwake = false;
            main.loop = true;
            main.startSpeed = 0f;
            main.startLifetime = 4f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = 100;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            if (customData)
            {
                var custom = ps.customData;
                custom.enabled = true;
                custom.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
                custom.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 4);
                for (int i = 0; i < 4; i++) custom.SetVector(ParticleSystemCustomData.Custom1, i, i + 1f);
            }
            var callback = go.AddComponent<JobCallback>();
            callback.label = name;
            m_Calls[name] = 0;
            if (neverPlay) go.SetActive(true);
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, Vector3 size, Vector3 rotation, Vector3 spin,
            Color32 color, float lifetime, uint seed)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = velocity,
                startSize3D = size,
                rotation3D = rotation,
                angularVelocity3D = spin,
                axisOfRotation = new Vector3(0f, 1f, 0f),
                startColor = color,
                startLifetime = lifetime,
                randomSeed = seed,
            };
            ps.Emit(e, 1);
        }

        static ParticleSystem.Particle[] Particles(ParticleSystem ps)
        {
            var particles = new ParticleSystem.Particle[ps.particleCount];
            ps.GetParticles(particles);
            return particles;
        }

        void Start()
        {
            s_Probe = this;
            Application.logMessageReceived += (condition, stack, type) => m_Logs.Add(type + ": " + condition);
            m_Values = new NativeArray<float>(ReadJob.Stride * ReadJob.Particles, Allocator.Persistent);
            m_Seeds = new NativeArray<uint>(ReadJob.Particles, Allocator.Persistent);
            m_Info = new NativeArray<int>(16, Allocator.Persistent);
            m_Seen = new NativeArray<float>(1, Allocator.Persistent);
            m_Hits = new NativeArray<int>(8, Allocator.Persistent);
            m_Counts = new NativeArray<int>(8, Allocator.Persistent);
            m_Batches = new NativeArray<int>(8, Allocator.Persistent);
            m_Count = new NativeArray<int>(1, Allocator.Persistent);

            m_A = MakeSystem("A", true, Vector3.zero);
            m_ACallback = m_A.GetComponent<JobCallback>();
            var second = m_A.gameObject.AddComponent<JobCallback>();
            second.label = "A2";
            m_Calls["A2"] = 0;
            var disabled = m_A.gameObject.AddComponent<JobCallback>();
            disabled.label = "Adisabled";
            disabled.enabled = false;
            m_Calls["Adisabled"] = 0;
            m_A.gameObject.AddComponent<ParameterCallback>();
            m_Other = MakeSystem("Other", false, new Vector3(50f, 0f, 0f));
            m_Empty = MakeSystem("Empty", false, new Vector3(10f, 0f, 0f));
            m_Stopped = MakeSystem("Stopped", false, new Vector3(20f, 0f, 0f));
            m_Paused = MakeSystem("Paused", false, new Vector3(30f, 0f, 0f));
            m_Simulated = MakeSystem("Simulated", false, new Vector3(40f, 0f, 0f));
            m_StopClearLater = MakeSystem("StopClearLater", false, new Vector3(60f, 0f, 0f));
            m_StopLater = MakeSystem("StopLater", false, new Vector3(70f, 0f, 0f));
            m_NeverPlayed = MakeSystem("NeverPlayed", false, new Vector3(80f, 0f, 0f), neverPlay: true);
            m_StopClearLater.Play();
            m_StopLater.Play();
            m_A.Play();
            m_Other.Play();
            m_Empty.Play();
            m_Paused.Play();
        }

        internal static void Scheduled(JobCallback callback) => s_Probe.OnScheduled(callback);

        internal static void ScheduledWithData(ParticleSystemJobData data)
        {
            s_Probe.m_ParameterCalls++;
            s_Probe.m_ParameterCount = data.count;
        }

        void OnScheduled(JobCallback callback)
        {
            m_Calls[callback.label]++;
            m_Phases.TryGetValue(callback.label, out var phases);
            m_Phases[callback.label] = (phases == null ? "" : phases + " ") + m_Phase;
            if (m_InSimulate) m_SimulateCalls += callback.label + " ";
            // The order of different systems' messages is not recorded (Unity does not specify it).
            if (callback.label == "A" || callback.label == "A2") m_Frame.Add("message " + callback.label);
            if (callback.label == "Other" && m_Mode == "other")
            {
                m_Mode = "";
                Put("schedule.otherSystemInMessage", Try(() => new CountJob { Count = m_Count }.Schedule(m_A)));
                Put("schedule.otherSystemInMessage.count", m_Count[0]);
            }
            if (callback != m_ACallback || m_Mode == "other") return;
            switch (m_Mode)
            {
                case "read":
                    m_Handle = new ReadJob { Values = m_Values, Seeds = m_Seeds, Info = m_Info }.Schedule(m_A);
                    break;
                case "write":
                    m_Handle = new WriteJob().Schedule(m_A);
                    Put("sync.inMessage.position0", Particles(m_A)[0].position);
                    break;
                case "chain":
                    new ChainFirstJob().Schedule(m_A);
                    m_Handle = new ChainSecondJob { Seen = m_Seen }.Schedule(m_A);
                    break;
                case "parallel":
                    var parallel = new ParallelJob { Hits = m_Hits, Counts = m_Counts }.Schedule(m_A, 1);
                    m_Handle = new BatchJob { Batches = m_Batches }.ScheduleBatch(m_A, 2, parallel);
                    break;
                case "kill":
                    m_Handle = new KillJob().Schedule(m_A);
                    break;
            }
            m_Mode = "";
        }

        void Update()
        {
            m_Phase++;
            m_Frame.Clear();
            m_Frame.Add("Update");
            switch (m_Phase)
            {
                case 1:
                    Emit(m_A, new Vector3(1f, 2f, 3f), Vector3.zero, new Vector3(0.5f, 0.75f, 1.5f), new Vector3(90f, 0f, 45f), Vector3.zero,
                        new Color32(10, 20, 30, 40), 4f, 1234u);
                    Emit(m_A, Vector3.zero, new Vector3(1f, 0f, 0f), Vector3.one, Vector3.zero, new Vector3(0f, 0f, 30f),
                        new Color32(255, 255, 255, 255), 4f, 5678u);
                    Emit(m_A, new Vector3(-1f, 0f, 0f), Vector3.zero, Vector3.one, Vector3.zero, Vector3.zero,
                        new Color32(1, 2, 3, 4), 2f, 77u);
                    Emit(m_Other, Vector3.zero, Vector3.zero, Vector3.one, Vector3.zero, Vector3.zero, new Color32(1, 1, 1, 1), 4f, 1u);
                    Emit(m_Paused, Vector3.zero, Vector3.zero, Vector3.one, Vector3.zero, Vector3.zero, new Color32(1, 1, 1, 1), 4f, 1u);
                    m_Paused.Pause();
                    Emit(m_Simulated, Vector3.zero, Vector3.zero, Vector3.one, Vector3.zero, Vector3.zero, new Color32(1, 1, 1, 1), 4f, 1u);
                    Put("schedule.inUpdate", Try(() => new CountJob { Count = m_Count }.Schedule(m_A)));
                    Put("scheduleParallel.inUpdate", Try(() => new ParallelJob { Hits = m_Hits, Counts = m_Counts }.Schedule(m_A, 1)));
                    Put("scheduleBatch.inUpdate", Try(() => new BatchJob { Batches = m_Batches }.ScheduleBatch(m_A, 2)));
                    m_Mode = "read";
                    break;
                case 2:
                    m_Mode = "write";
                    break;
                case 3:
                {
                    var p = Particles(m_A);
                    Put("write.count", p.Length);
                    for (int i = 0; i < p.Length; i++)
                    {
                        string k = "write.p" + i + ".";
                        Put(k + "position", p[i].position);
                        Put(k + "velocity", p[i].velocity);
                        Put(k + "axisOfRotation", p[i].axisOfRotation);
                        Put(k + "rotation3D", p[i].rotation3D);
                        Put(k + "angularVelocity3D", p[i].angularVelocity3D);
                        Put(k + "startSize3D", p[i].startSize3D);
                        Put(k + "startColor", p[i].startColor);
                        Put(k + "remainingLifetime", p[i].remainingLifetime);
                        Put(k + "startLifetime", p[i].startLifetime);
                        Put(k + "randomSeed", p[i].randomSeed);
                    }
                    var custom = new List<Vector4>();
                    m_A.GetCustomParticleData(custom, ParticleSystemCustomData.Custom1);
                    Put("write.custom1", string.Join(" | ", custom.ConvertAll(v => Text(v))));
                    m_Mode = "chain";
                    break;
                }
                case 4:
                    // Playing empty systems stopped by a script, with and without clearing.
                    m_StopClearLater.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    m_StopLater.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    Put("stopLater.isPlaying", m_StopClearLater.isPlaying + " " + m_StopLater.isPlaying);
                    Put("chain.position0x", Particles(m_A)[0].position.x);
                    m_Mode = "parallel";
                    break;
                case 5:
                    m_Mode = "kill";
                    break;
                case 6:
                {
                    var p = Particles(m_A);
                    Put("kill.countBeforeNextUpdate", p.Length);
                    Put("kill.p0.remainingLifetime", p[0].remainingLifetime);
                    Put("kill.p1.remainingLifetime", p[1].remainingLifetime);
                    break;
                }
                case 7:
                    Put("kill.countAfterNextUpdate", m_A.particleCount);
                    m_InSimulate = true;
                    m_Simulated.Simulate(0.1f, true, false, true);
                    m_InSimulate = false;
                    Put("simulate.messages", m_SimulateCalls.Trim());
                    m_Mode = "other";
                    break;
                case 8:
                    foreach (var pair in m_Calls) Put("messages." + pair.Key, pair.Value);
                    foreach (var pair in m_Phases) Put("messagePhases." + pair.Key, pair.Value);
                    Put("messages.parameter", m_ParameterCalls);
                    Put("messages.parameter.count", m_ParameterCount);
                    Put("logs", string.Join(" | ", m_Logs));
                    Put("complete", true);
                    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), m_Out.ToString());
                    m_Values.Dispose(); m_Seeds.Dispose(); m_Info.Dispose(); m_Seen.Dispose();
                    m_Hits.Dispose(); m_Counts.Dispose(); m_Batches.Dispose(); m_Count.Dispose();
                    EditorApplication.Exit(0);
                    break;
            }
        }

        void LateUpdate()
        {
            m_Frame.Add("LateUpdate");
            switch (m_Phase)
            {
                case 1:
                {
                    Put("frame.order", string.Join(", ", m_Frame));
                    m_Handle.Complete();
                    Put("read.count", m_Info[0]);
                    Put("read.custom1.created", m_Info[1]);
                    Put("read.custom1.length", m_Info[2]);
                    Put("read.custom2.created", m_Info[3]);
                    Put("read.custom2.length", m_Info[4]);
                    Put("read.positions.length", m_Info[5]);
                    Put("read.meshIndices.created", m_Info[6]);
                    Put("read.startColors.length", m_Info[7]);
                    var p = Particles(m_A);
                    for (int i = 0; i < ReadJob.Particles; i++)
                    {
                        int at = i * ReadJob.Stride;
                        string k = "read.p" + i + ".";
                        Vector3 V(int o) => new Vector3(m_Values[at + o], m_Values[at + o + 1], m_Values[at + o + 2]);
                        // Values a frame's time step moves are compared with the particle's, not recorded.
                        Put(k + "positionMatchesParticle", V(0) == p[i].position);
                        if (i != 1) Put(k + "position", V(0));
                        Put(k + "velocity", V(3));
                        Put(k + "axisOfRotation", V(6));
                        Put(k + "rotationMatchesRadians", V(9) == p[i].rotation3D * Mathf.Deg2Rad);
                        if (i != 1) Put(k + "rotation", V(9));
                        Put(k + "rotationalSpeed", V(12));
                        Put(k + "size", V(15));
                        Put(k + "startColor", m_Values[at + 18] + " " + m_Values[at + 19] + " " + m_Values[at + 20] + " " + m_Values[at + 21]);
                        float alive = m_Values[at + 22];
                        float fromParticle = (1f - p[i].remainingLifetime / p[i].startLifetime) * 100f;
                        Put(k + "aliveTimePercentNearParticleAge", Mathf.Abs(alive - fromParticle) < 1e-3f);
                        Put(k + "aliveTimePercentInRange", alive > 0f && alive < 100f);
                        Put(k + "inverseStartLifetime", m_Values[at + 23]);
                        Put(k + "custom1", new Vector4(m_Values[at + 24], m_Values[at + 25], m_Values[at + 26], m_Values[at + 27]));
                        Put(k + "custom2", new Vector4(m_Values[at + 28], m_Values[at + 29], m_Values[at + 30], m_Values[at + 31]));
                        Put(k + "randomSeed", m_Seeds[i]);
                        Put(k + "meshIndex", m_Info[8 + i]);
                    }
                    break;
                }
                case 2:
                {
                    Put("sync.lateUpdate.position0", Particles(m_A)[0].position);
                    // The job finished (it runs once its batch is out), the update has not ended.
                    JobHandle.ScheduleBatchedJobs();
                    var waited = System.Diagnostics.Stopwatch.StartNew();
                    while (!m_Handle.IsCompleted && waited.ElapsedMilliseconds < 5000) System.Threading.Thread.Sleep(1);
                    Put("sync.lateUpdate.jobDone", m_Handle.IsCompleted);
                    Put("sync.lateUpdate.jobDone.position0", Particles(m_A)[0].position);
                    break;
                }
                case 3:
                    m_Handle.Complete();
                    Put("chain.secondSaw", m_Seen[0]);
                    break;
                case 4:
                    m_Handle.Complete();
                    Put("parallel.hits", string.Join(" ", m_Hits));
                    Put("parallel.counts", string.Join(" ", m_Counts));
                    Put("batch.starts", string.Join(" ", m_Batches));
                    break;
            }
        }
    }
}
