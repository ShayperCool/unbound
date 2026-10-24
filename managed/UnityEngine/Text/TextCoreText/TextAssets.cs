using System;
using System.Collections.Generic;
using UnityEngine.Serialization;
using UnityEngine.TextCore.LowLevel;

// The assets of TextCore's text engine and their elements, with the serialized field names of
// Unity's (project .asset files deserialize into them).

namespace UnityEngine.TextCore.Text
{
    [Serializable]
    public abstract partial class TextAsset : ScriptableObject
    {
        [SerializeField] internal string m_Version;
        internal int m_InstanceID;
        internal int m_HashCode;
        [SerializeField] [FormerlySerializedAs("material")] internal Material m_Material;
        internal int m_MaterialHashCode;

        protected TextAsset() { }

        public string version
        {
            get => m_Version;
            internal set => m_Version = value;
        }

        public int instanceID
        {
            get
            {
                if (m_InstanceID == 0) m_InstanceID = GetInstanceID();
                return m_InstanceID;
            }
        }

        public int hashCode
        {
            get
            {
                if (m_HashCode == 0) m_HashCode = TextUtilities.GetHashCodeCaseInSensitive(name);
                return m_HashCode;
            }
            set => m_HashCode = value;
        }

        public Material material
        {
            get => m_Material;
            set => m_Material = value;
        }

        public int materialHashCode
        {
            get
            {
                if (m_MaterialHashCode == 0)
                {
                    if (m_Material == null) return 0;
                    m_MaterialHashCode = TextUtilities.GetHashCodeCaseInSensitive(m_Material.name);
                }
                return m_MaterialHashCode;
            }
            set => m_MaterialHashCode = value;
        }
    }

    /// <summary>A character or sprite of a text asset: its code point, glyph and scale.</summary>
    [Serializable]
    public abstract partial class TextElement
    {
        [SerializeField] protected TextElementType m_ElementType;
        [SerializeField] internal uint m_Unicode;
        internal TextAsset m_TextAsset;
        internal Glyph m_Glyph;
        [SerializeField] internal uint m_GlyphIndex;
        [SerializeField] internal float m_Scale;

        protected TextElement() { }

        public TextElementType elementType => m_ElementType;
        public uint unicode { get => m_Unicode; set => m_Unicode = value; }
        public TextAsset textAsset { get => m_TextAsset; set => m_TextAsset = value; }
        public Glyph glyph { get => m_Glyph; set => m_Glyph = value; }
        public uint glyphIndex { get => m_GlyphIndex; set => m_GlyphIndex = value; }
        public float scale { get => m_Scale; set => m_Scale = value; }
    }

    [Serializable]
    public partial class Character : TextElement
    {
        public Character()
        {
            m_ElementType = TextElementType.Character;
            m_Scale = 1f;
        }

        public Character(uint unicode, Glyph glyph) : this(unicode, null, glyph) { }

        public Character(uint unicode, FontAsset fontAsset, Glyph glyph)
        {
            m_ElementType = TextElementType.Character;
            m_Unicode = unicode;
            m_TextAsset = fontAsset;
            m_Glyph = glyph;
            m_GlyphIndex = glyph.index;
            m_Scale = 1f;
        }

        /// <summary>A character whose glyph is not loaded yet.</summary>
        internal Character(uint unicode, uint glyphIndex)
        {
            m_ElementType = TextElementType.Character;
            m_Unicode = unicode;
            m_GlyphIndex = glyphIndex;
            m_Scale = 1f;
        }
    }

    /// <summary>The font assets of a weight's regular and italic typefaces.</summary>
    [Serializable]
    public partial struct FontWeightPair
    {
        public FontAsset regularTypeface;
        public FontAsset italicTypeface;
    }

    [Serializable]
    public partial struct FontAssetCreationEditorSettings
    {
        public string sourceFontFileGUID;
        public int faceIndex;
        public int pointSizeSamplingMode;
        public float pointSize;
        public int padding;
        public int paddingMode;
        public int packingMode;
        public int atlasWidth;
        public int atlasHeight;
        public int characterSetSelectionMode;
        public string characterSequence;
        public string referencedFontAssetGUID;
        public string referencedTextAssetGUID;
        public int fontStyle;
        public float fontStyleModifier;
        public int renderMode;
        public bool includeFontFeatures;

        internal FontAssetCreationEditorSettings(string sourceFontFileGUID, float pointSize, int pointSizeSamplingMode, int padding, int packingMode, int atlasWidth, int atlasHeight, int characterSelectionMode, string characterSet, int renderMode)
        {
            this.sourceFontFileGUID = sourceFontFileGUID;
            faceIndex = 0;
            this.pointSize = pointSize;
            this.pointSizeSamplingMode = pointSizeSamplingMode;
            this.padding = padding;
            paddingMode = 2;
            this.packingMode = packingMode;
            this.atlasWidth = atlasWidth;
            this.atlasHeight = atlasHeight;
            characterSequence = characterSet;
            characterSetSelectionMode = characterSelectionMode;
            this.renderMode = renderMode;
            referencedFontAssetGUID = string.Empty;
            referencedTextAssetGUID = string.Empty;
            fontStyle = 0;
            fontStyleModifier = 0f;
            includeFontFeatures = false;
        }
    }

    /// <summary>A font asset's OpenType features. Kerning (pair adjustments) is what the generator
    /// applies (TextGenerationSettings.fontFeatures); the substitution and mark tables are kept
    /// in the serialized lists the font engine fills.</summary>
    [Serializable]
    public partial class FontFeatureTable
    {
        [SerializeField] List<GlyphPairAdjustmentRecord> m_GlyphPairAdjustmentRecords;
        /// <summary>Pairs by <c>second glyph &lt;&lt; 16 | first glyph</c>.</summary>
        internal Dictionary<uint, GlyphPairAdjustmentRecord> m_GlyphPairAdjustmentRecordLookup;

        internal FontFeatureTable()
        {
            m_GlyphPairAdjustmentRecords = new List<GlyphPairAdjustmentRecord>();
            m_GlyphPairAdjustmentRecordLookup = new Dictionary<uint, GlyphPairAdjustmentRecord>();
        }

        internal List<GlyphPairAdjustmentRecord> glyphPairAdjustmentRecords => m_GlyphPairAdjustmentRecords ??= new List<GlyphPairAdjustmentRecord>();

        /// <summary>Orders the pairs by first glyph, then second.</summary>
        public void SortGlyphPairAdjustmentRecords()
        {
            if (m_GlyphPairAdjustmentRecords == null || m_GlyphPairAdjustmentRecords.Count == 0) return;
            var sorted = new List<GlyphPairAdjustmentRecord>(m_GlyphPairAdjustmentRecords);
            sorted.Sort((a, b) =>
            {
                int first = a.firstAdjustmentRecord.glyphIndex.CompareTo(b.firstAdjustmentRecord.glyphIndex);
                return first != 0 ? first : a.secondAdjustmentRecord.glyphIndex.CompareTo(b.secondAdjustmentRecord.glyphIndex);
            });
            m_GlyphPairAdjustmentRecords = sorted;
        }

        internal static uint PairKey(uint first, uint second) => (second << 16) | first;
    }

    /// <summary>Sprites in text are not supported yet: sprite assets are never found.</summary>
    public partial class SpriteAsset : TextAsset
    {
    }

    /// <summary>A named pair of opening and closing tag sequences a &lt;style&gt; tag expands to.</summary>
    [Serializable]
    public partial class TextStyle
    {
        internal static TextStyle k_NormalStyle;

        [SerializeField] string m_Name;
        [SerializeField] int m_HashCode;
        [SerializeField] string m_OpeningDefinition;
        [SerializeField] string m_ClosingDefinition;
        [SerializeField] uint[] m_OpeningTagArray;
        [SerializeField] uint[] m_ClosingTagArray;
        [SerializeField] internal uint[] m_OpeningTagUnicodeArray;
        [SerializeField] internal uint[] m_ClosingTagUnicodeArray;

        internal TextStyle(string styleName, string styleOpeningDefinition, string styleClosingDefinition)
        {
            m_Name = styleName;
            m_OpeningDefinition = styleOpeningDefinition;
            m_ClosingDefinition = styleClosingDefinition;
            RefreshStyle();
        }

        public static TextStyle NormalStyle => k_NormalStyle ??= new TextStyle("Normal", string.Empty, string.Empty);

        public string name { get => m_Name; set => m_Name = value; }
        public int hashCode { get => m_HashCode; set => m_HashCode = value; }
        public string styleOpeningDefinition => m_OpeningDefinition;
        public string styleClosingDefinition => m_ClosingDefinition;
        public uint[] styleOpeningTagArray => m_OpeningTagArray;
        public uint[] styleClosingTagArray => m_ClosingTagArray;

        public void RefreshStyle()
        {
            m_HashCode = TextUtilities.GetHashCodeCaseInSensitive(m_Name ?? "");
            m_OpeningTagArray = ToCodes(m_OpeningDefinition);
            m_OpeningTagUnicodeArray = ToCodes(m_OpeningDefinition);
            m_ClosingTagArray = ToCodes(m_ClosingDefinition);
            m_ClosingTagUnicodeArray = ToCodes(m_ClosingDefinition);
        }

        static uint[] ToCodes(string s)
        {
            var codes = new uint[s?.Length ?? 0];
            for (int i = 0; i < codes.Length; i++) codes[i] = s[i];
            return codes;
        }
    }

    [Serializable]
    public partial class TextStyleSheet : ScriptableObject
    {
        [SerializeField] List<TextStyle> m_StyleList = new List<TextStyle>(1);
        Dictionary<int, TextStyle> m_StyleLookupDictionary;
        readonly object m_LookupLock = new object();

        internal List<TextStyle> styles => m_StyleList;

        public TextStyle GetStyle(int hashCode)
        {
            if (m_StyleLookupDictionary == null)
                lock (m_LookupLock)
                    if (m_StyleLookupDictionary == null) LoadStyleDictionary();
            return m_StyleLookupDictionary.TryGetValue(hashCode, out var style) ? style : null;
        }

        public TextStyle GetStyle(string name) => GetStyle(TextUtilities.GetHashCodeCaseInSensitive(name));

        public void RefreshStyles() => LoadStyleDictionary();

        /// <summary>The styles by name hash, with a "Normal" style that adds nothing.</summary>
        void LoadStyleDictionary()
        {
            var lookup = new Dictionary<int, TextStyle>();
            m_StyleList ??= new List<TextStyle>();
            foreach (var style in m_StyleList)
            {
                if (style == null) continue;
                style.RefreshStyle();
                lookup.TryAdd(style.hashCode, style);
            }
            int normal = TextUtilities.GetHashCodeCaseInSensitive("Normal");
            if (!lookup.ContainsKey(normal))
            {
                var style = new TextStyle("Normal", string.Empty, string.Empty);
                m_StyleList.Add(style);
                lookup.Add(normal, style);
            }
            m_StyleLookupDictionary = lookup;
        }
    }

    /// <summary>The characters a line may not start with (leading) or end with (following), for
    /// CJK line breaking: the text assets "LineBreaking Leading Characters" and "LineBreaking
    /// Following Characters" from Resources, else none.</summary>
    [Serializable]
    public partial class UnicodeLineBreakingRules
    {
        [SerializeField] UnityEngine.TextAsset m_UnicodeLineBreakingRules;
        [SerializeField] UnityEngine.TextAsset m_LeadingCharacters;
        [SerializeField] UnityEngine.TextAsset m_FollowingCharacters;
        [SerializeField] bool m_UseModernHangulLineBreakingRules;
        HashSet<uint> m_LeadingCharactersLookup;
        HashSet<uint> m_FollowingCharactersLookup;

        public UnityEngine.TextAsset lineBreakingRules => m_UnicodeLineBreakingRules;
        public UnityEngine.TextAsset leadingCharacters => m_LeadingCharacters;
        public UnityEngine.TextAsset followingCharacters => m_FollowingCharacters;

        internal HashSet<uint> leadingCharactersLookup
        {
            get
            {
                if (m_LeadingCharactersLookup == null) LoadLineBreakingRules();
                return m_LeadingCharactersLookup;
            }
            set => m_LeadingCharactersLookup = value;
        }

        internal HashSet<uint> followingCharactersLookup
        {
            get
            {
                if (m_LeadingCharactersLookup == null) LoadLineBreakingRules();
                return m_FollowingCharactersLookup;
            }
            set => m_FollowingCharactersLookup = value;
        }

        public bool useModernHangulLineBreakingRules
        {
            get => m_UseModernHangulLineBreakingRules;
            set => m_UseModernHangulLineBreakingRules = value;
        }

        internal void LoadLineBreakingRules() => LoadLineBreakingRules(m_LeadingCharacters, m_FollowingCharacters);

        internal void LoadLineBreakingRules(UnityEngine.TextAsset leadingRules, UnityEngine.TextAsset followingRules)
        {
            if (m_LeadingCharactersLookup != null) return;
            leadingRules ??= Load("LineBreaking Leading Characters");
            followingRules ??= Load("LineBreaking Following Characters");
            m_LeadingCharacters ??= leadingRules;
            m_FollowingCharacters ??= followingRules;
            m_LeadingCharactersLookup = Characters(leadingRules);
            m_FollowingCharactersLookup = Characters(followingRules);
        }

        static UnityEngine.TextAsset Load(string name)
        {
            try
            {
                return Resources.Load<UnityEngine.TextAsset>(name);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static HashSet<uint> Characters(UnityEngine.TextAsset file)
        {
            var set = new HashSet<uint>();
            var text = file != null ? file.text : null;
            if (text != null)
                foreach (var c in text) set.Add(c);
            return set;
        }
    }
}
