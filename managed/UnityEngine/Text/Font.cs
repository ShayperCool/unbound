using System;
using System.Collections.Generic;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A font: a dynamic font renders glyphs from a TrueType/OpenType face (project font, the built-in
    /// LegacyRuntime.ttf or an OS font) into its texture on request, as Unity's FreeType-based dynamic
    /// fonts do; a custom (.fontsettings) font maps characters to rectangles of its own texture.
    /// </summary>
    [ClassId(128)]
    public sealed partial class Font : Object, ISerializedAsset
    {
        public delegate void FontTextureRebuildCallback();

        /// <summary>Raised when any dynamic font's texture is rebuilt: glyph uvs obtained before are stale.</summary>
        public static event Action<Font> textureRebuilt;

        event FontTextureRebuildCallback m_FontTextureRebuildCallback;

        // Serialized state, as Unity's Font keeps it: metrics at m_FontSize.
        internal float m_FontSize = 16f;
        internal float m_Ascent;
        internal float m_Descent;
        internal float m_LineSpacing;
        internal string[] m_FontNames = Array.Empty<string>();
        internal Material m_Material;
        internal Texture m_Texture;
        internal bool m_Dynamic;
        internal bool m_ShouldRoundAdvanceValue = true;
        internal int m_CharacterPadding = 1;
        internal int m_AsciiStartOffset;
        internal float m_Tracking = 1f;
        /// <summary>A custom font's characters (m_CharacterRects).</summary>
        internal readonly List<CharacterInfo> m_CharacterRects = new List<CharacterInfo>();
        internal readonly List<Font> m_FallbackFonts = new List<Font>();
        internal string[] m_FallbackGuids = Array.Empty<string>();

        /// <summary>The face a dynamic font renders from; null for custom fonts.</summary>
        internal GlyphSource m_Source;
        FontAtlas m_Atlas;

        public Font()
        {
        }

        /// <summary>A dynamic font of an installed OS font (a name) or a font file (a path).</summary>
        public Font(string name)
        {
            m_Name = name ?? "";
            if (string.IsNullOrEmpty(System.IO.Path.GetDirectoryName(name ?? "")))
                InitFromSource(NativeGlyphSource.Open(new { names = new[] { name } }), 16, name);
            else
                InitFromSource(NativeGlyphSource.Open(new { file = name }), 16, System.IO.Path.GetFileNameWithoutExtension(name));
        }

        /// <summary>A dynamic font over a face (the importers and tests).</summary>
        internal Font(GlyphSource source, int fontSize, string name)
        {
            m_Name = name ?? "";
            InitFromSource(source, fontSize, name);
        }

        internal void InitFromSource(GlyphSource source, int fontSize, string familyName, int ascentCalculationMode = 1)
        {
            m_Source = source;
            m_Dynamic = source != null;
            m_FontSize = fontSize;
            if (source != null)
            {
                float scale = fontSize / (float)Math.Max(1, source.UnitsPerEm);
                float ascender = ascentCalculationMode == 1 ? source.Ascender : source.YMax;
                m_Ascent = ascender * scale;
                m_Descent = source.Descender * scale;
                m_LineSpacing = (source.Ascender - source.Descender + source.LineGap) * scale;
                if (m_FontNames.Length == 0)
                    m_FontNames = new[] { string.IsNullOrEmpty(source.Family) ? familyName ?? "" : source.Family };
            }
            EnsureMaterial();
        }

        void EnsureMaterial()
        {
            if (m_Material != null || m_Source == null) return;
            m_Atlas ??= new FontAtlas(this);
            var shader = Shader.Find("GUI/Text Shader") ?? Shader.Create("GUI/Text Shader", 3000);
            m_Material = new Material(shader) { name = "Font Material", mainTexture = m_Atlas.Texture };
            m_Material.IsPersistentAsset = IsPersistentAsset;
            m_Texture = m_Atlas.Texture;
        }

        internal FontAtlas Atlas
        {
            get
            {
                if (m_Atlas == null && m_Source != null) EnsureMaterial();
                return m_Atlas;
            }
        }

        public Material material
        {
            get => m_Material;
            set => m_Material = value;
        }

        public string[] fontNames
        {
            get => (string[])m_FontNames.Clone();
            set => m_FontNames = value != null ? (string[])value.Clone() : Array.Empty<string>();
        }

        public bool dynamic => m_Dynamic;

        public int ascent => (int)m_Ascent;

        public int fontSize => (int)m_FontSize;

        public int lineHeight => (int)m_LineSpacing;

        public CharacterInfo[] characterInfo
        {
            get => m_Source != null ? Atlas.CharacterInfos() : m_CharacterRects.ToArray();
            set
            {
                m_CharacterRects.Clear();
                if (value != null) m_CharacterRects.AddRange(value);
            }
        }

        [Obsolete("Font.textureRebuildCallback has been deprecated. Use Font.textureRebuilt instead.")]
        public FontTextureRebuildCallback textureRebuildCallback
        {
            get => m_FontTextureRebuildCallback;
            set => m_FontTextureRebuildCallback = value;
        }

        public static Font CreateDynamicFontFromOSFont(string fontname, int size) => CreateDynamicFontFromOSFont(new[] { fontname }, size);

        public static Font CreateDynamicFontFromOSFont(string[] fontnames, int size)
        {
            var names = fontnames ?? Array.Empty<string>();
            var font = new Font(NativeGlyphSource.Open(new { names }), size, names.Length > 0 ? names[0] : "");
            font.m_FontNames = (string[])names.Clone();
            return font;
        }

        internal static void InvokeTextureRebuilt_Internal(Font font)
        {
            textureRebuilt?.Invoke(font);
            font.m_FontTextureRebuildCallback?.Invoke();
        }

        public static int GetMaxVertsForString(string str) => str.Length * 4 + 4;

        internal static Font GetDefault() => BuiltinFonts.LegacyRuntime;

        public bool HasCharacter(char c) => HasCharacter((int)c);

        internal bool HasCharacter(int c)
        {
            if (m_Source != null) return m_Source.HasCharacter(c);
            foreach (var info in m_CharacterRects)
                if (info.index == c) return true;
            return false;
        }

        public static string[] GetOSInstalledFontNames() => OsFonts().Names ?? Array.Empty<string>();

        public static string[] GetPathsToOSFonts() => OsFonts().Paths ?? Array.Empty<string>();

        internal static string[] GetOSFallbacks() => (string[])NativeGlyphSource.OsFallbackNames.Clone();

        sealed class OsFontList
        {
            public string[] Names { get; set; }
            public string[] Paths { get; set; }
        }

        static OsFontList s_OsFonts;

        static OsFontList OsFonts()
        {
            if (s_OsFonts != null) return s_OsFonts;
            try
            {
                s_OsFonts = JsonSerializer.Deserialize<OsFontList>(Native.Call("font-os-fonts", "{}"),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (Exception)
            {
                s_OsFonts = new OsFontList();
            }
            return s_OsFonts;
        }

        public bool GetCharacterInfo(char ch, out CharacterInfo info, int size, FontStyle style)
        {
            if (m_Source == null)
            {
                foreach (var c in m_CharacterRects)
                {
                    if (c.index == ch)
                    {
                        info = c;
                        return true;
                    }
                }
                info = default;
                return false;
            }
            return Atlas.TryGetCharacterInfo(ch, PixelSize(size), style, out info);
        }

        public bool GetCharacterInfo(char ch, out CharacterInfo info, int size) => GetCharacterInfo(ch, out info, size, FontStyle.Normal);

        public bool GetCharacterInfo(char ch, out CharacterInfo info) => GetCharacterInfo(ch, out info, 0, FontStyle.Normal);

        public void RequestCharactersInTexture(string characters, int size, FontStyle style)
        {
            if (m_Source == null || string.IsNullOrEmpty(characters)) return;
            var keys = new List<GlyphKey>(characters.Length);
            int pixelSize = PixelSize(size);
            for (int i = 0; i < characters.Length; i++)
            {
                int c = characters[i];
                if (char.IsHighSurrogate(characters, i) && i + 1 < characters.Length && char.IsLowSurrogate(characters[i + 1]))
                    c = char.ConvertToUtf32(characters[i], characters[++i]);
                keys.Add(new GlyphKey(c, pixelSize, style));
            }
            Atlas.Request(keys);
        }

        public void RequestCharactersInTexture(string characters, int size) => RequestCharactersInTexture(characters, size, FontStyle.Normal);

        public void RequestCharactersInTexture(string characters) => RequestCharactersInTexture(characters, 0, FontStyle.Normal);

        /// <summary>The pixel size a request renders at: 0 is the font's own size.</summary>
        internal int PixelSize(int size) => size <= 0 || !m_Dynamic ? Math.Max(1, (int)m_FontSize) : size;

        /// <summary>Ascent, descent and line spacing at a pixel size (Unity scales its m_FontSize metrics).</summary>
        internal float AscentAt(int size) => m_Dynamic ? m_Ascent * size / Math.Max(1f, m_FontSize) : m_Ascent;
        internal float DescentAt(int size) => m_Dynamic ? m_Descent * size / Math.Max(1f, m_FontSize) : m_Descent;
        internal float LineSpacingAt(int size) => m_Dynamic ? m_LineSpacing * size / Math.Max(1f, m_FontSize) : m_LineSpacing;

        /// <summary>The glyph sources to try for a character: the face, then the fallback fonts.</summary>
        internal IEnumerable<GlyphSource> Sources()
        {
            if (m_Source != null) yield return m_Source;
            ResolveFallbacks();
            foreach (var fallback in m_FallbackFonts)
                if (fallback != null && fallback != this && fallback.m_Source != null)
                    yield return fallback.m_Source;
            var os = NativeGlyphSource.OsFallback;
            if (os != null) yield return os;
        }

        void ResolveFallbacks()
        {
            if (m_FallbackGuids.Length == 0) return;
            var guids = m_FallbackGuids;
            m_FallbackGuids = Array.Empty<string>();
            foreach (var guid in guids)
            {
                try
                {
                    if (AssetDatabaseRuntime.LoadMain(guid, typeof(Font)) is Font f) m_FallbackFonts.Add(f);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Unbound: fallback font {guid} of '{m_Name}': {e.Message}");
                }
            }
        }

        /// <summary>A custom font (.fontsettings) or a font serialized in an asset file.</summary>
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_LineSpacing = body.Float("m_LineSpacing");
            m_FontSize = body.Float("m_FontSize", 16f);
            m_Ascent = body.Float("m_Ascent");
            m_Descent = body.Float("m_Descent");
            m_AsciiStartOffset = body.Int("m_AsciiStartOffset");
            m_Tracking = body.Float("m_Tracking", 1f);
            m_CharacterPadding = body.Int("m_CharacterPadding", 1);
            m_ShouldRoundAdvanceValue = body.Bool("m_ShouldRoundAdvanceValue", true);
            m_Material = resolver.Resolve(body.Ref("m_DefaultMaterial"), typeof(Material)) as Material;
            m_Texture = resolver.Resolve(body.Ref("m_Texture"), typeof(Texture)) as Texture;
            var names = new List<string>();
            foreach (var n in body.Get("m_FontNames")?.Seq() ?? Array.Empty<YNode>()) names.Add(n.AsString());
            m_FontNames = names.ToArray();
            m_CharacterRects.Clear();
            foreach (var rect in body.Get("m_CharacterRects")?.Seq() ?? Array.Empty<YNode>())
            {
                var info = new CharacterInfo
                {
                    index = rect.Int("index"),
#pragma warning disable 618
                    uv = ReadRect(rect.Get("uv")),
                    vert = ReadRect(rect.Get("vert")),
                    width = rect.Float("advance", rect.Float("width")),
                    flipped = rect.Bool("flipped"),
#pragma warning restore 618
                };
                m_CharacterRects.Add(info);
            }
            m_Dynamic = false;
        }

        static Rect ReadRect(YNode node) =>
            node == null ? default : new Rect(node.Float("x"), node.Float("y"), node.Float("width"), node.Float("height"));
    }

    /// <summary>A character of a font texture: its uv rectangle and its metrics in pixels.</summary>
    public partial struct CharacterInfo
    {
        public int index;
        [Obsolete("CharacterInfo.uv is deprecated. Use uvBottomLeft, uvBottomRight, uvTopRight or uvTopLeft instead.")]
        public Rect uv;
        [Obsolete("CharacterInfo.vert is deprecated. Use minX, maxX, minY, maxY instead.")]
        public Rect vert;
        [Obsolete("CharacterInfo.width is deprecated. Use advance instead.")]
        public float width;
        public int size;
        public FontStyle style;
        [Obsolete("CharacterInfo.flipped is deprecated. Use uvBottomLeft, uvBottomRight, uvTopRight or uvTopLeft instead, which will be correct regardless of orientation.")]
        public bool flipped;

#pragma warning disable 618
        public int advance
        {
            get => (int)Math.Round(width, MidpointRounding.AwayFromZero);
            set => width = value;
        }

        public int glyphWidth
        {
            get => (int)vert.width;
            set => vert.width = value;
        }

        public int glyphHeight
        {
            get => (int)-vert.height;
            set
            {
                var old = vert.height;
                vert.height = -value;
                vert.y += old - vert.height;
            }
        }

        public int bearing
        {
            get => (int)vert.x;
            set => vert.x = value;
        }

        public int minY
        {
            get => (int)(vert.y + vert.height);
            set => vert.height = value - vert.y;
        }

        public int maxY
        {
            get => (int)vert.y;
            set
            {
                float delta = vert.y - value;
                vert.y = value;
                vert.height += delta;
            }
        }

        public int minX
        {
            get => (int)vert.x;
            set
            {
                float delta = vert.x - value;
                vert.x = value;
                vert.width += delta;
            }
        }

        public int maxX
        {
            get => (int)(vert.x + vert.width);
            set => vert.width = value - vert.x;
        }

        internal Vector2 uvBottomLeftUnFlipped
        {
            get => new Vector2(uv.x, uv.y);
            set
            {
                var topRight = uvTopRightUnFlipped;
                uv.x = value.x;
                uv.y = value.y;
                uv.width = topRight.x - uv.x;
                uv.height = topRight.y - uv.y;
            }
        }

        internal Vector2 uvBottomRightUnFlipped
        {
            get => new Vector2(uv.x + uv.width, uv.y);
            set
            {
                var topRight = uvTopRightUnFlipped;
                uv.width = value.x - uv.x;
                uv.y = value.y;
                uv.height = topRight.y - uv.y;
            }
        }

        internal Vector2 uvTopRightUnFlipped
        {
            get => new Vector2(uv.x + uv.width, uv.y + uv.height);
            set
            {
                uv.width = value.x - uv.x;
                uv.height = value.y - uv.y;
            }
        }

        internal Vector2 uvTopLeftUnFlipped
        {
            get => new Vector2(uv.x, uv.y + uv.height);
            set
            {
                var topRight = uvTopRightUnFlipped;
                uv.x = value.x;
                uv.height = value.y - uv.y;
                uv.width = topRight.x - uv.x;
            }
        }

        public Vector2 uvBottomLeft
        {
            get => uvBottomLeftUnFlipped;
            set => uvBottomLeftUnFlipped = value;
        }

        public Vector2 uvBottomRight
        {
            get => flipped ? uvTopLeftUnFlipped : uvBottomRightUnFlipped;
            set
            {
                if (flipped) uvTopLeftUnFlipped = value;
                else uvBottomRightUnFlipped = value;
            }
        }

        public Vector2 uvTopRight
        {
            get => uvTopRightUnFlipped;
            set => uvTopRightUnFlipped = value;
        }

        public Vector2 uvTopLeft
        {
            get => flipped ? uvBottomRightUnFlipped : uvTopLeftUnFlipped;
            set
            {
                if (flipped) uvBottomRightUnFlipped = value;
                else uvTopLeftUnFlipped = value;
            }
        }
#pragma warning restore 618
    }
}
