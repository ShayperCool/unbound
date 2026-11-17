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
        internal UaRetainedMesh Mask;
        internal Rect ContentRect;
        internal bool Valid;

        internal bool HasTextures => Visuals.HasTextures || Content.HasTextures || (Mask?.HasTextures ?? false);
        internal void ReleaseAtlas()
        {
            Visuals.ReleaseAtlas(); Content.ReleaseAtlas(); Mask?.ReleaseAtlas(); Valid = false;
        }
        internal void Invalidate() => Valid = false;

        /// <summary>Whether the textures it samples are the ones it was generated for (a destroyed
        /// texture or a text atlas that changed size needs the geometry generated again).</summary>
        internal bool TexturesValid() => Visuals.TexturesValid() && Content.TexturesValid() && (Mask == null || Mask.TexturesValid());
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
            /// <summary>An SVG gradient vertex's setting index in its image.</summary>
            public int setting;
        }

        internal struct Segment
        {
            public Texture texture;
            public bool hasTexture;
            public int textureWidth, textureHeight;
            public float sdfScale, sharpness;
            public bool hasSettings;
            public UaTextCoreSettings settings;
            /// <summary>The VectorImage whose gradient settings the segment's vertices index.</summary>
            public VectorImage gradients;
            internal bool AtlasEligible;
            internal UaDynamicAtlas Atlas;
            internal UaDynamicAtlas.Entry AtlasEntry;
            internal int AtlasGeneration;
            public int vertexStart, vertexEnd, indexStart, indexEnd;
            /// <summary>An ImmediateModeElement's callback, drawn in this segment's place (the
            /// segment has no vertices).</summary>
            public System.Action immediate;
            public VisualElement immediateOwner;
            public bool immediateCull;
        }

        internal readonly List<V> Vertices = new List<V>();
        internal readonly List<int> Indices = new List<int>();
        internal readonly List<Segment> Segments = new List<Segment>();

        /// <summary>Whether it has an immediate-mode callback.</summary>
        internal bool HasImmediate;

        internal bool HasTextures
        {
            get { foreach (var segment in Segments) if (segment.hasTexture) return true; return false; }
        }
        internal void ReleaseAtlas()
        {
            for (int i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                segment.Atlas?.Return(segment.AtlasEntry);
                segment.AtlasEntry = null; segment.Atlas = null;
                Segments[i] = segment;
            }
        }
        internal void Clear()
        {
            ReleaseAtlas();
            Vertices.Clear();
            Indices.Clear();
            Segments.Clear();
            HasImmediate = false;
        }

        internal bool TexturesValid()
        {
            foreach (var s in Segments)
            {
                if (!s.hasTexture) continue;
                if (s.texture == null) return false;
                if (s.Atlas != null && s.AtlasGeneration != s.Atlas.Generation) return false;
                // An atlas entry keeps its rectangle until its users release it, even if the
                // source is resized. Direct text atlases still invalidate when their size changes.
                if (s.AtlasEntry == null && (s.texture.width != s.textureWidth || s.texture.height != s.textureHeight)) return false;
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
        float BeginRecorded(Texture texture, float sdfScale, float sharpness, VectorImage gradients = null, bool atlasEligible = true)
        {
            var r = m_Recording;
            bool settings = m_HasPendingSettings;
            m_HasPendingSettings = false;
            int n = r.Segments.Count;
            if (n > 0 && !settings)
            {
                var last = r.Segments[n - 1];
                if (last.immediate == null && !last.hasSettings && last.texture == texture && last.sdfScale == sdfScale && last.sharpness == sharpness && last.gradients == gradients && last.AtlasEligible == atlasEligible) return 0f;
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
                gradients = gradients,
                AtlasEligible = atlasEligible,
                Atlas = Atlas,
                AtlasGeneration = Atlas?.Generation ?? 0,
                AtlasEntry = atlasEligible ? Atlas?.Acquire(texture as Texture2D, gradients != null) : null,
                vertexStart = r.Vertices.Count,
                vertexEnd = r.Vertices.Count,
                indexStart = r.Indices.Count,
                indexEnd = r.Indices.Count,
            });
            return 0f;
        }

        /// <summary>An ImmediateModeElement's callback, in the place of the content drawn so far:
        /// recorded with the element's content, or put straight into the batches.</summary>
        internal void DrawImmediate(VisualElement owner, System.Action callback, bool cull)
        {
            if (m_Recording == null)
            {
                Immediate(owner, callback, cull);
                return;
            }
            CloseSegment();
            m_Recording.HasImmediate = true;
            int vertices = m_Recording.Vertices.Count, indices = m_Recording.Indices.Count;
            m_Recording.Segments.Add(new UaRetainedMesh.Segment
            {
                immediate = callback,
                immediateOwner = owner,
                immediateCull = cull,
                vertexStart = vertices,
                vertexEnd = vertices,
                indexStart = indices,
                indexEnd = indices,
            });
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

        int RecordVertex(Vector2 local, Color32 color, Vector2 uv, byte type, Vector4 circle, bool arc, byte dilate, int setting)
        {
            var r = m_Recording;
            if (r.Segments.Count == 0) BeginRecorded(null, 0f, 0f);
            r.Vertices.Add(new UaRetainedMesh.V { position = local, color = color, uv = uv, circle = circle, type = type, dilate = dilate, arc = arc, setting = setting });
            return r.Vertices.Count - 1;
        }

        /// <summary>Generates an element's geometry (in its local space) if it has none or must
        /// repaint.</summary>
        UaRetainedGeometry Geometry(VisualElement ve, ComputedValues style)
        {
            var g = ve.uaGeometry;
            bool retainedMasks = Unbound.Runtime.Compat.RetainedStencilShapesMeasured;
            var contentRect = retainedMasks ? ve.contentRect : default;
            if (g != null && g.Valid && g.TexturesValid() && (!retainedMasks || g.ContentRect.Equals(contentRect))) return g;
            g ??= new UaRetainedGeometry();
            g.Visuals.Clear();
            g.Content.Clear();
            g.Mask?.Clear();
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
                float devicePixelsPerPoint = GUIUtility.uaContentPixelsPerPoint;
                GUIUtility.uaContentPixelsPerPoint = m_PixelsPerPoint;
                try
                {
                    DrawContent(ve);
                }
                finally
                {
                    GUIUtility.uaContentPixelsPerPoint = devicePixelsPerPoint;
                    CloseSegment();
                    m_Recording = null;
                }
            }
            if (Unbound.Runtime.Compat.RetainedStencilShapesMeasured && style.overflow != OverflowInternal.Visible
                && (HasRadius(ve, style) || style.backgroundImage.vectorImage != null))
            {
                g.Mask ??= new UaRetainedMesh();
                m_Recording = g.Mask;
                try { DrawStencilMask(ve, style); }
                finally
                {
                    CloseSegment();
                    m_Recording = null;
                }
            }
            // ContentBox masks and element content depend on resolved padding, including
            // percentages changed by an ancestor's size while this element's size stays fixed.
            g.ContentRect = contentRect;
            g.Valid = true;
            ve.uaGeometry = g;
            return g;
        }

        /// <summary>Only the immediate-mode callbacks of a recorded mesh.</summary>
        void ReplayImmediate(UaRetainedMesh mesh)
        {
            if (!mesh.HasImmediate) return;
            foreach (var s in mesh.Segments)
                if (s.immediate != null) Immediate(s.immediateOwner, s.immediate, s.immediateCull);
        }

        /// <summary>Draws a recorded mesh with the current transform, opacity and clipping.</summary>
        void Replay(UaRetainedMesh mesh)
        {
            if (mesh.Indices.Count == 0 && !mesh.HasImmediate) return;
            int first = Positions.Count;
            foreach (var s in mesh.Segments)
            {
                if (s.immediate != null)
                {
                    Immediate(s.immediateOwner, s.immediate, s.immediateCull);
                    continue;
                }
                var binding = s.AtlasEntry;
                float id = Begin(binding != null ? binding.Page.Token : s.texture, s.sdfScale, s.sharpness);
                int textSettings = 0;
                if (s.hasSettings)
                {
                    TextSettings.Add(s.settings);
                    textSettings = TextSettings.Count - 1;
                }
                m_SettingOffset = s.gradients != null ? GradientOffset(s.gradients) : 0;
                for (int i = s.vertexStart; i < s.vertexEnd; i++)
                {
                    var v = mesh.Vertices[i];
                    var uv = v.uv;
                    byte type = v.type;
                    if (binding != null && type == VertTexture)
                    {
                        uv = new Vector2(binding.Rect.x + uv.x * binding.Rect.width, binding.Rect.y + uv.y * binding.Rect.height);
                        type = VertDynamicTexture;
                    }
                    Vertex(v.position, v.color, uv, type, id, v.circle, v.arc, v.dilate, textSettings, v.setting);
                }
                m_SettingOffset = 0;
                for (int i = s.indexStart; i < s.indexEnd; i++) Indices.Add(first + mesh.Indices[i]);
            }
        }
    }
}
