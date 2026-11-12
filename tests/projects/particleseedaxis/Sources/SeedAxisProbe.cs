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
        main.startColor = (Color)new Color32(192, 128, 224, 200); main.gravityModifier = 0f;
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

    static void InitialRecord(string name, int step, ParticleSystem ps) =>
        Record(name, step, ps, null, default, 0, -1);

    static void InitializationControls()
    {
        foreach (int count in new[] { 2, 5 })
        {
            string name = "bulk" + count;
            var ps = Make(name, 12345u);
            InitialRecord(name, 0, ps);
            ps.Emit(count); InitialRecord(name, 1, ps);
            ps.Emit(1); InitialRecord(name, 2, ps);
            DestroyImmediate(ps.gameObject);
        }
        {
            const string name = "deferred_read";
            var ps = Make(name, 12345u);
            InitialRecord(name, 0, ps);
            ps.Emit(1); ps.Emit(1); ps.Emit(1);
            InitialRecord(name, 1, ps);
            ps.Emit(1); InitialRecord(name, 2, ps);
            DestroyImmediate(ps.gameObject);
        }
        {
            const string name = "seed_reset";
            var ps = Make(name, 12345u);
            ps.Emit(2); InitialRecord(name, 1, ps);
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.randomSeed = 305419896u;
            ps.Emit(2); InitialRecord(name, 2, ps);
            DestroyImmediate(ps.gameObject);
        }
        foreach (string name in new[] { "size3d", "rotation3d", "both3d", "random_scalar", "random3d" })
        {
            var ps = Make(name, 12345u);
            var m = ps.main;
            m.startSize3D = name == "size3d" || name == "both3d" || name == "random3d";
            m.startRotation3D = name == "rotation3d" || name == "both3d" || name == "random3d";
            if (m.startSize3D) { m.startSizeX = .5f; m.startSizeY = .75f; m.startSizeZ = 1.25f; }
            if (m.startRotation3D) { m.startRotationX = .1f; m.startRotationY = .2f; m.startRotationZ = .3f; }
            if (name == "random_scalar" || name == "random3d")
            {
                m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                m.startSizeX = new ParticleSystem.MinMaxCurve(.5f, 1.5f);
                m.startRotationZ = new ParticleSystem.MinMaxCurve(.1f, .9f);
                m.startColor = new ParticleSystem.MinMaxGradient(Color.black, Color.white);
                m.flipRotation = .5f;
                if (m.startSize3D)
                {
                    m.startSizeY = new ParticleSystem.MinMaxCurve(1f, 2f);
                    m.startSizeZ = new ParticleSystem.MinMaxCurve(2f, 3f);
                    m.startRotationX = new ParticleSystem.MinMaxCurve(.2f, .8f);
                    m.startRotationY = new ParticleSystem.MinMaxCurve(.3f, .7f);
                }
            }
            ps.Emit(2); InitialRecord(name, 1, ps);
            ps.Emit(1); InitialRecord(name, 2, ps);
            DestroyImmediate(ps.gameObject);
        }
        {
            const string name = "explicit_seed";
            var ps = Make(name, 12345u);
            ps.Emit(new ParticleSystem.EmitParams { randomSeed = 77u }, 2);
            InitialRecord(name, 1, ps);
            ps.Emit(1); InitialRecord(name, 2, ps);
            DestroyImmediate(ps.gameObject);
        }
        {
            const string name = "import_axis";
            var ps = Make(name, 12345u);
            ps.SetParticles(new[] { Parent(77u) }, 1);
            InitialRecord(name, 1, ps);
            ps.Emit(1); InitialRecord(name, 2, ps);
            DestroyImmediate(ps.gameObject);
        }
    }

    static void Storage(string name, ParticleSystem ps)
    {
        Output.Append("{\"scenario\":\"storage_").Append(name).Append("\",\"step\":0,\"count\":0,\"readCount\":0,\"time\":0,\"parentCount\":0");
        Output.Append(",\"rotation3D\":").Append(ps.has3DParticleRotations ? "true" : "false");
        Output.Append(",\"size3D\":").Append(ps.hasNonUniformParticleSizes ? "true" : "false").Append("}\n");
    }

    // When a system's particles keep 3D rotations and sizes, observed through
    // has3DParticleRotations and hasNonUniformParticleSizes.
    static void StorageControls()
    {
        var ps = Make("storage", 12345u);
        var m = ps.main;
        Storage("fresh", ps);
        m.startRotation3D = true; m.startSize3D = true;
        Storage("set3d", ps);
        ps.Emit(1); ps.Clear(false);
        Storage("emitted", ps);
        m.startRotation3D = false; m.startSize3D = false;
        ps.Emit(1); ps.Clear(false);
        Storage("emittedAfterOff", ps);
        DestroyImmediate(ps.gameObject);
        foreach (string mode in new[] { "start3d", "rotationSeparate", "rotationBySpeedSeparate", "sizeSeparate", "sizeBySpeedSeparate", "alignToDirection", "rotationModule" })
        {
            ps = Make("storage_" + mode, 12345u);
            m = ps.main;
            if (mode == "start3d") { m.startRotation3D = true; m.startSize3D = true; }
            if (mode == "rotationSeparate") { var r = ps.rotationOverLifetime; r.enabled = true; r.separateAxes = true; }
            if (mode == "rotationModule") { var r = ps.rotationOverLifetime; r.enabled = true; }
            if (mode == "rotationBySpeedSeparate") { var r = ps.rotationBySpeed; r.enabled = true; r.separateAxes = true; }
            if (mode == "sizeSeparate") { var r = ps.sizeOverLifetime; r.enabled = true; r.separateAxes = true; }
            if (mode == "sizeBySpeedSeparate") { var r = ps.sizeBySpeed; r.enabled = true; r.separateAxes = true; }
            if (mode == "alignToDirection") { var sh = ps.shape; sh.enabled = true; sh.alignToDirection = true; }
            ps.Simulate(.01f, false, true);
            Storage("simulated_" + mode, ps);
            DestroyImmediate(ps.gameObject);
        }
        {
            // A rotation module's angular velocity as read right after Emit and after a step.
            ps = Make("rotation_emit", 12345u);
            var r = ps.rotationOverLifetime; r.enabled = true; r.separateAxes = true; r.x = .5f; r.y = .6f; r.z = .7f;
            ps.Emit(1); InitialRecord("rotation_emit", 1, ps);
            DestroyImmediate(ps.gameObject);
        }
        foreach (string mode in new[] { "plain", "rotation3D", "angularVelocity3D", "startSize3D", "scalarSetters" })
        {
            ps = Make("storage_set_" + mode, 12345u);
            var particle = new ParticleSystem.Particle { startLifetime = 2, remainingLifetime = 2, randomSeed = 7 };
            if (mode == "rotation3D") particle.rotation3D = Vector3.zero;
            if (mode == "angularVelocity3D") particle.angularVelocity3D = Vector3.zero;
            if (mode == "startSize3D") particle.startSize3D = Vector3.one;
            if (mode == "scalarSetters") { particle.rotation = 10; particle.angularVelocity = 5; particle.startSize = 2; }
            ps.SetParticles(new[] { particle }, 1);
            Storage("setParticles_" + mode, ps);
            if (mode == "rotation3D" || mode == "startSize3D")
            {
                // A script's 3D particles keep the storage through an update, until a playback
                // restarts without particles.
                ps.Simulate(.01f, false, false, false);
                Storage("setParticles_" + mode + "_updated", ps);
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Play(false); ps.Simulate(.01f, false, false, false);
                Storage("setParticles_" + mode + "_replayed", ps);
            }
            DestroyImmediate(ps.gameObject);
        }
    }

    // Which inherited values a Manual child takes, by flag, by the child's rotation mode (2D, 3D
    // start rotation, 3D only through a separate-axes rotation module) and by 3D size on either side.
    static void InheritanceControls()
    {
        foreach (string mode in new[] { "2d", "start3d", "overLifetime3d", "bySpeed3d",
                     "start3dReactivated", "start3dPlayed", "start3dSimulated", "start3dEmitted",
                     "overLifetime3dEmitted", "overLifetimeEmitted" })
        foreach (int flags in new[] { 4, 15 })
        {
            string name = "inherit_rot_" + mode + "_" + flags;
            var parent = Make(name + "_parent", 54321u);
            var pe = parent.emission; pe.enabled = false;
            var child = Make(name, 12345u, parent.transform);
            var m = child.main;
            if (mode.StartsWith("start3d")) { m.startRotation3D = true; m.startRotationX = .1f; m.startRotationY = .2f; m.startRotationZ = .3f; }
            // What the child did since its rotation became 3D, before the parent triggers it.
            if (mode == "start3dReactivated") { child.gameObject.SetActive(false); child.gameObject.SetActive(true); }
            if (mode == "start3dPlayed") { child.Play(false); child.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }
            if (mode == "start3dSimulated") { child.Simulate(.01f, false, true); child.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }
            if (mode == "start3dEmitted") { child.Emit(1); child.Clear(false); }
            if (mode == "overLifetime3d") { var r = child.rotationOverLifetime; r.enabled = true; r.separateAxes = true; r.x = .5f; r.y = .6f; r.z = .7f; }
            if (mode == "overLifetime3dEmitted") { var r = child.rotationOverLifetime; r.enabled = true; r.separateAxes = true; r.x = .5f; r.y = .6f; r.z = .7f; child.Emit(1); child.Clear(false); }
            if (mode == "overLifetimeEmitted") { var r = child.rotationOverLifetime; r.enabled = true; r.z = .7f; child.Emit(1); child.Clear(false); }
            if (mode == "bySpeed3d") { var r = child.rotationBySpeed; r.enabled = true; r.separateAxes = true; r.x = .5f; r.y = .6f; r.z = .7f; }
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, (ParticleSystemSubEmitterProperties)flags);
            var supplied = Parent(ParentSeeds[0]);
            parent.TriggerSubEmitter(0, ref supplied);
            Record(name, 1, child, parent, supplied, flags, 0);
            DestroyImmediate(parent.gameObject);
        }
        foreach (bool parent3d in new[] { false, true })
        foreach (bool child3d in new[] { false, true })
        {
            string name = "inherit_size_" + (parent3d ? "3d" : "1d") + "_" + (child3d ? "3d" : "1d");
            var parent = Make(name + "_parent", 54321u);
            var pm = parent.main; pm.startSize3D = parent3d;
            var pe = parent.emission; pe.enabled = false;
            var child = Make(name, 12345u, parent.transform);
            var cm = child.main;
            if (child3d) { cm.startSize3D = true; cm.startSizeX = .5f; cm.startSizeY = .75f; cm.startSizeZ = 1f; }
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritSize);
            var supplied = Parent(ParentSeeds[0]);
            supplied.startSize3D = new Vector3(2f, 3f, 4f);
            parent.TriggerSubEmitter(0, ref supplied);
            Record(name, 1, child, parent, supplied, 2, 0);
            DestroyImmediate(parent.gameObject);
        }
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
        InitializationControls();
        InheritanceControls();
        StorageControls();
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_SEED_AXIS_OUT"), Output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
