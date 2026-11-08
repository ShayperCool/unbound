using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records how a particle system's gravity follows the physics settings: the project's
/// DynamicsManager / Physics2DSettings gravity (non-default, not along -Y), Physics.gravity and
/// Physics2D.gravity changed between Simulate steps and between played frames, the 2D source,
/// gravity in local simulation space, and constant, curve and random gravity modifiers. Each
/// record is one JSON line {"key", "value": [numbers]} in <c>PARTICLE_GRAVITY_OUT</c>.
/// </summary>
public class ParticleGravityProbe : MonoBehaviour
{
    const float Dt = 0.043f;
    const int Steps = 25;
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[64];
    static string s_Path;
    static Vector3 s_Gravity3D;
    static Vector2 s_Gravity2D;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_GRAVITY_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        new GameObject("ParticleGravityProbe").AddComponent<ParticleGravityProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Record(string key, params float[] values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    /// <summary>The count, then every particle's position and velocity.</summary>
    static void RecordParticles(string key, ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Buffer);
        var values = new float[1 + n * 6];
        values[0] = n;
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            values[1 + i * 6] = p.position.x; values[2 + i * 6] = p.position.y; values[3 + i * 6] = p.position.z;
            values[4 + i * 6] = p.velocity.x; values[5 + i * 6] = p.velocity.y; values[6 + i * 6] = p.velocity.z;
        }
        Record(key, values);
    }

    /// <summary>A system on an inactive object (local simulation space, unrotated: gravity as in
    /// world space), stopped and cleared, with a fixed seed, no shape, no play on awake and one burst of <paramref name="burst"/> particles at time 0 moving along
    /// +Z at 3 units/s; <paramref name="configure"/> sets the rest.</summary>
    static ParticleSystem Make(string name, Action<ParticleSystem> configure, int burst = 1)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.position = new Vector3(0.5f, 1f, -2f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 4242;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = 10f;
        main.startSpeed = 3f;
        main.startSize = 0.2f;
        main.gravityModifier = 1f;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });
        configure(ps);
        go.SetActive(true);
        return ps;
    }

    /// <summary>Simulates <see cref="Steps"/> steps (the first restarting the system), recording
    /// after each; <paramref name="before"/> runs before a step.</summary>
    static void Step(string scenario, ParticleSystem ps, Action<int, ParticleSystem> before = null)
    {
        for (int i = 0; i < Steps; i++)
        {
            before?.Invoke(i, ps);
            ps.Simulate(Dt, true, i == 0, false);
            RecordParticles(scenario + "/" + i.ToString(CultureInfo.InvariantCulture), ps);
        }
        DestroyImmediate(ps.gameObject);
    }

    static void Restore()
    {
        Physics.gravity = s_Gravity3D;
        Physics2D.gravity = s_Gravity2D;
    }

    static void Simulated()
    {
        foreach (float modifier in new[] { 0f, 1f, 0.5f, -1.5f })
        {
            string m = modifier.ToString("R", CultureInfo.InvariantCulture);
            Step("3d/modifier" + m, Make("3d", s => { var main = s.main; main.gravityModifier = modifier; }));
            Step("2d/modifier" + m, Make("2d", s =>
            {
                var main = s.main; main.gravityModifier = modifier; main.gravitySource = ParticleSystemGravitySource.Physics2D;
            }));
        }

        // Gravity changed between steps of a running simulation, then restored.
        Step("3d/runtime", Make("3d", s => { }), (i, ps) =>
        {
            if (i == 6) Physics.gravity = new Vector3(0f, 3f, -1f);
            if (i == 13) Physics.gravity = new Vector3(-2f, 0f, 0f);
            if (i == 20) Physics2D.gravity = new Vector2(7f, 7f); // the other source: no effect expected
        });
        Restore();
        Step("2d/runtime", Make("2d", s => { var main = s.main; main.gravitySource = ParticleSystemGravitySource.Physics2D; }), (i, ps) =>
        {
            if (i == 6) Physics2D.gravity = new Vector2(0f, 3f);
            if (i == 13) Physics2D.gravity = new Vector2(4f, -1f);
            if (i == 20) Physics.gravity = new Vector3(7f, 7f, 7f);
        });
        Restore();
        // The source switched on a running system, and back.
        Step("switch/source", Make("switch", s => { }), (i, ps) =>
        {
            var main = ps.main;
            if (i == 8) main.gravitySource = ParticleSystemGravitySource.Physics2D;
            if (i == 16) main.gravitySource = ParticleSystemGravitySource.Physics3D;
        });
        Restore();

        // World simulation space on a rotated, scaled emitter: gravity as it is.
        Step("world/3d", Make("world", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
            s.transform.rotation = Quaternion.Euler(30f, 45f, 10f);
            s.transform.localScale = new Vector3(2f, 1f, 0.5f);
        }));
        Step("world/2d", Make("world", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravitySource = ParticleSystemGravitySource.Physics2D;
            s.transform.rotation = Quaternion.Euler(30f, 45f, 10f);
        }));

        // Local simulation space: gravity stays a world direction.
        Step("local/rotated", Make("local", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            s.transform.rotation = Quaternion.Euler(30f, 45f, 10f);
        }));
        Step("local/rotated_scaled_hierarchy", Make("local", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            s.transform.rotation = Quaternion.Euler(30f, 45f, 10f);
            s.transform.localScale = new Vector3(2f, 1f, 0.5f);
        }));
        var parent = new GameObject("scaled parent").transform;
        parent.localScale = new Vector3(0.5f, 3f, 1f);
        Step("local/rotated_scaled_local", Make("local", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            s.transform.SetParent(parent, false);
            s.transform.rotation = Quaternion.Euler(30f, 45f, 10f);
            s.transform.localScale = new Vector3(2f, 1f, 0.5f);
        }));
        Step("local/rotated_scaled_shape", Make("local", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            s.transform.SetParent(parent, false);
            s.transform.rotation = Quaternion.Euler(30f, 45f, 10f);
            s.transform.localScale = new Vector3(2f, 1f, 0.5f);
        }));
        var custom = new GameObject("custom space").transform;
        custom.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(-20f, 70f, 5f));
        custom.localScale = new Vector3(4f, 0.5f, 2f);
        Step("custom/rotated_scaled", Make("custom", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = custom;
        }));
        Step("custom/rotated_scaled_still", Make("custom", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = custom;
            main.startSpeed = 0f;
        }));
        DestroyImmediate(parent.gameObject);
        DestroyImmediate(custom.gameObject);
        Step("local/rotated_2d", Make("local", s =>
        {
            var main = s.main; main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravitySource = ParticleSystemGravitySource.Physics2D;
            s.transform.rotation = Quaternion.Euler(0f, 0f, 60f);
        }));

        // Modifier curves over the system's duration (1 s, looping), and per-particle random modes.
        var rise = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        var fall = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, -0.5f), new Keyframe(1f, 0.25f));
        Step("curve/3d", Make("curve", s => { var main = s.main; main.gravityModifier = new ParticleSystem.MinMaxCurve(2f, rise); }));
        Step("curve/2d", Make("curve", s =>
        {
            var main = s.main; main.gravityModifier = new ParticleSystem.MinMaxCurve(1.5f, fall);
            main.gravitySource = ParticleSystemGravitySource.Physics2D;
        }));
        Step("random/constants", Make("random", s =>
        {
            var main = s.main; main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.5f, 2f);
        }, 5));
        Step("random/curves", Make("random", s =>
        {
            var main = s.main; main.gravityModifier = new ParticleSystem.MinMaxCurve(1.5f, rise, fall);
        }, 5));
        // Random constants given with the minimum above the maximum.
        Step("random/constants_reversed", Make("random", s =>
        {
            var main = s.main; main.gravityModifier = new ParticleSystem.MinMaxCurve(2f, -0.5f);
        }, 5));
        // A start delay: particles emitted during it fall under the modifier curve, read at the
        // system time the delay leaves (constant and rising curve), looping and not.
        foreach (var (name, loop) in new[] { ("delay/curve", true), ("delay/curve_once", false) })
        {
            bool l = loop;
            Step(name, Make("delay", s =>
            {
                var main = s.main; main.startDelay = 0.2f; main.loop = l;
                main.gravityModifier = new ParticleSystem.MinMaxCurve(2f, rise);
            }), (i, ps) => { if (i == 1 || i == 6) ps.Emit(2); });
        }
    }

    void Start() => StartCoroutine(Probe());

    IEnumerator Probe()
    {
        s_Gravity3D = Physics.gravity;
        s_Gravity2D = Physics2D.gravity;
        Record("settings/gravity3d", s_Gravity3D.x, s_Gravity3D.y, s_Gravity3D.z);
        Record("settings/gravity2d", s_Gravity2D.x, s_Gravity2D.y);
        try { Simulated(); }
        finally { Restore(); }

        // Played over frames at a fixed delta: gravity changed in Update (before the frame's
        // particle update) at frame 5, each frame's state recorded in the next frame's Update.
        Time.captureDeltaTime = 0.02f;
        yield return null;
        // The scene has no camera: an invisible looping system would pause under automatic culling.
        var played3D = Make("played3d", s => { var main = s.main; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; });
        var played2D = Make("played2d", s =>
        {
            var main = s.main; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.gravitySource = ParticleSystemGravitySource.Physics2D;
        });
        played3D.Play();
        played2D.Play();
        for (int frame = 0; frame < 12; frame++)
        {
            yield return null;
            RecordParticles("played/3d/" + frame, played3D);
            RecordParticles("played/2d/" + frame, played2D);
            Record("played/delta/" + frame, Time.deltaTime);
            if (frame == 5)
            {
                Physics.gravity = new Vector3(0f, 2f, 1f);
                Physics2D.gravity = new Vector2(3f, 0f);
            }
        }
        Restore();
        Time.captureDeltaTime = 0f;
        Record("complete", 1);
        File.WriteAllText(s_Path, s_Out.ToString());
        Debug.Log("particle gravity complete: " + s_Path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
