using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>
    /// An element's geometry as UIR keeps it between repaints: its background and border, and its
    /// content (Image, generateVisualContent), in the element's local space. Generated when the
    /// element repaints (a repaint or color change, a new size, joining a panel), drawn again with
    /// the element's current transform, opacity and clipping otherwise (UIR's nudging). What it was
    /// generated from stays: a panel scale change that leaves an element's size and style alone
    /// keeps the pixel-grid decisions of the old scale, as in Unity.
    /// </summary>
    internal sealed class UaRetainedGeometry
    {
        internal readonly UaRetainedMesh Visuals = new UaRetainedMesh();
        internal readonly UaRetainedMesh Content = new UaRetainedMesh();
        internal bool Valid;

        internal void Invalidate() => Valid = false;

        /// <summary>Whether the textures it samples are the ones it was generated for (a destroyed
        /// texture or a text atlas that changed size needs the geometry generated again).</summary>
        internal bool TexturesValid() => Visuals.TexturesValid() && Content.TexturesValid();
    }

    internal sealed class UaRetainedMesh
    {
        internal struct V
        {
            public Vector2 position;
            public Color32 color;
            public Vector2 uv;
            public Vector4 circle;
            public byte type, dilate;
            public bool arc;
        }

        internal struct Segment
        {
            public Texture texture;
            public bool hasTexture;
            public int textureWidth, textureHeight;
            public float sdfScale, sharpness;
            public bool hasSettings;
            public UaTextCoreSettings settings;
            public int vertexStart, vertexEnd, indexStart, indexEnd;
        }

        internal readonly List<V> Vertices = new List<V>();
        internal readonly List<int> Indices = new List<int>();
        internal readonly List<Segment> Segments = new List<Segment>();

        internal void Clear()
        {
            Vertices.Clear();
            Indices.Clear();
            Segments.Clear();
        }

        internal bool TexturesValid()
        {
            foreach (var s in Segments)
            {
                if (!s.hasTexture) continue;
                if (s.texture == null) return false;
                if (s.texture.width != s.textureWidth || s.texture.height != s.textureHeight) return false;
            }
            return true;
        }
    }

    internal sealed partial class UaPainter
    {
        /// <summary>The mesh being generated (vertices then stay in local space), or null when
        /// drawing straight into the panel's buffers.</summary>
        UaRetainedMesh m_Recording;
        UaTextCoreSettings m_PendingSettings;
        bool m_HasPendingSettings;

        int VertexCount => m_Recording != null ? m_Recording.Vertices.Count : Positions.Count;

        void Index(int i)
        {
            if (m_Recording != null) m_Recording.Indices.Add(i);
            else Indices.Add(i);
        }

        /// <summary>A recorded primitive's start: a segment per texture (and text settings).</summary>
        float BeginRecorded(Texture texture, float sdfScale, float sharpness)
        {
            var r = m_Recording;
            bool settings = m_HasPendingSettings;
            m_HasPendingSettings = false;
            int n = r.Segments.Count;
            if (n > 0 && !settings)
            {
                var last = r.Segments[n - 1];
                if (!last.hasSettings && last.texture == texture && last.sdfScale == sdfScale && last.sharpness == sharpness) return 0f;
            }
            CloseSegment();
            r.Segments.Add(new UaRetainedMesh.Segment
            {
                texture = texture,
                hasTexture = texture != null,
                textureWidth = texture != null ? texture.width : 0,
                textureHeight = texture != null ? texture.height : 0,
                sdfScale = sdfScale,
                sharpness = sharpness,
                hasSettings = settings,
                settings = settings ? m_PendingSettings : default,
                vertexStart = r.Vertices.Count,
                vertexEnd = r.Vertices.Count,
                indexStart = r.Indices.Count,
                indexEnd = r.Indices.Count,
            });
            return 0f;
        }

        void CloseSegment()
        {
            var r = m_Recording;
            int n = r.Segments.Count;
            if (n == 0) return;
            var s = r.Segments[n - 1];
            s.vertexEnd = r.Vertices.Count;
            s.indexEnd = r.Indices.Count;
            r.Segments[n - 1] = s;
        }

        int RecordVertex(Vector2 local, Color32 color, Vector2 uv, byte type, Vector4 circle, bool arc, byte dilate)
        {
            var r = m_Recording;
            if (r.Segments.Count == 0) BeginRecorded(null, 0f, 0f);
            r.Vertices.Add(new UaRetainedMesh.V { position = local, color = color, uv = uv, circle = circle, type = type, dilate = dilate, arc = arc });
            return r.Vertices.Count - 1;
        }

        /// <summary>Generates an element's geometry (in its local space) if it has none or must
        /// repaint.</summary>
        UaRetainedGeometry Geometry(VisualElement ve, ComputedValues style)
        {
            var g = ve.uaGeometry;
            if (g != null && g.Valid && g.TexturesValid()) return g;
            g ??= new UaRetainedGeometry();
            g.Visuals.Clear();
            g.Content.Clear();
            if (style.visibility == Visibility.Visible)
            {
                m_Recording = g.Visuals;
                try
                {
                    DrawBackground(ve, style);
                    DrawBorder(ve, style);
                }
                finally
                {
                    CloseSegment();
                    m_Recording = null;
                }
                m_Recording = g.Content;
                try
                {
                    DrawContent(ve);
                }
                finally
                {
                    CloseSegment();
                    m_Recording = null;
                }
            }
            g.Valid = true;
            ve.uaGeometry = g;
            return g;
        }

        /// <summary>Draws a recorded mesh with the current transform, opacity and clipping.</summary>
        void Replay(UaRetainedMesh mesh)
        {
            if (mesh.Indices.Count == 0) return;
            int first = Positions.Count;
            foreach (var s in mesh.Segments)
            {
                float id = Begin(s.texture, s.sdfScale, s.sharpness);
                int textSettings = 0;
                if (s.hasSettings)
                {
                    TextSettings.Add(s.settings);
                    textSettings = TextSettings.Count - 1;
                }
                for (int i = s.vertexStart; i < s.vertexEnd; i++)
                {
                    var v = mesh.Vertices[i];
                    Vertex(v.position, v.color, v.uv, v.type, id, v.circle, v.arc, v.dilate, textSettings);
                }
                for (int i = s.indexStart; i < s.indexEnd; i++) Indices.Add(first + mesh.Indices[i]);
            }
        }
    }
}
