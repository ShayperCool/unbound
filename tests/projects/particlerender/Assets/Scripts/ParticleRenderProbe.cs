using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what a particle system's renderer bakes and how its culling mode updates it:
/// <list type="bullet">
/// <item><c>proc/*</c>: <c>proceduralSimulationSupported</c> for one changed setting at a time,
/// before and after Play, and after a property change while playing.</item>
/// <item><c>bake/*</c>: <c>BakeTexture</c> and <c>BakeTrailsTexture</c>: the return value, whether
/// the passed texture is kept, the textures' size, format and texels, for vertex streams, options,
/// render modes and an empty system.</item>
/// <item><c>stretch/*</c>: the mesh <c>BakeMesh</c> builds for Stretched Billboard particles (length
/// and velocity scales, freeform stretching, rotation with the stretch direction, pivot, 3D size,
/// a transformed and a world-space system, sorting), and images.</item>
/// <item><c>cull/*</c>: systems played over frames at a fixed capture delta while one disabled
/// camera, rendered by the probe every frame, looks at them, away, and back, for every culling mode;
/// and systems the camera never draws (another layer).</item>
/// <item><c>camvel/*</c>: Stretched Billboard bakes from a camera moving at a constant velocity.</item>
/// </list>
/// Each record is one JSON line {"key", "value": [numbers]} in <c>PARTICLE_RENDER_OUT</c>; images
/// go to <c>PARTICLE_RENDER_IMAGES</c>.
/// </summary>
public class ParticleRenderProbe : MonoBehaviour
{
    const int ImageSize = 128;
    const int HiddenLayer = 8;
    static readonly StringBuilder s_Out = new StringBuilder();
    static string s_Path, s_Images;
    static Camera s_BakeCamera, s_SideCamera;
    static Material s_ParticleMaterial, s_TrailMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_RENDER_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        s_Images = Environment.GetEnvironmentVariable("PARTICLE_RENDER_IMAGES");
        new GameObject("ParticleRenderProbe").AddComponent<ParticleRenderProbe>();
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
                float u = x / 31f, v = y / 31f;
                float d = new Vector2(x - 15.5f, y - 15.5f).magnitude / 16f;
                // Distinct corners show the uv orientation in images.
                t.SetPixel(x, y, new Color(u, v, 1f - u * 0.5f, Mathf.Clamp01(1.2f - d * d)));
            }
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply();
        return t;
    }

    /// <summary>A system that emits nothing by itself; particles come from <see cref="Emit"/>.</summary>
    static ParticleSystem Make(string name, Action<ParticleSystem> configure = null)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 777u;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 1f;
        main.loop = false;
        main.maxParticles = 64;
        main.startLifetime = 5f;
        main.startSpeed = 0f;
        main.startSize = 0.3f;
        var shape = ps.shape;
        shape.enabled = false;
        var emission = ps.emission;
        emission.enabled = false;
        configure?.Invoke(ps);
        go.SetActive(true);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = s_ParticleMaterial;
        renderer.trailMaterial = s_TrailMaterial;
        return ps;
    }

    static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float rotation = 0f, float lifetime = 5f,
        Vector3? size3D = null, Color32? color = null)
    {
        var e = new ParticleSystem.EmitParams
        {
            position = position, velocity = velocity, startLifetime = lifetime, rotation = rotation,
            startColor = color ?? new Color32(255, 200, 150, 255), randomSeed = 1234u,
        };
        if (size3D.HasValue) e.startSize3D = size3D.Value; else e.startSize = size;
        ps.Emit(e, 1);
    }

    /// <summary>Four particles: moving right, slowly up, away from the bake camera, and at rest; the
    /// second rotated, the last two of different sizes.</summary>
    static void EmitFour(ParticleSystem ps, bool size3D = false)
    {
        Emit(ps, new Vector3(-1f, 0.5f, 0f), new Vector3(2f, 0f, 0f), 0.3f, 0f, 5f, size3D ? new Vector3(0.2f, 0.6f, 1f) : (Vector3?)null);
        Emit(ps, new Vector3(1f, 0.5f, 0f), new Vector3(0f, 0.5f, 0f), 0.4f, 30f, 4f);
        Emit(ps, new Vector3(-1f, -0.5f, 0f), new Vector3(0.3f, 0.2f, 3f), 0.25f, 0f, 3f);
        Emit(ps, new Vector3(1f, -0.5f, 0f), Vector3.zero, 0.5f, 60f, 2f);
    }

    static void Done(ParticleSystem ps) => DestroyImmediate(ps.gameObject);

    static readonly List<Vector4> s_Uvs = new List<Vector4>();

    /// <summary>The baked mesh: vertex count, positions, normals present, uv0 (xyzw), colors, indices.</summary>
    static void RecordMesh(string key, Mesh mesh)
    {
        var vertices = mesh.vertices;
        var v = new List<float> { vertices.Length };
        foreach (var p in vertices) { v.Add(p.x); v.Add(p.y); v.Add(p.z); }
        Record(key + "/positions", v);
        s_Uvs.Clear();
        mesh.GetUVs(0, s_Uvs);
        var u = new List<float> { s_Uvs.Count };
        foreach (var p in s_Uvs) { u.Add(p.x); u.Add(p.y); u.Add(p.z); u.Add(p.w); }
        Record(key + "/uv0", u);
        var normals = mesh.normals;
        var n = new List<float> { normals.Length };
        foreach (var p in normals) { n.Add(p.x); n.Add(p.y); n.Add(p.z); }
        Record(key + "/normals", n);
        var colors = mesh.colors32;
        var c = new List<float> { colors.Length };
        foreach (var p in colors) { c.Add(p.r); c.Add(p.g); c.Add(p.b); c.Add(p.a); }
        Record(key + "/colors", c);
        var triangles = mesh.triangles;
        var t = new List<float> { triangles.Length };
        foreach (var i in triangles) t.Add(i);
        Record(key + "/indices", t);
    }

    static void BakeMesh(string key, ParticleSystem ps, Camera camera, ParticleSystemBakeMeshOptions options = ParticleSystemBakeMeshOptions.Default)
    {
        var mesh = new Mesh();
        ps.GetComponent<ParticleSystemRenderer>().BakeMesh(mesh, camera, options);
        RecordMesh(key, mesh);
        DestroyImmediate(mesh);
    }

    /// <summary>Whether the texture exists, then width, height, format, mip count, then every texel's
    /// RGBA as GetPixels returns it (row by row from the bottom).</summary>
    static void RecordTexture(string key, Texture2D t, int texels = int.MaxValue)
    {
        if (t == null) { Record(key, new List<float> { 0 }); return; }
        var v = new List<float> { 1, t.width, t.height, (int)t.format, t.mipmapCount, t.isReadable ? 1 : 0 };
        if (t.isReadable)
        {
            var pixels = t.GetPixels();
            for (int i = 0; i < pixels.Length && i < texels; i++) { var p = pixels[i]; v.Add(p.r); v.Add(p.g); v.Add(p.b); v.Add(p.a); }
        }
        Record(key, v);
    }

    /// <summary>The floats a vertex of the mesh takes in a baked texture: each attribute's
    /// dimension, a color (four bytes) counting one.</summary>
    static int Stride(Mesh mesh)
    {
        int n = 0;
        foreach (var a in mesh.GetVertexAttributes()) n += a.format == UnityEngine.Rendering.VertexAttributeFormat.UNorm8 ? a.dimension / 4 : a.dimension;
        return n;
    }

    /// <summary>The texture sizes BakeTexture picks for systems of several capacities and particle
    /// counts, render modes and meshes: vertices (width, height, format), the return value, indices
    /// (width, height, format).</summary>
    static void BakeSizes()
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var meshes = new[] { (Mesh)null, quad.GetComponent<MeshFilter>().sharedMesh, cube.GetComponent<MeshFilter>().sharedMesh };
        DestroyImmediate(quad);
        DestroyImmediate(cube);
        foreach (int max in new[] { 1, 4, 10, 100, 1000 })
            foreach (int count in new[] { 1, 3, 4, 30 })
            {
                if (count > max) continue;
                for (int m = 0; m < meshes.Length; m++)
                    foreach (var options in new[] { ParticleSystemBakeTextureOptions.PerVertex, ParticleSystemBakeTextureOptions.PerParticle | ParticleSystemBakeTextureOptions.IncludeParticleIndices })
                    {
                        var ps = Make("bakesize", s => { var main = s.main; main.maxParticles = max; });
                        var r = ps.GetComponent<ParticleSystemRenderer>();
                        if (meshes[m] != null) { r.renderMode = ParticleSystemRenderMode.Mesh; r.mesh = meshes[m]; }
                        for (int i = 0; i < count; i++) Emit(ps, new Vector3(i * 0.1f, 0f, 0f), Vector3.zero, 0.2f);
                        Texture2D v = null, ix = null;
                        int n = r.BakeTexture(ref v, ref ix, s_BakeCamera, options);
                        Record("bakesize/" + max + "/" + count + "/" + m + "/" + (int)options, new List<float>
                        {
                            v != null ? v.width : -1, v != null ? v.height : -1, v != null ? (int)v.format : -1, n,
                            ix != null ? ix.width : -1, ix != null ? ix.height : -1, ix != null ? (int)ix.format : -1,
                        });
                        Done(ps);
                    }
            }
    }

    // ------------------------------------------------------------------ procedural

    static void Procedural()
    {
        var cases = new List<(string, Action<ParticleSystem>)>
        {
            ("default", s => { }),
            ("loop", s => { var m = s.main; m.loop = true; }),
            ("emitting", s => { var e = s.emission; e.enabled = true; e.rateOverTime = 10f; var sh = s.shape; sh.enabled = true; }),
            ("worldspace", s => { var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World; }),
            ("rateoverdistance", s => { var e = s.emission; e.enabled = true; e.rateOverDistance = 5f; }),
            ("gravity", s => { var m = s.main; m.gravityModifier = 0.5f; }),
            ("gravitycurve", s => { var m = s.main; m.gravityModifier = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)); }),
            ("startdelay", s => { var m = s.main; m.startDelay = 0.5f; }),
            ("prewarm", s => { var m = s.main; m.loop = true; m.prewarm = true; }),
            ("simspeed", s => { var m = s.main; m.simulationSpeed = 2f; }),
            ("velocity", s => { var v = s.velocityOverLifetime; v.enabled = true; v.x = 1f; }),
            ("velocityorbital", s => { var v = s.velocityOverLifetime; v.enabled = true; v.orbitalZ = 1f; }),
            ("limitvelocity", s => { var v = s.limitVelocityOverLifetime; v.enabled = true; v.limit = 1f; }),
            ("inheritvelocity", s => { var v = s.inheritVelocity; v.enabled = true; v.curveMultiplier = 1f; }),
            ("lifetimebyspeed", s => { var v = s.lifetimeByEmitterSpeed; v.enabled = true; }),
            ("force", s => { var f = s.forceOverLifetime; f.enabled = true; f.x = 1f; }),
            ("forcerandomized", s => { var f = s.forceOverLifetime; f.enabled = true; f.x = new ParticleSystem.MinMaxCurve(0f, 1f); f.randomized = true; }),
            ("color", s => { var c = s.colorOverLifetime; c.enabled = true; }),
            ("colorbyspeed", s => { var c = s.colorBySpeed; c.enabled = true; }),
            ("size", s => { var c = s.sizeOverLifetime; c.enabled = true; c.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f)); }),
            ("sizebyspeed", s => { var c = s.sizeBySpeed; c.enabled = true; }),
            ("rotation", s => { var c = s.rotationOverLifetime; c.enabled = true; }),
            ("rotationbyspeed", s => { var c = s.rotationBySpeed; c.enabled = true; }),
            ("externalforces", s => { var c = s.externalForces; c.enabled = true; }),
            ("noise", s => { var c = s.noise; c.enabled = true; }),
            ("collision", s => { var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; }),
            ("collisionplanes", s => { var c = s.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; }),
            ("trigger", s => { var c = s.trigger; c.enabled = true; }),
            ("subemitters", s => { var c = s.subEmitters; c.enabled = true; }),
            ("texturesheet", s => { var c = s.textureSheetAnimation; c.enabled = true; }),
            ("lights", s => { var c = s.lights; c.enabled = true; }),
            ("trails", s => { var c = s.trails; c.enabled = true; }),
            ("customdata", s => { var c = s.customData; c.enabled = true; }),
            ("shapemesh", s => { var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Mesh; }),
            ("startspeedcurve", s => { var m = s.main; m.startSpeed = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)); }),
            ("stopaction", s => { var m = s.main; m.stopAction = ParticleSystemStopAction.Callback; }),
            ("unscaled", s => { var m = s.main; m.useUnscaledTime = true; }),
            ("maxparticles", s => { var m = s.main; m.maxParticles = 3; }),
            ("customspace", s => { var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.Custom; }),
            ("stopdisable", s => { var m = s.main; m.stopAction = ParticleSystemStopAction.Disable; }),
            ("stopdestroy", s => { var m = s.main; m.stopAction = ParticleSystemStopAction.Destroy; }),
            ("gravitytwo", s => { var m = s.main; m.gravityModifier = new ParticleSystem.MinMaxCurve(0.2f, 0.8f); }),
            ("velocitycurve", s => { var v = s.velocityOverLifetime; v.enabled = true; v.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)); v.y = 0f; v.z = 0f; }),
            ("forcecurve", s => { var f = s.forceOverLifetime; f.enabled = true; f.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f)); f.y = 0f; f.z = 0f; }),
            ("velocityspeed", s => { var v = s.velocityOverLifetime; v.enabled = true; v.speedModifier = 2f; }),
            ("velocityradial", s => { var v = s.velocityOverLifetime; v.enabled = true; v.radial = 1f; }),
            ("rotationbyspeedoff", s => { var c = s.rotationBySpeed; c.enabled = false; c.z = 1f; }),
            ("collisiondisabled", s => { var c = s.collision; c.enabled = false; }),
            ("ringbuffer", s => { var m = s.main; m.ringBufferMode = ParticleSystemRingBufferMode.PauseUntilReplaced; }),
            ("emittervelocity", s => { var m = s.main; m.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Rigidbody; }),
        };
        foreach (var (name, configure) in cases)
        {
            var ps = Make("proc/" + name, configure);
            float a = ps.proceduralSimulationSupported ? 1 : 0;
            ps.Play();
            float b = ps.proceduralSimulationSupported ? 1 : 0;
            ps.Simulate(0.1f, true, false, false);
            float c = ps.proceduralSimulationSupported ? 1 : 0;
            Record("proc/" + name, new List<float> { a, b, c });
            Done(ps);
        }

        // A change while playing (a setting that keeps procedural mode) and after a stop.
        var played = Make("proc/changed", s => { var m = s.main; m.loop = true; });
        played.Play();
        float before = played.proceduralSimulationSupported ? 1 : 0;
        var main = played.main;
        main.startSize = 0.6f;
        float afterSize = played.proceduralSimulationSupported ? 1 : 0;
        played.Simulate(0.1f, true, false, false);
        float afterStep = played.proceduralSimulationSupported ? 1 : 0;
        played.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        float afterStop = played.proceduralSimulationSupported ? 1 : 0;
        played.Play();
        float replay = played.proceduralSimulationSupported ? 1 : 0;
        Record("proc/changed", new List<float> { before, afterSize, afterStep, afterStop, replay });
        Done(played);
    }

    // ------------------------------------------------------------------ bake textures

    static void BakeTextures()
    {
        var ps = Make("bake/billboard");
        ps.transform.position = new Vector3(0.5f, -0.25f, 1f);
        ps.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
        ps.transform.localScale = new Vector3(2f, 1f, 1f);
        EmitFour(ps);
        ps.Simulate(0.1f, true, false, false);
        var renderer = ps.GetComponent<ParticleSystemRenderer>();

        void Bake(string key, ParticleSystemBakeTextureOptions options, bool indices, Texture2D vertices = null, Texture2D index = null)
        {
            var v = vertices; var i = index;
            var givenV = v; var givenI = i;
            int n = indices ? renderer.BakeTexture(ref v, ref i, s_BakeCamera, options) : renderer.BakeTexture(ref v, s_BakeCamera, options);
            Record(key + "/result", new List<float> { n, givenV != null && ReferenceEquals(v, givenV) ? 1 : 0, givenI != null && ReferenceEquals(i, givenI) ? 1 : 0 });
            RecordTexture(key + "/vertices", v);
            if (indices) RecordTexture(key + "/indices", i);
        }

        var all = (ParticleSystemBakeTextureOptions[])Enum.GetValues(typeof(ParticleSystemBakeTextureOptions));
        Bake("bake/default", ParticleSystemBakeTextureOptions.Default, false);
        Bake("bake/default/indices", ParticleSystemBakeTextureOptions.Default, true);
        Bake("bake/pervertex/position", ParticleSystemBakeTextureOptions.PerVertex | ParticleSystemBakeTextureOptions.BakePosition, false);
        Bake("bake/pervertex/transform", ParticleSystemBakeTextureOptions.PerVertex | ParticleSystemBakeTextureOptions.BakePosition | ParticleSystemBakeTextureOptions.BakeRotationAndScale, true);
        Bake("bake/perparticle", ParticleSystemBakeTextureOptions.PerParticle, true);
        Bake("bake/perparticle/transform", ParticleSystemBakeTextureOptions.PerParticle | ParticleSystemBakeTextureOptions.BakePosition | ParticleSystemBakeTextureOptions.BakeRotationAndScale, true);
        Bake("bake/perparticle/indices", ParticleSystemBakeTextureOptions.PerParticle | ParticleSystemBakeTextureOptions.IncludeParticleIndices, true);
        Bake("bake/pervertex/indices", ParticleSystemBakeTextureOptions.PerVertex | ParticleSystemBakeTextureOptions.IncludeParticleIndices, true);
        Bake("bake/none", (ParticleSystemBakeTextureOptions)0, true);
        Bake("bake/both", ParticleSystemBakeTextureOptions.PerVertex | ParticleSystemBakeTextureOptions.PerParticle, true);
        // Textures passed in: kept or replaced, and resized?
        Bake("bake/given", ParticleSystemBakeTextureOptions.Default, true,
            new Texture2D(2, 2, TextureFormat.RGBA32, false), new Texture2D(3, 1, TextureFormat.RGBA32, false));
        Bake("bake/givenfloat", ParticleSystemBakeTextureOptions.Default, true,
            new Texture2D(64, 8, TextureFormat.RGBAFloat, false), new Texture2D(64, 8, TextureFormat.RGBAFloat, false));

        // Custom vertex streams, per vertex and per particle.
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Center, ParticleSystemVertexStream.Velocity,
            ParticleSystemVertexStream.SizeXYZ, ParticleSystemVertexStream.Rotation, ParticleSystemVertexStream.AgePercent,
            ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.Custom1XYZW,
        });
        ps.SetCustomParticleData(new List<Vector4> { new Vector4(0.1f, 0.2f, 0.3f, 0.4f), new Vector4(1, 2, 3, 4), new Vector4(5, 6, 7, 8), new Vector4(-1, -2, -3, -4) },
            ParticleSystemCustomData.Custom1);
        Bake("bake/streams/pervertex", ParticleSystemBakeTextureOptions.PerVertex, true);
        Bake("bake/streams/perparticle", ParticleSystemBakeTextureOptions.PerParticle, true);
        Bake("bake/streams/perparticle/indices", ParticleSystemBakeTextureOptions.PerParticle | ParticleSystemBakeTextureOptions.IncludeParticleIndices, false);
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Tangent, ParticleSystemVertexStream.UV2,
            ParticleSystemVertexStream.AnimBlend, ParticleSystemVertexStream.Speed, ParticleSystemVertexStream.Custom1X,
        });
        Bake("bake/streams2/pervertex", ParticleSystemBakeTextureOptions.PerVertex, true);
        Bake("bake/streams2/perparticle", ParticleSystemBakeTextureOptions.PerParticle, false);
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });

        // Other render modes: stretched, mesh.
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        Bake("bake/stretch", ParticleSystemBakeTextureOptions.Default, true);
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        renderer.mesh = cube.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(cube);
        Bake("bake/mesh", ParticleSystemBakeTextureOptions.Default, true);
        Bake("bake/mesh/perparticle", ParticleSystemBakeTextureOptions.PerParticle, true);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        // An empty system.
        ps.Clear();
        Bake("bake/empty", ParticleSystemBakeTextureOptions.Default, true);
        Bake("bake/empty/given", ParticleSystemBakeTextureOptions.Default, true,
            new Texture2D(2, 2, TextureFormat.RGBA32, false), new Texture2D(2, 2, TextureFormat.RGBA32, false));
        Done(ps);

        // Trails: per-particle trails of two moving particles, and a ribbon.
        foreach (var mode in new[] { ParticleSystemTrailMode.PerParticle, ParticleSystemTrailMode.Ribbon })
        {
            string key = "bake/trails/" + (mode == ParticleSystemTrailMode.Ribbon ? "ribbon" : "perparticle");
            var tps = Make(key, s =>
            {
                var t = s.trails; t.enabled = true; t.mode = mode; t.lifetime = 1f; t.minVertexDistance = 0.1f;
                var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World;
            });
            var tr = tps.GetComponent<ParticleSystemRenderer>();
            Emit(tps, new Vector3(-1f, 0f, 0f), new Vector3(1f, 0.5f, 0f), 0.3f);
            Emit(tps, new Vector3(1f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), 0.3f);
            Emit(tps, new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f), 0.3f);
            for (int i = 0; i < 6; i++) tps.Simulate(0.1f, true, false, false);
            var baked = new Mesh();
            tr.BakeTrailsMesh(baked, s_BakeCamera, ParticleSystemBakeMeshOptions.Default);
            // Unity leaves the texels past the baked data as they were in its scratch memory: only
            // the data is recorded (a vertex per trail vertex, or per trail point per particle).
            int vertexCount = baked.vertexCount, stride = Stride(baked);
            DestroyImmediate(baked);
            foreach (var (name, options) in new[]
            {
                ("default", ParticleSystemBakeTextureOptions.Default),
                ("perparticle", ParticleSystemBakeTextureOptions.PerParticle),
                ("indices", ParticleSystemBakeTextureOptions.PerVertex | ParticleSystemBakeTextureOptions.IncludeParticleIndices),
            })
            {
                Texture2D v = null, i = null;
                int n = tr.BakeTrailsTexture(ref v, ref i, s_BakeCamera, options);
                Record(key + "/" + name + "/result", new List<float> { n });
                bool perParticle = (options & ParticleSystemBakeTextureOptions.PerParticle) != 0;
                RecordTexture(key + "/" + name + "/vertices", v, (perParticle ? vertexCount / 2 : vertexCount) * stride);
                RecordTexture(key + "/" + name + "/indices", i, n);
            }
            var mesh = new Mesh();
            tr.BakeTrailsMesh(mesh, s_BakeCamera, ParticleSystemBakeMeshOptions.Default);
            RecordMesh(key + "/mesh", mesh);
            DestroyImmediate(mesh);
            Done(tps);
        }
    }

    // ------------------------------------------------------------------ stretched billboards

    static ParticleSystem MakeStretch(string name, Action<ParticleSystemRenderer> configure, Action<ParticleSystem> system = null, bool size3D = false)
    {
        var ps = Make(name, s =>
        {
            if (size3D) { var m = s.main; m.startSize3D = true; }
            system?.Invoke(s);
        });
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 2f;
        r.velocityScale = 0f;
        r.cameraVelocityScale = 0f;
        configure?.Invoke(r);
        EmitFour(ps, size3D);
        ps.Simulate(0.1f, true, false, false);
        return ps;
    }

    static readonly (string, Action<ParticleSystemRenderer>, Action<ParticleSystem>, bool)[] s_StretchCases =
    {
        ("base", null, null, false),
        ("velocity", r => { r.lengthScale = 1f; r.velocityScale = 0.25f; }, null, false),
        ("length0", r => { r.lengthScale = 0f; r.velocityScale = 0.5f; }, null, false),
        ("negative", r => { r.lengthScale = -1f; r.velocityScale = 0.1f; }, null, false),
        ("freeform", r => { r.freeformStretching = true; r.rotateWithStretchDirection = true; }, null, false),
        ("freeform_norot", r => { r.freeformStretching = true; r.rotateWithStretchDirection = false; }, null, false),
        ("norot", r => { r.freeformStretching = false; r.rotateWithStretchDirection = false; }, null, false),
        ("freeform_velocity", r => { r.freeformStretching = true; r.lengthScale = 0.5f; r.velocityScale = 0.4f; }, null, false),
        ("pivot", r => { r.pivot = new Vector3(0.5f, 0.25f, 0.3f); }, null, false),
        ("pivot_freeform", r => { r.pivot = new Vector3(0.5f, 0.25f, 0.3f); r.freeformStretching = true; }, null, false),
        ("pivot_length1", r => { r.lengthScale = 1f; r.velocityScale = 0.3f; r.pivot = new Vector3(0.5f, 0.25f, 0f); }, null, false),
        ("size3d", null, null, true),
        ("size3d_freeform", r => { r.freeformStretching = true; }, null, true),
        ("transform", null, s => { s.transform.position = new Vector3(0.2f, 0.1f, 0.5f); s.transform.rotation = Quaternion.Euler(10f, 20f, 30f); s.transform.localScale = new Vector3(1.5f, 0.5f, 2f); }, false),
        ("transform_freeform", r => { r.freeformStretching = true; }, s => { s.transform.position = new Vector3(0.2f, 0.1f, 0.5f); s.transform.rotation = Quaternion.Euler(10f, 20f, 30f); s.transform.localScale = new Vector3(1.5f, 0.5f, 2f); }, false),
        ("world", null, s => { var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World; s.transform.position = new Vector3(0.5f, 0f, 0f); s.transform.rotation = Quaternion.Euler(0f, 0f, 45f); }, false),
        ("minsize", r => { r.minParticleSize = 0.2f; }, null, false),
        ("maxsize", r => { r.maxParticleSize = 0.02f; }, null, false),
        ("flip", r => { r.flip = new Vector3(1f, 1f, 0f); }, null, false),
        ("sort_distance", r => { r.sortMode = ParticleSystemSortMode.Distance; }, null, false),
        ("sort_oldest", r => { r.sortMode = ParticleSystemSortMode.OldestInFront; }, null, false),
        ("sort_youngest", r => { r.sortMode = ParticleSystemSortMode.YoungestInFront; }, null, false),
        ("sort_distance_freeform", r => { r.sortMode = ParticleSystemSortMode.Distance; r.freeformStretching = true; }, null, false),
    };

    static void Stretched()
    {
        foreach (var (name, configure, system, size3D) in s_StretchCases)
        {
            var ps = MakeStretch("stretch/" + name, configure, system, size3D);
            BakeMesh("stretch/" + name + "/front", ps, s_BakeCamera);
            BakeMesh("stretch/" + name + "/side", ps, s_SideCamera);
            BakeMesh("stretch/" + name + "/front/transform", ps, s_BakeCamera, ParticleSystemBakeMeshOptions.BakePosition | ParticleSystemBakeMeshOptions.BakeRotationAndScale);
            if (name.Contains("freeform"))
            {
                // Baked again after a camera has drawn the system.
                var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
                s_BakeCamera.targetTexture = target;
                s_BakeCamera.Render();
                s_BakeCamera.targetTexture = null;
                DestroyImmediate(target);
                BakeMesh("stretch/" + name + "/rendered", ps, s_BakeCamera);
            }
            Done(ps);
        }
        foreach (var name in new[] { "base", "velocity", "freeform", "pivot", "size3d_freeform", "transform" })
        {
            foreach (var (n, configure, system, size3D) in s_StretchCases)
            {
                if (n != name) continue;
                var ps = MakeStretch("img/" + n, configure, system, size3D);
                Capture("stretch_" + n, new Vector3(0f, 0f, -4f), Quaternion.identity);
                Capture("stretch_" + n + "_side", s_SideCamera.transform.position * 0.5f, s_SideCamera.transform.rotation);
                Done(ps);
            }
        }
    }

    /// <summary>Renders the scene into PARTICLE_RENDER_IMAGES/name.png.</summary>
    static void Capture(string name, Vector3 position, Quaternion rotation)
    {
        if (string.IsNullOrEmpty(s_Images)) return;
        var go = new GameObject("ImageCamera");
        var camera = go.AddComponent<Camera>();
        go.transform.SetPositionAndRotation(position, rotation);
        camera.fieldOfView = 50f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = ~(1 << HiddenLayer);
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

    // ------------------------------------------------------------------ culling over frames

    static ParticleSystem MakeCulled(string name, ParticleSystemCullingMode mode, bool loop, Vector3 position, int layer,
        Action<ParticleSystem> configure = null)
    {
        var ps = Make(name, s =>
        {
            var m = s.main;
            m.loop = loop;
            m.duration = 1f;
            m.startLifetime = 1.2f;
            m.startSpeed = 1f;
            m.startSize = 0.2f;
            m.cullingMode = mode;
            var e = s.emission; e.enabled = true; e.rateOverTime = 10f;
            var sh = s.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
            configure?.Invoke(s);
        });
        ps.transform.position = position;
        ps.gameObject.layer = layer;
        return ps;
    }

    /// <summary>The state, the visibility, and the sum of the particles' remaining lifetimes and
    /// positions (enough to see an update or a catch-up).</summary>
    static ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[256];

    static void RecordCulled(string key, ParticleSystem ps)
    {
        int n = ps.GetParticles(s_Buffer);
        float life = 0f; var pos = Vector3.zero;
        for (int i = 0; i < n; i++) { life += s_Buffer[i].remainingLifetime; pos += s_Buffer[i].position; }
        var r = ps.GetComponent<ParticleSystemRenderer>();
        Record(key, new List<float>
        {
            ps.particleCount, ps.time, ps.isPlaying ? 1 : 0, ps.isPaused ? 1 : 0, ps.isEmitting ? 1 : 0, ps.isStopped ? 1 : 0,
            ps.IsAlive(true) ? 1 : 0, r.isVisible ? 1 : 0, life, pos.x, pos.y, pos.z, ps.proceduralSimulationSupported ? 1 : 0,
        });
    }

    IEnumerator Culling()
    {
        Time.captureDeltaTime = 1f / 20f;
        var cameraObject = new GameObject("CullCamera");
        cameraObject.transform.position = new Vector3(0f, 0f, -12f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 60f;
        camera.cullingMask = ~(1 << HiddenLayer);
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;

        var systems = new List<(string, ParticleSystem)>();
        var cases = new List<(string, ParticleSystemCullingMode, bool, Action<ParticleSystem>)>
        {
            ("automatic_loop", ParticleSystemCullingMode.Automatic, true, null),
            ("automatic_once", ParticleSystemCullingMode.Automatic, false, s => { var m = s.main; m.duration = 4f; }),
            ("automatic_loop_limit", ParticleSystemCullingMode.Automatic, true, s => { var v = s.limitVelocityOverLifetime; v.enabled = true; v.limit = 0.5f; }),
            ("automatic_loop_world", ParticleSystemCullingMode.Automatic, true, s => { var m = s.main; m.simulationSpace = ParticleSystemSimulationSpace.World; }),
            ("pause_loop", ParticleSystemCullingMode.Pause, true, null),
            ("pause_once", ParticleSystemCullingMode.Pause, false, s => { var m = s.main; m.duration = 4f; }),
            ("pause_loop_limit", ParticleSystemCullingMode.Pause, true, s => { var v = s.limitVelocityOverLifetime; v.enabled = true; v.limit = 0.5f; }),
            ("catchup_loop", ParticleSystemCullingMode.PauseAndCatchup, true, null),
            ("catchup_once", ParticleSystemCullingMode.PauseAndCatchup, false, s => { var m = s.main; m.duration = 4f; }),
            ("catchup_loop_limit", ParticleSystemCullingMode.PauseAndCatchup, true, s => { var v = s.limitVelocityOverLifetime; v.enabled = true; v.limit = 0.5f; }),
            ("always_loop", ParticleSystemCullingMode.AlwaysSimulate, true, null),
            ("pause_loop_stretch", ParticleSystemCullingMode.Pause, true, s => { }),
            ("catchup_replay", ParticleSystemCullingMode.PauseAndCatchup, true, null),
        };
        int index = 0;
        foreach (var (name, mode, loop, configure) in cases)
        {
            var position = new Vector3(-5.5f + index % 7 * 1.8f, index < 7 ? 1.5f : -1.5f, 0f);
            var seen = MakeCulled("cull/" + name, mode, loop, position, 0, configure);
            if (name == "pause_loop_stretch") seen.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Stretch;
            systems.Add(("cull/" + name, seen));
            // The same system on a layer the camera never draws.
            var hidden = MakeCulled("hidden/" + name, mode, loop, position, HiddenLayer, configure);
            if (name == "pause_loop_stretch") hidden.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Stretch;
            systems.Add(("hidden/" + name, hidden));
            index++;
        }
        var scriptPaused = systems.Find(s => s.Item1 == "cull/catchup_loop").Item2;
        var replayed = systems.Find(s => s.Item1 == "cull/catchup_replay").Item2;
        yield return null;
        foreach (var (_, ps) in systems) ps.Play();
        for (int frame = 0; frame < 60; frame++)
        {
            yield return null;
            foreach (var (key, ps) in systems) RecordCulled(key + "/" + frame, ps);
            // No camera renders before frame 5; then it looks at the systems until frame 14, away
            // for 15-39, back from 40.
            bool away = frame >= 15 && frame < 40;
            cameraObject.transform.rotation = away ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            if (frame >= 5) camera.Render();
            // Play again while playing and culled, on another system.
            if (frame == 25) replayed.Play();
            // A script pause and resume while the camera looks away, on one system.
            if (frame == 20) scriptPaused.Pause();
            if (frame == 30) scriptPaused.Play();
        }
        foreach (var (_, ps) in systems) Done(ps);
        camera.targetTexture = null;
        DestroyImmediate(target);
        DestroyImmediate(cameraObject);
        Time.captureDeltaTime = 0f;
    }

    /// <summary>Stretched particles baked from a camera moving at a constant velocity, with and
    /// without camera velocity scale.</summary>
    IEnumerator CameraVelocity()
    {
        Time.captureDeltaTime = 1f / 20f;
        var cameraObject = new GameObject("MovingCamera");
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        var still = MakeStretch("camvel/scale0", r => { r.cameraVelocityScale = 0f; });
        var scaled = MakeStretch("camvel/scale", r => { r.cameraVelocityScale = 0.5f; r.lengthScale = 1f; });
        var freeform = MakeStretch("camvel/freeform", r => { r.cameraVelocityScale = 0.5f; r.lengthScale = 1f; r.freeformStretching = true; });
        foreach (var ps in new[] { still, scaled, freeform }) { var m = ps.main; m.simulationSpeed = 0f; ps.Play(); }
        for (int frame = 0; frame < 6; frame++)
        {
            yield return null;
            cameraObject.transform.position = new Vector3(0.1f * frame, 0.05f * frame, -10f);
            camera.Render();
            var v = camera.velocity;
            Record("camvel/" + frame + "/velocity", new List<float> { v.x, v.y, v.z });
            if (frame >= 3)
            {
                BakeMesh("camvel/" + frame + "/scale0", still, camera);
                BakeMesh("camvel/" + frame + "/scale", scaled, camera);
                BakeMesh("camvel/" + frame + "/freeform", freeform, camera);
            }
        }
        foreach (var ps in new[] { still, scaled, freeform }) Done(ps);
        camera.targetTexture = null;
        DestroyImmediate(target);
        DestroyImmediate(cameraObject);
        Time.captureDeltaTime = 0f;
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
        // A fixed aspect: the screen's differs between the Editor and other hosts.
        s_BakeCamera.aspect = 4f / 3f;
        var side = new GameObject("SideCamera");
        side.transform.position = new Vector3(6f, 4f, -6f);
        side.transform.LookAt(Vector3.zero);
        s_SideCamera = side.AddComponent<Camera>();
        s_SideCamera.enabled = false;
        s_SideCamera.aspect = 4f / 3f;
        Procedural();
        BakeTextures();
        BakeSizes();
        Stretched();
        yield return Culling();
        yield return CameraVelocity();
        Record("complete", new List<float> { 1f });
        File.WriteAllText(s_Path, s_Out.ToString());
        Debug.Log("particle render complete: " + s_Path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
