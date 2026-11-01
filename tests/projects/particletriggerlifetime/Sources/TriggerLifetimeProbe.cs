using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Each case uses one parent particle and an explicitly synchronized collider.
public class TriggerLifetimeProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[32];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("TriggerLifetimeProbe").AddComponent<TriggerLifetimeProbe>();

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
        Case("single_crossing", true, false, false, 2.013f);
        Case("natural_death", false, false, false, .013f);
        Case("natural_zero_boundary", false, false, false, .025f);
        Case("newborn_inside", true, true, false, 2.013f);
        Case("trigger_death_child", true, false, true, 2.013f);
        Case("natural_death_child", false, false, true, .013f);
        Case("single_combined", true, false, false, 2.013f, .05f);
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_TRIGGER_LIFETIME_OUT"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Case(string name, bool kill, bool newborn, bool deathChild, float lifetime, float dt = .025f)
    {
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = new Vector3(0f, 0f, 1.5f);
        var collider = sphere.GetComponent<Collider>();
        collider.isTrigger = true;
        Physics.SyncTransforms();
        var ps = Make(name);
        if (kill)
        {
            var t = ps.trigger; t.enabled = true; t.AddCollider(collider);
            t.inside = ParticleSystemOverlapAction.Kill;
            t.outside = t.enter = t.exit = ParticleSystemOverlapAction.Ignore;
        }
        ParticleSystem child = null;
        if (deathChild)
        {
            child = Make(name + "_child", ps.transform);
            var m = child.main; m.startSpeed = 0f;
            var e = child.emission; e.enabled = true; e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var sub = ps.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        }
        if (newborn)
        {
            ps.transform.position = new Vector3(0f, 0f, .51f);
            var e = ps.emission; e.enabled = true; e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            ps.Play(false);
        }
        else
        {
            ps.SetParticles(new[] { new ParticleSystem.Particle {
                position = new Vector3(0f, 0f, .46f), velocity = new Vector3(0f, 0f, 2f),
                startLifetime = lifetime, remainingLifetime = lifetime, startSize = 1f,
                startColor = new Color32(255, 255, 255, 255), randomSeed = 12345
            } }, 1);
        }
        RecordState(name, "before", ps);
        if (child != null) RecordState(name + " child", "before", child);
        for (int i = 1; i <= 2; i++)
        {
            ps.Simulate(dt, false, false, false);
            string step = "after" + i.ToString(CultureInfo.InvariantCulture);
            RecordState(name, step, ps);
            if (child != null) RecordState(name + " child", step, child);
        }
        DestroyImmediate(ps.gameObject);
        DestroyImmediate(sphere);
    }
}
