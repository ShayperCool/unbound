using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;
using PL = UnityEngine.PlayerLoop;

namespace UnityEngine
{
    /// <summary>What line and trail renderers share (Unity's LineParameters): width, color, caps,
    /// alignment and texture mapping along the strip.</summary>
    internal sealed class LineParameters
    {
        internal float WidthMultiplier = 1f;
        internal AnimationCurve WidthCurve = new AnimationCurve(new Keyframe(0f, 1f));
        internal Gradient ColorGradient = new Gradient();
        /// <summary>Kept as set (negative counts read back unchanged); the strip uses at least 0.</summary>
        internal int NumCornerVertices;
        internal int NumCapVertices;
        internal LineAlignment Alignment = LineAlignment.View;
        internal LineTextureMode TextureMode = LineTextureMode.Stretch;
        internal Vector2 TextureScale = Vector2.one;
        internal float ShadowBias = 0.5f;
        internal bool GenerateLightingData;

        internal void Read(YNode node)
        {
            if (node == null) return;
            WidthMultiplier = node.Float("widthMultiplier", 1f);
            var curve = node.Get("widthCurve");
            if (curve != null) WidthCurve = BuiltinReaders.AnimationCurve(curve);
            var gradient = node.Get("colorGradient");
            if (gradient != null) ColorGradient = BuiltinReaders.Gradient(gradient);
            NumCornerVertices = node.Int("numCornerVertices");
            NumCapVertices = node.Int("numCapVertices");
            Alignment = (LineAlignment)node.Int("alignment");
            TextureMode = (LineTextureMode)node.Int("textureMode");
            TextureScale = node.Get("textureScale") != null ? node.Vector2("textureScale") : Vector2.one;
            ShadowBias = node.Float("shadowBias", 0.5f);
            GenerateLightingData = node.Bool("generateLightingData");
        }

        internal LineParameters Clone() => new LineParameters
        {
            WidthMultiplier = WidthMultiplier,
            WidthCurve = new AnimationCurve(WidthCurve.keys) { preWrapMode = WidthCurve.preWrapMode, postWrapMode = WidthCurve.postWrapMode },
            ColorGradient = CloneGradient(ColorGradient),
            NumCornerVertices = NumCornerVertices,
            NumCapVertices = NumCapVertices,
            Alignment = Alignment,
            TextureMode = TextureMode,
            TextureScale = TextureScale,
            ShadowBias = ShadowBias,
            GenerateLightingData = GenerateLightingData,
        };

        static Gradient CloneGradient(Gradient g)
        {
            var copy = new Gradient { mode = g.mode, colorSpace = g.colorSpace };
            copy.SetKeys(g.colorKeys, g.alphaKeys);
            return copy;
        }

        // The start/end shortcuts edit the first and last keys (measured, tests/projects/lines api.*):
        // a one-key width curve keeps its key for the start and gains a key at 1 for the end; colors
        // are stored at 8 bits per channel.

        internal float StartWidth
        {
            get => WidthCurve.length > 0 ? WidthCurve.Evaluate(0f) * WidthMultiplier : 0f;
            set => SetWidthKey(0, value);
        }

        internal float EndWidth
        {
            get => WidthCurve.length > 0 ? WidthCurve.Evaluate(1f) * WidthMultiplier : 0f;
            set => SetWidthKey(1, value);
        }

        void SetWidthKey(int end, float width)
        {
            float value = WidthMultiplier != 0f ? width / WidthMultiplier : width;
            var keys = WidthCurve.keys;
            if (keys.Length == 0) keys = new[] { new Keyframe(end == 0 ? 0f : 1f, value) };
            else if (keys.Length == 1 && end == 1 && keys[0].time < 1f) keys = new[] { keys[0], new Keyframe(1f, value) };
            else keys[end == 0 ? 0 : keys.Length - 1].value = value;
            WidthCurve = new AnimationCurve(keys) { preWrapMode = WidthCurve.preWrapMode, postWrapMode = WidthCurve.postWrapMode };
        }

        internal Color StartColor
        {
            get => ColorGradient.Evaluate(0f);
            set => SetColorKey(0, value);
        }

        internal Color EndColor
        {
            get => ColorGradient.Evaluate(1f);
            set => SetColorKey(1, value);
        }

        void SetColorKey(int end, Color color)
        {
            Color stored = (Color32)color;
            var colors = ColorGradient.colorKeys;
            var alphas = ColorGradient.alphaKeys;
            if (colors.Length < 2) colors = new[] { new GradientColorKey(StartColor, 0f), new GradientColorKey(EndColor, 1f) };
            if (alphas.Length < 2) alphas = new[] { new GradientAlphaKey(StartColor.a, 0f), new GradientAlphaKey(EndColor.a, 1f) };
            int ci = end == 0 ? 0 : colors.Length - 1;
            int ai = end == 0 ? 0 : alphas.Length - 1;
            colors[ci].color = new Color(stored.r, stored.g, stored.b, 1f);
            alphas[ai].alpha = stored.a;
            var g = new Gradient { mode = ColorGradient.mode, colorSpace = ColorGradient.colorSpace };
            g.SetKeys(colors, alphas);
            ColorGradient = g;
        }
    }

    /// <summary>How a strip faces: towards a camera (View: the camera's position and forward, in the
    /// strip's build space) or along the build space's Z axis (TransformZ).</summary>
    internal struct LineFacing
    {
        internal bool TransformZ;
        // Native TransformZ trails reverse both cap fans independently of cap count.
        internal bool ReverseCaps;
        internal Vector3 TransformForward;
        internal Vector3 CameraPosition;
        internal Vector3 CameraForward;
    }

    /// <summary>
    /// The mesh of a line or trail, as Unity generates it (measured, tests/projects/lines):
    /// a pair of vertices per point (v = 0.5 + scale/2 on the minus side, 0.5 - scale/2 on the
    /// plus side), joined by quads. Each segment has an effective direction: its own unless it is
    /// short compared with the line's widest width (2 width / length - 1 > 0, clamped to 1) or has
    /// zero length (squared length below 1e-5), in which case it blends with the directions before
    /// it and looks ahead to the next one (see EffectiveDirections). A point's pair is the miter
    /// between its neighbouring effective directions (length half width / cos, at most three half
    /// widths), a joint turning by less than about 8 degrees easing its outgoing direction back;
    /// with corner vertices the outer side becomes an arc of that many steps around the inner miter
    /// vertex at the full width. Caps are half discs of extra vertices before and after the strip.
    /// The side of a segment is cross(direction, camera position - point) projected onto the view
    /// plane (View) or cross(direction, -Z) (TransformZ).
    /// </summary>
    internal sealed class LineStrip
    {
        const float ZeroLengthSqr = 1e-5f;
        const float MiterLimit = 3f;

        readonly List<Vector3> m_Vertices = new List<Vector3>();
        readonly List<Vector3> m_Normals = new List<Vector3>();
        readonly List<Vector4> m_Tangents = new List<Vector4>();
        readonly List<Color32> m_Colors = new List<Color32>();
        readonly List<Vector2> m_Uvs = new List<Vector2>();
        readonly List<int> m_Triangles = new List<int>();
        bool m_Lighting;

        // Per point and per segment scratch.
        float[] m_Dist = Array.Empty<float>();
        float[] m_Half = Array.Empty<float>();
        Color32[] m_Color = Array.Empty<Color32>();
        Vector3[] m_Dir = Array.Empty<Vector3>();
        Vector3[] m_Effective = Array.Empty<Vector3>();
        float[] m_Length = Array.Empty<float>();
        bool[] m_Zero = Array.Empty<bool>();
        bool[] m_NoMiter = Array.Empty<bool>();

        enum Join : byte { Quad, CornerA, CornerB }

        struct Pair
        {
            internal Vector3 A, B, Tangent;
            internal float U;
            internal Color32 Color;
            internal Join Join;
        }

        readonly List<Pair> m_Pairs = new List<Pair>();

        static void Grow<T>(ref T[] array, int count)
        {
            if (array.Length < count) array = new T[Math.Max(count, array.Length * 2)];
        }

        /// <summary>Builds the strip through the points (first is t = 0). <paramref name="hidden"/>
        /// marks points drawn at zero width (a trail's points added while it was not emitting).
        /// Returns false when there is nothing to draw (fewer than two points).</summary>
        internal bool Build(IReadOnlyList<Vector3> points, IReadOnlyList<bool> hidden, bool loop, LineParameters p, LineFacing facing)
        {
            m_Vertices.Clear();
            m_Normals.Clear();
            m_Tangents.Clear();
            m_Colors.Clear();
            m_Uvs.Clear();
            m_Triangles.Clear();
            m_Pairs.Clear();
            m_Lighting = p.GenerateLightingData;
            int count = points.Count;
            if (count < 2) return false;
            int n = loop ? count + 1 : count;
            int segments = n - 1;
            Grow(ref m_Dist, n);
            Grow(ref m_Half, n);
            Grow(ref m_Color, n);
            Grow(ref m_Dir, segments);
            Grow(ref m_Effective, segments);
            Grow(ref m_Length, segments);
            Grow(ref m_Zero, segments);
            Grow(ref m_NoMiter, n);

            m_Dist[0] = 0f;
            for (int i = 1; i < n; i++) m_Dist[i] = m_Dist[i - 1] + Vector3.Distance(points[(i - 1) % count], points[i % count]);
            float total = m_Dist[n - 1];
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < n; i++)
            {
                float t = total > 0f ? m_Dist[i] / total : 0f;
                bool hide = hidden != null && hidden[i % count];
                m_Half[i] = hide ? 0f : 0.5f * p.WidthMultiplier * p.WidthCurve.Evaluate(t);
                m_Color[i] = VertexColor(p.ColorGradient.Evaluate(t), linear);
            }

            bool allZero = true;
            for (int j = 0; j < segments; j++)
            {
                var d = points[(j + 1) % count] - points[j % count];
                m_Length[j] = d.magnitude;
                m_Zero[j] = d.sqrMagnitude < ZeroLengthSqr;
                m_Dir[j] = m_Zero[j] ? Vector3.zero : d / m_Length[j];
                if (!m_Zero[j]) allZero = false;
            }
            if (allZero)
            {
                // Nothing but coincident points: the raw directions, else a fixed one.
                for (int j = 0; j < segments; j++)
                {
                    var d = points[(j + 1) % count] - points[j % count];
                    m_Dir[j] = m_Length[j] > 0f ? d / m_Length[j] : Vector3.left;
                    m_Zero[j] = false;
                }
            }
            for (int i = 0; i < n; i++) m_NoMiter[i] = false;
            // The joint two points after a zero-length segment keeps the half width (no miter).
            for (int j = 0; j < segments; j++)
                if (m_Zero[j])
                {
                    int at = loop ? (j + 2) % segments : j + 2;
                    if (at < n) m_NoMiter[at] = true;
                    if (loop && at == 0) m_NoMiter[n - 1] = true;
                }
            // So does the first point after leading zero-length segments (measured, a trail's
            // repeated newest point: tests/projects/lines trail.*.asline).
            if (!loop)
            {
                int lead = 0;
                while (lead < segments && m_Zero[lead]) lead++;
                if (lead > 0) m_NoMiter[lead] = true;
            }
            if (loop) LoopDirections(segments);
            else EffectiveDirections(segments);

            int corners = Math.Max(0, p.NumCornerVertices);
            var faceNormal = facing.TransformZ ? -facing.TransformForward : -facing.CameraForward;
            float va = 0.5f + 0.5f * p.TextureScale.y, vb = 0.5f - 0.5f * p.TextureScale.y;
            for (int i = 0; i < n; i++)
            {
                var point = points[i % count];
                float h = m_Half[i];
                var normal = facing.TransformZ ? faceNormal : facing.CameraPosition - point;
                float u = U(p, i, n, loop, total);
                var color = m_Color[i];
                bool hasIn = i > 0 || loop, hasOut = i < segments || loop;
                var din = hasIn ? m_Effective[i > 0 ? i - 1 : segments - 1] : Vector3.zero;
                var dout = hasOut ? m_Effective[i < segments ? i : 0] : Vector3.zero;
                if (hasIn && hasOut && !loop) dout = EaseSmallTurn(din, dout);
                if (!hasIn || !hasOut)
                {
                    var d = hasOut ? dout : din;
                    var s = Side(d, normal, facing);
                    m_Pairs.Add(new Pair { A = point - s * h, B = point + s * h, Tangent = Vector3.Cross(faceNormal, s), U = u, Color = color });
                    continue;
                }
                var s1 = Side(din, normal, facing);
                var s2 = Side(dout, normal, facing);
                var sum = s1 + s2;
                Vector3 m;
                float length;
                if (sum.sqrMagnitude < 1e-12f)
                {
                    m = s1;
                    length = h;
                }
                else
                {
                    m = sum.normalized;
                    length = h / Mathf.Max(Vector3.Dot(m, s1), 1f / MiterLimit);
                }
                if (m_NoMiter[i]) length = h;
                else if (i > 0 && i < segments && Mathf.Abs(length) > Mathf.Abs(h))
                {
                    // A miter reaches at most three quarters of the longer neighbouring segment,
                    // never inside the half width (measured, line.sweep.Z and line.sweep.repeat).
                    float reach = 0.75f * Mathf.Max(m_Zero[i - 1] ? 0f : m_Length[i - 1], m_Zero[i] ? 0f : m_Length[i]);
                    if (reach > 0f) length = Mathf.Sign(h) * Mathf.Max(Mathf.Abs(h), Mathf.Min(Mathf.Abs(length), reach));
                }
                float turn = Vector3.Dot(Vector3.Cross(s1, s2), normal);
                bool aInner = turn > 0f || (turn == 0f && Vector3.Dot(s1, s2) < 0f);
                // A zero or negative width swaps the arc's fixed side (measured corner triangles,
                // tests/projects/lines line.width.zero and line.width.negative).
                if (h <= 0f) aInner = !aInner;
                if (corners == 0)
                {
                    m_Pairs.Add(new Pair { A = point - m * length, B = point + m * length, Tangent = Vector3.Cross(faceNormal, s2), U = u, Color = color });
                    continue;
                }
                float w = 2f * h;
                var inner = aInner ? point - m * length : point + m * length;
                float angle = Mathf.Acos(Mathf.Clamp(Vector3.Dot(s1, s2), -1f, 1f));
                var perp = s2 - s1 * Vector3.Dot(s1, s2);
                perp = perp.sqrMagnitude > 1e-12f ? perp.normalized : din;
                // The tangent arc uses the angle between the side directions. Even normalized
                // float vectors can have squared length below one; dividing by their lengths
                // keeps coincident directions at zero angle (saved round-trail controls), while
                // retaining the face-plane tangents of spatial lines. Positions keep their arc.
                float tangentDenominator = Mathf.Sqrt(s1.sqrMagnitude * s2.sqrMagnitude);
                float tangentAngle = tangentDenominator > 0f
                    ? Mathf.Acos(Mathf.Clamp(Vector3.Dot(s1, s2) / tangentDenominator, -1f, 1f)) : 0f;
                // A loop's first point keeps only the last step of its corner; the closing point has it all.
                for (int k = loop && i == 0 ? corners : 0; k <= corners; k++)
                {
                    float f = (float)k / corners;
                    var r = s1 * Mathf.Cos(angle * f) + perp * Mathf.Sin(angle * f);
                    // The tangent lies in the strip's face, across the step's side (measured,
                    // line.light.persp.spatial.c2cap2).
                    var tangentSide = s1 * Mathf.Cos(tangentAngle * f) + perp * Mathf.Sin(tangentAngle * f);
                    var tangent = w * Vector3.Cross(faceNormal, tangentSide);
                    var join = k == 0 ? Join.Quad : aInner ? Join.CornerA : Join.CornerB;
                    if (aInner) m_Pairs.Add(new Pair { A = inner, B = inner + r * w, Tangent = tangent, U = u, Color = color, Join = join });
                    else m_Pairs.Add(new Pair { A = inner - r * w, B = inner, Tangent = tangent, U = u, Color = color, Join = join });
                }
            }

            int caps = loop ? 0 : Math.Max(0, p.NumCapVertices);
            if (caps > 0) Cap(points[0], m_Pairs[0], m_Effective[0], true, m_Half[0] < 0f, caps, va, vb, facing, faceNormal);
            int first = m_Vertices.Count;
            foreach (var pair in m_Pairs)
            {
                Add(pair.A, new Vector2(pair.U, va), pair.Color, faceNormal, pair.Tangent);
                Add(pair.B, new Vector2(pair.U, vb), pair.Color, faceNormal, pair.Tangent);
            }
            for (int i = 1; i < m_Pairs.Count; i++)
            {
                int a0 = first + 2 * i - 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                switch (m_Pairs[i].Join)
                {
                    case Join.CornerB: Tri(a0, b1, a1); break;
                    case Join.CornerA: Tri(b0, b1, a1); break;
                    default: Tri(a0, b0, a1); Tri(b0, b1, a1); break;
                }
            }
            if (caps > 0) Cap(points[(n - 1) % count], m_Pairs[m_Pairs.Count - 1], m_Effective[segments - 1], false, m_Half[n - 1] < 0f, caps, va, vb, facing, faceNormal);
            return true;
        }

        /// <summary>A loop's segment directions: a zero-length segment takes the next non-zero one.</summary>
        void LoopDirections(int segments)
        {
            int first = 0;
            while (first < segments && m_Zero[first]) first++;
            if (first == segments)
            {
                for (int j = 0; j < segments; j++) m_Effective[j] = m_Dir[j];
                return;
            }
            var next = m_Dir[first];
            int at = first;
            for (int remaining = segments; remaining > 0; remaining--)
            {
                at = at > 0 ? at - 1 : segments - 1;
                if (!m_Zero[at]) next = m_Dir[at];
                m_Effective[at] = next;
            }
        }

        /// <summary>The cosine above which a joint eases its outgoing direction (about 8 degrees).</summary>
        const float SmallTurnCos = 0.99f;

        /// <summary>How much a segment blends: none while it is at least twice the line's widest
        /// width long, fully at that width or below, and fully for zero length (2 width / length - 1,
        /// clamped; measured, tests/projects/lines blend.* with line.tex.*.zigzag.curve for the
        /// widest width).</summary>
        float Shortness(int j, int segments, float width)
        {
            if (j < 0 || j >= segments) return 0f;
            if (m_Zero[j]) return 1f;
            return Mathf.Clamp01(2f * width / m_Length[j] - 1f);
        }

        /// <summary>A segment's direction as its neighbours see it: its own, or for a zero-length
        /// segment the next segment's, the last segment the previous one's (measured, line.dup.* and
        /// a trail's repeated oldest point).</summary>
        Vector3 Ahead(int j, int segments)
        {
            if (j >= segments) return Vector3.zero;
            if (!m_Zero[j]) return m_Dir[j];
            if (j + 1 < segments) return m_Dir[j + 1];
            return j > 0 ? m_Dir[j - 1] : Vector3.zero;
        }

        /// <summary>The effective directions (measured, tests/projects/lines blend.* and line.sweep.*):
        /// a chain carried along the line, each segment's own direction plus its shortness times the
        /// chain so far (a zero-length segment carries the previous effective direction), then plus
        /// the next segment's shortness times that segment's direction.</summary>
        void EffectiveDirections(int segments)
        {
            float width = 0f;
            for (int i = 0; i <= segments; i++) width = Mathf.Max(width, 2f * Mathf.Abs(m_Half[i]));
            var chain = Vector3.zero;
            for (int j = 0; j < segments; j++)
            {
                if (m_Zero[j]) chain = j > 0 ? m_Effective[j - 1] : Vector3.zero;
                else if (j == 0) chain = m_Dir[0];
                else chain = (m_Dir[j] + chain * Shortness(j, segments, width)).normalized;
                var e = chain + Ahead(j + 1, segments) * Shortness(j + 1, segments, width);
                if (e.sqrMagnitude > 0f) e.Normalize();
                else e = j > 0 ? m_Effective[j - 1] : Ahead(j + 1, segments);
                m_Effective[j] = e;
            }
            // Leading zero-length segments with nothing ahead take the first direction found.
            for (int j = segments - 1; j >= 0; j--)
                if (m_Effective[j].sqrMagnitude == 0f && j + 1 < segments) m_Effective[j] = m_Effective[j + 1];
        }

        /// <summary>A joint whose effective directions turn by less than about 8 degrees (cosine c
        /// above 0.99) takes as its outgoing direction the incoming one blended toward the outgoing
        /// one by (1 - c) / 0.01, so the miter fades out continuously (measured, tests/projects/lines
        /// blend.asym.30.*, blend.rev.Z.run.* and line.sweep.obtuse.w0.8).</summary>
        static Vector3 EaseSmallTurn(Vector3 din, Vector3 dout)
        {
            float c = Vector3.Dot(din, dout);
            if (c <= SmallTurnCos) return dout;
            float w = (1f - c) / (1f - SmallTurnCos);
            var e = din * (1f - w) + dout * w;
            return e.sqrMagnitude > 0f ? e.normalized : dout;
        }

        /// <summary>A vertex color: the gradient's color at 8 bits (rounded half up), its color
        /// channels taken to linear from those 8 bits in a linear project (measured,
        /// tests/projects/lines line.color.*).</summary>
        static Color32 VertexColor(Color color, bool linear)
        {
            byte r = ToByte(color.r), g = ToByte(color.g), b = ToByte(color.b), a = ToByte(color.a);
            if (linear)
            {
                r = ToByte(Mathf.GammaToLinearSpace(r / 255f));
                g = ToByte(Mathf.GammaToLinearSpace(g / 255f));
                b = ToByte(Mathf.GammaToLinearSpace(b / 255f));
            }
            return new Color32(r, g, b, a);
        }

        static byte ToByte(float value) => (byte)(Mathf.Clamp01(value) * 255f + 0.5f);

        static Vector3 Side(Vector3 direction, Vector3 normal, LineFacing facing)
        {
            var s = Vector3.Cross(direction, normal);
            if (!facing.TransformZ) s -= facing.CameraForward * Vector3.Dot(s, facing.CameraForward);
            return s.normalized;
        }

        /// <summary>The texture coordinate along the strip: distance over length (Stretch), distance
        /// (Tile, Static), point index over count (DistributePerSegment) or index
        /// (RepeatPerSegment), times the scale; a loop's indices count from 1.</summary>
        float U(LineParameters p, int i, int n, bool loop, float total)
        {
            switch (p.TextureMode)
            {
                case LineTextureMode.Tile:
                case LineTextureMode.Static:
                    return m_Dist[i] * p.TextureScale.x;
                case LineTextureMode.DistributePerSegment:
                    return (loop ? (float)(i + 1) / (n + 1) : (float)i / (n - 1)) * p.TextureScale.x;
                case LineTextureMode.RepeatPerSegment:
                    return (loop ? i + 1 : i) * p.TextureScale.x;
                default:
                    return (total > 0f ? m_Dist[i] / total : 0f) * p.TextureScale.x;
            }
        }

        void Add(Vector3 position, Vector2 uv, Color32 color, Vector3 normal, Vector3 tangent)
        {
            m_Vertices.Add(position);
            m_Uvs.Add(uv);
            m_Colors.Add(color);
            if (!m_Lighting) return;
            m_Normals.Add(normal);
            m_Tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
        }

        void Tri(int a, int b, int c)
        {
            m_Triangles.Add(a);
            m_Triangles.Add(b);
            m_Triangles.Add(c);
        }

        /// <summary>A half disc closing one end: its centre, then an arc from the end pair's plus
        /// vertex around the end to its minus vertex, in the strip's facing plane. At a negative
        /// width the disc turns back over the strip (measured, line.width.negative). TransformZ
        /// trails use the opposite cap plane and fans: the old nonzero caps establish the plane,
        /// and the independent capcount pair establishes both fans at counts one and two.</summary>
        void Cap(Vector3 center, Pair pair, Vector3 direction, bool start, bool negative, int caps, float va, float vb, LineFacing facing, Vector3 normal)
        {
            int at = m_Vertices.Count;
            Add(center, new Vector2(pair.U, 0.5f), pair.Color, normal, pair.Tangent);
            var radius = pair.B - center;
            float r = radius.magnitude;
            var plane = facing.TransformZ ? facing.TransformForward : facing.CameraForward;
            var outward = Vector3.Cross(plane, radius).normalized;
            if (Vector3.Dot(outward, direction) < 0f) outward = -outward;
            if (negative) outward = -outward;
            if (facing.ReverseCaps) outward = -outward;
            var back = outward * (start ? -r : r);
            for (int k = 0; k < caps + 2; k++)
            {
                float angle = Mathf.PI * k / (caps + 1);
                float v = vb + (va - vb) * k / (caps + 1);
                Add(center + radius * Mathf.Cos(angle) + back * Mathf.Sin(angle), new Vector2(pair.U, v), pair.Color, normal, pair.Tangent);
            }
            for (int k = 0; k < caps + 1; k++)
            {
                if (start != facing.ReverseCaps) Tri(at, at + 2 + k, at + 1 + k);
                else Tri(at, at + 1 + k, at + 2 + k);
            }
        }

        /// <summary>Writes the strip, its vertices taken through <paramref name="toMesh"/>.</summary>
        internal void WriteTo(Mesh mesh, Matrix4x4 toMesh)
        {
            mesh.Clear();
            var vertices = m_Vertices.ToArray();
            bool identity = toMesh.isIdentity;
            if (!identity)
                for (int i = 0; i < vertices.Length; i++) vertices[i] = toMesh.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;
            if (m_Lighting)
            {
                var normals = m_Normals.ToArray();
                var tangents = m_Tangents.ToArray();
                if (!identity)
                    for (int i = 0; i < normals.Length; i++)
                    {
                        normals[i] = toMesh.MultiplyVector(normals[i]).normalized;
                        var t = toMesh.MultiplyVector(tangents[i]);
                        tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                    }
                mesh.normals = normals;
                mesh.tangents = tangents;
            }
            mesh.colors32 = m_Colors.ToArray();
            mesh.uv = m_Uvs.ToArray();
            mesh.triangles = m_Triangles.ToArray();
            mesh.RecalculateBounds();
        }
    }

    /// <summary>The shared behaviour of LineRenderer and TrailRenderer: a strip mesh per camera.</summary>
    internal static class LineDrawing
    {
        internal static Mesh MeshFor(Dictionary<Camera, Mesh> meshes, Camera camera, string name)
        {
            if (camera == null) camera = Camera.main;
            if (camera != null && meshes.TryGetValue(camera, out var mesh) && mesh != null) return mesh;
            if (meshes.Count > 8)
            {
                var dead = new List<Camera>();
                foreach (var pair in meshes)
                    if (pair.Key == null) dead.Add(pair.Key);
                foreach (var c in dead) meshes.Remove(c);
            }
            mesh = new Mesh { name = name };
            if (camera != null) meshes[camera] = mesh;
            return mesh;
        }

        internal static LineFacing Facing(LineParameters p, Vector3 cameraPosition, Vector3 cameraForward, bool trail = false) => new LineFacing
        {
            TransformZ = p.Alignment == LineAlignment.TransformZ,
            ReverseCaps = trail && p.Alignment == LineAlignment.TransformZ,
            TransformForward = Vector3.forward,
            CameraPosition = cameraPosition,
            CameraForward = cameraForward.normalized,
        };

        internal static Bounds BoundsOf(IReadOnlyList<Vector3> worldPoints, float width, Matrix4x4 worldToLocal)
        {
            if (worldPoints.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var b = new Bounds(worldToLocal.MultiplyPoint3x4(worldPoints[0]), Vector3.zero);
            for (int i = 1; i < worldPoints.Count; i++) b.Encapsulate(worldToLocal.MultiplyPoint3x4(worldPoints[i]));
            b.Expand(width);
            return b;
        }

        /// <summary>The transform's rotation and scale without its position (what BakeMesh's
        /// useTransform applies to a local-space strip, measured).</summary>
        internal static Matrix4x4 RotationScale(Transform transform)
        {
            var m = transform.localToWorldMatrix;
            m.m03 = m.m13 = m.m23 = 0f;
            return m;
        }
    }

    /// <summary>A strip through a list of points, in world or local space.</summary>
    [ClassId(120)]
    public sealed partial class LineRenderer : Renderer
    {
        internal LineParameters m_Parameters = new LineParameters();
        internal List<Vector3> m_Positions = new List<Vector3> { Vector3.zero, new Vector3(0f, 0f, 1f) };
        bool m_UseWorldSpace = true;
        bool m_Loop;
        SpriteMaskInteraction m_MaskInteraction;
        readonly LineStrip m_Strip = new LineStrip();
        readonly List<Vector3> m_World = new List<Vector3>();
        readonly Dictionary<Camera, Mesh> m_Meshes = new Dictionary<Camera, Mesh>();

        public LineRenderer() { }

        public float startWidth { get => m_Parameters.StartWidth; set => m_Parameters.StartWidth = value; }
        public float endWidth { get => m_Parameters.EndWidth; set => m_Parameters.EndWidth = value; }
        public float widthMultiplier { get => m_Parameters.WidthMultiplier; set => m_Parameters.WidthMultiplier = value; }
        public int numCornerVertices { get => m_Parameters.NumCornerVertices; set => m_Parameters.NumCornerVertices = value; }
        public int numCapVertices { get => m_Parameters.NumCapVertices; set => m_Parameters.NumCapVertices = value; }
        public bool useWorldSpace { get => m_UseWorldSpace; set => m_UseWorldSpace = value; }
        public bool loop { get => m_Loop; set => m_Loop = value; }
        public Color startColor { get => m_Parameters.StartColor; set => m_Parameters.StartColor = value; }
        public Color endColor { get => m_Parameters.EndColor; set => m_Parameters.EndColor = value; }
        public Vector2 textureScale { get => m_Parameters.TextureScale; set => m_Parameters.TextureScale = value; }
        public float shadowBias { get => m_Parameters.ShadowBias; set => m_Parameters.ShadowBias = value; }
        public bool generateLightingData { get => m_Parameters.GenerateLightingData; set => m_Parameters.GenerateLightingData = value; }
        public LineTextureMode textureMode { get => m_Parameters.TextureMode; set => m_Parameters.TextureMode = value; }
        public LineAlignment alignment { get => m_Parameters.Alignment; set => m_Parameters.Alignment = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        internal override SpriteMaskInteraction MaskInteraction => m_MaskInteraction;
        public AnimationCurve widthCurve { get => m_Parameters.WidthCurve; set => m_Parameters.WidthCurve = value ?? new AnimationCurve(new Keyframe(0f, 1f)); }
        public Gradient colorGradient { get => m_Parameters.ColorGradient; set => m_Parameters.ColorGradient = value ?? new Gradient(); }

        public int positionCount
        {
            get => m_Positions.Count;
            set
            {
                value = Mathf.Max(0, value);
                while (m_Positions.Count > value) m_Positions.RemoveAt(m_Positions.Count - 1);
                while (m_Positions.Count < value) m_Positions.Add(Vector3.zero);
            }
        }

        [Obsolete("Use positionCount instead (UnityUpgradable) -> positionCount", false)]
        public int numPositions { get => positionCount; set => positionCount = value; }

        [Obsolete("Use startWidth, endWidth or widthCurve instead.", false)]
        public void SetWidth(float start, float end)
        {
            startWidth = start;
            endWidth = end;
        }

        [Obsolete("Use startColor, endColor or colorGradient instead.", false)]
        public void SetColors(Color start, Color end)
        {
            startColor = start;
            endColor = end;
        }

        [Obsolete("Use positionCount instead.", false)]
        public void SetVertexCount(int count) => positionCount = count;

        // Out-of-range indices and short arrays log an error instead of throwing (measured).

        public void SetPosition(int index, Vector3 position)
        {
            if (index < 0 || index >= m_Positions.Count)
            {
                Debug.LogError("LineRenderer.SetPosition index out of bounds!");
                return;
            }
            m_Positions[index] = position;
        }

        public Vector3 GetPosition(int index)
        {
            if (index < 0 || index >= m_Positions.Count)
            {
                Debug.LogError("LineRenderer.GetPosition index out of bounds!");
                return Vector3.zero;
            }
            return m_Positions[index];
        }

        int CopyCount(int length)
        {
            if (length < m_Positions.Count) Debug.LogError("LineRenderer.GetPositions array is too small!");
            return Math.Min(length, m_Positions.Count);
        }

        public int GetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            int n = CopyCount(positions.Length);
            m_Positions.CopyTo(0, positions, 0, n);
            return n;
        }

        public void SetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            int n = Math.Min(positions.Length, m_Positions.Count);
            for (int i = 0; i < n; i++) m_Positions[i] = positions[i];
        }

        public void SetPositions(NativeArray<Vector3> positions) => SetPositions(positions.ToArray());

        public void SetPositions(NativeSlice<Vector3> positions) => SetPositions(positions.ToArray());

        public int GetPositions(NativeArray<Vector3> positions)
        {
            int n = CopyCount(positions.Length);
            for (int i = 0; i < n; i++) positions[i] = m_Positions[i];
            return n;
        }

        public int GetPositions(NativeSlice<Vector3> positions)
        {
            int n = CopyCount(positions.Length);
            for (int i = 0; i < n; i++) positions[i] = m_Positions[i];
            return n;
        }

        /// <summary>Drops points that move the line less than a tolerance (Ramer-Douglas-Peucker).</summary>
        public void Simplify(float tolerance)
        {
            var kept = new List<Vector3>();
            LineSimplifier.Simplify(m_Positions, tolerance, kept);
            m_Positions = kept;
        }

        List<Vector3> WorldPoints()
        {
            m_World.Clear();
            if (m_UseWorldSpace) m_World.AddRange(m_Positions);
            else
            {
                var m = transform.localToWorldMatrix;
                foreach (var p in m_Positions) m_World.Add(m.MultiplyPoint3x4(p));
            }
            return m_World;
        }

        /// <summary>Builds the strip in the line's own space (world, or local for a local-space
        /// line) as seen from a camera. A local line facing the view is built from its world
        /// points (widths are world units) and taken back to local space; facing its Z axis it is
        /// built in local space. Returns the matrix from build space to line space.</summary>
        bool BuildStrip(Vector3 cameraPosition, Vector3 cameraForward, out Matrix4x4 toLineSpace, bool rendering = false)
        {
            toLineSpace = Matrix4x4.identity;
            var facing = LineDrawing.Facing(m_Parameters, cameraPosition, cameraForward);
            // BakeMesh uses the build space's Z axis. Rendering a world-space TransformZ line
            // uses its object's forward instead; local lines receive that rotation in their model.
            // Existing tilted image: tests/projects/lines img.TransformZ.L.c2cap2.color.
            if (rendering && m_UseWorldSpace && facing.TransformZ) facing.TransformForward = transform.forward;
            if (m_UseWorldSpace || facing.TransformZ)
                return m_Strip.Build(m_Positions, null, m_Loop, m_Parameters, facing);
            toLineSpace = transform.worldToLocalMatrix;
            return m_Strip.Build(WorldPoints(), null, m_Loop, m_Parameters, facing);
        }

        public void BakeMesh(Mesh mesh, [Internal.DefaultValue("false")] bool useTransform = false) => BakeMesh(mesh, Camera.main, useTransform);

        /// <summary>The strip as seen from <paramref name="camera"/>: world-space lines bake in world
        /// space; local-space lines in local space, with the transform's rotation and scale (not its
        /// position) when <paramref name="useTransform"/> is set (measured).</summary>
        public void BakeMesh(Mesh mesh, Camera camera, [Internal.DefaultValue("false")] bool useTransform = false)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var ct = camera.transform;
            if (!BuildStrip(ct.position, ct.forward, out var toLine))
            {
                mesh.Clear();
                return;
            }
            var toMesh = !m_UseWorldSpace && useTransform ? LineDrawing.RotationScale(transform) * toLine : toLine;
            m_Strip.WriteTo(mesh, toMesh);
        }

        internal override Bounds GetLocalBounds() =>
            LineDrawing.BoundsOf(WorldPoints(), Mathf.Abs(m_Parameters.WidthMultiplier), transform.worldToLocalMatrix);

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!BuildStrip(camera.Position, camera.Forward, out var toLine, rendering: true)) return;
            var mesh = LineDrawing.MeshFor(m_Meshes, camera.Camera, "LineRenderer");
            m_Strip.WriteTo(mesh, toLine);
            FrameBuilder.AddMeshDraws(this, mesh, m_UseWorldSpace ? Matrix4x4.identity : transform.localToWorldMatrix, Color.white, camera, into);
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            foreach (var mesh in m_Meshes.Values) if (mesh != null) DestroyImmediate(mesh);
            m_Meshes.Clear();
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            m_Parameters.Read(body.Get("m_Parameters"));
            m_Positions = new List<Vector3>();
            foreach (var item in body.Get("m_Positions")?.Seq() ?? Array.Empty<YNode>())
                m_Positions.Add(new Vector3(item.Float("x"), item.Float("y"), item.Float("z")));
            m_UseWorldSpace = body.Bool("m_UseWorldSpace", true);
            m_Loop = body.Bool("m_Loop");
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (LineRenderer)source;
            CopyRendererData(s);
            m_Parameters = s.m_Parameters.Clone();
            m_Positions = new List<Vector3>(s.m_Positions);
            m_UseWorldSpace = s.m_UseWorldSpace;
            m_Loop = s.m_Loop;
            m_MaskInteraction = s.m_MaskInteraction;
        }
    }

    /// <summary>
    /// A strip behind a moving object (measured, tests/projects/lines trail.*). When its object has
    /// moved since the trail last looked (an API read, or once a frame before rendering), the trail
    /// first records the previous position if it has no points, then the new position once it is
    /// at least minVertexDistance from the newest point; points are stamped with Time.time and
    /// remember whether the trail was emitting (non-emitting points are drawn at zero width).
    /// On movement or camera collection the oldest point is dropped while the next one is older than <see cref="time"/>,
    /// and a last point when it is. The strip runs from the object's position through the points,
    /// newest first; the oldest point is drawn moved towards the next one by the fraction of their
    /// time difference it has outlived.
    /// </summary>
    [ClassId(96)]
    public sealed partial class TrailRenderer : Renderer
    {
        struct Point
        {
            internal Vector3 Position;
            internal float Time;
            internal bool Visible;
        }

        internal LineParameters m_Parameters = new LineParameters();
        float m_Time = 5f;
        float m_MinVertexDistance = 0.1f;
        bool m_Autodestruct;
        bool m_Emitting = true;
        float m_PreviewTimeScale = 1f;
        SpriteMaskInteraction m_MaskInteraction;
        /// <summary>Oldest first.</summary>
        readonly List<Point> m_Points = new List<Point>();
        Vector3 m_LastPosition;
        bool m_HasLastPosition;
        bool m_HadPoints;
        readonly LineStrip m_Strip = new LineStrip();
        readonly List<Vector3> m_Draw = new List<Vector3>();
        readonly List<bool> m_DrawHidden = new List<bool>();
        readonly Dictionary<Camera, Mesh> m_Meshes = new Dictionary<Camera, Mesh>();

        public TrailRenderer() { }

        public float time { get => m_Time; set => m_Time = value; }
        internal float previewTimeScale { get => m_PreviewTimeScale; set => m_PreviewTimeScale = value; }
        public float startWidth { get => m_Parameters.StartWidth; set => m_Parameters.StartWidth = value; }
        public float endWidth { get => m_Parameters.EndWidth; set => m_Parameters.EndWidth = value; }
        public float widthMultiplier { get => m_Parameters.WidthMultiplier; set => m_Parameters.WidthMultiplier = value; }
        public bool autodestruct { get => m_Autodestruct; set => m_Autodestruct = value; }
        public int numCornerVertices { get => m_Parameters.NumCornerVertices; set => m_Parameters.NumCornerVertices = value; }
        public int numCapVertices { get => m_Parameters.NumCapVertices; set => m_Parameters.NumCapVertices = value; }
        public float minVertexDistance { get => m_MinVertexDistance; set => m_MinVertexDistance = value; }
        public Color startColor { get => m_Parameters.StartColor; set => m_Parameters.StartColor = value; }
        public Color endColor { get => m_Parameters.EndColor; set => m_Parameters.EndColor = value; }
        public Vector2 textureScale { get => m_Parameters.TextureScale; set => m_Parameters.TextureScale = value; }
        public float shadowBias { get => m_Parameters.ShadowBias; set => m_Parameters.ShadowBias = value; }
        public bool generateLightingData { get => m_Parameters.GenerateLightingData; set => m_Parameters.GenerateLightingData = value; }
        public LineTextureMode textureMode { get => m_Parameters.TextureMode; set => m_Parameters.TextureMode = value; }
        public LineAlignment alignment { get => m_Parameters.Alignment; set => m_Parameters.Alignment = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        internal override SpriteMaskInteraction MaskInteraction => m_MaskInteraction;
        public AnimationCurve widthCurve { get => m_Parameters.WidthCurve; set => m_Parameters.WidthCurve = value ?? new AnimationCurve(new Keyframe(0f, 1f)); }
        public Gradient colorGradient { get => m_Parameters.ColorGradient; set => m_Parameters.ColorGradient = value ?? new Gradient(); }

        public bool emitting { get => m_Emitting; set => m_Emitting = value; }

        public int positionCount
        {
            get
            {
                TrackMovement();
                return m_Points.Count;
            }
        }

        [Obsolete("Use positionCount instead (UnityUpgradable) -> positionCount", false)]
        public int numPositions => positionCount;

        static float Now => Time.time;

        /// <summary>Records the object's movement since the trail last looked; a move also drops
        /// the points that have outlived the trail's time (measured: a moving trail ages when
        /// it is read; stationary expiry is handled by camera collection).</summary>
        void TrackMovement()
        {
            if (m_GameObject == null) return;
            var position = transform.position;
            if (!m_HasLastPosition)
            {
                m_LastPosition = position;
                m_HasLastPosition = true;
                return;
            }
            if (position == m_LastPosition) return;
            float now = Now;
            if (m_Points.Count == 0) m_Points.Add(new Point { Position = m_LastPosition, Time = now, Visible = m_Emitting });
            if ((position - m_Points[m_Points.Count - 1].Position).sqrMagnitude >= m_MinVertexDistance * m_MinVertexDistance)
                m_Points.Add(new Point { Position = position, Time = now, Visible = m_Emitting });
            m_LastPosition = position;
            Age(now);
        }

        /// <summary>Drops the oldest point while the next one is older than <see cref="time"/>,
        /// and a last point that is.</summary>
        void Age(float now)
        {
            int expired = 0;
            while (m_Points.Count - expired >= 2 && now - m_Points[expired + 1].Time > m_Time) expired++;
            if (expired > 0) m_Points.RemoveRange(0, expired);
            if (m_Points.Count == 1 && now - m_Points[0].Time > m_Time) m_Points.Clear();
            if (m_Points.Count > 0) m_HadPoints = true;
        }

        /// <summary>Once a frame, before rendering: record movement. Stationary expiry belongs
        /// to camera collection, not this unconditional update.</summary>
        internal void UpdateTrail() => TrackMovement();

        /// <summary>The strip's points from the object (head) to the oldest point.</summary>
        List<Vector3> DrawPoints()
        {
            TrackMovement();
            m_Draw.Clear();
            m_DrawHidden.Clear();
            int count = m_Points.Count;
            if (count == 0 || m_GameObject == null) return m_Draw;
            m_Draw.Add(transform.position);
            m_DrawHidden.Add(!m_Emitting);
            for (int i = count - 1; i >= 1; i--)
            {
                m_Draw.Add(m_Points[i].Position);
                m_DrawHidden.Add(!m_Points[i].Visible);
            }
            var oldest = m_Points[0];
            var tail = oldest.Position;
            if (count >= 2)
            {
                var next = m_Points[1];
                float over = Now - m_Time - oldest.Time;
                float span = next.Time - oldest.Time;
                float f = span > 0f ? over / span : (over > 0f ? 1f : 0f);
                tail = Vector3.Lerp(oldest.Position, next.Position, Mathf.Clamp01(f));
            }
            m_Draw.Add(tail);
            m_DrawHidden.Add(!oldest.Visible);
            return m_Draw;
        }

        public void Clear()
        {
            m_Points.Clear();
            if (m_GameObject != null)
            {
                m_LastPosition = transform.position;
                m_HasLastPosition = true;
            }
        }

        // Setting or reading points first records the object's pending movement, as the other
        // API reads do (measured: a point set after a move is not the one the move aged out).

        public void SetPosition(int index, Vector3 position)
        {
            TrackMovement();
            if (index < 0 || index >= m_Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var p = m_Points[index];
            p.Position = position;
            m_Points[index] = p;
        }

        public Vector3 GetPosition(int index)
        {
            TrackMovement();
            if (index < 0 || index >= m_Points.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return m_Points[index].Position;
        }

        public int GetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            int n = Math.Min(positions.Length, m_Points.Count);
            for (int i = 0; i < n; i++) positions[i] = m_Points[i].Position;
            return n;
        }

        /// <summary>The points recorded while emitting, oldest first.</summary>
        public int GetVisiblePositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            int n = 0;
            foreach (var p in m_Points)
            {
                if (!p.Visible) continue;
                if (n >= positions.Length) break;
                positions[n++] = p.Position;
            }
            return n;
        }

        /// <summary>Replaces the positions of the first points (as many as both have).</summary>
        public void SetPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            int n = Math.Min(positions.Length, m_Points.Count);
            for (int i = 0; i < n; i++)
            {
                var p = m_Points[i];
                p.Position = positions[i];
                m_Points[i] = p;
            }
        }

        public void AddPosition(Vector3 position) => AddPositions(new[] { position });

        /// <summary>Appends points after the object's pending movement, stamped evenly between the
        /// newest point's time and now, short of now (measured: n points at the (i+1)/(n+1) steps).</summary>
        public void AddPositions(Vector3[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            TrackMovement();
            float now = Now;
            float from = m_Points.Count > 0 ? m_Points[m_Points.Count - 1].Time : now;
            for (int i = 0; i < positions.Length; i++)
                m_Points.Add(new Point { Position = positions[i], Time = from + (now - from) * (i + 1) / (positions.Length + 1), Visible = m_Emitting });
        }

        public void SetPositions(NativeArray<Vector3> positions) => SetPositions(positions.ToArray());
        public void SetPositions(NativeSlice<Vector3> positions) => SetPositions(positions.ToArray());
        public void AddPositions(NativeArray<Vector3> positions) => AddPositions(positions.ToArray());
        public void AddPositions(NativeSlice<Vector3> positions) => AddPositions(positions.ToArray());

        public int GetPositions(NativeArray<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetPositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public int GetPositions(NativeSlice<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetPositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public int GetVisiblePositions(NativeArray<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetVisiblePositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public int GetVisiblePositions(NativeSlice<Vector3> positions)
        {
            var copy = new Vector3[positions.Length];
            int n = GetVisiblePositions(copy);
            for (int i = 0; i < n; i++) positions[i] = copy[i];
            return n;
        }

        public void BakeMesh(Mesh mesh, [Internal.DefaultValue("false")] bool useTransform = false) => BakeMesh(mesh, Camera.main, useTransform);

        /// <summary>The trail's strip in world space as seen from <paramref name="camera"/>.</summary>
        public void BakeMesh(Mesh mesh, Camera camera, [Internal.DefaultValue("false")] bool useTransform = false)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            var ct = camera.transform;
            if (!m_Strip.Build(DrawPoints(), m_DrawHidden, false, m_Parameters, LineDrawing.Facing(m_Parameters, ct.position, ct.forward, trail: true)))
            {
                mesh.Clear();
                return;
            }
            m_Strip.WriteTo(mesh, Matrix4x4.identity);
        }

        internal override Bounds GetLocalBounds() =>
            LineDrawing.BoundsOf(DrawPoints(), Mathf.Abs(m_Parameters.WidthMultiplier), transform.worldToLocalMatrix);

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            // Native's culling callback allows automatic destruction in RemoveOldPoints, before
            // checking for an empty strip. Movement/bounds and BakeMesh cannot make this request.
            // Source proof: tests/projects/lines/TRAIL_AUTODESTRUCT_NATIVE.md, Unity 6000.0.71f1.
            TrackMovement();
            Age(Now);
            if (m_Points.Count == 0 && m_HadPoints && m_Autodestruct && Application.isPlaying)
                TrailSystem.RequestDestroy(m_GameObject);
            if (!m_Strip.Build(DrawPoints(), m_DrawHidden, false, m_Parameters, LineDrawing.Facing(m_Parameters, camera.Position, camera.Forward, trail: true))) return;
            var mesh = LineDrawing.MeshFor(m_Meshes, camera.Camera, "TrailRenderer");
            m_Strip.WriteTo(mesh, Matrix4x4.identity);
            FrameBuilder.AddMeshDraws(this, mesh, Matrix4x4.identity, Color.white, camera, into);
        }

        internal override void OnEngineEnable()
        {
            base.OnEngineEnable();
            m_LastPosition = transform.position;
            m_HasLastPosition = true;
            TrailSystem.Add(this);
        }

        internal override void OnEngineDisable()
        {
            base.OnEngineDisable();
            TrailSystem.Remove(this);
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            TrailSystem.Remove(this);
            foreach (var mesh in m_Meshes.Values) if (mesh != null) DestroyImmediate(mesh);
            m_Meshes.Clear();
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            m_Time = body.Float("m_Time", 5f);
            m_PreviewTimeScale = body.Float("m_PreviewTimeScale", 1f);
            m_Parameters.Read(body.Get("m_Parameters"));
            m_MinVertexDistance = body.Float("m_MinVertexDistance", 0.1f);
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
            m_Autodestruct = body.Bool("m_Autodestruct");
            m_Emitting = body.Bool("m_Emitting", true);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (TrailRenderer)source;
            CopyRendererData(s);
            m_Parameters = s.m_Parameters.Clone();
            m_Time = s.m_Time;
            m_PreviewTimeScale = s.m_PreviewTimeScale;
            m_MinVertexDistance = s.m_MinVertexDistance;
            m_MaskInteraction = s.m_MaskInteraction;
            m_Autodestruct = s.m_Autodestruct;
            m_Emitting = s.m_Emitting;
        }

        /// <summary>The enabled trails, updated once a frame before rendering.</summary>
        static class TrailSystem
        {
            static readonly List<TrailRenderer> s_Trails = new List<TrailRenderer>();
            static readonly List<TrailRenderer> s_Snapshot = new List<TrailRenderer>();
            static readonly Queue<GameObject> s_DestroyRequests = new Queue<GameObject>();
            static readonly HashSet<GameObject> s_DestroyTargets = new HashSet<GameObject>();
            static bool s_Registered;
            static bool s_Draining;

            internal static void Add(TrailRenderer trail)
            {
                if (!s_Registered)
                {
                    s_Registered = true;
                    PlayerLoopDriver.RegisterLast(typeof(PL.PostLateUpdate.UpdateAllRenderers), UpdateAll);
                    // Native automatic requests use mode 0x43: fixed mask 1 and dynamic mask 2
                    // consume them; startup mask 4 and EOF mask 0x20 do not. Prepend so existing
                    // Invokes/coroutines at these same nodes observe the completed destruction.
                    PlayerLoopDriver.RegisterFirst(typeof(PL.FixedUpdate.ScriptRunDelayedFixedFrameRate), DrainDestroyRequests);
                    PlayerLoopDriver.RegisterFirst(typeof(PL.Update.ScriptRunDelayedDynamicFrameRate), DrainDestroyRequests);
                    PlayerLoopDriver.RegisterFirst(typeof(PL.PostLateUpdate.ScriptRunDelayedDynamicFrameRate), DrainDestroyRequests);
                }
                if (!s_Trails.Contains(trail)) s_Trails.Add(trail);
            }

            internal static void Remove(TrailRenderer trail) => s_Trails.Remove(trail);

            internal static void RequestDestroy(GameObject target)
            {
                // Capture the target, not a component whose later state could cancel the request.
                // Deduplication bounds repeated camera collections to one pending entry per target.
                if (target != null && s_DestroyTargets.Add(target)) s_DestroyRequests.Enqueue(target);
            }

            static void DrainDestroyRequests()
            {
                if (s_Draining || s_DestroyRequests.Count == 0) return;
                s_Draining = true;
                try
                {
                    int count = s_DestroyRequests.Count;
                    // A fixed snapshot: callbacks can enqueue more, but cannot consume those new
                    // requests in this pass, even by reentering a matching node.
                    for (int i = 0; i < count; i++)
                    {
                        var target = s_DestroyRequests.Dequeue();
                        s_DestroyTargets.Remove(target);
                        // Scene unload and leaving Play mode already destroy their GameObjects.
                        // Retaining the reference until this pass cannot target a replacement object.
                        if (target == null) continue;
                        try { Destruction.DestroyImmediate(target); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                }
                finally { s_Draining = false; }
            }

            static void UpdateAll()
            {
                s_Snapshot.Clear();
                s_Snapshot.AddRange(s_Trails);
                foreach (var trail in s_Snapshot)
                    if (trail != null) trail.UpdateTrail();
            }
        }
    }

    /// <summary>Ramer-Douglas-Peucker simplification (LineUtility.Simplify, LineRenderer.Simplify).</summary>
    internal static class LineSimplifier
    {
        internal static void Keep(IReadOnlyList<Vector3> points, float tolerance, List<int> keep)
        {
            keep.Clear();
            if (points.Count == 0) return;
            if (points.Count < 3)
            {
                for (int i = 0; i < points.Count; i++) keep.Add(i);
                return;
            }
            var flags = new bool[points.Count];
            flags[0] = flags[points.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, points.Count - 1));
            while (stack.Count > 0)
            {
                var (first, last) = stack.Pop();
                float worst = 0f;
                int index = -1;
                for (int i = first + 1; i < last; i++)
                {
                    float d = DistanceToSegment(points[i], points[first], points[last]);
                    if (d > worst)
                    {
                        worst = d;
                        index = i;
                    }
                }
                if (index >= 0 && worst > tolerance)
                {
                    flags[index] = true;
                    stack.Push((first, index));
                    stack.Push((index, last));
                }
            }
            for (int i = 0; i < flags.Length; i++)
                if (flags[i]) keep.Add(i);
        }

        internal static void Simplify(IReadOnlyList<Vector3> points, float tolerance, List<Vector3> result)
        {
            var keep = new List<int>();
            Keep(points, tolerance, keep);
            result.Clear();
            foreach (var i in keep) result.Add(points[i]);
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            if (len <= 0f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
            return Vector3.Distance(p, a + ab * t);
        }
    }
}

namespace UnityEngine
{
    public sealed partial class LineUtility
    {
        public LineUtility() { }

        public static void Simplify(List<Vector3> points, float tolerance, List<int> pointsToKeep)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (pointsToKeep == null) throw new ArgumentNullException(nameof(pointsToKeep));
            LineSimplifier.Keep(points, tolerance, pointsToKeep);
        }

        public static void Simplify(List<Vector3> points, float tolerance, List<Vector3> simplifiedPoints)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (simplifiedPoints == null) throw new ArgumentNullException(nameof(simplifiedPoints));
            LineSimplifier.Simplify(points, tolerance, simplifiedPoints);
        }

        public static void Simplify(List<Vector2> points, float tolerance, List<int> pointsToKeep)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (pointsToKeep == null) throw new ArgumentNullException(nameof(pointsToKeep));
            LineSimplifier.Keep(points.ConvertAll(p => (Vector3)p), tolerance, pointsToKeep);
        }

        public static void Simplify(List<Vector2> points, float tolerance, List<Vector2> simplifiedPoints)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (simplifiedPoints == null) throw new ArgumentNullException(nameof(simplifiedPoints));
            var keep = new List<int>();
            LineSimplifier.Keep(points.ConvertAll(p => (Vector3)p), tolerance, keep);
            simplifiedPoints.Clear();
            foreach (var i in keep) simplifiedPoints.Add(points[i]);
        }
    }
}
