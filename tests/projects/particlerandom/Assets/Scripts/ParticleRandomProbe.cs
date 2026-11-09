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
public partial class ParticleRandomProbe : MonoBehaviour
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

    /// <summary>Emit with EmitParams overriding the start lifetime (Lifetime by Emitter Speed on)
    /// and the velocity (Inherit Velocity Initial in a curve mode), then one step.</summary>
    static void ModuleOverrides()
    {
        foreach (uint seed in Seeds)
        {
            var ps = Make("override", seed, s =>
            {
                var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
                m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = new Vector3(2f, 1f, -3f);
                var l = s.lifetimeByEmitterSpeed; l.enabled = true; l.curve = Two(0.3f, 2f); l.range = new Vector2(0f, 5f);
                var iv = s.inheritVelocity; iv.enabled = true; iv.mode = ParticleSystemInheritVelocityMode.Initial;
                iv.curve = new ParticleSystem.MinMaxCurve(1.5f, Rise, Fall);
            }, 0);
            ps.Simulate(0f, true, true, false);
            ps.Emit(new ParticleSystem.EmitParams { startLifetime = 2f, applyShapeToPosition = true }, 2);
            ps.Emit(new ParticleSystem.EmitParams { velocity = new Vector3(1f, 0f, 0f) }, 2);
            ps.Simulate(Dt, true, false, false);
            RecordParticles(Key("override/emitparams", seed, 0), ps);
            DestroyImmediate(ps.gameObject);
        }
    }

    /// <summary>A ring buffer of 12 in both modes filled past its capacity: Emit calls and a
    /// step between them, and rate emission over several steps (which particles stay, in which
    /// slots, and how many).</summary>
    static void RingBuffers()
    {
        foreach (var mode in new[] { ParticleSystemRingBufferMode.PauseUntilReplaced, ParticleSystemRingBufferMode.LoopUntilReplaced })
        {
            string name = mode == ParticleSystemRingBufferMode.PauseUntilReplaced ? "pause" : "loop";
            foreach (uint seed in Seeds)
            {
                var ps = Make("ring", seed, s => { var m = s.main; m.maxParticles = 12; m.ringBufferMode = mode; }, 0);
                ps.Simulate(0f, true, true, false);
                ps.Emit(10); RecordParticles(Key("ring/" + name + "/emit", seed, 0), ps);
                ps.Emit(10); RecordParticles(Key("ring/" + name + "/emit", seed, 1), ps);
                ps.Simulate(Dt, true, false, false); RecordParticles(Key("ring/" + name + "/emit", seed, 2), ps);
                ps.Emit(7); RecordParticles(Key("ring/" + name + "/emit", seed, 3), ps);
                DestroyImmediate(ps.gameObject);
            }
            Simulated("ring/" + name + "/rate", ps =>
            {
                var m = ps.main; m.maxParticles = 12; m.ringBufferMode = mode;
                var e = ps.emission; e.rateOverTime = 100f;
            }, 5, 0);
        }
    }

    /// <summary>Plane collisions with random dampen, bounce and lifetime loss (distinct ranges, so
    /// each value names its curve), two curves rising over the life, and one random curve alone:
    /// which random value each hit takes. Nine particles at speeds 1 to 4 reach the plane over
    /// several steps.</summary>
    static void CollisionCurves()
    {
        var rise = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        var cases = new (string, Action<ParticleSystem.CollisionModule>)[]
        {
            ("two", c => { c.dampen = Two(0.1f, 0.6f); c.bounce = Two(0.2f, 1f); c.lifetimeLoss = Two(0f, 0.3f); }),
            ("curves", c => { c.dampen = new ParticleSystem.MinMaxCurve(0.6f, rise, AnimationCurve.Linear(0f, 0.5f, 1f, 1f)); c.bounce = new ParticleSystem.MinMaxCurve(1f, rise, AnimationCurve.Linear(0f, 0.4f, 1f, 1f)); c.lifetimeLoss = 0f; }),
            ("bounceonly", c => { c.dampen = 0f; c.bounce = Two(0.2f, 1f); c.lifetimeLoss = 0f; }),
            ("curve", c => { c.dampen = new ParticleSystem.MinMaxCurve(0.5f, AnimationCurve.Linear(0f, 1f, 1f, 0f)); c.bounce = new ParticleSystem.MinMaxCurve(1f, rise); c.lifetimeLoss = 0f; }),
        };
        var plane = new GameObject("collision plane").transform;
        plane.SetPositionAndRotation(new Vector3(0f, 0f, 0.3f), Quaternion.Euler(-90f, 0f, 0f));
        foreach (var (name, configure) in cases)
            foreach (uint seed in Seeds)
            {
                var ps = Make("collision", seed, s =>
                {
                    var m = s.main; m.startSpeed = Two(1f, 4f); m.startSize = 0.02f; m.startLifetime = 2f;
                    var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.SetPlane(0, plane);
                    configure(c);
                }, 9);
                for (int i = 0; i < 6; i++)
                {
                    ps.Simulate(Dt, true, i == 0, false);
                    RecordParticles(Key("collision/" + name, seed, i), ps);
                }
                DestroyImmediate(ps.gameObject);
            }
        DestroyImmediate(plane.gameObject);
    }

    /// <summary>A moving emitter with a custom emitter velocity (zero, and across the motion),
    /// rate over time and over distance: where its newborns are placed.</summary>
    static void MovingCustomEmitter()
    {
        foreach (var (name, velocity, distance) in new[] { ("zero", Vector3.zero, false), ("across", new Vector3(0f, 4f, 0f), false), ("distance", new Vector3(0f, 4f, 0f), true) })
        {
            var ps = Make("moving", 12345u, s =>
            {
                var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.startSpeed = 0f;
                m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = velocity;
                var e = s.emission;
                if (distance) e.rateOverDistance = 8f; else e.rateOverTime = 80f;
            }, 0);
            for (int i = 0; i < 3; i++)
            {
                ps.transform.position = new Vector3(0.5f * i, 0f, 0f);
                ps.Simulate(Dt, true, i == 0, false);
                RecordParticles("moving/" + name + "/" + i.ToString(CultureInfo.InvariantCulture), ps);
            }
            DestroyImmediate(ps.gameObject);
        }
    }

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

        // Inherit Velocity (Initial, Current) and Lifetime by Emitter Speed, from a custom
        // emitter velocity in world space: their random modes' per-particle values.
        foreach (var (name, mode, curve) in new[]
                 {
                     ("initial/two", ParticleSystemInheritVelocityMode.Initial, Two(0.2f, 1.5f)),
                     ("initial/curves", ParticleSystemInheritVelocityMode.Initial, new ParticleSystem.MinMaxCurve(1.5f, Rise, Fall)),
                     ("current/two", ParticleSystemInheritVelocityMode.Current, Two(0f, 1f)),
                     ("current/curves", ParticleSystemInheritVelocityMode.Current, new ParticleSystem.MinMaxCurve(1.5f, Rise, Fall)),
                 })
        {
            var (m0, c0) = (mode, curve);
            Simulated("inherit/" + name, ps =>
            {
                var m = ps.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
                m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = new Vector3(2f, 1f, -3f);
                var iv = ps.inheritVelocity; iv.enabled = true; iv.mode = m0; iv.curve = c0;
            });
        }
        foreach (var (name, curve, range) in new[]
                 {
                     ("two", Two(0.3f, 2f), new Vector2(0f, 5f)),
                     ("curves", new ParticleSystem.MinMaxCurve(2f, Rise, Fall), new Vector2(1f, 6f)),
                     ("below", Two(0.5f, 1.5f), new Vector2(6f, 9f)),
                 })
        {
            var (c0, r0) = (curve, range);
            Simulated("lbes/" + name, ps =>
            {
                var m = ps.main; m.startLifetime = Two(1f, 3f);
                m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = new Vector3(2f, 1f, -3f);
                var l = ps.lifetimeByEmitterSpeed; l.enabled = true; l.curve = c0; l.range = r0;
            });
        }

        // Lifetime by Emitter Speed at a factor of 0 and with an empty range.
        Simulated("lbes/zero", ps =>
        {
            var m = ps.main; m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = new Vector3(2f, 1f, -3f);
            var l = ps.lifetimeByEmitterSpeed; l.enabled = true; l.curve = 0f; l.range = new Vector2(0f, 5f);
        });
        Simulated("lbes/degenerate", ps =>
        {
            var m = ps.main; m.startLifetime = Two(1f, 3f);
            m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom; m.emitterVelocity = new Vector3(2f, 1f, -3f);
            var l = ps.lifetimeByEmitterSpeed; l.enabled = true; l.curve = new ParticleSystem.MinMaxCurve(1f, Rise); l.range = new Vector2(2f, 2f);
        });
        ModuleOverrides();
        MovingCustomEmitter();
        CollisionCurves();
        RingBuffers();

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

    static ParticleSystem RateSystem(string name, uint seed, float rate, bool ranged, bool shape)
    {
        return Make(name, seed, ps =>
        {
            var m = ps.main;
            m.duration = 0.05f;
            m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            m.startLifetime = Two(1f, 3f);
            m.startSpeed = Two(1f, 4f);
            m.startSize = Two(0.5f, 2f);
            var e = ps.emission;
            e.rateOverTime = ranged ? Two(rate * 0.75f, rate * 1.25f) : new ParticleSystem.MinMaxCurve(rate);
            if (shape) Shape(ps, ParticleSystemShapeType.Sphere);
        }, 0);
    }

    // Isolate rate batches around four-lane boundaries, including a loop crossing.
    // Position and lifetime retain each seed's observable age, without sorting particles.
    static IEnumerator RateBatches()
    {
        float previousMax = Time.maximumParticleDeltaTime;
        Time.maximumParticleDeltaTime = 1f;
        foreach (uint seed in Seeds)
            foreach (int count in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17 })
                foreach (bool ranged in new[] { false, true })
                    foreach (bool shape in new[] { false, true })
                    {
                        string scenario = "ratebatch/" + count + (ranged ? "/ranged" : "/constant") + (shape ? "/sphere" : "/plain");
                        var ps = RateSystem(scenario, seed, (count + 0.25f) / 0.02f, ranged, shape);
                        for (int step = 0; step < 4; step++)
                        {
                            ps.Simulate(0.02f, true, step == 0, false);
                            RecordParticles(Key(scenario, seed, step), ps);
                        }
                        DestroyImmediate(ps.gameObject);
                    }
        foreach (uint seed in Seeds)
            foreach (string mode in new[] { "capacity", "expiry", "curves", "burst" })
            {
                string scenario = "ratebatch/edge/" + mode;
                var ps = RateSystem(scenario, seed, 262.5f, false, false);
                var m = ps.main;
                if (mode == "capacity") m.maxParticles = 7;
                if (mode == "expiry") m.startLifetime = Two(0.005f, 0.035f);
                if (mode == "curves")
                {
                    m.startLifetime = new ParticleSystem.MinMaxCurve(3f, AnimationCurve.Linear(0f, 0.5f, 1f, 1f));
                    m.startSpeed = new ParticleSystem.MinMaxCurve(4f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                }
                if (mode == "burst")
                {
                    var e = ps.emission;
                    e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)2), new ParticleSystem.Burst(0.03f, (short)3) });
                }
                for (int step = 0; step < 4; step++)
                {
                    ps.Simulate(0.02f, true, step == 0, false);
                    RecordParticles(Key(scenario, seed, step), ps);
                }
                DestroyImmediate(ps.gameObject);
            }
        Time.captureDeltaTime = 0.02f;
        yield return null;
        var playing = new List<(string, uint, ParticleSystem)>();
        foreach (uint seed in Seeds)
            foreach (bool ranged in new[] { false, true })
            {
                string scenario = "ratebatch/play/" + (ranged ? "ranged" : "constant");
                var ps = RateSystem(scenario, seed, 262.5f, ranged, false);
                ps.Play();
                playing.Add((scenario, seed, ps));
            }
        for (int frame = 0; frame < 4; frame++)
        {
            yield return null;
            foreach (var (scenario, seed, ps) in playing) RecordParticles(Key(scenario, seed, frame), ps);
        }
        foreach (var entry in playing) DestroyImmediate(entry.Item3.gameObject);
        Time.captureDeltaTime = 0f;
        Time.maximumParticleDeltaTime = previousMax;
    }

    // Each amount alone, each pair and all three, with zero/intermediate/full controls.
    // Repeated updates and separate Emit calls also check subsequent shape stream draws.
    static void ShapeModifiers()
    {
        float previousMax = Time.maximumParticleDeltaTime;
        Time.maximumParticleDeltaTime = 1f;
        var types = new[] { ParticleSystemShapeType.Sphere, ParticleSystemShapeType.Cone, ParticleSystemShapeType.Box };
        foreach (var type in types)
            for (int p = 0; p < 3; p++)
                for (int d = 0; d < 3; d++)
                    for (int s = 0; s < 3; s++)
                        ShapeModifierCase(type, p, d, s, "burst");
        foreach (var type in types)
            foreach (string path in new[] { "rate", "emit", "local", "world" })
                for (int mask = 0; mask < 8; mask++)
                    ShapeModifierCase(type, (mask & 1) != 0 ? 1 : 0,
                        (mask & 2) != 0 ? 1 : 0, (mask & 4) != 0 ? 1 : 0, path);
        foreach (var type in new[] { ParticleSystemShapeType.Circle, ParticleSystemShapeType.SingleSidedEdge,
            ParticleSystemShapeType.ConeVolume })
            for (int mask = 0; mask < 8; mask++)
                ShapeModifierCase(type, (mask & 1) != 0 ? 1 : 0,
                    (mask & 2) != 0 ? 1 : 0, (mask & 4) != 0 ? 1 : 0, "burst");
        Time.maximumParticleDeltaTime = previousMax;
    }

    static void ShapeModifierCase(ParticleSystemShapeType type, int p, int d, int s, string path)
    {
        string scenario = "shapemod/" + type + "/" + p + d + s + "/" + path;
        foreach (uint seed in Seeds)
        {
            var ps = Make(scenario, seed, system =>
            {
                Shape(system, type, shape =>
                {
                    shape.randomPositionAmount = p == 2 ? 1f : p * 0.3f;
                    shape.randomDirectionAmount = d == 2 ? 1f : d * 0.5f;
                    shape.sphericalDirectionAmount = s == 2 ? 1f : s * 0.25f;
                    shape.radius = 1.2f; shape.radiusThickness = 0.6f;
                    shape.angle = 25f;
                    if (path == "local" || path == "world")
                    {
                        shape.position = new Vector3(0.4f, -0.7f, 0.2f);
                        shape.rotation = new Vector3(21f, -33f, 17f);
                        shape.scale = new Vector3(2f, 0.7f, 1.3f);
                        system.transform.position = new Vector3(3f, -2f, 1f);
                        system.transform.rotation = Quaternion.Euler(-12f, 47f, 8f);
                        var main = system.main;
                        main.simulationSpace = path == "world" ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
                    }
                });
                if (path == "rate") { var e = system.emission; e.rateOverTime = 262.5f; }
            }, path == "rate" || path == "emit" ? 0 : 9);
            for (int step = 0; step < 2; step++)
            {
                if (path == "emit") ps.Emit(step == 0 ? 9 : 5);
                ps.Simulate(0.02f, true, path != "emit" && step == 0, false);
                RecordParticles(Key(scenario, seed, step), ps);
            }
            DestroyImmediate(ps.gameObject);
        }
    }

    void Start() => StartCoroutine(Probe());

    IEnumerator Probe()
    {
        yield return RateBatches();
        ShapeModifiers();
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
        BirthControls();
        BirthRateControls();
        BirthLifetimeControls();
        yield return BirthLifetimeCallbacks();
        yield return ModelControls();
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
