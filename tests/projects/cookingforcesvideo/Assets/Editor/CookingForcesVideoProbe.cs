using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode (see README.md): what MeshCollider cooking options do to a mesh with
/// welded, degenerate and unused vertices (queries against it, its bounds, the options read back,
/// a convex hull, a mesh that is not readable); whether 2D effectors move bodies on other layers
/// by their collider mask, the layer collision matrix and the colliders' layer overrides; and the
/// audio tracks a VideoClip and a prepared VideoPlayer report.
/// </summary>
[InitializeOnLoad]
public static class CookingForcesVideoProbe
{
    const string Variable = "COOKING_FORCES_VIDEO_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<bool> s_Steps;

    static CookingForcesVideoProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Log) s_Logs.Add(type + ": " + message.Split('\n')[0]);
            };
            s_Steps = Steps();
            EditorApplication.update += Tick;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Tick()
    {
        bool more;
        try { more = s_Steps.MoveNext(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message); more = false; }
        if (more) return;
        EditorApplication.update -= Tick;
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Put(string key, object value)
    {
        if (value is bool b) { s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(b ? "true" : "false").Append("}\n"); return; }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"")
            .Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\"}\n");
    }

    /// <summary>The logs since the last call (warnings, errors, exceptions), under a key.</summary>
    static void Logs(string key)
    {
        Put(key + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    static string F(float v, int digits) => v.ToString("F" + digits, CultureInfo.InvariantCulture);
    static string V(Vector3 v, int digits) => "(" + F(v.x, digits) + "," + F(v.y, digits) + "," + F(v.z, digits) + ")";
    static string V(Vector2 v, int digits) => "(" + F(v.x, digits) + "," + F(v.y, digits) + ")";

    static IEnumerator<bool> Steps()
    {
        Cooking();
        yield return true;
        foreach (var _ in Effectors()) yield return true;
        foreach (var _ in Video()) yield return true;
    }

    // ------------------------------------------------------------------ MeshCollider cooking

    /// <summary>A mesh on the y = 0 plane: three triangles over [0,2]x[0,2] sharing a corner given
    /// twice (two vertices at (1,0,1)), a degenerate triangle (three points on the line x = 3)
    /// and a vertex no triangle uses (at (5,5,5)). The triangles face up.</summary>
    static Mesh Probe()
    {
        var mesh = new Mesh { name = "CookingProbe" };
        mesh.vertices = new[]
        {
            new Vector3(0, 0, 0), new Vector3(2, 0, 0), new Vector3(1, 0, 1), new Vector3(1, 0, 1),
            new Vector3(0, 0, 2), new Vector3(2, 0, 2), new Vector3(3, 0, 0), new Vector3(3, 0, 1),
            new Vector3(3, 0, 2), new Vector3(5, 5, 5),
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 3, 5, 5, 2, 4, 6, 7, 8 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static readonly Vector3[] s_Rays =
    {
        new Vector3(1.0f, 0f, 0.3f), new Vector3(1.6f, 0f, 1.0f), new Vector3(1.0f, 0f, 1.7f),
        new Vector3(3.0f, 0f, 1.0f), new Vector3(1.0f, 0f, 1.0f), new Vector3(4.5f, 0f, 4.5f),
    };

    static void Rays(string key, Collider collider)
    {
        var origin = collider.transform.position;
        for (int i = 0; i < s_Rays.Length; i++)
            foreach (var (name, from, direction) in new[] { ("down", new Vector3(0, 3, 0), Vector3.down), ("up", new Vector3(0, -3, 0), Vector3.up) })
            {
                var ray = new Ray(origin + s_Rays[i] + from, direction);
                Put(key + ".ray" + i + "." + name, collider.Raycast(ray, out var hit, 10f)
                    ? "hit d=" + F(hit.distance, 4) + " tri=" + hit.triangleIndex + " n=" + V(hit.normal, 3) + " bary=" + V(hit.barycentricCoordinate, 3)
                    : "miss");
            }
        Put(key + ".bounds", V(collider.bounds.center - origin, 4) + " " + V(collider.bounds.size, 4));
    }

    static void Cooking()
    {
        s_Logs.Clear();
        var mesh = Probe();
        const MeshColliderCookingOptions defaults = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning
            | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;
        var probeObject = new GameObject("DefaultCooking").AddComponent<MeshCollider>();
        Put("cooking.defaultOptions", (int)probeObject.cookingOptions);
        Object.Destroy(probeObject.gameObject);
        var cases = new (string Name, MeshColliderCookingOptions Options)[]
        {
            ("none", MeshColliderCookingOptions.None),
            ("defaults", defaults),
            ("noFaster", defaults & ~MeshColliderCookingOptions.CookForFasterSimulation),
            ("noCleaning", defaults & ~MeshColliderCookingOptions.EnableMeshCleaning),
            ("noWelding", defaults & ~MeshColliderCookingOptions.WeldColocatedVertices),
            ("noMidphase", defaults & ~MeshColliderCookingOptions.UseFastMidphase),
            ("cleaningOnly", MeshColliderCookingOptions.EnableMeshCleaning),
            ("weldingOnly", MeshColliderCookingOptions.WeldColocatedVertices),
            // The obsolete InflateConvexMesh bit (an error to name in 6000.0).
            ("inflate", defaults | (MeshColliderCookingOptions)1),
        };
        float x = 0;
        foreach (var (name, options) in cases)
        {
            foreach (bool convex in new[] { false, true })
            {
                var go = new GameObject("Cooking " + name + (convex ? " convex" : ""));
                go.transform.position = new Vector3(x, 0, 0);
                x += 20;
                var collider = go.AddComponent<MeshCollider>();
                collider.cookingOptions = options;
                collider.convex = convex;
                collider.sharedMesh = mesh;
                string key = "cooking." + name + (convex ? ".convex" : "");
                Put(key + ".options", (int)collider.cookingOptions);
                Rays(key, collider);
                Logs(key);
            }
        }
        // Options changed after the mesh: the collider is cooked again.
        {
            var go = new GameObject("Cooking changed");
            go.transform.position = new Vector3(x, 0, 0);
            x += 20;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.cookingOptions = MeshColliderCookingOptions.None;
            Rays("cooking.changed", collider);
            Logs("cooking.changed");
        }
        // A mesh that is not readable any more (its data uploaded and released).
        {
            var released = Probe();
            released.UploadMeshData(true);
            Put("cooking.released.readable", released.isReadable);
            var go = new GameObject("Cooking released");
            go.transform.position = new Vector3(x, 0, 0);
            x += 20;
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = released;
            Put("cooking.released.mesh", collider.sharedMesh != null ? collider.sharedMesh.name : "null");
            Rays("cooking.released", collider);
            Logs("cooking.released");
        }
        // Physics.BakeMesh before the collider takes the mesh.
        {
            var baked = Probe();
            try
            {
                Physics.BakeMesh(baked.GetInstanceID(), false, MeshColliderCookingOptions.None);
                Put("cooking.baked.call", "ok");
            }
            catch (Exception e)
            {
                Put("cooking.baked.call", e.GetType().Name);
            }
            var go = new GameObject("Cooking baked");
            go.transform.position = new Vector3(x, 0, 0);
            var collider = go.AddComponent<MeshCollider>();
            collider.cookingOptions = MeshColliderCookingOptions.None;
            collider.sharedMesh = baked;
            Rays("cooking.baked", collider);
            Logs("cooking.baked");
        }
    }

    // ------------------------------------------------------------------ 2D effectors

    const int EffectorLayer = 8, LayerA = 9, LayerB = 10;

    static IEnumerable<bool> Effectors()
    {
        Physics2D.simulationMode = SimulationMode2D.Script;
        var variants = new[] { "all", "maskA", "matrix", "excludeB", "receiveB", "sendNotB", "maskAincludeB" };
        var kinds = new[] { "area", "point", "surface", "buoyancy" };
        float x = 0;
        foreach (var kind in kinds)
            foreach (var variant in variants)
            {
                s_Logs.Clear();
                var at = new Vector2(x, 100);
                x += 30;
                var (effector, a, b) = Scene(kind, variant, at);
                if (variant == "matrix") Physics2D.IgnoreLayerCollision(EffectorLayer, LayerB, true);
                var startA = a.position;
                var startB = b.position;
                for (int i = 0; i < 25; i++) Physics2D.Simulate(0.02f);
                if (variant == "matrix") Physics2D.IgnoreLayerCollision(EffectorLayer, LayerB, false);
                string key = "effector." + kind + "." + variant;
                foreach (var (name, body, start) in new[] { ("A", a, startA), ("B", b, startB) })
                {
                    var moved = body.position - start;
                    // Whether the effector acted: the area and point effectors move a body at rest,
                    // the surface carries it along (x), buoyancy keeps it from falling as freely
                    // as it would (0.5 s: 1.2 units).
                    bool acted = kind switch
                    {
                        "surface" => Mathf.Abs(moved.x) > 0.01f,
                        "buoyancy" => moved.y > -0.6f,
                        _ => moved.magnitude > 0.01f,
                    };
                    Put(key + "." + name + ".acted", acted);
                    Put(key + "." + name + ".motion", V(moved, 2) + " v=" + V(body.linearVelocity, 2));
                }
                Logs(key);
                Object.Destroy(effector.gameObject);
                Object.Destroy(a.gameObject);
                Object.Destroy(b.gameObject);
                yield return true;
            }
        Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
    }

    /// <summary>An effector of <paramref name="kind"/> on the effector layer and two dynamic
    /// circles, A on layer 9 and B on layer 10, where it acts on them.</summary>
    static (Effector2D, Rigidbody2D, Rigidbody2D) Scene(string kind, string variant, Vector2 at)
    {
        var host = new GameObject("Effector " + kind + " " + variant) { layer = EffectorLayer };
        host.transform.position = at;
        var box = host.AddComponent<BoxCollider2D>();
        box.usedByEffector = true;
        Effector2D effector;
        bool gravity = false;
        // Apart, so that one body pushed into the other cannot move it.
        Vector2 offsetA = new Vector2(-0.5f, -1f), offsetB = new Vector2(0.5f, 1f);
        switch (kind)
        {
            case "area":
                box.size = new Vector2(4, 4);
                box.isTrigger = true;
                var area = host.AddComponent<AreaEffector2D>();
                area.forceAngle = 0;
                area.forceMagnitude = 10;
                effector = area;
                break;
            case "point":
                box.size = new Vector2(4, 4);
                box.isTrigger = true;
                var point = host.AddComponent<PointEffector2D>();
                point.forceMagnitude = 10;
                effector = point;
                break;
            case "surface":
                box.size = new Vector2(6, 1);
                var surface = host.AddComponent<SurfaceEffector2D>();
                surface.speed = 5;
                effector = surface;
                gravity = true;
                offsetA = new Vector2(-1.5f, 0.75f);
                offsetB = new Vector2(1.5f, 0.75f);
                break;
            default:
                box.size = new Vector2(4, 4);
                box.isTrigger = true;
                var buoyancy = host.AddComponent<BuoyancyEffector2D>();
                buoyancy.surfaceLevel = 1;
                buoyancy.density = 3;
                effector = buoyancy;
                gravity = true;
                // Both under the fluid's surface.
                offsetA = new Vector2(-0.5f, -1f);
                offsetB = new Vector2(0.5f, -0.5f);
                break;
        }
        effector.useColliderMask = variant == "maskA" || variant == "maskAincludeB";
        effector.colliderMask = 1 << LayerA;
        if (variant == "sendNotB") box.forceSendLayers = ~(1 << LayerB);
        var a = Body("A", LayerA, at + offsetA, gravity);
        var b = Body("B", LayerB, at + offsetB, gravity);
        var bCollider = b.GetComponent<Collider2D>();
        if (variant == "excludeB") bCollider.excludeLayers = 1 << EffectorLayer;
        if (variant == "receiveB") bCollider.forceReceiveLayers = ~(1 << EffectorLayer);
        if (variant == "maskAincludeB") bCollider.includeLayers = 1 << EffectorLayer;
        return (effector, a, b);
    }

    static Rigidbody2D Body(string name, int layer, Vector2 at, bool gravity)
    {
        var go = new GameObject("Body " + name) { layer = layer };
        go.transform.position = at;
        go.AddComponent<CircleCollider2D>().radius = 0.25f;
        var body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = gravity ? 1 : 0;
        return body;
    }

    // ------------------------------------------------------------------ VideoClip audio tracks

    static IEnumerable<bool> Video()
    {
        foreach (var path in new[] { "Assets/Video/twotracks.webm", "Assets/Video/silent.webm" })
        {
            s_Logs.Clear();
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
            string key = "video." + Path.GetFileNameWithoutExtension(path);
            Put(key + ".clip.audioTrackCount", clip.audioTrackCount);
            for (ushort i = 0; i < 3; i++)
                Put(key + ".clip.track" + i, "channels=" + clip.GetAudioChannelCount(i) + " rate=" + clip.GetAudioSampleRate(i) + " language=" + clip.GetAudioLanguage(i));
            Logs(key + ".clip");
            var player = new GameObject("Player " + key).AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.renderMode = VideoRenderMode.APIOnly;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.clip = clip;
            Put(key + ".player.before", "tracks=" + player.audioTrackCount + " controlled=" + player.controlledAudioTrackCount + " max=" + VideoPlayer.controlledAudioTrackMaxCount);
            Logs(key + ".player.before");
            player.Prepare();
            for (int i = 0; i < 600 && !player.isPrepared; i++) yield return true;
            Put(key + ".player.prepared", player.isPrepared);
            Put(key + ".player.audioTrackCount", player.audioTrackCount);
            for (ushort i = 0; i < 3; i++)
                Put(key + ".player.track" + i, "channels=" + player.GetAudioChannelCount(i) + " rate=" + player.GetAudioSampleRate(i) + " language=" + player.GetAudioLanguageCode(i)
                    + " enabled=" + (i < player.audioTrackCount ? player.IsAudioTrackEnabled(i).ToString() : "-"));
            Logs(key + ".player");
            Object.Destroy(player.gameObject);
        }
    }
}
