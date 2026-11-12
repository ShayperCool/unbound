using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    // VectorImage drawing as Unity's renderer does it (matches the Unity 6000.0.71f1 Editor,
    // tests/projects/uitkvector): the image's triangles placed into a rectangle by its scale mode or
    // nine-sliced, tinted, drawn without anti-aliasing (arc data of painter-made images kept); images
    // with a gradient atlas draw with UIR's SVG gradient render type, their gradient settings rows
    // placed in the panel's gradient settings texture.
    internal sealed partial class UaPainter
    {
        /// <summary>Rows of UIR's gradient settings texture (GradientSettingsAtlas's length).</summary>
        internal const int GradientSettingsRows = 4096;

        /// <summary>Below this the middle of a nine-sliced vector image counts as empty (its right and
        /// bottom parts then move by the extra space instead of the middle stretching).</summary>
        const float SliceMiddleEpsilon = 0.001f;

        /// <summary>The images whose gradient settings this frame's geometry uses, in row order, and the
        /// first row of each.</summary>
        internal readonly List<VectorImage> GradientImages = new List<VectorImage>();
        readonly Dictionary<VectorImage, int> m_GradientRows = new Dictionary<VectorImage, int>();
        internal int GradientRowCount;
        bool m_WarnedGradientRows;

        /// <summary>The gradient settings row the current primitive's setting indices start at.</summary>
        int m_SettingOffset;

        // Pooled working buffers of one image draw (clipping and slicing add vertices).
        readonly List<VectorImageVertex> m_VectorVertices = new List<VectorImageVertex>();
        readonly List<int> m_VectorIndices = new List<int>();
        readonly List<int> m_VectorSplit = new List<int>();
        readonly List<VectorImageVertex> m_Polygon = new List<VectorImageVertex>(), m_PolygonOut = new List<VectorImageVertex>();

        void ClearGradients()
        {
            GradientImages.Clear();
            m_GradientRows.Clear();
            GradientRowCount = 0;
            m_SettingOffset = 0;
        }

        /// <summary>The first gradient settings row of <paramref name="image"/> in this frame, adding
        /// its rows on first use (UIR's VectorImageManager.Register).</summary>
        int GradientOffset(VectorImage image)
        {
            if (m_GradientRows.TryGetValue(image, out int row)) return row;
            int count = image.settings?.Length ?? 0;
            if (GradientRowCount + count > GradientSettingsRows)
            {
                if (!m_WarnedGradientRows)
                {
                    m_WarnedGradientRows = true;
                    Debug.LogError($"Exhausted max gradient settings ({GradientSettingsRows}) for atlas: GradientSettings");
                }
                m_GradientRows[image] = 0;
                return 0;
            }
            row = GradientRowCount;
            m_GradientRows[image] = row;
            GradientImages.Add(image);
            GradientRowCount += count;
            return row;
        }

        /// <summary>Starts a primitive of <paramref name="image"/>'s triangles: its atlas texture and
        /// gradient settings when it has an atlas.</summary>
        float BeginVector(VectorImage image, out byte type)
        {
            if (image.atlas == null)
            {
                type = VertSolid;
                return Begin(null);
            }
            type = VertSvgGradient;
            if (m_Recording != null) return BeginRecorded(image.atlas, 0f, 0f, image);
            float id = Begin(image.atlas);
            m_SettingOffset = GradientOffset(image);
            return id;
        }

        /// <summary>A rectangle showing a VectorImage (UIR's DrawVectorImage): stretched or fitted
        /// by its scale mode and cut to its uv part, or nine-sliced when it has slices.</summary>
        void DrawVectorRectangle(RectParams p)
        {
            var image = p.vectorImage;
            if (image.vertices == null || image.indices == null || image.vertices.Length == 0 || image.indices.Length == 0) return;
            m_VectorVertices.Clear();
            m_VectorIndices.Clear();
            foreach (var v in image.vertices) m_VectorVertices.Add(v);
            foreach (var i in image.indices) m_VectorIndices.Add(i);
            if (p.HasSlices) NineSliceVector(image.size, p.rect, new Vector4(p.leftSlice, p.topSlice, p.rightSlice, p.bottomSlice));
            else if (!StretchVector(image.size, p.rect, p.uv, p.scaleMode)) return;
            EmitVector(image, p.color, Matrix4x4.identity, false, true);
        }

        /// <summary>MeshGenerationContext.DrawVectorImage: the image's triangles moved by
        /// <paramref name="offset"/>, rotated and scaled, untinted (a negative scale on one axis
        /// reverses the winding).</summary>
        void DrawVectorImageTransformed(VectorImage image, Vector2 offset, float degrees, Vector2 scale)
        {
            m_VectorVertices.Clear();
            m_VectorIndices.Clear();
            foreach (var v in image.vertices) m_VectorVertices.Add(v);
            foreach (var i in image.indices) m_VectorIndices.Add(i);
            var matrix = Matrix4x4.TRS(offset, Quaternion.AngleAxis(degrees, Vector3.forward), new Vector3(scale.x, scale.y, 1f));
            EmitVector(image, Color.white, matrix, (scale.x < 0f) ^ (scale.y < 0f), false);
        }

        void EmitVector(VectorImage image, Color tint, Matrix4x4 matrix, bool flip, bool tinted)
        {
            float id = BeginVector(image, out byte type);
            int first = VertexCount;
            int r = 0, g = 0, b = 0, a = 0;
            if (tinted)
            {
                r = TintFactor(tint.r);
                g = TintFactor(tint.g);
                b = TintFactor(tint.b);
                a = TintFactor(tint.a);
            }
            var unused = new Vector4(UnusedArc, UnusedArc, UnusedArc, UnusedArc);
            for (int i = 0; i < m_VectorVertices.Count; i++)
            {
                var v = m_VectorVertices[i];
                Color32 c = v.tint;
                if (tinted) c = new Color32((byte)(r * c.r >> 8), (byte)(g * c.g >> 8), (byte)(b * c.b >> 8), (byte)(a * c.a >> 8));
                // Gradients sample their atlas without arc anti-aliasing; solid triangles keep the
                // arc data painter-made images carry.
                bool arc = type == VertSolid && v.flags.b != 0;
                var position = matrix.MultiplyPoint3x4(v.position);
                Vertex(new Vector2(position.x, position.y), c, v.uv, type, id, arc ? v.circle : unused, arc, setting: (int)v.settingIndex);
            }
            for (int i = 0; i + 2 < m_VectorIndices.Count; i += 3)
            {
                if (flip) Triangle(first + m_VectorIndices[i], first + m_VectorIndices[i + 2], first + m_VectorIndices[i + 1]);
                else Triangle(first + m_VectorIndices[i], first + m_VectorIndices[i + 1], first + m_VectorIndices[i + 2]);
            }
            m_SettingOffset = 0;
        }

        /// <summary>A tint channel as the multiplier of an 8-bit vertex channel: (t*255 rounded) + 1,
        /// the product taken >> 8.</summary>
        static int TintFactor(float channel) => ((int)(Mathf.Clamp01(channel) * 255f + 0.5f) & 0xFF) + 1;

        // ------------------------------------------------------------------ stretch and scale modes

        /// <summary>Places the working triangles into <paramref name="rect"/>: the image's
        /// <paramref name="uv"/> part (0..1, top-down) stretched, fitted or cropped by
        /// <paramref name="mode"/>; triangles outside a cropped or partial view are cut to it.
        /// False when nothing is left.</summary>
        bool StretchVector(Vector2 size, Rect rect, Rect uv, ScaleMode mode)
        {
            var sub = new Rect(uv.x * size.x, uv.y * size.y, uv.width * size.x, uv.height * size.y);
            var view = sub;
            bool cut = uv != new Rect(0f, 0f, 1f, 1f);
            float rectAspect = rect.width / rect.height, subAspect = sub.width / sub.height;
            var scale = new Vector2(rect.width / sub.width, rect.height / sub.height);
            var offset = Vector2.zero;
            if (mode == ScaleMode.ScaleToFit)
            {
                if (rectAspect <= subAspect)
                {
                    scale = new Vector2(scale.x, scale.x);
                    offset.y = (rect.height - sub.height * scale.x) * 0.5f;
                }
                else
                {
                    scale = new Vector2(scale.y, scale.y);
                    offset.x = (rect.width - sub.width * scale.y) * 0.5f;
                }
            }
            else if (mode == ScaleMode.ScaleAndCrop)
            {
                if (subAspect < rectAspect)
                {
                    scale = new Vector2(scale.x, scale.x);
                    float crop = sub.height * 0.5f - 0.5f * (rect.height / scale.x);
                    offset.y = -scale.x * crop;
                    view.y = crop + sub.y;
                    view.height = rect.height / scale.x;
                    cut = true;
                }
                else if (subAspect > rectAspect)
                {
                    scale = new Vector2(scale.y, scale.y);
                    float crop = sub.width * 0.5f - 0.5f * (rect.width / scale.y);
                    offset.x = -scale.y * crop;
                    view.x = crop + sub.x;
                    view.width = rect.width / scale.y;
                    cut = true;
                }
                else
                {
                    scale = new Vector2(scale.x, scale.x);
                }
            }
            if (cut)
            {
                if (view.width <= 0f || view.height <= 0f) return false;
                ClipToRect(view);
            }
            offset += rect.position - sub.position * scale;
            for (int i = 0; i < m_VectorVertices.Count; i++)
            {
                var v = m_VectorVertices[i];
                v.position = new Vector3(v.position.x * scale.x + offset.x, v.position.y * scale.y + offset.y, v.position.z);
                m_VectorVertices[i] = v;
            }
            return true;
        }

        /// <summary>Keeps the working triangles inside <paramref name="view"/>: those inside stay,
        /// those outside (or touching it from outside) go, the others are replaced by the triangles
        /// of their intersection with it (new vertices interpolated across the triangle).</summary>
        void ClipToRect(Rect view)
        {
            m_VectorSplit.Clear();
            for (int t = 0; t + 2 < m_VectorIndices.Count; t += 3)
            {
                int i0 = m_VectorIndices[t], i1 = m_VectorIndices[t + 1], i2 = m_VectorIndices[t + 2];
                Vector2 a = m_VectorVertices[i0].position, b = m_VectorVertices[i1].position, c = m_VectorVertices[i2].position;
                float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                if (minX >= view.xMin && maxX <= view.xMax && minY >= view.yMin && maxY <= view.yMax)
                {
                    m_VectorSplit.Add(i0);
                    m_VectorSplit.Add(i1);
                    m_VectorSplit.Add(i2);
                    continue;
                }
                if (maxX <= view.xMin || minX >= view.xMax || maxY <= view.yMin || minY >= view.yMax) continue;
                m_Polygon.Clear();
                m_Polygon.Add(m_VectorVertices[i0]);
                m_Polygon.Add(m_VectorVertices[i1]);
                m_Polygon.Add(m_VectorVertices[i2]);
                ClipPolygon(0, view.xMin, false);
                ClipPolygon(0, view.xMax, true);
                ClipPolygon(1, view.yMin, false);
                ClipPolygon(1, view.yMax, true);
                if (m_Polygon.Count < 3) continue;
                int start = m_VectorVertices.Count;
                foreach (var v in m_Polygon) m_VectorVertices.Add(v);
                for (int k = 1; k + 1 < m_Polygon.Count; k++)
                {
                    m_VectorSplit.Add(start);
                    m_VectorSplit.Add(start + k);
                    m_VectorSplit.Add(start + k + 1);
                }
            }
            m_VectorIndices.Clear();
            m_VectorIndices.AddRange(m_VectorSplit);
        }

        /// <summary>One edge of the polygon clip: keeps the part of <see cref="m_Polygon"/> on the
        /// inner side of the line <c>axis = value</c> (below it when <paramref name="max"/>).</summary>
        void ClipPolygon(int axis, float value, bool max)
        {
            m_PolygonOut.Clear();
            int n = m_Polygon.Count;
            for (int i = 0; i < n; i++)
            {
                var p = m_Polygon[i];
                var q = m_Polygon[(i + 1) % n];
                float dp = max ? value - p.position[axis] : p.position[axis] - value;
                float dq = max ? value - q.position[axis] : q.position[axis] - value;
                if (dp >= 0f) m_PolygonOut.Add(p);
                if ((dp >= 0f) != (dq >= 0f)) m_PolygonOut.Add(Lerp(p, q, dp / (dp - dq)));
            }
            m_Polygon.Clear();
            m_Polygon.AddRange(m_PolygonOut);
        }

        /// <summary>A vertex between two vertices of a triangle: position, tint, uv and arc circle
        /// interpolated, the setting index and flags of the first.</summary>
        static VectorImageVertex Lerp(in VectorImageVertex a, in VectorImageVertex b, float t) => new VectorImageVertex
        {
            position = Vector3.LerpUnclamped(a.position, b.position, t),
            tint = Color32.LerpUnclamped(a.tint, b.tint, t),
            uv = Vector2.LerpUnclamped(a.uv, b.uv, t),
            settingIndex = a.settingIndex,
            flags = a.flags,
            circle = Vector4.LerpUnclamped(a.circle, b.circle, t),
        };

        // ------------------------------------------------------------------ nine slices

        /// <summary>Nine-slices the working triangles into <paramref name="target"/>: the image cut at
        /// its slices (in image units, clamped to it), corners and edges at their size (all scaled
        /// down together when the target is smaller), the middle stretched over the rest. The slice
        /// scale does not apply to vector images.</summary>
        void NineSliceVector(Vector2 size, Rect target, Vector4 slices)
        {
            float l = Mathf.Min(Mathf.Max(slices.x, 0f), size.x);
            float t = Mathf.Min(Mathf.Max(slices.y, 0f), size.y);
            float r = Mathf.Min(Mathf.Max(slices.z, 0f), size.x - l);
            float b = Mathf.Min(Mathf.Max(slices.w, 0f), size.y - t);
            float midX = size.x - (r + l), midY = size.y - (b + t);
            SplitAt(0, l);
            SplitAt(0, l + midX);
            SplitAt(1, t);
            SplitAt(1, t + midY);
            SliceAxis(size.x, midX, target.width, out float edgeX, out float middleX, out float shiftX);
            SliceAxis(size.y, midY, target.height, out float edgeY, out float middleY, out float shiftY);
            for (int i = 0; i < m_VectorVertices.Count; i++)
            {
                var v = m_VectorVertices[i];
                float x = SliceMap(v.position.x, l, midX, edgeX, middleX, shiftX, target.x);
                float y = SliceMap(v.position.y, t, midY, edgeY, middleY, shiftY, target.y);
                v.position = new Vector3(x, y, v.position.z);
                m_VectorVertices[i] = v;
            }
        }

        /// <summary>One axis of a nine slice: the scale of the edges, of the middle, and the shift of the
        /// far edge when the middle is empty.</summary>
        static void SliceAxis(float size, float middle, float target, out float edge, out float stretch, out float shift)
        {
            float fixedPart = size - middle;
            if (fixedPart <= target)
            {
                edge = 1f;
                shift = target - fixedPart;
                stretch = 1f;
                if (middle >= SliceMiddleEpsilon)
                {
                    stretch = shift / middle;
                    shift = 0f;
                }
            }
            else
            {
                edge = target / fixedPart;
                stretch = 0f;
                shift = 0f;
            }
        }

        static float SliceMap(float p, float near, float middle, float edge, float stretch, float shift, float origin)
        {
            if (p < near) return p * edge + origin;
            if (p >= near + middle) return (p - (near + middle)) * edge + origin + middle * stretch + near * edge + shift;
            return (p - near) * stretch + origin + near * edge + shift;
        }

        /// <summary>Splits the working triangles that cross the line <c>axis = value</c> into
        /// triangles on either side.</summary>
        void SplitAt(int axis, float value)
        {
            m_VectorSplit.Clear();
            for (int t = 0; t + 2 < m_VectorIndices.Count; t += 3)
            {
                int i0 = m_VectorIndices[t], i1 = m_VectorIndices[t + 1], i2 = m_VectorIndices[t + 2];
                float d0 = m_VectorVertices[i0].position[axis] - value;
                float d1 = m_VectorVertices[i1].position[axis] - value;
                float d2 = m_VectorVertices[i2].position[axis] - value;
                bool below = d0 < 0f || d1 < 0f || d2 < 0f, above = d0 > 0f || d1 > 0f || d2 > 0f;
                if (!below || !above)
                {
                    m_VectorSplit.Add(i0);
                    m_VectorSplit.Add(i1);
                    m_VectorSplit.Add(i2);
                    continue;
                }
                // Each side as a polygon, fanned.
                for (int side = 0; side < 2; side++)
                {
                    m_Polygon.Clear();
                    m_Polygon.Add(m_VectorVertices[i0]);
                    m_Polygon.Add(m_VectorVertices[i1]);
                    m_Polygon.Add(m_VectorVertices[i2]);
                    ClipPolygon(axis, value, side == 0);
                    if (m_Polygon.Count < 3) continue;
                    int start = m_VectorVertices.Count;
                    foreach (var v in m_Polygon) m_VectorVertices.Add(v);
                    for (int k = 1; k + 1 < m_Polygon.Count; k++)
                    {
                        m_VectorSplit.Add(start);
                        m_VectorSplit.Add(start + k);
                        m_VectorSplit.Add(start + k + 1);
                    }
                }
            }
            m_VectorIndices.Clear();
            m_VectorIndices.AddRange(m_VectorSplit);
        }
    }
}
