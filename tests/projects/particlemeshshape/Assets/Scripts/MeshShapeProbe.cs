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

    /// <summary>time, count, then per particle: position, velocity,
    /// colour (bytes), 3D rotation and the seed's halves.</summary>
    static void RecordParticles(string key, ParticleSystem ps)
    {
        Array.Clear(s_Buffer, 0, s_Buffer.Length);
        int n = ps.GetParticles(s_Buffer);
        var v = new List<float> { ps.time, n };
        for (int i = 0; i < n; i++)
        {
            var p = s_Buffer[i];
            v.Add(p.position.x); v.Add(p.position.y); v.Add(p.position.z);
            v.Add(p.velocity.x); v.Add(p.velocity.y); v.Add(p.velocity.z);
            var c = p.startColor;
            v.Add(c.r); v.Add(c.g); v.Add(c.b); v.Add(c.a);
            v.Add(p.rotation3D.x); v.Add(p.rotation3D.y); v.Add(p.rotation3D.z);
            v.Add(p.randomSeed & 0xffff); v.Add(p.randomSeed >> 16);
        }
        Record(key, v);
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

    System.Collections.IEnumerator Probe()
    {
        yield return null;
        Application.logMessageReceived += OnLog;
        s_Material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply") ?? Shader.Find("Sprites/Default"));
        try
        {
            Components();
            MeshScenarios();
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
