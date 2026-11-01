using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// All calls and observations occur in one Start, before any simulation or frame update.
public class SeedAxisProbe : MonoBehaviour
{
    static readonly StringBuilder Output = new StringBuilder();
    static readonly ParticleSystem.Particle[] Buffer = new ParticleSystem.Particle[8];
    static readonly uint[] ChildSeeds = { 12345u, 305419896u };
    static readonly uint[] ParentSeeds = { 12345u, 3735928559u };
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 x) => "[" + F(x.x) + "," + F(x.y) + "," + F(x.z) + "]";
    static string C(Color32 x) => "[" + x.r + "," + x.g + "," + x.b + "," + x.a + "]";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("SeedAxisProbe").AddComponent<SeedAxisProbe>();

    static ParticleSystem Make(string name, uint seed, Transform parent = null)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false; ps.randomSeed = seed;
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 8; main.startDelay = 0f;
        main.startLifetime = 2.013f; main.startSpeed = 1f;
        main.startSize3D = false; main.startSize = .75f;
        main.startRotation3D = false; main.startRotation = .3f; main.flipRotation = 0f;
        main.startColor = new Color32(192, 128, 224, 200); main.gravityModifier = 0f;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.enabled = true;
        emission.rateOverTime = 0f; emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        go.SetActive(true);
        return ps;
    }

    static ParticleSystem.Particle Parent(uint seed) => new ParticleSystem.Particle {
        position = Vector3.zero, velocity = Vector3.zero,
        startLifetime = 4.026f, remainingLifetime = 2.013f,
        startSize = 2f, startColor = new Color32(128, 192, 64, 255),
        rotation3D = new Vector3(10f, 20f, 30f),
        axisOfRotation = new Vector3(1f, 0f, 0f), randomSeed = seed
    };

    static void Particle(ParticleSystem.Particle p, ParticleSystem ps)
    {
        Output.Append("{\"pos\":").Append(V(p.position));
        Output.Append(",\"vel\":").Append(V(p.velocity));
        Output.Append(",\"totalVel\":").Append(V(p.totalVelocity));
        Output.Append(",\"animatedVel\":").Append(V(p.animatedVelocity));
        Output.Append(",\"axis\":").Append(V(p.axisOfRotation));
        Output.Append(",\"life\":").Append(F(p.remainingLifetime));
        Output.Append(",\"seed\":").Append(p.randomSeed);
        Output.Append(",\"startLife\":").Append(F(p.startLifetime));
        Output.Append(",\"startSize\":").Append(V(p.startSize3D));
        Output.Append(",\"size\":").Append(V(p.GetCurrentSize3D(ps)));
        Output.Append(",\"startColor\":").Append(C(p.startColor));
        Output.Append(",\"color\":").Append(C(p.GetCurrentColor(ps)));
        Output.Append(",\"rot\":").Append(V(p.rotation3D));
        Output.Append(",\"angVel\":").Append(V(p.angularVelocity3D)).Append('}');
    }

    static void Record(string scenario, int step, ParticleSystem child, ParticleSystem parent,
        ParticleSystem.Particle supplied, int flags, int order)
    {
        Array.Clear(Buffer, 0, Buffer.Length);
        int count = child.particleCount, n = child.GetParticles(Buffer);
        Output.Append("{\"scenario\":\"").Append(scenario).Append("\",\"step\":").Append(step);
        Output.Append(",\"kind\":\"").Append(parent == null ? "emit" : "manual").Append('"');
        Output.Append(",\"childSeed\":").Append(child.randomSeed);
        Output.Append(",\"flags\":").Append(flags).Append(",\"order\":").Append(order);
        Output.Append(",\"parentSystemSeed\":").Append(parent == null ? 0u : parent.randomSeed);
        Output.Append(",\"parentCount\":").Append(parent == null ? 0 : parent.particleCount);
        Output.Append(",\"supplied\":");
        if (parent == null) Output.Append("null"); else Particle(supplied, parent);
        Output.Append(",\"count\":").Append(count).Append(",\"readCount\":").Append(n);
        Output.Append(",\"time\":").Append(F(child.time));
        Output.Append(",\"playing\":").Append(child.isPlaying ? "true" : "false");
        Output.Append(",\"emitting\":").Append(child.isEmitting ? "true" : "false");
        Output.Append(",\"stopped\":").Append(child.isStopped ? "true" : "false");
        Output.Append(",\"paused\":").Append(child.isPaused ? "true" : "false");
        Output.Append(",\"alive\":").Append(child.IsAlive(true) ? "true" : "false");
        Output.Append(",\"particles\":[");
        for (int i = 0; i < n; ++i) { if (i != 0) Output.Append(','); Particle(Buffer[i], child); }
        Output.Append("]}\n");
    }

    void Start()
    {
        foreach (uint childSeed in ChildSeeds)
        {
            for (int flags = 0; flags <= 15; flags += 15)
            for (int order = 0; order < 2; ++order)
            {
                string name = "manual_" + childSeed + "_" + flags + "_" + order;
                var parent = Make(name + "_parent", 54321u);
                var pe = parent.emission; pe.enabled = false;
                var child = Make(name, childSeed, parent.transform);
                var subs = parent.subEmitters; subs.enabled = true;
                subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual,
                    (ParticleSystemSubEmitterProperties)flags);
                Record(name, 0, child, parent, Parent(ParentSeeds[order]), flags, order);
                for (int i = 0; i < 3; ++i)
                {
                    var supplied = Parent(ParentSeeds[(i + order) % 2]);
                    parent.TriggerSubEmitter(0, ref supplied);
                    Record(name, i + 1, child, parent, supplied, flags, order);
                }
                DestroyImmediate(parent.gameObject);
            }
            string ordinary = "emit_" + childSeed;
            var ps = Make(ordinary, childSeed);
            Record(ordinary, 0, ps, null, default, 0, -1);
            for (int i = 1; i <= 3; ++i)
            {
                ps.Emit(1);
                Record(ordinary, i, ps, null, default, 0, -1);
            }
            DestroyImmediate(ps.gameObject);
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_SEED_AXIS_OUT"), Output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
