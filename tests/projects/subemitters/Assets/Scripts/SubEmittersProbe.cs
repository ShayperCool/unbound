using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what Death and Manual (script-triggered) sub-emitters emit: which of the child's bursts
/// fire, counts and cycles, burst and emit probabilities, timing within a step, positions and
/// inherited properties. Parents get their particles from Emit with explicit values; children use
/// world simulation space, no shape and a constant start speed along +Z, so their particles are
/// deterministic except where a scenario asks for randomness (keys under <c>random/</c>). Each record
/// is one JSON line {"key", "value": [numbers]} in <c>SUBEMITTERS_OUT</c>.
/// </summary>
public class SubEmittersProbe : MonoBehaviour
{
    const float Dt = 0.05f;
    const int Steps = 14;
    static readonly StringBuilder s_Out = new StringBuilder();
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[256];
    static string s_Path;
    static int s_Errors;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("SUBEMITTERS_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        new GameObject("SubEmittersProbe").AddComponent<SubEmittersProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

    internal static void Record(string key, params float[] values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    const int Fields = 18;

    /// <summary>The count, then every particle (sorted by position, then remaining lifetime, so the
    /// order particles are stored in does not matter): position, velocity, remaining and start
    /// lifetime, start size, start color (0-255), rotation.</summary>
    internal static void RecordParticles(string key, ParticleSystem ps)
    {
        int count = ps.particleCount;
        if (s_Buffer.Length < count) s_Buffer = new ParticleSystem.Particle[count * 2];
        int n = ps.GetParticles(s_Buffer);
        var rows = new List<float[]>(n);
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            Color32 c = p.startColor;
            var s = p.startSize3D;
            var r = p.rotation3D;
            rows.Add(new[]
            {
                p.position.x, p.position.y, p.position.z, p.velocity.x, p.velocity.y, p.velocity.z,
                p.remainingLifetime, p.startLifetime, s.x, s.y, s.z, c.r, c.g, c.b, c.a, r.x, r.y, r.z,
            });
        }
        rows.Sort((a, b) =>
        {
            for (int k = 0; k < 7; k++)
            {
                int d = a[k].CompareTo(b[k]);
                if (d != 0) return d;
            }
            return 0;
        });
        var values = new float[1 + n * Fields];
        values[0] = n;
        for (int i = 0; i < n; i++) Array.Copy(rows[i], 0, values, 1 + i * Fields, Fields);
        Record(key, values);
    }

    /// <summary>A system on an inactive object, stopped and cleared, with a fixed seed, no shape, no
    /// play on awake, no emission of its own; <paramref name="configure"/> sets the rest.</summary>
    internal static ParticleSystem Make(string name, Transform parent, Action<ParticleSystem> configure)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, false);
        else go.transform.position = new Vector3(1f, 2f, 3f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 777;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 5f;
        main.loop = true;
        main.startLifetime = 2.013f;
        main.startSpeed = 1f;
        main.startSize = 0.25f;
        main.maxParticles = 2000;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        configure?.Invoke(ps);
        go.SetActive(true);
        return ps;
    }

    /// <summary>A child in world simulation space under <paramref name="parent"/>, its bursts given.</summary>
    internal static ParticleSystem Child(ParticleSystem parent, params ParticleSystem.Burst[] bursts)
    {
        return Make(parent.name + " child", parent.transform, c =>
        {
            var m = c.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
            c.emission.SetBursts(bursts);
        });
    }

    static readonly Color32[] s_Colors =
    {
        new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 200), new Color32(0, 0, 255, 150), new Color32(255, 255, 0, 100),
    };

    /// <summary>Emits <paramref name="n"/> parent particles at (k/4·…, 0, 0) in the parent's local
    /// space moving +Y at 1 unit/s, with lifetimes <paramref name="life"/>(k) and distinct colors,
    /// sizes and rotations.</summary>
    internal static void EmitParents(ParticleSystem ps, int n, Func<int, float> life)
    {
        for (int k = 0; k < n; k++)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = new Vector3(n > 4 ? k * 0.25f : k, 0f, 0f),
                velocity = new Vector3(0f, 1f, 0f),
                startLifetime = life(k),
                startSize = 0.5f + 0.5f * (k % 4),
                startColor = s_Colors[k % 4],
                rotation3D = new Vector3(0f, 0f, 30f * (k % 4 + 1)),
            };
            ps.Emit(e, 1);
        }
    }

    static float DeathLife(int k) => 0.123f + 0.1f * Mathf.Min(k % 4, 2);

    /// <summary>A parent with one Death sub-emitter whose child has <paramref name="bursts"/>, four
    /// parents dying at 0.123, 0.223 and (two) 0.323 s, stepped and recorded.</summary>
    static void Death(string scenario, ParticleSystem.Burst[] bursts, ParticleSystemSubEmitterProperties props = ParticleSystemSubEmitterProperties.InheritNothing,
        int parents = 4, float probability = 1f, Action<ParticleSystem, ParticleSystem> configure = null, Func<int, float> life = null,
        Action<int> after = null)
    {
        var ps = Make(scenario, null, null);
        var child = Child(ps, bursts);
        var sub = ps.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, props, probability);
        configure?.Invoke(ps, child);
        EmitParents(ps, parents, life ?? DeathLife);
        StepRecord(scenario, ps, child, null, scenario.StartsWith("random/"), after);
    }

    /// <summary>Steps the parent (with its children) <see cref="Steps"/> times, recording the parent
    /// and child after each step; <paramref name="before"/> runs before a step and may record.</summary>
    static void StepRecord(string scenario, ParticleSystem ps, ParticleSystem child, Action<int> before, bool countsOnly, Action<int> after = null)
    {
        for (int i = 0; i < Steps; i++)
        {
            before?.Invoke(i);
            ps.Simulate(Dt, true, false, false);
            string step = I(i);
            if (countsOnly)
            {
                Record(scenario + "/" + step, ps.particleCount, child.particleCount);
                continue;
            }
            Record(scenario + "/parent/" + step, ps.particleCount, ps.time);
            RecordParticles(scenario + "/child/" + step, child);
            after?.Invoke(i);
        }
        DestroyImmediate(ps.gameObject);
    }

    static ParticleSystem.Burst B(float time, short count) => new ParticleSystem.Burst(time, count);

    static void DeathScenarios()
    {
        Death("death/one", new[] { B(0f, 3) });
        Death("death/two", new[] { B(0f, 3), B(0f, 5) });
        Death("death/late_first", new[] { B(0.3f, 2), B(0f, 4) }, configure: (ps, child) =>
        {
            var em = child.emission;
            Record("death/late_first/burst_times", em.burstCount, em.GetBurst(0).time, em.GetBurst(1).time);
        });
        Death("death/three_times", new[] { B(0.1f, 1), B(0.2f, 2), B(0.4f, 4) });
        Death("death/cycles", new[] { new ParticleSystem.Burst(0f, 2, 3, 0.1f) });
        Death("death/cycles_infinite", new[] { new ParticleSystem.Burst(0f, 2, 0, 0.1f) });
        Death("death/cycles_second", new[] { B(0f, 1), new ParticleSystem.Burst(0f, 2, 3, 0.1f) });
        Death("death/no_bursts", new ParticleSystem.Burst[0], configure: (ps, child) =>
        {
            var em = child.emission; em.rateOverTime = 20f;
        });
        Death("death/emission_disabled", new[] { B(0f, 3) }, configure: (ps, child) =>
        {
            var em = child.emission; em.enabled = false;
        });
        Death("death/two_slots", new[] { B(0f, 3) }, configure: (ps, child) =>
        {
            var sub = ps.subEmitters;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritColor);
            Record("death/two_slots/slots", sub.subEmittersCount);
        });
        ParticleSystem second = null;
        Death("death/two_children", new[] { B(0f, 3) }, configure: (ps, child) =>
        {
            second = Child(ps, B(0f, 5));
            var sub = ps.subEmitters;
            sub.AddSubEmitter(second, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        }, after: i => RecordParticles("death/two_children/second/" + I(i), second));
        Death("death/many", new[] { B(0f, 3) }, parents: 40, life: k => 0.123f, configure: (ps, child) =>
        {
            var m = child.main; m.maxParticles = 100;
        });
        Death("death/inherit_all", new[] { B(0f, 2) }, ParticleSystemSubEmitterProperties.InheritEverything);
        Death("death/inherit_color_size_rotation", new[] { B(0f, 2) },
            ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize | ParticleSystemSubEmitterProperties.InheritRotation);
        Death("death/inherit_lifetime", new[] { B(0f, 2) }, ParticleSystemSubEmitterProperties.InheritLifetime);
        Death("death/inherit_modules", new[] { B(0f, 2) },
            ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize, configure: (ps, child) =>
            {
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.2f, 0.4f, 0.6f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
                col.color = g;
                var size = ps.sizeOverLifetime; size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 3f)));
            });
        Death("death/child_rotated", new[] { B(0f, 2) }, configure: (ps, child) =>
        {
            child.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        });

        // Seeded probability and count-range cases compare parent and child counts exactly.
        Func<int, float> spread = k => 0.123f + 0.1f * (k % 4);
        var burstProbability = B(0f, 3); burstProbability.probability = 0.5f;
        Death("random/death_burst_probability", new[] { burstProbability }, parents: 120, life: spread);
        Death("random/death_count_range", new[] { new ParticleSystem.Burst(0f, 2, 6) }, parents: 120, life: spread);
        Death("random/death_emit_probability", new[] { B(0f, 3) }, parents: 120, probability: 0.5f, life: spread);
    }

    /// <summary>A parent with sub-emitter slots of <paramref name="type"/> (the child has
    /// <paramref name="bursts"/>), four long-lived parents; <paramref name="trigger"/> runs before
    /// steps 3 and 8 and the child is recorded right after it (<c>call/</c>).</summary>
    static void Manual(string scenario, ParticleSystem.Burst[] bursts, Action<ParticleSystem> trigger,
        ParticleSystemSubEmitterType type = ParticleSystemSubEmitterType.Manual,
        ParticleSystemSubEmitterProperties props = ParticleSystemSubEmitterProperties.InheritNothing,
        int parents = 4, float probability = 1f, Action<ParticleSystem, ParticleSystem> configure = null, float life = 1.013f)
    {
        var ps = Make(scenario, null, null);
        var child = Child(ps, bursts);
        var sub = ps.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, type, props, probability);
        configure?.Invoke(ps, child);
        EmitParents(ps, parents, k => life + 0.25f * (k % 4));
        bool countsOnly = scenario.StartsWith("random/");
        StepRecord(scenario, ps, child, i =>
        {
            if (i != 3 && i != 8) return;
            trigger(ps);
            if (countsOnly) Record(scenario + "/call/" + I(i), ps.particleCount, child.particleCount);
            else RecordParticles(scenario + "/call/" + I(i), child);
        }, countsOnly);
    }

    static readonly List<ParticleSystem.Particle> s_List = new List<ParticleSystem.Particle>();

    static void ManualScenarios()
    {
        Action<ParticleSystem> all = ps => ps.TriggerSubEmitter(0);
        Manual("manual/one", new[] { B(0f, 3) }, all);
        Manual("manual/two", new[] { B(0f, 3), B(0.2f, 5) }, all);
        Manual("manual/late_first", new[] { B(0.3f, 2) }, all);
        Manual("manual/cycles", new[] { new ParticleSystem.Burst(0f, 2, 3, 0.1f) }, all);
        Manual("manual/no_bursts", new ParticleSystem.Burst[0], all, configure: (ps, child) =>
        {
            var em = child.emission; em.rateOverTime = 20f;
        });
        Manual("manual/module_disabled", new[] { B(0f, 3) }, all, configure: (ps, child) =>
        {
            var sub = ps.subEmitters; sub.enabled = false;
        });
        Manual("manual/death_slot", new[] { B(0f, 3) }, all, ParticleSystemSubEmitterType.Death);
        Manual("manual/inherit_all", new[] { B(0f, 2) }, all, props: ParticleSystemSubEmitterProperties.InheritEverything);
        Manual("manual/inherit_lifetime", new[] { B(0f, 2) }, all, props: ParticleSystemSubEmitterProperties.InheritLifetime);
        Manual("manual/inherit_lifetime_long", new[] { B(0f, 2) }, all, props: ParticleSystemSubEmitterProperties.InheritLifetime, life: 4.013f);
        Manual("manual/ref", new[] { B(0f, 2) }, ps =>
        {
            int n = ps.GetParticles(s_Buffer);
            var p = s_Buffer[1 % n];
            p.position = new Vector3(5f, 5f, 5f);
            p.startColor = new Color32(10, 20, 30, 40);
            p.startSize = 2f;
            ps.TriggerSubEmitter(0, ref p);
        }, props: ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize);
        Manual("manual/list", new[] { B(0f, 2) }, ps =>
        {
            int n = ps.GetParticles(s_Buffer);
            s_List.Clear();
            s_List.Add(s_Buffer[0]);
            s_List.Add(s_Buffer[2 % n]);
            ps.TriggerSubEmitter(0, s_List);
        });
        int outOfRange = 0;
        Manual("manual/out_of_range", new[] { B(0f, 2) }, ps =>
        {
            int errors = s_Errors, thrown = 0;
            try { ps.TriggerSubEmitter(3); }
            catch (Exception) { thrown = 1; }
            Record("manual/out_of_range/result/" + I(outOfRange++), thrown, s_Errors - errors);
        });
        Manual("random/manual_emit_probability", new[] { B(0f, 3) }, all, parents: 120, probability: 0.5f);
    }

    /// <summary>Where in a step a Death sub-emitter's particles start: ten parents dying at
    /// (k + 0.3) / 10 of step 2 for steps of <paramref name="dt"/> (one internal step at 0.02, two
    /// at 0.05, four at 0.1 with the default maximum particle timestep 0.03).</summary>
    static void Timing(float dt, string suffix = "")
    {
        string scenario = "timing/dt" + F(dt) + suffix;
        var ps = Make(scenario, null, null);
        var child = Child(ps, B(0f, 1));
        var sub = ps.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        EmitParents(ps, 10, k => dt * (2f + (k + 0.3f) / 10f));
        for (int i = 0; i < 4; i++)
        {
            ps.Simulate(dt, true, false, false);
            Record(scenario + "/parent/" + I(i), ps.particleCount);
            RecordParticles(scenario + "/child/" + I(i), child);
        }
        DestroyImmediate(ps.gameObject);
    }

    static readonly Vector3[] s_Directions =
    {
        new Vector3(0f, 1f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
        new Vector3(0f, -1f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0.6f, 0.8f, 0f), new Vector3(0.3f, -0.5f, 0.8124038f),
        new Vector3(0f, 0f, 0f),
    };

    static readonly Vector3[] s_ChildRotations = { Vector3.zero, new Vector3(0f, 90f, 0f), new Vector3(-90f, 0f, 0f) };

    /// <summary>The direction a Manual sub-emitter's child emits in: one parent moving along each of
    /// <see cref="s_Directions"/> (2 units/s), the child's transform rotated so its +Z is local +Z,
    /// +X and +Y; recorded right after TriggerSubEmitter. <c>direction/rotated_parent</c>: the parent
    /// (local simulation) rotated; <c>direction/offset</c>: the child's zero-size box shape moved off
    /// its centre.</summary>
    static void Directions()
    {
        for (int d = 0; d < s_Directions.Length; d++)
            for (int r = 0; r < s_ChildRotations.Length; r++)
                Direction("direction/" + I(d) + "/" + I(r), s_Directions[d], s_ChildRotations[r], Vector3.zero, null);
        Direction("direction/rotated_parent/0", new Vector3(0f, 1f, 0f), Vector3.zero, new Vector3(30f, 0f, 45f), null);
        Direction("direction/rotated_parent/1", new Vector3(0f, 0f, 1f), Vector3.zero, new Vector3(0f, 60f, 0f), null);
        Action<ParticleSystem> offset = c =>
        {
            var shape = c.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = Vector3.zero;
            shape.position = new Vector3(0.5f, 0.25f, 0.125f);
        };
        Direction("direction/offset/0", new Vector3(1f, 0f, 0f), Vector3.zero, Vector3.zero, offset);
        Direction("direction/offset/1", new Vector3(0f, 1f, 0f), Vector3.zero, Vector3.zero, offset);
        Direction("direction/offset/2", new Vector3(0f, 0f, 1f), Vector3.zero, Vector3.zero, offset);
        // A Death sub-emitter: the direction where the parent dies.
        var ps = Make("direction/death", null, null);
        var child = Child(ps, B(0f, 1));
        var sub = ps.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        int[] dying = { 1, 4, 6, 7 };
        for (int d = 0; d < dying.Length; d++)
            ps.Emit(new ParticleSystem.EmitParams { position = new Vector3(3f * d, 0f, 0f), velocity = 2f * s_Directions[dying[d]], startLifetime = 0.0613f }, 1);
        ps.Simulate(Dt, true, false, false);
        RecordParticles("direction/death/child", child);
        DestroyImmediate(ps.gameObject);
    }

    static void Direction(string scenario, Vector3 direction, Vector3 childRotation, Vector3 parentRotation, Action<ParticleSystem> configure)
    {
        var ps = Make(scenario, null, s => s.transform.rotation = Quaternion.Euler(parentRotation));
        var child = Child(ps, B(0f, 1));
        child.transform.localRotation = Quaternion.Euler(childRotation);
        configure?.Invoke(child);
        var sub = ps.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);
        ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = 2f * direction, startLifetime = 5f }, 1);
        ps.Simulate(Dt, true, false, false);
        ps.TriggerSubEmitter(0);
        RecordParticles(scenario, child);
        DestroyImmediate(ps.gameObject);
    }

    void Start() => StartCoroutine(Probe());

    static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) s_Errors++;
    }

    IEnumerator Probe()
    {
        Application.logMessageReceived += OnLog;
        DeathScenarios();
        ManualScenarios();
        Timing(0.02f);
        Timing(0.05f);
        Timing(0.1f);
        Directions();

        // Played over frames at a fixed delta (each frame's state recorded in the next Update).
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var update = Make("played/update", null, s =>
        {
            s.emission.SetBursts(new[] { B(0f, 3) });
            var m = s.main; m.startLifetime = 5.013f; m.startSpeed = 1f;
        });
        var updateChild = Child(update, B(0f, 2), B(0.1f, 4));
        var updateSub = update.subEmitters; updateSub.enabled = true;
        updateSub.AddSubEmitter(updateChild, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);

        var stopped = Make("played/stopped", null, s =>
        {
            s.emission.SetBursts(new[] { B(0f, 3) });
            var m = s.main; m.loop = false; m.duration = 0.1f; m.startLifetime = 0.213f; m.startSpeed = 1f;
            m.stopAction = ParticleSystemStopAction.Callback;
        });
        var stoppedChild = Child(stopped, B(0f, 2));
        var stoppedSub = stopped.subEmitters; stoppedSub.enabled = true;
        stoppedSub.AddSubEmitter(stoppedChild, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);
        stopped.gameObject.AddComponent<SubEmitterStopCallback>();

        var volume = new GameObject("trigger volume").AddComponent<SphereCollider>();
        volume.isTrigger = true;
        volume.radius = 0.25f;
        volume.transform.position = new Vector3(-3f, 2f, 0.6f);
        var trigger = Make("played/trigger", null, s =>
        {
            s.transform.position = new Vector3(-3f, 2f, 0f);
            s.emission.SetBursts(new[] { B(0f, 1) });
            var m = s.main; m.startLifetime = 1.013f; m.startSpeed = 2f;
            var t = s.trigger; t.enabled = true; t.AddCollider(volume);
            t.enter = ParticleSystemOverlapAction.Callback;
            t.inside = ParticleSystemOverlapAction.Ignore;
            t.exit = ParticleSystemOverlapAction.Ignore;
            t.outside = ParticleSystemOverlapAction.Ignore;
        });
        var triggerChild = Child(trigger, B(0f, 2), B(0f, 3));
        var triggerSub = trigger.subEmitters; triggerSub.enabled = true;
        triggerSub.AddSubEmitter(triggerChild, ParticleSystemSubEmitterType.Manual, ParticleSystemSubEmitterProperties.InheritNothing);
        trigger.gameObject.AddComponent<SubEmitterTriggerCallback>();

        // A one-shot parent whose Death children outlive it: does it stay playing, and do they move?
        var finish = Make("played/finish", null, s =>
        {
            s.emission.SetBursts(new[] { B(0f, 2) });
            var m = s.main; m.loop = false; m.duration = 0.05f; m.startLifetime = 0.113f; m.startSpeed = 1f;
            m.stopAction = ParticleSystemStopAction.Callback;
        });
        var finishChild = Child(finish, B(0f, 2));
        var finishChildMain = finishChild.main; finishChildMain.startLifetime = 0.213f;
        // Does the child that started playing with its Death particles run its own stop action?
        finishChildMain.stopAction = ParticleSystemStopAction.Callback;
        finishChild.gameObject.AddComponent<SubEmitterChildStopCallback>();
        var finishSub = finish.subEmitters; finishSub.enabled = true;
        finishSub.AddSubEmitter(finishChild, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        finish.gameObject.AddComponent<SubEmitterFinishCallback>();

        update.Play();
        stopped.Play();
        trigger.Play();
        finish.Play();
        for (int frame = 0; frame < 30; frame++)
        {
            SubEmitterStopCallback.Frame = SubEmitterTriggerCallback.Frame = SubEmitterFinishCallback.Frame = frame;
            yield return null;
            string f = I(frame);
            Record("played/delta/" + f, Time.deltaTime);
            Record("played/update/parent/" + f, update.particleCount, update.time, updateChild.isPlaying ? 1 : 0);
            RecordParticles("played/update/child/" + f, updateChild);
            // The stopped parent played again: do the particles its stop callback emitted move again?
            if (frame == 20) stopped.Play();
            if (frame == 4 || frame == 12)
            {
                update.TriggerSubEmitter(0);
                RecordParticles("played/update/call/" + f, updateChild);
            }
            Record("played/stopped/parent/" + f, stopped.particleCount, stopped.isPlaying ? 1 : 0, stoppedChild.isPlaying ? 1 : 0);
            SubEmitterStopCallback.Save(stopped);
            RecordParticles("played/stopped/child/" + f, stoppedChild);
            Record("played/finish/parent/" + f, finish.particleCount, finish.isPlaying ? 1 : 0, finish.IsAlive(true) ? 1 : 0, finishChild.isPlaying ? 1 : 0);
            RecordParticles("played/finish/child/" + f, finishChild);
            Record("played/trigger/parent/" + f, trigger.particleCount);
            RecordParticles("played/trigger/child/" + f, triggerChild);
        }
        // The same Simulate sweeps while the frame's delta is 0.01.
        Time.captureDeltaTime = 0.01f;
        yield return null;
        Record("timing/delta0.01", Time.deltaTime);
        Timing(0.05f, "_delta0.01");
        Timing(0.1f, "_delta0.01");
        // ... and while the fixed timestep is 0.01.
        float fixedDelta = Time.fixedDeltaTime;
        Time.fixedDeltaTime = 0.01f;
        Timing(0.05f, "_fixed0.01");
        Timing(0.1f, "_fixed0.01");
        Time.fixedDeltaTime = fixedDelta;

        // Death children in played frames of 0.05 s (two internal steps each): parents emitted in
        // this Update die at (k + 0.3) / 10 of the third frame's update.
        Time.captureDeltaTime = 0.05f;
        yield return null;
        var frames = Make("played/death", null, s => { var m = s.main; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; });
        var framesChild = Child(frames, B(0f, 1));
        var framesSub = frames.subEmitters; framesSub.enabled = true;
        framesSub.AddSubEmitter(framesChild, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        frames.Play();
        EmitParents(frames, 10, k => 0.05f * (2f + (k + 0.3f) / 10f));
        for (int frame = 0; frame < 5; frame++)
        {
            yield return null;
            Record("played/death/parent/" + I(frame), frames.particleCount, Time.deltaTime);
            RecordParticles("played/death/child/" + I(frame), framesChild);
        }
        DestroyImmediate(frames.gameObject);

        Time.captureDeltaTime = 0f;
        Application.logMessageReceived -= OnLog;
        Record("complete", 1);
        File.WriteAllText(s_Path, s_Out.ToString());
        Debug.Log("sub-emitters complete: " + s_Path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
