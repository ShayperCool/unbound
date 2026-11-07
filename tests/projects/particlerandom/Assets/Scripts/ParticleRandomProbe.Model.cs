using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class ParticleRandomProbe
{
    // Model controls: one rule for inherited Birth lifetime across Simulate and Play and
    // across lifetime modes; the Simulate catch-up bound with inheritance; the start-lifetime clock with and without sub-emitters; force
    // and speed randomness under different death orders; sub-emitter draws with automatic
    // seeding; rate batches with very short lifetimes; what the startLifetime setter keeps.
    static IEnumerator ModelControls()
    {
        float maximum = Time.maximumParticleDeltaTime, fixedDelta = Time.fixedDeltaTime;
        Time.maximumParticleDeltaTime = 0.03f;
        Time.fixedDeltaTime = 0.02f;
        foreach (string childMode in new[] { "const", "two" })
        foreach (bool inherit in new[] { false, true })
            InheritControl("const", childMode, inherit, null);
        foreach (bool inherit in new[] { false, true })
            CatchupBoundControl(inherit);
        LifetimeClockControls();
        DeathOrderControls();
        AutoSeedControls();
        UnusedLaneControls();
        LifetimeSetterControls();
        Time.maximumParticleDeltaTime = maximum;
        Time.fixedDeltaTime = fixedDelta;

        // The same inheritance systems played over frames.
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var played = new List<(string, ParticleSystem, ParticleSystem)>();
        foreach (string parentMode in new[] { "const", "two", "curve" })
        foreach (string childMode in new[] { "const", "two" })
        foreach (bool inherit in new[] { false, true })
            played.Add(InheritControl(parentMode, childMode, inherit, "play"));
        var clocks = new List<(string, ParticleSystem)>();
        foreach (var (name, setup) in LifetimeClockSystems())
        {
            var ps = setup();
            var m = ps.main; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            clocks.Add(("model/clock/play/" + name, ps));
        }
        var deaths = new List<(string, ParticleSystem)>();
        foreach (string variant in new[] { "force", "speed" })
        foreach (uint seed in Seeds)
        {
            var ps = DeathOrderSystem("model/death/play/" + variant + "/" + seed, seed, variant);
            var m = ps.main; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            deaths.Add(("model/death/play/" + variant + "/" + seed, ps));
        }
        var inherited = new List<SortedDictionary<uint, float>>();
        foreach (var _ in played) inherited.Add(new SortedDictionary<uint, float>());
        foreach (var (_, parent, _) in played) parent.Play(true);
        foreach (var (_, ps) in clocks) ps.Play(true);
        foreach (var (_, ps) in deaths) ps.Play(true);
        for (int frame = 0; frame < 30; frame++)
        {
            yield return null;
            for (int i = 0; i < played.Count; i++)
                CollectStartLifetimes(played[i].Item3, inherited[i]);
            foreach (var (key, ps) in clocks) RecordParticles(key + "/" + frame, ps);
            foreach (var (key, ps) in deaths) RecordParticles(key + "/" + frame, ps);
        }
        for (int i = 0; i < played.Count; i++)
        {
            // Rows of (seed low half, seed high half, start lifetime), ordered by seed.
            var row = new List<float> { inherited[i].Count };
            foreach (var child in inherited[i])
            {
                row.Add(child.Key & 0xffff); row.Add(child.Key >> 16); row.Add(child.Value);
            }
            Record(played[i].Item1 + "/lifetimes", row);
        }
        foreach (var (_, parent, _) in played) DestroyImmediate(parent.gameObject);
        foreach (var (_, ps) in clocks) DestroyImmediate(ps.gameObject);
        foreach (var (_, ps) in deaths) DestroyImmediate(ps.gameObject);
        Time.captureDeltaTime = 0f;
    }

    // Played Birth children are compared by their start lifetime alone, keyed by seed: the
    // inheritance rule, without the played Birth origins and rate boundaries.
    static void CollectStartLifetimes(ParticleSystem ps, SortedDictionary<uint, float> lifetimes)
    {
        var particles = new ParticleSystem.Particle[ps.particleCount];
        int n = ps.GetParticles(particles);
        for (int i = 0; i < n; i++)
            if (!lifetimes.ContainsKey(particles[i].randomSeed))
                lifetimes.Add(particles[i].randomSeed, particles[i].startLifetime);
    }

    static ParticleSystem.MinMaxCurve ModelLifetime(string mode, float constant, float min, float max) =>
        mode == "const" ? new ParticleSystem.MinMaxCurve(constant)
        : mode == "two" ? Two(min, max)
        : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, min, 1f, max));

    // A parent with three cohorts emits a Birth child with rate and bursts. With `play`
    // the systems are returned unplayed; otherwise twenty Simulate calls are recorded
    // (constant parent lifetimes only: with other parent lifetimes the Simulate Birth
    // rate boundaries are a separate, unresolved question).
    static (string, ParticleSystem, ParticleSystem) InheritControl(string parentMode, string childMode, bool inherit, string play)
    {
        string key = "model/inherit/" + (play ?? "simulate") + "/" + parentMode + "/" + childMode + "/"
            + (inherit ? "lifetime" : "nothing");
        var parent = Make(key, 12345, ps =>
        {
            var m = ps.main; m.duration = 1f; m.loop = false;
            m.startLifetime = ModelLifetime(parentMode, 0.613f, 0.413f, 0.913f);
            m.startSpeed = 2f;
            if (play != null) m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = 0f;
            var e = ps.emission;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1),
                new ParticleSystem.Burst(0.13f, (short)1), new ParticleSystem.Burst(0.27f, (short)1) });
        }, 1);
        parent.gameObject.SetActive(false);
        var childObject = new GameObject("child");
        childObject.transform.SetParent(parent.transform, false);
        var child = childObject.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.useAutoRandomSeed = false; child.randomSeed = 12346;
        var cm = child.main; cm.playOnAwake = false;
        cm.startLifetime = ModelLifetime(childMode, 0.813f, 0.413f, 1.213f);
        cm.startSpeed = 0.5f; cm.maxParticles = 256; cm.loop = false; cm.duration = 5f;
        if (play != null) cm.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var shape = child.shape; shape.enabled = false;
        var cr = child.rotationOverLifetime; cr.enabled = true; cr.z = 0f;
        var ce = child.emission; ce.rateOverTime = 10f;
        ce.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1), new ParticleSystem.Burst(0.17f, (short)1) });
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, inherit
            ? ParticleSystemSubEmitterProperties.InheritLifetime : ParticleSystemSubEmitterProperties.InheritNothing);
        parent.gameObject.SetActive(true);
        if (play != null) return (key, parent, child);
        for (int i = 0; i < 20; i++)
        {
            parent.Simulate(0.05f, true, i == 0, false);
            RecordParticles(key + "/parent/" + i, parent);
            RecordParticles(key + "/child/" + i, child);
        }
        DestroyImmediate(parent.gameObject);
        return (key, null, null);
    }

    // A long-lived parent gives inherited children more lifetime than their own start
    // lifetime. One long call separates a catch-up bound taken before inheritance from
    // one taken after it.
    static void CatchupBoundControl(bool inherit)
    {
        string key = "model/catchup/const/" + (inherit ? "lifetime" : "nothing");
        var parent = Make(key, 12345, ps =>
        {
            var m = ps.main; m.duration = 5f; m.loop = false; m.startLifetime = 3f; m.startSpeed = 1f;
            var rotation = ps.rotationOverLifetime; rotation.enabled = true; rotation.z = 0f;
        }, 1);
        parent.gameObject.SetActive(false);
        var childObject = new GameObject("child");
        childObject.transform.SetParent(parent.transform, false);
        var child = childObject.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.useAutoRandomSeed = false; child.randomSeed = 12346;
        var cm = child.main; cm.playOnAwake = false;
        cm.startLifetime = 0.413f;
        cm.startSpeed = 0.5f; cm.maxParticles = 256; cm.loop = false; cm.duration = 5f;
        var shape = child.shape; shape.enabled = false;
        var cr = child.rotationOverLifetime; cr.enabled = true; cr.z = 0f;
        var ce = child.emission; ce.rateOverTime = 10f;
        ce.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, inherit
            ? ParticleSystemSubEmitterProperties.InheritLifetime : ParticleSystemSubEmitterProperties.InheritNothing);
        parent.gameObject.SetActive(true);
        parent.Simulate(1f, true, true, false);
        RecordParticles(key + "/parent/0", parent);
        RecordParticles(key + "/child/0", child);
        for (int i = 1; i < 4; i++)
        {
            parent.Simulate(0.05f, true, false, false);
            RecordParticles(key + "/parent/" + i, parent);
            RecordParticles(key + "/child/" + i, child);
        }
        DestroyImmediate(parent.gameObject);
    }

    // Start lifetimes whose exported value and clock show the reciprocal arithmetic,
    // without sub-emitters, with an unrelated Death sub-emitter and with a Birth one.
    static IEnumerable<(string, Func<ParticleSystem>)> LifetimeClockSystems()
    {
        foreach (float life in new[] { 0.613f, 0.413f, 1.013f, 0.37f, 2.9f })
        foreach (string variant in new[] { "plain", "death", "birth" })
        {
            float l = life; string v = variant;
            yield return (F(life) + "/" + variant, () => LifetimeClockSystem("model/clock", new ParticleSystem.MinMaxCurve(l), v));
        }
        yield return ("two/plain", () => LifetimeClockSystem("model/clock", Two(0.3f, 0.9f), "plain"));
        yield return ("two/birth", () => LifetimeClockSystem("model/clock", Two(0.3f, 0.9f), "birth"));
        yield return ("curve/plain", () => LifetimeClockSystem("model/clock",
            new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 0.9f)), "plain"));
    }

    static ParticleSystem LifetimeClockSystem(string name, ParticleSystem.MinMaxCurve lifetime, string variant)
    {
        var ps = Make(name, 4242, s =>
        {
            var m = s.main; m.startLifetime = lifetime; m.startSpeed = 1f;
            var e = s.emission; e.rateOverTime = 40f;
            var r = s.rotationOverLifetime; r.enabled = true; r.z = 0f;
        }, 4);
        if (variant == "plain") return ps;
        ps.gameObject.SetActive(false);
        var childObject = new GameObject("child");
        childObject.transform.SetParent(ps.transform, false);
        var child = childObject.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.useAutoRandomSeed = false; child.randomSeed = 77;
        var cm = child.main; cm.playOnAwake = false; cm.startLifetime = 1f; cm.maxParticles = 16;
        cm.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        var shape = child.shape; shape.enabled = false;
        var ce = child.emission; ce.rateOverTime = 0f; ce.SetBursts(new ParticleSystem.Burst[0]);
        var sub = ps.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, variant == "death" ? ParticleSystemSubEmitterType.Death : ParticleSystemSubEmitterType.Birth,
            ParticleSystemSubEmitterProperties.InheritNothing);
        ps.gameObject.SetActive(true);
        return ps;
    }

    static void LifetimeClockControls()
    {
        foreach (var (name, setup) in LifetimeClockSystems())
        {
            var ps = setup();
            for (int i = 0; i < 12; i++)
            {
                ps.Simulate(0.025f, true, i == 0, false);
                RecordParticles("model/clock/simulate/" + name + "/" + i, ps);
            }
            DestroyImmediate(ps.gameObject);
        }
    }

    // Randomized force and speed modifier with lifetimes that end in different
    // buffer slots, so deaths compact the buffer in different orders.
    static ParticleSystem DeathOrderSystem(string name, uint seed, string variant)
    {
        return Make(name, seed, s =>
        {
            var m = s.main; m.startLifetime = Two(0.08f, 0.9f); m.startSpeed = Two(1f, 3f);
            var r = s.rotationOverLifetime; r.enabled = true; r.z = 0f;
            var e = s.emission; e.rateOverTime = 30f;
            if (variant == "force")
            {
                var f = s.forceOverLifetime; f.enabled = true; f.randomized = true;
                f.x = Two(-5f, 5f); f.y = Two(-5f, 5f); f.z = Two(-5f, 5f);
            }
            if (variant == "speed")
            {
                var v = s.velocityOverLifetime; v.enabled = true; v.speedModifier = Two(0.5f, 2f);
            }
        }, 23);
    }

    static void DeathOrderControls()
    {
        foreach (string variant in new[] { "force", "speed" })
        foreach (uint seed in Seeds)
        {
            string key = "model/death/simulate/" + variant + "/" + seed;
            var ps = DeathOrderSystem(key, seed, variant);
            for (int i = 0; i < 20; i++)
            {
                ps.Simulate(0.05f, true, i == 0, false);
                RecordParticles(key + "/" + i, ps);
            }
            DestroyImmediate(ps.gameObject);
        }
    }

    // Death sub-emitter burst counts from parent particles with assigned seeds. Automatic
    // seeding changes the system seed for every playback; the records keep only whether
    // all counts agree with a twin, which does not depend on the drawn seed. The fixed
    // twin uses the serialized seed; the getter twin uses the seed the getter reports
    // during the automatic playback.
    static void AutoSeedControls()
    {
        foreach (string variant in new[] { "fixed", "auto", "getter" })
        {
            int agree = 0, total = 0, reported = 0;
            var counts = new List<float>();
            for (int trial = 0; trial < 8; trial++)
            {
                uint trialSeed = 7919u * (uint)trial + 13u;
                bool auto = variant != "fixed";
                int first = AutoSeedBurst(auto, 12345u, trialSeed, out uint getter);
                if (getter == 12345u) reported++;
                int twin = AutoSeedBurst(false, variant == "getter" ? getter : 12345u, trialSeed, out _);
                if (variant == "fixed") { counts.Add(first); counts.Add(twin); }
                total++;
                if (first == twin) agree++;
            }
            counts.Insert(0, agree == total ? 1f : 0f);
            counts.Insert(1, reported == total ? 1f : reported == 0 ? 0f : 0.5f);
            Record("model/autoseed/" + variant, counts);
        }
    }

    static int AutoSeedBurst(bool auto, uint seed, uint particleSeed, out uint getter)
    {
        var parent = Make("model/autoseed", seed, ps =>
        {
            var m = ps.main; m.startLifetime = 1f; m.startSpeed = 0f;
        }, 0);
        parent.useAutoRandomSeed = auto;
        parent.gameObject.SetActive(false);
        var childObject = new GameObject("child");
        childObject.transform.SetParent(parent.transform, false);
        var child = childObject.AddComponent<ParticleSystem>();
        child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        child.useAutoRandomSeed = false; child.randomSeed = 99;
        var cm = child.main; cm.playOnAwake = false; cm.startLifetime = 10f; cm.maxParticles = 1000;
        var shape = child.shape; shape.enabled = false;
        var ce = child.emission; ce.rateOverTime = 0f;
        ce.SetBursts(new[] { new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(0f, 40f)) });
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
        parent.gameObject.SetActive(true);
        parent.Simulate(0.01f, true, true, false);
        getter = parent.randomSeed;
        var p = new ParticleSystem.Particle
        {
            position = Vector3.zero, velocity = Vector3.zero, startSize = 1f, startColor = Color.white,
            startLifetime = 1f, remainingLifetime = 0.005f, randomSeed = particleSeed,
        };
        parent.SetParticles(new[] { p }, 1);
        parent.Simulate(0.02f, true, false, false);
        int count = child.particleCount;
        DestroyImmediate(parent.gameObject);
        return count;
    }

    // What the startLifetime setter keeps of nonpositive values, per curve mode.
    static void LifetimeSetterControls()
    {
        var ps = Make("model/lifetimesetter", 12345, null, 0);
        var cases = new (string, ParticleSystem.MinMaxCurve)[]
        {
            ("const/0", new ParticleSystem.MinMaxCurve(0f)),
            ("const/-1", new ParticleSystem.MinMaxCurve(-1f)),
            ("const/0.00005", new ParticleSystem.MinMaxCurve(0.00005f)),
            ("two/-0.5/1.5", Two(-0.5f, 1.5f)),
            ("two/-2/-1", Two(-2f, -1f)),
            ("two/1.5/-0.5", Two(1.5f, -0.5f)),
            ("curve/-1", new ParticleSystem.MinMaxCurve(-1f, AnimationCurve.Linear(0f, -1f, 1f, 1f))),
        };
        foreach (var (name, curve) in cases)
        {
            var m = ps.main; m.startLifetime = curve;
            // The kept values: both constants, or a curve's multiplier.
            var read = ps.main.startLifetime;
            Record("model/lifetimesetter/" + name, read.mode == ParticleSystemCurveMode.Curve
                ? new[] { (float)read.mode, read.curveMultiplier, ps.main.startLifetimeMultiplier }
                : new[] { (float)read.mode, read.constantMin, read.constantMax, ps.main.startLifetimeMultiplier });
        }
        DestroyImmediate(ps.gameObject);
    }

    // A rate batch's unused final lanes take start values too. The range below zero is
    // raised by the setter (see LifetimeSetterControls), so every lane starts with some
    // lifetime and the shortest die within the batch.
    static void UnusedLaneControls()
    {
        foreach (uint seed in Seeds)
        foreach (float rate in new[] { 150f, 250f })
        {
            string key = "model/lanes/" + F(rate) + "/" + seed;
            var ps = Make(key, seed, s =>
            {
                var m = s.main; m.startLifetime = Two(-0.5f, 1.5f); m.startSpeed = 1f;
                var e = s.emission; e.rateOverTime = rate;
                var r = s.rotationOverLifetime; r.enabled = true; r.z = 0f;
            }, 0);
            var lifetime = ps.main.startLifetime;
            Record(key + "/getter", new[] { lifetime.constantMin, lifetime.constantMax });
            for (int i = 0; i < 10; i++)
            {
                ps.Simulate(0.02f, true, i == 0, false);
                RecordParticles(key + "/" + i, ps);
            }
            DestroyImmediate(ps.gameObject);
        }
    }
}
