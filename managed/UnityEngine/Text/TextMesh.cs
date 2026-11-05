using System;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// The legacy 3D text component. Its text is laid out by the legacy <see cref="TextGenerator"/>
    /// (as UI Text's is) into a mesh of one quad per visible character, which the GameObject's
    /// <see cref="MeshRenderer"/> draws with its own materials instead of a MeshFilter's mesh.
    /// The mesh is regenerated lazily: a property change or a rebuild of a font texture marks it
    /// stale, and the next draw or bounds query lays the text out again.
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
    /// Unbound's choices (unmeasured, no Reference measures TextMesh):
    /// - world units: a font pixel is <c>characterSize / 10</c> units (<see cref="PixelsPerUnit"/>);
    /// - line height and spacing are the legacy generator's (the font's line spacing at the pixel
    ///   size, rounded, times lineSpacing);
    /// - rich text is the legacy generator's subset (b, i, size, color, material, quad);
    ///   &lt;material&gt; selects no material and every quad draws with the renderer's materials,
    ///   which warns once;
    /// - vertices: four per visible character (top left, top right, bottom right, bottom left), two
    ///   triangles each, the generator's uv0 and vertex colours (the text colour, or a color tag's);
    ///   no normals or tangents; offsetZ is the vertices' local z, not scaled by characterSize;
    /// - the font texture is whatever the renderer's material samples (Unbound assigns no material);
    /// - tabs advance tabSize spaces, rounded to whole spaces (a fractional size warns once);
    /// - defaults of a new component: empty text, no font, size 0, Normal, offset 0, Left, UpperLeft,
    ///   characterSize 1, lineSpacing 1, tabSize 4, rich text on, white;
    /// - with a TextMesh, a MeshFilter's mesh is not drawn; without a font nothing is drawn.
    /// </summary>
    [ClassId(102)]
    [RequireComponent(typeof(Transform), typeof(MeshRenderer))]
    public sealed partial class TextMesh : Component
    {
        /// <summary>Font pixels per world unit at characterSize 1 (Unbound's choice, unmeasured).</summary>
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
        TextGenerator m_Generator;

        /// <summary>Font texture rebuilds so far: uvs generated before one are stale.</summary>
        static int s_FontRebuilds;
        /// <summary>Attached, undestroyed TextMeshes: renderers skip the lookup while there are none.</summary>
        static int s_Live;

        static TextMesh() => Font.textureRebuilt += _ => s_FontRebuilds++;

        public TextMesh() { }

        public string text { get => m_Text; set { m_Text = value; m_Dirty = true; } }
        public Font font { get => m_Font; set { m_Font = value; m_Dirty = true; } }
        public int fontSize { get => m_FontSize; set { m_FontSize = value; m_Dirty = true; } }
        public FontStyle fontStyle { get => m_FontStyle; set { m_FontStyle = value; m_Dirty = true; } }
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
        /// null without a font.</summary>
        internal Mesh RenderMesh()
        {
            if (m_Font == null) return null;
            if (m_Dirty || m_Mesh == null || m_BuiltAtRebuild != s_FontRebuilds) Generate();
            return m_Mesh;
        }

        void Generate()
        {
            var font = m_Font;
            m_Generator ??= new TextGenerator();
            bool dynamic = font.dynamic;
            var settings = new TextGenerationSettings
            {
                font = font,
                color = m_Color,
                // Documented as for dynamic fonts: other fonts keep their own size and style.
                fontSize = dynamic ? m_FontSize : 0,
                fontStyle = dynamic ? m_FontStyle : FontStyle.Normal,
                lineSpacing = m_LineSpacing,
                richText = m_RichText,
                scaleFactor = 1f,
                // Lines align within the block by the alignment; the block's top, middle or bottom
                // sits on the origin by the anchor. Its left, centre or right is placed below.
                textAnchor = (TextAnchor)(Row(m_Anchor) * 3 + Column(m_Alignment)),
                alignByGeometry = false,
                resizeTextForBestFit = false,
                updateBounds = true,
                verticalOverflow = VerticalWrapMode.Overflow,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                generationExtents = Vector2.zero,
                pivot = Vector2.zero,
                generateOutOfBounds = true,
            };
            m_Generator.Invalidate();
            m_Generator.Populate(LaidOutText(), settings);
            // Read after generating: a rebuild this generation caused leaves its own uvs current.
            m_BuiltAtRebuild = s_FontRebuilds;
            m_Dirty = false;

            // With empty extents each line is placed about x = 0 by the alignment (left lines start
            // there, right lines end there); move the block so the anchor's point is at the origin.
            float width = m_Generator.rectExtents.width;
            float dx = width * (Column(m_Alignment) - AnchorColumn(m_Anchor)) * 0.5f;
            float scale = m_CharacterSize / PixelsPerUnit;
            var verts = m_Generator.verts;
            int count = verts.Count - verts.Count % 4;
            var positions = new Vector3[count];
            var colors = new Color32[count];
            var uvs = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                var v = verts[i];
                positions[i] = new Vector3((v.position.x + dx) * scale, v.position.y * scale, m_OffsetZ);
                colors[i] = v.color;
                uvs[i] = new Vector2(v.uv0.x, v.uv0.y);
            }
            var triangles = new int[count / 4 * 6];
            for (int q = 0, t = 0; q < count; q += 4)
            {
                triangles[t++] = q;
                triangles[t++] = q + 1;
                triangles[t++] = q + 2;
                triangles[t++] = q + 2;
                triangles[t++] = q + 3;
                triangles[t++] = q;
            }
            if (m_Mesh == null)
                using (ObjectRegistry.AsAssets())
                    m_Mesh = new Mesh { name = "TextMesh", hideFlags = HideFlags.HideAndDontSave };
            m_Mesh.Clear();
            m_Mesh.indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m_Mesh.vertices = positions;
            m_Mesh.colors32 = colors;
            m_Mesh.uv = uvs;
            m_Mesh.triangles = triangles;
        }

        /// <summary>The text as laid out: tabs become tabSize spaces.</summary>
        string LaidOutText()
        {
            var text = m_Text ?? "";
            if (m_RichText && (text.IndexOf("<material", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("<quad", StringComparison.OrdinalIgnoreCase) >= 0))
                Stubs.Ignored("TextMesh <material> and <quad> tags",
                    "are not honoured yet: they select no material, and all of the text (quads too) draws as one submesh with the MeshRenderer's materials");
            if (text.IndexOf('\t') < 0) return text;
            float size = Math.Max(0f, m_TabSize);
            int spaces = (int)Math.Round(size, MidpointRounding.AwayFromZero);
            if (spaces != size)
                Stubs.Ignored("TextMesh.tabSize", "of a fraction of a space is not honoured yet: tabs advance a whole number of spaces");
            return text.Replace("\t", new string(' ', spaces));
        }

        static int Row(TextAnchor anchor) => anchor >= TextAnchor.UpperLeft && anchor <= TextAnchor.LowerRight ? (int)anchor / 3 : 0;

        static int AnchorColumn(TextAnchor anchor) => anchor >= TextAnchor.UpperLeft && anchor <= TextAnchor.LowerRight ? (int)anchor % 3 : 0;

        static int Column(TextAlignment alignment) => alignment >= TextAlignment.Left && alignment <= TextAlignment.Right ? (int)alignment : 0;

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

        /// <summary>A packed rgba: red in the low byte (as the serialized Color32s Unbound reads).</summary>
        static Color PackedColor(YNode packed, Color fallback)
        {
            if (packed == null || packed.Kind != YKind.Scalar) return fallback;
            uint rgba = unchecked((uint)packed.AsLong());
            return new Color32((byte)(rgba & 0xff), (byte)((rgba >> 8) & 0xff), (byte)((rgba >> 16) & 0xff), (byte)(rgba >> 24));
        }
    }
}
