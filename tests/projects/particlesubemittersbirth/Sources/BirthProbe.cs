using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Public parent/birth-child histories with no collision or trigger modules.
public class BirthProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[4096];
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("BirthProbe").AddComponent<BirthProbe>();
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string C(Color32 c) => "[" + c.r + "," + c.g + "," + c.b + "," + c.a + "]";
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    /// <summary>A system on an inactive object, stopped and cleared, with a fixed seed, no
    /// shape and no play on awake; <paramref name="configure"/> sets the rest.</summary>
    static ParticleSystem Make(string name, Action<ParticleSystem> configure, Vector3 position = default, Transform parent = null)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 12345;
        var main = ps.main;
        main.playOnAwake = false;
        main.startLifetime = 1.013f;
        main.startSpeed = 2f;
        main.startSize = 1f;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 10f;
        configure(ps);
        go.SetActive(true);
        return ps;
    }

    static void RecordState(string scenario, string step, ParticleSystem ps)
    {
        int count = ps.particleCount;
        if (s_Buffer.Length < count) s_Buffer = new ParticleSystem.Particle[count * 2];
        // Unity leaves the fields a system does not use as they were in the buffer.
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        s_Out.Append("{\"scenario\":").Append(Q(scenario)).Append(",\"step\":").Append(Q(step));
        s_Out.Append(",\"count\":").Append(count);
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
            s_Out.Append(",\"life\":").Append(F(p.remainingLifetime));
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


    void Start()
    {
        RunCase(false);
        RunCase(true);
        RunCase(true, false);
        RunCase(true, false, true);
        RunPhaseCase("phase_event_zero", 0f, 0f, 13f);
        RunPhaseCase("phase_event_inside", 0.017f, 0f, 13f);
        RunPhaseCase("phase_child_delay", 0.017f, 0.023f, 13f);
        RunPhaseCase("phase_child_rate", 0.017f, 0f, 17f);
        RunPhaseCase("phase_burst", 0.017f, 0f, 0f, 0.031f);
        RunPhaseCase("phase_burst_delay", 0.017f, 0.023f, 0f, 0.031f);
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_BIRTH_OUT"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    // One event-time discriminator: move the parent's event within an irregular call,
    // then vary only the child's delay, rate, or burst clock. All particles outlive it.
    static void RunPhaseCase(string name, float parentBurst, float childDelay, float childRate, float childBurst = -1f)
    {
        float[] deltas = { 0.013f, 0.011f, 0.019f, 0.037f, 0.041f, 0.007f, 0.053f, 0.029f, 0.061f };
        ParticleSystem child = null;
        var ps = Make(name, s =>
        {
            var m = s.main;
            m.startSize = 2f;
            m.startColor = (Color)new Color32(128, 192, 64, 255);
            var e = s.emission;
            e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(parentBurst, 1) });
            child = Make(name + "_child", c =>
            {
                var cm = c.main;
                cm.startSpeed = 0.5f;
                cm.startLifetime = 0.413f;
                cm.startDelay = childDelay;
                var ce = c.emission;
                ce.rateOverTime = childRate;
                if (childBurst >= 0f) ce.SetBursts(new[] { new ParticleSystem.Burst(childBurst, 1) });
            }, default, s.transform);
            var sub = s.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth,
                ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize);
        });
        s_Out.Append("{\"scenario\":").Append(Q(name + " inputs")).Append(",\"step\":\"configured\",\"value\":{")
            .Append("\"parentBurst\":").Append(F(parentBurst))
            .Append(",\"childDelay\":").Append(F(childDelay))
            .Append(",\"childRate\":").Append(F(childRate))
            .Append(",\"childBurst\":").Append(F(childBurst))
            .Append(",\"maximumParticleDeltaTime\":").Append(F(Time.maximumParticleDeltaTime))
            .Append(",\"parentSpace\":").Append((int)ps.main.simulationSpace)
            .Append(",\"childSpace\":").Append((int)child.main.simulationSpace)
            .Append(",\"properties\":").Append((int)ps.subEmitters.GetSubEmitterProperties(0))
            .Append(",\"deltas\":[");
        for (int i = 0; i < deltas.Length; i++)
        {
            if (i > 0) s_Out.Append(',');
            s_Out.Append(F(deltas[i]));
        }
        s_Out.Append("]}}\n");
        for (int i = 0; i < deltas.Length; i++)
        {
            ps.Simulate(deltas[i], true, i == 0, false);
            string step = i.ToString(CultureInfo.InvariantCulture);
            RecordState(name, step, ps);
            RecordState(name + "_child", step, child);
        }
        DestroyImmediate(ps.gameObject);
    }

    static void RunCase(bool single, bool withChildren = true, bool detached = false)
    {
        string name = single ? "single_birth" : "sub_emitters_birth";
        if (!withChildren) name += "_parent_only";
        if (detached) name += "_detached";
        ParticleSystem child = null;
        var ps = Make(name, s =>
        {
            var e = s.emission;
            e.rateOverTime = single ? 0f : 5f;
            if (single) e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            if (single)
            {
                var m = s.main;
                m.startSize = 2f;
                m.startColor = (Color)new Color32(128, 192, 64, 255);
            }
            child = Make(name + "_child", c =>
            {
                var cm = c.main;
                cm.startSpeed = 0.5f;
                cm.startLifetime = 0.413f;
                var ce = c.emission; ce.rateOverTime = 10f;
            }, default, s.transform);
            if (detached) child.transform.SetParent(null, true);
            var sub = s.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth,
                ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize);
        });
        // Capture the public inputs after registration, including any space conversion.
        s_Out.Append("{\"scenario\":").Append(Q(name+" inputs")).Append(",\"step\":\"configured\",\"value\":{")
            .Append("\"parentSpace\":").Append((int)ps.main.simulationSpace)
            .Append(",\"childSpace\":").Append((int)child.main.simulationSpace)
            .Append(",\"parentPosition\":").Append(V(ps.transform.position))
            .Append(",\"childPosition\":").Append(V(child.transform.position))
            .Append(",\"parentScale\":").Append(V(ps.transform.lossyScale))
            .Append(",\"childScale\":").Append(V(child.transform.lossyScale))
            .Append(",\"properties\":").Append((int)ps.subEmitters.GetSubEmitterProperties(0))
            .Append("}}\n");
        for (int i = 0; i < (single ? 10 : 15); i++)
        {
            ps.Simulate(0.05f, withChildren, i == 0, false);
            string step = i.ToString(CultureInfo.InvariantCulture);
            RecordState(name, step, ps);
            RecordState(name + "_child", step, child);
        }
        if (detached) DestroyImmediate(child.gameObject);
        DestroyImmediate(ps.gameObject);
    }
}
