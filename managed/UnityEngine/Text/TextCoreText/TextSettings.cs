using System;
using System.Collections.Generic;
using UnityEngine.Serialization;

namespace UnityEngine.TextCore.Text
{
    /// <summary>
    /// Text settings: the default font asset, the fallback font assets searched for characters a
    /// font lacks (then the OS fonts Unity lists as fallbacks), the missing-character glyph, the
    /// default style sheet, line-breaking rules, and the font assets made for Fonts
    /// (<see cref="GetCachedFontAsset"/>, how UI Toolkit draws a Font).
    /// </summary>
    [Serializable]
    public partial class TextSettings : ScriptableObject
    {
        [SerializeField] protected string m_Version;
        [SerializeField] [FormerlySerializedAs("m_defaultFontAsset")] protected FontAsset m_DefaultFontAsset;
        [SerializeField] [FormerlySerializedAs("m_defaultFontAssetPath")] protected string m_DefaultFontAssetPath = "Fonts & Materials/";
        [SerializeField] [FormerlySerializedAs("m_fallbackFontAssets")] protected List<FontAsset> m_FallbackFontAssets;
        static List<FontAsset> s_FallbackOSFontAssetInternal;
        [SerializeField] [FormerlySerializedAs("m_matchMaterialPreset")] protected bool m_MatchMaterialPreset;
        [SerializeField] [FormerlySerializedAs("m_missingGlyphCharacter")] protected int m_MissingCharacterUnicode;
        [SerializeField] protected bool m_ClearDynamicDataOnBuild = true;
        [SerializeField] bool m_EnableEmojiSupport;
        [SerializeField] List<TextAsset> m_EmojiFallbackTextAssets;
        [SerializeField] [FormerlySerializedAs("m_defaultSpriteAsset")] protected SpriteAsset m_DefaultSpriteAsset;
        [SerializeField] [FormerlySerializedAs("m_defaultSpriteAssetPath")] protected string m_DefaultSpriteAssetPath = "Sprite Assets/";
        [SerializeField] protected List<SpriteAsset> m_FallbackSpriteAssets;
        [SerializeField] protected uint m_MissingSpriteCharacterUnicode;
        [SerializeField] [FormerlySerializedAs("m_defaultStyleSheet")] protected TextStyleSheet m_DefaultStyleSheet;
        [SerializeField] protected string m_StyleSheetsResourcePath = "Text Style Sheets/";
        [SerializeField] [FormerlySerializedAs("m_defaultColorGradientPresetsPath")] protected string m_DefaultColorGradientPresetsPath = "Text Color Gradients/";
        [SerializeField] protected UnicodeLineBreakingRules m_UnicodeLineBreakingRules;
        [SerializeField] [FormerlySerializedAs("m_warningsDisabled")] protected bool m_DisplayWarnings;

        /// <summary>The font assets made for Fonts, by the Font's hash code.</summary>
        internal Dictionary<int, FontAsset> m_FontLookup;
        internal List<FontReferenceMap> m_FontReferences = new List<FontReferenceMap>();

        public TextSettings() { }

        public string version
        {
            get => m_Version;
            internal set => m_Version = value;
        }

        public FontAsset defaultFontAsset
        {
            get => m_DefaultFontAsset;
            set => m_DefaultFontAsset = value;
        }

        public string defaultFontAssetPath
        {
            get => m_DefaultFontAssetPath;
            set => m_DefaultFontAssetPath = value;
        }

        public List<FontAsset> fallbackFontAssets
        {
            get => m_FallbackFontAssets;
            set => m_FallbackFontAssets = value;
        }

        /// <summary>Font assets of the OS fonts Unity falls back to, made once for all settings.</summary>
        internal List<FontAsset> fallbackOSFontAssets
        {
            get
            {
                if (GetStaticFallbackOSFontAsset() == null) SetStaticFallbackOSFontAsset(GetOSFontAssetList());
                return GetStaticFallbackOSFontAsset();
            }
        }

        public bool matchMaterialPreset
        {
            get => m_MatchMaterialPreset;
            set => m_MatchMaterialPreset = value;
        }

        public int missingCharacterUnicode
        {
            get => m_MissingCharacterUnicode;
            set => m_MissingCharacterUnicode = value;
        }

        public bool clearDynamicDataOnBuild
        {
            get => m_ClearDynamicDataOnBuild;
            set => m_ClearDynamicDataOnBuild = value;
        }

        public bool enableEmojiSupport
        {
            get => m_EnableEmojiSupport;
            set => m_EnableEmojiSupport = value;
        }

        public List<TextAsset> emojiFallbackTextAssets
        {
            get => m_EmojiFallbackTextAssets;
            set => m_EmojiFallbackTextAssets = value;
        }

        public SpriteAsset defaultSpriteAsset
        {
            get => m_DefaultSpriteAsset;
            set => m_DefaultSpriteAsset = value;
        }

        public string defaultSpriteAssetPath
        {
            get => m_DefaultSpriteAssetPath;
            set => m_DefaultSpriteAssetPath = value;
        }

        public uint missingSpriteCharacterUnicode
        {
            get => m_MissingSpriteCharacterUnicode;
            set => m_MissingSpriteCharacterUnicode = value;
        }

        public TextStyleSheet defaultStyleSheet
        {
            get => m_DefaultStyleSheet;
            set => m_DefaultStyleSheet = value;
        }

        public string styleSheetsResourcePath
        {
            get => m_StyleSheetsResourcePath;
            set => m_StyleSheetsResourcePath = value;
        }

        public string defaultColorGradientPresetsPath
        {
            get => m_DefaultColorGradientPresetsPath;
            set => m_DefaultColorGradientPresetsPath = value;
        }

        public UnicodeLineBreakingRules lineBreakingRules
        {
            get
            {
                if (m_UnicodeLineBreakingRules == null)
                {
                    m_UnicodeLineBreakingRules = new UnicodeLineBreakingRules();
                    m_UnicodeLineBreakingRules.LoadLineBreakingRules();
                }
                return m_UnicodeLineBreakingRules;
            }
            set => m_UnicodeLineBreakingRules = value;
        }

        public bool displayWarnings
        {
            get => m_DisplayWarnings;
            set => m_DisplayWarnings = value;
        }

        internal virtual List<FontAsset> GetStaticFallbackOSFontAsset() => s_FallbackOSFontAssetInternal;

        internal virtual void SetStaticFallbackOSFontAsset(List<FontAsset> fontAssets) => s_FallbackOSFontAssetInternal = fontAssets;

        internal virtual List<FontAsset> GetFallbackFontAssets(int textPixelSize = -1) => fallbackFontAssets;

        void OnEnable()
        {
            lineBreakingRules.LoadLineBreakingRules();
            SetStaticFallbackOSFontAsset(null);
        }

        protected void InitializeFontReferenceLookup()
        {
            m_FontReferences ??= new List<FontReferenceMap>();
            for (int i = 0; i < m_FontReferences.Count; i++)
            {
                var reference = m_FontReferences[i];
                if (reference.font == null || reference.fontAsset == null)
                {
                    m_FontReferences.RemoveAt(i--);
                    continue;
                }
                m_FontLookup.TryAdd(reference.font.GetHashCode(), reference.fontAsset);
            }
        }

        /// <summary>The font asset for a Font, made on first use (FontAssetFactory.ConvertFontToFontAsset).</summary>
        internal FontAsset GetCachedFontAsset(Font font)
        {
            if (font == null) return null;
            if (m_FontLookup == null)
            {
                m_FontLookup = new Dictionary<int, FontAsset>();
                InitializeFontReferenceLookup();
            }
            int key = font.GetHashCode();
            if (m_FontLookup.TryGetValue(key, out var cached)) return cached;
            if (TextGenerator.IsExecutingJob) return null;
            var fontAsset = FontAssetFactory.ConvertFontToFontAsset(font);
            if (fontAsset != null)
            {
                m_FontReferences.Add(new FontReferenceMap(font, fontAsset));
                m_FontLookup.Add(key, fontAsset);
            }
            return fontAsset;
        }

        static List<FontAsset> GetOSFontAssetList() => FontAsset.CreateFontAssetOSFallbackList(Font.GetOSFallbacks());

        internal virtual float GetEditorTextSharpness()
        {
            Debug.LogWarning("GetEditorTextSettings() should only be called on EditorTextSettings");
            return 0f;
        }

        internal virtual Font GetEditorFont()
        {
            Debug.LogWarning("GetEditorTextSettings() should only be called on EditorTextSettings");
            return null;
        }

        [Serializable]
        internal partial struct FontReferenceMap
        {
            public Font font;
            public FontAsset fontAsset;

            public FontReferenceMap(Font font, FontAsset fontAsset)
            {
                this.font = font;
                this.fontAsset = fontAsset;
            }
        }
    }
}
