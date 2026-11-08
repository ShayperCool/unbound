using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what ParticleSystem.MinMaxCurve keeps and returns: the struct's constructors, implicit
/// conversion, setters and mode switches; the same values written to and read back from module
/// properties, with each module's <c>*Multiplier</c> shortcut; values authored in a prefab whose
/// serialized scalar differs from its curve keys; and simulations that use those values. Each record
/// is one JSON line {"key", "value": [numbers]} in <c>PARTICLE_CURVES_OUT</c>.
/// </summary>
public partial class ParticleCurveProbe : MonoBehaviour
{
    const float Thrown = -12345f;
    const float NullCurve = -1f;
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[64];
    static string s_Path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_CURVES_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        new GameObject("ParticleCurveProbe").AddComponent<ParticleCurveProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Record(string key, IList<float> values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Count; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    static void AddCurve(List<float> v, AnimationCurve c)
    {
        if (c == null) { v.Add(NullCurve); return; }
        v.Add(c.length);
        for (int i = 0; i < c.length; i++) { v.Add(c[i].time); v.Add(c[i].value); }
    }

    static float Try(Func<float> f)
    {
        try { return f(); }
        catch (Exception) { return Thrown; }
    }

    /// <summary>mode, constant, constantMin, constantMax, curveMultiplier, Evaluate at
    /// (0,0) (0.5,0.25) (1,1), Evaluate(0.5), (0.5,1.5) and (0.5,-0.5), then curveMax and curveMin as
    /// count followed by (time, value) pairs, -1 for null.</summary>
    static List<float> Describe(ParticleSystem.MinMaxCurve c)
    {
        var v = new List<float> { (float)c.mode, c.constant, c.constantMin, c.constantMax, c.curveMultiplier };
        v.Add(Try(() => c.Evaluate(0f, 0f)));
        v.Add(Try(() => c.Evaluate(0.5f, 0.25f)));
        v.Add(Try(() => c.Evaluate(1f, 1f)));
        v.Add(Try(() => c.Evaluate(0.5f)));
        v.Add(Try(() => c.Evaluate(0.5f, 1.5f)));
        v.Add(Try(() => c.Evaluate(0.5f, -0.5f)));
        AddCurve(v, c.curveMax);
        AddCurve(v, c.curveMin);
        return v;
    }

    static void Rec(string key, ParticleSystem.MinMaxCurve c) => Record(key, Describe(c));

    static AnimationCurve CurveA() => new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f));
    static AnimationCurve CurveB() => new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(1f, 2f));
    static AnimationCurve CurveC() => new AnimationCurve(new Keyframe(0f, 3f), new Keyframe(0.5f, 1f), new Keyframe(1f, 4f));

    static readonly (string, Func<ParticleSystem.MinMaxCurve>)[] Bases =
    {
        ("const", () => new ParticleSystem.MinMaxCurve(2f)),
        ("curve", () => new ParticleSystem.MinMaxCurve(3f, CurveA())),
        ("twocurves", () => new ParticleSystem.MinMaxCurve(1.5f, CurveA(), CurveB())),
        ("twoconst", () => new ParticleSystem.MinMaxCurve(1f, 4f)),
        ("implicit", () => 5f),
        ("default", () => new ParticleSystem.MinMaxCurve()),
    };

    /// <summary>Values only assigned and read back, then given a negative and a large multiplier.</summary>
    static readonly (string, Func<ParticleSystem.MinMaxCurve>)[] Extremes =
    {
        ("negconst", () => new ParticleSystem.MinMaxCurve(-2f)),
        ("negcurve", () => new ParticleSystem.MinMaxCurve(-3f, CurveA())),
        ("negtwoconst", () => new ParticleSystem.MinMaxCurve(-3f, -1f)),
        ("swappedtwoconst", () => new ParticleSystem.MinMaxCurve(4f, 1f)),
        ("bigconst", () => new ParticleSystem.MinMaxCurve(1000f)),
        ("bigtwocurves", () => new ParticleSystem.MinMaxCurve(1000f, CurveA(), CurveB())),
        ("nullcurve", () => new ParticleSystem.MinMaxCurve(3f, (AnimationCurve)null)),
        ("nullmincurve", () => new ParticleSystem.MinMaxCurve(2f, null, CurveB())),
    };

    static readonly (string, Func<ParticleSystem.MinMaxCurve, ParticleSystem.MinMaxCurve>)[] Setters =
    {
        ("none", c => c),
        ("constant", c => { c.constant = 7f; return c; }),
        ("constantMin", c => { c.constantMin = -1f; return c; }),
        ("constantMax", c => { c.constantMax = 8f; return c; }),
        ("curveMultiplier", c => { c.curveMultiplier = 9f; return c; }),
        ("curve", c => { c.curve = CurveC(); return c; }),
        ("curveMin", c => { c.curveMin = CurveB(); return c; }),
        ("curveMax", c => { c.curveMax = CurveC(); return c; }),
        ("toConst", c => { c.mode = ParticleSystemCurveMode.Constant; return c; }),
        ("toCurve", c => { c.mode = ParticleSystemCurveMode.Curve; return c; }),
        ("toTwoCurves", c => { c.mode = ParticleSystemCurveMode.TwoCurves; return c; }),
        ("toTwoConst", c => { c.mode = ParticleSystemCurveMode.TwoConstants; return c; }),
    };

    /// <summary>A module property: getter, setter and its multiplier shortcut.</summary>
    struct Prop
    {
        public string name;
        public Action<ParticleSystem> enable;
        public Func<ParticleSystem, ParticleSystem.MinMaxCurve> get;
        public Action<ParticleSystem, ParticleSystem.MinMaxCurve> set;
        public Func<ParticleSystem, float> getMul;
        public Action<ParticleSystem, float> setMul;
    }

    static Prop P(string name, Func<ParticleSystem, ParticleSystem.MinMaxCurve> get, Action<ParticleSystem, ParticleSystem.MinMaxCurve> set,
        Func<ParticleSystem, float> getMul, Action<ParticleSystem, float> setMul, Action<ParticleSystem> enable = null) =>
        new Prop { name = name, get = get, set = set, getMul = getMul, setMul = setMul, enable = enable };

    static readonly Prop[] Props =
    {
        P("main.startSize", s => s.main.startSize, (s, c) => { var m = s.main; m.startSize = c; }, s => s.main.startSizeMultiplier, (s, x) => { var m = s.main; m.startSizeMultiplier = x; }),
        P("main.startSpeed", s => s.main.startSpeed, (s, c) => { var m = s.main; m.startSpeed = c; }, s => s.main.startSpeedMultiplier, (s, x) => { var m = s.main; m.startSpeedMultiplier = x; }),
        P("main.startLifetime", s => s.main.startLifetime, (s, c) => { var m = s.main; m.startLifetime = c; }, s => s.main.startLifetimeMultiplier, (s, x) => { var m = s.main; m.startLifetimeMultiplier = x; }),
        P("main.startDelay", s => s.main.startDelay, (s, c) => { var m = s.main; m.startDelay = c; }, s => s.main.startDelayMultiplier, (s, x) => { var m = s.main; m.startDelayMultiplier = x; }),
        P("main.startRotation", s => s.main.startRotation, (s, c) => { var m = s.main; m.startRotation = c; }, s => s.main.startRotationMultiplier, (s, x) => { var m = s.main; m.startRotationMultiplier = x; }),
        P("main.gravityModifier", s => s.main.gravityModifier, (s, c) => { var m = s.main; m.gravityModifier = c; }, s => s.main.gravityModifierMultiplier, (s, x) => { var m = s.main; m.gravityModifierMultiplier = x; }),
        P("emission.rateOverTime", s => s.emission.rateOverTime, (s, c) => { var m = s.emission; m.rateOverTime = c; }, s => s.emission.rateOverTimeMultiplier, (s, x) => { var m = s.emission; m.rateOverTimeMultiplier = x; }),
        P("emission.rateOverDistance", s => s.emission.rateOverDistance, (s, c) => { var m = s.emission; m.rateOverDistance = c; }, s => s.emission.rateOverDistanceMultiplier, (s, x) => { var m = s.emission; m.rateOverDistanceMultiplier = x; }),
        P("sizeOverLifetime.size", s => s.sizeOverLifetime.size, (s, c) => { var m = s.sizeOverLifetime; m.size = c; }, s => s.sizeOverLifetime.sizeMultiplier, (s, x) => { var m = s.sizeOverLifetime; m.sizeMultiplier = x; }),
        P("velocityOverLifetime.x", s => s.velocityOverLifetime.x, (s, c) => { var m = s.velocityOverLifetime; m.x = c; }, s => s.velocityOverLifetime.xMultiplier, (s, x) => { var m = s.velocityOverLifetime; m.xMultiplier = x; }),
        P("velocityOverLifetime.speedModifier", s => s.velocityOverLifetime.speedModifier, (s, c) => { var m = s.velocityOverLifetime; m.speedModifier = c; }, s => s.velocityOverLifetime.speedModifierMultiplier, (s, x) => { var m = s.velocityOverLifetime; m.speedModifierMultiplier = x; }),
        P("rotationOverLifetime.z", s => s.rotationOverLifetime.z, (s, c) => { var m = s.rotationOverLifetime; m.z = c; }, s => s.rotationOverLifetime.zMultiplier, (s, x) => { var m = s.rotationOverLifetime; m.zMultiplier = x; }),
        P("forceOverLifetime.y", s => s.forceOverLifetime.y, (s, c) => { var m = s.forceOverLifetime; m.y = c; }, s => s.forceOverLifetime.yMultiplier, (s, x) => { var m = s.forceOverLifetime; m.yMultiplier = x; }),
        P("limitVelocity.limit", s => s.limitVelocityOverLifetime.limit, (s, c) => { var m = s.limitVelocityOverLifetime; m.limit = c; }, s => s.limitVelocityOverLifetime.limitMultiplier, (s, x) => { var m = s.limitVelocityOverLifetime; m.limitMultiplier = x; }),
        P("limitVelocity.drag", s => s.limitVelocityOverLifetime.drag, (s, c) => { var m = s.limitVelocityOverLifetime; m.drag = c; }, s => s.limitVelocityOverLifetime.dragMultiplier, (s, x) => { var m = s.limitVelocityOverLifetime; m.dragMultiplier = x; }),
        P("inheritVelocity.curve", s => s.inheritVelocity.curve, (s, c) => { var m = s.inheritVelocity; m.curve = c; }, s => s.inheritVelocity.curveMultiplier, (s, x) => { var m = s.inheritVelocity; m.curveMultiplier = x; }),
        P("noise.strength", s => s.noise.strength, (s, c) => { var m = s.noise; m.strength = c; }, s => s.noise.strengthMultiplier, (s, x) => { var m = s.noise; m.strengthMultiplier = x; }),
        P("noise.scrollSpeed", s => s.noise.scrollSpeed, (s, c) => { var m = s.noise; m.scrollSpeed = c; }, s => s.noise.scrollSpeedMultiplier, (s, x) => { var m = s.noise; m.scrollSpeedMultiplier = x; }),
        P("textureSheet.frameOverTime", s => s.textureSheetAnimation.frameOverTime, (s, c) => { var m = s.textureSheetAnimation; m.frameOverTime = c; }, s => s.textureSheetAnimation.frameOverTimeMultiplier, (s, x) => { var m = s.textureSheetAnimation; m.frameOverTimeMultiplier = x; }),
        P("textureSheet.startFrame", s => s.textureSheetAnimation.startFrame, (s, c) => { var m = s.textureSheetAnimation; m.startFrame = c; }, s => s.textureSheetAnimation.startFrameMultiplier, (s, x) => { var m = s.textureSheetAnimation; m.startFrameMultiplier = x; }),
        P("lights.intensity", s => s.lights.intensity, (s, c) => { var m = s.lights; m.intensity = c; }, s => s.lights.intensityMultiplier, (s, x) => { var m = s.lights; m.intensityMultiplier = x; }),
        P("trails.widthOverTrail", s => s.trails.widthOverTrail, (s, c) => { var m = s.trails; m.widthOverTrail = c; }, s => s.trails.widthOverTrailMultiplier, (s, x) => { var m = s.trails; m.widthOverTrailMultiplier = x; }),
        P("trails.lifetime", s => s.trails.lifetime, (s, c) => { var m = s.trails; m.lifetime = c; }, s => s.trails.lifetimeMultiplier, (s, x) => { var m = s.trails; m.lifetimeMultiplier = x; }),
        P("collision.bounce", s => s.collision.bounce, (s, c) => { var m = s.collision; m.bounce = c; }, s => s.collision.bounceMultiplier, (s, x) => { var m = s.collision; m.bounceMultiplier = x; }),
        P("shape.radiusSpeed", s => s.shape.radiusSpeed, (s, c) => { var m = s.shape; m.radiusSpeed = c; }, s => s.shape.radiusSpeedMultiplier, (s, x) => { var m = s.shape; m.radiusSpeedMultiplier = x; }),
        P("sizeBySpeed.size", s => s.sizeBySpeed.size, (s, c) => { var m = s.sizeBySpeed; m.size = c; }, s => s.sizeBySpeed.sizeMultiplier, (s, x) => { var m = s.sizeBySpeed; m.sizeMultiplier = x; }),
        P("lifetimeByEmitterSpeed.curve", s => s.lifetimeByEmitterSpeed.curve, (s, c) => { var m = s.lifetimeByEmitterSpeed; m.curve = c; }, s => s.lifetimeByEmitterSpeed.curveMultiplier, (s, x) => { var m = s.lifetimeByEmitterSpeed; m.curveMultiplier = x; }),
        P("externalForces.multiplierCurve", s => s.externalForces.multiplierCurve, (s, c) => { var m = s.externalForces; m.multiplierCurve = c; }, s => s.externalForces.multiplier, (s, x) => { var m = s.externalForces; m.multiplier = x; }),
    };

    /// <summary>Further properties: default, each base read back, and the extremes.</summary>
    static readonly Prop[] ExtraProps =
    {
        P("main.startSizeY", s => s.main.startSizeY, (s, c) => { var m = s.main; m.startSizeY = c; }, s => s.main.startSizeYMultiplier, (s, x) => { var m = s.main; m.startSizeYMultiplier = x; }),
        P("main.startSizeZ", s => s.main.startSizeZ, (s, c) => { var m = s.main; m.startSizeZ = c; }, s => s.main.startSizeZMultiplier, (s, x) => { var m = s.main; m.startSizeZMultiplier = x; }),
        P("limitVelocityOverLifetime.limitZ", s => s.limitVelocityOverLifetime.limitZ, (s, c) => { var m = s.limitVelocityOverLifetime; m.limitZ = c; }, s => s.limitVelocityOverLifetime.limitZMultiplier, (s, x) => { var m = s.limitVelocityOverLifetime; m.limitZMultiplier = x; }),
        P("main.startRotationX", s => s.main.startRotationX, (s, c) => { var m = s.main; m.startRotationX = c; }, s => s.main.startRotationXMultiplier, (s, x) => { var m = s.main; m.startRotationXMultiplier = x; }),
        P("shape.meshSpawnSpeed", s => s.shape.meshSpawnSpeed, (s, c) => { var m = s.shape; m.meshSpawnSpeed = c; }, s => s.shape.meshSpawnSpeedMultiplier, (s, x) => { var m = s.shape; m.meshSpawnSpeedMultiplier = x; }),
        P("shape.arcSpeed", s => s.shape.arcSpeed, (s, c) => { var m = s.shape; m.arcSpeed = c; }, s => s.shape.arcSpeedMultiplier, (s, x) => { var m = s.shape; m.arcSpeedMultiplier = x; }),
        P("velocityOverLifetime.orbitalX", s => s.velocityOverLifetime.orbitalX, (s, c) => { var m = s.velocityOverLifetime; m.orbitalX = c; }, s => s.velocityOverLifetime.orbitalXMultiplier, (s, x) => { var m = s.velocityOverLifetime; m.orbitalXMultiplier = x; }),
        P("velocityOverLifetime.orbitalOffsetY", s => s.velocityOverLifetime.orbitalOffsetY, (s, c) => { var m = s.velocityOverLifetime; m.orbitalOffsetY = c; }, s => s.velocityOverLifetime.orbitalOffsetYMultiplier, (s, x) => { var m = s.velocityOverLifetime; m.orbitalOffsetYMultiplier = x; }),
        P("velocityOverLifetime.radial", s => s.velocityOverLifetime.radial, (s, c) => { var m = s.velocityOverLifetime; m.radial = c; }, s => s.velocityOverLifetime.radialMultiplier, (s, x) => { var m = s.velocityOverLifetime; m.radialMultiplier = x; }),
        P("limitVelocityOverLifetime.limitX", s => s.limitVelocityOverLifetime.limitX, (s, c) => { var m = s.limitVelocityOverLifetime; m.limitX = c; }, s => s.limitVelocityOverLifetime.limitXMultiplier, (s, x) => { var m = s.limitVelocityOverLifetime; m.limitXMultiplier = x; }),
        P("sizeOverLifetime.y", s => s.sizeOverLifetime.y, (s, c) => { var m = s.sizeOverLifetime; m.y = c; }, s => s.sizeOverLifetime.yMultiplier, (s, x) => { var m = s.sizeOverLifetime; m.yMultiplier = x; }),
        P("sizeBySpeed.z", s => s.sizeBySpeed.z, (s, c) => { var m = s.sizeBySpeed; m.z = c; }, s => s.sizeBySpeed.zMultiplier, (s, x) => { var m = s.sizeBySpeed; m.zMultiplier = x; }),
        P("rotationOverLifetime.x", s => s.rotationOverLifetime.x, (s, c) => { var m = s.rotationOverLifetime; m.x = c; }, s => s.rotationOverLifetime.xMultiplier, (s, x) => { var m = s.rotationOverLifetime; m.xMultiplier = x; }),
        P("rotationBySpeed.y", s => s.rotationBySpeed.y, (s, c) => { var m = s.rotationBySpeed; m.y = c; }, s => s.rotationBySpeed.yMultiplier, (s, x) => { var m = s.rotationBySpeed; m.yMultiplier = x; }),
        P("noise.strengthY", s => s.noise.strengthY, (s, c) => { var m = s.noise; m.strengthY = c; }, s => s.noise.strengthYMultiplier, (s, x) => { var m = s.noise; m.strengthYMultiplier = x; }),
        P("noise.remap", s => s.noise.remap, (s, c) => { var m = s.noise; m.remap = c; }, s => s.noise.remapMultiplier, (s, x) => { var m = s.noise; m.remapMultiplier = x; }),
        P("noise.positionAmount", s => s.noise.positionAmount, (s, c) => { var m = s.noise; m.positionAmount = c; }, null, null),
        P("noise.rotationAmount", s => s.noise.rotationAmount, (s, c) => { var m = s.noise; m.rotationAmount = c; }, null, null),
        P("noise.sizeAmount", s => s.noise.sizeAmount, (s, c) => { var m = s.noise; m.sizeAmount = c; }, null, null),
        P("collision.dampen", s => s.collision.dampen, (s, c) => { var m = s.collision; m.dampen = c; }, s => s.collision.dampenMultiplier, (s, x) => { var m = s.collision; m.dampenMultiplier = x; }),
        P("collision.lifetimeLoss", s => s.collision.lifetimeLoss, (s, c) => { var m = s.collision; m.lifetimeLoss = c; }, s => s.collision.lifetimeLossMultiplier, (s, x) => { var m = s.collision; m.lifetimeLossMultiplier = x; }),
        P("lights.range", s => s.lights.range, (s, c) => { var m = s.lights; m.range = c; }, s => s.lights.rangeMultiplier, (s, x) => { var m = s.lights; m.rangeMultiplier = x; }),
        P("forceOverLifetime.z", s => s.forceOverLifetime.z, (s, c) => { var m = s.forceOverLifetime; m.z = c; }, s => s.forceOverLifetime.zMultiplier, (s, x) => { var m = s.forceOverLifetime; m.zMultiplier = x; }),
        P("customData.vector0", s => s.customData.GetVector(ParticleSystemCustomData.Custom1, 0), (s, c) => { var m = s.customData; m.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector); m.SetVector(ParticleSystemCustomData.Custom1, 0, c); }, null, null),
        P("burst.count", s => { var b = new ParticleSystem.Burst[1]; return s.emission.GetBursts(b) > 0 ? b[0].count : new ParticleSystem.MinMaxCurve(-777f); }, (s, c) => { var m = s.emission; m.SetBursts(new[] { new ParticleSystem.Burst(0f, c) }); }, null, null),
        F("forceField.directionX", f => f.directionX, (f, c) => f.directionX = c),
        F("forceField.gravity", f => f.gravity, (f, c) => f.gravity = c),
        F("forceField.rotationSpeed", f => f.rotationSpeed, (f, c) => f.rotationSpeed = c),
        F("forceField.rotationAttraction", f => f.rotationAttraction, (f, c) => f.rotationAttraction = c),
        F("forceField.drag", f => f.drag, (f, c) => f.drag = c),
        F("forceField.vectorFieldSpeed", f => f.vectorFieldSpeed, (f, c) => f.vectorFieldSpeed = c),
        F("forceField.vectorFieldAttraction", f => f.vectorFieldAttraction, (f, c) => f.vectorFieldAttraction = c),
    };

    static Prop F(string name, Func<ParticleSystemForceField, ParticleSystem.MinMaxCurve> get, Action<ParticleSystemForceField, ParticleSystem.MinMaxCurve> set) =>
        new Prop { name = name, get = s => get(Field(s)), set = (s, c) => set(Field(s), c) };

    static ParticleSystemForceField Field(ParticleSystem s)
    {
        var f = s.GetComponent<ParticleSystemForceField>();
        return f != null ? f : s.gameObject.AddComponent<ParticleSystemForceField>();
    }

    static void Mul(List<float> v, Prop p, ParticleSystem ps) { if (p.getMul != null) v.Add(p.getMul(ps)); }

    static void Extras()
    {
        foreach (var p in ExtraProps)
        {
            var ps = Make(p.name);
            var d = new List<float>();
            Mul(d, p, ps);
            d.AddRange(Describe(p.get(ps)));
            Record("extra/" + p.name + "/default", d);
            foreach (var (bname, make) in Bases)
            {
                if (bname == "default") continue;
                p.set(ps, make());
                var v = new List<float>();
                Mul(v, p, ps);
                v.AddRange(Describe(p.get(ps)));
                Record("extra/" + p.name + "/" + bname, v);
            }
            foreach (var (bname, make) in Extremes)
            {
                p.set(ps, make());
                var v = new List<float>();
                Mul(v, p, ps);
                v.AddRange(Describe(p.get(ps)));
                if (p.setMul != null)
                {
                    p.setMul(ps, -6f); Mul(v, p, ps); v.AddRange(Describe(p.get(ps)));
                    p.set(ps, make()); p.setMul(ps, 1000f); Mul(v, p, ps); v.AddRange(Describe(p.get(ps)));
                }
                Record("extra/" + p.name + "/" + bname, v);
            }
            Destroy(ps.gameObject);
        }
        // A Burst holds its count as given until it is set on a system.
        var burst = new ParticleSystem.Burst(0f, new ParticleSystem.MinMaxCurve(3f, CurveA()));
        Rec("extra/burst.struct/curve", burst.count);
        burst.count = new ParticleSystem.MinMaxCurve(-2f);
        Rec("extra/burst.struct/negconst", burst.count);
    }

    static ParticleSystem Make(string name)
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
        main.maxParticles = 64;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        go.SetActive(true);
        return ps;
    }

    /// <summary>The struct alone: each constructor, then each setter or mode switch.</summary>
    static void Structs()
    {
        foreach (var (bname, make) in Bases)
            foreach (var (sname, apply) in Setters)
                Rec("struct/" + bname + "/" + sname, apply(make()));
        // Chains: a constant set, then a switch to a curve mode, and the reverse.
        var c = new ParticleSystem.MinMaxCurve(3f, CurveA());
        c.constant = 7f; c.mode = ParticleSystemCurveMode.Constant;
        Rec("struct/chain/curve-constant-toConst", c);
        c.mode = ParticleSystemCurveMode.Curve;
        Rec("struct/chain/curve-constant-toConst-toCurve", c);
        c = new ParticleSystem.MinMaxCurve(2f);
        c.curveMultiplier = 6f; c.curve = CurveB(); c.mode = ParticleSystemCurveMode.Curve;
        Rec("struct/chain/const-mul-curve-toCurve", c);
    }

    /// <summary>Every module property: the default read, each base assigned and read back, the
    /// multiplier shortcut's read, its write, then a read-modify-assign of a setter.</summary>
    static void Modules()
    {
        foreach (var p in Props)
        {
            var ps = Make(p.name);
            var d = new List<float> { p.getMul(ps) };
            d.AddRange(Describe(p.get(ps)));
            Record("module/" + p.name + "/default", d);
            foreach (var (bname, make) in Bases)
            {
                if (bname == "default") continue;
                foreach (var (sname, apply) in Setters)
                {
                    string key = "module/" + p.name + "/" + bname + "/" + sname;
                    p.set(ps, make());
                    var after = p.get(ps);
                    var v = new List<float> { p.getMul(ps) };
                    v.AddRange(Describe(after));
                    // Read-modify-assign the setter through the module.
                    p.set(ps, apply(after));
                    v.Add(p.getMul(ps));
                    v.AddRange(Describe(p.get(ps)));
                    Record(key, v);
                }
                // The multiplier shortcut written after the assignment.
                p.set(ps, make());
                p.setMul(ps, 6f);
                var m = new List<float> { p.getMul(ps) };
                m.AddRange(Describe(p.get(ps)));
                Record("module/" + p.name + "/" + bname + "/setMultiplier", m);
            }
            foreach (var (bname, make) in Extremes)
            {
                p.set(ps, make());
                var v = new List<float> { p.getMul(ps) };
                v.AddRange(Describe(p.get(ps)));
                p.setMul(ps, -6f);
                v.Add(p.getMul(ps));
                v.AddRange(Describe(p.get(ps)));
                p.set(ps, make());
                p.setMul(ps, 1000f);
                v.Add(p.getMul(ps));
                v.AddRange(Describe(p.get(ps)));
                Record("module/" + p.name + "/" + bname, v);
            }
            // Does a curve survive a pass through Constant mode? Curve, then a constant, then
            // the read constant switched back to Curve.
            p.set(ps, new ParticleSystem.MinMaxCurve(3f, CurveC()));
            p.set(ps, 2f);
            var k = p.get(ps);
            k.mode = ParticleSystemCurveMode.Curve;
            p.set(ps, k);
            Rec("module/" + p.name + "/keepcurve", p.get(ps));
            Destroy(ps.gameObject);
        }
    }

    static void Fields(string key, CurveFields f)
    {
        Rec(key + "/constant", f.constant);
        Rec(key + "/curve", f.curve);
        Rec(key + "/twoCurves", f.twoCurves);
        Rec(key + "/twoConstants", f.twoConstants);
        Rec(key + "/initialized", f.initialized);
    }

    /// <summary>Script fields: authored in the prefab, copied by Instantiate, and on a new component.</summary>
    static void ScriptFields()
    {
        var prefab = Resources.Load<GameObject>("Authored");
        if (prefab == null) return;
        var go = Instantiate(prefab);
        var fields = go.GetComponent<CurveFields>();
        Fields("fields/authored", fields);
        var clone = Instantiate(go);
        Fields("fields/clone", clone.GetComponent<CurveFields>());
        var fresh = new GameObject("fresh").AddComponent<CurveFields>();
        Fields("fields/fresh", fresh);
        // A runtime value set on a field and copied by Instantiate.
        var c = new ParticleSystem.MinMaxCurve(5f, CurveA());
        c.constant = 11f; c.constantMin = 3f;
        fresh.curve = c;
        Fields("fields/freshclone", Instantiate(fresh.gameObject).GetComponent<CurveFields>());
        Destroy(go); Destroy(clone);
    }

    /// <summary>Values authored in Resources/Authored.prefab: one system per case, its scalar
    /// differing from the curve keys (see ProbeRunner.Create).</summary>
    static void Authored()
    {
        var prefab = Resources.Load<GameObject>("Authored");
        if (prefab == null) { Record("authored/missing", new List<float> { 1f }); return; }
        var go = Instantiate(prefab);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            string n = ps.gameObject.name;
            foreach (var p in Props)
            {
                if (p.name != "main.startSize" && p.name != "main.startSpeed" && p.name != "main.startLifetime" &&
                    p.name != "main.gravityModifier" && p.name != "emission.rateOverTime" && p.name != "sizeOverLifetime.size" &&
                    p.name != "collision.bounce" && p.name != "textureSheet.frameOverTime") continue;
                var v = new List<float> { p.getMul(ps) };
                v.AddRange(Describe(p.get(ps)));
                Record("authored/" + n + "/" + p.name, v);
            }
            ps.Simulate(0.25f, false, true, true);
            RecordParticles("authored/" + n + "/simulate", ps);
            // Switch each authored value's mode at runtime; the new particles show which scalar it keeps.
            var main = ps.main;
            var size = main.startSize;
            size.mode = size.mode == ParticleSystemCurveMode.Constant ? ParticleSystemCurveMode.Curve : ParticleSystemCurveMode.Constant;
            main.startSize = size;
            var sol = ps.sizeOverLifetime;
            var sz = sol.size;
            sz.mode = sz.mode == ParticleSystemCurveMode.Constant ? ParticleSystemCurveMode.Curve : ParticleSystemCurveMode.Constant;
            sol.size = sz;
            ps.Simulate(0.25f, false, true, true);
            RecordParticles("authored/" + n + "/switched", ps);
            Rec("authored/" + n + "/switched/main.startSize", main.startSize);
            Rec("authored/" + n + "/switched/sizeOverLifetime.size", sol.size);
        }
        Destroy(go);
    }

    /// <summary>The count, then for every particle: start size, current size, start lifetime,
    /// remaining lifetime, speed (velocity z) and rotation z (degrees).</summary>
    static void RecordParticles(string key, ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { n };
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            v.Add(p.startSize); v.Add(p.GetCurrentSize(ps)); v.Add(p.startLifetime); v.Add(p.remainingLifetime);
            v.Add(p.velocity.z); v.Add(p.rotation);
        }
        Record(key, v);
    }

    /// <summary>Simulations where the constant and the curve multiplier differ: start size in
    /// curve mode with a constant set, size over lifetime in constant mode with a multiplier set,
    /// then each switched at runtime; and the multiplier shortcut on a constant start speed.</summary>
    static void Simulations()
    {
        for (int variant = 0; variant < 4; variant++)
        {
            string key = "sim/" + variant;
            var ps = Make(key);
            var main = ps.main;
            main.startLifetime = 4f;
            main.startSpeed = 1f;
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)2), new ParticleSystem.Burst(0.5f, (short)2) });
            var size = new ParticleSystem.MinMaxCurve(2f, CurveB());
            size.constant = 0.75f;
            main.startSize = size;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var sc = new ParticleSystem.MinMaxCurve(1.5f);
            sc.curveMultiplier = 3f;
            sc.curve = CurveA();
            sol.size = sc;
            if (variant == 1) main.startSpeedMultiplier = 2.5f;
            if (variant == 2) main.startSizeMultiplier = 1.25f;
            if (variant == 3) sol.sizeMultiplier = 0.5f;
            ps.Simulate(0.25f, false, true, true);
            RecordParticles(key + "/a", ps);
            // Switch both at runtime: start size to Constant, size over lifetime to Curve.
            var s2 = main.startSize; s2.mode = ParticleSystemCurveMode.Constant; main.startSize = s2;
            var s3 = sol.size; s3.mode = ParticleSystemCurveMode.Curve; sol.size = s3;
            ps.Simulate(0.5f, false, false, true);
            RecordParticles(key + "/b", ps);
            // And back.
            var s4 = main.startSize; s4.mode = ParticleSystemCurveMode.Curve; main.startSize = s4;
            var s5 = sol.size; s5.mode = ParticleSystemCurveMode.Constant; sol.size = s5;
            emission.SetBursts(new[] { new ParticleSystem.Burst(1.0f, (short)2) });
            ps.Simulate(0.5f, false, false, true);
            RecordParticles(key + "/c", ps);
            Rec(key + "/main.startSize", main.startSize);
            Rec(key + "/sizeOverLifetime.size", sol.size);
            Destroy(ps.gameObject);
        }
    }

    void Start() => StartCoroutine(Probe());

    IEnumerator Probe()
    {
        yield return null;
        Structs();
        Modules();
        Extras();
        Authored();
        ScriptFields();
        Simulations();
        Gradients();
        Record("complete", new List<float> { 1f });
        File.WriteAllText(s_Path, s_Out.ToString());
        Debug.Log("particle curves complete: " + s_Path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
