using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// The first age of a sub-emitter's particle: Death sub-emitters of a stationary parent whose
/// one particle dies naturally inside a <see cref="ParticleSystem.Simulate(float, bool, bool, bool)"/>
/// call, at several call deltas, internal steps and clock contexts, and one Birth sub-emitter
/// history copied from the broad particles probe.
///
/// Phases (relative frames are fixed; nothing waits on real time):
/// <list type="bullet">
/// <item>Frame 0 (this component's Start): context, the startup clocks, then case.baseline
/// (.025 x2, parent life .013), case.d1 (.035 x2, life .013: death in the first of two internal
/// steps), case.d2 (.05 x2, life .038: death in the second of two .025 steps) and case.birth (the
/// broad sub_emitters_birth history). Then Time.captureDeltaTime = 1/30 and Time.timeScale = .5.</item>
/// <item>Frame 2: case.d3 (.025 x2, life .013) under those clocks. Then Time.fixedDeltaTime = .0075.</item>
/// <item>Frame 4: case.fixed (.025 x2, life .013) with that current fixed delta.</item>
/// <item>The startup fixedDeltaTime, timeScale and captureDeltaTime are restored (also after a fault).</item>
/// </list>
/// Each case records its authored input, the actual clocks before and after, and the parent, the
/// Death (or Birth) child and an ordinary transform child after every call; every system of a
/// case is destroyed before the probe yields, so no ordinary update ages it.
///
/// Writes <c>UNBOUND_PROBE_PARTICLESUBEMITTERTIMING_OUT</c>/values.jsonl, one
/// <c>{"k":key,"v":value}</c> per line in a fixed order, through a temporary name. A block ends
/// with <c>key.log</c> (the log messages it caused); <c>key.exception</c> is a probe fault. The last
/// lines are <c>capture.unscopedLogs</c> and the single marker <c>{"k":"complete","v":true}</c>.
/// Absolute frame numbers and real times go to diagnostics.jsonl, outside the compared record.
/// Then <see cref="finishedCode"/> is set; the Editor runner polls it. This script never ends Play
/// mode or quits. No rendering, image, bake or mesh-index read.
/// </summary>
public class SubEmitterTimingProbe : MonoBehaviour
{
    public const string OutputVariable = "UNBOUND_PROBE_PARTICLESUBEMITTERTIMING_OUT";

    /// <summary>Null until the record is written; then 0 (complete) or 3 (fatal).</summary>
    public static int? finishedCode;

    // Predeclared inputs. The analyzer reads them back from the record (case.*.input, phase.*).
    const float CaptureDelta = 1f / 30f;
    const float SlowTimeScale = .5f;
    const float FixedControl = .0075f;
    const int BoundaryFrames = 2;
    const int SettleFrames = 5;

    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[64];

    readonly StringBuilder records = new StringBuilder();
    readonly StringBuilder diagnostics = new StringBuilder();
    readonly List<string> logs = new List<string>();
    readonly List<string> unscoped = new List<string>();
    bool capturing;
    int startFrame;
    float startupFixed, startupTimeScale, startupCapture;
    bool startupRead;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable(OutputVariable) == null) return;
        finishedCode = null;
        new GameObject("SubEmitterTimingProbe").AddComponent<SubEmitterTimingProbe>();
    }

    // ------------------------------------------------------------------ record

    void Value(string k, string json) { records.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(json).Append("}\n"); }

    static string Q(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string B(bool b) => b ? "true" : "false";
    static string N(long n) => n.ToString(CultureInfo.InvariantCulture);
    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? Q(f.ToString("R", CultureInfo.InvariantCulture)) : f.ToString("R", CultureInfo.InvariantCulture);
    static string D(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string C(Color32 c) => "[" + c.r + "," + c.g + "," + c.b + "," + c.a + "]";

    static string List(List<string> items)
    {
        var b = new StringBuilder("[");
        for (int i = 0; i < items.Count; i++) b.Append(i > 0 ? "," : "").Append(Q(items[i]));
        return b.Append(']').ToString();
    }

    void OnLog(string condition, string stack, LogType type) => (capturing ? logs : unscoped).Add(type + ": " + condition);

    /// <summary>A group of records and the log messages they cause (key.log); key.exception is a
    /// probe fault and makes the capture fail.</summary>
    bool Block(string key, Action body)
    {
        logs.Clear();
        capturing = true;
        bool ok = true;
        try { body(); }
        catch (Exception e) { Value(key + ".exception", Q(e.GetType().Name + ": " + e.Message)); ok = false; }
        capturing = false;
        Value(key + ".log", List(logs));
        logs.Clear();
        return ok;
    }

    /// <summary>The clocks a first-age rule could bind to, read now. frameOffset is relative to
    /// this component's first frame; absolute frames and real times are diagnostics only.</summary>
    string Clocks() =>
        "{\"deltaTime\":" + F(Time.deltaTime)
        + ",\"unscaledDeltaTime\":" + F(Time.unscaledDeltaTime)
        + ",\"fixedDeltaTime\":" + F(Time.fixedDeltaTime)
        + ",\"fixedUnscaledDeltaTime\":" + F(Time.fixedUnscaledDeltaTime)
        + ",\"captureDeltaTime\":" + F(Time.captureDeltaTime)
        + ",\"captureFramerate\":" + N(Time.captureFramerate)
        + ",\"timeScale\":" + F(Time.timeScale)
        + ",\"maximumDeltaTime\":" + F(Time.maximumDeltaTime)
        + ",\"maximumParticleDeltaTime\":" + F(Time.maximumParticleDeltaTime)
        + ",\"inFixedTimeStep\":" + B(Time.inFixedTimeStep)
        + ",\"frameOffset\":" + N(Time.frameCount - startFrame) + "}";

    void Diagnostic(string what)
    {
        diagnostics.Append("{\"what\":").Append(Q(what)).Append(",\"frameCount\":").Append(N(Time.frameCount))
            .Append(",\"time\":").Append(F(Time.time)).Append(",\"unscaledTime\":").Append(F(Time.unscaledTime))
            .Append(",\"timeSinceLevelLoad\":").Append(F(Time.timeSinceLevelLoad))
            .Append(",\"realtimeSinceStartup\":").Append(D(Time.realtimeSinceStartupAsDouble)).Append("}\n");
    }

    /// <summary>The system and every particle GetParticles returns, in its order (none sorted or
    /// filtered). Mesh index, custom data and rendering are not read.</summary>
    static string State(ParticleSystem ps)
    {
        int count = ps.particleCount;
        if (s_Buffer.Length < count) s_Buffer = new ParticleSystem.Particle[count * 2];
        // Unity leaves the fields a system does not use as they were in the buffer.
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        var s = new StringBuilder();
        s.Append("{\"count\":").Append(count);
        s.Append(",\"readCount\":").Append(n);
        s.Append(",\"time\":").Append(F(ps.time));
        s.Append(",\"playing\":").Append(B(ps.isPlaying));
        s.Append(",\"emitting\":").Append(B(ps.isEmitting));
        s.Append(",\"stopped\":").Append(B(ps.isStopped));
        s.Append(",\"paused\":").Append(B(ps.isPaused));
        s.Append(",\"alive\":").Append(B(ps.IsAlive(true)));
        s.Append(",\"particles\":[");
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            if (i > 0) s.Append(',');
            s.Append("{\"pos\":").Append(V(p.position));
            s.Append(",\"vel\":").Append(V(p.velocity));
            s.Append(",\"totalVel\":").Append(V(p.totalVelocity));
            s.Append(",\"animatedVel\":").Append(V(p.animatedVelocity));
            s.Append(",\"axis\":").Append(V(p.axisOfRotation));
            s.Append(",\"life\":").Append(F(p.remainingLifetime));
            s.Append(",\"seed\":").Append(p.randomSeed);
            s.Append(",\"startLife\":").Append(F(p.startLifetime));
            s.Append(",\"startSize\":").Append(V(p.startSize3D));
            s.Append(",\"size\":").Append(V(p.GetCurrentSize3D(ps)));
            s.Append(",\"startColor\":").Append(C(p.startColor));
            s.Append(",\"color\":").Append(C(p.GetCurrentColor(ps)));
            s.Append(",\"rot\":").Append(V(p.rotation3D));
            s.Append(",\"angVel\":").Append(V(p.angularVelocity3D));
            s.Append('}');
        }
        return s.Append("]}").ToString();
    }

    // ------------------------------------------------------------------ run

    IEnumerator Start()
    {
        Application.logMessageReceived += OnLog;
        startFrame = Time.frameCount;
        Diagnostic("start");
        bool ok = Startup();
        for (int i = 0; ok && i < BoundaryFrames; i++) { yield return null; Diagnostic("boundary d3 " + N(i + 1)); }
        ok = ok && Later("case.d3", "phase.fixed", () => Time.fixedDeltaTime = FixedControl);
        for (int i = 0; ok && i < BoundaryFrames; i++) { yield return null; Diagnostic("boundary fixed " + N(i + 1)); }
        ok = ok && Later("case.fixed", null, null);
        ok = Restore() && ok;
        Application.logMessageReceived -= OnLog;
        Value("capture.unscopedLogs", List(unscoped));
        if (ok) Value("complete", "true");
        Diagnostic("written");
        string directory = Environment.GetEnvironmentVariable(OutputVariable);
        File.WriteAllText(Path.Combine(directory, "diagnostics.jsonl"), diagnostics.ToString());
        string temporary = Path.Combine(directory, "values.jsonl.partial");
        File.WriteAllText(temporary, records.ToString());
        File.Move(temporary, Path.Combine(directory, "values.jsonl"));
        // Let Play mode settle a few frames before the runner may leave it.
        for (int i = 0; i < SettleFrames; i++) yield return null;
        finishedCode = ok ? 0 : 3;
    }

    bool Fatal(Exception e)
    {
        Value("fatal", Q(e.ToString()));
        return false;
    }

    bool Startup()
    {
        try
        {
            bool ok = Block("context", () =>
            {
                Value("context.unityVersion", Q(Application.unityVersion));
                Value("context.isBatchMode", B(Application.isBatchMode));
                Value("context.platform", Q(Application.platform.ToString()));
                Value("context.graphicsDeviceType", Q(SystemInfo.graphicsDeviceType.ToString()));
            });
            ok &= Block("clock.startup", () =>
            {
                startupFixed = Time.fixedDeltaTime;
                startupTimeScale = Time.timeScale;
                startupCapture = Time.captureDeltaTime;
                startupRead = true;
                Value("clock.startup.clocks", Clocks());
            });
            ok = ok && DeathCase("case.baseline", .025f, .013f);
            ok = ok && DeathCase("case.d1", .035f, .013f);
            ok = ok && DeathCase("case.d2", .05f, .038f);
            ok = ok && BirthControl("case.birth");
            ok = ok && Block("phase.d3", () =>
            {
                Value("phase.d3.set", "{\"captureDeltaTime\":" + F(CaptureDelta) + ",\"timeScale\":" + F(SlowTimeScale)
                      + ",\"boundaryFrames\":" + N(BoundaryFrames) + "}");
                Time.captureDeltaTime = CaptureDelta;
                Time.timeScale = SlowTimeScale;
                Value("phase.d3.readback", Clocks());
            });
            return ok;
        }
        catch (Exception e) { return Fatal(e); }
    }

    /// <summary>A later-frame .025 natural-Death case, then the next phase's settings change.</summary>
    bool Later(string key, string next, Action change)
    {
        try
        {
            Diagnostic(key);
            bool ok = DeathCase(key, .025f, .013f);
            if (ok && next != null)
            {
                ok = Block(next, () =>
                {
                    Value(next + ".set", "{\"fixedDeltaTime\":" + F(FixedControl) + ",\"boundaryFrames\":" + N(BoundaryFrames) + "}");
                    change();
                    Value(next + ".readback", Clocks());
                });
            }
            return ok;
        }
        catch (Exception e) { return Fatal(e); }
    }

    /// <summary>Puts back the startup fixed delta, time scale and capture delta (also after a
    /// fault), so nothing the probe set reaches the project's settings. Without startup clocks
    /// the probe has set nothing and restores nothing.</summary>
    bool Restore()
    {
        try
        {
            return Block("restore", () =>
            {
                if (!startupRead) throw new InvalidOperationException("the startup clocks were not read");
                Time.fixedDeltaTime = startupFixed;
                Time.timeScale = startupTimeScale;
                Time.captureDeltaTime = startupCapture;
                Value("restore.readback", Clocks());
            });
        }
        catch (Exception e) { return Fatal(e); }
    }

    // ------------------------------------------------------------------ cases

    /// <summary>A system on an inactive object, stopped and cleared, with a fixed seed, no shape,
    /// no emission and no play on awake (the particledeathtiming probe's systems).</summary>
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

    static ParticleSystem.Particle Particle(Vector3 position, Vector3 velocity, float lifetime)
        => new ParticleSystem.Particle
        {
            position = position, velocity = velocity,
            startLifetime = lifetime, remainingLifetime = lifetime, startSize = 1f,
            startColor = new Color32(255, 255, 255, 255), randomSeed = 12345
        };

    /// <summary>A stationary parent at z .46 with one particle of remaining life
    /// <paramref name="parentLife"/>; a Death sub-emitter (speed 1, lifetime 2.013, one burst at 0,
    /// InheritNothing) and an ordinary transform child with one speed-1 particle. Two calls of
    /// Simulate(<paramref name="callDelta"/>, withChildren true, restart false, fixedTimeStep false).</summary>
    bool DeathCase(string key, float callDelta, float parentLife)
    {
        ParticleSystem parent = null;
        try
        {
            return Block(key, () =>
            {
                Value(key + ".input", "{\"callDelta\":" + F(callDelta) + ",\"calls\":2,\"parentLife\":" + F(parentLife)
                      + ",\"parentPosition\":[0,0,0.46],\"parentVelocity\":[0,0,0],\"deathChildSpeed\":1,\"deathChildLifetime\":2.013"
                      + ",\"deathBurst\":1,\"ordinarySpeed\":1,\"ordinaryLifetime\":2.013,\"withChildren\":true,\"restart\":false"
                      + ",\"fixedTimeStep\":false,\"seed\":12345}");
                Value(key + ".clock.before", Clocks());
                parent = Make(key + ".parent");
                var death = Make(key + ".death", parent.transform);
                var main = death.main; main.startSpeed = 1f;
                var emission = death.emission;
                emission.enabled = true; emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                var sub = parent.subEmitters; sub.enabled = true;
                sub.AddSubEmitter(death, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
                var ordinary = Make(key + ".ordinary", parent.transform);
                main = ordinary.main; main.startSpeed = 1f;
                parent.SetParticles(new[] { Particle(new Vector3(0f, 0f, .46f), Vector3.zero, parentLife) }, 1);
                ordinary.SetParticles(new[] { Particle(Vector3.zero, Vector3.forward, 2.013f) }, 1);
                Snapshot(key, "before", parent, death, ordinary);
                for (int i = 1; i <= 2; i++)
                {
                    parent.Simulate(callDelta, true, false, false);
                    Snapshot(key, "after" + N(i), parent, death, ordinary);
                }
                Value(key + ".clock.after", Clocks());
            });
        }
        finally
        {
            if (parent != null) DestroyImmediate(parent.gameObject);
        }
    }

    void Snapshot(string key, string step, ParticleSystem parent, ParticleSystem death, ParticleSystem ordinary)
    {
        Value(key + ".parent." + step, State(parent));
        Value(key + ".death." + step, State(death));
        Value(key + ".ordinary." + step, State(ordinary));
    }

    /// <summary>The broad particles probe's Make (tests/projects/particles, ParticlesProbe.Make).</summary>
    static ParticleSystem MakeBroad(string name, Action<ParticleSystem> configure, Vector3 position = default, Transform parent = null)
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

    /// <summary>The broad probe's sub_emitters_birth history, unchanged: 15 calls of
    /// Simulate(.05, withChildren true, restart on the first, fixedTimeStep false).</summary>
    bool BirthControl(string key)
    {
        ParticleSystem ps = null;
        try
        {
            return Block(key, () =>
            {
                Value(key + ".input", "{\"source\":\"tests/projects/particles ParticlesProbe sub_emitters_birth\",\"callDelta\":0.05,\"calls\":15"
                      + ",\"withChildren\":true,\"restartFirst\":true,\"fixedTimeStep\":false}");
                Value(key + ".clock.before", Clocks());
                ParticleSystem child = null;
                ps = MakeBroad("sub_emitters_birth", s =>
                {
                    var e = s.emission; e.rateOverTime = 5f;
                    child = MakeBroad("sub_emitters_birth_child", c =>
                    {
                        var cm = c.main; cm.startSpeed = 0.5f; cm.startLifetime = 0.413f;
                        var ce = c.emission; ce.rateOverTime = 10f;
                    }, default, s.transform);
                    var sub = s.subEmitters; sub.enabled = true;
                    sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritColor | ParticleSystemSubEmitterProperties.InheritSize);
                });
                for (int i = 0; i < 15; i++)
                {
                    ps.Simulate(0.05f, true, i == 0, false);
                    Value(key + ".parent." + N(i), State(ps));
                    Value(key + ".child." + N(i), State(child));
                }
                Value(key + ".clock.after", Clocks());
            });
        }
        finally
        {
            if (ps != null) DestroyImmediate(ps.transform.root.gameObject);
        }
    }
}
