using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>
    /// Builds a panel's geometry as Unity's UI renderer (UIR) does: one vertex stream in panel
    /// space with UIR's per-vertex data (render type, texture id, arc anti-aliasing circles, shader
    /// info ids), split into batches by scissor rectangle and by the eight texture slots of
    /// Hidden/Internal-UIRDefault. Elements draw background, border, then content, then children;
    /// opacity and text settings live in the shader info texture, as UIR keeps them.
    /// </summary>
    internal sealed partial class UaPainter
    {
        internal const int MaxVerticesPerMesh = 0xFFFF;
        internal const int TextureSlots = 8;
        const float UnusedArc = -10000f;

        // UIR's vertex types (UnityUIE.cginc k_VertType*).
        internal const byte VertSolid = 0, VertText = 1, VertTexture = 2;

        internal sealed class Batch
        {
            internal int IndexStart, IndexCount;
            internal RectInt? Scissor;
            internal readonly Texture[] Textures = new Texture[TextureSlots];
            internal readonly float[] Ids = new float[TextureSlots];
            internal readonly float[] SdfScales = new float[TextureSlots];
            internal readonly float[] Sharpness = new float[TextureSlots];
            internal int TextureCount;

            internal int SlotOf(Texture t)
            {
                for (int i = 0; i < TextureCount; i++)
                    if (Textures[i] == t) return i;
                return -1;
            }
        }

        internal readonly List<Vector3> Positions = new List<Vector3>();
        internal readonly List<Color32> Colors = new List<Color32>();
        internal readonly List<Vector4>[] Uvs = new List<Vector4>[8];
        internal readonly List<int> Indices = new List<int>();
        internal readonly List<Batch> Batches = new List<Batch>();

        // Shader info: opacity entries (id → opacity) and text settings (id → settings).
        internal readonly List<float> Opacities = new List<float>();
        internal readonly List<UaTextCoreSettings> TextSettings = new List<UaTextCoreSettings>();

        readonly Dictionary<Texture, float> m_TextureIds = new Dictionary<Texture, float>();
        readonly List<NativeArray<Vertex>> m_VertexArrays = new List<NativeArray<Vertex>>();
        readonly List<NativeArray<ushort>> m_IndexArrays = new List<NativeArray<ushort>>();
        readonly List<(MeshWriteData Data, Texture Texture)> m_PendingUserMeshes = new List<(MeshWriteData, Texture)>();
        readonly MeshGenerationContext m_Context = new MeshGenerationContext();

        // The element being drawn.
        Matrix4x4 m_Matrix = Matrix4x4.identity;
        bool m_Identity = true;
        int m_OpacityId;
        RectInt? m_Scissor;
        float m_PixelsPerPoint = 1f;
        float m_ViewportHeight;
        static readonly HashSet<string> s_Warned = new HashSet<string>();

        internal UaPainter()
        {
            for (int i = 0; i < Uvs.Length; i++) Uvs[i] = new List<Vector4>();
            m_Context.uaPainter = this;
        }

        internal static void WarnOnce(string message)
        {
            if (s_Warned.Add(message)) Debug.LogWarning("UI Toolkit: " + message);
        }

        // ------------------------------------------------------------------ the tree

        /// <summary>Paints a panel's tree; <paramref name="viewportHeight"/> is the target's height in
        /// pixels (scissor rectangles count from its bottom).</summary>
        internal void Paint(BaseVisualElementPanel panel, float viewportHeight)
        {
            Clear();
            m_PixelsPerPoint = panel.scaledPixelsPerPoint;
            m_ViewportHeight = viewportHeight;
            Opacities.Add(1f);
            TextSettings.Add(default);
            PaintElement(panel.visualTree, 1f, null);
            CloseBatch();
        }

        void Clear()
        {
            Positions.Clear();
            Colors.Clear();
            foreach (var uv in Uvs) uv.Clear();
            Indices.Clear();
            Batches.Clear();
            Opacities.Clear();
            TextSettings.Clear();
            m_TextureIds.Clear();
            foreach (var a in m_VertexArrays) a.Dispose();
            foreach (var a in m_IndexArrays) a.Dispose();
            m_VertexArrays.Clear();
            m_IndexArrays.Clear();
            m_Scissor = null;
        }

        void PaintElement(VisualElement ve, float parentOpacity, Rect? parentClip)
        {
            var style = ve.computedValues;
            if (style.display == DisplayStyle.None || ve.disableRendering) return;
            float opacity = parentOpacity * Mathf.Clamp01(style.opacity);
            if (opacity <= 0f) return;

            var savedScissor = m_Scissor;
            m_Matrix = ve.worldTransform;
            m_Identity = m_Matrix.isIdentity;
            m_OpacityId = OpacityId(opacity);

            bool visible = style.visibility == Visibility.Visible;
            if (visible)
            {
                DrawBackground(ve, style);
                DrawBorder(ve, style);
            }

            // Overflow clipping (UIR's worldClip: the padding box, or the content box, inside the
            // parent's clip), applied by scissor.
            Rect? clip = parentClip;
            if (style.overflow != OverflowInternal.Visible)
            {
                var r = SubtractBorderPadding(ve, style, ve.worldBound);
                clip = parentClip.HasValue ? Intersect(r, parentClip.Value) : r;
                m_Scissor = ToScissor(clip.Value);
                if (HasRadius(ve, style)) WarnOnce($"rounded overflow clipping (element '{ve.name}') is drawn with a rectangular clip; stencil masks are not supported yet");
            }

            if (visible) DrawContent(ve);

            var children = ve.hierarchy.children;
            for (int i = 0; i < children.Count; i++)
            {
                PaintElement(children[i], opacity, clip);
            }
            m_Scissor = savedScissor;
        }

        void DrawContent(VisualElement ve)
        {
            if (ve.generateVisualContent == null) return;
            m_Context.uaElement = ve;
            // Callbacks may draw children through other elements' matrices; keep this element's.
            var matrix = m_Matrix;
            try
            {
                ve.generateVisualContent(m_Context);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            m_Matrix = matrix;
            m_Identity = matrix.isIdentity;
            FlushUserMeshes();
        }

        static Rect SubtractBorderPadding(VisualElement ve, ComputedValues s, Rect world)
        {
            var m = ve.worldTransform;
            float sx = m.m00, sy = m.m11;
            world.x += s.borderLeftWidth * sx;
            world.y += s.borderTopWidth * sy;
            world.width -= (s.borderLeftWidth + s.borderRightWidth) * sx;
            world.height -= (s.borderTopWidth + s.borderBottomWidth) * sy;
            if (s.unityOverflowClipBox == OverflowClipBox.ContentBox)
            {
                var r = ve.resolvedStyle;
                world.x += r.paddingLeft * sx;
                world.y += r.paddingTop * sy;
                world.width -= (r.paddingLeft + r.paddingRight) * sx;
                world.height -= (r.paddingTop + r.paddingBottom) * sy;
            }
            return world;
        }

        static Rect Intersect(Rect a, Rect b)
        {
            float x = Mathf.Max(a.xMin, b.xMin), x2 = Mathf.Min(a.xMax, b.xMax);
            float y = Mathf.Max(a.yMin, b.yMin), y2 = Mathf.Min(a.yMax, b.yMax);
            return new Rect(x, y, Mathf.Max(x2 - x, 0f), Mathf.Max(y2 - y, 0f));
        }

        /// <summary>A panel rectangle as target pixels from the bottom (UIR's
        /// RectPointsToPixelsAndFlipYAxis).</summary>
        RectInt ToScissor(Rect r) => new RectInt(
            Mathf.RoundToInt(r.x * m_PixelsPerPoint),
            Mathf.RoundToInt(m_ViewportHeight - r.yMax * m_PixelsPerPoint),
            Mathf.RoundToInt(r.width * m_PixelsPerPoint),
            Mathf.RoundToInt(r.height * m_PixelsPerPoint));

        int OpacityId(float opacity)
        {
            if (opacity >= 1f) return 0;
            Opacities.Add(opacity);
            return Opacities.Count - 1;
        }

        // ------------------------------------------------------------------ batches and vertices

        Batch Current => Batches.Count > 0 ? Batches[Batches.Count - 1] : null;

        void CloseBatch()
        {
            var b = Current;
            if (b != null) b.IndexCount = Indices.Count - b.IndexStart;
        }

        /// <summary>Starts a primitive: the batch it goes into (a new one when the scissor changed
        /// or the texture slots are full) and its texture's id there (0 without one).</summary>
        float Begin(Texture texture, float sdfScale = 0f, float sharpness = 0f)
        {
            var b = Current;
            bool scissorChanged = b != null && !SameScissor(b.Scissor, m_Scissor);
            bool full = b != null && texture != null && b.SlotOf(texture) < 0 && b.TextureCount == TextureSlots;
            if (b == null || scissorChanged || full)
            {
                CloseBatch();
                b = new Batch { IndexStart = Indices.Count, Scissor = m_Scissor };
                Batches.Add(b);
            }
            if (texture == null) return 0f;
            int slot = b.SlotOf(texture);
            if (slot < 0)
            {
                slot = b.TextureCount++;
                b.Textures[slot] = texture;
                if (!m_TextureIds.TryGetValue(texture, out var id)) m_TextureIds[texture] = id = m_TextureIds.Count + 1;
                b.Ids[slot] = id;
                b.SdfScales[slot] = sdfScale;
                b.Sharpness[slot] = sharpness;
            }
            return b.Ids[slot];
        }

        static bool SameScissor(RectInt? a, RectInt? b) =>
            a.HasValue == b.HasValue && (!a.HasValue || a.Value.Equals(b.Value));

        const float Byte = 1f / 255f;

        /// <summary>Adds a vertex given in the element's local space; returns its index.</summary>
        int Vertex(Vector2 local, Color32 color, Vector2 uv, byte type, float textureId, Vector4 circle, bool arc, byte dilate = 0, int textSettings = 0)
        {
            var p = m_Identity ? new Vector3(local.x, local.y, 0f) : m_Matrix.MultiplyPoint3x4(new Vector3(local.x, local.y, 0f));
            p.z = UnityEngine.UIElements.Vertex.nearZ;
            Positions.Add(p);
            Colors.Add(color);
            int opacityPage = UaShaderInfo.OpacityPage(m_OpacityId), opacityId = UaShaderInfo.InPage(m_OpacityId);
            int textPage = UaShaderInfo.TextPage(textSettings), textId = UaShaderInfo.InPage(textSettings);
            Uvs[0].Add(new Vector4(uv.x, uv.y, 0f, 0f));
            // xformClipPages: the transform page (0, 0) and the clip rect page (1, 0).
            Uvs[1].Add(new Vector4(UaShaderInfo.TransformPage * Byte, 0f, UaShaderInfo.ClipPage * Byte, 0f));
            // ids: transform, clip rect, opacity, text settings.
            Uvs[2].Add(new Vector4(0f, 0f, opacityId * Byte, textId * Byte));
            // flags: render type, text dilation, arc, dynamic color.
            Uvs[3].Add(new Vector4(type * Byte, dilate * Byte, arc ? Byte : 0f, 0f));
            // opacityColorPages: the opacity page, the text settings page.
            Uvs[4].Add(new Vector4(opacityPage * Byte, 0f, textPage * Byte, 0f));
            Uvs[5].Add(Vector4.zero);
            Uvs[6].Add(circle);
            Uvs[7].Add(new Vector4(textureId, 0f, 0f, 0f));
            return Positions.Count - 1;
        }

        void Triangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
        }

        // ------------------------------------------------------------------ user meshes

        internal void AllocateUserMesh(int vertexCount, int indexCount, Texture texture, MeshWriteData data)
        {
            var vertices = new NativeArray<Vertex>(vertexCount, Allocator.Persistent);
            var indices = new NativeArray<ushort>(indexCount, Allocator.Persistent);
            m_VertexArrays.Add(vertices);
            m_IndexArrays.Add(indices);
            data.Reset(vertices, indices);
            m_PendingUserMeshes.Add((data, texture));
        }

        void FlushUserMeshes()
        {
            foreach (var (data, texture) in m_PendingUserMeshes) AddUserMesh(data.m_Vertices, data.m_Indices, texture);
            m_PendingUserMeshes.Clear();
        }

        internal void AddUserMesh(NativeSlice<Vertex> vertices, NativeSlice<ushort> indices, Texture texture)
        {
            float id = Begin(texture);
            byte type = texture != null ? VertTexture : VertSolid;
            int first = Positions.Count;
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                var uv = v.uv;
                Vertex(new Vector2(v.position.x, v.position.y), v.tint, uv, type, id, new Vector4(UnusedArc, UnusedArc, UnusedArc, UnusedArc), false);
            }
            for (int i = 0; i < indices.Length; i++) Indices.Add(first + indices[i]);
        }

        // ------------------------------------------------------------------ text

        internal void DrawText(NativeSlice<Vertex> vertices, Texture2D atlas, float sdfScale, float sharpness, UaTextCoreSettings settings)
        {
            if (vertices.Length < 4 || atlas == null) return;
            int textSettings = 0;
            if (!settings.IsDefault)
            {
                TextSettings.Add(settings);
                textSettings = TextSettings.Count - 1;
            }
            float id = Begin(atlas, sdfScale, sharpness);
            var unused = new Vector4(UnusedArc, UnusedArc, UnusedArc, UnusedArc);
            for (int q = 0; q + 3 < vertices.Length; q += 4)
            {
                int first = Positions.Count;
                for (int k = 0; k < 4; k++)
                {
                    var v = vertices[q + k];
                    Vertex(new Vector2(v.position.x, v.position.y), v.tint, v.uv, VertText, id, unused, false, v.flags.g, textSettings);
                }
                Triangle(first, first + 1, first + 2);
                Triangle(first + 2, first + 3, first);
            }
        }
    }
}
