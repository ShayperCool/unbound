using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Stationary natural death isolates Death-child initialization from ordinary traversal.
public class DeathTimingProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static float s_ExternalDelta = .025f;
    static bool s_RecordEventDelta;
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[32];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("DeathTimingProbe").AddComponent<DeathTimingProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string C(Color32 c) => "[" + c.r + "," + c.g + "," + c.b + "," + c.a + "]";
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    static void RecordState(string scenario, string step, ParticleSystem ps)
    {
        int count = ps.particleCount;
        if (s_Buffer.Length < count) s_Buffer = new ParticleSystem.Particle[count * 2];
        // Unity leaves the fields a system does not use as they were in the buffer.
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        s_Out.Append("{\"scenario\":").Append(Q(scenario)).Append(",\"step\":").Append(Q(step));
        if (s_RecordEventDelta)
        {
            s_Out.Append(",\"externalDelta\":").Append(F(s_ExternalDelta));
            s_Out.Append(",\"deltaTime\":").Append(F(Time.deltaTime));
        }
        s_Out.Append(",\"fixedDeltaTime\":").Append(F(Time.fixedDeltaTime));
        s_Out.Append(",\"maximumParticleDeltaTime\":").Append(F(Time.maximumParticleDeltaTime));
        s_Out.Append(",\"count\":").Append(count);
        s_Out.Append(",\"readCount\":").Append(n);
        s_Out.Append(",\"time\":").Append(F(ps.time));
        s_Out.Append(",\"playing\":").Append(ps.isPlaying ? "true" : "false");
        s_Out.Append(",\"emitting\":").Append(ps.isEmitting ? "true" : "false");
        s_Out.Append(",\"stopped\":").Append(ps.isStopped ? "true" : "false");
        s_Out.Append(",\"paused\":").Append(ps.isPaused ? "true" : "false");
        s_Out.Append(",\"alive\":").Append(ps.IsAlive(true) ? "true" : "false");
        s_Out.Append(",\"particles\":[");
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            if (i > 0) s_Out.Append(',');
            s_Out.Append("{\"pos\":").Append(V(p.position));
            s_Out.Append(",\"vel\":").Append(V(p.velocity));
            s_Out.Append(",\"totalVel\":").Append(V(p.totalVelocity));
            s_Out.Append(",\"animatedVel\":").Append(V(p.animatedVelocity));
            s_Out.Append(",\"axis\":").Append(V(p.axisOfRotation));
            s_Out.Append(",\"life\":").Append(F(p.remainingLifetime));
            s_Out.Append(",\"seed\":").Append(p.randomSeed);
            s_Out.Append(",\"startLife\":").Append(F(p.startLifetime));
            s_Out.Append(",\"startSize\":").Append(V(p.startSize3D));
            s_Out.Append(",\"size\":").Append(V(p.GetCurrentSize3D(ps)));
            s_Out.Append(",\"startColor\":").Append(C(p.startColor));
            s_Out.Append(",\"color\":").Append(C(p.GetCurrentColor(ps)));
            s_Out.Append(",\"rot\":").Append(V(p.rotation3D));
            s_Out.Append(",\"angVel\":").Append(V(p.angularVelocity3D));
            s_Out.Append('}');
        }
        s_Out.Append("]}\n");
    }


    static ParticleSystem Make(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 12345;
        var m = ps.main;
        m.playOnAwake = false; m.startLifetime = 2.013f; m.startSpeed = 2f; m.startSize = 1f;
        var shape = ps.shape; shape.enabled = false;
        var e = ps.emission; e.enabled = false;
        go.SetActive(true);
        return ps;
    }

    void Start()
    {
        float originalFixedDeltaTime = Time.fixedDeltaTime;
        try
        {
            string fixedStep = Environment.GetEnvironmentVariable("PARTICLE_DEATH_FIXED_DELTA");
            if (!string.IsNullOrEmpty(fixedStep))
                Time.fixedDeltaTime = float.Parse(fixedStep, CultureInfo.InvariantCulture);
            string externalDelta = Environment.GetEnvironmentVariable("PARTICLE_DEATH_EXTERNAL_DELTA");
            if (!string.IsNullOrEmpty(externalDelta))
            {
                s_ExternalDelta = float.Parse(externalDelta, CultureInfo.InvariantCulture);
                s_RecordEventDelta = true;
            }
            Case();
        }
        finally { Time.fixedDeltaTime = originalFixedDeltaTime; }
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_DEATH_TIMING_OUT"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Case()
    {
        var parent = Make("stationary_natural_death");
        var death = Make("death_child", parent.transform);
        var main = death.main; main.startSpeed = 1f;
        var emission = death.emission;
        emission.enabled = true; emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(death, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        var ordinary = Make("ordinary_child", parent.transform);
        main = ordinary.main; main.startSpeed = 1f;
        parent.SetParticles(new[] { Particle(new Vector3(0f, 0f, .46f), Vector3.zero, .013f) }, 1);
        ordinary.SetParticles(new[] { Particle(Vector3.zero, Vector3.forward, 2.013f) }, 1);
        Record("before", parent, death, ordinary);
        for (int i = 1; i <= 2; i++)
        {
            parent.Simulate(s_ExternalDelta, true, false, false);
            Record("after" + i.ToString(CultureInfo.InvariantCulture), parent, death, ordinary);
        }
        DestroyImmediate(parent.gameObject);
    }

    static ParticleSystem.Particle Particle(Vector3 position, Vector3 velocity, float lifetime)
        => new ParticleSystem.Particle {
            position = position, velocity = velocity,
            startLifetime = lifetime, remainingLifetime = lifetime, startSize = 1f,
            startColor = new Color32(255, 255, 255, 255), randomSeed = 12345
        };

    static void Record(string step, ParticleSystem parent, ParticleSystem death, ParticleSystem ordinary)
    {
        RecordState("stationary_natural_death", step, parent);
        RecordState("death_child", step, death);
        RecordState("ordinary_child", step, ordinary);
    }
}
