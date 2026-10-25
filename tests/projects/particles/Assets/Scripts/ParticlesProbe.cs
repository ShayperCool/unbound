using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Builds particle systems in code, each exercising one module with fixed (non-random) values,
/// steps them with <see cref="ParticleSystem.Simulate(float, bool, bool, bool)"/> and records the
/// system's state and every particle after each step, as JSON lines in <c>PARTICLES_PROBE_OUT</c>.
/// A second phase plays systems over real frames at a fixed delta (Time.captureDeltaTime) to
/// record what only happens in the player loop: playOnAwake, stop actions, collision and trigger
/// callbacks. Then it quits. The Linux Editor's records are the reference.
/// </summary>
public class ParticlesProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Events = new List<string>();
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[4096];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ParticlesProbe").AddComponent<ParticlesProbe>();

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
        FlushEvents(scenario, step + " before");
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

    static void RecordValue(string scenario, string step, string json)
    {
        s_Out.Append("{\"scenario\":").Append(Q(scenario)).Append(",\"step\":").Append(Q(step)).Append(",\"value\":").Append(json).Append("}\n");
    }

    /// <summary>Simulates <paramref name="steps"/> steps of <paramref name="dt"/> (the first one
    /// restarting the system), recording after each; <paramref name="before"/> runs before a step.</summary>
    static void Step(string scenario, ParticleSystem ps, float dt, int steps, Action<int> before = null, bool fixedTimeStep = false, bool restart = true)
    {
        for (int i = 0; i < steps; i++)
        {
            before?.Invoke(i);
            ps.Simulate(dt, true, restart && i == 0, fixedTimeStep);
            RecordState(scenario, i.ToString(CultureInfo.InvariantCulture), ps);
            FlushEvents(scenario, i.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Records what was logged since the last flush, under the scenario that logged it.</summary>
    static void FlushEvents(string scenario, string step)
    {
        if (s_Events.Count == 0) return;
        var sb = new StringBuilder("[");
        for (int i = 0; i < s_Events.Count; i++) sb.Append(i > 0 ? "," : "").Append(Q(s_Events[i]));
        RecordValue(scenario + " log", step, sb.Append(']').ToString());
        s_Events.Clear();
    }

    static void Done(ParticleSystem ps) => DestroyImmediate(ps.transform.root.gameObject);

    static AnimationCurve Linear(float from, float to) => AnimationCurve.Linear(0f, from, 1f, to);

    /// <summary>A curve that stays at <paramref name="value"/>: the velocity axes must all be
    /// curves when one is.</summary>
    static ParticleSystem.MinMaxCurve Flat(float value) => new ParticleSystem.MinMaxCurve(value, AnimationCurve.Constant(0f, 1f, 1f));

    static string TimeNow() => "{\"deltaTime\":" + F(Time.deltaTime) + ",\"time\":" + F(Time.time) + ",\"frame\":" + Time.frameCount
        + ",\"fixedDeltaTime\":" + F(Time.fixedDeltaTime) + ",\"unscaledDeltaTime\":" + F(Time.unscaledDeltaTime) + "}";

    void Start()
    {
        RecordValue("time", "start", TimeNow());
        Application.logMessageReceived += (condition, stack, type) => s_Events.Add(type + ": " + condition);
        Simulated();
        StartCoroutine(Played());
    }

    void Simulated()
    {
        ParticleSystem ps;

        // Emission over time: when particles appear within a step and how far they get.
        ps = Make("rate", s => { });
        Step("rate", ps, 0.05f, 30);
        Done(ps);

        ps = Make("rate_fractional", s => { var e = s.emission; e.rateOverTime = 7f; });
        Step("rate_fractional", ps, 1f / 30f, 30);
        Done(ps);

        ps = Make("rate_big_step", s => { var e = s.emission; e.rateOverTime = 20f; });
        Step("rate_big_step", ps, 0.5f, 6);
        Done(ps);

        ps = Make("fixed_time_step", s => { });
        Step("fixed_time_step", ps, 0.05f, 20, null, true);
        Done(ps);

        ps = Make("bursts", s =>
        {
            var e = s.emission;
            e.rateOverTime = 0f;
            e.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 5),
                new ParticleSystem.Burst(0.25f, 3, 3, 0.1f),
            });
        });
        Step("bursts", ps, 0.05f, 20);
        Done(ps);

        ps = Make("burst_probability_interval", s =>
        {
            var e = s.emission;
            e.rateOverTime = 0f;
            var b = new ParticleSystem.Burst(0.1f, 2, 0, 0.3f);
            e.SetBursts(new[] { b });
            var m = s.main; m.duration = 1f;
        });
        Step("burst_probability_interval", ps, 0.1f, 25);
        Done(ps);

        // Duration, looping, delay, prewarm, max particles.
        ps = Make("duration_once", s => { var m = s.main; m.duration = 0.5f; m.loop = false; });
        Step("duration_once", ps, 0.1f, 20);
        Done(ps);

        ps = Make("start_delay", s => { var m = s.main; m.startDelay = 0.25f; m.duration = 1f; });
        Step("start_delay", ps, 0.1f, 20);
        Done(ps);

        ps = Make("loop_wrap", s => { var m = s.main; m.duration = 0.3f; m.loop = true; });
        Step("loop_wrap", ps, 0.1f, 12);
        Done(ps);

        ps = Make("prewarm", s => { var m = s.main; m.duration = 1f; m.loop = true; m.prewarm = true; var e = s.emission; e.rateOverTime = 9.7f; });
        Step("prewarm", ps, 0.1f, 8);
        Done(ps);

        ps = Make("max_particles", s => { var m = s.main; m.maxParticles = 5; var e = s.emission; e.rateOverTime = 50f; });
        Step("max_particles", ps, 0.05f, 12);
        Done(ps);

        ps = Make("simulation_speed", s => { var m = s.main; m.simulationSpeed = 2f; });
        Step("simulation_speed", ps, 0.05f, 15);
        Done(ps);

        // Start values.
        ps = Make("start_values", s =>
        {
            var m = s.main;
            m.startLifetime = 2.013f;
            m.startSpeed = 3f;
            m.startSize3D = true;
            m.startSizeX = 0.5f; m.startSizeY = 2f; m.startSizeZ = 1.5f;
            m.startRotation3D = true;
            m.startRotationX = 0.5f; m.startRotationY = 1f; m.startRotationZ = 1.5f;
            m.startColor = new Color(1f, 0.5f, 0.25f, 0.75f);
        });
        Step("start_values", ps, 0.1f, 10);
        Done(ps);

        ps = Make("start_curves", s =>
        {
            var m = s.main;
            m.duration = 1f;
            m.startSpeed = new ParticleSystem.MinMaxCurve(4f, Linear(0f, 1f));
            m.startSize = new ParticleSystem.MinMaxCurve(2f, Linear(1f, 0.25f));
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.537f, Linear(0.5f, 1f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
            m.startColor = new ParticleSystem.MinMaxGradient(g);
        });
        Step("start_curves", ps, 0.1f, 12);
        Done(ps);

        // Gravity and simulation space.
        ps = Make("gravity", s => { var m = s.main; m.gravityModifier = 1f; m.startSpeed = 5f; m.startLifetime = 2.013f; });
        ps.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        Step("gravity", ps, 0.05f, 30);
        Done(ps);

        ps = Make("local_space_moving", s => { var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.Local; });
        Step("local_space_moving", ps, 0.05f, 20, i => ps.transform.position = new Vector3(i * 0.1f, 0f, 0f));
        Done(ps);

        ps = Make("world_space_moving", s => { var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World; });
        Step("world_space_moving", ps, 0.05f, 20, i => ps.transform.position = new Vector3(i * 0.1f, 0f, 0f));
        Done(ps);

        ps = Make("world_space_rotated_scaled", s =>
        {
            var m = s.main;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
        });
        ps.transform.rotation = Quaternion.Euler(0f, 90f, 30f);
        ps.transform.localScale = new Vector3(2f, 1f, 0.5f);
        Step("world_space_rotated_scaled", ps, 0.1f, 10);
        Done(ps);

        ps = Make("scaling_local", s => { var m = s.main; m.scalingMode = ParticleSystemScalingMode.Local; });
        ps.transform.localScale = new Vector3(2f, 3f, 4f);
        Step("scaling_local", ps, 0.1f, 8);
        Done(ps);

        ps = Make("scaling_shape", s => { var m = s.main; m.scalingMode = ParticleSystemScalingMode.Shape; });
        ps.transform.localScale = new Vector3(2f, 3f, 4f);
        Step("scaling_shape", ps, 0.1f, 8);
        Done(ps);

        ps = Make("rate_over_distance", s =>
        {
            var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = s.emission; e.rateOverTime = 0f; e.rateOverDistance = 3.7f;
        });
        Step("rate_over_distance", ps, 0.05f, 20, i => ps.transform.position = new Vector3(i * 0.2f, 0f, 0f));
        Done(ps);

        ps = Make("inherit_velocity", s =>
        {
            var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
            var iv = s.inheritVelocity; iv.enabled = true; iv.mode = ParticleSystemInheritVelocityMode.Initial; iv.curve = 1f;
        });
        Step("inherit_velocity", ps, 0.05f, 20, i => ps.transform.position = new Vector3(i * 0.1f, 0f, 0f));
        Done(ps);

        ps = Make("inherit_velocity_current", s =>
        {
            var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
            var iv = s.inheritVelocity; iv.enabled = true; iv.mode = ParticleSystemInheritVelocityMode.Current; iv.curve = 0.5f;
        });
        Step("inherit_velocity_current", ps, 0.05f, 20, i => ps.transform.position = new Vector3(i * i * 0.01f, 0f, 0f));
        Done(ps);

        ps = Make("lifetime_by_emitter_speed", s =>
        {
            var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
            var l = s.lifetimeByEmitterSpeed; l.enabled = true; l.curve = new ParticleSystem.MinMaxCurve(1f, Linear(1f, 0.25f)); l.range = new Vector2(0f, 4f);
        });
        Step("lifetime_by_emitter_speed", ps, 0.05f, 20, i => ps.transform.position = new Vector3(i * 0.1f, 0f, 0f));
        Done(ps);

        // Velocity, limit, force.
        ps = Make("velocity_over_lifetime", s =>
        {
            var v = s.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.x = Flat(1f); v.y = new ParticleSystem.MinMaxCurve(2f, Linear(0f, 1f)); v.z = Flat(0f);
        });
        ps.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        Step("velocity_over_lifetime", ps, 0.05f, 25);
        Done(ps);

        ps = Make("velocity_mixed_modes", s =>
        {
            var v = s.velocityOverLifetime; v.enabled = true;
            v.x = 1f; v.y = new ParticleSystem.MinMaxCurve(2f, Linear(0f, 1f)); v.z = 0f;
        });
        Step("velocity_mixed_modes", ps, 0.1f, 4);
        Done(ps);

        ps = Make("velocity_world", s =>
        {
            var v = s.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = 1f; v.y = 0f; v.z = 0f;
        });
        ps.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        Step("velocity_world", ps, 0.05f, 15);
        Done(ps);

        ps = Make("velocity_orbital_radial", s =>
        {
            var m = s.main; m.startSpeed = 0f;
            var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1f; sh.radiusThickness = 0f;
            sh.arcMode = ParticleSystemShapeMultiModeValue.Loop; sh.arcSpeed = 1f;
            var v = s.velocityOverLifetime; v.enabled = true;
            v.orbitalZ = 2f; v.radial = 0.5f; v.speedModifier = 1.5f;
        });
        Step("velocity_orbital_radial", ps, 0.05f, 20);
        Done(ps);

        ps = Make("limit_velocity", s =>
        {
            var m = s.main; m.startSpeed = 6f; m.startLifetime = 2.013f;
            var l = s.limitVelocityOverLifetime; l.enabled = true; l.limit = 2f; l.dampen = 0.5f;
        });
        Step("limit_velocity", ps, 0.05f, 20);
        Done(ps);

        ps = Make("drag", s =>
        {
            var m = s.main; m.startSpeed = 6f; m.startLifetime = 2.013f; m.startSize = 2f;
            var l = s.limitVelocityOverLifetime; l.enabled = true; l.limit = 100f; l.dampen = 0f;
            l.drag = 1.5f; l.multiplyDragByParticleSize = true; l.multiplyDragByParticleVelocity = true;
        });
        Step("drag", ps, 0.05f, 20);
        Done(ps);

        ps = Make("limit_separate_axes", s =>
        {
            var m = s.main; m.startSpeed = 5f;
            var v = s.velocityOverLifetime; v.enabled = true; v.x = 4f; v.y = 0f; v.z = 0f;
            var l = s.limitVelocityOverLifetime; l.enabled = true; l.separateAxes = true;
            l.limitX = 1f; l.limitY = 10f; l.limitZ = 2f; l.dampen = 1f; l.space = ParticleSystemSimulationSpace.Local;
        });
        Step("limit_separate_axes", ps, 0.05f, 15);
        Done(ps);

        ps = Make("force_over_lifetime", s =>
        {
            var f = s.forceOverLifetime; f.enabled = true; f.space = ParticleSystemSimulationSpace.World;
            f.x = 0f; f.y = -3f; f.z = new ParticleSystem.MinMaxCurve(2f, Linear(1f, -1f));
        });
        Step("force_over_lifetime", ps, 0.05f, 25);
        Done(ps);

        // Color, size, rotation over lifetime and by speed.
        ps = Make("color_over_lifetime", s =>
        {
            var c = s.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
        });
        Step("color_over_lifetime", ps, 0.05f, 25);
        Done(ps);

        ps = Make("color_by_speed", s =>
        {
            var v = s.velocityOverLifetime; v.enabled = true; v.x = Flat(0f); v.y = Flat(0f); v.z = new ParticleSystem.MinMaxCurve(4f, Linear(0f, 1f));
            var c = s.colorBySpeed; c.enabled = true; c.range = new Vector2(2f, 6f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            c.color = g;
        });
        Step("color_by_speed", ps, 0.05f, 25);
        Done(ps);

        ps = Make("size_over_lifetime", s =>
        {
            var z = s.sizeOverLifetime; z.enabled = true; z.size = new ParticleSystem.MinMaxCurve(1.5f, Linear(1f, 0f));
        });
        Step("size_over_lifetime", ps, 0.05f, 25);
        Done(ps);

        ps = Make("size_separate_axes", s =>
        {
            var m = s.main; m.startSize3D = true; m.startSizeX = 1f; m.startSizeY = 2f; m.startSizeZ = 3f;
            var z = s.sizeOverLifetime; z.enabled = true; z.separateAxes = true;
            z.x = new ParticleSystem.MinMaxCurve(1f, Linear(1f, 0f)); z.y = 2f; z.z = new ParticleSystem.MinMaxCurve(1f, Linear(0f, 1f));
        });
        Step("size_separate_axes", ps, 0.05f, 20);
        Done(ps);

        ps = Make("size_by_speed", s =>
        {
            var v = s.velocityOverLifetime; v.enabled = true; v.x = Flat(0f); v.y = Flat(0f); v.z = new ParticleSystem.MinMaxCurve(4f, Linear(0f, 1f));
            var z = s.sizeBySpeed; z.enabled = true; z.range = new Vector2(2f, 6f); z.size = new ParticleSystem.MinMaxCurve(1f, Linear(0.5f, 2f));
        });
        Step("size_by_speed", ps, 0.05f, 25);
        Done(ps);

        ps = Make("rotation_over_lifetime", s =>
        {
            var m = s.main; m.startRotation = 0.25f;
            var r = s.rotationOverLifetime; r.enabled = true; r.z = Mathf.PI;
        });
        Step("rotation_over_lifetime", ps, 0.05f, 20);
        Done(ps);

        ps = Make("rotation_separate_axes", s =>
        {
            var r = s.rotationOverLifetime; r.enabled = true; r.separateAxes = true;
            r.x = 1f; r.y = new ParticleSystem.MinMaxCurve(2f, Linear(1f, 0f)); r.z = -1f;
        });
        Step("rotation_separate_axes", ps, 0.05f, 20);
        Done(ps);

        ps = Make("rotation_by_speed", s =>
        {
            var v = s.velocityOverLifetime; v.enabled = true; v.x = Flat(0f); v.y = Flat(0f); v.z = new ParticleSystem.MinMaxCurve(4f, Linear(0f, 1f));
            var r = s.rotationBySpeed; r.enabled = true; r.range = new Vector2(2f, 6f); r.z = new ParticleSystem.MinMaxCurve(3f, Linear(0f, 1f));
        });
        Step("rotation_by_speed", ps, 0.05f, 20);
        Done(ps);

        ps = Make("flip_rotation", s => { var m = s.main; m.startRotation = 1f; m.flipRotation = 1f; var r = s.rotationOverLifetime; r.enabled = true; r.z = 1f; });
        Step("flip_rotation", ps, 0.1f, 8);
        Done(ps);

        // Shapes with their direction modes but no randomness.
        foreach (var (name, type) in new[]
        {
            ("shape_cone_loop", ParticleSystemShapeType.Cone),
            ("shape_circle_loop", ParticleSystemShapeType.Circle),
            ("shape_cone_volume_loop", ParticleSystemShapeType.ConeVolume),
            ("shape_donut_loop", ParticleSystemShapeType.Donut),
            ("shape_edge_loop", ParticleSystemShapeType.SingleSidedEdge),
        })
        {
            ps = Make(name, s =>
            {
                var sh = s.shape; sh.enabled = true; sh.shapeType = type; sh.radius = 1.5f; sh.radiusThickness = 0f; sh.angle = 30f;
                sh.arc = 270f; sh.arcMode = ParticleSystemShapeMultiModeValue.Loop; sh.arcSpeed = 0.5f; sh.arcSpread = 0f;
                sh.radiusMode = ParticleSystemShapeMultiModeValue.Loop; sh.radiusSpeed = 1f; sh.length = 2f; sh.donutRadius = 0.25f;
                sh.position = new Vector3(0.5f, 0f, 0f); sh.rotation = new Vector3(0f, 0f, 30f); sh.scale = new Vector3(1f, 2f, 1f);
                var e = s.emission; e.rateOverTime = 20f;
            });
            Step(name, ps, 0.05f, 12);
            Done(ps);
        }

        ps = Make("shape_arc_burst_spread", s =>
        {
            var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1f; sh.radiusThickness = 0f;
            sh.arc = 360f; sh.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
            var e = s.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
        });
        Step("shape_arc_burst_spread", ps, 0.05f, 4);
        Done(ps);

        ps = Make("shape_ping_pong", s =>
        {
            var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1f; sh.radiusThickness = 0f;
            sh.arc = 180f; sh.arcMode = ParticleSystemShapeMultiModeValue.PingPong; sh.arcSpeed = 1.9f;
            var e = s.emission; e.rateOverTime = 20f;
        });
        Step("shape_ping_pong", ps, 0.05f, 16);
        Done(ps);

        ps = Make("shape_align_to_direction", s =>
        {
            var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1f; sh.radiusThickness = 0f;
            sh.arcMode = ParticleSystemShapeMultiModeValue.Loop; sh.arcSpeed = 1f; sh.alignToDirection = true;
        });
        Step("shape_align_to_direction", ps, 0.1f, 8);
        Done(ps);

        // Shapes with random placement: the count and where they are allowed to be.
        foreach (var (name, type) in new[]
        {
            ("random_sphere", ParticleSystemShapeType.Sphere),
            ("random_hemisphere", ParticleSystemShapeType.Hemisphere),
            ("random_box", ParticleSystemShapeType.Box),
            ("random_cone", ParticleSystemShapeType.Cone),
        })
        {
            ps = Make(name, s =>
            {
                var m = s.main; m.startSpeed = 0f;
                var sh = s.shape; sh.enabled = true; sh.shapeType = type; sh.radius = 1f; sh.angle = 20f;
                var e = s.emission; e.rateOverTime = 40f;
            });
            Step(name, ps, 0.1f, 5);
            Done(ps);
        }

        // Emit, SetParticles, custom data, sub-emitters.
        ps = Make("emit_params", s => { var e = s.emission; e.enabled = false; });
        ps.Simulate(0f, true, true, false);
        var ep = new ParticleSystem.EmitParams
        {
            position = new Vector3(1f, 2f, 3f),
            velocity = new Vector3(0f, 1f, 0f),
            startLifetime = 3f,
            startSize = 0.5f,
            startColor = new Color32(10, 20, 30, 40),
            rotation = 45f,
            angularVelocity = 90f,
        };
        ps.Emit(ep, 2);
        ps.Emit(3);
        var ep2 = new ParticleSystem.EmitParams { applyShapeToPosition = true, position = new Vector3(0f, 1f, 0f) };
        ps.Emit(ep2, 1);
        RecordState("emit_params", "emitted", ps);
        Step("emit_params", ps, 0.1f, 5, null, false, false);
        Done(ps);

        ps = Make("emit_while_stopped", s => { var e = s.emission; e.rateOverTime = 0f; });
        ps.Emit(4);
        RecordState("emit_while_stopped", "emitted", ps);
        ps.Simulate(0.1f, true, false, false);
        RecordState("emit_while_stopped", "simulated", ps);
        Done(ps);

        ps = Make("set_particles", s => { var e = s.emission; e.rateOverTime = 20f; });
        ps.Simulate(0.3f, true, true, false);
        {
            int n = ps.GetParticles(s_Buffer);
            for (int i = 0; i < n; i++)
            {
                s_Buffer[i].position += Vector3.up;
                s_Buffer[i].velocity = new Vector3(1f, 0f, 0f);
                if (i == 0) s_Buffer[i].remainingLifetime = -1f;
            }
            ps.SetParticles(s_Buffer, n);
        }
        RecordState("set_particles", "set", ps);
        Step("set_particles", ps, 0.1f, 3, null, false, false);
        Done(ps);

        ps = Make("custom_data", s =>
        {
            var cd = s.customData; cd.enabled = true;
            cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 3);
            cd.SetVector(ParticleSystemCustomData.Custom1, 0, 2f);
            cd.SetVector(ParticleSystemCustomData.Custom1, 1, new ParticleSystem.MinMaxCurve(1f, Linear(0f, 1f)));
            cd.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Color);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.yellow, 0f), new GradientColorKey(Color.cyan, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            cd.SetColor(ParticleSystemCustomData.Custom2, g);
        });
        for (int i = 0; i < 6; i++)
        {
            ps.Simulate(0.1f, true, i == 0, false);
            var data1 = new List<Vector4>();
            var data2 = new List<Vector4>();
            ps.GetCustomParticleData(data1, ParticleSystemCustomData.Custom1);
            ps.GetCustomParticleData(data2, ParticleSystemCustomData.Custom2);
            var sb = new StringBuilder("[");
            for (int k = 0; k < data1.Count; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append('[').Append(V(data1[k])).Append(',').Append(F(data1[k].w)).Append(',').Append(V(data2[k])).Append(',').Append(F(data2[k].w)).Append(']');
            }
            RecordValue("custom_data", i.ToString(CultureInfo.InvariantCulture), sb.Append(']').ToString());
        }
        Done(ps);

        {
            ParticleSystem child = null;
            ps = Make("sub_emitters", s =>
            {
                var m = s.main; m.startLifetime = 0.513f;
                var e = s.emission; e.rateOverTime = 4f;
                child = Make("sub_emitters_child", c =>
                {
                    var cm = c.main; cm.startSpeed = 1f; cm.startLifetime = 0.313f;
                    var ce = c.emission; ce.rateOverTime = 0f; ce.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
                }, default, s.transform);
                var sub = s.subEmitters; sub.enabled = true;
                sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            });
            for (int i = 0; i < 20; i++)
            {
                ps.Simulate(0.05f, true, i == 0, false);
                RecordState("sub_emitters", i.ToString(CultureInfo.InvariantCulture), ps);
                RecordState("sub_emitters_child", i.ToString(CultureInfo.InvariantCulture), child);
            }
            Done(ps);
        }

        {
            ParticleSystem child = null;
            ps = Make("sub_emitters_birth", s =>
            {
                var e = s.emission; e.rateOverTime = 5f;
                child = Make("sub_emitters_birth_child", c =>
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
                RecordState("sub_emitters_birth", i.ToString(CultureInfo.InvariantCulture), ps);
                RecordState("sub_emitters_birth_child", i.ToString(CultureInfo.InvariantCulture), child);
            }
            Done(ps);
        }

        // Stop, clear, pause and the queries.
        ps = Make("stop_emitting", s => { });
        Step("stop_emitting", ps, 0.1f, 15, i => { if (i == 5) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); });
        Done(ps);

        ps = Make("stop_clear", s => { });
        ps.Simulate(0.5f, true, true, false);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        RecordState("stop_clear", "stopped", ps);
        ps.Clear();
        RecordState("stop_clear", "cleared", ps);
        Done(ps);

        ps = Make("play_pause", s => { });
        ps.Play();
        RecordState("play_pause", "play", ps);
        ps.Simulate(0.2f, true, false, false);
        RecordState("play_pause", "simulated", ps);
        ps.Pause();
        RecordState("play_pause", "pause", ps);
        ps.Play();
        RecordState("play_pause", "play_again", ps);
        ps.Simulate(0.2f, true, false, false);
        RecordState("play_pause", "simulated_again", ps);
        ps.time = 0.05f;
        RecordState("play_pause", "time_set", ps);
        Done(ps);

        // Collision with planes and the world, triggers, external forces.
        {
            var plane = new GameObject("collision_plane").transform;
            plane.position = new Vector3(0f, -1f, 0f);
            ps = Make("collision_planes", s =>
            {
                var m = s.main; m.startSpeed = 4f; m.startLifetime = 2.013f; m.startSize = 0.2f;
                var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes;
                c.AddPlane(plane); c.dampen = 0.25f; c.bounce = 0.8f; c.lifetimeLoss = 0.1f; c.radiusScale = 1f;
            });
            ps.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            Step("collision_planes", ps, 0.05f, 30);
            Done(ps);
            DestroyImmediate(plane.gameObject);
        }

        {
            var plane = new GameObject("collision_plane_kill").transform;
            plane.position = new Vector3(0f, -0.5f, 0f);
            ps = Make("collision_planes_kill", s =>
            {
                var m = s.main; m.startSpeed = 4f; m.startLifetime = 2.013f;
                var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes;
                c.AddPlane(plane); c.minKillSpeed = 3f; c.bounce = 0.5f;
            });
            ps.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Step("collision_planes_kill", ps, 0.05f, 20);
            Done(ps);
            DestroyImmediate(plane.gameObject);
        }

        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "collision_floor";
            floor.transform.position = new Vector3(0f, -1.5f, 0f);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            Physics.SyncTransforms();
            ps = Make("collision_world", s =>
            {
                var m = s.main; m.startSpeed = 4f; m.startLifetime = 2.013f; m.startSize = 0.2f; m.gravityModifier = 1f;
                m.simulationSpace = ParticleSystemSimulationSpace.World;
                var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.High; c.dampen = 0f; c.bounce = 0.5f; c.radiusScale = 1f;
            });
            ps.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            Step("collision_world", ps, 0.05f, 30);
            Done(ps);
            DestroyImmediate(floor);
        }

        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "trigger_sphere";
            sphere.transform.position = new Vector3(0f, 0f, 1.5f);
            sphere.GetComponent<Collider>().isTrigger = true;
            ps = Make("trigger_kill_inside", s =>
            {
                var m = s.main; m.startSpeed = 2f; m.startLifetime = 2.013f;
                var t = s.trigger; t.enabled = true; t.AddCollider(sphere.GetComponent<Collider>());
                t.inside = ParticleSystemOverlapAction.Kill; t.outside = ParticleSystemOverlapAction.Ignore;
                t.enter = ParticleSystemOverlapAction.Ignore; t.exit = ParticleSystemOverlapAction.Ignore;
            });
            Step("trigger_kill_inside", ps, 0.05f, 30);
            Done(ps);
            DestroyImmediate(sphere);
        }

        {
            var field = new GameObject("force_field").AddComponent<ParticleSystemForceField>();
            field.shape = ParticleSystemForceFieldShape.Sphere;
            field.endRange = 100f;
            field.directionY = -2f;
            field.gravity = 0f;
            ps = Make("external_forces", s =>
            {
                var x = s.externalForces; x.enabled = true; x.multiplier = 1.5f;
            });
            Step("external_forces", ps, 0.05f, 20);
            Done(ps);
            DestroyImmediate(field.gameObject);
        }

        {
            var field = new GameObject("force_field_gravity").AddComponent<ParticleSystemForceField>();
            field.shape = ParticleSystemForceFieldShape.Sphere;
            field.endRange = 100f;
            field.gravity = 3f;
            field.gravityFocus = 0f;
            field.transform.position = new Vector3(0f, 0f, 3f);
            ps = Make("external_forces_gravity", s =>
            {
                var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
                var x = s.externalForces; x.enabled = true;
            });
            Step("external_forces_gravity", ps, 0.05f, 20);
            Done(ps);
            DestroyImmediate(field.gameObject);
        }

        // One long Simulate call, and prewarm with lifetimes shorter and longer than the duration.
        ps = Make("simulate_long_call", s => { var e = s.emission; e.rateOverTime = 20f; });
        ps.Simulate(0.3f, true, true, false);
        RecordState("simulate_long_call", "0.3", ps);
        ps.Simulate(0.7f, true, true, false);
        RecordState("simulate_long_call", "0.7 restarted", ps);
        Done(ps);

        ps = Make("prewarm_long_life", s => { var m = s.main; m.duration = 1f; m.loop = true; m.prewarm = true; m.startLifetime = 2.537f; var e = s.emission; e.rateOverTime = 9.7f; });
        Step("prewarm_long_life", ps, 0.1f, 4);
        Done(ps);

        ps = Make("prewarm_short_life", s => { var m = s.main; m.duration = 2f; m.loop = true; m.prewarm = true; m.startLifetime = 0.737f; var e = s.emission; e.rateOverTime = 9.7f; });
        Step("prewarm_short_life", ps, 0.1f, 4);
        Done(ps);

        // Edge loop with another radius: how fast the emission point moves along it.
        ps = Make("shape_edge_loop_r1", s =>
        {
            var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.SingleSidedEdge; sh.radius = 1f;
            sh.radiusMode = ParticleSystemShapeMultiModeValue.Loop; sh.radiusSpeed = 0.7f;
            var e = s.emission; e.rateOverTime = 20f;
        });
        Step("shape_edge_loop_r1", ps, 0.05f, 12);
        Done(ps);

        // Kill speeds: is the speed before or after the bounce compared?
        {
            var plane = new GameObject("collision_plane_killspeed").transform;
            plane.position = new Vector3(0f, -1f, 0f);
            ps = Make("collision_kill_speed", s =>
            {
                var m = s.main; m.startSpeed = 4f; m.startLifetime = 2.013f; m.startSize = 0.2f;
                var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes;
                c.AddPlane(plane); c.minKillSpeed = 3f; c.bounce = 0.5f;
            });
            ps.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Step("collision_kill_speed", ps, 0.05f, 12);
            Done(ps);
            DestroyImmediate(plane.gameObject);
        }

        // Triggers: which particles count as inside, and what the actions do.
        foreach (var (name, z, inside, outside) in new[]
        {
            ("trigger_kill_far", 2.5f, ParticleSystemOverlapAction.Kill, ParticleSystemOverlapAction.Ignore),
            ("trigger_kill_outside", 2.5f, ParticleSystemOverlapAction.Ignore, ParticleSystemOverlapAction.Kill),
            ("trigger_callback_inside", 2.5f, ParticleSystemOverlapAction.Callback, ParticleSystemOverlapAction.Ignore),
        })
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name + "_sphere";
            sphere.transform.position = new Vector3(0f, 0f, z);
            sphere.GetComponent<Collider>().isTrigger = true;
            ps = Make(name, s =>
            {
                var m = s.main; m.startSpeed = 2f; m.startLifetime = 2.013f; m.startSize = 0.4f;
                var t = s.trigger; t.enabled = true; t.AddCollider(sphere.GetComponent<Collider>());
                t.inside = inside; t.outside = outside;
                t.enter = ParticleSystemOverlapAction.Ignore; t.exit = ParticleSystemOverlapAction.Ignore;
            });
            var inner = new List<ParticleSystem.Particle>();
            for (int i = 0; i < 20; i++)
            {
                ps.Simulate(0.05f, true, i == 0, false);
                RecordState(name, i.ToString(CultureInfo.InvariantCulture), ps);
                RecordValue(name + " inside", i.ToString(CultureInfo.InvariantCulture), ps.GetTriggerParticles(ParticleSystemTriggerEventType.Inside, inner).ToString(CultureInfo.InvariantCulture));
            }
            Done(ps);
            DestroyImmediate(sphere);
        }

        // Force field rotation, drag and range.
        foreach (var (name, setup) in new (string, Action<ParticleSystemForceField>)[]
        {
            ("external_forces_rotation", f => { f.rotationSpeed = 2f; f.rotationAttraction = 0.5f; f.transform.position = new Vector3(1f, 0f, 0f); }),
            ("external_forces_drag", f => { f.drag = 1.5f; f.multiplyDragByParticleSize = false; f.multiplyDragByParticleVelocity = false; }),
            ("external_forces_range", f => { f.directionX = 3f; f.endRange = 0.5f; f.startRange = 0.1f; }),
            ("external_forces_box", f => { f.shape = ParticleSystemForceFieldShape.Box; f.directionY = 2f; f.endRange = 0.4f; f.transform.rotation = Quaternion.Euler(0f, 0f, 90f); }),
        })
        {
            var field = new GameObject(name + "_field").AddComponent<ParticleSystemForceField>();
            field.shape = ParticleSystemForceFieldShape.Sphere;
            field.endRange = 100f;
            field.gravity = 0f;
            setup(field);
            ps = Make(name, s => { var x = s.externalForces; x.enabled = true; });
            Step(name, ps, 0.05f, 16);
            Done(ps);
            DestroyImmediate(field.gameObject);
        }

        // Noise: how much it moves particles (Unity's own noise field).
        ps = Make("noise", s =>
        {
            var m = s.main; m.startSpeed = 0f;
            var nz = s.noise; nz.enabled = true; nz.strength = 1f; nz.frequency = 0.5f; nz.scrollSpeed = 0f; nz.damping = true;
            nz.quality = ParticleSystemNoiseQuality.High; nz.octaveCount = 1;
        });
        Step("noise", ps, 0.1f, 8);
        Done(ps);
    }

    System.Collections.IEnumerator Played()
    {
        FlushEvents("simulated", "end");
        Time.captureDeltaTime = 1f / 30f;
        var plane = new GameObject("played_plane").transform;
        plane.position = new Vector3(0f, -1f, 0f);
        var played = new List<ParticleSystem>();

        var awake = new GameObject("played_awake");
        awake.SetActive(false);
        var a = awake.AddComponent<ParticleSystem>();
        a.useAutoRandomSeed = false;
        {
            var m = a.main; m.duration = 0.5f; m.loop = false; m.startLifetime = 0.313f; m.stopAction = ParticleSystemStopAction.Callback;
            var sh = a.shape; sh.enabled = false;
        }
        awake.AddComponent<Listener>().tag = "played_awake";
        awake.SetActive(true);
        played.Add(a);

        var collide = Make("played_collision", s =>
        {
            var m = s.main; m.startSpeed = 4f; m.startLifetime = 1.513f; m.playOnAwake = true;
            var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.AddPlane(plane);
            c.sendCollisionMessages = true; c.bounce = 1f; c.dampen = 0f;
        });
        collide.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        collide.gameObject.AddComponent<Listener>().tag = "played_collision";
        collide.Play();
        played.Add(collide);

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "played_trigger_box";
        box.transform.position = new Vector3(0f, 0f, 1f);
        box.GetComponent<Collider>().isTrigger = true;
        var trig = Make("played_trigger", s =>
        {
            var m = s.main; m.startSpeed = 2f; m.startLifetime = 1.513f;
            var t = s.trigger; t.enabled = true; t.AddCollider(box.GetComponent<Collider>());
            t.enter = ParticleSystemOverlapAction.Callback; t.exit = ParticleSystemOverlapAction.Callback;
            t.inside = ParticleSystemOverlapAction.Ignore; t.outside = ParticleSystemOverlapAction.Ignore;
        });
        trig.gameObject.AddComponent<Listener>().tag = "played_trigger";
        trig.Play();
        played.Add(trig);

        var destroy = Make("played_destroy", s =>
        {
            var m = s.main; m.duration = 0.2f; m.loop = false; m.startLifetime = 0.213f; m.stopAction = ParticleSystemStopAction.Destroy;
        });
        destroy.Play();

        for (int frame = 0; frame < 45; frame++)
        {
            yield return null;
            if (frame < 4) RecordValue("time", "frame " + frame, TimeNow());
            FlushEvents("played", "frame " + frame);
            foreach (var ps in played)
                if (ps != null) RecordState(ps.name, "frame " + frame, ps);
            RecordValue("played_destroy", "frame " + frame, destroy == null ? "\"destroyed\"" : "\"exists\"");
        }

        string path = Environment.GetEnvironmentVariable("PARTICLES_PROBE_OUT");
        if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Records the particle callbacks of the object it is on.</summary>
    class Listener : MonoBehaviour
    {
        public new string tag;
        readonly List<ParticleCollisionEvent> m_Collisions = new List<ParticleCollisionEvent>();
        readonly List<ParticleSystem.Particle> m_Inside = new List<ParticleSystem.Particle>();

        void OnParticleSystemStopped() => s_Events.Add(tag + " OnParticleSystemStopped");

        void OnParticleCollision(GameObject other)
        {
            var ps = GetComponent<ParticleSystem>();
            int n = ps.GetCollisionEvents(other, m_Collisions);
            var sb = new StringBuilder(tag + " OnParticleCollision " + other.name + " " + n);
            foreach (var e in m_Collisions) sb.Append(' ').Append(V(e.intersection)).Append(V(e.normal)).Append(V(e.velocity));
            s_Events.Add(sb.ToString());
        }

        void OnParticleTrigger()
        {
            var ps = GetComponent<ParticleSystem>();
            int enter = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Enter, m_Inside);
            int exit = ps.GetTriggerParticles(ParticleSystemTriggerEventType.Exit, m_Inside);
            s_Events.Add(tag + " OnParticleTrigger enter " + enter + " exit " + exit);
        }
    }
}
