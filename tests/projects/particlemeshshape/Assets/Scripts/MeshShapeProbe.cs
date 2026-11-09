using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records where the Shape module's Mesh, MeshRenderer and SkinnedMeshRenderer shapes emit: per
/// particle the start position, the start velocity (speed 1 along the emission direction), the
/// start colour, the 3D rotation and the seed's halves, for the Vertex, Edge and Triangle mesh
/// types with Random, Loop and Ping-Pong spawn modes, spread, Single Material, mesh colours,
/// normal offset, align to direction, the shape transform, renderer transforms, a skinned pose,
/// meshes without normals, colours or triangles and a mesh made non-readable. Also how a system
/// built with AddComponent looks before activation (its renderer). Each record is one JSON line
/// {"key", "value": [numbers]} in <c>PARTICLE_MESH_SHAPE_OUT</c>; the messages Unity logs go to
/// the same file's directory as <c>messages.txt</c> and are counted per scenario.
/// </summary>
public class MeshShapeProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly StringBuilder s_Messages = new StringBuilder();
    static readonly ParticleSystem.Particle[] s_Buffer = new ParticleSystem.Particle[1024];
    static string s_Path;
    /// <summary>Per scenario, which messages were logged while it ran (some come after its
    /// record): 1 the invalid-mesh warning, 2 the zero-surface warning, 4 another warning, 8 an error.</summary>
    static readonly List<(string Key, int Flags)> s_Flags = new List<(string, int)>();
    static Material s_Material;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        s_Path = Environment.GetEnvironmentVariable("PARTICLE_MESH_SHAPE_OUT");
        if (string.IsNullOrEmpty(s_Path)) return;
        new GameObject("MeshShapeProbe").AddComponent<MeshShapeProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static void Record(string key, IList<float> values)
    {
        s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Count; i++) s_Out.Append(i > 0 ? "," : "").Append(F(values[i]));
        s_Out.Append("]}\n");
    }

    static void OnLog(string message, string stack, LogType type)
    {
        int flag;
        if (type == LogType.Warning)
            flag = message.Contains("is not valid") ? 1 : message.Contains("zero surface area") ? 2 : 4;
        else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) flag = 8;
        else return;
        if (s_Flags.Count > 0) s_Flags[s_Flags.Count - 1] = (s_Flags[s_Flags.Count - 1].Key, s_Flags[s_Flags.Count - 1].Flags | flag);
        s_Messages.Append(type).Append(": ").Append(message.Replace('\n', ' ')).Append('\n');
    }

    // ------------------------------------------------------------------ meshes

    /// <summary>Five used vertices and one unused, three triangles of different areas in two
    /// submeshes (0: triangles 0 and 1, 1: triangle 2), tilted normals and distinct colours.</summary>
    static Mesh Tris(bool normals = true, bool colors = true)
    {
        var m = new Mesh { name = "tris" };
        m.vertices = new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 1f, 0f),
            new Vector3(2f, 1f, 0.5f), new Vector3(-1f, -2f, 1f), new Vector3(5f, 5f, 5f),
        };
        if (normals)
            m.normals = new[]
            {
                new Vector3(0f, 0f, -1f), new Vector3(0.6f, 0f, -0.8f), new Vector3(0f, 0.6f, -0.8f),
                new Vector3(0.48f, 0.6f, -0.64f), new Vector3(-0.6f, -0.8f, 0f), new Vector3(0f, 1f, 0f),
            };
        if (colors)
            m.colors32 = new[]
            {
                new Color32(255, 0, 0, 255), new Color32(0, 255, 0, 200), new Color32(0, 0, 255, 100),
                new Color32(255, 255, 0, 255), new Color32(0, 255, 255, 50), new Color32(128, 128, 128, 255),
            };
        m.subMeshCount = 2;
        m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        m.SetTriangles(new[] { 0, 1, 4 }, 1);
        return m;
    }

    /// <summary>A 3x3 vertex grid of eight triangles (one zero-area: a repeated corner), for
    /// longer Loop/Ping-Pong walks and the degenerate case.</summary>
    static Mesh Grid()
    {
        var m = new Mesh { name = "grid" };
        var v = new List<Vector3>();
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++) v.Add(new Vector3(x, y * 0.5f, (x * y) * 0.25f));
        v.Add(new Vector3(1f, 0.5f, 0.25f)); // 9: coincident with 4
        m.SetVertices(v);
        m.SetTriangles(new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5, 3, 6, 4, 4, 6, 7, 4, 7, 5, 5, 7, 8, 4, 9, 4 }, 0);
        m.RecalculateNormals();
        return m;
    }

    static Mesh Lines()
    {
        var m = new Mesh { name = "lines" };
        m.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f) };
        m.SetIndices(new[] { 0, 1, 1, 2 }, MeshTopology.Lines, 0);
        return m;
    }

    static Mesh Points()
    {
        var m = new Mesh { name = "points" };
        m.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f) };
        m.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Points, 0);
        return m;
    }

    static Mesh Skinned()
    {
        var m = Tris();
        m.name = "skinned";
        var weights = new BoneWeight[6];
        for (int i = 0; i < 6; i++)
        {
            weights[i].boneIndex0 = i % 2;
            weights[i].weight0 = 1f;
        }
        m.boneWeights = weights;
        m.bindposes = new[] { Matrix4x4.identity, Matrix4x4.Translate(new Vector3(-1f, 0f, 0f)) };
        var deltas = new Vector3[6];
        for (int i = 0; i < 6; i++) deltas[i] = new Vector3(0f, 0f, 2f);
        m.AddBlendShapeFrame("push", 100f, deltas, null, null);
        return m;
    }

    // ------------------------------------------------------------------ systems

    /// <summary>A stopped, non-looping system in local space: constant start values (lifetime 10,
    /// speed 1, white), the given mesh shape, a burst of <paramref name="burst"/> at 0 and no rate.</summary>
    static ParticleSystem Make(string name, ParticleSystemShapeType type, ParticleSystemMeshShapeType meshType, Mesh mesh, int burst = 24, float rate = 0f)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = 1234u;
        var main = ps.main;
        main.playOnAwake = false;
        main.duration = 5f;
        main.loop = false;
        main.maxParticles = 1000;
        main.startLifetime = 10f;
        main.startSpeed = 1f;
        main.startSize = 1f;
        main.startColor = Color.white;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        emission.SetBursts(burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)burst) } : new ParticleSystem.Burst[0]);
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = type;
        shape.meshShapeType = meshType;
        if (type == ParticleSystemShapeType.Mesh) shape.mesh = mesh;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = s_Material;
        go.SetActive(true);
        return ps;
    }

    /// <summary>time, count, then per particle: position, velocity, 3D rotation and the seed's
    /// halves; the start colours (bytes) go to a record of their own, key + "/color".</summary>
    static void RecordParticles(string key, ParticleSystem ps)
    {
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { ps.time, n };
        var colors = new List<float> { n };
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            v.Add(p.position.x); v.Add(p.position.y); v.Add(p.position.z);
            v.Add(p.velocity.x); v.Add(p.velocity.y); v.Add(p.velocity.z);
            v.Add(p.rotation3D.x); v.Add(p.rotation3D.y); v.Add(p.rotation3D.z);
            v.Add(p.randomSeed & 0xffff); v.Add(p.randomSeed >> 16);
            var c = p.startColor;
            colors.Add(c.r); colors.Add(c.g); colors.Add(c.b); colors.Add(c.a);
        }
        Record(key, v);
        Record(key + "/color", colors);
    }

    /// <summary>Simulates the system from its start with each call (seconds, fixed steps),
    /// recording after each, then destroys it.</summary>
    static void Run(string key, ParticleSystem ps, params float[] calls)
    {
        s_Flags.Add((key, 0));
        s_Messages.Append("# ").Append(key).Append('\n');
        if (calls.Length == 0) calls = new[] { 0.05f };
        for (int i = 0; i < calls.Length; i++)
        {
            ps.Simulate(calls[i], true, i == 0, true);
            RecordParticles(calls.Length == 1 ? key : key + "/" + i, ps);
        }
        Destroy(ps.gameObject);
    }

    static readonly ParticleSystemMeshShapeType[] s_Types =
        { ParticleSystemMeshShapeType.Vertex, ParticleSystemMeshShapeType.Edge, ParticleSystemMeshShapeType.Triangle };

    static string T(ParticleSystemMeshShapeType t) => t.ToString().ToLowerInvariant();

    static void MeshScenarios()
    {
        var tris = Tris();
        var grid = Grid();
        // A control outside mesh shapes: align to direction on a sphere (every direction) and a cone.
        foreach (var type in new[] { ParticleSystemShapeType.Sphere, ParticleSystemShapeType.Cone })
        {
            var ps = Make("align", type, ParticleSystemMeshShapeType.Vertex, null);
            var s = ps.shape;
            s.alignToDirection = true;
            Run("align/" + type.ToString().ToLowerInvariant(), ps);
        }
        foreach (var t in s_Types)
        {
            string k = T(t);
            // Random: a burst (one batch), a rate over steps, and Emit from script.
            Run("random/" + k + "/burst", Make("a", ParticleSystemShapeType.Mesh, t, tris));
            Run("random/" + k + "/rate", Make("b", ParticleSystemShapeType.Mesh, t, tris, 0, 100f), 0.1f, 0.1f);
            var e = Make("c", ParticleSystemShapeType.Mesh, t, tris, 0);
            s_Flags.Add(("random/" + k + "/emit", 0));
            e.Simulate(0f, true, true, true);
            e.Emit(13);
            RecordParticles("random/" + k + "/emit", e);
            Destroy(e.gameObject);
            Run("random/" + k + "/grid", Make("d", ParticleSystemShapeType.Mesh, t, grid, 40));
            Run("random/" + k + "/seed", WithSeed(Make("e", ParticleSystemShapeType.Mesh, t, tris), 99u));

            // Loop and Ping-Pong: rate 20 over 1.6 s in steps of 0.1 s, speeds and spreads.
            foreach (var mode in new[] { ParticleSystemShapeMultiModeValue.Loop, ParticleSystemShapeMultiModeValue.PingPong })
                foreach (var (speed, spread) in new[] { (1f, 0f), (0.7f, 0f), (1f, 0.25f), (0f, 0f) })
                {
                    var ps = Make("f", ParticleSystemShapeType.Mesh, t, grid, 0, 20f);
                    var s = ps.shape;
                    s.meshSpawnMode = mode;
                    s.meshSpawnSpeed = speed;
                    s.meshSpawnSpread = spread;
                    Run($"{mode.ToString().ToLowerInvariant()}/{k}/{F(speed)}/{F(spread)}", ps, 0.4f, 0.4f, 0.4f, 0.4f);
                }

            // Loop over a mesh with an unused vertex, whole and with Single Material submesh 1.
            foreach (int index in new[] { -1, 1 })
            {
                var ps = Make("f2", ParticleSystemShapeType.Mesh, t, tris, 0, 20f);
                var s = ps.shape;
                s.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
                s.useMeshMaterialIndex = index >= 0;
                s.meshMaterialIndex = Math.Max(index, 0);
                Run("loop/" + k + "/tris/" + index, ps, 0.8f);
            }

            // Single Material: submesh 1, submesh 0, a missing submesh.
            foreach (int index in new[] { 1, 0, 5 })
            {
                var ps = Make("g", ParticleSystemShapeType.Mesh, t, tris);
                var s = ps.shape;
                s.useMeshMaterialIndex = true;
                s.meshMaterialIndex = index;
                Run("material/" + k + "/" + index, ps);
            }

            // Mesh colours off; normal offset; no normals; no colours (no renderer material).
            var off = Make("h", ParticleSystemShapeType.Mesh, t, tris);
            var so = off.shape;
            so.useMeshColors = false;
            Run("nocolors/" + k, off);
            var normal = Make("i", ParticleSystemShapeType.Mesh, t, tris);
            var sn = normal.shape;
            sn.normalOffset = 0.5f;
            Run("normaloffset/" + k, normal);
            Run("nonormals/" + k, Make("j", ParticleSystemShapeType.Mesh, t, Tris(false, false)));

            // Align to direction (rotation3D), the shape's own transform, randomized direction.
            var align = Make("k", ParticleSystemShapeType.Mesh, t, tris);
            var sa = align.shape;
            sa.alignToDirection = true;
            Run("align/" + k, align);
            var moved = Make("l", ParticleSystemShapeType.Mesh, t, tris);
            var sm = moved.shape;
            sm.position = new Vector3(1f, -2f, 0.5f);
            sm.rotation = new Vector3(30f, 45f, 10f);
            sm.scale = new Vector3(2f, 0.5f, 1.5f);
            Run("transform/" + k, moved);
            var randomized = Make("m", ParticleSystemShapeType.Mesh, t, tris);
            var sr = randomized.shape;
            sr.randomDirectionAmount = 0.5f;
            sr.randomPositionAmount = 0.25f;
            Run("modifiers/" + k, randomized);

            // Meshes with nothing of the kind: a degenerate-only grid part, lines, points, empty.
            Run("lines/" + k, Make("n", ParticleSystemShapeType.Mesh, t, Lines(), 6));
            Run("points/" + k, Make("o", ParticleSystemShapeType.Mesh, t, Points(), 6));
            Run("empty/" + k, Make("p", ParticleSystemShapeType.Mesh, t, new Mesh { name = "empty" }, 6));
            Run("nomesh/" + k, Make("q", ParticleSystemShapeType.Mesh, t, null, 6));
            var unreadable = Tris();
            unreadable.UploadMeshData(true);
            Run("unreadable/" + k, Make("r", ParticleSystemShapeType.Mesh, t, unreadable, 6));

            Renderers(t, k);
        }
    }

    static ParticleSystem WithSeed(ParticleSystem ps, uint seed)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.randomSeed = seed;
        return ps;
    }

    static void Renderers(ParticleSystemMeshShapeType t, string k)
    {
        // A MeshRenderer moved, rotated and scaled away from the system, in local and world space;
        // the renderer's material colour without vertex colours; its second material slot.
        foreach (var world in new[] { false, true })
        {
            var target = new GameObject("target");
            target.transform.position = new Vector3(3f, 1f, -2f);
            target.transform.rotation = Quaternion.Euler(0f, 90f, 30f);
            target.transform.localScale = new Vector3(2f, 1f, 1f);
            target.AddComponent<MeshFilter>().sharedMesh = Tris(true, false);
            var renderer = target.AddComponent<MeshRenderer>();
            var red = new Material(s_Material) { color = new Color(1f, 0.5f, 0.25f, 0.75f) };
            var blue = new Material(s_Material) { color = new Color(0.1f, 0.2f, 0.9f, 1f) };
            renderer.sharedMaterials = new[] { red, blue };
            var ps = Make("s", ParticleSystemShapeType.MeshRenderer, t, null);
            ps.transform.position = new Vector3(-1f, 0f, 0f);
            ps.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
            var main = ps.main;
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            var s = ps.shape;
            s.meshRenderer = renderer;
            Run("meshrenderer/" + (world ? "world/" : "local/") + k, ps);
            var slot = Make("t", ParticleSystemShapeType.MeshRenderer, t, null);
            var s2 = slot.shape;
            s2.meshRenderer = renderer;
            s2.useMeshMaterialIndex = true;
            s2.meshMaterialIndex = 1;
            Run("meshrenderer/slot1/" + (world ? "world/" : "local/") + k, slot);
            Destroy(target);
        }

        // A SkinnedMeshRenderer: rest pose, a bone moved and turned, a blend shape at half weight.
        foreach (var pose in new[] { "rest", "bone", "blend" })
        {
            var root = new GameObject("skin");
            root.transform.position = new Vector3(0f, 2f, 0f);
            var b0 = new GameObject("b0").transform;
            b0.SetParent(root.transform, false);
            var b1 = new GameObject("b1").transform;
            b1.SetParent(root.transform, false);
            b1.localPosition = new Vector3(1f, 0f, 0f);
            var smr = root.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = Skinned();
            smr.bones = new[] { b0, b1 };
            smr.rootBone = b0;
            smr.sharedMaterial = s_Material;
            if (pose == "bone")
            {
                b1.localPosition = new Vector3(1f, 1f, 0f);
                b1.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            if (pose == "blend") smr.SetBlendShapeWeight(0, 50f);
            var ps = Make("u", ParticleSystemShapeType.SkinnedMeshRenderer, t, null);
            var s = ps.shape;
            s.skinnedMeshRenderer = smr;
            Run("skinned/" + pose + "/" + k, ps);
            Destroy(root);
        }
    }

    // ------------------------------------------------------------------ shape texture

    /// <summary>256x256, texel (x, y) = (x, y, 128, x): the sampled texel reads back from red and
    /// green, alpha grows to the right (clip controls).</summary>
    static Texture2D CoordTexture()
    {
        var t = new Texture2D(256, 256, TextureFormat.RGBA32, false) { name = "coords", filterMode = FilterMode.Point };
        var px = new Color32[256 * 256];
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++) px[y * 256 + x] = new Color32((byte)x, (byte)y, 128, (byte)x);
        t.SetPixels32(px);
        t.Apply(false, false);
        return t;
    }

    /// <summary>4x4 texels of unrelated colours and alphas, for bilinear filtering and wrap modes.</summary>
    static Texture2D SmallTexture(TextureWrapMode wrap)
    {
        var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "small", wrapMode = wrap, filterMode = FilterMode.Point };
        var px = new Color32[16];
        for (int i = 0; i < 16; i++)
            px[i] = new Color32((byte)((i * 97 + 13) & 255), (byte)((i * 53 + 200) & 255), (byte)((i * 151 + 7) & 255), (byte)((i * 71 + 90) & 255));
        t.SetPixels32(px);
        t.Apply(false, false);
        return t;
    }

    /// <summary>Two triangles with UVs in channel 0 (a sheared square) and channel 1 (another
    /// layout), so a sampled colour shows the interpolated coordinates.</summary>
    static Mesh UvQuad()
    {
        var m = new Mesh { name = "uvquad" };
        m.vertices = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 1f, 0f), new Vector3(2f, 1.5f, 0f) };
        m.uv = new[] { new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.2f), new Vector2(0.2f, 0.85f), new Vector2(0.95f, 0.95f) };
        m.uv2 = new[] { new Vector2(0.6f, 0.1f), new Vector2(0.1f, 0.3f), new Vector2(0.7f, 0.9f), new Vector2(0.3f, 0.6f) };
        m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        m.RecalculateNormals();
        return m;
    }

    static ParticleSystem Textured(string name, ParticleSystemShapeType type, Texture2D texture, Mesh mesh = null,
        ParticleSystemMeshShapeType meshType = ParticleSystemMeshShapeType.Triangle, int burst = 32)
    {
        var ps = Make(name, type, meshType, mesh, burst);
        var main = ps.main;
        main.startSpeed = 0f;
        var s = ps.shape;
        s.texture = texture;
        return ps;
    }

    static readonly (string Name, ParticleSystemShapeType Type)[] s_TextureShapes =
    {
        ("sphere", ParticleSystemShapeType.Sphere), ("sphereshell", ParticleSystemShapeType.SphereShell),
        ("hemisphere", ParticleSystemShapeType.Hemisphere), ("cone", ParticleSystemShapeType.Cone),
        ("conevolume", ParticleSystemShapeType.ConeVolume), ("box", ParticleSystemShapeType.Box),
        ("boxshell", ParticleSystemShapeType.BoxShell), ("boxedge", ParticleSystemShapeType.BoxEdge),
        ("circle", ParticleSystemShapeType.Circle), ("donut", ParticleSystemShapeType.Donut),
        ("rectangle", ParticleSystemShapeType.Rectangle), ("edge", ParticleSystemShapeType.SingleSidedEdge),
    };

    /// <summary>Where each shape reads the texture, what the clip channel and threshold discard,
    /// what the colour and alpha switches change, bilinear filtering with each wrap mode, the UV
    /// channel of a mesh, and a texture made non-readable. Start speed 0: the recorded position is
    /// the start position.</summary>
    static void TextureScenarios()
    {
        var coords = CoordTexture();
        var quad = UvQuad();
        foreach (var (name, type) in s_TextureShapes)
            Run("texture/uv/" + name, Textured("tx", type, coords));
        foreach (var t in s_Types)
            Run("texture/uv/mesh/" + T(t), Textured("tx", ParticleSystemShapeType.Mesh, coords, quad, t));

        // The shape's size and transform: a larger sphere and cone, a scaled, moved and turned box
        // and circle, a partial arc, a longer edge.
        var big = Textured("tx", ParticleSystemShapeType.Sphere, coords);
        var sb = big.shape; sb.radius = 2.5f;
        Run("texture/size/sphere", big);
        var cone = Textured("tx", ParticleSystemShapeType.ConeVolume, coords);
        var sc = cone.shape; sc.radius = 2f; sc.angle = 40f; sc.length = 3f;
        Run("texture/size/conevolume", cone);
        foreach (var type in new[] { ParticleSystemShapeType.Box, ParticleSystemShapeType.Circle })
        {
            var moved = Textured("tx", type, coords);
            var sm = moved.shape;
            sm.position = new Vector3(1f, -2f, 0.5f);
            sm.rotation = new Vector3(30f, 45f, 10f);
            sm.scale = new Vector3(2f, 0.5f, 1.5f);
            Run("texture/transform/" + type.ToString().ToLowerInvariant(), moved);
        }
        var arc = Textured("tx", ParticleSystemShapeType.Circle, coords);
        var sarc = arc.shape; sarc.arc = 90f; sarc.radius = 3f;
        Run("texture/size/circlearc", arc);
        var edge = Textured("tx", ParticleSystemShapeType.SingleSidedEdge, coords);
        var se = edge.shape; se.radius = 4f;
        Run("texture/size/edge", edge);
        var sphereArc = Textured("tx", ParticleSystemShapeType.Hemisphere, coords);
        var ssa = sphereArc.shape; ssa.arc = 120f; ssa.radius = 2f; ssa.radiusThickness = 0.5f;
        Run("texture/size/hemispherearc", sphereArc);
        var thick = Textured("tx", ParticleSystemShapeType.Cone, coords);
        var sth = thick.shape; sth.radiusThickness = 0.4f; sth.arc = 200f; sth.radius = 1.5f;
        Run("texture/size/conethickness", thick);
        var donut = Textured("tx", ParticleSystemShapeType.Donut, coords);
        var sd = donut.shape; sd.radius = 2f; sd.donutRadius = 0.5f; sd.radiusThickness = 0.3f; sd.arc = 270f;
        Run("texture/size/donut", donut);
        var circleThick = Textured("tx", ParticleSystemShapeType.Circle, coords);
        var sct = circleThick.shape; sct.radiusThickness = 0.4f;
        Run("texture/size/circlethickness", circleThick);

        // Clipping: by alpha (grows with x) at 0.5, by red at 0.25, green at 0.75, thresholds 0 and 1.
        foreach (var (channel, threshold) in new[] { (ParticleSystemShapeTextureChannel.Alpha, 0.5f), (ParticleSystemShapeTextureChannel.Red, 0.25f),
                     (ParticleSystemShapeTextureChannel.Green, 0.75f), (ParticleSystemShapeTextureChannel.Blue, 0.6f),
                     (ParticleSystemShapeTextureChannel.Alpha, 0f), (ParticleSystemShapeTextureChannel.Alpha, 1f) })
        {
            foreach (var (name, type) in new[] { ("rectangle", ParticleSystemShapeType.Rectangle), ("mesh", ParticleSystemShapeType.Mesh) })
            {
                var ps = Textured("tx", type, coords, quad);
                var s = ps.shape;
                s.textureClipChannel = channel;
                s.textureClipThreshold = threshold;
                Run($"texture/clip/{channel.ToString().ToLowerInvariant()}/{F(threshold)}/{name}", ps);
            }
        }
        // Clipping a rate over several steps (how many survive, which seeds they keep) and Emit.
        var rate = Make("tx", ParticleSystemShapeType.Rectangle, ParticleSystemMeshShapeType.Vertex, null, 0, 100f);
        var mr = rate.main; mr.startSpeed = 0f;
        var sr = rate.shape; sr.texture = coords; sr.textureClipThreshold = 0.5f;
        Run("texture/clip/rate", rate, 0.1f, 0.1f);
        var emit = Textured("tx", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 0);
        var sx = emit.shape; sx.textureClipThreshold = 0.5f;
        s_Flags.Add(("texture/clip/emit", 0));
        emit.Simulate(0f, true, true, true);
        emit.Emit(20);
        RecordParticles("texture/clip/emit", emit);
        Destroy(emit.gameObject);
        // Emit counts that are not a multiple of four, from the start and after five live
        // particles, clipped and unclipped (which particles stay, in which order).
        foreach (var (label, first, threshold) in new[] { ("emit10", 0, 0.5f), ("emit5then10", 5, 0.5f), ("emit5then10", 5, 0f) })
        {
            var ps = Textured("tx", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 0);
            var s = ps.shape;
            string key = $"texture/clip/{label}/{F(threshold)}";
            s_Flags.Add((key, 0));
            ps.Simulate(0f, true, true, true);
            s.textureClipThreshold = 0f;
            if (first > 0) ps.Emit(first);
            s.textureClipThreshold = threshold;
            ps.Emit(10);
            RecordParticles(key, ps);
            Destroy(ps.gameObject);
        }
        // Clipping outside Emit(count): Emit with EmitParams (then one step), a ring buffer, the
        // births of a Death sub-emitter, and Death sub-emitters of clipped births.
        {
            var ps = Textured("tx", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 0);
            var s = ps.shape; s.textureClipThreshold = 0.5f;
            s_Flags.Add(("texture/clip/emitparams", 0));
            ps.Simulate(0f, true, true, true);
            ps.Emit(new ParticleSystem.EmitParams(), 10);
            RecordParticles("texture/clip/emitparams/0", ps);
            ps.Simulate(0.02f, true, false, true);
            RecordParticles("texture/clip/emitparams/1", ps);
            Destroy(ps.gameObject);
        }
        {
            var ps = Textured("tx", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 0);
            var m = ps.main; m.maxParticles = 12; m.ringBufferMode = ParticleSystemRingBufferMode.LoopUntilReplaced;
            s_Flags.Add(("texture/clip/ring", 0));
            ps.Simulate(0f, true, true, true);
            ps.Emit(10);
            var s = ps.shape; s.textureClipThreshold = 0.5f;
            ps.Emit(10);
            RecordParticles("texture/clip/ring", ps);
            Destroy(ps.gameObject);
        }
        // Death sub-emitters of births that die at their start: most lanes discarded (the count
        // reaches zero), a lifetime shorter than the step, and a first-step collision kill.
        foreach (var (label, threshold, lifetime, collide) in new[] { ("deathcap", 0.95f, 5f, false), ("deathexpire", 0f, 0.001f, false), ("deathcollide", 0f, 5f, true) })
        {
            var parent = Textured("tx", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 10);
            var pm = parent.main; pm.startLifetime = lifetime; pm.startSpeed = collide ? 40f : 0f;
            var ps0 = parent.shape; ps0.textureClipThreshold = threshold;
            var child = Textured("child", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 1);
            child.transform.SetParent(parent.transform, false);
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            GameObject wall = null;
            if (collide)
            {
                wall = new GameObject("wall");
                wall.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0.2f), Quaternion.Euler(-90f, 0f, 0f));
                var c = parent.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes; c.SetPlane(0, wall.transform);
                c.lifetimeLoss = 1f; c.bounce = 0f;
            }
            string key = "texture/clip/" + label;
            s_Flags.Add((key, 0));
            parent.Simulate(0.02f, true, true, true);
            RecordParticles(key + "/0", parent);
            RecordParticles(key + "/0/child", child);
            Destroy(parent.gameObject);
            if (wall != null) Destroy(wall);
        }
        foreach (var (label, parentClip, childClip) in new[] { ("deathsub", 0.5f, 0f), ("subbirths", 0f, 0.5f) })
        {
            var parent = Textured("tx", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 10);
            var pm = parent.main; pm.startLifetime = 0.03f;
            var ps0 = parent.shape; ps0.textureClipThreshold = parentClip;
            var child = Textured("child", ParticleSystemShapeType.Rectangle, coords, null, ParticleSystemMeshShapeType.Triangle, 6);
            child.transform.SetParent(parent.transform, false);
            var cs = child.shape; cs.textureClipThreshold = childClip;
            var subs = parent.subEmitters; subs.enabled = true;
            subs.AddSubEmitter(child, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
            string key = "texture/clip/" + label;
            s_Flags.Add((key, 0));
            parent.Simulate(0.02f, true, true, true);
            RecordParticles(key + "/0", parent);
            RecordParticles(key + "/0/child", child);
            parent.Simulate(0.04f, true, false, true);
            RecordParticles(key + "/1", parent);
            RecordParticles(key + "/1/child", child);
            Destroy(parent.gameObject);
        }

        // Colour and alpha switches, a start colour, a gradient start colour.
        foreach (var (colour, alpha) in new[] { (false, true), (true, false), (false, false) })
        {
            var ps = Textured("tx", ParticleSystemShapeType.Rectangle, coords);
            var s = ps.shape;
            s.textureColorAffectsParticles = colour;
            s.textureAlphaAffectsParticles = alpha;
            Run($"texture/affects/{(colour ? 1 : 0)}{(alpha ? 1 : 0)}", ps);
        }
        var tinted = Textured("tx", ParticleSystemShapeType.Rectangle, coords);
        var mt = tinted.main; mt.startColor = new Color(0.5f, 0.25f, 1f, 0.6f);
        Run("texture/startcolor", tinted);
        var meshColours = Textured("tx", ParticleSystemShapeType.Mesh, coords, Tris(), ParticleSystemMeshShapeType.Triangle);
        Run("texture/meshcolors", meshColours); // vertex colours and a texture without UVs

        // Bilinear filtering and point sampling of a 4x4 texture, clamped and repeated.
        foreach (var wrap in new[] { TextureWrapMode.Clamp, TextureWrapMode.Repeat })
            foreach (var bilinear in new[] { false, true })
                foreach (var (name, type) in new[] { ("rectangle", ParticleSystemShapeType.Rectangle), ("mesh", ParticleSystemShapeType.Mesh) })
                {
                    var ps = Textured("tx", type, SmallTexture(wrap), quad, ParticleSystemMeshShapeType.Triangle, 48);
                    var s = ps.shape;
                    s.textureBilinearFiltering = bilinear;
                    Run($"texture/filter/{wrap.ToString().ToLowerInvariant()}/{(bilinear ? "bilinear" : "point")}/{name}", ps);
                }

        // UV channels: 1, and 2 which the mesh lacks.
        foreach (int channel in new[] { 1, 2 })
        {
            var ps = Textured("tx", ParticleSystemShapeType.Mesh, coords, quad);
            var s = ps.shape;
            s.textureUVChannel = channel;
            Run("texture/uvchannel/" + channel, ps);
        }

        // A texture made non-readable after upload.
        var locked = CoordTexture();
        locked.Apply(false, true);
        Run("texture/unreadable", Textured("tx", ParticleSystemShapeType.Rectangle, locked));

        // Script API round trip.
        var api = Make("tx", ParticleSystemShapeType.Rectangle, ParticleSystemMeshShapeType.Vertex, null);
        var sa = api.shape;
        var before = new float[] { sa.texture == null ? 1 : 0, (int)sa.textureClipChannel, sa.textureClipThreshold, sa.textureColorAffectsParticles ? 1 : 0,
            sa.textureAlphaAffectsParticles ? 1 : 0, sa.textureBilinearFiltering ? 1 : 0, sa.textureUVChannel };
        sa.texture = coords; sa.textureClipChannel = ParticleSystemShapeTextureChannel.Green; sa.textureClipThreshold = 1.5f;
        sa.textureColorAffectsParticles = false; sa.textureAlphaAffectsParticles = false; sa.textureBilinearFiltering = true; sa.textureUVChannel = 7;
        var after = new float[] { sa.texture == coords ? 1 : 0, (int)sa.textureClipChannel, sa.textureClipThreshold, sa.textureColorAffectsParticles ? 1 : 0,
            sa.textureAlphaAffectsParticles ? 1 : 0, sa.textureBilinearFiltering ? 1 : 0, sa.textureUVChannel };
        sa.textureClipThreshold = -1f; sa.textureUVChannel = -2;
        var bounds = new float[] { sa.textureClipThreshold, sa.textureUVChannel };
        var api2 = new List<float>(before); api2.AddRange(after); api2.AddRange(bounds);
        Record("texture/api", api2);
        Destroy(api.gameObject);
    }

    // ------------------------------------------------------------------ other topologies

    static Mesh Indexed(string name, Vector3[] vertices, params (int[] Indices, MeshTopology Topology)[] submeshes)
    {
        var m = new Mesh { name = name };
        m.vertices = vertices;
        m.subMeshCount = submeshes.Length;
        for (int i = 0; i < submeshes.Length; i++) m.SetIndices(submeshes[i].Indices, submeshes[i].Topology, i);
        return m;
    }

    /// <summary>Meshes whose index data is not a triangle list: six line indices (a whole number of
    /// triangles), lines whose indices read as triangles with area, a line strip, quads, and a
    /// triangle submesh beside a lines submesh.</summary>
    static void LineScenarios()
    {
        var square = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(2f, 1f, 0f), new Vector3(0f, 1f, 0f), new Vector3(1f, 3f, 0f) };
        var meshes = new[]
        {
            ("lines6", Indexed("lines6", square, (new[] { 0, 1, 1, 2, 2, 0 }, MeshTopology.Lines))),
            ("linesarea", Indexed("linesarea", square, (new[] { 0, 1, 2, 3, 0, 2 }, MeshTopology.Lines))),
            ("strip", Indexed("strip", square, (new[] { 0, 1, 2, 3, 4 }, MeshTopology.LineStrip))),
            ("quads", Indexed("quads", square, (new[] { 0, 1, 2, 3 }, MeshTopology.Quads))),
            ("mixed", Indexed("mixed", square, (new[] { 0, 1, 2 }, MeshTopology.Triangles), (new[] { 2, 3, 3, 4, 4, 0 }, MeshTopology.Lines))),
        };
        foreach (var (name, mesh) in meshes)
            foreach (var t in s_Types)
                Run($"topology/{name}/{T(t)}", Make("v", ParticleSystemShapeType.Mesh, t, mesh, 12));
        // Its triangle submesh alone (Single Material 0): whether the lines beside it still make
        // Edge and Vertex sample its triangle as segments.
        foreach (var t in s_Types)
        {
            var ps = Make("w", ParticleSystemShapeType.Mesh, t, meshes[4].Item2, 12);
            var s = ps.shape;
            s.useMeshMaterialIndex = true;
            s.meshMaterialIndex = 0;
            Run($"topology/mixed0/{T(t)}", ps);
        }
        // The mixed mesh's lines submesh alone (Single Material).
        foreach (var t in s_Types)
        {
            var ps = Make("w", ParticleSystemShapeType.Mesh, t, meshes[4].Item2, 12);
            var s = ps.shape;
            s.useMeshMaterialIndex = true;
            s.meshMaterialIndex = 1;
            Run($"topology/mixed1/{T(t)}", ps);
        }
    }

    // ------------------------------------------------------------------ spread steps

    /// <summary>Arc spread (circle), radius spread (edge) and mesh spawn spread (mesh edge) in Loop
    /// mode at speed 1 and spread 0.25: rate 20 births land on spread steps, rate 7 births never do;
    /// Burst Spread with spread 0.3.</summary>
    static void SpreadScenarios()
    {
        var grid = Grid();
        foreach (float rate in new[] { 20f, 7f })
        {
            var circle = Make("x", ParticleSystemShapeType.Circle, ParticleSystemMeshShapeType.Vertex, null, 0, rate);
            var sc = circle.shape;
            sc.arcMode = ParticleSystemShapeMultiModeValue.Loop; sc.arcSpeed = 1f; sc.arcSpread = 0.25f; sc.radiusThickness = 0f;
            Run($"spread/circle/{F(rate)}", circle, 0.4f, 0.4f, 0.4f, 0.4f);
            var edge = Make("x", ParticleSystemShapeType.SingleSidedEdge, ParticleSystemMeshShapeType.Vertex, null, 0, rate);
            var se = edge.shape;
            se.radiusMode = ParticleSystemShapeMultiModeValue.Loop; se.radiusSpeed = 1f; se.radiusSpread = 0.25f;
            Run($"spread/edge/{F(rate)}", edge, 0.4f, 0.4f, 0.4f, 0.4f);
            var mesh = Make("x", ParticleSystemShapeType.Mesh, ParticleSystemMeshShapeType.Edge, grid, 0, rate);
            var sm = mesh.shape;
            sm.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop; sm.meshSpawnSpeed = 1f; sm.meshSpawnSpread = 0.25f;
            Run($"spread/mesh/{F(rate)}", mesh, 0.4f, 0.4f, 0.4f, 0.4f);
        }
        var burstCircle = Make("y", ParticleSystemShapeType.Circle, ParticleSystemMeshShapeType.Vertex, null, 10);
        var bc = burstCircle.shape;
        bc.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread; bc.arcSpread = 0.3f; bc.radiusThickness = 0f;
        Run("spread/circle/burst", burstCircle);
        var burstEdge = Make("y", ParticleSystemShapeType.SingleSidedEdge, ParticleSystemMeshShapeType.Vertex, null, 10);
        var be = burstEdge.shape;
        be.radiusMode = ParticleSystemShapeMultiModeValue.BurstSpread; be.radiusSpread = 0.3f;
        Run("spread/edge/burst", burstEdge);
    }

    /// <summary>The obsolete shape types set from script, without a texture.</summary>
    static void ObsoleteShapes()
    {
#pragma warning disable 618
        foreach (var type in new[] { ParticleSystemShapeType.SphereShell, ParticleSystemShapeType.HemisphereShell, ParticleSystemShapeType.ConeShell,
                     ParticleSystemShapeType.ConeVolumeShell, ParticleSystemShapeType.CircleEdge })
#pragma warning restore 618
            Run("obsolete/" + type.ToString().ToLowerInvariant(), Make("z", type, ParticleSystemMeshShapeType.Vertex, null, 8));
    }

    // ------------------------------------------------------------------ AddComponent (Q146)

    /// <summary>Whether a ParticleSystemRenderer exists right after AddComponent&lt;ParticleSystem&gt;()
    /// on an active and an inactive object, and what its properties return then.</summary>
    static void Components()
    {
        foreach (var active in new[] { true, false })
        {
            var go = new GameObject("component");
            go.SetActive(active);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            var v = new List<float> { r != null ? 1 : 0, go.GetComponents<Component>().Length, ps.isPlaying ? 1 : 0, ps.isEmitting ? 1 : 0 };
            if (r != null)
            {
                v.Add(r.enabled ? 1 : 0);
                v.Add((int)r.renderMode);
                v.Add(r.sharedMaterial != null ? 1 : 0);
                v.Add(r.sharedMaterials.Length);
                v.Add(r.trailMaterial != null ? 1 : 0);
                v.Add((int)r.alignment);
                v.Add(r.minParticleSize);
                v.Add(r.maxParticleSize);
                v.Add(r.sortingFudge);
                v.Add((int)r.sortMode);
                v.Add(r.isVisible ? 1 : 0);
                v.Add(r.GetComponent<ParticleSystem>() == ps ? 1 : 0);
                v.Add(r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off ? 0 : 1);
                v.Add(r.receiveShadows ? 1 : 0);
                v.Add(r.activeVertexStreamsCount);
            }
            go.SetActive(true);
            v.Add(go.GetComponent<ParticleSystemRenderer>() != null ? 1 : 0);
            v.Add(go.GetComponents<ParticleSystemRenderer>().Length);
            Record("component/" + (active ? "active" : "inactive"), v);
            Destroy(go);
        }
        // RequireComponent: removing the renderer while the system exists.
        var g = new GameObject("remove");
        g.AddComponent<ParticleSystem>();
        var renderer = g.GetComponent<ParticleSystemRenderer>();
        Record("component/require", new float[] { renderer != null ? 1 : 0, g.GetComponents<Component>().Length });
        Destroy(g);
    }

    void Start() => StartCoroutine(Probe());

    /// <summary>The Sprite and SpriteRenderer shapes (<c>sprite/*</c>): a full-rect sprite cut from
    /// the coordinate texture (rect 64, 32, 128 x 96, pivot 0.25, 0.75, 32 pixels per unit) for
    /// each mesh type, with the coordinate texture as the shape texture and without it, a missing
    /// UV channel, and a SpriteRenderer moved, turned and scaled, also flipped in x.</summary>
    static void SpriteScenarios()
    {
        var coords = CoordTexture();
        var sprite = Sprite.Create(coords, new Rect(64f, 32f, 128f, 96f), new Vector2(0.25f, 0.75f), 32f, 0, SpriteMeshType.FullRect);
        ParticleSystem Shaped(ParticleSystemShapeType type, ParticleSystemMeshShapeType meshType, Texture2D texture)
        {
            var ps = Make("sprite", type, meshType, null);
            var s = ps.shape;
            if (type == ParticleSystemShapeType.Sprite) s.sprite = sprite;
            s.texture = texture;
            return ps;
        }
        foreach (var t in s_Types)
        {
            Run("sprite/texture/" + T(t), Shaped(ParticleSystemShapeType.Sprite, t, coords));
            Run("sprite/plain/" + T(t), Shaped(ParticleSystemShapeType.Sprite, t, null));
        }
        var missing = Shaped(ParticleSystemShapeType.Sprite, ParticleSystemMeshShapeType.Triangle, coords);
        var sm = missing.shape; sm.textureUVChannel = 1;
        Run("sprite/uvchannel/1", missing);
        foreach (bool flip in new[] { false, true })
        {
            var holder = new GameObject("sprite renderer");
            holder.transform.SetPositionAndRotation(new Vector3(2f, 1f, -0.5f), Quaternion.Euler(10f, 20f, 30f));
            holder.transform.localScale = new Vector3(2f, 0.5f, 1f);
            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.flipX = flip;
            var ps = Shaped(ParticleSystemShapeType.SpriteRenderer, ParticleSystemMeshShapeType.Triangle, coords);
            var s = ps.shape; s.spriteRenderer = renderer;
            Run("sprite/renderer/" + (flip ? "flipx" : "plain"), ps);
            Destroy(holder);
        }
        // Normal offset, the Loop spawn mode, no sprite, and Use Mesh Colors with a tinted
        // SpriteRenderer.
        var offset = Shaped(ParticleSystemShapeType.Sprite, ParticleSystemMeshShapeType.Triangle, null);
        var so = offset.shape; so.normalOffset = 0.5f;
        Run("sprite/normaloffset", offset);
        foreach (var t in new[] { ParticleSystemMeshShapeType.Vertex, ParticleSystemMeshShapeType.Edge })
        {
            var loop = Shaped(ParticleSystemShapeType.Sprite, t, null);
            var sl = loop.shape; sl.meshSpawnMode = ParticleSystemShapeMultiModeValue.Loop;
            Run("sprite/loop/" + T(t), loop);
        }
        foreach (var t in new[] { ParticleSystemMeshShapeType.Vertex, ParticleSystemMeshShapeType.Triangle })
        {
            var none = Make("sprite", ParticleSystemShapeType.Sprite, t, null);
            Run("sprite/none/" + T(t), none);
        }
        var tinted = new GameObject("tinted sprite renderer");
        var tr = tinted.AddComponent<SpriteRenderer>();
        tr.sprite = sprite;
        tr.color = new Color(1f, 0.5f, 0.25f, 0.5f);
        var tps = Shaped(ParticleSystemShapeType.SpriteRenderer, ParticleSystemMeshShapeType.Triangle, null);
        var ts = tps.shape; ts.spriteRenderer = tr; ts.useMeshColors = true;
        Run("sprite/renderer/tinted", tps);
        Destroy(tinted);
    }

    System.Collections.IEnumerator Probe()
    {
        yield return null;
        Application.logMessageReceived += OnLog;
        s_Material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply") ?? Shader.Find("Sprites/Default"));
        try
        {
            Components();
            MeshScenarios();
            TextureScenarios();
            LineScenarios();
            SpreadScenarios();
            ObsoleteShapes();
            SpriteScenarios();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Record("exception", new float[] { 1 });
        }
        foreach (var (key, flags) in s_Flags) Record("messages/" + key, new float[] { flags });
        Record("complete", new float[] { 1 });
        File.WriteAllText(s_Path, s_Out.ToString());
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(s_Path), "messages.txt"), s_Messages.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
