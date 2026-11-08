using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ParticleSystem.MinMaxGradient: the struct's constructors, setters and Evaluate; module colour
/// properties written and read back; script fields (Resources/Gradients.prefab, Instantiate, a new
/// component); particle start colours and current colours for every gradient mode, so the bytes show
/// how a gradient is evaluated and how start colour and colour over lifetime combine; and the
/// deprecated ParticleSystem.start* setters on values in other modes.
/// </summary>
public partial class ParticleCurveProbe
{
    static readonly ParticleSystem.Particle[] s_Colours = new ParticleSystem.Particle[1024];

    static void AddColor(List<float> v, Color c) { v.Add(c.r); v.Add(c.g); v.Add(c.b); v.Add(c.a); }

    static void AddGradient(List<float> v, Gradient g)
    {
        if (g == null) { v.Add(NullCurve); return; }
        v.Add(g.colorKeys.Length);
        foreach (var k in g.colorKeys) { v.Add(k.time); AddColor(v, k.color); }
        v.Add(g.alphaKeys.Length);
        foreach (var k in g.alphaKeys) { v.Add(k.time); v.Add(k.alpha); }
        v.Add((int)g.mode);
    }

    static void TryColor(List<float> v, Func<Color> f)
    {
        try { AddColor(v, f()); }
        catch (Exception) { v.Add(Thrown); v.Add(Thrown); v.Add(Thrown); v.Add(Thrown); }
    }

    const float Unused = -777f;

    static bool UsesColors(ParticleSystemGradientMode m) => m == ParticleSystemGradientMode.Color || m == ParticleSystemGradientMode.TwoColors;

    /// <summary>mode, color, colorMin, colorMax, Evaluate at (0) (0.35) (0.35,0.25) (1,1) (0.5,1.5)
    /// (0.5,-0.5) (-0.5) (1.5), then gradientMax and gradientMin (keys, -1 for null). A value read
    /// from a module (<paramref name="moduleRead"/>) holds only what its mode uses (the rest is not
    /// initialized, measured): the other parts are written as -777.</summary>
    static List<float> DescribeGradient(ParticleSystem.MinMaxGradient g, bool moduleRead = false)
    {
        var m = g.mode;
        var v = new List<float> { (float)m };
        bool colorMax = !moduleRead || UsesColors(m);
        bool colorMin = !moduleRead || m == ParticleSystemGradientMode.TwoColors;
        bool gradientMax = !moduleRead || !UsesColors(m);
        bool gradientMin = !moduleRead || m == ParticleSystemGradientMode.TwoGradients;
        if (colorMax) AddColor(v, g.color); else AddColor(v, new Color(Unused, Unused, Unused, Unused));
        if (colorMin) AddColor(v, g.colorMin); else AddColor(v, new Color(Unused, Unused, Unused, Unused));
        if (colorMax) AddColor(v, g.colorMax); else AddColor(v, new Color(Unused, Unused, Unused, Unused));
        TryColor(v, () => g.Evaluate(0f));
        TryColor(v, () => g.Evaluate(0.35f));
        TryColor(v, () => g.Evaluate(0.35f, 0.25f));
        TryColor(v, () => g.Evaluate(1f, 1f));
        TryColor(v, () => g.Evaluate(0.5f, 1.5f));
        TryColor(v, () => g.Evaluate(0.5f, -0.5f));
        TryColor(v, () => g.Evaluate(-0.5f));
        TryColor(v, () => g.Evaluate(1.5f));
        if (gradientMax) AddGradient(v, g.gradientMax); else v.Add(Unused);
        if (gradientMin) AddGradient(v, g.gradientMin); else v.Add(Unused);
        return v;
    }

    static void RecG(string key, ParticleSystem.MinMaxGradient g) => Record(key, DescribeGradient(g));

    static void RecM(string key, ParticleSystem.MinMaxGradient g) => Record(key, DescribeGradient(g, true));

    /// <summary>Colour keys whose bytes are not round, a middle key, alpha keys at other times.</summary>
    static Gradient GradA(GradientMode mode = GradientMode.Blend)
    {
        var g = new Gradient { mode = mode };
        g.SetKeys(new[] { new GradientColorKey(new Color(0.1f, 0.9f, 0.33f), 0f), new GradientColorKey(new Color(0.77f, 0.21f, 0.5f), 0.3f),
                new GradientColorKey(new Color(0.05f, 0.6f, 1f), 0.71f), new GradientColorKey(new Color(1f, 0.13f, 0f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.2f, 0.4f), new GradientAlphaKey(0.9f, 1f) });
        return g;
    }

    /// <summary>Keys inside (0, 1): constant before the first and after the last.</summary>
    static Gradient GradB()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(0.95f, 0.45f, 0.05f), 0.2f), new GradientColorKey(new Color(0.15f, 0.35f, 0.85f), 0.85f) },
            new[] { new GradientAlphaKey(0.3f, 0.15f), new GradientAlphaKey(0.85f, 0.6f) });
        return g;
    }

    static readonly (string, Func<ParticleSystem.MinMaxGradient>)[] GradientBases =
    {
        ("color", () => new ParticleSystem.MinMaxGradient(new Color(0.2f, 0.4f, 0.6f, 0.8f))),
        ("twocolors", () => new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.2f, 0.3f, 1f), new Color(0.7f, 0.6f, 0.5f, 0.4f))),
        ("gradient", () => new ParticleSystem.MinMaxGradient(GradA())),
        ("twogradients", () => new ParticleSystem.MinMaxGradient(GradB(), GradA())),
        ("randomcolor", () => new ParticleSystem.MinMaxGradient(GradA()) { mode = ParticleSystemGradientMode.RandomColor }),
        ("implicitcolor", () => new Color(0.5f, 0.25f, 1f, 0.75f)),
        ("implicitgradient", () => GradB()),
        ("default", () => new ParticleSystem.MinMaxGradient()),
        ("nullgradient", () => new ParticleSystem.MinMaxGradient((Gradient)null)),
        ("hdr", () => new ParticleSystem.MinMaxGradient(new Color(2f, -0.5f, 0.5f, 1.5f))),
    };

    static readonly (string, Func<ParticleSystem.MinMaxGradient, ParticleSystem.MinMaxGradient>)[] GradientSetters =
    {
        ("none", g => g),
        ("color", g => { g.color = new Color(0.9f, 0.8f, 0.1f, 0.6f); return g; }),
        ("colorMin", g => { g.colorMin = new Color(0.3f, 0.1f, 0.9f, 0.2f); return g; }),
        ("gradient", g => { g.gradient = GradB(); return g; }),
        ("gradientMin", g => { g.gradientMin = GradB(); return g; }),
        ("toColor", g => { g.mode = ParticleSystemGradientMode.Color; return g; }),
        ("toGradient", g => { g.mode = ParticleSystemGradientMode.Gradient; return g; }),
        ("toTwoColors", g => { g.mode = ParticleSystemGradientMode.TwoColors; return g; }),
        ("toTwoGradients", g => { g.mode = ParticleSystemGradientMode.TwoGradients; return g; }),
        ("toRandomColor", g => { g.mode = ParticleSystemGradientMode.RandomColor; return g; }),
    };

    struct GradientProp
    {
        public string name;
        public Func<ParticleSystem, ParticleSystem.MinMaxGradient> get;
        public Action<ParticleSystem, ParticleSystem.MinMaxGradient> set;
    }

    static GradientProp GP(string name, Func<ParticleSystem, ParticleSystem.MinMaxGradient> get, Action<ParticleSystem, ParticleSystem.MinMaxGradient> set) =>
        new GradientProp { name = name, get = get, set = set };

    static readonly GradientProp[] GradientProps =
    {
        GP("main.startColor", s => s.main.startColor, (s, c) => { var m = s.main; m.startColor = c; }),
        GP("colorOverLifetime.color", s => s.colorOverLifetime.color, (s, c) => { var m = s.colorOverLifetime; m.color = c; }),
        GP("colorBySpeed.color", s => s.colorBySpeed.color, (s, c) => { var m = s.colorBySpeed; m.color = c; }),
        GP("trails.colorOverLifetime", s => s.trails.colorOverLifetime, (s, c) => { var m = s.trails; m.colorOverLifetime = c; }),
        GP("trails.colorOverTrail", s => s.trails.colorOverTrail, (s, c) => { var m = s.trails; m.colorOverTrail = c; }),
        GP("customData.color", s => s.customData.GetColor(ParticleSystemCustomData.Custom1),
            (s, c) => { var m = s.customData; m.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Color); m.SetColor(ParticleSystemCustomData.Custom1, c); }),
    };

    static void GradientStructs()
    {
        foreach (var (bname, make) in GradientBases)
            foreach (var (sname, apply) in GradientSetters)
                RecG("gradient/struct/" + bname + "/" + sname, apply(make()));
        // Gradient modes of the keys: Fixed and PerceptualBlend through Evaluate.
        RecG("gradient/struct/fixed", new ParticleSystem.MinMaxGradient(GradA(GradientMode.Fixed)));
        RecG("gradient/struct/perceptual", new ParticleSystem.MinMaxGradient(GradA(GradientMode.PerceptualBlend)));
    }

    static void GradientModules()
    {
        foreach (var p in GradientProps)
        {
            var ps = Make(p.name);
            RecM("gradient/module/" + p.name + "/initial", p.get(ps));
            foreach (var (bname, make) in GradientBases)
            {
                p.set(ps, make());
                var v = DescribeGradient(p.get(ps), true);
                // The gradient read back is a copy: changing it leaves the module's value.
                var read = p.get(ps);
                if (read.gradientMax != null)
                    read.gradientMax.SetKeys(new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.cyan, 1f) },
                        new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0.25f, 1f) });
                read.colorMax = Color.red;
                v.AddRange(DescribeGradient(p.get(ps), true));
                Record("gradient/module/" + p.name + "/" + bname, v);
            }
            // A gradient changed after it was assigned: does the module keep its own copy?
            var g = GradA();
            p.set(ps, new ParticleSystem.MinMaxGradient(g));
            g.SetKeys(new[] { new GradientColorKey(Color.blue, 0f) }, new[] { new GradientAlphaKey(1f, 0f) });
            RecM("gradient/module/" + p.name + "/aliased", p.get(ps));
            Destroy(ps.gameObject);
        }
    }

    static void GradientFieldValues(string key, GradientFields f)
    {
        RecG(key + "/color", f.color);
        RecG(key + "/twoColors", f.twoColors);
        RecG(key + "/gradient", f.gradient);
        RecG(key + "/twoGradients", f.twoGradients);
        RecG(key + "/randomColor", f.randomColor);
        RecG(key + "/initialized", f.initialized);
    }

    /// <summary>Script fields: authored in the prefab, copied by Instantiate, on a new component, a
    /// runtime value copied by Instantiate; the prefab's system as authored.</summary>
    static void GradientScriptFields()
    {
        var prefab = Resources.Load<GameObject>("Gradients");
        if (prefab == null) { Record("gradient/fields/missing", new List<float> { 1f }); return; }
        var go = Instantiate(prefab);
        GradientFieldValues("gradient/fields/authored", go.GetComponent<GradientFields>());
        var clone = Instantiate(go);
        GradientFieldValues("gradient/fields/clone", clone.GetComponent<GradientFields>());
        var fresh = new GameObject("fresh").AddComponent<GradientFields>();
        GradientFieldValues("gradient/fields/fresh", fresh);
        var g = new ParticleSystem.MinMaxGradient(GradA(), GradB());
        g.colorMin = new Color(0.4f, 0.3f, 0.2f, 0.1f);
        fresh.twoGradients = g;
        GradientFieldValues("gradient/fields/freshclone", Instantiate(fresh.gameObject).GetComponent<GradientFields>());
        var ps = go.GetComponentInChildren<ParticleSystem>();
        RecM("gradient/authored/main.startColor", ps.main.startColor);
        RecM("gradient/authored/colorOverLifetime.color", ps.colorOverLifetime.color);
        Destroy(go); Destroy(clone);
    }

    /// <summary>The count, then per particle: start colour and current colour (bytes), age and
    /// start lifetime.</summary>
    static void RecordColours(string key, ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Colours);
        var v = new List<float> { n };
        for (int i = 0; i < n; i++)
        {
            var p = s_Colours[i];
            Color32 s = p.startColor, c = p.GetCurrentColor(ps);
            v.Add(s.r); v.Add(s.g); v.Add(s.b); v.Add(s.a);
            v.Add(c.r); v.Add(c.g); v.Add(c.b); v.Add(c.a);
            v.Add(p.startLifetime - p.remainingLifetime); v.Add(p.startLifetime);
        }
        Record(key, v);
    }

    static ParticleSystem ColourSystem(string name, int burst, float rate)
    {
        var ps = Make(name);
        var main = ps.main;
        main.loop = false;
        main.maxParticles = 1000;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 4f);
        var emission = ps.emission;
        emission.rateOverTime = rate;
        emission.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst) } : new ParticleSystem.Burst[0]);
        return ps;
    }

    static readonly (string, Func<ParticleSystem.MinMaxGradient>)[] ColourModes =
    {
        ("gradient", () => new ParticleSystem.MinMaxGradient(GradA())),
        ("fixed", () => new ParticleSystem.MinMaxGradient(GradA(GradientMode.Fixed))),
        ("perceptual", () => new ParticleSystem.MinMaxGradient(GradA(GradientMode.PerceptualBlend))),
        ("inner", () => new ParticleSystem.MinMaxGradient(GradB())),
        ("twogradients", () => new ParticleSystem.MinMaxGradient(GradB(), GradA())),
        ("twocolors", () => new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.2f, 0.3f, 0.4f), new Color(0.9f, 0.7f, 0.55f, 1f))),
        ("randomcolor", () => new ParticleSystem.MinMaxGradient(GradA()) { mode = ParticleSystemGradientMode.RandomColor }),
        ("color", () => new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.6f, 0.9f, 0.5f))),
        ("hdr", () => new ParticleSystem.MinMaxGradient(new Color(1.5f, -0.25f, 0.5f, 2f))),
    };

    /// <summary>Start colour in each mode (rate over a second: every normalized time), colour over
    /// lifetime in each mode (a burst of random lifetimes, recorded at several ages), both together,
    /// and colour by speed.</summary>
    static void GradientParticles()
    {
        foreach (var (name, make) in ColourModes)
        {
            var ps = ColourSystem("start/" + name, 40, 200f);
            var main = ps.main;
            main.startColor = make();
            ps.Simulate(1f, false, true, true);
            RecordColours("gradient/particles/start/" + name, ps);
            Destroy(ps.gameObject);
        }
        foreach (var (name, make) in ColourModes)
        {
            var ps = ColourSystem("life/" + name, 150, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = make();
            ps.Simulate(0.6f, false, true, true);
            RecordColours("gradient/particles/life/" + name + "/0", ps);
            ps.Simulate(0.35f, false, false, true);
            RecordColours("gradient/particles/life/" + name + "/1", ps);
            Destroy(ps.gameObject);
        }
        // Start colour × colour over lifetime × colour by speed.
        foreach (var (sname, smake) in new[] { ColourModes[0], ColourModes[5], ColourModes[7] })
            foreach (var (lname, lmake) in new[] { ColourModes[0], ColourModes[4], ColourModes[7] })
            {
                var ps = ColourSystem("both", 60, 60f);
                var main = ps.main;
                main.startColor = smake();
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = lmake();
                ps.Simulate(0.8f, false, true, true);
                RecordColours($"gradient/particles/both/{sname}/{lname}", ps);
                Destroy(ps.gameObject);
            }
        var speed = ColourSystem("speed", 80, 0f);
        var m = speed.main;
        m.startSpeed = new ParticleSystem.MinMaxCurve(0f, 4f);
        m.startColor = new Color(0.8f, 0.6f, 0.4f, 0.9f);
        var cbs = speed.colorBySpeed;
        cbs.enabled = true;
        cbs.color = new ParticleSystem.MinMaxGradient(GradA());
        cbs.range = new Vector2(0.5f, 3.5f);
        var life = speed.colorOverLifetime;
        life.enabled = true;
        life.color = new ParticleSystem.MinMaxGradient(GradB());
        speed.Simulate(0.5f, false, true, true);
        RecordColours("gradient/particles/speed", speed);
        Destroy(speed.gameObject);
    }

    /// <summary>The deprecated ParticleSystem.start* properties: the getter on a value in each other
    /// mode, then the setter, then the module value read back.</summary>
    static void DeprecatedSetters()
    {
#pragma warning disable 618
        var curves = new (string, Func<ParticleSystem, ParticleSystem.MinMaxCurve>, Action<ParticleSystem, ParticleSystem.MinMaxCurve>, Func<ParticleSystem, float>, Action<ParticleSystem, float>)[]
        {
            ("startSpeed", s => s.main.startSpeed, (s, c) => { var m = s.main; m.startSpeed = c; }, s => s.startSpeed, (s, x) => s.startSpeed = x),
            ("startSize", s => s.main.startSize, (s, c) => { var m = s.main; m.startSize = c; }, s => s.startSize, (s, x) => s.startSize = x),
            ("startLifetime", s => s.main.startLifetime, (s, c) => { var m = s.main; m.startLifetime = c; }, s => s.startLifetime, (s, x) => s.startLifetime = x),
            ("startRotation", s => s.main.startRotation, (s, c) => { var m = s.main; m.startRotation = c; }, s => s.startRotation, (s, x) => s.startRotation = x),
            ("startDelay", s => s.main.startDelay, (s, c) => { var m = s.main; m.startDelay = c; }, s => s.startDelay, (s, x) => s.startDelay = x),
            ("gravityModifier", s => s.main.gravityModifier, (s, c) => { var m = s.main; m.gravityModifier = c; }, s => s.gravityModifier, (s, x) => s.gravityModifier = x),
            ("emissionRate", s => s.emission.rateOverTime, (s, c) => { var m = s.emission; m.rateOverTime = c; }, s => s.emissionRate, (s, x) => s.emissionRate = x),
        };
        foreach (var (name, get, set, oldGet, oldSet) in curves)
            foreach (var (bname, make) in Bases)
            {
                if (bname == "default" || bname == "implicit") continue;
                var ps = Make("deprecated");
                set(ps, make());
                var v = new List<float> { oldGet(ps) };
                oldSet(ps, 2.5f);
                v.Add(oldGet(ps));
                v.AddRange(Describe(get(ps)));
                oldSet(ps, -1.5f);
                v.Add(oldGet(ps));
                v.AddRange(Describe(get(ps)));
                Record("deprecated/" + name + "/" + bname, v);
                Destroy(ps.gameObject);
            }
        // startRotation3D: three axes, separate and not.
        foreach (bool separate in new[] { false, true })
        {
            var ps = Make("deprecated");
            var main = ps.main;
            main.startRotation3D = separate;
            main.startRotationX = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(3f, CurveA());
            main.startRotationZ = new ParticleSystem.MinMaxCurve(1.5f, CurveA(), CurveB());
            var v = new List<float>();
            var r = ps.startRotation3D; v.Add(r.x); v.Add(r.y); v.Add(r.z);
            ps.startRotation3D = new Vector3(0.5f, -2f, 7f);
            r = ps.startRotation3D; v.Add(r.x); v.Add(r.y); v.Add(r.z);
            v.Add(main.startRotation3D ? 1 : 0);
            v.AddRange(Describe(main.startRotationX));
            v.AddRange(Describe(main.startRotationY));
            v.AddRange(Describe(main.startRotationZ));
            Record("deprecated/startRotation3D/" + (separate ? "separate" : "single"), v);
            Destroy(ps.gameObject);
        }
        // startColor on each gradient mode.
        foreach (var (bname, make) in GradientBases)
        {
            var ps = Make("deprecated");
            var main = ps.main;
            main.startColor = make();
            var v = new List<float>();
            // The getter reads the module's colour: only defined in the colour modes.
            if (UsesColors(main.startColor.mode)) AddColor(v, ps.startColor); else v.Add(Unused);
            ps.startColor = new Color(0.6f, 0.7f, 0.2f, 0.3f);
            AddColor(v, ps.startColor);
            v.AddRange(DescribeGradient(main.startColor, true));
            Record("deprecated/startColor/" + bname, v);
            Destroy(ps.gameObject);
        }
        // startSize on a system with separate axes.
        var sep = Make("deprecated");
        var sm = sep.main;
        sm.startSize3D = true;
        sm.startSizeX = new ParticleSystem.MinMaxCurve(1f, 3f);
        sm.startSizeY = 4f;
        sm.startSizeZ = new ParticleSystem.MinMaxCurve(2f, CurveA());
        var sv = new List<float> { sep.startSize };
        sep.startSize = 0.75f;
        sv.Add(sep.startSize);
        sv.Add(sm.startSize3D ? 1 : 0);
        sv.AddRange(Describe(sm.startSizeX));
        sv.AddRange(Describe(sm.startSizeY));
        sv.AddRange(Describe(sm.startSizeZ));
        Record("deprecated/startSize/separate", sv);
        Destroy(sep.gameObject);
#pragma warning restore 618
    }

    static void Gradients()
    {
        GradientStructs();
        GradientModules();
        GradientScriptFields();
        GradientParticles();
        DeprecatedSetters();
    }
}
