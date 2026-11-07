using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records when a burst whose time is at or near the end of the loop fires: its particles' start
/// size (a curve over the loop, so it tells which loop time the burst took), remaining lifetime
/// (its age) and seed, after Simulate calls with fixed and frame steps that cross or land on the
/// loop end, for looping and non-looping systems, bursts at the duration and at 0, repeated
/// cycles whose last one reaches the duration, probability, a single call over several loops,
/// a start delay, and a system played over frames. Each record is one JSON line
/// {"key", "value": [numbers]} in <c>PARTICLE_BURST_LOOP_OUT</c>.
/// </summary>
public class BurstLoopProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[512];
    static string s_Path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_BURST_LOOP_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        new GameObject("BurstLoopProbe").AddComponent<BurstLoopProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Record(string key, IList<float> values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Count; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    /// <summary>A looping 1 s system with the given bursts and no rate. Particles live 4 s and do
    /// not move; their start size runs from 1 at the loop's start to 3 at its end.</summary>
    static ParticleSystem Make(string name, params ParticleSystem.Burst[] bursts)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 4242u;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 1f;
        main.loop = true;
        main.maxParticles = 500;
        main.startLifetime = 4f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f, 2f, 2f), new Keyframe(1f, 3f, 2f, 2f)));
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(bursts);
        go.SetActive(true);
        return ps;
    }

    /// <summary>time, count, then per particle: start size, remaining lifetime and the seed's halves.</summary>
    static void RecordParticles(string key, ParticleSystem ps)
    {
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { ps.time, n, ps.isEmitting ? 1 : 0 };
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            v.Add(p.startSize); v.Add(p.remainingLifetime); v.Add(p.randomSeed & 0xffff); v.Add(p.randomSeed >> 16);
        }
        Record(key, v);
    }

    /// <summary>Runs Simulate with each of <paramref name="calls"/> (seconds) after a restart,
    /// recording after every call.</summary>
    static void Calls(string key, ParticleSystem ps, bool fixedStep, params float[] calls)
    {
        for (int i = 0; i < calls.Length; i++)
        {
            ps.Simulate(calls[i], true, i == 0, fixedStep);
            RecordParticles(key + "/" + i, ps);
        }
        Destroy(ps.gameObject);
    }

    static ParticleSystem.Burst Burst(float time, short count, int cycles = 1, float interval = 0.01f, float probability = 1f)
    {
        var b = new ParticleSystem.Burst(time, count) { cycleCount = cycles, repeatInterval = interval, probability = probability };
        return b;
    }

    static float[] Repeat(float call, int times)
    {
        var calls = new float[times];
        for (int i = 0; i < times; i++) calls[i] = call;
        return calls;
    }

    static void Simulations()
    {
        // At the duration, fixed 0.02 s steps that cross the loop end (0.25 s, then 0.5 s calls,
        // whose 0.01 s remainders carry over), and calls landing on it.
        Calls("end/fixed/cross", Make("a", Burst(1f, 2)), true, 0.25f, 0.5f, 0.5f, 0.5f, 0.5f);
        Calls("end/fixed/land", Make("b", Burst(1f, 2)), true, 0.5f, 0.5f, 0.1f, 0.9f, 0.1f);
        // Frame steps: crossing, landing exactly, and steps of 0.3 s over four loops.
        Calls("end/frame/cross", Make("c", Burst(1f, 2)), false, 0.9f, 0.2f, 0.9f, 0.2f);
        Calls("end/frame/land", Make("d", Burst(1f, 2)), false, 0.5f, 0.5f, 0.1f, 0.4f, 0.5f, 0.1f);
        Calls("end/frame/steps", Make("e", Burst(1f, 2)), false, Repeat(0.3f, 14));
        Calls("end/frame/fine", Make("e2", Burst(1f, 2)), false, Repeat(0.07f, 32));
        // One call over several loops.
        Calls("end/frame/long", Make("f", Burst(1f, 2)), false, 2.5f, 1.2f);
        Calls("end/fixed/long", Make("g", Burst(1f, 2)), true, 2.5f, 1.2f);
        // At the start and at the end together.
        Calls("both/frame", Make("h", Burst(0f, 1), Burst(1f, 2)), false, Repeat(0.3f, 8));
        Calls("both/fixed", Make("i", Burst(0f, 1), Burst(1f, 2)), true, Repeat(0.3f, 8));
        // Just inside and just past the end.
        Calls("inside/frame", Make("j", Burst(0.999f, 2)), false, Repeat(0.3f, 8));
        Calls("past/frame", Make("k", Burst(1.05f, 2)), false, Repeat(0.3f, 8));
        // Cycles whose last one reaches or passes the end.
        Calls("cycles/three", Make("l", Burst(0.5f, 1, 3, 0.25f)), false, Repeat(0.3f, 8));
        Calls("cycles/three/fixed", Make("l2", Burst(0.5f, 1, 3, 0.25f)), true, Repeat(0.3f, 8));
        Calls("cycles/four", Make("m", Burst(0.5f, 1, 4, 0.25f)), false, Repeat(0.3f, 8));
        Calls("cycles/forever", Make("n", Burst(0f, 1, 0, 0.5f)), false, Repeat(0.3f, 8));
        Calls("cycles/forever/fixed", Make("n2", Burst(0f, 1, 0, 0.5f)), true, Repeat(0.3f, 8));
        // One step of 0.02 s whose loop end falls at a chosen point (0.00001 to 0.019 s in),
        // with bursts at the start, just before the end and at the end.
        foreach (float before in new[] { 0.00001f, 0.0001f, 0.001f, 0.005f, 0.01f, 0.015f, 0.019f })
            Calls("edge/" + before.ToString("R", CultureInfo.InvariantCulture), Make("edge", Burst(0f, 1), Burst(0.9995f, 3), Burst(1f, 2)),
                false, 1f - before, 0.02f, 0.1f);
        // Two bursts in one step: inside the loop, in a step crossing the end, and with the end.
        Calls("pair/mid", Make("pm", Burst(0.295f, 1), Burst(0.305f, 2)), false, 0.29f, 0.02f, 0.1f);
        Calls("pair/cross", Make("pc", Burst(0.987f, 1), Burst(0.995f, 2)), false, 0.985f, 0.02f, 0.1f);
        Calls("pair/crossend", Make("pe", Burst(0.987f, 1), Burst(1f, 2)), false, 0.985f, 0.02f, 0.1f);
        Calls("pair/crosszero", Make("pz", Burst(0.987f, 1), Burst(0f, 2)), false, 0.985f, 0.02f, 0.1f);
        Calls("single/cross", Make("ps", Burst(0.987f, 1)), false, 0.985f, 0.02f, 0.1f);
        Calls("single/crosszero", Make("p0", Burst(0f, 2)), false, 0.985f, 0.02f, 0.1f);
        // Repeating bursts at the loop's start: finite and endless cycles, and an endless repeat
        // that lands on the end.
        Calls("cycles/two0", Make("n3", Burst(0f, 1, 2, 0.5f)), false, Repeat(0.3f, 8));
        Calls("cycles/forever/end", Make("n4", Burst(0.25f, 1, 0, 0.75f)), false, Repeat(0.3f, 8));
        // A probability draw at the end.
        Calls("probability", Make("o", Burst(1f, 1, 1, 0.01f, 0.5f), Burst(0.5f, 1, 1, 0.01f, 0.5f)), false, Repeat(0.3f, 24));
        // A burst count from a curve over the loop (2 at the start, 6 at the end).
        var curved = Make("p");
        var burst = new ParticleSystem.Burst(1f, new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 2f, 1f, 6f)));
        var em = curved.emission;
        em.SetBursts(new[] { burst });
        Calls("countcurve", curved, false, Repeat(0.3f, 8));
        // Not looping: the end of the only loop.
        var once = Make("q", Burst(1f, 2), Burst(0f, 1));
        var main = once.main;
        main.loop = false;
        Calls("once/frame", once, false, 0.9f, 0.2f, 0.2f);
        once = Make("r", Burst(1f, 2));
        main = once.main;
        main.loop = false;
        Calls("once/land", once, false, 0.5f, 0.5f, 0.2f);
        once = Make("s", Burst(1f, 2));
        main = once.main;
        main.loop = false;
        Calls("once/fixed", once, true, 0.25f, 0.5f, 0.5f);
        // A start delay before the loop.
        var delayed = Make("t", Burst(1f, 2));
        main = delayed.main;
        main.startDelay = 0.2f;
        Calls("delay", delayed, false, Repeat(0.3f, 8));
    }

    IEnumerator Played()
    {
        Time.captureDeltaTime = 0.03f;
        var ps = Make("played", Burst(1f, 2), Burst(0f, 1));
        // No camera sees it: keep it simulating.
        var main = ps.main;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        yield return null;
        ps.Play();
        for (int frame = 0; frame < 75; frame++)
        {
            yield return null;
            RecordParticles("played/" + frame, ps);
        }
        Destroy(ps.gameObject);
        Time.captureDeltaTime = 0f;
    }

    void Start() => StartCoroutine(Probe());

    IEnumerator Probe()
    {
        yield return null;
        Simulations();
        yield return Played();
        Record("complete", new List<float> { 1f });
        File.WriteAllText(s_Path, s_Out.ToString());
        Debug.Log("particle burst loop complete: " + s_Path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
