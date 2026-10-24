using System;
using Unity.Collections;
using Unbound.UIElements;

namespace UnityEngine.UIElements
{
    /// <summary>A vertex of UI Toolkit geometry: what elements and generateVisualContent callbacks
    /// emit, in the element's local space. The internal fields are the renderer's (UIR) per-vertex
    /// data, which the panel renderer fills.</summary>
    public partial struct Vertex
    {
        public static readonly float nearZ = 0f;

        public Vector3 position;
        public Color32 tint;
        public Vector2 uv;
        internal Color32 xformClipPages;
        internal Color32 ids;
        internal Color32 flags;
        internal Color32 opacityColorPages;
        internal Color32 settingIndex;
        internal Vector4 circle;
        internal float textureId;
    }

    /// <summary>Vertices and indices allocated for an element's content, filled by its
    /// generateVisualContent callback.</summary>
    public partial class MeshWriteData
    {
        internal NativeSlice<Vertex> m_Vertices;
        internal NativeSlice<ushort> m_Indices;
        internal int currentIndex;
        internal int currentVertex;

        internal MeshWriteData() { }

        public int vertexCount => m_Vertices.Length;

        public int indexCount => m_Indices.Length;

        [Obsolete("Texture coordinates are now automatically remapped by the renderer. You are no longer required to remap the UV coordinates in the provided rectangle.")]
        public Rect uvRegion => new Rect(0f, 0f, 1f, 1f);

        public void SetNextVertex(Vertex vertex) => m_Vertices[currentVertex++] = vertex;

        public void SetNextIndex(ushort index) => m_Indices[currentIndex++] = index;

        public void SetAllVertices(Vertex[] vertices)
        {
            if (currentVertex != 0) throw new InvalidOperationException("SetAllVertices may not be called after using SetNextVertex");
            m_Vertices.CopyFrom(vertices);
            currentVertex = m_Vertices.Length;
        }

        public void SetAllVertices(NativeSlice<Vertex> vertices)
        {
            if (currentVertex != 0) throw new InvalidOperationException("SetAllVertices may not be called after using SetNextVertex");
            m_Vertices.CopyFrom(vertices);
            currentVertex = m_Vertices.Length;
        }

        public void SetAllIndices(ushort[] indices)
        {
            if (currentIndex != 0) throw new InvalidOperationException("SetAllIndices may not be called after using SetNextIndex");
            m_Indices.CopyFrom(indices);
            currentIndex = m_Indices.Length;
        }

        public void SetAllIndices(NativeSlice<ushort> indices)
        {
            if (currentIndex != 0) throw new InvalidOperationException("SetAllIndices may not be called after using SetNextIndex");
            m_Indices.CopyFrom(indices);
            currentIndex = m_Indices.Length;
        }

        internal void Reset(NativeSlice<Vertex> vertices, NativeSlice<ushort> indices)
        {
            m_Vertices = vertices;
            m_Indices = indices;
            currentIndex = currentVertex = 0;
        }
    }

    /// <summary>What an element draws its content with: meshes it allocates (in its local space)
    /// and the renderer's own primitives, drawn after its background and border.</summary>
    public partial class MeshGenerationContext
    {
        internal VisualElement uaElement;
        internal UaPainter uaPainter;

        internal MeshGenerationContext() { }

        public VisualElement visualElement => uaElement;

        /// <summary>Allocates a mesh of the element's content, drawn with <paramref name="texture"/>
        /// (none: vertex colors only); the caller fills it before the callback returns.</summary>
        public MeshWriteData Allocate(int vertexCount, int indexCount, Texture texture = null)
        {
            var data = new MeshWriteData();
            if (vertexCount == 0 || indexCount == 0)
            {
                data.Reset(default, default);
                return data;
            }
            if (vertexCount > UaPainter.MaxVerticesPerMesh)
                throw new ArgumentOutOfRangeException(nameof(vertexCount), $"Attempting to allocate {vertexCount} vertices which exceeds the limit of {UaPainter.MaxVerticesPerMesh}.");
            uaPainter.AllocateUserMesh(vertexCount, indexCount, texture, data);
            return data;
        }

        public void DrawMesh(NativeSlice<Vertex> vertices, NativeSlice<ushort> indices, Texture texture = null)
        {
            if (vertices.Length == 0 || indices.Length == 0) return;
            uaPainter.AddUserMesh(vertices, indices, texture);
        }

        /// <summary>A textured rectangle as Unity's renderer draws one (MeshGenerator's MakeTextured
        /// then DrawRectangle): <paramref name="texture"/> fitted into <paramref name="rect"/> by
        /// <paramref name="scaleMode"/>, showing its <paramref name="uv"/> part, tinted.</summary>
        internal void UaDrawTexturedRect(Rect rect, Rect uv, Texture texture, ScaleMode scaleMode, Color tint) =>
            uaPainter.DrawTexturedRect(rect, uv, texture, scaleMode, tint);

        /// <summary>A sprite fitted into <paramref name="rect"/> (MeshGenerator's MakeSprite), its
        /// <paramref name="subRect"/> part (0..1), tinted.</summary>
        internal void UaDrawSprite(Rect rect, Rect subRect, Sprite sprite, ScaleMode scaleMode, Color tint) =>
            uaPainter.DrawSprite(rect, subRect, sprite, scaleMode, tint);

        /// <summary>A solid rectangle (MeshGenerator.RectangleParams.MakeSolid then DrawRectangle).</summary>
        internal void UaDrawSolidRect(Rect rect, Color color) => uaPainter.DrawSolidRect(rect, color);

        /// <summary>Glyph quads of TextCore text: four vertices per glyph (bottom-left, top-left,
        /// top-right, bottom-right) in the element's local space, uv in the atlas, tint the glyph's
        /// color, <c>flags.g</c> the bold dilation (255: extra dilate, as Unity's
        /// ConvertTextVertexToUIRVertex sets it). <paramref name="sdfScale"/> is the atlas's gradient
        /// scale (padding + 1) for SDF atlases (Alpha8), 0 for bitmap and color atlases;
        /// <paramref name="settings"/> the outline and underlay (default: none).</summary>
        internal void UaDrawText(NativeSlice<Vertex> vertices, Texture2D atlas, float sdfScale, float sharpness = 0f, UaTextCoreSettings settings = default) =>
            uaPainter.DrawText(vertices, atlas, sdfScale, sharpness, settings);
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;

    /// <summary>The outline and underlay of SDF text (UIR's TextCoreSettings), in the text shader's
    /// units: outline width and underlay offset/softness as TextCore's style sheets give them.</summary>
    internal struct UaTextCoreSettings
    {
        public Color faceColor;
        public Color outlineColor;
        public float outlineWidth;
        public Color underlayColor;
        public Vector2 underlayOffset;
        public float underlaySoftness;

        internal bool IsDefault => outlineWidth == 0f && underlayColor.a == 0f && underlayOffset == Vector2.zero && underlaySoftness == 0f;
    }
}
