using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine.Serialization;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    /// <summary>
    /// A font for TextCore's text engine: the face's metrics, the characters and glyphs it has
    /// rendered into its atlas textures, and their kerning. A dynamic font asset loads its face from
    /// a Font (or a font file, or an OS font) through the FontEngine and adds glyphs to its atlas as
    /// text asks for them, packing them into its free rects (one more atlas texture when full,
    /// with multi-atlas support). Kerning pairs of new glyphs are read later, when
    /// <see cref="UpdateFontAssetsInUpdateQueue"/> runs (GenerateText does first), as in Unity: a
    /// first measure of new characters is without their kerning.
    /// </summary>
    [Serializable]
    public partial class FontAsset : TextAsset
    {
        /// <summary>The atlas padding Unity's SDF materials add one to (their gradient scale).</summary>
        const int k_SdfGradientExtra = 1;

        static readonly Dictionary<int, FontAsset> s_FontAssetByInstanceId = new Dictionary<int, FontAsset>();

        [SerializeField] internal string m_SourceFontFileGUID;
        internal Font m_SourceFontFile_EditorRef;
        [SerializeField] internal FontAssetCreationEditorSettings m_fontAssetCreationEditorSettings;
        [SerializeField] Font m_SourceFontFile;
        [SerializeField] internal string m_SourceFontFilePath;
        [SerializeField] AtlasPopulationMode m_AtlasPopulationMode;
        [SerializeField] internal bool InternalDynamicOS;
        [SerializeField] internal bool IsEditorFont;
        [SerializeField] internal FaceInfo m_FaceInfo;
        int m_FamilyNameHashCode;
        int m_StyleNameHashCode;
        [SerializeField] internal List<Glyph> m_GlyphTable = new List<Glyph>();
        internal Dictionary<uint, Glyph> m_GlyphLookupDictionary;
        [SerializeField] internal List<Character> m_CharacterTable = new List<Character>();
        /// <summary>Characters by code point, and by <see cref="CompositeKey"/> for styled lookups.</summary>
        internal Dictionary<uint, Character> m_CharacterLookupDictionary;
        internal Texture2D m_AtlasTexture;
        [SerializeField] internal Texture2D[] m_AtlasTextures;
        [SerializeField] internal int m_AtlasTextureIndex;
        [SerializeField] bool m_IsMultiAtlasTexturesEnabled;
        [SerializeField] bool m_GetFontFeatures = true;
        [SerializeField] bool m_ClearDynamicDataOnBuild;
        [SerializeField] internal int m_AtlasWidth;
        [SerializeField] internal int m_AtlasHeight;
        [SerializeField] internal int m_AtlasPadding;
        [SerializeField] internal GlyphRenderMode m_AtlasRenderMode;
        [SerializeField] List<GlyphRect> m_UsedGlyphRects;
        [SerializeField] List<GlyphRect> m_FreeGlyphRects;
        [SerializeField] internal FontFeatureTable m_FontFeatureTable = new FontFeatureTable();
        [SerializeField] internal bool m_ShouldReimportFontFeatures;
        [SerializeField] internal List<FontAsset> m_FallbackFontAssetTable;
        [SerializeField] FontWeightPair[] m_FontWeightTable = new FontWeightPair[10];
        [SerializeField] [FormerlySerializedAs("normalStyle")] internal float m_RegularStyleWeight;
        [SerializeField] [FormerlySerializedAs("normalSpacingOffset")] internal float m_RegularStyleSpacing;
        [SerializeField] [FormerlySerializedAs("boldStyle")] internal float m_BoldStyleWeight = 0.75f;
        [SerializeField] [FormerlySerializedAs("boldSpacing")] internal float m_BoldStyleSpacing = 7f;
        [SerializeField] [FormerlySerializedAs("italicStyle")] internal byte m_ItalicStyleSlant = 35;
        [SerializeField] [FormerlySerializedAs("tabSize")] internal byte m_TabMultiple = 10;

        internal bool IsFontAssetLookupTablesDirty;

        /// <summary>Glyph indexes in the atlas, and those added since font features were read.</summary>
        List<uint> m_GlyphIndexList = new List<uint>();
        List<uint> m_GlyphIndexListNewlyAdded = new List<uint>();
        internal List<uint> m_GlyphsToAdd = new List<uint>();
        internal HashSet<uint> m_GlyphsToAddLookup = new HashSet<uint>();
        internal List<Character> m_CharactersToAdd = new List<Character>();
        internal HashSet<uint> m_CharactersToAddLookup = new HashSet<uint>();
        internal List<uint> s_MissingCharacterList = new List<uint>();
        internal HashSet<uint> m_MissingUnicodesFromFontFile = new HashSet<uint>();
        internal Dictionary<(uint, uint), uint> m_VariantGlyphIndexes = new Dictionary<(uint, uint), uint>();
        internal bool m_IsClone;

        internal static Action<Texture, FontAsset> OnFontAssetTextureChanged;
        internal static Action<FontAsset> RegisterResourceForUpdate;
        internal static Action<FontAsset> RegisterResourceForReimport;
        internal static Action<Texture2D, bool> SetAtlasTextureIsReadable;
        internal static Func<string, Font> GetSourceFontRef;
        internal static Func<Font, string> SetSourceFontGUID;
        internal static Func<bool> EditorApplicationIsUpdating;

        static HashSet<int> s_SearchedFontAssets;
        static readonly List<FontAsset> s_FontFeaturesUpdateQueue = new List<FontAsset>();
        static readonly HashSet<int> s_FontFeaturesUpdateQueueLookup = new HashSet<int>();
        static readonly List<FontAsset> s_KerningUpdateQueue = new List<FontAsset>();
        static readonly HashSet<int> s_KerningUpdateQueueLookup = new HashSet<int>();
        static readonly List<Texture2D> s_AtlasTexturesUpdateQueue = new List<Texture2D>();
        static readonly HashSet<int> s_AtlasTexturesUpdateQueueLookup = new HashSet<int>();
        static readonly List<WeakReference<FontAsset>> s_CallbackInstances = new List<WeakReference<FontAsset>>();

        /// <summary>The control and format characters every font asset answers for, with zero-size
        /// glyphs unless the face has them (end of text and tab first: they are added at once).</summary>
        static readonly uint[] s_SynthesizedCharacters = { 3, 9, 10, 11, 13, 0x061C, 0x200B, 0x200E, 0x200F, 0x2028, 0x2029, 0x2060 };

        public FontAsset() { }

        // ---- Properties ----

        internal Font SourceFont_EditorRef
        {
            get
            {
                if (m_SourceFontFile_EditorRef == null) m_SourceFontFile_EditorRef = GetSourceFontRef?.Invoke(m_SourceFontFileGUID);
                return m_SourceFontFile_EditorRef;
            }
            set
            {
                m_SourceFontFile_EditorRef = value;
                m_SourceFontFileGUID = SetSourceFontGUID?.Invoke(value);
                m_SourceFontFile = m_AtlasPopulationMode == AtlasPopulationMode.Dynamic ? value : null;
            }
        }

        public FontAssetCreationEditorSettings fontAssetCreationEditorSettings
        {
            get => m_fontAssetCreationEditorSettings;
            set => m_fontAssetCreationEditorSettings = value;
        }

        public Font sourceFontFile
        {
            get => m_SourceFontFile;
            internal set => m_SourceFontFile = value;
        }

        public AtlasPopulationMode atlasPopulationMode
        {
            get => m_AtlasPopulationMode;
            set
            {
                m_AtlasPopulationMode = value;
                if (value == AtlasPopulationMode.Static || value == AtlasPopulationMode.DynamicOS) m_SourceFontFile = null;
                else m_SourceFontFile = m_SourceFontFile_EditorRef;
            }
        }

        public FaceInfo faceInfo
        {
            get => m_FaceInfo;
            set => m_FaceInfo = value;
        }

        internal int familyNameHashCode
        {
            get
            {
                if (m_FamilyNameHashCode == 0) m_FamilyNameHashCode = TextUtilities.GetHashCodeCaseInSensitive(m_FaceInfo.familyName);
                return m_FamilyNameHashCode;
            }
            set => m_FamilyNameHashCode = value;
        }

        internal int styleNameHashCode
        {
            get
            {
                if (m_StyleNameHashCode == 0) m_StyleNameHashCode = TextUtilities.GetHashCodeCaseInSensitive(m_FaceInfo.styleName);
                return m_StyleNameHashCode;
            }
            set => m_StyleNameHashCode = value;
        }

        public List<Glyph> glyphTable
        {
            get => m_GlyphTable;
            internal set => m_GlyphTable = value;
        }

        public Dictionary<uint, Glyph> glyphLookupTable
        {
            get
            {
                if (m_GlyphLookupDictionary == null) ReadFontAssetDefinition();
                return m_GlyphLookupDictionary;
            }
        }

        public List<Character> characterTable
        {
            get => m_CharacterTable;
            internal set => m_CharacterTable = value;
        }

        public Dictionary<uint, Character> characterLookupTable
        {
            get
            {
                if (m_CharacterLookupDictionary == null) ReadFontAssetDefinition();
                return m_CharacterLookupDictionary;
            }
        }

        public Texture2D atlasTexture
        {
            get
            {
                if (m_AtlasTexture == null && m_AtlasTextures != null && m_AtlasTextures.Length > 0) m_AtlasTexture = m_AtlasTextures[0];
                return m_AtlasTexture;
            }
        }

        public Texture2D[] atlasTextures
        {
            get => m_AtlasTextures;
            set => m_AtlasTextures = value;
        }

        public int atlasTextureCount => m_AtlasTextureIndex + 1;

        public bool isMultiAtlasTexturesEnabled
        {
            get => m_IsMultiAtlasTexturesEnabled;
            set => m_IsMultiAtlasTexturesEnabled = value;
        }

        public bool getFontFeatures
        {
            get => m_GetFontFeatures;
            set => m_GetFontFeatures = value;
        }

        internal bool clearDynamicDataOnBuild
        {
            get => m_ClearDynamicDataOnBuild;
            set => m_ClearDynamicDataOnBuild = value;
        }

        public int atlasWidth
        {
            get => m_AtlasWidth;
            internal set => m_AtlasWidth = value;
        }

        public int atlasHeight
        {
            get => m_AtlasHeight;
            internal set => m_AtlasHeight = value;
        }

        public int atlasPadding
        {
            get => m_AtlasPadding;
            internal set => m_AtlasPadding = value;
        }

        public GlyphRenderMode atlasRenderMode
        {
            get => m_AtlasRenderMode;
            internal set => m_AtlasRenderMode = value;
        }

        internal List<GlyphRect> usedGlyphRects
        {
            get => m_UsedGlyphRects;
            set => m_UsedGlyphRects = value;
        }

        internal List<GlyphRect> freeGlyphRects
        {
            get => m_FreeGlyphRects;
            set => m_FreeGlyphRects = value;
        }

        public FontFeatureTable fontFeatureTable
        {
            get => m_FontFeatureTable;
            internal set => m_FontFeatureTable = value;
        }

        public List<FontAsset> fallbackFontAssetTable
        {
            get => m_FallbackFontAssetTable;
            set => m_FallbackFontAssetTable = value;
        }

        public FontWeightPair[] fontWeightTable
        {
            get => m_FontWeightTable;
            internal set => m_FontWeightTable = value;
        }

        public float regularStyleWeight { get => m_RegularStyleWeight; set => m_RegularStyleWeight = value; }
        public float regularStyleSpacing { get => m_RegularStyleSpacing; set => m_RegularStyleSpacing = value; }
        public float boldStyleWeight { get => m_BoldStyleWeight; set => m_BoldStyleWeight = value; }
        public float boldStyleSpacing { get => m_BoldStyleSpacing; set => m_BoldStyleSpacing = value; }
        public byte italicStyleSlant { get => m_ItalicStyleSlant; set => m_ItalicStyleSlant = value; }
        public byte tabMultiple { get => m_TabMultiple; set => m_TabMultiple = value; }

        /// <summary>A bitmap (coverage, not distance field) atlas of a face without color.</summary>
        internal bool IsBitmap() =>
            (m_AtlasRenderMode & (GlyphRenderMode)GlyphRasterModeBitmap) != 0 && (m_AtlasRenderMode & (GlyphRenderMode)GlyphRasterModeColor) == 0;

        const int GlyphRasterModeBitmap = 16;
        const int GlyphRasterModeColor = 65536;

        static bool IsColorMode(GlyphRenderMode mode) => ((int)mode & GlyphRasterModeColor) == GlyphRasterModeColor;

        static TextureFormat AtlasFormat(GlyphRenderMode mode) => IsColorMode(mode) ? TextureFormat.RGBA32 : TextureFormat.Alpha8;

        /// <summary>SDF atlases keep a one-pixel border free on the top and right.</summary>
        static int AtlasBorder(GlyphRenderMode mode) => ((int)mode & GlyphRasterModeBitmap) == GlyphRasterModeBitmap ? 0 : 1;

        // ---- Creation ----

        /// <summary>A dynamic font asset of an installed OS font.</summary>
        public static FontAsset CreateFontAsset(string familyName, string styleName, int pointSize = 90)
        {
            var fontAsset = CreateFontAssetInternal(familyName, styleName, pointSize);
            if (fontAsset == null)
                Debug.Log("Unable to find a font file with the specified Family Name [" + familyName + "] and Style [" + styleName + "].");
            return fontAsset;
        }

        internal static FontAsset CreateFontAssetInternal(string familyName, string styleName, int pointSize = 90) =>
            CreateFontAsset(familyName, styleName, pointSize, 9, GlyphRenderMode.DEFAULT);

        internal static FontAsset CreateFontAsset(string familyName, string styleName, int pointSize, int padding, GlyphRenderMode renderMode) =>
            FontEngine.TryGetSystemFontReference(familyName, styleName, out var reference)
                ? CreateFontAsset(reference.filePath, reference.faceIndex, pointSize, padding, renderMode, 1024, 1024, AtlasPopulationMode.DynamicOS)
                : null;

        internal static List<FontAsset> CreateFontAssetOSFallbackList(string[] fallbacksFamilyNames, int pointSize = 90)
        {
            var list = new List<FontAsset>();
            foreach (var family in fallbacksFamilyNames ?? Array.Empty<string>())
            {
                var fontAsset = CreateFontAssetFromFamilyName(family, pointSize);
                if (fontAsset != null) list.Add(fontAsset);
            }
            return list;
        }

        internal static FontAsset CreateFontAssetWithOSFallbackList(string[] fallbacksFamilyNames, int pointSize = 90)
        {
            FontAsset first = null;
            foreach (var family in fallbacksFamilyNames ?? Array.Empty<string>())
            {
                var fontAsset = CreateFontAssetFromFamilyName(family, pointSize);
                if (fontAsset == null) continue;
                first ??= fontAsset;
                first.fallbackFontAssetTable ??= new List<FontAsset>();
                first.fallbackFontAssetTable.Add(fontAsset);
            }
            return first;
        }

        static FontAsset CreateFontAssetFromFamilyName(string familyName, int pointSize = 90)
        {
            if (!FontEngine.TryGetSystemFontReference(familyName, null, out var reference)) return null;
            var fontAsset = CreateFontAsset(reference.filePath, reference.faceIndex, pointSize, 9, GlyphRenderMode.DEFAULT, 1024, 1024, AtlasPopulationMode.DynamicOS);
            if (fontAsset == null) return null;
            FontAssetFactory.SetHideFlags(fontAsset);
            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.InternalDynamicOS = true;
            return fontAsset;
        }

        public static FontAsset CreateFontAsset(string fontFilePath, int faceIndex, int samplingPointSize, int atlasPadding, GlyphRenderMode renderMode, int atlasWidth, int atlasHeight) =>
            CreateFontAsset(fontFilePath, faceIndex, samplingPointSize, atlasPadding, renderMode, atlasWidth, atlasHeight, AtlasPopulationMode.Dynamic);

        static FontAsset CreateFontAsset(string fontFilePath, int faceIndex, int samplingPointSize, int atlasPadding, GlyphRenderMode renderMode, int atlasWidth, int atlasHeight, AtlasPopulationMode atlasPopulationMode, bool enableMultiAtlasSupport = true)
        {
            if (FontEngine.LoadFontFace(fontFilePath, samplingPointSize, faceIndex) != FontEngineError.Success)
            {
                Debug.Log("Unable to load font face from [" + fontFilePath + "].");
                return null;
            }
            var fontAsset = CreateFontAssetInstance(null, atlasPadding, renderMode, atlasWidth, atlasHeight, atlasPopulationMode, enableMultiAtlasSupport);
            if (fontAsset != null) fontAsset.m_SourceFontFilePath = fontFilePath;
            return fontAsset;
        }

        /// <summary>A dynamic SDF font asset of a Font at Unity's defaults: 90 points, padding 9, a
        /// 1024 x 1024 atlas.</summary>
        public static FontAsset CreateFontAsset(Font font) => CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);

        public static FontAsset CreateFontAsset(Font font, int samplingPointSize, int atlasPadding, GlyphRenderMode renderMode, int atlasWidth, int atlasHeight, AtlasPopulationMode atlasPopulationMode = AtlasPopulationMode.Dynamic, bool enableMultiAtlasSupport = true) =>
            CreateFontAsset(font, 0, samplingPointSize, atlasPadding, renderMode, atlasWidth, atlasHeight, atlasPopulationMode, enableMultiAtlasSupport);

        static FontAsset CreateFontAsset(Font font, int faceIndex, int samplingPointSize, int atlasPadding, GlyphRenderMode renderMode, int atlasWidth, int atlasHeight, AtlasPopulationMode atlasPopulationMode, bool enableMultiAtlasSupport)
        {
            if (font == null) return null;
            if (font.name == "LegacyRuntime")
            {
                // The built-in font falls back to the OS fonts Unity lists for it.
                var fallbacks = Font.GetOSFallbacks();
                if (FontEngine.LoadFontFace(font, samplingPointSize, faceIndex) == FontEngineError.Success)
                {
                    var legacy = CreateFontAssetInstance(font, atlasPadding, renderMode, atlasWidth, atlasHeight, atlasPopulationMode, enableMultiAtlasSupport);
                    if (legacy != null) legacy.fallbackFontAssetTable = CreateFontAssetOSFallbackList(fallbacks, samplingPointSize);
                    return legacy;
                }
                var os = CreateFontAssetWithOSFallbackList(fallbacks, samplingPointSize);
                if (os != null) return os;
            }
            if (FontEngine.LoadFontFace(font, samplingPointSize, faceIndex) != FontEngineError.Success)
            {
                var byName = CreateFontAsset(font.name, "Regular");
                if (byName != null) return byName;
                Debug.LogWarning("Unable to load font face for [" + font.name + "]. Make sure \"Include Font Data\" is enabled in the Font Import Settings.", font);
                return null;
            }
            return CreateFontAssetInstance(font, atlasPadding, renderMode, atlasWidth, atlasHeight, atlasPopulationMode, enableMultiAtlasSupport);
        }

        /// <summary>A font asset of the face the font engine has loaded: an empty atlas texture
        /// (1 x 1 until the first glyph), its material and a free rect of the whole atlas.</summary>
        static FontAsset CreateFontAssetInstance(Font font, int atlasPadding, GlyphRenderMode renderMode, int atlasWidth, int atlasHeight, AtlasPopulationMode atlasPopulationMode, bool enableMultiAtlasSupport)
        {
            var fontAsset = ScriptableObject.CreateInstance<FontAsset>();
            fontAsset.m_Version = "1.1.0";
            fontAsset.faceInfo = FontEngine.GetFaceInfo();
            if (renderMode == GlyphRenderMode.DEFAULT) renderMode = FontEngine.IsColorFontFace() ? GlyphRenderMode.COLOR : GlyphRenderMode.SDFAA;
            if (atlasPopulationMode == AtlasPopulationMode.Dynamic && font != null)
            {
                fontAsset.sourceFontFile = font;
                fontAsset.m_SourceFontFileGUID = SetSourceFontGUID?.Invoke(font);
                fontAsset.m_SourceFontFile_EditorRef = font;
            }
            fontAsset.atlasPopulationMode = atlasPopulationMode;
            fontAsset.atlasWidth = atlasWidth;
            fontAsset.atlasHeight = atlasHeight;
            fontAsset.atlasPadding = atlasPadding;
            fontAsset.atlasRenderMode = renderMode;
            var format = AtlasFormat(renderMode);
            var texture = new Texture2D(1, 1, format, false);
            fontAsset.atlasTextures = new[] { texture };
            fontAsset.isMultiAtlasTexturesEnabled = enableMultiAtlasSupport;
            int border = AtlasBorder(renderMode);
            Material material;
            if (border == 0)
            {
                var shader = format == TextureFormat.Alpha8 ? TextShaderUtilities.ShaderRef_MobileBitmap : TextShaderUtilities.ShaderRef_Sprite;
                if (shader == null) return null;
                material = new Material(shader);
            }
            else
            {
                material = new Material(TextShaderUtilities.ShaderRef_MobileSDF);
                material.SetFloat(TextShaderUtilities.ID_GradientScale, atlasPadding + k_SdfGradientExtra);
                material.SetFloat(TextShaderUtilities.ID_WeightNormal, fontAsset.regularStyleWeight);
                material.SetFloat(TextShaderUtilities.ID_WeightBold, fontAsset.boldStyleWeight);
            }
            material.SetTexture(TextShaderUtilities.ID_MainTex, texture);
            material.SetFloat(TextShaderUtilities.ID_TextureWidth, atlasWidth);
            material.SetFloat(TextShaderUtilities.ID_TextureHeight, atlasHeight);
            fontAsset.material = material;
            fontAsset.freeGlyphRects = new List<GlyphRect>(8) { new GlyphRect(0, 0, atlasWidth - border, atlasHeight - border) };
            fontAsset.usedGlyphRects = new List<GlyphRect>(8);
            string name = fontAsset.faceInfo.familyName + " - " + fontAsset.faceInfo.styleName;
            material.name = name + " Material";
            texture.name = name + " Atlas";
            fontAsset.ReadFontAssetDefinition();
            return fontAsset;
        }

        internal static FontAsset GetFontAssetByID(int id) => s_FontAssetByInstanceId[id];

        void RegisterCallbackInstance()
        {
            foreach (var reference in s_CallbackInstances)
                if (reference.TryGetTarget(out var target) && target == this) return;
            for (int i = 0; i < s_CallbackInstances.Count; i++)
            {
                if (s_CallbackInstances[i].TryGetTarget(out _)) continue;
                s_CallbackInstances[i] = new WeakReference<FontAsset>(this);
                return;
            }
            s_CallbackInstances.Add(new WeakReference<FontAsset>(this));
        }

        void OnDestroy()
        {
            s_FontAssetByInstanceId.Remove(instanceID);
            TextResourceManager.RemoveFontAsset(this);
            if (m_IsClone) return;
            DestroyAtlasTextures();
            if (m_Material != null) Object.DestroyImmediate(m_Material);
            m_Material = null;
        }

        // ---- Lookup tables ----

        /// <summary>Builds the lookups of the serialized tables, adds the synthesized characters
        /// and fills face metrics a font asset may lack.</summary>
        public void ReadFontAssetDefinition()
        {
            InitializeDictionaryLookupTables();
            AddSynthesizedCharactersAndFaceMetrics();
            if (m_FaceInfo.capLine == 0f && m_CharacterLookupDictionary.TryGetValue('X', out var x))
                m_FaceInfo.capLine = m_GlyphLookupDictionary[x.glyphIndex].metrics.horizontalBearingY;
            if (m_FaceInfo.meanLine == 0f && m_CharacterLookupDictionary.TryGetValue('X', out x))
                m_FaceInfo.meanLine = m_GlyphLookupDictionary[x.glyphIndex].metrics.horizontalBearingY;
            if (m_FaceInfo.scale == 0f) m_FaceInfo.scale = 1f;
            if (m_FaceInfo.strikethroughOffset == 0f) m_FaceInfo.strikethroughOffset = m_FaceInfo.capLine / 2.5f;
            if (m_AtlasPadding == 0 && material != null && material.HasProperty(TextShaderUtilities.ID_GradientScale))
                m_AtlasPadding = (int)material.GetFloat(TextShaderUtilities.ID_GradientScale) - k_SdfGradientExtra;
            if (m_FaceInfo.unitsPerEM == 0 && atlasPopulationMode != AtlasPopulationMode.Static)
                m_FaceInfo.unitsPerEM = FontEngine.GetFaceInfo().unitsPerEM;
            hashCode = TextUtilities.GetHashCodeCaseInSensitive(name);
            familyNameHashCode = TextUtilities.GetHashCodeCaseInSensitive(m_FaceInfo.familyName);
            styleNameHashCode = TextUtilities.GetHashCodeCaseInSensitive(m_FaceInfo.styleName);
            materialHashCode = TextUtilities.GetHashCodeCaseInSensitive(name + " Atlas Material");
            TextResourceManager.AddFontAsset(this);
            IsFontAssetLookupTablesDirty = false;
            RegisterCallbackInstance();
        }

        internal void InitializeDictionaryLookupTables()
        {
            InitializeGlyphLookupDictionary();
            InitializeCharacterLookupDictionary();
            if ((m_AtlasPopulationMode == AtlasPopulationMode.Dynamic || m_AtlasPopulationMode == AtlasPopulationMode.DynamicOS) && m_ShouldReimportFontFeatures)
                ImportFontFeatures();
            InitializeGlyphPairAdjustmentRecordsLookupDictionary();
        }

        internal void InitializeGlyphLookupDictionary()
        {
            m_GlyphTable ??= new List<Glyph>();
            m_GlyphLookupDictionary = new Dictionary<uint, Glyph>(m_GlyphTable.Count);
            m_GlyphIndexList = new List<uint>(m_GlyphTable.Count);
            m_GlyphIndexListNewlyAdded = new List<uint>();
            foreach (var glyph in m_GlyphTable)
                if (m_GlyphLookupDictionary.TryAdd(glyph.index, glyph)) m_GlyphIndexList.Add(glyph.index);
        }

        internal void InitializeCharacterLookupDictionary()
        {
            m_CharacterTable ??= new List<Character>();
            m_CharacterLookupDictionary = new Dictionary<uint, Character>(m_CharacterTable.Count);
            foreach (var character in m_CharacterTable)
            {
                if (!m_CharacterLookupDictionary.TryAdd(character.unicode, character)) continue;
                character.textAsset = this;
                character.glyph = m_GlyphLookupDictionary[character.glyphIndex];
            }
            m_MissingUnicodesFromFontFile?.Clear();
        }

        /// <summary>Forgets the characters other font assets were found to have for this one.</summary>
        internal void ClearFallbackCharacterTable()
        {
            var foreign = new List<uint>();
            foreach (var pair in m_CharacterLookupDictionary)
                if (pair.Value.textAsset != this) foreign.Add(pair.Key);
            foreach (var key in foreign) m_CharacterLookupDictionary.Remove(key);
        }

        internal void InitializeGlyphPairAdjustmentRecordsLookupDictionary()
        {
            m_FontFeatureTable ??= new FontFeatureTable();
            var records = m_FontFeatureTable.glyphPairAdjustmentRecords;
            m_FontFeatureTable.m_GlyphPairAdjustmentRecordLookup = new Dictionary<uint, GlyphPairAdjustmentRecord>(records.Count);
            foreach (var record in records)
                m_FontFeatureTable.m_GlyphPairAdjustmentRecordLookup.TryAdd(FontFeatureTable.PairKey(record.firstAdjustmentRecord.glyphIndex, record.secondAdjustmentRecord.glyphIndex), record);
        }

        internal void AddSynthesizedCharactersAndFaceMetrics()
        {
            bool faceLoaded = false;
            if (m_AtlasPopulationMode == AtlasPopulationMode.Dynamic || m_AtlasPopulationMode == AtlasPopulationMode.DynamicOS)
            {
                faceLoaded = LoadFontFace() == FontEngineError.Success;
                if (!faceLoaded && !InternalDynamicOS) Debug.LogWarning("Unable to load font face for [" + name + "] font asset.", this);
            }
            for (int i = 0; i < s_SynthesizedCharacters.Length; i++)
                AddSynthesizedCharacter(s_SynthesizedCharacters[i], faceLoaded, addImmediately: i < 2);
        }

        /// <summary>A control character the face has is added (when <paramref name="addImmediately"/>)
        /// with its own metrics and no atlas space; one it lacks gets an empty glyph. Either answers
        /// for every weight, upright and italic.</summary>
        void AddSynthesizedCharacter(uint unicode, bool isFontFaceLoaded, bool addImmediately)
        {
            if (m_CharacterLookupDictionary.ContainsKey(unicode)) return;
            Glyph glyph;
            if (isFontFaceLoaded && FontEngine.GetGlyphIndex(unicode) != 0)
            {
                if (!addImmediately) return;
                var flags = (m_AtlasRenderMode & (GlyphRenderMode)4) == (GlyphRenderMode)4
                    ? GlyphLoadFlags.LOAD_NO_HINTING | GlyphLoadFlags.LOAD_NO_BITMAP
                    : GlyphLoadFlags.LOAD_NO_BITMAP;
                if (!FontEngine.TryGetGlyphWithUnicodeValue(unicode, flags, out glyph)) return;
            }
            else
            {
                glyph = new Glyph(0, new GlyphMetrics(0f, 0f, 0f, 0f, 0f), GlyphRect.zero, 1f, 0);
            }
            var character = new Character(unicode, this, glyph);
            foreach (TextFontWeight weight in Enum.GetValues(typeof(TextFontWeight)))
            {
                m_CharacterLookupDictionary.Add(CompositeKey(unicode, FontStyles.Normal, weight), character);
                m_CharacterLookupDictionary.Add(CompositeKey(unicode, FontStyles.Italic, weight), character);
            }
        }

        internal void AddCharacterToLookupCache(uint unicode, Character character) =>
            AddCharacterToLookupCache(unicode, character, FontStyles.Normal, TextFontWeight.Regular);

        internal void AddCharacterToLookupCache(uint unicode, Character character, FontStyles fontStyle, TextFontWeight fontWeight)
        {
            if (m_CharacterLookupDictionary == null) ReadFontAssetDefinition();
            m_CharacterLookupDictionary.TryAdd(CompositeKey(unicode, fontStyle, fontWeight), character);
        }

        internal bool GetCharacterInLookupCache(uint unicode, FontStyles fontStyle, TextFontWeight fontWeight, out Character character)
        {
            if (m_CharacterLookupDictionary == null) ReadFontAssetDefinition();
            return m_CharacterLookupDictionary.TryGetValue(CompositeKey(unicode, fontStyle, fontWeight), out character);
        }

        internal void RemoveCharacterInLookupCache(uint unicode, FontStyles fontStyle, TextFontWeight fontWeight)
        {
            if (m_CharacterLookupDictionary == null) ReadFontAssetDefinition();
            m_CharacterLookupDictionary.Remove(CompositeKey(unicode, fontStyle, fontWeight));
        }

        internal bool ContainsCharacterInLookupCache(uint unicode, FontStyles fontStyle, TextFontWeight fontWeight)
        {
            if (m_CharacterLookupDictionary == null) ReadFontAssetDefinition();
            return m_CharacterLookupDictionary.ContainsKey(CompositeKey(unicode, fontStyle, fontWeight));
        }

        /// <summary>The lookup key of a character in a style: the code point (21 bits), the weight's
        /// index above it and an italic bit; the plain code point for regular upright text.</summary>
        static uint CompositeKey(uint unicode, FontStyles fontStyle, TextFontWeight fontWeight)
        {
            if (fontStyle == FontStyles.Normal && fontWeight == TextFontWeight.Regular) return unicode;
            bool italic = (fontStyle & FontStyles.Italic) == FontStyles.Italic;
            int weight = fontWeight != TextFontWeight.Regular ? TextUtilities.GetTextFontWeightIndex(fontWeight) : 0;
            return (unicode & 0x1FFFFF) | (uint)((weight & 0xF) << 21) | (italic ? 0x2000000u : 0u);
        }

        /// <summary>Loads the face this font asset renders from into the font engine: the source
        /// font (or its file path) of a dynamic asset, else the OS font of its family and style.</summary>
        internal FontEngineError LoadFontFace()
        {
            if (m_AtlasPopulationMode == AtlasPopulationMode.Dynamic)
            {
                if (m_SourceFontFile == null) m_SourceFontFile = SourceFont_EditorRef;
                if (m_SourceFontFile != null && FontEngine.LoadFontFace(m_SourceFontFile, m_FaceInfo.pointSize, m_FaceInfo.faceIndex) == FontEngineError.Success)
                    return FontEngineError.Success;
                if (!string.IsNullOrEmpty(m_SourceFontFilePath))
                    return FontEngine.LoadFontFace(m_SourceFontFilePath, m_FaceInfo.pointSize, m_FaceInfo.faceIndex);
                return FontEngineError.Invalid_Face;
            }
            if (SourceFont_EditorRef != null && FontEngine.LoadFontFace(m_SourceFontFile_EditorRef, m_FaceInfo.pointSize, m_FaceInfo.faceIndex) == FontEngineError.Success)
                return FontEngineError.Success;
            if (!string.IsNullOrEmpty(m_SourceFontFilePath) && FontEngine.LoadFontFace(m_SourceFontFilePath, m_FaceInfo.pointSize, m_FaceInfo.faceIndex) == FontEngineError.Success)
                return FontEngineError.Success;
            return FontEngine.LoadFontFace(m_FaceInfo.familyName, m_FaceInfo.styleName, m_FaceInfo.pointSize);
        }

        internal void SortCharacterTable()
        {
            if (m_CharacterTable != null && m_CharacterTable.Count > 0) m_CharacterTable = m_CharacterTable.OrderBy(c => c.unicode).ToList();
        }

        internal void SortGlyphTable()
        {
            if (m_GlyphTable != null && m_GlyphTable.Count > 0) m_GlyphTable = m_GlyphTable.OrderBy(g => g.index).ToList();
        }

        internal void SortFontFeatureTable() => m_FontFeatureTable.SortGlyphPairAdjustmentRecords();

        internal void SortAllTables()
        {
            SortGlyphTable();
            SortCharacterTable();
            SortFontFeatureTable();
        }

        // ---- Characters ----

        public bool HasCharacter(int character) =>
            characterLookupTable != null && m_CharacterLookupDictionary.ContainsKey((uint)character);

        public bool HasCharacter(char character, bool searchFallbacks = false, bool tryAddCharacter = false) =>
            HasCharacter((uint)character, searchFallbacks, tryAddCharacter);

        public bool HasCharacter(uint character, bool searchFallbacks = false, bool tryAddCharacter = false)
        {
            if (characterLookupTable == null) return false;
            if (m_CharacterLookupDictionary.ContainsKey(character)) return true;
            if (tryAddCharacter && IsDynamic && TryAddCharacterInternal(character, FontStyles.Normal, TextFontWeight.Regular, out _)) return true;
            if (!searchFallbacks) return false;
            BeginFallbackSearch();
            return SearchFallbacks(character, FontStyles.Normal, TextFontWeight.Regular, tryAddCharacter);
        }

        bool IsDynamic => m_AtlasPopulationMode == AtlasPopulationMode.Dynamic || m_AtlasPopulationMode == AtlasPopulationMode.DynamicOS;

        void BeginFallbackSearch()
        {
            if (s_SearchedFontAssets == null) s_SearchedFontAssets = new HashSet<int>();
            else s_SearchedFontAssets.Clear();
            s_SearchedFontAssets.Add(GetInstanceID());
        }

        bool SearchFallbacks(uint character, FontStyles fontStyle, TextFontWeight fontWeight, bool tryAddCharacter)
        {
            if (fallbackFontAssetTable == null) return false;
            for (int i = 0; i < fallbackFontAssetTable.Count && fallbackFontAssetTable[i] != null; i++)
            {
                var fallback = fallbackFontAssetTable[i];
                if (s_SearchedFontAssets.Add(fallback.GetInstanceID()) && fallback.HasCharacter_Internal(character, fontStyle, fontWeight, true, tryAddCharacter))
                    return true;
            }
            return false;
        }

        bool HasCharacter_Internal(uint character, FontStyles fontStyle, TextFontWeight fontWeight, bool searchFallbacks, bool tryAddCharacter)
        {
            if (m_CharacterLookupDictionary == null)
            {
                ReadFontAssetDefinition();
                if (m_CharacterLookupDictionary == null) return false;
            }
            if (ContainsCharacterInLookupCache(character, fontStyle, fontWeight)) return true;
            if (tryAddCharacter && IsDynamic && TryAddCharacterInternal(character, fontStyle, fontWeight, out _)) return true;
            return searchFallbacks && SearchFallbacks(character, fontStyle, fontWeight, tryAddCharacter);
        }

        public bool HasCharacters(string text, out List<char> missingCharacters)
        {
            missingCharacters = null;
            if (characterLookupTable == null) return false;
            missingCharacters = new List<char>();
            for (int i = 0; i < text.Length; i++)
            {
                uint c = FontAssetUtilities.GetCodePoint(text, ref i);
                if (!m_CharacterLookupDictionary.ContainsKey(c)) missingCharacters.Add((char)c);
            }
            return missingCharacters.Count == 0;
        }

        public bool HasCharacters(string text, out uint[] missingCharacters, bool searchFallbacks = false, bool tryAddCharacter = false)
        {
            missingCharacters = null;
            if (characterLookupTable == null) return false;
            s_MissingCharacterList.Clear();
            for (int i = 0; i < text.Length; i++)
            {
                uint c = FontAssetUtilities.GetCodePoint(text, ref i);
                if (m_CharacterLookupDictionary.ContainsKey(c)) continue;
                if (tryAddCharacter && IsDynamic && TryAddCharacterInternal(c, FontStyles.Normal, TextFontWeight.Regular, out _)) continue;
                bool found = false;
                if (searchFallbacks)
                {
                    BeginFallbackSearch();
                    found = SearchFallbacks(c, FontStyles.Normal, TextFontWeight.Regular, tryAddCharacter);
                }
                if (!found) s_MissingCharacterList.Add(c);
            }
            if (s_MissingCharacterList.Count == 0) return true;
            missingCharacters = s_MissingCharacterList.ToArray();
            return false;
        }

        public bool HasCharacters(string text)
        {
            if (characterLookupTable == null) return false;
            for (int i = 0; i < text.Length; i++)
                if (!m_CharacterLookupDictionary.ContainsKey(FontAssetUtilities.GetCodePoint(text, ref i))) return false;
            return true;
        }

        public static string GetCharacters(FontAsset fontAsset)
        {
            var text = new StringBuilder(fontAsset.characterTable.Count);
            foreach (var character in fontAsset.characterTable) text.Append((char)character.unicode);
            return text.ToString();
        }

        public static int[] GetCharactersArray(FontAsset fontAsset)
        {
            var codes = new int[fontAsset.characterTable.Count];
            for (int i = 0; i < codes.Length; i++) codes[i] = (int)fontAsset.characterTable[i].unicode;
            return codes;
        }

        internal uint GetGlyphIndex(uint unicode) => GetGlyphIndex(unicode, out _);

        internal uint GetGlyphIndex(uint unicode, out bool success)
        {
            success = true;
            if (characterLookupTable.TryGetValue(unicode, out var character)) return character.glyphIndex;
            if (TextGenerator.IsExecutingJob)
            {
                success = false;
                return 0;
            }
            return LoadFontFace() == FontEngineError.Success ? FontEngine.GetGlyphIndex(unicode) : 0;
        }

        internal uint GetGlyphVariantIndex(uint unicode, uint variantSelectorUnicode) =>
            LoadFontFace() == FontEngineError.Success ? FontEngine.GetVariantGlyphIndex(unicode, variantSelectorUnicode) : 0;

        /// <summary>Rebuilds the atlas with the characters it has.</summary>
        internal void UpdateFontAssetData()
        {
            var unicodes = new uint[m_CharacterTable.Count];
            for (int i = 0; i < unicodes.Length; i++) unicodes[i] = m_CharacterTable[i].unicode;
            ClearCharacterAndGlyphTables();
            ClearFontFeaturesTables();
            ClearAtlasTextures(true);
            ReadFontAssetDefinition();
            if (unicodes.Length > 0) TryAddCharacters(unicodes, m_GetFontFeatures);
        }

        public void ClearFontAssetData(bool setAtlasSizeToZero = false)
        {
            ClearCharacterAndGlyphTables();
            ClearFontFeaturesTables();
            ClearAtlasTextures(setAtlasSizeToZero);
            ReadFontAssetDefinition();
            foreach (var reference in s_CallbackInstances)
                if (reference.TryGetTarget(out var target) && target != this) target.ClearFallbackCharacterTable();
            RegisterResourceForUpdate?.Invoke(this);
        }

        internal void ClearCharacterAndGlyphTablesInternal()
        {
            ClearCharacterAndGlyphTables();
            ClearAtlasTextures(true);
            ReadFontAssetDefinition();
            RegisterResourceForUpdate?.Invoke(this);
        }

        void ClearCharacterAndGlyphTables()
        {
            m_GlyphTable?.Clear();
            m_CharacterTable?.Clear();
            m_UsedGlyphRects?.Clear();
            if (m_FreeGlyphRects != null)
            {
                int border = AtlasBorder(m_AtlasRenderMode);
                m_FreeGlyphRects.Clear();
                m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - border, m_AtlasHeight - border));
            }
        }

        void ClearFontFeaturesTables() => m_FontFeatureTable?.glyphPairAdjustmentRecords.Clear();

        internal void ClearAtlasTextures(bool setAtlasSizeToZero = false)
        {
            m_AtlasTextureIndex = 0;
            if (m_AtlasTextures == null) return;
            for (int i = 1; i < m_AtlasTextures.Length; i++)
            {
                if (m_AtlasTextures[i] == null) continue;
                Object.DestroyImmediate(m_AtlasTextures[i], true);
                RegisterResourceForReimport?.Invoke(this);
            }
            Array.Resize(ref m_AtlasTextures, 1);
            var texture = m_AtlasTexture = m_AtlasTextures[0];
            if (texture == null) return;
            if (!texture.isReadable) SetAtlasTextureIsReadable?.Invoke(texture, true);
            var format = AtlasFormat(m_AtlasRenderMode);
            if (setAtlasSizeToZero) texture.Reinitialize(1, 1, format, false);
            else if (texture.width != m_AtlasWidth || texture.height != m_AtlasHeight) texture.Reinitialize(m_AtlasWidth, m_AtlasHeight, format, false);
            FontEngine.ResetAtlasTexture(texture);
            texture.Apply();
        }

        void DestroyAtlasTextures()
        {
            m_AtlasTexture = null;
            m_AtlasTextureIndex = -1;
            if (m_AtlasTextures == null) return;
            foreach (var texture in m_AtlasTextures)
                if (texture != null) Object.DestroyImmediate(texture, true);
            m_AtlasTextures = null;
        }

        // ---- Update queues ----

        internal static void RegisterFontAssetForFontFeatureUpdate(FontAsset fontAsset)
        {
            if (s_FontFeaturesUpdateQueueLookup.Add(fontAsset.instanceID)) s_FontFeaturesUpdateQueue.Add(fontAsset);
        }

        internal static void RegisterFontAssetForKerningUpdate(FontAsset fontAsset)
        {
            if (s_KerningUpdateQueueLookup.Add(fontAsset.instanceID)) s_KerningUpdateQueue.Add(fontAsset);
        }

        internal static void UpdateFontFeaturesForFontAssetsInQueue()
        {
            if (s_FontFeaturesUpdateQueue.Count > 0)
            {
                foreach (var fontAsset in s_FontFeaturesUpdateQueue) fontAsset.UpdateGPOSFontFeaturesForNewlyAddedGlyphs();
                s_FontFeaturesUpdateQueue.Clear();
                s_FontFeaturesUpdateQueueLookup.Clear();
            }
            if (s_KerningUpdateQueue.Count > 0)
            {
                foreach (var fontAsset in s_KerningUpdateQueue) fontAsset.UpdateGlyphAdjustmentRecordsForNewGlyphs();
                s_KerningUpdateQueue.Clear();
                s_KerningUpdateQueueLookup.Clear();
            }
        }

        internal static void RegisterAtlasTextureForApply(Texture2D texture)
        {
            if (s_AtlasTexturesUpdateQueueLookup.Add(texture.GetInstanceID())) s_AtlasTexturesUpdateQueue.Add(texture);
        }

        internal static void UpdateAtlasTexturesInQueue()
        {
            if (s_AtlasTexturesUpdateQueue.Count == 0) return;
            foreach (var texture in s_AtlasTexturesUpdateQueue)
                if (texture != null) texture.Apply(false, false);
            s_AtlasTexturesUpdateQueue.Clear();
            s_AtlasTexturesUpdateQueueLookup.Clear();
        }

        /// <summary>Uploads the atlas textures glyphs were added to and reads the kerning of the
        /// glyphs added since the last call (UI Toolkit calls it before generating text each frame).</summary>
        internal static void UpdateFontAssetsInUpdateQueue()
        {
            UpdateAtlasTexturesInQueue();
            UpdateFontFeaturesForFontAssetsInQueue();
        }

        // ---- Adding characters ----

        public bool TryAddCharacters(uint[] unicodes, bool includeFontFeatures = false) => TryAddCharacters(unicodes, out _, includeFontFeatures);

        /// <summary>Adds characters (and their glyphs, rendered into the atlas) in one batch.</summary>
        public bool TryAddCharacters(uint[] unicodes, out uint[] missingUnicodes, bool includeFontFeatures = false)
        {
            if (unicodes == null || unicodes.Length == 0 || m_AtlasPopulationMode == AtlasPopulationMode.Static)
            {
                Debug.LogWarning(m_AtlasPopulationMode == AtlasPopulationMode.Static
                    ? "Unable to add characters to font asset [" + name + "] because its AtlasPopulationMode is set to Static."
                    : "Unable to add characters to font asset [" + name + "] because the provided Unicode list is Null or Empty.", this);
                missingUnicodes = null;
                return false;
            }
            if (LoadFontFace() != FontEngineError.Success)
            {
                missingUnicodes = (uint[])unicodes.Clone();
                return false;
            }
            if (m_CharacterLookupDictionary == null || m_GlyphLookupDictionary == null) ReadFontAssetDefinition();
            m_GlyphsToAdd.Clear();
            m_GlyphsToAddLookup.Clear();
            m_CharactersToAdd.Clear();
            m_CharactersToAddLookup.Clear();
            s_MissingCharacterList.Clear();
            bool anyMissing = false;
            for (int i = 0; i < unicodes.Length; i++)
            {
                uint unicode = FontAssetUtilities.GetCodePoint(unicodes, ref i);
                if (m_CharacterLookupDictionary.ContainsKey(unicode)) continue;
                uint glyphIndex = FaceGlyphIndex(unicode);
                if (glyphIndex == 0)
                {
                    s_MissingCharacterList.Add(unicode);
                    anyMissing = true;
                    continue;
                }
                var character = new Character(unicode, glyphIndex);
                if (m_GlyphLookupDictionary.TryGetValue(glyphIndex, out var existing))
                {
                    character.glyph = existing;
                    character.textAsset = this;
                    m_CharacterTable.Add(character);
                    m_CharacterLookupDictionary.Add(unicode, character);
                    continue;
                }
                if (m_GlyphsToAddLookup.Add(glyphIndex)) m_GlyphsToAdd.Add(glyphIndex);
                if (m_CharactersToAddLookup.Add(unicode)) m_CharactersToAdd.Add(character);
            }
            if (m_GlyphsToAdd.Count == 0)
            {
                missingUnicodes = unicodes;
                return !anyMissing;
            }
            EnsureAtlasAllocated();
            bool allAdded = FontEngine.TryAddGlyphsToTexture(m_GlyphsToAdd, m_AtlasPadding, GlyphPackingMode.BestShortSideFit, m_FreeGlyphRects, m_UsedGlyphRects, m_AtlasRenderMode, m_AtlasTextures[m_AtlasTextureIndex], out var glyphs);
            RecordAddedGlyphs(glyphs);
            AttachPendingCharacters();
            if (m_IsMultiAtlasTexturesEnabled)
                while (!allAdded) allAdded = TryAddGlyphsToNewAtlasTexture();
            if (includeFontFeatures) UpdateFontFeaturesForNewlyAddedGlyphs();
            RegisterResourceForUpdate?.Invoke(this);
            foreach (var character in m_CharactersToAdd) s_MissingCharacterList.Add(character.unicode);
            missingUnicodes = s_MissingCharacterList.Count > 0 ? s_MissingCharacterList.ToArray() : null;
            return allAdded && !anyMissing;
        }

        public bool TryAddCharacters(string characters, bool includeFontFeatures = false) => TryAddCharacters(characters, out _, includeFontFeatures);

        public bool TryAddCharacters(string characters, out string missingCharacters, bool includeFontFeatures = false)
        {
            var unicodes = new uint[characters?.Length ?? 0];
            for (int i = 0; i < unicodes.Length; i++) unicodes[i] = characters[i];
            bool added = TryAddCharacters(unicodes, out var missing, includeFontFeatures);
            if (missing == null || missing.Length == 0)
            {
                missingCharacters = null;
                return added;
            }
            var text = new StringBuilder(missing.Length);
            foreach (var c in missing) text.Append((char)c);
            missingCharacters = text.ToString();
            return added;
        }

        /// <summary>The loaded face's glyph of a character; a no-break space draws the space's glyph
        /// and the soft and non-breaking hyphens the hyphen's when the face lacks them.</summary>
        static uint FaceGlyphIndex(uint unicode)
        {
            uint glyphIndex = FontEngine.GetGlyphIndex(unicode);
            if (glyphIndex != 0) return glyphIndex;
            if (unicode == 0xA0) return FontEngine.GetGlyphIndex(0x20);
            if (unicode == 0xAD || unicode == 0x2011) return FontEngine.GetGlyphIndex(0x2D);
            return 0;
        }

        void EnsureAtlasAllocated()
        {
            var texture = m_AtlasTextures[m_AtlasTextureIndex];
            if (texture.width > 1 && texture.height > 1) return;
            texture.Reinitialize(m_AtlasWidth, m_AtlasHeight);
            FontEngine.ResetAtlasTexture(texture);
        }

        void RecordAddedGlyphs(Glyph[] glyphs)
        {
            for (int i = 0; i < glyphs.Length && glyphs[i] != null; i++)
            {
                var glyph = glyphs[i];
                glyph.atlasIndex = m_AtlasTextureIndex;
                m_GlyphTable.Add(glyph);
                m_GlyphLookupDictionary.Add(glyph.index, glyph);
                m_GlyphIndexListNewlyAdded.Add(glyph.index);
                m_GlyphIndexList.Add(glyph.index);
            }
            m_GlyphsToAdd.Clear();
        }

        /// <summary>Adds the pending characters whose glyphs are now in an atlas; the glyphs of the
        /// others are what is left to add.</summary>
        void AttachPendingCharacters()
        {
            for (int i = 0; i < m_CharactersToAdd.Count; i++)
            {
                var character = m_CharactersToAdd[i];
                if (!m_GlyphLookupDictionary.TryGetValue(character.glyphIndex, out var glyph))
                {
                    m_GlyphsToAdd.Add(character.glyphIndex);
                    continue;
                }
                character.glyph = glyph;
                character.textAsset = this;
                m_CharacterTable.Add(character);
                m_CharacterLookupDictionary.Add(character.unicode, character);
                m_CharactersToAdd.RemoveAt(i--);
            }
        }

        bool TryAddGlyphsToNewAtlasTexture()
        {
            SetupNewAtlasTexture();
            bool allAdded = FontEngine.TryAddGlyphsToTexture(m_GlyphsToAdd, m_AtlasPadding, GlyphPackingMode.BestShortSideFit, m_FreeGlyphRects, m_UsedGlyphRects, m_AtlasRenderMode, m_AtlasTextures[m_AtlasTextureIndex], out var glyphs);
            RecordAddedGlyphs(glyphs);
            AttachPendingCharacters();
            return allAdded;
        }

        internal bool TryAddGlyphVariantIndexInternal(uint unicode, uint nextCharacter, uint variantGlyphIndex) =>
            m_VariantGlyphIndexes.TryAdd((unicode, nextCharacter), variantGlyphIndex);

        internal bool TryGetGlyphVariantIndexInternal(uint unicode, uint nextCharacter, out uint variantGlyphIndex) =>
            m_VariantGlyphIndexes.TryGetValue((unicode, nextCharacter), out variantGlyphIndex);

        internal bool TryAddGlyphInternal(uint glyphIndex, out Glyph glyph)
        {
            if (glyphLookupTable.TryGetValue(glyphIndex, out glyph)) return true;
            if (LoadFontFace() != FontEngineError.Success) return false;
            return TryAddGlyphToAtlas(glyphIndex, out glyph);
        }

        internal bool TryAddCharacterInternal(uint unicode, out Character character) =>
            TryAddCharacterInternal(unicode, FontStyles.Normal, TextFontWeight.Regular, out character);

        /// <summary>Adds a character, rendering its glyph into the atlas when it is new.</summary>
        internal bool TryAddCharacterInternal(uint unicode, FontStyles fontStyle, TextFontWeight fontWeight, out Character character, bool populateLigatures = true)
        {
            character = null;
            if (m_MissingUnicodesFromFontFile.Contains(unicode)) return false;
            if (LoadFontFace() != FontEngineError.Success) return false;
            uint glyphIndex = FaceGlyphIndex(unicode);
            if (glyphIndex == 0)
            {
                m_MissingUnicodesFromFontFile.Add(unicode);
                return false;
            }
            if (glyphLookupTable.ContainsKey(glyphIndex))
            {
                character = CreateCharacterAndAddToCache(unicode, m_GlyphLookupDictionary[glyphIndex], fontStyle, fontWeight);
                RegisterResourceForUpdate?.Invoke(this);
                return true;
            }
            if (!TryAddGlyphToAtlas(glyphIndex, out var glyph, populateLigatures)) return false;
            character = CreateCharacterAndAddToCache(unicode, glyph, fontStyle, fontWeight);
            return true;
        }

        bool TryAddGlyphToAtlas(uint glyphIndex, out Glyph glyph, bool populateLigatures = true)
        {
            glyph = null;
            var texture = m_AtlasTextures[m_AtlasTextureIndex];
            if (!texture.isReadable)
            {
                Debug.LogWarning("Unable to add the requested glyph to font asset [" + name + "]'s atlas texture. Please make the texture [" + texture.name + "] readable.", texture);
                return false;
            }
            EnsureAtlasAllocated();
            FontEngine.SetTextureUploadMode(false);
            if (TryAddGlyphToTexture(glyphIndex, out glyph, populateLigatures)) return true;
            if (m_IsMultiAtlasTexturesEnabled && m_UsedGlyphRects.Count > 0)
            {
                SetupNewAtlasTexture();
                FontEngine.SetTextureUploadMode(false);
                if (TryAddGlyphToTexture(glyphIndex, out glyph, populateLigatures)) return true;
            }
            return false;
        }

        bool TryAddGlyphToTexture(uint glyphIndex, out Glyph glyph, bool populateLigatures = true)
        {
            var texture = m_AtlasTextures[m_AtlasTextureIndex];
            if (!FontEngine.TryAddGlyphToTexture(glyphIndex, m_AtlasPadding, GlyphPackingMode.BestShortSideFit, m_FreeGlyphRects, m_UsedGlyphRects, m_AtlasRenderMode, texture, out glyph))
                return false;
            glyph.atlasIndex = m_AtlasTextureIndex;
            m_GlyphTable.Add(glyph);
            m_GlyphLookupDictionary.Add(glyphIndex, glyph);
            m_GlyphIndexList.Add(glyphIndex);
            m_GlyphIndexListNewlyAdded.Add(glyphIndex);
            if (m_GetFontFeatures)
            {
                // Ligatures are not read (no GSUB yet); kerning is read with the next update.
                if (populateLigatures) RegisterFontAssetForFontFeatureUpdate(this);
                else RegisterFontAssetForKerningUpdate(this);
            }
            RegisterAtlasTextureForApply(texture);
            FontEngine.SetTextureUploadMode(true);
            RegisterResourceForUpdate?.Invoke(this);
            return true;
        }

        /// <summary>Starts another atlas texture (the font asset's material for atlas 0, fallback
        /// materials for the others) with all of its space free.</summary>
        void SetupNewAtlasTexture()
        {
            m_AtlasTextureIndex++;
            if (m_AtlasTextures.Length == m_AtlasTextureIndex) Array.Resize(ref m_AtlasTextures, m_AtlasTextures.Length * 2);
            var texture = new Texture2D(m_AtlasWidth, m_AtlasHeight, AtlasFormat(m_AtlasRenderMode), false) { hideFlags = m_AtlasTextures[0].hideFlags };
            m_AtlasTextures[m_AtlasTextureIndex] = texture;
            FontEngine.ResetAtlasTexture(texture);
            int border = AtlasBorder(m_AtlasRenderMode);
            m_FreeGlyphRects.Clear();
            m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - border, m_AtlasHeight - border));
            m_UsedGlyphRects.Clear();
            texture.name = atlasTexture.name + " " + m_AtlasTextureIndex;
            OnFontAssetTextureChanged?.Invoke(texture, this);
        }

        Character CreateCharacterAndAddToCache(uint unicode, Glyph glyph, FontStyles fontStyle, TextFontWeight fontWeight)
        {
            if (!m_CharacterLookupDictionary.TryGetValue(unicode, out var character))
            {
                character = new Character(unicode, this, glyph);
                m_CharacterTable.Add(character);
                AddCharacterToLookupCache(unicode, character, FontStyles.Normal, TextFontWeight.Regular);
            }
            if (fontStyle != FontStyles.Normal || fontWeight != TextFontWeight.Regular)
                AddCharacterToLookupCache(unicode, character, fontStyle, fontWeight);
            return character;
        }

        // ---- Font features ----

        void UpdateFontFeaturesForNewlyAddedGlyphs()
        {
            UpdateGlyphAdjustmentRecords();
            m_GlyphIndexListNewlyAdded.Clear();
        }

        void UpdateGlyphAdjustmentRecordsForNewGlyphs()
        {
            UpdateGlyphAdjustmentRecords();
            m_GlyphIndexListNewlyAdded.Clear();
        }

        void UpdateGPOSFontFeaturesForNewlyAddedGlyphs()
        {
            UpdateGlyphAdjustmentRecords();
            m_GlyphIndexListNewlyAdded.Clear();
        }

        internal void ImportFontFeatures()
        {
            if (LoadFontFace() != FontEngineError.Success) return;
            var pairs = FontEngine.GetAllPairAdjustmentRecords();
            if (pairs != null) AddPairAdjustmentRecords(pairs);
            m_ShouldReimportFontFeatures = false;
            RegisterResourceForUpdate?.Invoke(this);
        }

        /// <summary>Reads the kerning pairs whose first glyph was added since the last update.</summary>
        internal void UpdateGlyphAdjustmentRecords()
        {
            LoadFontFace();
            var pairs = FontEngine.GetPairAdjustmentRecords(m_GlyphIndexListNewlyAdded);
            if (pairs != null) AddPairAdjustmentRecords(pairs);
        }

        /// <summary>Adds pairs read in font units; the first glyph's advance adjustment is scaled to
        /// the sampling point size (the generator scales it with the element), as Unity does.</summary>
        void AddPairAdjustmentRecords(GlyphPairAdjustmentRecord[] records)
        {
            float scale = m_FaceInfo.pointSize / m_FaceInfo.unitsPerEM;
            var list = m_FontFeatureTable.glyphPairAdjustmentRecords;
            var lookup = m_FontFeatureTable.m_GlyphPairAdjustmentRecordLookup;
            foreach (var record in records)
            {
                uint first = record.firstAdjustmentRecord.glyphIndex;
                uint second = record.secondAdjustmentRecord.glyphIndex;
                if (first == 0 && second == 0) break;
                var value = record.firstAdjustmentRecord.glyphValueRecord;
                value.xAdvance *= scale;
                var scaled = record;
                scaled.firstAdjustmentRecord = new GlyphAdjustmentRecord(first, value);
                if (lookup.TryAdd(FontFeatureTable.PairKey(first, second), scaled)) list.Add(scaled);
            }
        }

        internal void EnsureNativeFontAssetIsCreated() => s_FontAssetByInstanceId.TryAdd(instanceID, this);

        /// <summary>The native (HarfBuzz) side of Unity's advanced text generator; not used.</summary>
        internal static void CreateHbFaceIfNeeded() { }
    }
}
