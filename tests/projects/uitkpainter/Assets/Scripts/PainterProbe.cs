using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

// Painter2D controls, one mechanism per case. Each case is drawn twice: by a detached painter,
// whose SaveToVectorImage output (vertices with tint, uv, flags and arc circle, indices, size) is
// written as one JSON line, and by an element's generateVisualContent on the runtime panel, which
// is rendered to an image at scale 1 and 1.25.
public static class PainterProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string Output => Environment.GetEnvironmentVariable("UITK_PAINTER_OUT");

    struct Case
    {
        public string name;
        public Action<Painter2D> draw;
    }

    static readonly Color Red = new Color(0.9f, 0.2f, 0.1f, 1f);
    static readonly Color Blue = new Color(0.2f, 0.4f, 0.9f, 1f);
    static readonly Color HalfGreen = new Color(0.2f, 0.8f, 0.3f, 0.5f);

    static Action<Painter2D> Stroke(float width, LineJoin join, LineCap cap, Action<Painter2D> path, float miter = -1)
    {
        return p => {
            p.lineWidth = width; p.lineJoin = join; p.lineCap = cap; p.strokeColor = Red;
            if (miter >= 0) p.miterLimit = miter;
            p.BeginPath(); path(p); p.Stroke();
        };
    }

    static Action<Painter2D> Fill(FillRule rule, Action<Painter2D> path)
    {
        return p => { p.fillColor = Blue; p.BeginPath(); path(p); p.Fill(rule); };
    }

    static void Zigzag(Painter2D p) { p.MoveTo(new Vector2(8, 60)); p.LineTo(new Vector2(30, 12)); p.LineTo(new Vector2(52, 60)); p.LineTo(new Vector2(72, 30)); }
    static void Sharp(Painter2D p) { p.MoveTo(new Vector2(8, 60)); p.LineTo(new Vector2(40, 10)); p.LineTo(new Vector2(48, 62)); }
    static void Square(Painter2D p) { p.MoveTo(new Vector2(12, 12)); p.LineTo(new Vector2(62, 12)); p.LineTo(new Vector2(62, 58)); p.LineTo(new Vector2(12, 58)); p.ClosePath(); }
    // A five-pointed star (pentagram), on a 1/8 grid so Mono and CoreCLR build the same points.
    static readonly Vector2[] StarPoints = {
        new Vector2(40, 6), new Vector2(57.625f, 60.25f), new Vector2(11.5f, 26.75f), new Vector2(68.5f, 26.75f), new Vector2(22.375f, 60.25f),
    };
    static void Star(Painter2D p)
    {
        for (int i = 0; i < StarPoints.Length; i++)
        {
            if (i == 0) p.MoveTo(StarPoints[i]); else p.LineTo(StarPoints[i]);
        }
        p.ClosePath();
    }
    static void Rect(Painter2D p, float x0, float y0, float x1, float y1, bool ccw)
    {
        p.MoveTo(new Vector2(x0, y0));
        if (ccw) { p.LineTo(new Vector2(x0, y1)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x1, y0)); }
        else { p.LineTo(new Vector2(x1, y0)); p.LineTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x0, y1)); }
        p.ClosePath();
    }
    static void Holes(Painter2D p, bool ccw) { Rect(p, 8, 8, 72, 64, false); Rect(p, 24, 20, 56, 52, ccw); }

    static readonly Case[] Cases = {
        new Case { name = "stroke-line-butt", draw = Stroke(8, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 12)); p.LineTo(new Vector2(70, 50)); }) },
        new Case { name = "stroke-line-round", draw = Stroke(8, LineJoin.Miter, LineCap.Round, p => { p.MoveTo(new Vector2(10, 12)); p.LineTo(new Vector2(70, 50)); }) },
        new Case { name = "stroke-line-width1", draw = Stroke(1, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 12)); p.LineTo(new Vector2(70, 50)); }) },
        new Case { name = "stroke-line-width03", draw = Stroke(0.3f, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 12)); p.LineTo(new Vector2(70, 50)); }) },
        new Case { name = "stroke-line-axis", draw = Stroke(6, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 20)); p.LineTo(new Vector2(70, 20)); p.MoveTo(new Vector2(20, 30)); p.LineTo(new Vector2(20, 64)); }) },
        new Case { name = "stroke-join-miter", draw = Stroke(8, LineJoin.Miter, LineCap.Butt, Zigzag) },
        new Case { name = "stroke-join-bevel", draw = Stroke(8, LineJoin.Bevel, LineCap.Butt, Zigzag) },
        new Case { name = "stroke-join-round", draw = Stroke(8, LineJoin.Round, LineCap.Round, Zigzag) },
        new Case { name = "stroke-miter-limit-default", draw = Stroke(6, LineJoin.Miter, LineCap.Butt, Sharp) },
        new Case { name = "stroke-miter-limit-2", draw = Stroke(6, LineJoin.Miter, LineCap.Butt, Sharp, 2) },
        new Case { name = "stroke-closed", draw = Stroke(6, LineJoin.Miter, LineCap.Round, Square) },
        new Case { name = "stroke-open-square", draw = Stroke(6, LineJoin.Round, LineCap.Butt, p => { p.MoveTo(new Vector2(12, 12)); p.LineTo(new Vector2(62, 12)); p.LineTo(new Vector2(62, 58)); p.LineTo(new Vector2(12, 58)); p.LineTo(new Vector2(12, 12)); }) },
        new Case { name = "stroke-backtrack", draw = Stroke(6, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 30)); p.LineTo(new Vector2(70, 30)); p.LineTo(new Vector2(30, 30)); }) },
        new Case { name = "stroke-zero-length", draw = Stroke(10, LineJoin.Round, LineCap.Round, p => { p.MoveTo(new Vector2(30, 30)); p.LineTo(new Vector2(30, 30)); }) },
        new Case { name = "stroke-arc-cw", draw = Stroke(5, LineJoin.Miter, LineCap.Butt, p => p.Arc(new Vector2(40, 36), 26, 0, 270, ArcDirection.Clockwise)) },
        new Case { name = "stroke-arc-ccw", draw = Stroke(5, LineJoin.Miter, LineCap.Butt, p => p.Arc(new Vector2(40, 36), 26, 0, 270, ArcDirection.CounterClockwise)) },
        new Case { name = "stroke-arc-full", draw = Stroke(4, LineJoin.Miter, LineCap.Butt, p => p.Arc(new Vector2(40, 36), 26, 0, 360)) },
        new Case { name = "stroke-arc-small", draw = Stroke(3, LineJoin.Miter, LineCap.Round, p => p.Arc(new Vector2(40, 36), 4, 30, 200)) },
        new Case { name = "stroke-arc-joined", draw = Stroke(5, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.LineTo(new Vector2(20, 36)); p.Arc(new Vector2(40, 36), 20, 180, 360); p.LineTo(new Vector2(72, 60)); }) },
        new Case { name = "stroke-arcto", draw = Stroke(5, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.ArcTo(new Vector2(40, 8), new Vector2(72, 60), 14); p.LineTo(new Vector2(72, 60)); }) },
        new Case { name = "stroke-bezier", draw = Stroke(4, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.BezierCurveTo(new Vector2(20, -10), new Vector2(60, 90), new Vector2(72, 10)); }) },
        new Case { name = "stroke-quadratic", draw = Stroke(4, LineJoin.Round, LineCap.Round, p => { p.MoveTo(new Vector2(8, 60)); p.QuadraticCurveTo(new Vector2(40, -20), new Vector2(72, 60)); }) },
        new Case { name = "stroke-subpaths", draw = Stroke(4, LineJoin.Bevel, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 10)); p.LineTo(new Vector2(36, 30)); p.MoveTo(new Vector2(44, 10)); p.LineTo(new Vector2(72, 30)); p.LineTo(new Vector2(44, 60)); }) },
        new Case { name = "stroke-translucent", draw = p => { p.lineWidth = 6; p.strokeColor = HalfGreen; p.BeginPath(); Zigzag(p); p.Stroke(); } },
        new Case { name = "stroke-defaults", draw = p => { p.BeginPath(); Zigzag(p); p.Stroke(); } },
        new Case { name = "fill-triangle", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(10, 60)); p.LineTo(new Vector2(40, 8)); p.LineTo(new Vector2(70, 60)); p.ClosePath(); }) },
        new Case { name = "fill-open", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(10, 60)); p.LineTo(new Vector2(40, 8)); p.LineTo(new Vector2(70, 60)); }) },
        new Case { name = "fill-concave", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(8, 8)); p.LineTo(new Vector2(72, 36)); p.LineTo(new Vector2(8, 64)); p.LineTo(new Vector2(30, 36)); p.ClosePath(); }) },
        new Case { name = "fill-star-nonzero", draw = Fill(FillRule.NonZero, Star) },
        new Case { name = "fill-star-oddeven", draw = Fill(FillRule.OddEven, Star) },
        new Case { name = "fill-holes-same-nonzero", draw = Fill(FillRule.NonZero, p => Holes(p, false)) },
        new Case { name = "fill-holes-opposite-nonzero", draw = Fill(FillRule.NonZero, p => Holes(p, true)) },
        new Case { name = "fill-holes-same-oddeven", draw = Fill(FillRule.OddEven, p => Holes(p, false)) },
        new Case { name = "fill-circle", draw = Fill(FillRule.NonZero, p => p.Arc(new Vector2(40, 36), 26, 0, 360)) },
        new Case { name = "fill-pie", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(40, 36)); p.Arc(new Vector2(40, 36), 28, 20, 250); p.ClosePath(); }) },
        new Case { name = "fill-pie-ccw", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(40, 36)); p.Arc(new Vector2(40, 36), 28, 20, 250, ArcDirection.CounterClockwise); p.ClosePath(); }) },
        new Case { name = "fill-rounded-rect", draw = Fill(FillRule.NonZero, p => {
            p.MoveTo(new Vector2(22, 10)); p.ArcTo(new Vector2(70, 10), new Vector2(70, 62), 12); p.ArcTo(new Vector2(70, 62), new Vector2(10, 62), 12);
            p.ArcTo(new Vector2(10, 62), new Vector2(10, 10), 12); p.ArcTo(new Vector2(10, 10), new Vector2(70, 10), 12); p.ClosePath(); }) },
        new Case { name = "fill-bezier", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(8, 60)); p.BezierCurveTo(new Vector2(20, -10), new Vector2(60, -10), new Vector2(72, 60)); p.ClosePath(); }) },
        new Case { name = "fill-quadratic", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(8, 60)); p.QuadraticCurveTo(new Vector2(40, -20), new Vector2(72, 60)); p.ClosePath(); }) },
        new Case { name = "fill-bezier-inflection", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(8, 36)); p.BezierCurveTo(new Vector2(30, -20), new Vector2(50, 90), new Vector2(72, 36)); p.LineTo(new Vector2(72, 66)); p.LineTo(new Vector2(8, 66)); p.ClosePath(); }) },
        new Case { name = "fill-bowtie", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(8, 8)); p.LineTo(new Vector2(72, 64)); p.LineTo(new Vector2(72, 8)); p.LineTo(new Vector2(8, 64)); p.ClosePath(); }) },
        new Case { name = "fill-translucent", draw = p => { p.fillColor = HalfGreen; p.BeginPath(); Star(p); p.Fill(); } },
        new Case { name = "fill-defaults", draw = p => { p.BeginPath(); Square(p); p.Fill(); } },
        new Case { name = "fill-then-stroke", draw = p => { p.fillColor = Blue; p.strokeColor = Red; p.lineWidth = 3; p.BeginPath(); Square(p); p.Fill(); p.Stroke(); } },
        // Controls of measured path rules.
        new Case { name = "stroke-quadratic-closed", draw = Stroke(3, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.QuadraticCurveTo(new Vector2(40, -20), new Vector2(72, 60)); p.ClosePath(); }) },
        new Case { name = "fill-arc-closed-nomove", draw = Fill(FillRule.NonZero, p => { p.Arc(new Vector2(40, 36), 26, 0, 300); p.ClosePath(); }) },
        new Case { name = "stroke-arc-full-roundcap", draw = Stroke(6, LineJoin.Miter, LineCap.Round, p => p.Arc(new Vector2(40, 36), 26, 90, 450)) },
        new Case { name = "stroke-arc-ccw-roundcap", draw = Stroke(6, LineJoin.Round, LineCap.Round, p => { p.MoveTo(new Vector2(8, 60)); p.Arc(new Vector2(40, 40), 22, 180, 0, ArcDirection.CounterClockwise); p.LineTo(new Vector2(72, 8)); }) },
        new Case { name = "stroke-arc-bevel", draw = Stroke(7, LineJoin.Bevel, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 8)); p.LineTo(new Vector2(18, 36)); p.Arc(new Vector2(40, 36), 22, 180, 330); }) },
        new Case { name = "fill-hexagon", draw = Fill(FillRule.NonZero, p => {
            // Exact coordinates: the probe's own arithmetic differs between Mono and CoreCLR.
            p.MoveTo(new Vector2(70, 36)); p.LineTo(new Vector2(55, 62)); p.LineTo(new Vector2(25, 62));
            p.LineTo(new Vector2(10, 36)); p.LineTo(new Vector2(25, 10)); p.LineTo(new Vector2(55, 10)); p.ClosePath(); }) },
        new Case { name = "stroke-after-close", draw = Stroke(5, LineJoin.Miter, LineCap.Round, p => { p.MoveTo(new Vector2(12, 12)); p.LineTo(new Vector2(60, 12)); p.LineTo(new Vector2(36, 50)); p.ClosePath(); p.LineTo(new Vector2(70, 64)); }) },
        new Case { name = "stroke-fill-reuse", draw = p => { p.strokeColor = Red; p.fillColor = HalfGreen; p.lineWidth = 2; p.BeginPath(); Zigzag(p); p.Stroke(); p.lineWidth = 6; p.LineTo(new Vector2(72, 66)); p.Stroke(); p.Fill(); } },
        new Case { name = "fill-pie-small", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(20, 60)); p.Arc(new Vector2(20, 60), 50, -40, 0); p.ClosePath(); }) },
        new Case { name = "stroke-thick-curve", draw = Stroke(16, LineJoin.Round, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 60)); p.BezierCurveTo(new Vector2(40, -10), new Vector2(50, 70), new Vector2(20, 30)); }) },
        new Case { name = "stroke-width-zero", draw = Stroke(0, LineJoin.Miter, LineCap.Round, Zigzag) },
        new Case { name = "stroke-transparent", draw = p => { p.strokeColor = new Color(1, 0, 0, 0); p.lineWidth = 4; p.BeginPath(); Zigzag(p); p.Stroke(); } },
        new Case { name = "stroke-nan", draw = Stroke(4, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 60)); p.LineTo(new Vector2(float.NaN, 20)); p.LineTo(new Vector2(40, 10)); p.LineTo(new Vector2(72, 60)); }) },
        new Case { name = "stroke-arcto-collinear", draw = Stroke(4, LineJoin.Round, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 36)); p.ArcTo(new Vector2(40, 36), new Vector2(72, 36), 10); p.LineTo(new Vector2(72, 60)); }) },
        new Case { name = "stroke-tiny-arc", draw = Stroke(4, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(8, 36)); p.Arc(new Vector2(40, 36), 0.0005f, 0, 180); p.LineTo(new Vector2(72, 20)); }) },
        new Case { name = "stroke-arcto-radius-zero", draw = Stroke(4, LineJoin.Miter, LineCap.Butt, p => { p.MoveTo(new Vector2(10, 10)); p.ArcTo(new Vector2(60, 10), new Vector2(60, 60), 0); p.LineTo(new Vector2(60, 60)); p.LineTo(new Vector2(10, 60)); }) },
        new Case { name = "fill-arcto-radius-zero", draw = Fill(FillRule.NonZero, p => { p.MoveTo(new Vector2(10, 10)); p.ArcTo(new Vector2(60, 10), new Vector2(60, 60), 0); p.LineTo(new Vector2(60, 60)); p.LineTo(new Vector2(10, 60)); p.ClosePath(); }) },
        new Case { name = "fill-plus", draw = Fill(FillRule.NonZero, p => { Rect(p, 10, 26, 70, 46, false); Rect(p, 30, 6, 50, 66, false); }) },
        new Case { name = "stroke-closepath-twice", draw = Stroke(5, LineJoin.Round, LineCap.Butt, p => { p.MoveTo(new Vector2(12, 12)); p.LineTo(new Vector2(60, 12)); p.LineTo(new Vector2(36, 50)); p.ClosePath(); p.ClosePath(); }) },
        new Case { name = "stroke-close-after-return", draw = Stroke(5, LineJoin.Miter, LineCap.Round, p => { p.MoveTo(new Vector2(12, 12)); p.LineTo(new Vector2(60, 12)); p.LineTo(new Vector2(36, 50)); p.LineTo(new Vector2(12, 12)); p.ClosePath(); }) },
        new Case { name = "stroke-arc-width-diameter", draw = Stroke(20, LineJoin.Miter, LineCap.Butt, p => p.Arc(new Vector2(40, 36), 10, 0, 200)) },
        new Case { name = "stroke-arc-tiny-roundcap", draw = Stroke(6, LineJoin.Round, LineCap.Round, p => { p.MoveTo(new Vector2(10, 40)); p.LineTo(new Vector2(40, 40)); p.Arc(new Vector2(40, 0), 40, 90, 89.9f, ArcDirection.CounterClockwise); p.LineTo(new Vector2(60, 60)); }) },
        new Case { name = "fill-two-contours-oddeven", draw = Fill(FillRule.OddEven, p => { p.Arc(new Vector2(30, 36), 22, 0, 360); p.MoveTo(new Vector2(70, 36)); p.Arc(new Vector2(50, 36), 20, 0, 360); }) },
    };

    static string F(float v) => v.ToString("R", Inv);

    static void Defaults(StringBuilder sb, string label, Painter2D p)
    {
        sb.Append("{\"k\":\"defaults\",\"painter\":\"").Append(label).Append("\",\"lineWidth\":").Append(F(p.lineWidth))
          .Append(",\"strokeColor\":").Append(Col(p.strokeColor)).Append(",\"fillColor\":").Append(Col(p.fillColor))
          .Append(",\"lineJoin\":\"").Append(p.lineJoin).Append("\",\"lineCap\":\"").Append(p.lineCap)
          .Append("\",\"miterLimit\":").Append(F(p.miterLimit)).Append(",\"strokeGradient\":").Append(p.strokeGradient == null ? "null" : "\"set\"").Append("}\n");
    }

    static string Col(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";

    sealed class LogCapture : ILogHandler
    {
        public readonly ILogHandler Inner;
        public readonly List<string> Lines = new List<string>();
        public LogCapture(ILogHandler inner) { Inner = inner; }
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            Lines.Add(logType + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
            Inner.LogFormat(logType, context, format, args);
        }
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            Lines.Add("Exception: " + exception.GetType().Name + ": " + exception.Message);
            Inner.LogException(exception, context);
        }
    }

    static string Str(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static void Api(StringBuilder sb, LogCapture logs, string name, Func<string> body)
    {
        logs.Lines.Clear();
        string result;
        try { result = body(); }
        catch (Exception e) { result = Str("threw " + e.GetType().Name + ": " + e.Message); }
        sb.Append("{\"k\":\"api\",\"case\":").Append(Str(name)).Append(",\"result\":").Append(result).Append(",\"logs\":[");
        for (int i = 0; i < logs.Lines.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Str(logs.Lines[i])); }
        sb.Append("]}\n");
    }

    static string MeshSummary(Painter2D p)
    {
        var image = ScriptableObject.CreateInstance<VectorImage>();
        p.SaveToVectorImage(image);
        var verts = (Array)typeof(VectorImage).GetField("vertices", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(image);
        string s = "{\"vertices\":" + (verts == null ? 0 : verts.Length) + ",\"size\":[" + F(image.width) + "," + F(image.height) + "]}";
        UnityEngine.Object.DestroyImmediate(image);
        return s;
    }

    static string State(Painter2D p) =>
        "{\"lineWidth\":" + F(p.lineWidth) + ",\"strokeColor\":" + Col(p.strokeColor) + ",\"fillColor\":" + Col(p.fillColor) +
        ",\"lineJoin\":\"" + p.lineJoin + "\",\"lineCap\":\"" + p.lineCap + "\",\"miterLimit\":" + F(p.miterLimit) + "}";

    // ---- stroke gradients ----

    // Key colours in whole 8-bit steps whose differences are multiples of 16, and alpha keys away
    // from the middle of a colour span: no colour the cases read sits halfway between two 8-bit
    // values, where the last bit of the arithmetic would decide the rounding.
    static readonly Color KeyRed = new Color32(232, 56, 24, 255);
    static readonly Color KeyGreen = new Color32(24, 184, 56, 255);
    static readonly Color KeyBlue = new Color32(56, 104, 232, 255);

    static Gradient TwoKeys()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(KeyRed, 0f), new GradientColorKey(KeyBlue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // Colour keys at 0, 0.25 and 1, alpha keys at 0, 0.5 and 1.
    static Gradient ThreeKeys(GradientMode mode = GradientMode.Blend)
    {
        var g = new Gradient { mode = mode };
        g.SetKeys(new[] { new GradientColorKey(KeyRed, 0f), new GradientColorKey(KeyGreen, 0.25f), new GradientColorKey(KeyBlue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 0.5f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    static Gradient ManyKeys()
    {
        var g = new Gradient();
        var keys = new GradientColorKey[8];
        for (int i = 0; i < 8; i++) keys[i] = new GradientColorKey(i % 2 == 0 ? KeyRed : KeyBlue, i / 8f + 0.0625f);
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.4f, 0.53125f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    static string Keys(Gradient g)
    {
        if (g == null) return "null";
        var sb = new StringBuilder("{\"mode\":\"").Append(g.mode).Append("\",\"space\":\"").Append(g.colorSpace).Append("\",\"color\":[");
        var c = g.colorKeys; var a = g.alphaKeys;
        for (int i = 0; i < c.Length; i++) { if (i > 0) sb.Append(','); sb.Append('[').Append(F(c[i].time)).Append(',').Append(Col(c[i].color)).Append(']'); }
        sb.Append("],\"alpha\":[");
        for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(','); sb.Append('[').Append(F(a[i].time)).Append(',').Append(F(a[i].alpha)).Append(']'); }
        return sb.Append("]}").ToString();
    }

    static Action<Painter2D> GradientStroke(float width, LineJoin join, LineCap cap, Func<Gradient> gradient, Action<Painter2D> path)
    {
        return p => {
            p.lineWidth = width; p.lineJoin = join; p.lineCap = cap; p.strokeColor = Red; p.strokeGradient = gradient();
            p.BeginPath(); path(p); p.Stroke();
            p.strokeGradient = null;
        };
    }

    static void Line(Painter2D p) { p.MoveTo(new Vector2(10, 12)); p.LineTo(new Vector2(70, 50)); }

    static readonly Case[] GradientCases = {
        // Left set on purpose: what the next element's painter starts with is recorded on the panel.
        new Case { name = "gradient-left-set", draw = p => { p.lineWidth = 6; p.strokeColor = Red; p.strokeGradient = TwoKeys(); p.BeginPath(); Line(p); p.Stroke(); } },
        // Drawn on the panel right after the element that left a gradient set, with strokeColor only.
        new Case { name = "gradient-after-left-set", draw = p => { p.lineWidth = 6; p.strokeColor = Blue; p.BeginPath(); Line(p); p.Stroke(); } },
        new Case { name = "gradient-line", draw = GradientStroke(8, LineJoin.Miter, LineCap.Butt, TwoKeys, Line) },
        new Case { name = "gradient-line-keys", draw = GradientStroke(8, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(), Line) },
        new Case { name = "gradient-line-round", draw = GradientStroke(8, LineJoin.Miter, LineCap.Round, () => ThreeKeys(), Line) },
        new Case { name = "gradient-line-many-keys", draw = GradientStroke(6, LineJoin.Miter, LineCap.Butt, ManyKeys, Line) },
        new Case { name = "gradient-line-fixed", draw = GradientStroke(8, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(GradientMode.Fixed), Line) },
        new Case { name = "gradient-line-perceptual", draw = GradientStroke(8, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(GradientMode.PerceptualBlend), Line) },
        new Case { name = "gradient-line-linear-space", draw = GradientStroke(8, LineJoin.Miter, LineCap.Butt, () => { var g = ThreeKeys(); g.colorSpace = ColorSpace.Linear; return g; }, Line) },
        new Case { name = "gradient-line-gamma-space", draw = GradientStroke(8, LineJoin.Miter, LineCap.Butt, () => { var g = ThreeKeys(); g.colorSpace = ColorSpace.Gamma; return g; }, Line) },
        new Case { name = "gradient-join-miter", draw = GradientStroke(8, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(), Zigzag) },
        new Case { name = "gradient-join-bevel", draw = GradientStroke(8, LineJoin.Bevel, LineCap.Butt, () => ThreeKeys(), Zigzag) },
        new Case { name = "gradient-join-round", draw = GradientStroke(8, LineJoin.Round, LineCap.Round, () => ThreeKeys(), Zigzag) },
        new Case { name = "gradient-closed", draw = GradientStroke(6, LineJoin.Miter, LineCap.Round, () => ThreeKeys(), Square) },
        new Case { name = "gradient-subpaths", draw = GradientStroke(4, LineJoin.Bevel, LineCap.Round, TwoKeys, p => { p.MoveTo(new Vector2(8, 10)); p.LineTo(new Vector2(36, 30)); p.MoveTo(new Vector2(44, 10)); p.LineTo(new Vector2(72, 30)); p.LineTo(new Vector2(44, 60)); }) },
        new Case { name = "gradient-zero-length", draw = GradientStroke(10, LineJoin.Round, LineCap.Round, TwoKeys, p => { p.MoveTo(new Vector2(30, 30)); p.LineTo(new Vector2(30, 30)); }) },
        new Case { name = "gradient-short-and-long", draw = GradientStroke(4, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(), p => { p.MoveTo(new Vector2(8, 20)); p.LineTo(new Vector2(8.03125f, 20)); p.LineTo(new Vector2(72, 20)); p.LineTo(new Vector2(72, 60)); }) },
        new Case { name = "gradient-arc-cw", draw = GradientStroke(5, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(), p => p.Arc(new Vector2(40, 36), 26, 0, 270, ArcDirection.Clockwise)) },
        new Case { name = "gradient-arc-ccw", draw = GradientStroke(5, LineJoin.Miter, LineCap.Round, () => ThreeKeys(), p => p.Arc(new Vector2(40, 36), 26, 0, 270, ArcDirection.CounterClockwise)) },
        new Case { name = "gradient-arc-full", draw = GradientStroke(4, LineJoin.Miter, LineCap.Butt, TwoKeys, p => p.Arc(new Vector2(40, 36), 26, 0, 360)) },
        new Case { name = "gradient-arc-joined", draw = GradientStroke(5, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(), p => { p.MoveTo(new Vector2(8, 60)); p.LineTo(new Vector2(20, 36)); p.Arc(new Vector2(40, 36), 20, 180, 360); p.LineTo(new Vector2(72, 60)); }) },
        new Case { name = "gradient-arc-tiny", draw = GradientStroke(4, LineJoin.Miter, LineCap.Butt, TwoKeys, p => { p.MoveTo(new Vector2(8, 36)); p.Arc(new Vector2(40, 36), 0.0005f, 0, 180); p.LineTo(new Vector2(72, 20)); }) },
        new Case { name = "gradient-arcto", draw = GradientStroke(5, LineJoin.Round, LineCap.Butt, () => ThreeKeys(), p => { p.MoveTo(new Vector2(8, 60)); p.ArcTo(new Vector2(40, 8), new Vector2(72, 60), 14); p.LineTo(new Vector2(72, 60)); }) },
        new Case { name = "gradient-bezier", draw = GradientStroke(4, LineJoin.Miter, LineCap.Butt, TwoKeys, p => { p.MoveTo(new Vector2(8, 60)); p.BezierCurveTo(new Vector2(20, -10), new Vector2(60, 90), new Vector2(72, 10)); }) },
        new Case { name = "gradient-bezier-keys", draw = GradientStroke(4, LineJoin.Miter, LineCap.Round, () => ThreeKeys(), p => { p.MoveTo(new Vector2(8, 60)); p.BezierCurveTo(new Vector2(20, -10), new Vector2(60, 90), new Vector2(72, 10)); }) },
        new Case { name = "gradient-bezier-many-keys", draw = GradientStroke(4, LineJoin.Miter, LineCap.Butt, ManyKeys, p => { p.MoveTo(new Vector2(8, 60)); p.BezierCurveTo(new Vector2(20, -10), new Vector2(60, 90), new Vector2(72, 10)); }) },
        new Case { name = "gradient-quadratic", draw = GradientStroke(4, LineJoin.Round, LineCap.Round, () => ThreeKeys(), p => { p.MoveTo(new Vector2(8, 60)); p.QuadraticCurveTo(new Vector2(40, -20), new Vector2(72, 60)); p.LineTo(new Vector2(72, 66)); }) },
        new Case { name = "gradient-bezier-straight", draw = GradientStroke(4, LineJoin.Miter, LineCap.Butt, () => ThreeKeys(), p => { p.MoveTo(new Vector2(8, 20)); p.BezierCurveTo(new Vector2(24, 28), new Vector2(56, 44), new Vector2(72, 52)); }) },
        new Case { name = "gradient-thick-curve", draw = GradientStroke(16, LineJoin.Round, LineCap.Butt, () => ThreeKeys(), p => { p.MoveTo(new Vector2(10, 60)); p.BezierCurveTo(new Vector2(40, -10), new Vector2(50, 70), new Vector2(20, 30)); }) },
        new Case { name = "gradient-mixed-path", draw = GradientStroke(4, LineJoin.Round, LineCap.Round, ManyKeys, p => { p.MoveTo(new Vector2(8, 60)); p.LineTo(new Vector2(20, 20)); p.BezierCurveTo(new Vector2(30, 0), new Vector2(46, 0), new Vector2(52, 20)); p.Arc(new Vector2(60, 40), 12, 180, 90, ArcDirection.CounterClockwise); p.ClosePath(); }) },
        // The documented rules: a gradient overrides strokeColor; null falls back on it.
        new Case { name = "gradient-then-null", draw = p => { p.lineWidth = 6; p.strokeColor = Blue; p.strokeGradient = TwoKeys(); p.strokeGradient = null; p.BeginPath(); Line(p); p.Stroke(); } },
        new Case { name = "gradient-color-set-after", draw = p => { p.lineWidth = 6; p.strokeGradient = TwoKeys(); p.strokeColor = HalfGreen; p.BeginPath(); Line(p); p.Stroke(); p.strokeGradient = null; } },
        new Case { name = "gradient-transparent-color", draw = p => { p.lineWidth = 6; p.strokeColor = new Color(1, 0, 0, 0); p.strokeGradient = TwoKeys(); p.BeginPath(); Line(p); p.Stroke(); p.strokeGradient = null; } },
        new Case { name = "gradient-transparent-keys", draw = p => { p.lineWidth = 6; p.strokeColor = Red; var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(KeyRed, 0f), new GradientColorKey(KeyBlue, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 1f) }); p.strokeGradient = g; p.BeginPath(); Line(p); p.Stroke(); p.strokeGradient = null; } },
        new Case { name = "gradient-changed-after-set", draw = p => { p.lineWidth = 6; p.strokeColor = Red; var g = TwoKeys(); p.strokeGradient = g; g.SetKeys(new[] { new GradientColorKey(Color.green, 0f), new GradientColorKey(Color.green, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }); p.BeginPath(); Line(p); p.Stroke(); p.strokeGradient = null; } },
        new Case { name = "gradient-fill-and-stroke", draw = p => { p.fillColor = Blue; p.strokeColor = Red; p.lineWidth = 3; p.strokeGradient = ThreeKeys(); p.BeginPath(); Square(p); p.Fill(); p.Stroke(); p.strokeGradient = null; } },
        new Case { name = "gradient-two-strokes", draw = p => { p.lineWidth = 4; p.strokeColor = Red; p.strokeGradient = TwoKeys(); p.BeginPath(); Line(p); p.Stroke(); p.LineTo(new Vector2(70, 12)); p.Stroke(); p.strokeGradient = null; } },
    };

    public static void Run(UIDocument document, RenderTexture target)
    {
        Directory.CreateDirectory(Output);
        var sb = new StringBuilder();
        var logs = new LogCapture(Debug.unityLogger.logHandler);
        Debug.unityLogger.logHandler = logs;
        Api(sb, logs, "detached-without-beginpath", () => {
            var p = new Painter2D();
            p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(10, 10)); p.Arc(new Vector2(5, 5), 3, 0, 90); p.ArcTo(new Vector2(1, 1), new Vector2(2, 0), 1);
            p.BezierCurveTo(new Vector2(1, 1), new Vector2(2, 2), new Vector2(3, 0)); p.QuadraticCurveTo(new Vector2(1, 1), new Vector2(2, 0));
            p.ClosePath(); p.Stroke(); p.Fill();
            string r = MeshSummary(p); p.Dispose(); return r; });
        Api(sb, logs, "detached-clear-resets", () => {
            var p = new Painter2D();
            p.lineWidth = 5; p.strokeColor = Red; p.lineJoin = LineJoin.Round; p.miterLimit = 3;
            p.BeginPath(); p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(10, 10)); p.Stroke();
            string before = MeshSummary(p);
            p.Clear();
            string r = "{\"before\":" + before + ",\"after\":" + MeshSummary(p) + ",\"state\":" + State(p) + "}";
            p.LineTo(new Vector2(20, 20)); p.Stroke();
            r = r.Substring(0, r.Length - 1) + ",\"strokeAfterClear\":" + MeshSummary(p) + "}";
            p.Dispose(); return r; });
        Api(sb, logs, "detached-two-calls-accumulate", () => {
            var p = new Painter2D();
            p.BeginPath(); p.MoveTo(new Vector2(0, 0)); p.LineTo(new Vector2(10, 0)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(50, 40)); p.LineTo(new Vector2(60, 40)); p.Stroke();
            string r = MeshSummary(p); p.Dispose(); return r; });
        Api(sb, logs, "save-null", () => { var p = new Painter2D(); try { p.SaveToVectorImage(null); return "\"returned\""; } finally { p.Dispose(); } });
        Api(sb, logs, "default-gradient", () => {
            var p = new Painter2D(); var g = p.strokeGradient; p.Dispose();
            if (g == null) return "null";
            var c = g.colorKeys; var a = g.alphaKeys;
            return "{\"colorKeys\":" + c.Length + ",\"alphaKeys\":" + a.Length + ",\"first\":" + Col(c[0].color) + ",\"mode\":\"" + g.mode + "\"}"; });
        var vertexField = typeof(VectorImage).GetField("vertices", BindingFlags.Instance | BindingFlags.NonPublic);
        var indexField = typeof(VectorImage).GetField("indices", BindingFlags.Instance | BindingFlags.NonPublic);
        var vertexType = vertexField.FieldType.GetElementType();
        FieldInfo VF(string n) => vertexType.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var fPos = VF("position"); var fTint = VF("tint"); var fUv = VF("uv"); var fFlags = VF("flags"); var fCircle = VF("circle");

        using (var fresh = new Painter2D()) Defaults(sb, "detached", fresh);
        void Meshes(Case[] cases)
        {
        foreach (var c in cases)
        {
            using (var painter = new Painter2D())
            {
                c.draw(painter);
                var image = ScriptableObject.CreateInstance<VectorImage>();
                bool saved = painter.SaveToVectorImage(image);
                var verts = (Array)vertexField.GetValue(image);
                var idx = (ushort[])indexField.GetValue(image);
                sb.Append("{\"k\":\"mesh\",\"case\":\"").Append(c.name).Append("\",\"saved\":").Append(saved ? "true" : "false")
                  .Append(",\"size\":[").Append(F(image.width)).Append(',').Append(F(image.height)).Append("],\"v\":[");
                for (int i = 0; verts != null && i < verts.Length; i++)
                {
                    object v = verts.GetValue(i);
                    var pos = (Vector3)fPos.GetValue(v); var tint = (Color32)fTint.GetValue(v); var uv = (Vector2)fUv.GetValue(v);
                    var flags = (Color32)fFlags.GetValue(v); var circle = (Vector4)fCircle.GetValue(v);
                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(F(pos.x)).Append(',').Append(F(pos.y)).Append(',')
                      .Append(tint.r).Append(',').Append(tint.g).Append(',').Append(tint.b).Append(',').Append(tint.a).Append(',')
                      .Append(F(uv.x)).Append(',').Append(F(uv.y)).Append(',')
                      .Append(flags.r).Append(',').Append(flags.g).Append(',').Append(flags.b).Append(',').Append(flags.a).Append(',')
                      .Append(F(circle.x)).Append(',').Append(F(circle.y)).Append(',').Append(F(circle.z)).Append(',').Append(F(circle.w)).Append(']');
                }
                sb.Append("],\"i\":[");
                for (int i = 0; idx != null && i < idx.Length; i++) { if (i > 0) sb.Append(','); sb.Append(idx[i]); }
                sb.Append("]}\n");
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
        }
        Meshes(Cases);

        // The same cases on the runtime panel, 80x72 cells in a 9-column grid.
        var root = document.rootVisualElement;
        root.Clear();
        root.style.backgroundColor = (Color)new Color32(240, 240, 236, 255);
        var states = new List<string>();
        Painter2D captured = null;
        for (int i = 0; i < Cases.Length; i++)
        {
            var c = Cases[i];
            var e = new VisualElement { name = c.name };
            e.style.position = Position.Absolute;
            e.style.left = 8 + (i % 9) * 88 + 0.3f * (i % 2);
            e.style.top = 8 + (i / 9) * 74;
            e.style.width = 80; e.style.height = 72;
            int index = i;
            e.generateVisualContent += ctx => {
                // The attached painter's state on entry (after the previous element changed it),
                // recorded for the first three elements of each pass.
                var p = ctx.painter2D;
                if (index < 3)
                {
                    var d = new StringBuilder(); Defaults(d, "attached-" + index, p); states.Add(d.ToString());
                    p.lineWidth = 7; p.strokeColor = Blue; p.lineJoin = LineJoin.Bevel;
                    if (index == 2) { logs.Lines.Clear(); p.MoveTo(new Vector2(1, 1)); p.LineTo(new Vector2(5, 5)); p.Stroke(); states.Add("{\"k\":\"attached-without-beginpath\",\"logs\":[" + string.Join(",", logs.Lines.ConvertAll(Str)) + "]}\n"); }
                    captured = p;
                }
                c.draw(p);
            };
            root.Add(e);
        }
        {
            // Call order on the panel: an allocated quad, a painter stroke across it, then another
            // allocated quad over part of the stroke.
            var e = new VisualElement { name = "allocate-stroke-order" };
            e.style.position = Position.Absolute;
            e.style.left = 8 + 8 * 88; e.style.top = 8 + 7 * 74;
            e.style.width = 80; e.style.height = 30;
            e.generateVisualContent += ctx => {
                Quad(ctx, new UnityEngine.Rect(4, 4, 40, 22), new Color32(40, 160, 60, 255));
                var p = ctx.painter2D;
                p.strokeColor = Red; p.lineWidth = 6; p.BeginPath(); p.MoveTo(new Vector2(2, 15)); p.LineTo(new Vector2(78, 15)); p.Stroke();
                Quad(ctx, new UnityEngine.Rect(50, 2, 20, 26), new Color32(40, 60, 200, 255));
                // DrawMesh joins the same order: a painter stroke, then a drawn quad over it.
                p.strokeColor = Blue; p.lineWidth = 4; p.BeginPath(); p.MoveTo(new Vector2(20, 26)); p.LineTo(new Vector2(78, 26)); p.Stroke();
                var vertices = new Unity.Collections.NativeArray<Vertex>(4, Unity.Collections.Allocator.Temp);
                var indices = new Unity.Collections.NativeArray<ushort>(6, Unity.Collections.Allocator.Temp);
                var c = new Color32(220, 200, 40, 255);
                vertices[0] = new Vertex { position = new Vector3(30, 20, Vertex.nearZ), tint = c };
                vertices[1] = new Vertex { position = new Vector3(40, 20, Vertex.nearZ), tint = c };
                vertices[2] = new Vertex { position = new Vector3(40, 30, Vertex.nearZ), tint = c };
                vertices[3] = new Vertex { position = new Vector3(30, 30, Vertex.nearZ), tint = c };
                ushort[] quad = { 0, 1, 2, 0, 2, 3 };
                for (int i = 0; i < 6; i++) indices[i] = quad[i];
                ctx.DrawMesh(vertices, indices);
            };
            root.Add(e);
        }
        foreach (float scale in new[] { 1f, 1.25f })
        {
            document.panelSettings.scale = scale;
            Update(); Update();
            Render(target, Path.Combine(Output, "painter-" + (scale == 1 ? "1" : "1.25") + ".png"));
        }
        foreach (var s in states) sb.Append(s);
        // The context's painter used after its callbacks: an error, until a detached painter is
        // created (which marks painters active again until the next callback ends).
        Api(sb, logs, "attached-outside-callback", () => { captured.BeginPath(); captured.MoveTo(new Vector2(0, 0)); return "null"; });
        Api(sb, logs, "attached-after-detached", () => { new Painter2D().Dispose(); captured.BeginPath(); captured.MoveTo(new Vector2(0, 0)); return "null"; });
        Api(sb, logs, "attached-save-and-clear", () => { captured.Clear(); return captured.SaveToVectorImage(ScriptableObject.CreateInstance<VectorImage>()) ? "true" : "false"; });
        // Stroke gradients: after everything above, so those lines and images do not depend on them.
        Meshes(GradientCases);
        Api(sb, logs, "gradient-roundtrip", () => {
            var p = new Painter2D(); var g = TwoKeys(); p.strokeGradient = g; var back = p.strokeGradient;
            string r = "{\"same\":" + (ReferenceEquals(back, g) ? "true" : "false") + ",\"keys\":" + Keys(back);
            g.SetKeys(new[] { new GradientColorKey(Color.green, 0.5f) }, new[] { new GradientAlphaKey(0.25f, 0.5f) });
            r += ",\"afterSourceChanged\":" + Keys(p.strokeGradient);
            p.strokeGradient = null;
            r += ",\"afterNull\":" + Keys(p.strokeGradient) + ",\"strokeColor\":" + Col(p.strokeColor) + "}";
            p.Dispose(); return r; });
        Api(sb, logs, "gradient-after-clear", () => {
            var p = new Painter2D(); p.strokeGradient = TwoKeys(); p.Clear();
            p.lineWidth = 6; p.strokeColor = Blue; p.BeginPath(); Line(p); p.Stroke();
            string r = MeshSummary(p); p.Dispose(); return r; });
        Api(sb, logs, "gradient-clear-resets", () => {
            var p = new Painter2D(); p.strokeGradient = ThreeKeys(); p.Clear();
            string r = Keys(p.strokeGradient); p.Dispose(); return r; });
        {
            document.panelSettings.scale = 1f;
            root.Clear();
            var gradientStates = new List<string>();
            for (int i = 0; i < GradientCases.Length; i++)
            {
                var c = GradientCases[i];
                var e = new VisualElement { name = c.name };
                e.style.position = Position.Absolute;
                e.style.left = 8 + (i % 9) * 88 + 0.3f * (i % 2);
                e.style.top = 8 + (i / 9) * 74;
                e.style.width = 80; e.style.height = 72;
                int index = i;
                e.generateVisualContent += ctx => {
                    // The context painter's gradient on entry, after the previous element set one.
                    if (index < 3) gradientStates.Add("{\"k\":\"gradient-on-entry\",\"element\":" + index + ",\"keys\":" + Keys(ctx.painter2D.strokeGradient) + "}\n");
                    c.draw(ctx.painter2D);
                };
                root.Add(e);
            }
            Update(); Update();
            Render(target, Path.Combine(Output, "painter-gradient.png"));
            foreach (var g in gradientStates) sb.Append(g);
        }
        Debug.unityLogger.logHandler = logs.Inner;
        File.WriteAllText(Path.Combine(Output, "painter.jsonl"), sb.ToString());
    }

    static void Quad(MeshGenerationContext ctx, UnityEngine.Rect r, Color32 c)
    {
        var mesh = ctx.Allocate(4, 6);
        mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = c });
        mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = c });
        mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = c });
        mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = c });
        mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2);
        mesh.SetNextIndex(0); mesh.SetNextIndex(2); mesh.SetNextIndex(3);
    }

    static void Update()
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
    }

    static void Render(RenderTexture target, string path)
    {
        var t = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        t.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { false });
        t.GetMethod("RenderOffscreenPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var tex = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, target.width, target.height), 0, 0); tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        RenderTexture.active = old;
    }
}
