using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Prewarm, Birth emission ends and sub-emitter inheritance:
/// <list type="bullet">
/// <item><c>prewarm/*</c>: prewarmed looping parents (one burst particle moving at 1 unit/s, or a
/// rate) with and without sub-emitters, right after Play and after two frames of 0.05 s: every
/// particle's age, position and seed; parent lifetime, duration, child lifetime (constant, two
/// constants), a grandchild, a Death child, child bursts, a looping child and a parent outliving a
/// non-looping child's duration.</item>
/// <item><c>birthend/*</c>: played and simulated (0.02 s) Birth children of a non-looping child whose
/// duration the parent outlives, and of children whose Emission module is disabled.</item>
/// <item><c>inherit/*</c>: Birth (constant and curve parent lifetimes), Death, Collision and Trigger
/// children inheriting colour, size, rotation, lifetime or duration from parents with colour, size
/// and rotation over lifetime, children with a start size curve; a three-level chain and a looping
/// parent. Children by seed: start colour, size, rotation, lifetimes and position.</item>
/// </list>
/// </summary>
public partial class ParticleRenderProbe
{
    static readonly List<float[]> s_Rows = new List<float[]>();

    /// <summary>count, time, isPlaying, isEmitting, then per particle (by age, z, seed): age, x, y,
    /// z, seed low half.</summary>
    static void RecordPrewarm(string key, ParticleSystem ps)
    {
        if (s_Buffer.Length < ps.particleCount) s_Buffer = new ParticleSystem.Particle[ps.particleCount * 2];
        int n = ps.GetParticles(s_Buffer);
        s_Rows.Clear();
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            s_Rows.Add(new[] { p.startLifetime - p.remainingLifetime, p.position.x, p.position.y, p.position.z, p.randomSeed & 0xffff });
        }
        s_Rows.Sort((a, b) => a[0] != b[0] ? a[0].CompareTo(b[0]) : a[3] != b[3] ? a[3].CompareTo(b[3]) : a[4].CompareTo(b[4]));
        var v = new List<float> { ps.particleCount, ps.time, ps.isPlaying ? 1 : 0, ps.isEmitting ? 1 : 0 };
        foreach (var r in s_Rows) v.AddRange(r);
        Record(key, v);
    }

    /// <summary>count, then per particle (by seed): seed halves, start colour (bytes), start size,
    /// rotation, start and remaining lifetime, position.</summary>
    static void RecordInherited(string key, ParticleSystem ps)
    {
        if (s_Buffer.Length < ps.particleCount) s_Buffer = new ParticleSystem.Particle[ps.particleCount * 2];
        int n = ps.GetParticles(s_Buffer);
        s_Rows.Clear();
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            Color32 c = p.startColor;
            s_Rows.Add(new[] { p.randomSeed & 0xffff, p.randomSeed >> 16, c.r, c.g, c.b, c.a, p.startSize, p.rotation,
                p.startLifetime, p.remainingLifetime, p.position.x, p.position.y, p.position.z });
        }
        s_Rows.Sort((a, b) => a[1] != b[1] ? a[1].CompareTo(b[1]) : a[0].CompareTo(b[0]));
        var v = new List<float> { n };
        foreach (var r in s_Rows) v.AddRange(r);
        Record(key, v);
    }

    /// <summary>A looping prewarmed parent: one particle a loop at its start, 0.7 s lifetime, moving
    /// at 1 unit/s along +Z.</summary>
    static ParticleSystem PrewarmParent(string key, Vector3 at, Action<ParticleSystem> configure)
    {
        var ps = Make(key, s =>
        {
            var m = s.main; m.loop = true; m.prewarm = true; m.duration = 1f; m.startLifetime = 0.7f; m.startSpeed = 1f;
            m.maxParticles = 256; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var e = s.emission; e.enabled = true; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            configure?.Invoke(s);
        });
        ps.transform.position = at;
        return ps;
    }

    /// <summary>A world-space child at 10/s with 3 s lifetimes linked to <paramref name="parent"/>.</summary>
    static ParticleSystem LinkedChild(string key, ParticleSystem parent, Action<ParticleSystem> configure = null,
        ParticleSystemSubEmitterType type = ParticleSystemSubEmitterType.Birth,
        ParticleSystemSubEmitterProperties properties = ParticleSystemSubEmitterProperties.InheritNothing)
    {
        var child = Make(key, s =>
        {
            var m = s.main; m.startLifetime = 3f; m.startSpeed = 0f; m.maxParticles = 256;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = s.emission; e.enabled = true; e.rateOverTime = 10f;
            configure?.Invoke(s);
        });
        child.transform.SetParent(parent.transform, false);
        var sub = parent.subEmitters;
        sub.enabled = true;
        sub.AddSubEmitter(child, type, properties);
        return child;
    }

    IEnumerator PrewarmControls()
    {
        Time.captureDeltaTime = 0.05f;
        yield return null;
        var systems = new List<(string, ParticleSystem)>();
        var played = new List<ParticleSystem>();
        var origin = new Vector3(0f, 300f, 0f);
        int slot = 0;
        void Case(string name, Action<ParticleSystem> parentConfig, Action<ParticleSystem> childConfig = null, bool sub = true,
            ParticleSystemSubEmitterType type = ParticleSystemSubEmitterType.Birth, Action<ParticleSystem> grandConfig = null)
        {
            string key = "prewarm/" + name;
            var parent = PrewarmParent(key + "/parent", origin + new Vector3(3f * slot++, 0f, 0f), parentConfig);
            systems.Add((key + "/parent", parent));
            played.Add(parent);
            if (!sub) return;
            var child = LinkedChild(key + "/child", parent, childConfig, type);
            systems.Add((key + "/child", child));
            if (grandConfig == null) return;
            systems.Add((key + "/grand", LinkedChild(key + "/grand", child, grandConfig)));
        }
        // Without sub-emitters: one lifetime, ending on a loop's end.
        Case("nosub", null, sub: false);
        Case("nosub_life2.5", s => { var m = s.main; m.startLifetime = 2.5f; }, sub: false);
        Case("nosub_rate", s =>
        {
            var m = s.main; m.startLifetime = 0.5f;
            var e = s.emission; e.rateOverTime = 5f; e.SetBursts(new ParticleSystem.Burst[0]);
        }, sub: false);
        // With a Birth child: the child's lifetime is added; children within it of the end stay.
        Case("birth", null);
        Case("birth_child1", null, s => { var m = s.main; m.startLifetime = 1f; });
        Case("birth_child2", null, s => { var m = s.main; m.startLifetime = 2f; });
        Case("birth_duration0.5", s => { var m = s.main; m.duration = 0.5f; });
        Case("birth_duration2", s => { var m = s.main; m.duration = 2f; });
        Case("birth_life1.6", s => { var m = s.main; m.startLifetime = 1.6f; });
        Case("birth_rate", s =>
        {
            var m = s.main; m.startLifetime = 0.5f;
            var e = s.emission; e.rateOverTime = 5f; e.SetBursts(new ParticleSystem.Burst[0]);
        });
        Case("birth_twoconst", null, s => { var m = s.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1f, 4f); });
        Case("birth_bursts", null, s =>
        {
            var e = s.emission; e.rateOverTime = 0f;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2), new ParticleSystem.Burst(0.3f, 1) });
        });
        Case("birth_childloop", null, s => { var m = s.main; m.loop = true; });
        // A parent outliving its non-looping child's 1 s duration: no birth at the duration.
        Case("birth_childend", s => { var m = s.main; m.startLifetime = 1.5f; });
        Case("chain", null, grandConfig: s => { var e = s.emission; e.rateOverTime = 5f; });
        Case("death", null, s =>
        {
            var e = s.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
        }, type: ParticleSystemSubEmitterType.Death);
        // A child lifetime reaching more than four seconds before the prewarm's end (open).
        Case("birth_child6", null, s => { var m = s.main; m.startLifetime = 6f; });
        foreach (var ps in played) ps.Play();
        foreach (var (key, ps) in systems) RecordPrewarm(key + "/start", ps);
        for (int frame = 0; frame < 2; frame++)
        {
            yield return null;
            foreach (var (key, ps) in systems) RecordPrewarm(key + "/" + frame, ps);
        }
        foreach (var ps in played) Done(ps);
        Time.captureDeltaTime = 0f;
    }

    /// <summary>Birth children of a non-looping child that ends before its parent (rate × duration
    /// whole), and of children whose Emission module is disabled, played and simulated.</summary>
    IEnumerator BirthEndControls()
    {
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var systems = new List<(string, ParticleSystem)>();
        var played = new List<ParticleSystem>();
        var simulated = new List<ParticleSystem>();
        var origin = new Vector3(0f, 400f, 0f);
        int slot = 0;
        foreach (bool play in new[] { true, false })
        {
            void Case(string name, Action<ParticleSystem> parentConfig, Action<ParticleSystem> childConfig)
            {
                string key = "birthend/" + (play ? "play/" : "simulate/") + name;
                var parent = PrewarmParent(key + "/parent", origin + new Vector3(3f * slot++, 0f, 0f), s =>
                {
                    var m = s.main; m.prewarm = false;
                    parentConfig?.Invoke(s);
                });
                systems.Add((key + "/child", LinkedChild(key + "/child", parent, childConfig)));
                (play ? played : simulated).Add(parent);
            }
            Case("childend", s => { var m = s.main; m.startLifetime = 1.5f; }, null);
            Case("childend0.5", s => { var m = s.main; m.startLifetime = 1.5f; }, s => { var m = s.main; m.duration = 0.5f; });
            Case("disabled", null, s => { var e = s.emission; e.enabled = false; });
            // Bursts off the step grid: a burst due exactly at a step's end is a float tie.
            Case("disabled_bursts", null, s =>
            {
                var e = s.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2), new ParticleSystem.Burst(0.31f, 1) });
                e.enabled = false;
            });
        }
        foreach (var ps in played) ps.Play();
        for (int frame = 0; frame < 80; frame++)
        {
            for (int i = 0; i < simulated.Count; i++) simulated[i].Simulate(0.02f, true, frame == 0, false);
            yield return null;
            if (frame % 4 == 3) foreach (var (key, ps) in systems) RecordPrewarm(key + "/" + frame, ps);
        }
        foreach (var ps in played) Done(ps);
        foreach (var ps in simulated) Done(ps);
        Time.captureDeltaTime = 0f;
    }

    static readonly (string, ParticleSystemSubEmitterProperties)[] s_Inherited =
    {
        ("color", ParticleSystemSubEmitterProperties.InheritColor),
        ("size", ParticleSystemSubEmitterProperties.InheritSize),
        ("rotation", ParticleSystemSubEmitterProperties.InheritRotation),
        ("lifetime", ParticleSystemSubEmitterProperties.InheritLifetime),
        ("duration", ParticleSystemSubEmitterProperties.InheritDuration),
    };

    /// <summary>Three bursts of one particle, colour, size and rotation over lifetime.</summary>
    static void InheritingParent(ParticleSystem s, bool curveLifetime, float size)
    {
        var m = s.main; m.loop = false; m.duration = 1f; m.startSpeed = 2f; m.maxParticles = 64;
        m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        m.startLifetime = curveLifetime ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 0.9f)) : new ParticleSystem.MinMaxCurve(0.6f);
        m.startColor = new Color(0.5f, 1f, 0.8f, 1f);
        m.startSize = size;
        m.startRotation = 30f * Mathf.Deg2Rad;
        var col = s.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
        col.color = g;
        var sz = s.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));
        var rot = s.rotationOverLifetime; rot.enabled = true; rot.z = 90f * Mathf.Deg2Rad;
        var e = s.emission; e.enabled = true; e.rateOverTime = 0f;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.13f, 1), new ParticleSystem.Burst(0.27f, 1) });
    }

    /// <summary>A child with a start size curve over time (0.2 to 1), 1 s lifetime, Birth rate or a
    /// burst for the other types.</summary>
    static void InheritingChild(ParticleSystem s, bool birth)
    {
        var m = s.main; m.loop = false; m.duration = 5f; m.startLifetime = 1f;
        m.startSize = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.2f, 1f, 1f));
        m.startColor = new Color(1f, 0.5f, 1f, 1f);
        m.startRotation = 10f * Mathf.Deg2Rad;
        var e = s.emission;
        if (birth) e.rateOverTime = 10f;
        else { e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) }); }
    }

    IEnumerator InheritControls()
    {
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var systems = new List<(string, ParticleSystem)>();
        var played = new List<ParticleSystem>();
        var extra = new List<GameObject>();
        var origin = new Vector3(0f, 500f, 0f);
        int slot = 0;
        foreach (var type in new[] { "birth", "birthcurve", "death", "collision", "trigger" })
            foreach (var (name, properties) in s_Inherited)
            {
                string key = "inherit/" + type + "/" + name;
                var at = origin + new Vector3(12f * slot++, 0f, 0f);
                bool events = type == "collision" || type == "trigger";
                var parent = Make(key + "/parent", s =>
                {
                    // Small particles on the event paths: the collision and trigger radius follows the size.
                    InheritingParent(s, type == "birthcurve", events ? 0.2f : 2f);
                    if (type == "collision")
                    {
                        var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World;
                        c.mode = ParticleSystemCollisionMode.Collision3D; c.bounce = 0.5f; c.lifetimeLoss = 0f;
                        c.sendCollisionMessages = false;
                    }
                });
                parent.transform.position = at;
                if (events) parent.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                if (type == "collision")
                {
                    var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    plane.transform.position = at + new Vector3(0f, -0.41f, 0f);
                    extra.Add(plane);
                }
                if (type == "trigger")
                {
                    var sphere = new GameObject(key + " trigger").AddComponent<SphereCollider>();
                    sphere.isTrigger = true; sphere.radius = 0.05f;
                    sphere.transform.position = at + new Vector3(0f, -0.21f, 0f);
                    extra.Add(sphere.gameObject);
                    var t = parent.trigger; t.enabled = true; t.AddCollider(sphere);
                    t.enter = ParticleSystemOverlapAction.Callback; t.inside = ParticleSystemOverlapAction.Ignore;
                    t.exit = ParticleSystemOverlapAction.Ignore; t.outside = ParticleSystemOverlapAction.Ignore;
                }
                var subType = type == "death" ? ParticleSystemSubEmitterType.Death
                    : type == "collision" ? ParticleSystemSubEmitterType.Collision
                    : type == "trigger" ? ParticleSystemSubEmitterType.Trigger : ParticleSystemSubEmitterType.Birth;
                var child = LinkedChild(key + "/child", parent, s => InheritingChild(s, subType == ParticleSystemSubEmitterType.Birth), subType, properties);
                systems.Add((key + "/parent", parent));
                systems.Add((key + "/child", child));
                played.Add(parent);
            }
        {
            string key = "inherit/chain";
            var parent = Make(key + "/parent", s => InheritingParent(s, true, 2f));
            parent.transform.position = origin + new Vector3(12f * slot++, 0f, 0f);
            var both = ParticleSystemSubEmitterProperties.InheritLifetime | ParticleSystemSubEmitterProperties.InheritColor;
            var child = LinkedChild(key + "/child", parent, s => InheritingChild(s, true), ParticleSystemSubEmitterType.Birth, both);
            var grand = LinkedChild(key + "/grand", child, s => { InheritingChild(s, true); var e = s.emission; e.rateOverTime = 5f; },
                ParticleSystemSubEmitterType.Birth, both);
            systems.Add((key + "/parent", parent)); systems.Add((key + "/child", child)); systems.Add((key + "/grand", grand));
            played.Add(parent);
        }
        {
            string key = "inherit/loop";
            var parent = Make(key + "/parent", s =>
            {
                InheritingParent(s, true, 2f);
                var m = s.main; m.loop = true; m.duration = 0.3f;
                var e = s.emission; e.rateOverTime = 7f; e.SetBursts(new ParticleSystem.Burst[0]);
            });
            parent.transform.position = origin + new Vector3(12f * slot++, 0f, 0f);
            var child = LinkedChild(key + "/child", parent, s => InheritingChild(s, true), ParticleSystemSubEmitterType.Birth,
                ParticleSystemSubEmitterProperties.InheritLifetime | ParticleSystemSubEmitterProperties.InheritSize);
            systems.Add((key + "/parent", parent)); systems.Add((key + "/child", child));
            played.Add(parent);
        }
        foreach (var ps in played) ps.Play();
        for (int frame = 0; frame < 45; frame++)
        {
            yield return null;
            if (frame % 3 == 2 || frame >= 28 && frame <= 32)
                foreach (var (key, ps) in systems) RecordInherited(key + "/" + frame.ToString(CultureInfo.InvariantCulture), ps);
        }
        foreach (var ps in played) Done(ps);
        foreach (var go in extra) DestroyImmediate(go);
        Time.captureDeltaTime = 0f;
    }
}
