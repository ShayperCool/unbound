using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records the random values a seeded particle system gives its particles: main-module start
/// values in Random Between Two Constants mode, shape positions and directions, every module's
/// random modes (two constants, two curves, two colors, two gradients, random color), burst
/// probability and count ranges, sub-emitter probability, custom data and the mesh index, under
/// several fixed seeds, through Simulate and through Play. Each record is one JSON line
/// {"key", "value": [numbers]} in <c>PARTICLE_RANDOM_OUT</c>.
/// </summary>
public class ParticleRandomProbe : MonoBehaviour
{
    const float Dt = 0.05f;
    static readonly uint[] Seeds = { 12345u, 987654321u, 4242u };
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[256];
    static readonly List<Vector4> s_Custom = new List<Vector4>();
    static string s_Path;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_RANDOM_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        new GameObject("ParticleRandomProbe").AddComponent<ParticleRandomProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Record(string key, IList<float> values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Count; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    /// <summary>A seed as its low and high 16 bits: both exact as floats.</summary>
    static void Add(List<float> v, uint seed) { v.Add(seed & 0xffffu); v.Add(seed >> 16); }
    static void Add(List<float> v, Vector3 a) { v.Add(a.x); v.Add(a.y); v.Add(a.z); }
    static void Add(List<float> v, Color32 c) { v.Add(c.r); v.Add(c.g); v.Add(c.b); v.Add(c.a); }

    /// <summary>The count, then for every particle (33 values): seed (two halves), position, velocity,
    /// animated velocity, 3D rotation (degrees), 3D angular velocity, start size, current size,
    /// start color, current color, start lifetime, remaining lifetime.</summary>
    static void RecordParticles(string key, ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { n };
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            Add(v, p.randomSeed);
            Add(v, p.position); Add(v, p.velocity); Add(v, p.animatedVelocity);
            Add(v, p.rotation3D); Add(v, p.angularVelocity3D);
            Add(v, p.startSize3D); Add(v, p.GetCurrentSize3D(ps));
            Add(v, p.startColor); Add(v, p.GetCurrentColor(ps));
            v.Add(p.startLifetime); v.Add(p.remainingLifetime);
        }
        Record(key, v);
    }

    static void RecordCustom(string key, ParticleSystem ps, ParticleSystemCustomData stream)
    {
        int n = ps.GetCustomParticleData(s_Custom, stream);
        var v = new List<float> { n };
        for (int i = 0; i < n; i++) { v.Add(s_Custom[i].x); v.Add(s_Custom[i].y); v.Add(s_Custom[i].z); v.Add(s_Custom[i].w); }
        Record(key, v);
    }

    static string Key(string scenario, uint seed, int step) =>
        scenario + "/" + seed.ToString(CultureInfo.InvariantCulture) + "/" + step.ToString(CultureInfo.InvariantCulture);

    /// <summary>A stopped, cleared system on an inactive object at the origin (local simulation
    /// space) with a fixed seed, no shape, no gravity and one burst of <paramref name="burst"/>
    /// particles at time 0 moving along +Z at 2 units/s; <paramref name="configure"/> sets the rest.</summary>
    static ParticleSystem Make(string name, uint seed, Action<ParticleSystem> configure, int burst = 6)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = seed;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 1f;
        main.loop = true;
        main.startLifetime = 10f;
        main.startSpeed = 2f;
        main.startSize = 1f;
        main.gravityModifier = 0f;
        main.maxParticles = 256;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst) } : new ParticleSystem.Burst[0]);
        configure?.Invoke(ps);
        go.SetActive(true);
        return ps;
    }

    /// <summary>For each seed: <paramref name="steps"/> Simulate steps (the first restarting),
    /// the particles recorded after each, and the custom data streams when asked.</summary>
    static void Simulated(string scenario, Action<ParticleSystem> configure, int steps = 2, int burst = 6, bool custom = false)
    {
        foreach (uint seed in Seeds)
        {
            var ps = Make(scenario, seed, configure, burst);
            for (int i = 0; i < steps; i++)
            {
                ps.Simulate(Dt, true, i == 0, false);
                RecordParticles(Key(scenario, seed, i), ps);
                if (custom)
                {
                    RecordCustom(Key(scenario, seed, i) + "/custom1", ps, ParticleSystemCustomData.Custom1);
                    RecordCustom(Key(scenario, seed, i) + "/custom2", ps, ParticleSystemCustomData.Custom2);
                }
            }
            DestroyImmediate(ps.gameObject);
        }
    }

    /// <summary>For each seed: the particle count after each of <paramref name="steps"/> steps.</summary>
    static void Counted(string scenario, Action<ParticleSystem> configure, int steps, float dt)
    {
        foreach (uint seed in Seeds)
        {
            var ps = Make(scenario, seed, configure, 0);
            var counts = new List<float>();
            for (int i = 0; i < steps; i++)
            {
                ps.Simulate(dt, true, i == 0, false);
                counts.Add(ps.particleCount);
            }
            Record(scenario + "/" + seed.ToString(CultureInfo.InvariantCulture), counts);
            DestroyImmediate(ps.gameObject);
        }
    }

    static ParticleSystem.MinMaxCurve Two(float a, float b) => new ParticleSystem.MinMaxCurve(a, b);

    static readonly AnimationCurve Rise = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
    static readonly AnimationCurve Fall = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, -0.5f));

    static Gradient Ramp(Color a, Color b)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
            new[] { new GradientAlphaKey(a.a, 0f), new GradientAlphaKey(b.a, 1f) });
        return g;
    }

    static void Shape(ParticleSystem ps, ParticleSystemShapeType type, Action<ParticleSystem.ShapeModule> more = null)
    {
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = type;
        more?.Invoke(shape);
    }

    static Mesh Quad(float size)
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(size, 0, 0), new Vector3(0, size, 0) };
        mesh.triangles = new[] { 0, 1, 2 };
        return mesh;
    }

    static void Scenarios()
    {
        // Main module, Random Between Two Constants.
        Simulated("start/two", ps =>
        {
            var m = ps.main;
            m.startLifetime = Two(1f, 3f); m.startSpeed = Two(1f, 4f); m.startSize = Two(0.5f, 2f);
            m.startRotation = Two(0f, 3f); m.flipRotation = 0.5f;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0f, 0.2f, 0.2f), new Color(0f, 1f, 1f, 1f));
        });
        Simulated("start/3d", ps =>
        {
            var m = ps.main;
            m.startSize3D = true; m.startSizeX = Two(0.5f, 2f); m.startSizeY = Two(1f, 3f); m.startSizeZ = Two(0f, 1f);
            m.startRotation3D = true; m.startRotationX = Two(0f, 1f); m.startRotationY = Two(1f, 2f); m.startRotationZ = Two(2f, 3f);
            m.startSpeed = Two(1f, 4f);
        });
        Simulated("start/gradients", ps =>
        {
            var m = ps.main;
            m.startColor = new ParticleSystem.MinMaxGradient(Ramp(Color.black, Color.white), Ramp(new Color(1f, 0f, 0f, 0.5f), new Color(0f, 0f, 1f, 1f)));
        });
        Simulated("start/randomcolor", ps =>
        {
            var g = Ramp(new Color(1f, 0f, 0f, 0f), new Color(0f, 1f, 0f, 1f));
            var m = ps.main;
            m.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        });
        Simulated("start/rate", ps =>
        {
            var m = ps.main;
            m.startLifetime = Two(1f, 3f); m.startSpeed = Two(1f, 4f); m.startSize = Two(0.5f, 2f);
            var e = ps.emission; e.rateOverTime = 50f;
            Shape(ps, ParticleSystemShapeType.Sphere);
        }, 3, 0);

        // Shapes.
        Simulated("shape/sphere", ps => Shape(ps, ParticleSystemShapeType.Sphere, s => s.radiusThickness = 0.5f));
        Simulated("shape/hemisphere", ps => Shape(ps, ParticleSystemShapeType.Hemisphere));
        Simulated("shape/cone", ps => Shape(ps, ParticleSystemShapeType.Cone, s => { s.angle = 25f; s.radius = 1f; }));
        Simulated("shape/conevolume", ps => Shape(ps, ParticleSystemShapeType.ConeVolume, s => { s.angle = 25f; s.radius = 1f; s.length = 2f; }));
        Simulated("shape/box", ps => Shape(ps, ParticleSystemShapeType.Box, s => s.scale = new Vector3(2f, 3f, 4f)));
        Simulated("shape/circle", ps => Shape(ps, ParticleSystemShapeType.Circle, s => s.radiusThickness = 0.5f));
        Simulated("shape/edge", ps => Shape(ps, ParticleSystemShapeType.SingleSidedEdge));
        Simulated("shape/donut", ps => Shape(ps, ParticleSystemShapeType.Donut));
        Simulated("shape/rectangle", ps => Shape(ps, ParticleSystemShapeType.Rectangle));
        Simulated("shape/randomize", ps => Shape(ps, ParticleSystemShapeType.Box, s =>
        {
            s.randomDirectionAmount = 0.5f; s.sphericalDirectionAmount = 0.25f; s.randomPositionAmount = 0.3f;
        }));

        // Velocity over Lifetime.
        Simulated("vol/two", ps =>
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.x = Two(0f, 1f); v.y = Two(0f, 1f); v.z = Two(0f, 1f);
        });
        Simulated("vol/curves", ps =>
        {
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.x = new ParticleSystem.MinMaxCurve(2f, Rise, Fall); v.y = new ParticleSystem.MinMaxCurve(1f, Fall, Rise);
            v.z = new ParticleSystem.MinMaxCurve(1f, Rise, Fall);
        });
        Simulated("vol/speedmodifier", ps =>
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.speedModifier = Two(0.5f, 1.5f);
        });
        Simulated("vol/orbital", ps =>
        {
            Shape(ps, ParticleSystemShapeType.Circle, s => s.radiusThickness = 0f);
            var v = ps.velocityOverLifetime; v.enabled = true;
            v.orbitalX = Two(0f, 1f); v.orbitalY = Two(0f, 1f); v.orbitalZ = Two(0f, 2f);
        });
        for (int axis = 0; axis < 3; axis++)
        {
            int a = axis;
            Simulated("vol/orbitalaxis" + axis, ps =>
            {
                Shape(ps, ParticleSystemShapeType.Circle, s => s.radiusThickness = 0f);
                var v = ps.velocityOverLifetime; v.enabled = true;
                if (a == 0) v.orbitalX = Two(0f, 1f);
                if (a == 1) v.orbitalY = Two(0f, 1f);
                if (a == 2) v.orbitalZ = Two(0f, 1f);
            });
        }
        for (int axis = 0; axis < 3; axis++)
        {
            int a = axis;
            Simulated("vol/orbitalmatched" + axis, ps =>
            {
                Shape(ps, ParticleSystemShapeType.Circle, s => s.radiusThickness = 0f);
                var v = ps.velocityOverLifetime; v.enabled = true;
                v.orbitalX = Two(0f, a == 0 ? 1f : 0f);
                v.orbitalY = Two(0f, a == 1 ? 1f : 0f);
                v.orbitalZ = Two(0f, a == 2 ? 1f : 0f);
            });
        }
        Simulated("vol/radial", ps =>
        {
            Shape(ps, ParticleSystemShapeType.Circle, s => s.radiusThickness = 0f);
            var v = ps.velocityOverLifetime; v.enabled = true; v.radial = Two(0f, 2f);
        });

        // Limit Velocity over Lifetime.
        Simulated("limit/speed", ps =>
        {
            var l = ps.limitVelocityOverLifetime; l.enabled = true; l.limit = Two(0.5f, 1.5f); l.dampen = 1f;
        });
        Simulated("limit/axes", ps =>
        {
            Shape(ps, ParticleSystemShapeType.Sphere);
            var l = ps.limitVelocityOverLifetime; l.enabled = true; l.separateAxes = true; l.dampen = 1f;
            l.limitX = Two(0f, 0.5f); l.limitY = Two(0f, 0.5f); l.limitZ = Two(0f, 0.5f);
        });
        Simulated("limit/drag", ps =>
        {
            var l = ps.limitVelocityOverLifetime; l.enabled = true; l.limit = 100f; l.drag = Two(0f, 2f);
        });

        // Force over Lifetime, Inherit-free.
        Simulated("force/two", ps =>
        {
            var f = ps.forceOverLifetime; f.enabled = true; f.x = Two(0f, 1f); f.y = Two(0f, 1f); f.z = Two(0f, 1f);
        });
        Simulated("force/randomized", ps =>
        {
            var f = ps.forceOverLifetime; f.enabled = true; f.randomized = true;
            f.x = Two(0f, 1f); f.y = Two(0f, 1f); f.z = Two(0f, 1f);
        }, 3);

        // Gravity modifier.
        Simulated("gravity/two", ps => { var m = ps.main; m.gravityModifier = Two(0f, 1f); });

        // Size, rotation and color modules.
        Simulated("size/life", ps => { var s = ps.sizeOverLifetime; s.enabled = true; s.size = Two(0f, 1f); });
        Simulated("size/axes", ps =>
        {
            var s = ps.sizeOverLifetime; s.enabled = true; s.separateAxes = true;
            s.x = Two(0f, 1f); s.y = Two(0f, 1f); s.z = Two(0f, 1f);
        });
        Simulated("size/curves", ps =>
        {
            var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, Rise, Fall);
        });
        Simulated("size/speed", ps =>
        {
            var s = ps.sizeBySpeed; s.enabled = true; s.size = Two(0f, 1f); s.range = new Vector2(0f, 10f);
        });
        Simulated("rotation/life", ps => { var r = ps.rotationOverLifetime; r.enabled = true; r.z = Two(0f, 6f); });
        Simulated("rotation/axes", ps =>
        {
            var r = ps.rotationOverLifetime; r.enabled = true; r.separateAxes = true;
            r.x = Two(0f, 6f); r.y = Two(0f, 6f); r.z = Two(0f, 6f);
        });
        Simulated("rotation/speed", ps =>
        {
            var r = ps.rotationBySpeed; r.enabled = true; r.z = Two(0f, 6f); r.range = new Vector2(0f, 10f);
        });
        Simulated("color/life", ps =>
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            c.color = new ParticleSystem.MinMaxGradient(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 1f));
        });
        Simulated("color/gradients", ps =>
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            c.color = new ParticleSystem.MinMaxGradient(Ramp(Color.black, Color.white), Ramp(Color.white, new Color(0f, 0f, 0f, 0f)));
        });
        Simulated("color/randomcolor", ps =>
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            c.color = new ParticleSystem.MinMaxGradient(Ramp(new Color(1f, 0f, 0f, 0f), new Color(0f, 1f, 1f, 1f))) { mode = ParticleSystemGradientMode.RandomColor };
        });
        Simulated("color/speed", ps =>
        {
            var c = ps.colorBySpeed; c.enabled = true; c.range = new Vector2(0f, 10f);
            c.color = new ParticleSystem.MinMaxGradient(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 1f));
        });

        Simulated("color/speed64", ps =>
        {
            var c = ps.colorBySpeed; c.enabled = true; c.range = new Vector2(0f, 10f);
            c.color = new ParticleSystem.MinMaxGradient(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 1f));
        }, burst: 64);
        Simulated("vol/speedmodifier9", ps =>
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.speedModifier = Two(0.5f, 1.5f);
        }, burst: 9);

        // Custom data.
        Simulated("custom/two", ps =>
        {
            var c = ps.customData; c.enabled = true;
            c.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            c.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 4);
            for (int k = 0; k < 4; k++) c.SetVector(ParticleSystemCustomData.Custom1, k, Two(0f, 1f));
            c.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Color);
            c.SetColor(ParticleSystemCustomData.Custom2, new ParticleSystem.MinMaxGradient(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 1f)));
        }, custom: true);

        Simulated("custom/reversed", ps =>
        {
            var c = ps.customData; c.enabled = true;
            c.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Color);
            c.SetColor(ParticleSystemCustomData.Custom1, new ParticleSystem.MinMaxGradient(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 1f)));
            c.SetMode(ParticleSystemCustomData.Custom2, ParticleSystemCustomDataMode.Vector);
            c.SetVectorComponentCount(ParticleSystemCustomData.Custom2, 4);
            for (int k = 0; k < 4; k++) c.SetVector(ParticleSystemCustomData.Custom2, k, Two(0f, 1f));
        }, custom: true);

        // Bursts: probability and a count range, one cycle every 0.1 s.
        Counted("burst/probability", ps =>
        {
            var e = ps.emission;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 1, 0, 0.1f) { probability = 0.5f } });
        }, 40, 0.1f);
        Counted("burst/count", ps =>
        {
            var e = ps.emission;
            e.SetBursts(new[] { new ParticleSystem.Burst(0f, 1, 6, 0, 0.1f) });
        }, 40, 0.1f);
    }

    static void SubEmitters()
    {
        foreach (uint seed in Seeds)
        {
            var parent = Make("subemit/parent", seed, ps =>
            {
                Shape(ps, ParticleSystemShapeType.Box, s => s.scale = new Vector3(10f, 10f, 10f));
            }, 12);
            parent.gameObject.SetActive(false);
            var childObject = new GameObject("subemit/child");
            childObject.transform.SetParent(parent.transform, false);
            var child = childObject.AddComponent<ParticleSystem>();
            child.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            child.useAutoRandomSeed = false;
            child.randomSeed = seed + 1u;
            var cm = child.main; cm.playOnAwake = false; cm.startLifetime = 10f; cm.startSpeed = 0f; cm.maxParticles = 256;
            var cs = child.shape; cs.enabled = false;
            var ce = child.emission; ce.rateOverTime = 0f;
            ce.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            var sub = parent.subEmitters; sub.enabled = true;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing, 0.5f);
            parent.gameObject.SetActive(true);
            for (int i = 0; i < 2; i++)
            {
                parent.Simulate(Dt, true, i == 0, false);
                RecordParticles(Key("subemit/parent", seed, i), parent);
                RecordParticles(Key("subemit/child", seed, i), child);
            }
            DestroyImmediate(parent.gameObject);
        }
    }

    static void Meshes()
    {
        var meshes = new[] { Quad(1f), Quad(2f), Quad(3f) };
        foreach (uint seed in Seeds)
        {
            var ps = Make("mesh/index", seed, null, 9);
            // The renderer the system adds for itself, configured once the object is active.
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.meshDistribution = ParticleSystemMeshDistribution.UniformRandom;
            r.SetMeshes(meshes);
            ps.Simulate(Dt, true, true, false);
            int n = ps.GetParticles(s_Buffer);
            var v = new List<float> { n };
            for (int i = 0; i < n; i++) { Add(v, s_Buffer[i].randomSeed); v.Add(s_Buffer[i].GetMeshIndex(ps)); }
            Record(Key("mesh/index", seed, 0), v);
            DestroyImmediate(ps.gameObject);
        }
    }

    void Start() => StartCoroutine(Probe());

    IEnumerator Probe()
    {
        Scenarios();
        SubEmitters();
        Meshes();

        // Played over frames at a fixed delta: the same start values and module randoms as Simulate.
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var played = new List<(uint, ParticleSystem)>();
        foreach (uint seed in Seeds)
        {
            var ps = Make("played", seed, s =>
            {
                var m = s.main; m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                m.startLifetime = Two(1f, 3f); m.startSpeed = Two(1f, 4f); m.startSize = Two(0.5f, 2f);
                Shape(s, ParticleSystemShapeType.Sphere);
                var v = s.velocityOverLifetime; v.enabled = true; v.x = Two(0f, 1f); v.y = Two(0f, 1f); v.z = Two(0f, 1f);
                var c = s.colorOverLifetime; c.enabled = true;
                c.color = new ParticleSystem.MinMaxGradient(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 1f));
            });
            ps.Play();
            played.Add((seed, ps));
        }
        for (int frame = 0; frame < 3; frame++)
        {
            yield return null;
            foreach (var (seed, ps) in played) RecordParticles(Key("played", seed, frame), ps);
        }
        Time.captureDeltaTime = 0f;
        var done = new List<float> { 1f };
        Record("complete", done);
        File.WriteAllText(s_Path, s_Out.ToString());
        Debug.Log("particle random complete: " + s_Path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
