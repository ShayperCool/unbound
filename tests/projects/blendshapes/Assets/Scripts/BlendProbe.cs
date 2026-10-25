using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Blend shapes: a scripted mesh's (one shape with a frame, one with an in-between frame) API
/// answers and misuses, the vertices, normals and tangents a SkinnedMeshRenderer bakes at weights
/// below, between, at and past the frames; the same for the mesh saved as an asset (Resources/
/// Shaped) and for a model's (Resources/Blob.fbx, made in Blender by Tools/make_blob.py: a sphere
/// with two shape keys on two bones, and an action animating them), together with bones and the
/// imported clip; an image of the model. Writes <c>blend.jsonl</c> and <c>blob.png</c> into
/// <c>BLEND_PROBE_OUT</c>, then quits.
/// </summary>
public class BlendProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("BlendProbe").AddComponent<BlendProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string V4(Vector4 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)},{F(v.w)}]";
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string L<T>(IEnumerable<T> items, Func<T, string> f) => "[" + string.Join(",", items.Select(f)) + "]";
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    /// <summary>A 3x3 grid of vertices facing -z: shape "A" with one frame (100), shape "B" with
    /// frames at 25 and 100 (no tangent deltas).</summary>
    public static Mesh ScriptedMesh()
    {
        var mesh = new Mesh();
        var vertices = new Vector3[9];
        var normals = new Vector3[9];
        var tangents = new Vector4[9];
        for (int i = 0; i < 9; i++)
        {
            vertices[i] = new Vector3(i % 3, i / 3, 0f);
            normals[i] = new Vector3(0f, 0f, -1f);
            tangents[i] = new Vector4(1f, 0f, 0f, 1f);
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.triangles = new[] { 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5, 3, 6, 4, 4, 6, 7, 4, 7, 5, 5, 7, 8 };
        var dv = new Vector3[9];
        var dn = new Vector3[9];
        var dt = new Vector3[9];
        for (int i = 0; i < 9; i++)
        {
            dv[i] = new Vector3(0f, 0f, -(i + 1) * 0.1f);
            dn[i] = new Vector3(0.1f * i, 0f, 0f);
            dt[i] = new Vector3(0f, 0.05f * i, 0f);
        }
        mesh.AddBlendShapeFrame("A", 100f, dv, dn, dt);
        var b25 = new Vector3[9];
        var b100 = new Vector3[9];
        var n100 = new Vector3[9];
        for (int i = 0; i < 9; i++)
        {
            b25[i] = new Vector3(0.2f, 0f, 0f);
            b100[i] = new Vector3(0.2f, 0.4f * (i % 3), 0f);
            n100[i] = new Vector3(0f, 0.3f, 0f);
        }
        mesh.AddBlendShapeFrame("B", 25f, b25, null, null);
        mesh.AddBlendShapeFrame("B", 100f, b100, n100, null);
        return mesh;
    }

    void OnLog(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);

    string Logs()
    {
        var text = L(m_Logs, Q);
        m_Logs.Clear();
        return text;
    }

    string Shapes(Mesh mesh)
    {
        var shapes = new List<string>();
        for (int s = 0; s < mesh.blendShapeCount; s++)
        {
            int frames = mesh.GetBlendShapeFrameCount(s);
            var weights = new List<string>();
            for (int f = 0; f < frames; f++) weights.Add(F(mesh.GetBlendShapeFrameWeight(s, f)));
            shapes.Add($"{{\"name\":{Q(mesh.GetBlendShapeName(s))},\"index\":{mesh.GetBlendShapeIndex(mesh.GetBlendShapeName(s))},\"frames\":{frames},\"weights\":[{string.Join(",", weights)}]}}");
        }
        return "[" + string.Join(",", shapes) + "]";
    }

    /// <summary>The vertices in the order of their positions, then uvs (models may order
    /// vertices their own way; vertices at one position differ by uv).</summary>
    static int[] Order(Mesh mesh)
    {
        var positions = mesh.vertices;
        var uvs = mesh.uv;
        Vector2 Uv(int i) => uvs.Length == positions.Length ? uvs[i] : Vector2.zero;
        return Enumerable.Range(0, positions.Length)
            .OrderBy(i => Math.Round(positions[i].x, 5)).ThenBy(i => Math.Round(positions[i].y, 5)).ThenBy(i => Math.Round(positions[i].z, 5))
            .ThenBy(i => Math.Round(Uv(i).x, 4)).ThenBy(i => Math.Round(Uv(i).y, 4))
            .ToArray();
    }

    /// <summary>Each frame's deltas, vertex by vertex in the order of <see cref="Order"/>.</summary>
    string Deltas(Mesh mesh)
    {
        var order = Order(mesh);
        var positions = mesh.vertices;
        var frames = new List<string>();
        var dv = new Vector3[positions.Length];
        var dn = new Vector3[positions.Length];
        var dt = new Vector3[positions.Length];
        for (int s = 0; s < mesh.blendShapeCount; s++)
            for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
            {
                mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                frames.Add($"{{\"shape\":{s},\"frame\":{f},\"vertices\":{L(order, i => V(dv[i]))},\"normals\":{L(order, i => V(dn[i]))},\"tangents\":{L(order, i => V(dt[i]))}}}");
            }
        return "[" + string.Join(",", frames) + "]";
    }

    /// <summary>A baked mesh's vertices, normals and tangents in the order of the source mesh's
    /// base positions.</summary>
    static string Baked(SkinnedMeshRenderer renderer)
    {
        var order = Order(renderer.sharedMesh);
        var baked = new Mesh();
        renderer.BakeMesh(baked);
        var v = baked.vertices;
        var n = baked.normals;
        var t = baked.tangents;
        string text = $"\"vertices\":{L(order, i => V(v[i]))},\"normals\":{(n.Length > 0 ? L(order, i => V(n[i])) : "[]")},\"tangents\":{(t.Length > 0 ? L(order, i => V4(t[i])) : "[]")}";
        Destroy(baked);
        return text;
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("BLEND_PROBE_OUT") ?? "/tmp/blend";
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += OnLog;

        // The scripted mesh.
        var scripted = ScriptedMesh();
        Record("api", $"\"count\":{scripted.blendShapeCount},\"shapes\":{Shapes(scripted)},\"missing\":{scripted.GetBlendShapeIndex("missing")},\"deltas\":{Deltas(scripted)},\"logs\":{Logs()}");

        // Misuse, each on a fresh mesh.
        var misuse = new (string name, Action<Mesh> act)[]
        {
            ("lower weight", m => m.AddBlendShapeFrame("B", 10f, new Vector3[9], null, null)),
            ("same weight", m => m.AddBlendShapeFrame("A", 100f, new Vector3[9], null, null)),
            ("zero weight", m => m.AddBlendShapeFrame("Z", 0f, new Vector3[9], null, null)),
            ("negative weight", m => m.AddBlendShapeFrame("N", -50f, new Vector3[9], null, null)),
            ("short deltas", m => m.AddBlendShapeFrame("S", 100f, new Vector3[4], null, null)),
            ("null deltas", m => m.AddBlendShapeFrame("U", 100f, null, null, null)),
            ("short normals", m => m.AddBlendShapeFrame("SN", 100f, new Vector3[9], new Vector3[2], null)),
            ("name out of range", m => m.GetBlendShapeName(7)),
            ("frame count out of range", m => m.GetBlendShapeFrameCount(7)),
            ("frame weight out of range", m => m.GetBlendShapeFrameWeight(0, 3)),
            ("frame vertices out of range", m => m.GetBlendShapeFrameVertices(0, 3, new Vector3[9], null, null)),
            ("frame vertices short", m => m.GetBlendShapeFrameVertices(0, 0, new Vector3[3], null, null)),
            ("clear", m => m.ClearBlendShapes()),
            ("vertices change", m => m.vertices = new Vector3[4]),
            ("vertices same count", m => m.vertices = m.vertices),
        };
        foreach (var (name, act) in misuse)
        {
            var mesh = ScriptedMesh();
            string error = "";
            try { act(mesh); }
            catch (Exception e) { error = e.GetType().Name; }
            Record("misuse", $"\"name\":{Q(name)},\"exception\":{Q(error)},\"count\":{mesh.blendShapeCount},\"shapes\":{Shapes(mesh)},\"logs\":{Logs()}");
            Destroy(mesh);
        }

        // Weights on a SkinnedMeshRenderer (no bones).
        var go = new GameObject("scripted");
        var renderer = go.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = scripted;
        Record("weights", $"\"initial\":[{F(renderer.GetBlendShapeWeight(0))},{F(renderer.GetBlendShapeWeight(1))}],\"logs\":{Logs()}");
        renderer.SetBlendShapeWeight(5, 10f);
        Record("weights", $"\"name\":\"set out of range\",\"get\":{F(renderer.GetBlendShapeWeight(5))},\"logs\":{Logs()}");
        renderer.SetBlendShapeWeight(-1, 10f);
        Record("weights", $"\"name\":\"set negative index\",\"get\":{F(renderer.GetBlendShapeWeight(-1))},\"logs\":{Logs()}");
        var cases = new (float a, float b)[] { (0, 0), (-50, 0), (50, 0), (100, 0), (150, 0), (0, -10), (0, 10), (0, 25), (0, 50), (0, 100), (0, 150), (50, 50) };
        foreach (var (a, b) in cases)
        {
            renderer.SetBlendShapeWeight(0, a);
            renderer.SetBlendShapeWeight(1, b);
            Record("bake", $"\"mesh\":\"scripted\",\"weights\":[{F(renderer.GetBlendShapeWeight(0))},{F(renderer.GetBlendShapeWeight(1))}],{Baked(renderer)},\"logs\":{Logs()}");
        }
        Record("source", $"\"vertices\":{L(scripted.vertices, V)},\"normals\":{L(scripted.normals, V)}");

        // The asset.
        var asset = Resources.Load<Mesh>("Shaped");
        Record("asset", $"\"found\":{(asset != null ? "true" : "false")},\"count\":{(asset != null ? asset.blendShapeCount : -1)},\"shapes\":{(asset != null ? Shapes(asset) : "[]")},\"deltas\":{(asset != null ? Deltas(asset) : "[]")},\"logs\":{Logs()}");
        if (asset != null)
        {
            renderer.sharedMesh = asset;
            renderer.SetBlendShapeWeight(0, 30f);
            renderer.SetBlendShapeWeight(1, 60f);
            Record("bake", $"\"mesh\":\"asset\",{Baked(renderer)},\"logs\":{Logs()}");
        }

        // The model.
        var prefab = Resources.Load<GameObject>("Blob");
        var blob = Instantiate(prefab);
        var skinned = blob.GetComponentInChildren<SkinnedMeshRenderer>();
        var model = skinned.sharedMesh;
        Record("model", $"\"renderer\":{Q(skinned.name)},\"bones\":{L(skinned.bones, b => Q(b != null ? b.name : null))},\"count\":{model.blendShapeCount},\"shapes\":{Shapes(model)},\"weights\":{L(Enumerable.Range(0, model.blendShapeCount), i => F(skinned.GetBlendShapeWeight(i)))},\"vertexCount\":{model.vertexCount},\"logs\":{Logs()}");
        Record("model deltas", $"\"deltas\":{Deltas(model)}");
        var modelCases = new (float puff, float squash, float bend)[] { (100, 0, 0), (0, 100, 0), (50, 50, 0), (60, 30, 30) };
        var upper = skinned.bones.FirstOrDefault(b => b != null && b.name == "Upper");
        foreach (var (puff, squash, bend) in modelCases)
        {
            skinned.SetBlendShapeWeight(0, puff);
            skinned.SetBlendShapeWeight(1, squash);
            if (upper != null) upper.localRotation = Quaternion.Euler(bend, 0f, 0f) * upper.localRotation;
            Record("bake", $"\"mesh\":\"model\",\"weights\":[{F(puff)},{F(squash)}],\"bend\":{F(bend)},{Baked(skinned)},\"logs\":{Logs()}");
        }

        // The image: the last case, lit from the front (flat ambient, a plain Standard material;
        // the scripted renderer, which has no material, out of the way).
        go.SetActive(false);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.2f, 1f);
        skinned.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.8f, 0.7f, 0.5f, 1f) };
        var light = new GameObject("light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(30f, 20f, 0f);
        var target = new RenderTexture(160, 120, 24, RenderTextureFormat.ARGB32);
        var camera = new GameObject("camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 1f);
        camera.targetTexture = target;
        camera.transform.position = new Vector3(0f, 0.3f, -5f);
        camera.transform.LookAt(new Vector3(0f, 0f, 0f));
        yield return null;
        camera.Render();
        Save(target, Path.Combine(dir, "blob.png"));

        // The clip.
        var clips = Resources.LoadAll<AnimationClip>("Blob").Where(c => !c.name.StartsWith("__preview__")).ToArray();
        var samples = new List<string>();
        foreach (var clip in clips)
            foreach (var time in new[] { 0f, 0.25f, 0.5f, 1f })
            {
                clip.SampleAnimation(blob, time);
                samples.Add($"{{\"clip\":{Q(clip.name)},\"time\":{F(time)},\"weights\":{L(Enumerable.Range(0, model.blendShapeCount), i => F(skinned.GetBlendShapeWeight(i)))}}}");
            }
        Record("clip", $"\"clips\":{L(clips, c => Q(c.name))},\"lengths\":{L(clips, c => F(c.length))},\"samples\":[{string.Join(",", samples)}],\"logs\":{Logs()}");

        Application.logMessageReceived -= OnLog;
        File.WriteAllText(Path.Combine(dir, "blend.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Save(RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Destroy(texture);
    }
}
