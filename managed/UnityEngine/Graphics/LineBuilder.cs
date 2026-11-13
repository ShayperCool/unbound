using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Where a line or trail strip is built and where its vertices go. The strip is built in the
    /// xy plane of a build space: the camera's view space for View alignment, or a frame on the
    /// transform's axes for TransformZ. <see cref="ToBuild"/> takes the input points there and
    /// <see cref="FromBuild"/> takes the finished vertices to the output space.
    /// </summary>
    internal struct LineSpace
    {
        internal Matrix4x4 ToBuild;
        internal Matrix4x4 FromBuild;
        /// <summary>Subtracted from the travelled distance of LineTextureMode.Static.</summary>
        internal float StaticOffset;

        /// <summary>
        /// The build space of a strip (matches the Unity 6000.0.71f1 Editor, tests/projects/lines).
        /// View: the camera's view space, an orthographic camera's moved forward by its near plane
        /// distance (the side of each segment is taken against the direction from the camera to the
        /// point, so this keeps orthographic strips from facing exactly along the view).
        /// TransformZ: the frame of <paramref name="frame"/>'s axes with z reversed, from
        /// <paramref name="origin"/>; a point with a positive z there is behind it, which turns its
        /// corners and caps the other way (see <see cref="TransformZOrigin"/>).
        /// <paramref name="inputToWorld"/> and <paramref name="worldToOutput"/> wrap both.
        /// </summary>
        internal static LineSpace Of(LineParameters p, Matrix4x4 view, bool orthographic, float near, Matrix4x4 frame,
            Vector3 origin, Matrix4x4 inputToWorld, Matrix4x4 worldToOutput)
        {
            Matrix4x4 toBuild, fromBuild;
            if (p.Alignment == LineAlignment.View)
            {
                if (orthographic) view = Matrix4x4.Translate(new Vector3(0f, 0f, near)) * view;
                toBuild = view;
                fromBuild = view.inverseDoublePrecision;
            }
            else
            {
                Vector3 x = frame.GetColumn(0), y = frame.GetColumn(1), z = frame.GetColumn(2);
                fromBuild = new Matrix4x4(x, y, -z, new Vector4(origin.x, origin.y, origin.z, 1f));
                toBuild = fromBuild.inverseDoublePrecision;
            }
            return new LineSpace { ToBuild = toBuild * inputToWorld, FromBuild = worldToOutput * fromBuild };
        }

        /// <summary>
        /// Where a TransformZ strip's build space starts: one plus the largest half extent of the
        /// renderer's world bounds along the frame's z axis, back from the frame's position for a
        /// line (so its points are in front), but forward from the world origin for a trail, which
        /// ignores the position (matches the Unity 6000.0.71f1 Editor, tests/projects/lines
        /// trail.*.transformZ and capcount.trail.Local.*: a trail near the origin has its points
        /// behind the start, and its caps and corners turned the other way).
        /// </summary>
        internal static Vector3 TransformZOrigin(Matrix4x4 frame, float boundsHalfExtent, bool trail)
        {
            Vector3 z = frame.GetColumn(2);
            var reach = (boundsHalfExtent + 1f) * z;
            return trail ? reach : (Vector3)frame.GetColumn(3) - reach;
        }
    }

    /// <summary>
    /// The mesh of a LineRenderer or TrailRenderer (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/lines; see LineSpace for the space it is built in).
    ///
    /// Each segment gets a side: the 2D unit vector across it in the build plane, taken against the
    /// direction from the camera to its end point (each component at least 0.01 from zero), or
    /// against the build z axis for TransformZ. A segment shorter than twice the strip's widest
    /// width (the largest width curve key times the multiplier) pulls its side and the previous
    /// one towards their mean. A joint's miter is where the two sides' offset lines cross, faded
    /// back to the incoming side as the sides become parallel or opposite, and no longer than
    /// three times the length of the segment before the incoming one over twice the widest width
    /// (at least 1). Each point has a pair of vertices at half its width along its miter (or, at
    /// the ends, its side); with corner vertices a joint turns an arc of that many steps around its
    /// inner vertex, and caps are half discs around the end points. Width and color are sampled at
    /// the travelled fraction of the strip (the point index for the per-segment texture modes).
    /// </summary>
    internal sealed class LineStrip
    {
        /// <summary>The squared segment length below which a segment has no direction of its own;
        /// a two-point strip only drops coincident points.</summary>
        const float ZeroLengthSqr = 1e-5f, ZeroLengthSqrTwoPoints = 1e-30f;
        /// <summary>The smallest magnitude of each component of the direction to the camera.</summary>
        const float MinEyeComponent = 0.01f;
        /// <summary>The longest miter, in half widths, relative to the segment that limits it.</summary>
        const float MiterLimit = 3f;
        /// <summary>Slopes of the miter's offset lines are held within this magnitude.</summary>
        const float MaxSlope = 100000f;
        /// <summary>Below this slope difference the offset lines are taken as parallel (no miter).</summary>
        const float ParallelSlopes = 1e-5f;
        /// <summary>The miter fades over squared side differences below 1/50.</summary>
        const float MiterFade = 50f;

        Vector3[] m_Points = Array.Empty<Vector3>();
        float[] m_Length = Array.Empty<float>();
        float[] m_Ratio = Array.Empty<float>();
        Vector2[] m_Side = Array.Empty<Vector2>();
        Vector2[] m_Miter = Array.Empty<Vector2>();

        internal Vector3[] Positions = Array.Empty<Vector3>();
        internal Vector3[] Normals = Array.Empty<Vector3>();
        internal Vector4[] Tangents = Array.Empty<Vector4>();
        internal Color32[] Colors = Array.Empty<Color32>();
        internal Vector2[] Uvs = Array.Empty<Vector2>();
        internal int[] Indices = Array.Empty<int>();
        internal int VertexCount, IndexCount;
        internal bool Lighting;

        // The point being built.
        Matrix4x4 m_From;
        Vector3 m_Normal;
        Color32 m_Color;
        float m_U, m_VPlus, m_VMinus;
        int m_Stored, m_Corners, m_Caps;
        bool m_Loop;

        static void Grow<T>(ref T[] array, int count)
        {
            if (array.Length < count) array = new T[Math.Max(count, array.Length * 2)];
        }

        /// <summary>Builds the strip through <paramref name="points"/>; <paramref name="widths"/>
        /// (null: all 1) scales each point's width (a trail's points added while it was not emitting
        /// have 0). Returns false when there is nothing to draw (fewer than two points).</summary>
        internal bool Build(IReadOnlyList<Vector3> points, IReadOnlyList<float> widths, bool loop, LineParameters p, in LineSpace space)
        {
            VertexCount = IndexCount = 0;
            Lighting = p.GenerateLightingData;
            int count = points.Count;
            if (count < 2) return false;
            m_Loop = loop;
            // A loop repeats its last point before the first and its first two after the last.
            int stored = loop ? count + 3 : count;
            m_Stored = stored;
            m_Corners = Math.Max(0, p.NumCornerVertices);
            m_Caps = loop ? 0 : Math.Max(0, p.NumCapVertices);
            Grow(ref m_Points, stored);
            Grow(ref m_Length, stored);
            Grow(ref m_Ratio, stored);
            Grow(ref m_Side, stored);
            Grow(ref m_Miter, Math.Max(1, stored - 2));
            int first = loop ? 1 : 0;
            for (int i = 0; i < count; i++) m_Points[first + i] = space.ToBuild.MultiplyPoint3x4(points[i]);
            if (loop)
            {
                m_Points[0] = m_Points[count];
                m_Points[count + 1] = m_Points[1];
                m_Points[count + 2] = m_Points[2];
            }

            float widest = 0f;
            var curve = p.WidthCurve;
            for (int k = 0; k < curve.length; k++) widest = Math.Max(widest, curve[k].value);
            widest *= p.WidthMultiplier;
            float total = Sides(widest, p.Alignment == LineAlignment.TransformZ);
            Miters();

            Reserve(count, loop);
            m_From = space.FromBuild;
            m_Normal = m_From.MultiplyVector(Vector3.forward);
            m_VPlus = 0.5f + 0.5f * p.TextureScale.y;
            m_VMinus = 0.5f - 0.5f * p.TextureScale.y;
            bool perSegment = p.TextureMode == LineTextureMode.DistributePerSegment || p.TextureMode == LineTextureMode.RepeatPerSegment;
            float uScale = p.TextureScale.x;
            if (p.TextureMode == LineTextureMode.Tile) uScale *= total;
            else if (p.TextureMode == LineTextureMode.RepeatPerSegment) uScale *= stored - 1;
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            int end = loop ? stored - 1 : count;
            float travelled = 0f, path = 0f;
            for (int i = first, raw = 0; i < end; i++, raw++)
            {
                float t;
                if (perSegment) t = i * (1f / (stored - 1));
                else
                {
                    travelled += m_Length[i];
                    t = travelled * (1f / total);
                }
                float sample = t >= 0f ? Math.Min(t, 1f) : 0f;
                if (p.TextureMode == LineTextureMode.Static)
                {
                    // The distance travelled along the input points, not the build space.
                    if (raw > 0) path += Vector3.Distance(points[raw % count], points[(raw - 1) % count]);
                    t = path - space.StaticOffset;
                }
                m_U = t * uScale;
                m_Color = VertexColor(p.ColorGradient.Evaluate(sample), linear);
                float width = p.WidthCurve.Evaluate(sample) * p.WidthMultiplier * (widths != null ? widths[i - first] : 1f);
                Point(i, 0.5f * width);
            }
            return true;
        }

        /// <summary>Segment lengths, sides and their blending; returns the strip's length (at least 1e-6).</summary>
        float Sides(float widest, bool transformZ)
        {
            int stored = m_Stored;
            float zero = stored > 2 ? ZeroLengthSqr : ZeroLengthSqrTwoPoints;
            float inverseWidth = 1f / (widest + widest);
            float total = 0f;
            var previous = Vector3.zero;
            bool hasPrevious = false;
            for (int i = 0; i < stored; i++)
            {
                int at = i == 0 ? 1 : i;
                var d = m_Points[at - 1] - m_Points[at];
                float length = d.magnitude;
                bool counted = m_Loop ? i >= 2 && i < stored - 1 : i > 0;
                m_Length[i] = counted ? length : 0f;
                if (counted) total += length;
                m_Ratio[i] = inverseWidth * length;
                // A segment without a direction takes the next one's, else the last direction found.
                if (d.sqrMagnitude < zero)
                {
                    if (at < stored - 1) d = m_Points[at] - m_Points[at + 1];
                    if (d.sqrMagnitude < zero) d = hasPrevious ? previous : Vector3.right;
                    else hasPrevious = true;
                }
                else hasPrevious = true;
                if (hasPrevious) previous = d;
                Vector2 side;
                if (transformZ) side = new Vector2(d.y, -d.x);
                else
                {
                    var e = m_Points[i];
                    float ex = EyeComponent(e.x), ey = EyeComponent(e.y), ez = EyeComponent(e.z);
                    side = new Vector2(d.z * ey - d.y * ez, d.x * ez - d.z * ex);
                }
                m_Side[i] = side * (1f / side.magnitude);
                if (i != 0 && length < widest + widest && (!m_Loop || i < stored - 2))
                {
                    var mean = (m_Side[i] + m_Side[i - 1]) * 0.5f;
                    float keep = Math.Max(m_Ratio[i] - 0.5f, 0f) * 2f;
                    m_Side[i - 1] = Toward(mean, m_Side[i - 1], keep);
                    m_Side[i] = Toward(mean, m_Side[i], keep);
                }
            }
            return Math.Max(total, 1e-6f);
        }

        static float EyeComponent(float v) => v < 0f || (v == 0f && float.IsNegative(v)) ? -Math.Max(-v, MinEyeComponent) : Math.Max(v, MinEyeComponent);

        static Vector2 Toward(Vector2 from, Vector2 to, float t)
        {
            var v = (to - from) * t + from;
            float sqr = v.x * v.x + v.y * v.y;
            return sqr > 1e-30f ? v * (1f / (float)Math.Sqrt(sqr)) : to;
        }

        /// <summary>The miter of each joint (index i for point i + 1), in half widths.</summary>
        void Miters()
        {
            for (int i = 0; i + 2 < m_Stored; i++)
            {
                var a = m_Side[i + 1];
                var b = m_Side[i + 2];
                float limit = Math.Max(1f, MiterLimit * Clamp01(m_Ratio[i]));
                var m = -a;
                float s1 = Slope(-a.x / a.y), s2 = Slope(b.x / -b.y);
                float slopes = s1 - s2;
                if (Math.Abs(slopes) >= ParallelSlopes)
                {
                    // The lines through -a and -b across their sides, y = s x + c.
                    float c1 = -a.y + a.x * s1, c2 = -b.y + b.x * s2;
                    float x = (c2 - c1) / slopes;
                    float fade = Math.Min(Clamp01((b - a).sqrMagnitude * MiterFade), Clamp01((a + b).sqrMagnitude * MiterFade));
                    m = new Vector2((x + a.x) * fade - a.x, (s1 * x + c1 + a.y) * fade - a.y);
                }
                float sqr = m.x * m.x + m.y * m.y;
                if (limit * limit < sqr) m *= limit / (float)Math.Sqrt(sqr);
                m_Miter[i] = m;
            }
        }

        static float Slope(float s) => Math.Min(s > -MaxSlope ? s : -MaxSlope, MaxSlope);

        static float Clamp01(float v) => v > 0f ? Math.Min(v, 1f) : 0f;

        void Point(int i, float half)
        {
            int last = m_Stored - 1;
            if (i > 0 && i < last)
            {
                if (m_Corners > 0) Corner(i, half);
                else Pair(i, half * m_Miter[i - 1], m_Side[Math.Min(i + 1, last)], i > (m_Loop ? 1 : 0), plusFirst: true);
                return;
            }
            var side = m_Side[Math.Min(i + 1, last)];
            if (i == 0 && m_Caps > 0) Cap(i, half, side, end: false);
            Pair(i, half * side, side, i != 0, plusFirst: false);
            if (i == last && m_Caps > 0) Cap(i, half, side, end: true);
        }

        /// <summary>Two vertices at the point plus and minus an offset; the first takes the plus v.</summary>
        void Pair(int i, Vector2 offset, Vector2 side, bool join, bool plusFirst)
        {
            var p = m_Points[i];
            var tangent = new Vector2(side.y, -side.x);
            if (!plusFirst) offset = -offset;
            int at = VertexCount;
            Vertex(new Vector3(p.x + offset.x, p.y + offset.y, p.z), m_VPlus, tangent);
            Vertex(new Vector3(p.x - offset.x, p.y - offset.y, p.z), m_VMinus, tangent);
            if (join)
            {
                Triangle(at - 2, at - 1, at);
                Triangle(at - 1, at + 1, at);
            }
        }

        /// <summary>The rounded joint: corner + 1 pairs on an arc from the incoming side to the
        /// outgoing one around the inner vertex, at the full width. A loop's first point has only
        /// the arc's last pair; its closing point has all of them.</summary>
        void Corner(int i, float half)
        {
            var a = m_Side[i];
            var b = m_Side[i + 1];
            float turn = (float)(Math.Atan2(b.y, b.x) - Math.Atan2(a.y, a.x));
            if (turn < -Mathf.PI) turn += 2f * Mathf.PI;
            if (turn > Mathf.PI) turn -= 2f * Mathf.PI;
            var p = m_Points[i];
            // The inner side: the opposite of the turn's sign, mirrored behind the camera. -0 and the
            // NaN of a side without direction (a TransformZ segment along the frame's z axis) turn
            // like a negative turn, whatever sign the platform gives that NaN (tests/projects/lines
            // edge.*.zParallel).
            float inner = float.IsNegative(turn) || float.IsNaN(turn) ? 1f : -1f;
            if (p.z > 0f) inner = -inner;
            var miter = m_Miter[i - 1];
            var center = new Vector2(p.x + miter.x * inner * half, p.y + miter.y * inner * half);
            float reach = (half + half) * inner;
            float step = turn * (1f / m_Corners);
            bool loopStart = i == 1 && m_Loop;
            float angle = loopStart ? turn : 0f;
            if (!loopStart) Triangle(VertexCount - 2, VertexCount - 1, VertexCount);
            for (int k = loopStart ? m_Corners : 0; k <= m_Corners; k++)
            {
                float s = (float)Math.Sin(angle), c = (float)Math.Cos(angle);
                var outer = new Vector2(center.x + (a.x * c - a.y * s) * reach, center.y + (a.y * c + a.x * s) * reach);
                var v0 = reach < 0f ? outer : center;
                var v1 = reach < 0f ? center : outer;
                var across = v1 - v0;
                var tangent = new Vector2(across.y, -across.x);
                int at = VertexCount;
                Vertex(new Vector3(v0.x, v0.y, p.z), m_VPlus, tangent);
                Vertex(new Vector3(v1.x, v1.y, p.z), m_VMinus, tangent);
                if (!loopStart) Triangle(reach >= 0f || k == 0 ? at - 1 : at - 2, at + 1, at);
                angle += step;
            }
        }

        /// <summary>A half disc of numCapVertices + 2 rim vertices around an end point, from the
        /// side towards the end pair's other vertex (reversed at the end and behind the camera).</summary>
        void Cap(int i, float half, Vector2 side, bool end)
        {
            var p = m_Points[i];
            int steps = m_Caps + 1;
            if (p.z > 0f) end = !end;
            float fraction = 1f / steps;
            float step = (end ? -fraction : fraction) * Mathf.PI;
            var tangent = new Vector2(side.y, -side.x);
            int center = VertexCount;
            Vertex(p, 0.5f, tangent);
            float angle = 0f;
            for (int k = 0; k <= steps; k++)
            {
                float s = (float)Math.Sin(angle), c = (float)Math.Cos(angle);
                var rim = new Vector3(p.x + (side.x * c - side.y * s) * half, p.y + (side.y * c + side.x * s) * half, p.z);
                Vertex(rim, k * fraction * (m_VPlus - m_VMinus) + m_VMinus, tangent);
                if (VertexCount - center > 2)
                {
                    if (end) Triangle(center, VertexCount - 2, VertexCount - 1);
                    else Triangle(center, VertexCount - 1, VertexCount - 2);
                }
                angle += step;
            }
        }

        void Vertex(Vector3 buildPosition, float v, Vector2 tangent)
        {
            int at = VertexCount++;
            if (at >= Positions.Length) GrowVertices(at + 1);
            Positions[at] = m_From.MultiplyPoint3x4(buildPosition);
            Colors[at] = m_Color;
            Uvs[at] = new Vector2(m_U, v);
            if (!Lighting) return;
            Normals[at] = m_Normal;
            var t = m_From.MultiplyVector(new Vector3(tangent.x, tangent.y, 0f));
            Tangents[at] = new Vector4(t.x, t.y, t.z, 1f);
        }

        void Triangle(int a, int b, int c)
        {
            if (IndexCount + 3 > Indices.Length) Grow(ref Indices, IndexCount + 3);
            Indices[IndexCount++] = a;
            Indices[IndexCount++] = b;
            Indices[IndexCount++] = c;
        }

        /// <summary>Room for the strip's vertices and indices: two per point, two per corner step
        /// of each inner joint, and numCapVertices + 3 per cap.</summary>
        void Reserve(int count, bool loop)
        {
            int pairs = count + (loop ? 1 : 0);
            int vertices = 2 * pairs, indices = 6 * pairs - 6;
            int joints = loop ? pairs - 1 : pairs - 2;
            if (m_Corners > 0 && joints > 0)
            {
                vertices += 2 * joints * m_Corners;
                indices += 3 * joints * m_Corners;
            }
            if (m_Caps > 0)
            {
                vertices += 2 * m_Caps + 6;
                indices += 6 * m_Caps + 6;
            }
            GrowVertices(vertices);
            Grow(ref Indices, indices);
        }

        void GrowVertices(int count)
        {
            Grow(ref Positions, count);
            Grow(ref Colors, count);
            Grow(ref Uvs, count);
            if (!Lighting) return;
            Grow(ref Normals, count);
            Grow(ref Tangents, count);
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

        /// <summary>Writes the strip into <paramref name="mesh"/>: a baked mesh gets exactly the
        /// strip, through the public setters; a streamed one (drawn every frame) keeps its
        /// arrays at a capacity and is written without allocating.</summary>
        internal void WriteTo(Mesh mesh, bool streaming)
        {
            int n = VertexCount;
            if (!streaming)
            {
                mesh.Clear();
                if (n > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Positions, 0, n);
                if (Lighting)
                {
                    mesh.SetNormals(Normals, 0, n);
                    mesh.SetTangents(Tangents, 0, n);
                }
                mesh.SetColors(Colors, 0, n);
                mesh.SetUVs(0, Uvs, 0, n);
                mesh.SetTriangles(Indices, 0, IndexCount, 0);
                return;
            }
            int length = Mesh.StreamCapacity(mesh.m_Vertices.Length, n);
            mesh.m_Vertices = Fill(mesh.m_Vertices, Positions, n, length);
            mesh.m_Normals = Lighting ? Fill(mesh.m_Normals, Normals, n, length) : null;
            mesh.m_Tangents = Lighting ? Fill(mesh.m_Tangents, Tangents, n, length) : null;
            mesh.m_Colors = Fill(mesh.m_Colors, Colors, n, length);
            var uv = mesh.m_UVs[0] != null && mesh.m_UVs[0].Length == length ? mesh.m_UVs[0] : new Vector4[length];
            for (int i = 0; i < n; i++) uv[i] = new Vector4(Uvs[i].x, Uvs[i].y, 0f, 0f);
            mesh.m_UVs[0] = uv;
            mesh.m_UVDimensions[0] = 2;
            mesh.WriteStreamedTriangles(Indices, IndexCount, n > ushort.MaxValue, streaming: true);
            mesh.FinishStreamedWrite(n, IndexCount, streaming: true);
        }

        static T[] Fill<T>(T[] current, T[] source, int count, int length)
        {
            var target = current != null && current.Length == length ? current : new T[length];
            Array.Copy(source, target, count);
            return target;
        }
    }
}
