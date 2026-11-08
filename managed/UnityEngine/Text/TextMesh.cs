using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The legacy 3D text component. Its text is laid out by the legacy text layout
    /// (<see cref="TextLayout.GenerateTextMesh"/>, sharing UI Text's rich text, glyph measuring and
    /// font textures) into a hidden mesh of one quad per character, which the GameObject's
    /// <see cref="MeshRenderer"/> draws with its own materials instead of a MeshFilter's mesh.
    /// The mesh is regenerated lazily: a property change or a rebuild of a font texture marks it
    /// stale, and the next draw or bounds query lays the text out again (nothing is allocated
    /// while the text does not change).
    ///
    /// Documented (Unity 6000.0 Scripting API, TextMesh and its properties):
    /// - characterSize: "The size of each character (This scales the whole text)";
    /// - anchor: "Which point of the text shares the position of the Transform";
    /// - alignment: "How lines of text are aligned (Left, Right, Center)";
    /// - tabSize: "How much space will be inserted for a tab '\t' character. This is a multiplum of
    ///   the 'spacebar' character offset";
    /// - fontSize and fontStyle apply to dynamic fonts; offsetZ offsets the text along z;
    /// - lineSpacing, richText, color, font and text as named.
    /// A TextMesh requires a MeshRenderer (Unity's public metadata: RequireComponent).
    ///
    /// Matches the Unity 6000.0.71f1 Editor (tests/projects/textmesh):
    /// - world units: a font pixel is <c>characterSize / 10</c> units (<see cref="PixelsPerUnit"/>);
    ///   offsetZ is the vertices' local z, not scaled; the layout itself is
    ///   <see cref="TextLayout.GenerateTextMesh"/>'s;
    /// - the mesh: unnamed, HideAndDontSave, four vertices per quad (top left, top right, bottom
    ///   right, bottom left) with the glyph's uvs and the vertex colour (the text colour, or a color
    ///   tag's), triangles (1, 2, 0) and (2, 3, 0) of each quad, no normals or tangents; its bounds
    ///   (and the renderer's) are the text block's rectangle at offsetZ, not the quads' extent;
    /// - without a font (none set, or set to null) the font is the built-in LegacyRuntime.ttf, and
    ///   AddComponent gives a MeshRenderer without a material that font's material ("Font
    ///   Material"); a renderer that has one keeps it, and changing the font later changes no material;
    /// - fontSize or fontStyle on a font that is not dynamic warns at each layout.
    /// Unbound's choices (unmeasured): which line's size sets the step after it (its own), a
    /// negative tabSize (as 0), and &lt;material&gt;/&lt;quad&gt; (warn: one submesh with the
    /// renderer's materials). Scene field names are Unity 6000.0 shipped data's (serializedVersion
    /// 3); other versions warn.
    /// </summary>
    [ClassId(102)]
    [RequireComponent(typeof(Transform), typeof(MeshRenderer))]
    public sealed partial class TextMesh : Component
    {
        /// <summary>Font pixels per world unit at characterSize 1 (measured, tests/projects/textmesh).</summary>
        internal const float PixelsPerUnit = 10f;

        /// <summary>The serializedVersion whose field names Unity 6000.0's shipped data shows:
        /// the URP package samples' prefabs (Shaders/Lit/Prefabs/*Podium.prefab) serialize
        /// TextMesh as serializedVersion 3 with m_Text, m_OffsetZ, m_CharacterSize, m_LineSpacing,
        /// m_Anchor, m_Alignment, m_TabSize, m_FontSize, m_FontStyle, m_RichText, m_Font and
        /// m_Color (a packed rgba).</summary>
        internal const int VerifiedSerializedVersion = 3;

        string m_Text = "";
        Font m_Font;
        int m_FontSize;
        FontStyle m_FontStyle = FontStyle.Normal;
        float m_OffsetZ;
        TextAlignment m_Alignment = TextAlignment.Left;
        TextAnchor m_Anchor = TextAnchor.UpperLeft;
        float m_CharacterSize = 1f;
        float m_LineSpacing = 1f;
        float m_TabSize = 4f;
        bool m_RichText = true;
        Color m_Color = Color.white;

        bool m_Dirty = true;
        int m_BuiltAtRebuild = -1;
        bool m_Attached;
        Mesh m_Mesh;
        /// <summary>The layout's vertices, reused between regenerations.</summary>
        List<UIVertex> m_Verts;

        /// <summary>Font texture rebuilds so far: uvs generated before one are stale.</summary>
        static int s_FontRebuilds;
        /// <summary>Attached, undestroyed TextMeshes: renderers skip the lookup while there are none.</summary>
        static int s_Live;

        static TextMesh() => Font.textureRebuilt += _ => s_FontRebuilds++;

        public TextMesh() { }

        public string text { get => m_Text; set { m_Text = value; m_Dirty = true; } }

        /// <summary>The font; without one (or set to null) the built-in LegacyRuntime.ttf.</summary>
        public Font font
        {
            get => m_Font != null ? m_Font : Font.GetDefault();
            set
            {
                m_Font = value;
                m_Dirty = true;
            }
        }

        public int fontSize { get => m_FontSize; set { m_FontSize = value; m_Dirty = true; } }
        public FontStyle fontStyle { get => m_FontStyle; set { m_FontStyle = value; m_Dirty = true; } }

        /// <summary>Each layout of a font that is not dynamic with a size or style override warns
        /// (measured: once per generation, none for setting the properties alone;
        /// tests/projects/assetinstantiate font.overrides and use.font, tests/projects/textmesh).</summary>
        void WarnCustomFontOverrides(Font font)
        {
            if (Compat.AssetCloneResourcesMeasured && !font.dynamic && (m_FontSize != 0 || m_FontStyle != FontStyle.Normal))
                Debug.LogWarning("Font size and style overrides are only supported for dynamic fonts.");
        }
        public float offsetZ { get => m_OffsetZ; set { m_OffsetZ = value; m_Dirty = true; } }
        public TextAlignment alignment { get => m_Alignment; set { m_Alignment = value; m_Dirty = true; } }
        public TextAnchor anchor { get => m_Anchor; set { m_Anchor = value; m_Dirty = true; } }
        public float characterSize { get => m_CharacterSize; set { m_CharacterSize = value; m_Dirty = true; } }
        public float lineSpacing { get => m_LineSpacing; set { m_LineSpacing = value; m_Dirty = true; } }
        public float tabSize { get => m_TabSize; set { m_TabSize = value; m_Dirty = true; } }
        public bool richText { get => m_RichText; set { m_RichText = value; m_Dirty = true; } }
        public Color color { get => m_Color; set { m_Color = value; m_Dirty = true; } }

        /// <summary>The TextMesh of a renderer's GameObject, or null (no lookup while none exists).</summary>
        internal static TextMesh Of(GameObject go) => s_Live > 0 && go != null ? go.GetComponent<TextMesh>() : null;

        /// <summary>The generated mesh in the GameObject's local space, regenerated when stale;
        /// null without any font (no built-in font either).</summary>
        internal Mesh RenderMesh()
        {
            var font = this.font;
            if (font == null) return null;
            if (m_Dirty || m_Mesh == null || m_BuiltAtRebuild != s_FontRebuilds)
            {
                Generate(font);
                ReportMissingMaterial();
            }
            return m_Mesh;
        }

        /// <summary>AddComponent gives a renderer without a material the font's (measured; one
        /// that has a material keeps it).</summary>
        internal override void OnAddedByScript()
        {
            base.OnAddedByScript();
            var renderer = GetComponent<MeshRenderer>();
            var material = font?.material;
            if (renderer == null || material == null || HasMaterial(renderer)) return;
            renderer.sharedMaterial = material;
        }

        static bool HasMaterial(Renderer renderer)
        {
            foreach (var material in renderer.m_Materials)
                if (material != null) return true;
            return false;
        }

        /// <summary>A renderer without a material (as a scene may serialize it) draws nothing;
        /// Unbound says so rather than drawing nothing silently.</summary>
        void ReportMissingMaterial()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null || HasMaterial(renderer)) return;
            Stubs.Ignored("TextMesh without a material",
                "is not drawn: its MeshRenderer has no material (assign one, such as the font's material)");
        }

        void Generate(Font font)
        {
            ReportUnsupportedTags();
            WarnCustomFontOverrides(font);
            m_Verts ??= new List<UIVertex>();
            TextLayout.GenerateTextMesh(m_Text, font, m_Color, m_FontSize, m_FontStyle, m_LineSpacing, m_RichText, m_Anchor,
                m_Alignment, m_TabSize, m_Verts, out var block);
            // Read after generating: a rebuild this generation caused leaves its own uvs current.
            m_BuiltAtRebuild = s_FontRebuilds;
            m_Dirty = false;

            float scale = m_CharacterSize / PixelsPerUnit;
            int count = m_Verts.Count;
            var positions = new Vector3[count];
            var colors = new Color32[count];
            var uvs = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                var v = m_Verts[i];
                positions[i] = new Vector3(v.position.x * scale, v.position.y * scale, m_OffsetZ);
                colors[i] = v.color;
                uvs[i] = new Vector2(v.uv0.x, v.uv0.y);
            }
            var triangles = new int[count / 4 * 6];
            for (int q = 0, t = 0; q < count; q += 4)
            {
                triangles[t++] = q + 1;
                triangles[t++] = q + 2;
                triangles[t++] = q;
                triangles[t++] = q + 2;
                triangles[t++] = q + 3;
                triangles[t++] = q;
            }
            if (m_Mesh == null)
                using (ObjectRegistry.AsAssets())
                    m_Mesh = new Mesh { name = "", hideFlags = HideFlags.HideAndDontSave };
            m_Mesh.Clear();
            m_Mesh.indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m_Mesh.vertices = positions;
            m_Mesh.colors32 = colors;
            m_Mesh.uv = uvs;
            m_Mesh.triangles = triangles;
            // The bounds are the text block, not the quads' extent.
            var center = new Vector3((block.x + block.width * 0.5f) * scale, (block.y + block.height * 0.5f) * scale, m_OffsetZ);
            m_Mesh.bounds = new Bounds(center, new Vector3(Math.Abs(block.width * scale), Math.Abs(block.height * scale), 0f));
        }

        /// <summary>&lt;material&gt; and &lt;quad&gt; select no material: everything draws as one
        /// submesh with the renderer's materials, which warns once.</summary>
        void ReportUnsupportedTags()
        {
            var text = m_Text;
            if (m_RichText && text != null && (text.IndexOf("<material", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("<quad", StringComparison.OrdinalIgnoreCase) >= 0))
                Stubs.Ignored("TextMesh <material> and <quad> tags",
                    "are not honoured yet: they select no material, and all of the text (quads too) draws as one submesh with the MeshRenderer's materials");
        }

        internal override void OnAttached()
        {
            base.OnAttached();
            if (m_Attached) return;
            m_Attached = true;
            s_Live++;
        }

        internal override void OnEngineDestroy()
        {
            if (m_Attached)
            {
                m_Attached = false;
                s_Live--;
            }
            var mesh = m_Mesh;
            m_Mesh = null;
            // The hidden mesh goes with its component (no destruction pipeline re-entered).
            mesh?.MarkDestroyed();
            base.OnEngineDestroy();
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (TextMesh)source;
            m_Text = s.m_Text;
            m_Font = s.m_Font;
            m_FontSize = s.m_FontSize;
            m_FontStyle = s.m_FontStyle;
            m_OffsetZ = s.m_OffsetZ;
            m_Alignment = s.m_Alignment;
            m_Anchor = s.m_Anchor;
            m_CharacterSize = s.m_CharacterSize;
            m_LineSpacing = s.m_LineSpacing;
            m_TabSize = s.m_TabSize;
            m_RichText = s.m_RichText;
            m_Color = s.m_Color;
            m_Dirty = true;
        }

        /// <summary>Reads the field names Unity 6000.0's shipped data serializes (see
        /// <see cref="VerifiedSerializedVersion"/>); another serializedVersion warns once that its
        /// names are unverified and authored values may not load.</summary>
        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            int version = body.Int("serializedVersion", VerifiedSerializedVersion);
            if (version != VerifiedSerializedVersion)
                Stubs.Ignored("TextMesh serializedVersion " + version,
                    "is not verified: its field names are read as serializedVersion " + VerifiedSerializedVersion + "'s, so authored values may not load");
            m_Text = body.Str("m_Text", "");
            m_OffsetZ = body.Float("m_OffsetZ", 0f);
            m_CharacterSize = body.Float("m_CharacterSize", 1f);
            m_LineSpacing = body.Float("m_LineSpacing", 1f);
            m_Anchor = (TextAnchor)body.Int("m_Anchor", (int)TextAnchor.UpperLeft);
            m_Alignment = (TextAlignment)body.Int("m_Alignment", (int)TextAlignment.Left);
            m_TabSize = body.Float("m_TabSize", 4f);
            m_FontSize = body.Int("m_FontSize", 0);
            m_FontStyle = (FontStyle)body.Int("m_FontStyle", (int)FontStyle.Normal);
            m_RichText = body.Bool("m_RichText", true);
            var font = body.Ref("m_Font");
            m_Font = font.IsNull ? null : resolver?.Resolve(font, typeof(Font)) as Font;
            m_Color = PackedColor(body.Get("m_Color")?.Get("rgba"), Color.white);
            m_Dirty = true;
        }

        /// <summary>A packed rgba: red in the low byte, as the serialized Color32s Unbound reads
        /// (Unbound's choice, unmeasured for TextMesh: every shipped sample is white).</summary>
        static Color PackedColor(YNode packed, Color fallback)
        {
            if (packed == null || packed.Kind != YKind.Scalar) return fallback;
            uint rgba = unchecked((uint)packed.AsLong());
            return new Color32((byte)(rgba & 0xff), (byte)((rgba >> 8) & 0xff), (byte)((rgba >> 16) & 0xff), (byte)(rgba >> 24));
        }
    }
}
