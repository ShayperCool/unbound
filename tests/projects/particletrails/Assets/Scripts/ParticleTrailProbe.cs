using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what a particle system with a Trails module reports and draws while particles die:
/// the system state, every particle GetParticles returns and the trail mesh BakeTrailsMesh builds,
/// after every Simulate step, for dieWithParticles false and true, PerParticle and Ribbon trails,
/// local and world trail space, trail lifetime curves and ratio, Death sub-emitters, collision,
/// Stop, Clear, SetParticles, a particle cap and a restart; then systems played over frames at a
/// fixed capture delta (stop callback), and images of fading trails. Each record is one JSON line
/// {"key", "value": [numbers]} in <c>PARTICLE_TRAILS_OUT</c>; images go to
/// <c>PARTICLE_TRAILS_IMAGES</c>.
/// </summary>
public class ParticleTrailProbe : MonoBehaviour
{
    const float Step = 0.05f;
    const int Steps = 36;
    const int ImageSize = 128;
    static readonly StringBuilder s_Out = new StringBuilder();
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[256];
    static readonly List<string> s_Events = new List<string>();
    static string s_Path, s_Images;
    static Camera s_BakeCamera;
    static Mesh s_Mesh;
    static Material s_ParticleMaterial, s_TrailMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_TRAILS_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        s_Images = Environment.GetEnvironmentVariable("PARTICLE_TRAILS_IMAGES");
        new GameObject("ParticleTrailProbe").AddComponent<ParticleTrailProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Record(string key, IList<float> values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Count; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    static Texture2D SoftTexture()
    {
        var t = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float d = new Vector2(x - 15.5f, y - 15.5f).magnitude / 16f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d * d)));
            }
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        return t;
    }

    static AnimationCurve Curve(params float[] timeValue)
    {
        var c = new AnimationCurve();
        for (int i = 0; i + 1 < timeValue.Length; i += 2) c.AddKey(new Keyframe(timeValue[i], timeValue[i + 1]));
        return c;
    }

    /// <summary>A non-looping 1 s system at the origin: four particles at 0 s and two at 0.3 s
    /// leave a circle of radius 0.2 outwards in the XY plane at 2 units/s, living 0.4 to 0.9 s;
    /// each has a trail of 0.5 × its start lifetime with points 0.07 apart (off the 0.05 a step
    /// moves, so float rounding does not decide a point) that outlives it.</summary>
    static ParticleSystem Make(string name, Action<ParticleSystem> configure = null, Transform parent = null)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 4242u;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 1f;
        main.loop = false;
        main.maxParticles = 64;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = 2f;
        main.startSize = 0.2f;
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.2f;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)4), new ParticleSystem.Burst(0.3f, (short)2) });
        var trails = ps.trails;
        trails.enabled = true;
        trails.mode = ParticleSystemTrailMode.PerParticle;
        trails.lifetime = 0.5f;
        trails.minVertexDistance = 0.07f;
        trails.dieWithParticles = false;
        trails.widthOverTrail = 0.5f;
        configure?.Invoke(ps);
        go.SetActive(true);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = s_ParticleMaterial;
        renderer.trailMaterial = s_TrailMaterial;
        return ps;
    }

    static void RecordState(string key, ParticleSystem ps)
    {
        Record(key + "/state", new List<float>
        {
            ps.particleCount, ps.IsAlive(true) ? 1 : 0, ps.isPlaying ? 1 : 0, ps.isEmitting ? 1 : 0,
            ps.isStopped ? 1 : 0, ps.isPaused ? 1 : 0, ps.time,
        });
        int count = ps.particleCount;
        if (s_Buffer.Length < count) s_Buffer = new ParticleSystem.Particle[count * 2];
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { n };
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            v.Add(p.position.x); v.Add(p.position.y); v.Add(p.position.z);
            v.Add(p.velocity.x); v.Add(p.velocity.y); v.Add(p.velocity.z);
            v.Add(p.remainingLifetime); v.Add(p.startLifetime);
            v.Add(p.GetCurrentSize(ps)); v.Add(p.GetCurrentColor(ps).a);
            v.Add(p.randomSeed & 0xffff); v.Add(p.randomSeed >> 16);
        }
        Record(key + "/particles", v);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        s_Mesh.Clear();
        renderer.BakeTrailsMesh(s_Mesh, s_BakeCamera, ParticleSystemBakeMeshOptions.Default);
        var vertices = s_Mesh.vertices;
        var colors = s_Mesh.colors32;
        var m = new List<float> { vertices.Length };
        for (int i = 0; i < vertices.Length; i++)
        {
            m.Add(vertices[i].x); m.Add(vertices[i].y); m.Add(vertices[i].z);
            m.Add(i < colors.Length ? colors[i].a : -1);
        }
        Record(key + "/trailmesh", m);
    }

    /// <summary>The count, then each particle's random seed as two 16-bit halves.</summary>
    static void RecordSeeds(string key, ParticleSystem ps)
    {
        if (s_Buffer.Length < ps.particleCount) s_Buffer = new ParticleSystem.Particle[ps.particleCount * 2];
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { n };
        for (int i = 0; i < n; i++) { v.Add(s_Buffer[i].randomSeed & 0xffff); v.Add(s_Buffer[i].randomSeed >> 16); }
        Record(key, v);
    }

    /// <summary>Steps <paramref name="ps"/> by <see cref="Step"/> seconds per call, recording after
    /// each; <paramref name="before"/> runs before a step and may act on the system.</summary>
    static void Run(string key, ParticleSystem ps, int steps = Steps, Action<int, ParticleSystem> before = null,
        Action<int> extra = null)
    {
        for (int i = 0; i < steps; i++)
        {
            before?.Invoke(i, ps);
            ps.Simulate(Step, true, i == 0, false);
            string k = key + "/" + i;
            RecordState(k, ps);
            extra?.Invoke(i);
        }
    }

    static void Done(ParticleSystem ps)
    {
        if (ps.transform.parent != null) DestroyImmediate(ps.transform.parent.gameObject);
        else DestroyImmediate(ps.gameObject);
    }

    static void Simulations()
    {
        foreach (bool die in new[] { false, true })
        {
            string d = die ? "die1" : "die0";
            var ps = Make("pp/local/" + d, s => { var t = s.trails; t.dieWithParticles = die; });
            Run("pp/local/" + d, ps);
            Done(ps);

            // Ribbon mode: one ribbon through the particles in emission order.
            ps = Make("ribbon/" + d, s =>
            {
                var t = s.trails; t.dieWithParticles = die; t.mode = ParticleSystemTrailMode.Ribbon;
                var e = s.emission; e.rateOverTime = 10f;
            });
            Run("ribbon/" + d, ps);
            Done(ps);
        }

        // Trails in world space while the emitter moves; and the moving emitter in local space.
        foreach (var (name, worldTrail, worldSim) in new[] { ("worldtrail", true, false), ("worldsim", false, true), ("localmove", false, false) })
        {
            var ps = Make("pp/" + name + "/die0", s =>
            {
                var t = s.trails; t.worldSpace = worldTrail;
                var m = s.main; m.simulationSpace = worldSim ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            });
            Run("pp/" + name + "/die0", ps, before: (i, s) => s.transform.position = new Vector3(0.1f * i, 0f, 0f));
            Done(ps);
        }

        // Trail lifetime as a range, a ratio of trails, larger vertex distance.
        var lc = Make("pp/lifecurve/die0", s =>
        {
            var t = s.trails; t.lifetime = new ParticleSystem.MinMaxCurve(0.2f, 1f); t.ratio = 0.5f; t.minVertexDistance = 0.23f;
        });
        Run("pp/lifecurve/die0", lc);
        Done(lc);

        // Which particles get a trail (ratio 0.5) and how long it lives (a range): 48 particles
        // live 1 s; those with a trail stay while it fades for 0.2 to 1 s.
        var ratio = Make("pp/ratio/die0", s =>
        {
            var m = s.main; m.startLifetime = 1f;
            var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)48) });
            var t = s.trails; t.lifetime = new ParticleSystem.MinMaxCurve(0.2f, 1f); t.ratio = 0.5f;
        });
        Run("pp/ratio/die0", ratio, 44);
        Done(ratio);

        // The same selection over 600 particles, for ratios 0.5 and 0.25: the seeds of all
        // particles, then of those still in the buffer after they died (the ones with a trail).
        foreach (float r in new[] { 0.5f, 0.25f })
        {
            string key = "pp/ratiobig/" + r.ToString("R", CultureInfo.InvariantCulture);
            var big = Make(key, s =>
            {
                var m = s.main; m.startLifetime = 1f; m.maxParticles = 600;
                var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)600) });
                var t = s.trails; t.ratio = r;
            });
            big.Simulate(0.5f, true, true, false);
            RecordSeeds(key + "/born", big);
            big.Simulate(0.55f, true, false, false);
            RecordSeeds(key + "/kept", big);
            Done(big);
        }

        // Trail lifetime by particle size.
        var sl = Make("pp/sizelife/die0", s =>
        {
            var t = s.trails; t.sizeAffectsLifetime = true;
            var m = s.main; m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        });
        Run("pp/sizelife/die0", sl);
        Done(sl);

        // A Death sub-emitter: when its event fires and where.
        foreach (bool die in new[] { false, true })
        {
            string key = "pp/deathsub/" + (die ? "die1" : "die0");
            var root = new GameObject(key + "/root");
            var parent = Make(key, s => { var t = s.trails; t.dieWithParticles = die; }, root.transform);
            var child = Make(key + "/child", s =>
            {
                var m = s.main; m.startLifetime = 5f; m.startSpeed = 0f;
                var sh = s.shape; sh.enabled = false;
                var e = s.emission; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                var t = s.trails; t.enabled = false;
            }, parent.transform);
            var sub = parent.subEmitters;
            sub.enabled = true;
            sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            Run(key, parent, extra: i => RecordState(key + "/" + i + "/child", child));
            DestroyImmediate(root);
        }

        // Stop (emitting) after the first deaths, then Stop and clear.
        var stop = Make("pp/stop/die0");
        Run("pp/stop/die0", stop, before: (i, s) =>
        {
            if (i == 12) { s.Stop(true, ParticleSystemStopBehavior.StopEmitting); RecordState("pp/stop/die0/stopped", s); }
            if (i == 20) { s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); RecordState("pp/stop/die0/cleared", s); }
        });
        Done(stop);

        var clear = Make("pp/clear/die0");
        Run("pp/clear/die0", clear, before: (i, s) =>
        {
            if (i == 12) { s.Clear(true); RecordState("pp/clear/die0/cleared", s); }
        });
        Done(clear);

        // A cap of four particles under a steady rate: do fading trails hold slots?
        var cap = Make("pp/maxp/die0", s =>
        {
            var m = s.main; m.maxParticles = 4; m.loop = true; m.startLifetime = 0.3f;
            var e = s.emission; e.rateOverTime = 20f; e.SetBursts(new ParticleSystem.Burst[0]);
        });
        Run("pp/maxp/die0", cap);
        Done(cap);

        // A looping system: newborns taking the slots fading trails free.
        var loop = Make("pp/loop/die0", s =>
        {
            var m = s.main; m.loop = true;
            var e = s.emission; e.rateOverTime = 7f;
        });
        Run("pp/loop/die0", loop, 48);
        Done(loop);

        // SetParticles: the particles as read (fading carriers included), and a living particle
        // written at zero lifetime.
        var set = Make("pp/setparticles/die0");
        Run("pp/setparticles/die0", set, before: (i, s) =>
        {
            if (i != 12) return;
            int n = s.GetParticles(s_Buffer);
            s.SetParticles(s_Buffer, n);
            RecordState("pp/setparticles/die0/written", s);
        });
        Done(set);
        var kill = Make("pp/killlife/die0");
        Run("pp/killlife/die0", kill, before: (i, s) =>
        {
            if (i != 6) return;
            int n = s.GetParticles(s_Buffer);
            s_Buffer[0].remainingLifetime = 0f;
            s.SetParticles(s_Buffer, n);
            RecordState("pp/killlife/die0/written", s);
        });
        Done(kill);

        // Particles that die by colliding with a plane (lifetime loss 1).
        var plane = new GameObject("pp/collision/plane").transform;
        plane.position = new Vector3(0f, -0.5f, 0f);
        var collide = Make("pp/collision/die0", s =>
        {
            var m = s.main; m.startLifetime = 2f;
            var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.SetPlane(0, plane);
            c.lifetimeLoss = 1f; c.bounce = 1f; c.dampen = 0f;
        });
        Run("pp/collision/die0", collide);
        Done(collide);
        DestroyImmediate(plane.gameObject);

        // A restart while trails fade.
        var restart = Make("pp/restart/die0");
        Run("pp/restart/die0", restart, 14);
        restart.Simulate(Step, true, true, false);
        RecordState("pp/restart/die0/restarted", restart);
        Done(restart);
    }

    /// <summary>Played over frames at a fixed capture delta: state, count and the stop callback.</summary>
    IEnumerator Played()
    {
        Time.captureDeltaTime = 1f / 20f;
        var played = new List<(string, ParticleSystem)>();
        foreach (bool die in new[] { false, true })
        {
            string key = "played/" + (die ? "die1" : "die0");
            var ps = Make(key, s =>
            {
                var t = s.trails; t.dieWithParticles = die;
                var m = s.main; m.stopAction = ParticleSystemStopAction.Callback;
                // No camera sees it: keep it simulating.
                m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            });
            ps.gameObject.AddComponent<Listener>().tag = key;
            played.Add((key, ps));
        }
        yield return null;
        foreach (var (_, ps) in played) ps.Play();
        for (int frame = 0; frame < 40; frame++)
        {
            yield return null;
            foreach (var (key, ps) in played)
            {
                Record(key + "/" + frame + "/state", new List<float>
                {
                    ps.particleCount, ps.IsAlive(true) ? 1 : 0, ps.isPlaying ? 1 : 0, ps.isEmitting ? 1 : 0, ps.isStopped ? 1 : 0,
                    s_Events.Contains(key + " OnParticleSystemStopped") ? 1 : 0,
                });
            }
        }
        foreach (var (_, ps) in played) Done(ps);
        Time.captureDeltaTime = 0f;
    }

    /// <summary>Renders the scene from (0, 0, -3) into PARTICLE_TRAILS_IMAGES/name.png.</summary>
    static void Capture(string name)
    {
        if (string.IsNullOrEmpty(s_Images)) return;
        var go = new GameObject("ImageCamera");
        var camera = go.AddComponent<Camera>();
        go.transform.position = new Vector3(0f, 0f, -3f);
        camera.fieldOfView = 40f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var target = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(ImageSize, ImageSize, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        Directory.CreateDirectory(s_Images);
        File.WriteAllBytes(Path.Combine(s_Images, name + ".png"), texture.EncodeToPNG());
        DestroyImmediate(go);
        DestroyImmediate(target);
        DestroyImmediate(texture);
    }

    /// <summary>Fading trails drawn: a trail width and alpha that fall along the trail, captured
    /// while particles die and after the last has died.</summary>
    static void Images()
    {
        foreach (var (name, die, worldTrail) in new[] { ("die0", false, false), ("die1", true, false), ("world", false, true) })
        {
            var ps = Make("img/" + name, s =>
            {
                var t = s.trails; t.dieWithParticles = die; t.worldSpace = worldTrail;
                t.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, Curve(0f, 1f, 1f, 0.2f));
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.6f, 0.2f), 0f), new GradientColorKey(new Color(0.2f, 0.6f, 1f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                t.colorOverTrail = g;
                var m = s.main; m.startSize = 0.3f;
            });
            for (int i = 0; i < 30; i++)
            {
                if (worldTrail) ps.transform.position = new Vector3(0.03f * i, 0f, 0f);
                ps.Simulate(Step, true, i == 0, false);
                if (i == 9 || i == 13 || i == 17 || i == 21 || i == 25) Capture(name + "_" + i);
            }
            Done(ps);
        }
    }

    void Start() => StartCoroutine(Probe());

    IEnumerator Probe()
    {
        yield return null;
        s_ParticleMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")) { mainTexture = SoftTexture() };
        s_TrailMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Additive")) { mainTexture = Texture2D.whiteTexture };
        var cameraObject = new GameObject("BakeCamera");
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        s_BakeCamera = cameraObject.AddComponent<Camera>();
        s_BakeCamera.enabled = false;
        s_Mesh = new Mesh();
        Simulations();
        Images();
        yield return Played();
        Record("complete", new List<float> { 1f });
        File.WriteAllText(s_Path, s_Out.ToString());
        Debug.Log("particle trails complete: " + s_Path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Records the stop callback of the object it is on.</summary>
    class Listener : MonoBehaviour
    {
        public new string tag;
        void OnParticleSystemStopped() => s_Events.Add(tag + " OnParticleSystemStopped");
    }
}
