using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The meshes LineRenderer and TrailRenderer generate, read back through <c>BakeMesh</c>, and a
/// few renders of them into float targets.
///
/// Lines (synchronous, one frame): shapes with sharp, reversed, repeated and degenerate points;
/// corner and cap vertex counts; loops; every texture mode and texture scales; width curves and
/// multipliers; color gradients (blend, fixed, perceptual, linear keys); View alignment for
/// orthographic and perspective cameras and TransformZ for rotated transforms; local-space lines
/// with and without the transform applied; lighting data; shadow bias. API values cover the
/// component defaults and the start/end shortcuts.
///
/// Trails (frame-stepped at a fixed capture frame rate): moving objects with several settings,
/// recorded right after each move and at the end of each frame: positions, visible positions and
/// the baked mesh; emitting toggles, autodestruct, Clear, Add/SetPosition(s).
///
/// Blend (last, synchronous): the predeclared short-segment cases of blend_cases.py.
/// </summary>
public partial class LineProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    const int W = 96, H = 64;
    const int ImageLayer = 9;

    readonly StringBuilder records = new StringBuilder();
    readonly List<string> logs = new List<string>();
    bool capturing;
    string directory;
    Camera main, imageOrtho, imagePersp, persp;
    Material colorMat, uvMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("LINE_PROBE_OUT") == null) return;
        Time.captureFramerate = 50;
        new GameObject("LineProbe").AddComponent<LineProbe>();
    }

    // ---- records ----

    void Value(string k, string json) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n"); }
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string B(bool b) => b ? "true" : "false";
    static string F(float f) => float.IsNaN(f) || float.IsInfinity(f) ? Q(f.ToString(CultureInfo.InvariantCulture)) : f.ToString("R", CultureInfo.InvariantCulture);
    static string V2(Vector2 v) => "[" + F(v.x) + "," + F(v.y) + "]";
    static string V3(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string C(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";

    static string Flat(IList<Vector3> vs)
    {
        var s = new StringBuilder("[");
        for (int i = 0; i < vs.Count; i++) s.Append(i > 0 ? "," : "").Append(F(vs[i].x)).Append(',').Append(F(vs[i].y)).Append(',').Append(F(vs[i].z));
        return s.Append(']').ToString();
    }

    void OnLog(string condition, string stack, LogType type)
    {
        if (capturing) logs.Add(type + ": " + condition);
    }

    void BeginLogs() { logs.Clear(); capturing = true; }

    /// <summary>Records the case's log messages when there were any.</summary>
    void EndLogs(string key)
    {
        capturing = false;
        if (logs.Count == 0) return;
        var s = new StringBuilder("[");
        for (int i = 0; i < logs.Count; i++) s.Append(i > 0 ? "," : "").Append(Q(logs[i]));
        Value(key + ".log", s.Append("]").ToString());
        logs.Clear();
    }

    void Flush()
    {
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());
    }

    /// <summary>Every vertex stream of the mesh, its layout, indices and bounds.</summary>
    void MeshValue(string key, Mesh m)
    {
        var s = new StringBuilder("{");
        s.Append("\"vc\":").Append(m.vertexCount).Append(",\"sub\":").Append(m.subMeshCount);
        s.Append(",\"attrs\":[");
        var attrs = m.GetVertexAttributes();
        for (int i = 0; i < attrs.Length; i++)
            s.Append(i > 0 ? "," : "").Append(Q(attrs[i].attribute + ":" + attrs[i].format + ":" + attrs[i].dimension + ":" + attrs[i].stream));
        s.Append("],\"v\":").Append(Flat(m.vertices));
        if (m.HasVertexAttribute(VertexAttribute.Normal)) s.Append(",\"n\":").Append(Flat(m.normals));
        if (m.HasVertexAttribute(VertexAttribute.Tangent))
        {
            s.Append(",\"t\":[");
            var t = m.tangents;
            for (int i = 0; i < t.Length; i++) s.Append(i > 0 ? "," : "").Append(F(t[i].x)).Append(',').Append(F(t[i].y)).Append(',').Append(F(t[i].z)).Append(',').Append(F(t[i].w));
            s.Append(']');
        }
        if (m.HasVertexAttribute(VertexAttribute.Color))
        {
            s.Append(",\"c\":[");
            var c = m.colors;
            for (int i = 0; i < c.Length; i++) s.Append(i > 0 ? "," : "").Append(F(c[i].r)).Append(',').Append(F(c[i].g)).Append(',').Append(F(c[i].b)).Append(',').Append(F(c[i].a));
            s.Append(']');
        }
        var uvs = new List<Vector4>();
        for (int ch = 0; ch < 8; ch++)
        {
            var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
            if (!m.HasVertexAttribute(attribute)) continue;
            int dims = m.GetVertexAttributeDimension(attribute);
            m.GetUVs(ch, uvs);
            s.Append(",\"uv").Append(ch).Append("\":[");
            for (int i = 0; i < uvs.Count; i++)
                for (int d = 0; d < dims; d++) s.Append(i > 0 || d > 0 ? "," : "").Append(F(uvs[i][d]));
            s.Append(']');
        }
        s.Append(",\"idx\":[");
        for (int sm = 0; sm < m.subMeshCount; sm++)
        {
            s.Append(sm > 0 ? "," : "").Append("{\"topo\":").Append(Q(m.GetTopology(sm).ToString())).Append(",\"i\":[");
            var idx = m.GetIndices(sm);
            for (int i = 0; i < idx.Length; i++) s.Append(i > 0 ? "," : "").Append(idx[i]);
            s.Append("]}");
        }
        s.Append("],\"bounds\":[").Append(V3(m.bounds.center)).Append(',').Append(V3(m.bounds.extents)).Append("]}");
        Value(key, s.ToString());
    }

    void Bake(string key, Renderer r, Camera cam, bool useTransform)
    {
        var mesh = new Mesh();
        BeginLogs();
        try
        {
            if (r is LineRenderer line) line.BakeMesh(mesh, cam, useTransform);
            else ((TrailRenderer)r).BakeMesh(mesh, cam, useTransform);
            MeshValue(key, mesh);
        }
        catch (Exception e) { Value(key + ".error", Q(e.GetType().Name)); }
        EndLogs(key);
        DestroyImmediate(mesh);
    }

    void Image(string key, Camera cam)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1, filterMode = FilterMode.Point };
        rt.Create();
        cam.targetTexture = rt;
        BeginLogs();
        cam.Render();
        EndLogs(key);
        cam.targetTexture = null;
        var saved = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
        image.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        image.Apply();
        RenderTexture.active = saved;
        var pixels = image.GetPixels();
        using (var w = new BinaryWriter(File.Create(Path.Combine(directory, key + ".rgba"))))
            foreach (var c in pixels) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
        Value(key + ".pixels", pixels.Length.ToString());
        DestroyImmediate(image);
        rt.Release();
        DestroyImmediate(rt);
    }

    static string Curve(AnimationCurve c)
    {
        var s = new StringBuilder("[");
        var keys = c.keys;
        for (int i = 0; i < keys.Length; i++)
            s.Append(i > 0 ? "," : "").Append('[').Append(F(keys[i].time)).Append(',').Append(F(keys[i].value)).Append(',')
                .Append(F(keys[i].inTangent)).Append(',').Append(F(keys[i].outTangent)).Append(']');
        return s.Append(']').ToString();
    }

    static string GradientKeys(Gradient g)
    {
        var s = new StringBuilder("{\"mode\":").Append(Q(g.mode.ToString())).Append(",\"c\":[");
        var ck = g.colorKeys;
        for (int i = 0; i < ck.Length; i++) s.Append(i > 0 ? "," : "").Append("[").Append(F(ck[i].time)).Append(',').Append(C(ck[i].color)).Append(']');
        s.Append("],\"a\":[");
        var ak = g.alphaKeys;
        for (int i = 0; i < ak.Length; i++) s.Append(i > 0 ? "," : "").Append("[").Append(F(ak[i].time)).Append(',').Append(F(ak[i].alpha)).Append(']');
        return s.Append("]}").ToString();
    }

    // ---- shapes ----

    static Vector3 P(float x, float y, float z = 0f) => new Vector3(x, y, z);

    static readonly Dictionary<string, Vector3[]> Shapes = new Dictionary<string, Vector3[]>
    {
        { "straight", new[] { P(-1, 0), P(0, 0), P(1, 0) } },
        { "L", new[] { P(-1, -0.5f), P(1, -0.5f), P(1, 1) } },
        { "Lneg", new[] { P(-1, 0.5f), P(1, 0.5f), P(1, -1) } },
        { "acute", new[] { P(-1, 0), P(1, 0), P(-0.5f, 0.5f) } },
        { "reverse", new[] { P(-1, 0), P(1, 0), P(-1, 0) } },
        { "halfback", new[] { P(-1, 0), P(1, 0), P(0, 0) } },
        { "obtuse", new[] { P(-1, 0), P(0.5f, 0), P(1.5f, 0.3f) } },
        { "repeat", new[] { P(-1, 0), P(0, 0), P(0, 0), P(1, 1) } },
        { "repeatEnd", new[] { P(-1, 0), P(1, 0), P(1, 0) } },
        { "repeatStart", new[] { P(-1, 0), P(-1, 0), P(1, 0) } },
        { "same", new[] { P(0.5f, 0.5f), P(0.5f, 0.5f), P(0.5f, 0.5f) } },
        { "one", new[] { P(0.25f, 0.5f) } },
        { "two", new[] { P(-1, 0), P(1, 0.5f) } },
        { "tiny", new[] { P(0, 0), P(0.0001f, 0) } },
        { "zigzag", new[] { P(-2, -0.5f), P(-1.4f, 0.6f), P(-0.2f, -0.4f), P(0.3f, 0.8f), P(1.8f, 0.1f) } },
        { "square", new[] { P(-1, -1), P(1, -1), P(1, 1), P(-1, 1) } },
        { "closedSquare", new[] { P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), P(-1, -1) } },
        { "spatial", new[] { P(-1, -0.5f, 0), P(0, 0.5f, 1), P(1, -0.3f, -0.5f), P(1.5f, 0.8f, 0.5f) } },
        { "dupStartTurn", new[] { P(-1, 0), P(-1, 0), P(0, 0), P(1, 1) } },
        { "dupEndTurn", new[] { P(-1, 0), P(0, 0), P(1, 1), P(1, 1) } },
        { "dupMidLong", new[] { P(-2, 0), P(-1, 0), P(0, 0), P(0, 0), P(1, 1), P(2, 1) } },
        { "dupTriple", new[] { P(-1, 0), P(0, 0), P(0, 0), P(0, 0), P(1, 1) } },
        { "dupUneven", new[] { P(-0.5f, 0), P(0, 0), P(0, 0), P(2, 2) } },
        { "dupTwice", new[] { P(-2, 0), P(-1, 0), P(-1, 0), P(0, 0.5f), P(0, 0.5f), P(1, 0) } },
        { "four", new[] { P(-2, 0), P(-1, 0), P(0, 0), P(1, 1) } },
        { "near1e-5", new[] { P(-1, 0), P(0, 0), P(0.00001f, 0), P(1, 1) } },
        { "near1e-3", new[] { P(-1, 0), P(0, 0), P(0.001f, 0), P(1, 1) } },
        { "near1e-2", new[] { P(-1, 0), P(0, 0), P(0.01f, 0), P(1, 1) } },
    };

    readonly List<GameObject> created = new List<GameObject>();

    LineRenderer NewLine(string shape)
    {
        var go = new GameObject("line." + shape) { layer = ImageLayer };
        created.Add(go);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = colorMat;
        line.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        line.widthMultiplier = 0.4f;
        line.colorGradient = WhiteGradient();
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.textureScale = Vector2.one;
        line.loop = false;
        line.useWorldSpace = true;
        line.generateLightingData = false;
        var points = Shapes[shape];
        line.positionCount = points.Length;
        line.SetPositions(points);
        return line;
    }

    static Gradient WhiteGradient()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    static Gradient ThreeKeys(GradientMode mode)
    {
        var g = new Gradient { mode = mode };
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.2f, 0.1f), 0f), new GradientColorKey(new Color(0.1f, 0.9f, 0.3f), 0.4f), new GradientColorKey(new Color(0.2f, 0.3f, 1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0.8f, 1f) });
        return g;
    }

    void Clean()
    {
        foreach (var go in created) if (go != null) DestroyImmediate(go);
        created.Clear();
    }

    void LineCase(string key, string shape, Action<LineRenderer> configure, Camera cam = null, bool useTransform = false, Camera image = null, bool noCamera = false)
    {
        LineRenderer line = null;
        try
        {
            line = NewLine(shape);
            configure?.Invoke(line);
            Bake("line." + key, line, noCamera ? null : cam != null ? cam : main, useTransform);
            if (image != null) Image("img." + key, image);
        }
        catch (Exception e) { Value("line." + key + ".fatal", Q(e.ToString())); }
        Clean();
    }

    // ---- line cases ----

    void Defaults()
    {
        var go = new GameObject("defaults");
        created.Add(go);
        var line = go.AddComponent<LineRenderer>();
        var p = new Vector3[8];
        int n = line.GetPositions(p);
        Value("default.line", "{\"positionCount\":" + line.positionCount + ",\"positions\":" + Flat(new ArraySegment<Vector3>(p, 0, n))
            + ",\"startWidth\":" + F(line.startWidth) + ",\"endWidth\":" + F(line.endWidth) + ",\"widthMultiplier\":" + F(line.widthMultiplier)
            + ",\"widthCurve\":" + Curve(line.widthCurve) + ",\"startColor\":" + C(line.startColor) + ",\"endColor\":" + C(line.endColor)
            + ",\"gradient\":" + GradientKeys(line.colorGradient) + ",\"numCornerVertices\":" + line.numCornerVertices
            + ",\"numCapVertices\":" + line.numCapVertices + ",\"alignment\":" + Q(line.alignment.ToString())
            + ",\"textureMode\":" + Q(line.textureMode.ToString()) + ",\"textureScale\":" + V2(line.textureScale)
            + ",\"shadowBias\":" + F(line.shadowBias) + ",\"generateLightingData\":" + B(line.generateLightingData)
            + ",\"loop\":" + B(line.loop) + ",\"useWorldSpace\":" + B(line.useWorldSpace)
            + ",\"maskInteraction\":" + Q(line.maskInteraction.ToString()) + ",\"materials\":" + line.sharedMaterials.Length + "}");
        Bake("default.line.mesh", line, main, false);

        var tgo = new GameObject("defaultsTrail");
        created.Add(tgo);
        var trail = tgo.AddComponent<TrailRenderer>();
        Value("default.trail", "{\"positionCount\":" + trail.positionCount + ",\"time\":" + F(trail.time)
            + ",\"minVertexDistance\":" + F(trail.minVertexDistance) + ",\"autodestruct\":" + B(trail.autodestruct)
            + ",\"emitting\":" + B(trail.emitting) + ",\"startWidth\":" + F(trail.startWidth) + ",\"endWidth\":" + F(trail.endWidth)
            + ",\"widthMultiplier\":" + F(trail.widthMultiplier) + ",\"widthCurve\":" + Curve(trail.widthCurve)
            + ",\"gradient\":" + GradientKeys(trail.colorGradient) + ",\"numCornerVertices\":" + trail.numCornerVertices
            + ",\"numCapVertices\":" + trail.numCapVertices + ",\"alignment\":" + Q(trail.alignment.ToString())
            + ",\"textureMode\":" + Q(trail.textureMode.ToString()) + ",\"textureScale\":" + V2(trail.textureScale)
            + ",\"shadowBias\":" + F(trail.shadowBias) + ",\"generateLightingData\":" + B(trail.generateLightingData)
            + ",\"maskInteraction\":" + Q(trail.maskInteraction.ToString()) + ",\"materials\":" + trail.sharedMaterials.Length + "}");
        Clean();
    }

    void Shortcuts()
    {
        var go = new GameObject("shortcuts");
        created.Add(go);
        var line = go.AddComponent<LineRenderer>();
        line.startWidth = 0.3f;
        Value("api.startWidth.curve", Curve(line.widthCurve));
        line.endWidth = 0.1f;
        Value("api.endWidth.curve", Curve(line.widthCurve));
        line.widthMultiplier = 2f;
        Value("api.widthMultiplier.widths", "[" + F(line.startWidth) + "," + F(line.endWidth) + "]");
        Value("api.widthMultiplier.curve", Curve(line.widthCurve));
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.25f), new Keyframe(1f, 0.5f));
        line.endWidth = 3f;
        Value("api.endWidth3.curve", Curve(line.widthCurve));
        line.startColor = new Color(1f, 0f, 0f, 0.5f);
        Value("api.startColor.gradient", GradientKeys(line.colorGradient));
        line.endColor = new Color(0f, 0f, 1f, 0.25f);
        Value("api.endColor.gradient", GradientKeys(line.colorGradient));
        line.colorGradient = ThreeKeys(GradientMode.Blend);
        line.endColor = new Color(0.5f, 0.5f, 0.5f, 0.75f);
        Value("api.endColor3.gradient", GradientKeys(line.colorGradient));
        line.numCornerVertices = -3;
        line.numCapVertices = -1;
        Value("api.negativeVertices", "[" + line.numCornerVertices + "," + line.numCapVertices + "]");
        line.positionCount = 2;
        line.positionCount = 5;
        var p = new Vector3[16];
        Value("api.positionCount5", Flat(new ArraySegment<Vector3>(p, 0, line.GetPositions(p))));
        line.SetPositions(new[] { P(1, 2, 3), P(4, 5, 6), P(7, 8, 9), P(1, 1, 1), P(2, 2, 2), P(3, 3, 3), P(4, 4, 4) });
        Value("api.setLonger", "{\"count\":" + line.positionCount + ",\"p\":" + Flat(new ArraySegment<Vector3>(p, 0, line.GetPositions(p))) + "}");
        line.SetPositions(new[] { P(9, 9, 9) });
        Value("api.setShorter", "{\"count\":" + line.positionCount + ",\"p\":" + Flat(new ArraySegment<Vector3>(p, 0, line.GetPositions(p))) + "}");
        BeginLogs();
        Value("api.getInto2", line.GetPositions(new Vector3[2]).ToString());
        EndLogs("api.getInto2");
        BeginLogs();
        try { line.SetPosition(10, Vector3.one); Value("api.setOutOfRange", Q("ok")); }
        catch (Exception e) { Value("api.setOutOfRange", Q(e.GetType().Name)); }
        EndLogs("api.setOutOfRange");
        BeginLogs();
        try { Value("api.getOutOfRange", V3(line.GetPosition(-1))); }
        catch (Exception e) { Value("api.getOutOfRange", Q(e.GetType().Name)); }
        EndLogs("api.getOutOfRange");
        line.positionCount = -2;
        Value("api.negativeCount", line.positionCount.ToString());
        Clean();
    }

    void Lines()
    {
        string[] geometry = { "straight", "L", "Lneg", "acute", "reverse", "halfback", "obtuse", "repeat", "repeatEnd", "repeatStart", "same", "one", "two", "tiny", "zigzag", "spatial" };
        foreach (var shape in geometry)
            foreach (int corners in new[] { 0, 1, 3 })
                foreach (int caps in new[] { 0, 3 })
                    LineCase("geo." + shape + ".c" + corners + ".cap" + caps, shape, l => { l.numCornerVertices = corners; l.numCapVertices = caps; });
        LineCase("geo.zero", "one", l => l.positionCount = 0);

        foreach (var shape in new[] { "L", "square", "closedSquare", "two", "one", "zigzag", "reverse" })
            foreach (int corners in new[] { 0, 2 })
                foreach (int caps in new[] { 0, 2 })
                    LineCase("loop." + shape + ".c" + corners + ".cap" + caps, shape, l => { l.loop = true; l.numCornerVertices = corners; l.numCapVertices = caps; });

        var modes = new[] { LineTextureMode.Stretch, LineTextureMode.Tile, LineTextureMode.DistributePerSegment, LineTextureMode.RepeatPerSegment, LineTextureMode.Static };
        foreach (var mode in modes)
        {
            foreach (var shape in new[] { "zigzag", "repeat" })
                foreach (var scale in new[] { new Vector2(1f, 1f), new Vector2(2f, 0.5f) })
                    foreach (int corners in new[] { 0, 2 })
                        LineCase("tex." + mode + "." + shape + ".s" + scale.x + "x" + scale.y + ".c" + corners, shape,
                            l => { l.textureMode = mode; l.textureScale = scale; l.numCornerVertices = corners; l.numCapVertices = 2; });
            LineCase("tex." + mode + ".zigzag.w1", "zigzag", l => { l.textureMode = mode; l.widthMultiplier = 1f; });
            LineCase("tex." + mode + ".zigzag.curve", "zigzag", l => { l.textureMode = mode; l.widthCurve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2f)); });
            LineCase("tex." + mode + ".loop.square.c2", "square", l => { l.textureMode = mode; l.loop = true; l.numCornerVertices = 2; });
        }

        var bump = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.5f));
        foreach (int corners in new[] { 0, 2 })
            LineCase("width.bump.c" + corners, "zigzag", l => { l.widthCurve = bump; l.widthMultiplier = 0.7f; l.numCornerVertices = corners; l.numCapVertices = 2; });
        LineCase("width.toZero", "L", l => { l.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)); l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("width.negative", "L", l => { l.widthMultiplier = -0.5f; l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("width.zero", "L", l => { l.widthMultiplier = 0f; l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("width.oneKey", "zigzag", l => l.widthCurve = new AnimationCurve(new Keyframe(0.3f, 0.6f)));
        LineCase("width.outside", "zigzag", l => l.widthCurve = new AnimationCurve(new Keyframe(-0.5f, 0.2f), new Keyframe(1.5f, 2f)));
        LineCase("width.startEnd", "zigzag", l => { l.startWidth = 0.1f; l.endWidth = 0.6f; });

        foreach (var mode in new[] { GradientMode.Blend, GradientMode.Fixed, GradientMode.PerceptualBlend })
            LineCase("color." + mode, "zigzag", l => { l.colorGradient = ThreeKeys(mode); l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("color.PerceptualLinear", "zigzag", l => { var g = ThreeKeys(GradientMode.PerceptualBlend); g.colorSpace = ColorSpace.Linear; l.colorGradient = g; });
        LineCase("color.BlendLinear", "zigzag", l => { var g = ThreeKeys(GradientMode.Blend); g.colorSpace = ColorSpace.Linear; l.colorGradient = g; });
        LineCase("color.startEnd", "L", l => { l.startColor = new Color(1f, 0f, 0f, 0.5f); l.endColor = new Color(0f, 0f, 1f, 0.25f); l.numCornerVertices = 2; });
        LineCase("color.loop", "square", l => { l.colorGradient = ThreeKeys(GradientMode.Blend); l.loop = true; l.numCornerVertices = 2; });

        var tilt = Quaternion.Euler(30f, 20f, 0f);
        LineCase("align.TransformZ.L.c2cap2", "L", l => { l.alignment = LineAlignment.TransformZ; l.transform.rotation = tilt; l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("align.TransformZ.zigzag", "zigzag", l => { l.alignment = LineAlignment.TransformZ; l.transform.rotation = tilt; });
        LineCase("align.TransformZ.identity.L.c2", "L", l => { l.alignment = LineAlignment.TransformZ; l.numCornerVertices = 2; });
        LineCase("align.TransformZ.flipped.L.c2", "L", l => { l.alignment = LineAlignment.TransformZ; l.transform.rotation = Quaternion.Euler(0f, 180f, 0f); l.numCornerVertices = 2; });
        LineCase("align.TransformZ.spatial.c2cap2", "spatial", l => { l.alignment = LineAlignment.TransformZ; l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("align.View.persp.L.c2cap2", "L", l => { l.numCornerVertices = 2; l.numCapVertices = 2; }, persp);
        LineCase("align.View.persp.spatial", "spatial", null, persp);
        LineCase("align.View.persp.spatial.c2cap2", "spatial", l => { l.numCornerVertices = 2; l.numCapVertices = 2; }, persp);
        LineCase("align.View.noCamera.L.c2", "L", l => l.numCornerVertices = 2, null, false, null, true);

        foreach (bool useTransform in new[] { false, true })
        {
            string u = useTransform ? ".useTransform" : "";
            LineCase("local.View.L.c2cap2" + u, "L", l => { Local(l, new Vector3(1.5f, 0.5f, 1f)); l.numCornerVertices = 2; l.numCapVertices = 2; }, null, useTransform);
            LineCase("local.TransformZ.L.c2cap2" + u, "L", l => { Local(l, new Vector3(1.5f, 0.5f, 1f)); l.alignment = LineAlignment.TransformZ; l.numCornerVertices = 2; l.numCapVertices = 2; }, null, useTransform);
            LineCase("local.uniform.zigzag" + u, "zigzag", l => Local(l, new Vector3(2f, 2f, 2f)), null, useTransform);
            LineCase("world.moved.L" + u, "L", l => { l.transform.position = new Vector3(0.5f, 0.25f, 0f); l.transform.rotation = Quaternion.Euler(0f, 0f, 30f); l.transform.localScale = new Vector3(1.5f, 0.5f, 1f); }, null, useTransform);
        }

        LineCase("light.View.L.c2cap2", "L", l => { l.generateLightingData = true; l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("light.TransformZ.L.c2cap2", "L", l => { l.generateLightingData = true; l.alignment = LineAlignment.TransformZ; l.transform.rotation = tilt; l.numCornerVertices = 2; l.numCapVertices = 2; });
        LineCase("light.persp.spatial.c2cap2", "spatial", l => { l.generateLightingData = true; l.numCornerVertices = 2; l.numCapVertices = 2; }, persp);
        LineCase("light.Tile.zigzag", "zigzag", l => { l.generateLightingData = true; l.textureMode = LineTextureMode.Tile; });
        LineCase("light.loop.square.c2", "square", l => { l.generateLightingData = true; l.loop = true; l.numCornerVertices = 2; });
        LineCase("light.reverse.c2", "reverse", l => { l.generateLightingData = true; l.numCornerVertices = 2; });
        LineCase("light.local.L", "L", l => { l.generateLightingData = true; Local(l, new Vector3(1.5f, 0.5f, 1f)); }, null, true);

        LineCase("local.View.rotateOnly.L", "L", l => { Local(l, Vector3.one); }, null, false);
        LineCase("local.View.scaleOnly.L", "L", l => { Local(l, new Vector3(1.5f, 0.5f, 1f)); l.transform.rotation = Quaternion.identity; }, null, false);
        LineCase("local.View.moveOnly.L", "L", l => { Local(l, Vector3.one); l.transform.rotation = Quaternion.identity; }, null, false);
        LineCase("local.TransformZ.tilt.L", "L", l => { Local(l, Vector3.one); l.alignment = LineAlignment.TransformZ; l.transform.rotation = Quaternion.Euler(30f, 20f, 0f); }, null, false);
        foreach (float bias in new[] { 0f, 2f })
            LineCase("shadowBias." + bias, "L", l => { l.shadowBias = bias; l.numCornerVertices = 2; l.numCapVertices = 2; });

        foreach (var shape in new[] { "dupStartTurn", "dupEndTurn", "dupMidLong", "dupTriple", "dupUneven", "dupTwice", "four", "near1e-5", "near1e-3", "near1e-2" })
            foreach (int corners in new[] { 0, 2 })
                LineCase("dup." + shape + ".c" + corners, shape, l => l.numCornerVertices = corners);
        LineCase("dup.repeat.bump", "repeat", l => l.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 1f)));
        LineCase("dup.repeat.loop", "repeat", l => l.loop = true);

        Shapes["sweepL"] = new[] { P(-1, 0), P(0, 0), P(0, 1) };
        Shapes["sweepLasym"] = new[] { P(-1, 0), P(0, 0), P(0, 0.5f) };
        Shapes["sweepZ"] = new[] { P(-2, 0), P(-1, 0), P(0, 0), P(0, 1), P(1, 1) };
        Shapes["sweepObtuse"] = new[] { P(-1, 0), P(0, 0), P(0.8660254f, 0.5f) };
        Shapes["sweepStraight"] = new[] { P(-0.6f, 0), P(-0.3f, 0), P(0, 0), P(0.3f, 0) };
        Shapes["sweepLong"] = new[] { P(-3, 0), P(0, 0), P(0, 3) };
        foreach (float width in new[] { 0.2f, 0.6f, 0.8f, 1f, 1.2f, 1.4f, 1.6f, 1.8f, 2f, 2.4f, 3f, 4f })
        {
            LineCase("sweep.L.w" + width, "sweepL", l => l.widthMultiplier = width);
            LineCase("sweep.Lasym.w" + width, "sweepLasym", l => l.widthMultiplier = width);
            LineCase("sweep.Z.w" + width, "sweepZ", l => l.widthMultiplier = width);
            LineCase("sweep.obtuse.w" + width, "sweepObtuse", l => l.widthMultiplier = width);
            LineCase("sweep.straight.w" + width, "sweepStraight", l => l.widthMultiplier = width);
            LineCase("sweep.long.w" + width, "sweepLong", l => l.widthMultiplier = width);
            LineCase("sweep.repeat.w" + width, "repeat", l => l.widthMultiplier = width);
            LineCase("sweep.L.c2.w" + width, "sweepL", l => { l.widthMultiplier = width; l.numCornerVertices = 2; });
        }
        LineCase("sweep.L.taper", "sweepL", l => { l.widthMultiplier = 1.6f; l.widthCurve = new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(1f, 1f)); });
        foreach (float gap in new[] { 0.002f, 0.003f, 0.004f, 0.005f, 0.007f })
        {
            Shapes["gap" + gap] = new[] { P(-1, 0), P(0, 0), P(gap, 0), P(1, 1) };
            LineCase("dup.gap" + gap, "gap" + gap, null);
        }
        foreach (float turn in new[] { 120f, 130f, 138f, 140f, 141f, 142f, 145f, 150f, 160f, 170f, 179f })
        {
            float a = turn * Mathf.Deg2Rad;
            var bend = new[] { P(-1, 0), P(0, 0), new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) };
            Shapes["bend" + turn] = bend;
            LineCase("miter." + turn + ".w0.4", "bend" + turn, null);
            if (turn == 160f || turn == 140f)
                foreach (float width in new[] { 0.1f, 1f })
                    LineCase("miter." + turn + ".w" + width, "bend" + turn, l => l.widthMultiplier = width);
            if (turn == 160f)
            {
                LineCase("miter.160.c2", "bend160", l => l.numCornerVertices = 2);
                LineCase("miter.160.bump", "bend160", l => l.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 1f)));
            }
        }
    }

    static void Local(LineRenderer l, Vector3 scale)
    {
        l.useWorldSpace = false;
        l.transform.position = new Vector3(0.5f, 0.25f, 0f);
        l.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
        l.transform.localScale = scale;
    }

    void Images()
    {
        LineCase("L.c3cap3.color", "L", l => { l.colorGradient = ThreeKeys(GradientMode.Blend); l.numCornerVertices = 3; l.numCapVertices = 3; }, null, false, imageOrtho);
        LineCase("acute.c0.color", "acute", l => l.colorGradient = ThreeKeys(GradientMode.Blend), null, false, imageOrtho);
        foreach (var mode in new[] { LineTextureMode.Stretch, LineTextureMode.Tile })
            LineCase("zigzag.c2cap2.uv." + mode, "zigzag", l => { l.sharedMaterial = uvMat; l.textureMode = mode; l.numCornerVertices = 2; l.numCapVertices = 2; }, null, false, imageOrtho);
        LineCase("persp.spatial.c2cap2.color", "spatial", l => { l.colorGradient = ThreeKeys(GradientMode.Blend); l.numCornerVertices = 2; l.numCapVertices = 2; }, persp, false, imagePersp);
        LineCase("TransformZ.L.c2cap2.color", "L", l => { l.colorGradient = ThreeKeys(GradientMode.Blend); l.alignment = LineAlignment.TransformZ; l.transform.rotation = Quaternion.Euler(30f, 20f, 0f); l.numCornerVertices = 2; l.numCapVertices = 2; }, null, false, imageOrtho);

        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var white = new Color[16];
        for (int i = 0; i < white.Length; i++) white[i] = Color.white;
        tex.SetPixels(white);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        foreach (var interaction in new[] { SpriteMaskInteraction.VisibleInsideMask, SpriteMaskInteraction.VisibleOutsideMask })
        {
            var maskGo = new GameObject("mask") { layer = ImageLayer };
            maskGo.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            maskGo.AddComponent<SpriteMask>().sprite = sprite;
            LineCase("mask." + interaction, "zigzag", l => { l.maskInteraction = interaction; l.colorGradient = ThreeKeys(GradientMode.Blend); }, null, false, imageOrtho);
            DestroyImmediate(maskGo);
        }
    }

    // ---- trails ----

    sealed class Trail
    {
        internal string Name;
        internal TrailRenderer Renderer;
        internal GameObject Object;
        internal Func<int, Vector3?> Path;
        internal Action<int, TrailRenderer> Act;
        internal bool Copy;
    }

    static Vector3 Wave(int f) => new Vector3(-2.5f + 0.2f * f, 0.4f * Mathf.Sin(0.6f * f), 0f);

    Trail NewTrail(List<Trail> trails, string name, float y, Action<TrailRenderer> configure, Func<int, Vector3?> path = null, Action<int, TrailRenderer> act = null)
    {
        var go = new GameObject("trail." + name);
        var offset = new Vector3(0f, y, 0f);
        path = path ?? (f => Wave(f) + offset);
        go.transform.position = path(0) ?? offset;
        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = colorMat;
        trail.time = 0.11f;
        trail.minVertexDistance = 0.05f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f));
        trail.widthMultiplier = 0.3f;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.red, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.2f, 1f) });
        trail.colorGradient = g;
        trail.numCornerVertices = 0;
        trail.numCapVertices = 0;
        trail.textureMode = LineTextureMode.Stretch;
        trail.autodestruct = false;
        configure?.Invoke(trail);
        var t = new Trail { Name = name, Renderer = trail, Object = go, Path = path, Act = act };
        trails.Add(t);
        return t;
    }

    void TrailState(string key, Trail t)
    {
        if (t.Renderer == null) { Value(key + ".alive", B(false)); return; }
        var p = new Vector3[64];
        int n = t.Renderer.GetPositions(p);
        var v = new Vector3[64];
        int visible = t.Renderer.GetVisiblePositions(v);
        Value(key + ".state", "{\"count\":" + t.Renderer.positionCount + ",\"p\":" + Flat(new ArraySegment<Vector3>(p, 0, n))
            + ",\"visible\":" + Flat(new ArraySegment<Vector3>(v, 0, visible)) + ",\"at\":" + V3(t.Object.transform.position)
            + ",\"emitting\":" + B(t.Renderer.emitting) + "}");
        Bake(key + ".mesh", t.Renderer, main, false);
        if (t.Copy) CopyAsLine(key, t.Renderer);
    }

    /// <summary>A LineRenderer through the centres of the trail's baked vertex pairs, with the
    /// trail's width and color settings: separates the strip builder from the trail's points.</summary>
    void CopyAsLine(string key, TrailRenderer trail)
    {
        var mesh = new Mesh();
        trail.BakeMesh(mesh, main, false);
        var v = mesh.vertices;
        DestroyImmediate(mesh);
        int caps = trail.numCapVertices > 0 ? trail.numCapVertices + 3 : 0;
        if (v.Length - 2 * caps < 4 || trail.numCornerVertices > 0) return;
        var centre = new List<Vector3>();
        for (int i = caps; i + 1 < v.Length - caps; i += 2) centre.Add((v[i] + v[i + 1]) * 0.5f);
        var go = new GameObject("trailCopy") { layer = ImageLayer };
        var line = go.AddComponent<LineRenderer>();
        line.widthCurve = trail.widthCurve;
        line.widthMultiplier = trail.widthMultiplier;
        line.colorGradient = trail.colorGradient;
        line.numCapVertices = trail.numCapVertices;
        line.textureMode = trail.textureMode;
        line.positionCount = centre.Count;
        line.SetPositions(centre.ToArray());
        Value(key + ".centre", Flat(centre));
        Bake(key + ".asline", line, main, false);
        DestroyImmediate(go);
    }

    IEnumerator Trails()
    {
        var trails = new List<Trail>();
        NewTrail(trails, "basic", 0f, null).Copy = true;
        NewTrail(trails, "flat", 0.05f, r => { r.widthCurve = AnimationCurve.Constant(0f, 1f, 1f); r.colorGradient = WhiteGradient(); }).Copy = true;
        NewTrail(trails, "api2", 1.2f, r => r.emitting = false, f => new Vector3(0f, 1.2f, 0f), (f, r) =>
        {
            if (f == 1) r.AddPosition(P(-1f, 1.2f));
            if (f == 5) r.AddPositions(new[] { P(-0.5f, 1.3f), P(0f, 1.1f), P(0.5f, 1.3f) });
            if (f == 6) r.AddPosition(P(1f, 1.2f));
        }).Copy = true;
        NewTrail(trails, "api2Emitting", 0.95f, null, f => new Vector3(-1f + 0.01f * f, 0.95f, 0f), (f, r) =>
        {
            if (f == 3) r.AddPositions(new[] { P(-0.5f, 1f), P(0f, 0.9f), P(0.5f, 1f) });
        }).Copy = true;
        NewTrail(trails, "round", 0.1f, r => { r.numCornerVertices = 2; r.numCapVertices = 2; r.generateLightingData = true; });
        NewTrail(trails, "far", 0.2f, r => r.minVertexDistance = 0.45f);
        int lane = 0;
        foreach (var mode in new[] { LineTextureMode.Stretch, LineTextureMode.Tile, LineTextureMode.DistributePerSegment, LineTextureMode.RepeatPerSegment, LineTextureMode.Static })
            NewTrail(trails, "tex." + mode, 0.3f + 0.1f * lane++, r => { r.textureMode = mode; r.textureScale = new Vector2(1.5f, 1f); r.time = 0.15f; r.sharedMaterial = uvMat; });
        NewTrail(trails, "emit", -0.2f, null, null, (f, r) =>
        {
            if (f == 4) r.emitting = false;
            if (f == 8) r.emitting = true;
        });
        NewTrail(trails, "stop.autodestruct", -0.3f, r => r.autodestruct = true, f => Wave(Math.Min(f, 5)) + new Vector3(0f, -0.3f, 0f));
        NewTrail(trails, "stop", -0.4f, null, f => Wave(Math.Min(f, 5)) + new Vector3(0f, -0.4f, 0f));
        NewTrail(trails, "clear", -0.5f, null, null, (f, r) => { if (f == 6) r.Clear(); });
        NewTrail(trails, "transformZ", -0.6f, r => { r.alignment = LineAlignment.TransformZ; r.numCornerVertices = 1; r.numCapVertices = 1; });
        NewTrail(trails, "api", 1.5f, r => r.emitting = false, f => new Vector3(0f, 1.5f, 0f), (f, r) =>
        {
            if (f == 1) r.AddPosition(P(-1f, 1.5f));
            if (f == 2) r.AddPositions(new[] { P(-0.5f, 1.6f), P(0f, 1.4f), P(0.5f, 1.7f) });
            if (f == 4) r.SetPosition(1, P(-0.6f, 1.2f));
            if (f == 6) r.SetPositions(new[] { P(1f, 1f), P(1.5f, 1.2f) });
            if (f == 9) r.Clear();
            if (f == 10) r.AddPosition(P(0f, 1f));
        });
        NewTrail(trails, "apiEmitting", -1f, null, null, (f, r) =>
        {
            if (f == 3) r.AddPosition(P(0f, -1.8f));
            if (f == 7) r.SetPosition(0, P(-2f, -1.8f));
        });
        NewTrail(trails, "apiLong", 1.8f, r => { r.emitting = false; r.time = 10f; r.numCornerVertices = 2; r.numCapVertices = 2; }, f => new Vector3(0f, 1.8f, 0f), (f, r) =>
        {
            if (f == 0) r.SetPositions(new[] { P(-1f, 1.8f), P(-0.5f, 1.9f), P(-0.5f, 1.9f), P(0.5f, 1.7f), P(-0.2f, 1.8f) });
        });

        for (int f = 0; f < 16; f++)
        {
            foreach (var t in trails)
            {
                if (t.Object == null) continue;
                var at = t.Path(f);
                if (at.HasValue) t.Object.transform.position = at.Value;
                if (t.Renderer != null) t.Act?.Invoke(f, t.Renderer);
            }
            Value("trail.f" + f + ".time", "[" + Time.frameCount + "," + F(Time.time) + "," + F(Time.deltaTime) + "]");
            foreach (var t in trails) TrailState("trail.f" + f + "." + t.Name + ".mid", t);
            yield return new WaitForEndOfFrame();
            foreach (var t in trails) TrailState("trail.f" + f + "." + t.Name + ".end", t);
            if (f == 10)
            {
                imageOrtho.cullingMask = 1;
                Image("img.trails.f10", imageOrtho);
                imageOrtho.cullingMask = 1 << ImageLayer;
            }
            yield return null;
        }
        foreach (var t in trails) if (t.Object != null) Destroy(t.Object);
    }

    // ---- short-segment blend ----

    /// <summary>The predeclared blend cases (blend_cases.py, BlendCases.cs): plain lines whose
    /// segments are given as ratios of the width, recorded after everything else so that the
    /// existing records keep their order and values.</summary>
    void Blend()
    {
        foreach (var (name, points) in BlendCases.All)
        {
            string key = "blend." + name;
            try
            {
                Shapes[key] = points;
                Bake(key, NewLine(key), main, false);
            }
            catch (Exception e) { Value(key + ".fatal", Q(e.ToString())); }
            Clean();
        }
    }

    // ---- cap count discrimination ----

    /// <summary>Eight predeclared controls: renderer type, alignment and one/two cap
    /// vertices, with all other public inputs identical. Synchronous public trail
    /// population excludes emission, movement, scheduling and expiration.</summary>
    void CapCounts()
    {
        var points = new[] { new Vector3(-1f, 0f, 0f), new Vector3(1f, 0.5f, 0f) };
        foreach (bool trail in new[] { false, true })
            foreach (var alignment in new[] { LineAlignment.View, LineAlignment.TransformZ })
                foreach (int caps in new[] { 1, 2 })
                {
                    string key = "capcount." + (trail ? "trail." : "line.") + alignment + ".cap" + caps;
                    var go = new GameObject(key);
                    go.transform.position = Vector3.zero;
                    go.transform.rotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    Renderer renderer;
                    var readback = new Vector3[2];
                    int count;
                    float width;
                    int capCount;
                    LineAlignment align;
                    string lifecycle;
                    if (trail)
                    {
                        var r = go.AddComponent<TrailRenderer>();
                        r.emitting = false;
                        r.autodestruct = false;
                        r.time = 10000f;
                        r.minVertexDistance = 0f;
                        r.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
                        r.widthMultiplier = 0.4f;
                        r.colorGradient = WhiteGradient();
                        r.numCornerVertices = 0;
                        r.numCapVertices = caps;
                        r.alignment = alignment;
                        r.textureMode = LineTextureMode.Stretch;
                        r.textureScale = Vector2.one;
                        r.generateLightingData = true;
                        r.sharedMaterial = colorMat;
                        r.Clear();
                        r.AddPositions(points);
                        count = r.GetPositions(readback);
                        width = r.widthMultiplier;
                        capCount = r.numCapVertices;
                        align = r.alignment;
                        lifecycle = ",\"emitting\":" + B(r.emitting) + ",\"autodestruct\":" + B(r.autodestruct)
                            + ",\"time\":" + F(r.time) + ",\"minVertexDistance\":" + F(r.minVertexDistance);
                        renderer = r;
                    }
                    else
                    {
                        var r = go.AddComponent<LineRenderer>();
                        r.useWorldSpace = true;
                        r.loop = false;
                        r.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
                        r.widthMultiplier = 0.4f;
                        r.colorGradient = WhiteGradient();
                        r.numCornerVertices = 0;
                        r.numCapVertices = caps;
                        r.alignment = alignment;
                        r.textureMode = LineTextureMode.Stretch;
                        r.textureScale = Vector2.one;
                        r.generateLightingData = true;
                        r.sharedMaterial = colorMat;
                        r.positionCount = points.Length;
                        r.SetPositions(points);
                        count = r.GetPositions(readback);
                        width = r.widthMultiplier;
                        capCount = r.numCapVertices;
                        align = r.alignment;
                        lifecycle = ",\"useWorldSpace\":" + B(r.useWorldSpace) + ",\"loop\":" + B(r.loop);
                        renderer = r;
                    }
                    if (count != 2 || readback[0] != points[0] || readback[1] != points[1]
                        || width != 0.4f || capCount != caps || align != alignment)
                        throw new InvalidOperationException(key + " public input readback differs");
                    Value(key + ".inputs", "{\"p\":" + Flat(readback) + ",\"count\":" + count
                        + ",\"width\":" + F(width) + ",\"caps\":" + capCount + ",\"alignment\":" + Q(align.ToString())
                        + ",\"objectPosition\":" + V3(go.transform.position) + ",\"objectForward\":" + V3(go.transform.forward)
                        + ",\"objectScale\":" + V3(go.transform.localScale) + ",\"cameraPosition\":" + V3(main.transform.position)
                        + ",\"cameraForward\":" + V3(main.transform.forward) + ",\"cameraUp\":" + V3(main.transform.up)
                        + ",\"orthographic\":" + B(main.orthographic) + ",\"material\":" + Q(renderer.sharedMaterial.shader.name)
                        + lifecycle + "}");
                    Bake(key + ".mesh", renderer, main, false);
                    DestroyImmediate(go);
                }
    }

    // ---- driver ----

    Camera NewCamera(string name, bool orthographic, Vector3 position, Vector3 lookAt)
    {
        var go = new GameObject(name);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = orthographic;
        cam.orthographicSize = 2f;
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.allowMSAA = false;
        cam.allowHDR = false;
        cam.aspect = (float)W / H;
        go.transform.position = position;
        go.transform.LookAt(lookAt);
        return cam;
    }

    IEnumerator Start()
    {
        directory = Environment.GetEnvironmentVariable("LINE_PROBE_OUT");
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += OnLog;
        int code = 0;
        try
        {
            colorMat = new Material(Resources.Load<Shader>("LineColor"));
            uvMat = new Material(Resources.Load<Shader>("LineUv"));
            main = NewCamera("Main Camera", true, new Vector3(0f, 0f, -10f), Vector3.zero);
            main.tag = "MainCamera";
            main.cullingMask = ~(1 << ImageLayer);
            imageOrtho = NewCamera("ImageOrtho", true, new Vector3(0f, 0f, -10f), Vector3.zero);
            imageOrtho.cullingMask = 1 << ImageLayer;
            imageOrtho.enabled = false;
            persp = NewCamera("Persp", false, new Vector3(1.5f, 1.2f, -5f), new Vector3(0.3f, 0.2f, 0f));
            persp.enabled = false;
            imagePersp = NewCamera("ImagePersp", false, new Vector3(1.5f, 1.2f, -5f), new Vector3(0.3f, 0.2f, 0f));
            imagePersp.cullingMask = 1 << ImageLayer;
            imagePersp.enabled = false;
            Value("env", "{\"colorSpace\":" + Q(QualitySettings.activeColorSpace.ToString()) + ",\"device\":" + Q(SystemInfo.graphicsDeviceType.ToString()) + "}");
            Defaults();
            Shortcuts();
            Lines();
            Images();
        }
        catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; }
        Flush();
        if (code == 0)
        {
            var steps = Trails();
            while (true)
            {
                bool more;
                try { more = steps.MoveNext(); }
                catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; break; }
                if (!more) break;
                yield return steps.Current;
            }
        }
        if (code == 0)
        {
            try { Blend(); CapCounts(); MiterReversals(); MiterChains(); SpaceControls(); EdgeControls(); }
            catch (Exception e) { Value("fatal", Q(e.ToString())); code = 1; }
        }
        Value("complete", B(code == 0));
        Flush();
        Debug.Log("LINE_PROBE_DONE " + directory + " " + code);
        Application.logMessageReceived -= OnLog;
        if (finished != null) finished(code);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
