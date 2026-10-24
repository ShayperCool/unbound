using System;
using UnityEngine.TextCore.Text;

namespace UnityEngine.UIElements
{
    /// <summary>The text settings of panels without their own: one instance made on first use, as at runtime in Unity.</summary>
    public partial class PanelTextSettings : TextSettings
    {
        static PanelTextSettings s_DefaultPanelTextSettings;

        public PanelTextSettings() { }

        internal static PanelTextSettings defaultPanelTextSettings
        {
            get
            {
                InitializeDefaultPanelTextSettingsIfNull();
                return s_DefaultPanelTextSettings;
            }
        }

        internal static void InitializeDefaultPanelTextSettingsIfNull()
        {
            if (s_DefaultPanelTextSettings == null) s_DefaultPanelTextSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
        }
    }

    /// <summary>A text element's text handle: converts its computed style into TextCore generation
    /// settings, measures it for layout and generates it (standard text generator).</summary>
    internal partial class UITKTextHandle : TextHandle
    {
        protected TextElement m_TextElement;
        internal static readonly float k_MinPadding = 6f;

        public Vector2 MeasuredSizes { get; set; }

        public Vector2 RoundedSizes { get; set; }

        public UITKTextHandle(TextElement te)
        {
            m_TextElement = te;
        }

        public override bool IsPlaceholder => base.IsPlaceholder;

        /// <summary>Unity's advanced text generator (native ICU/HarfBuzz) is not available: text always
        /// goes through the standard generator.</summary>
        internal override bool IsAdvancedTextEnabledForElement() => false;

        /// <summary>The text's size at a width and height (NaN: unconstrained) with the element's style.</summary>
        public Vector2 ComputeTextSize(in RenderedText textToMeasure, float width, float height)
        {
            if (!ConvertUssToTextGenerationSettings()) return new Vector2(float.NaN, float.NaN);
            settings.renderedText = textToMeasure;
            settings.screenRect = new Rect(0f, 0f, width, height);
            UpdatePreferredValues(settings);
            return preferredSize;
        }

        /// <summary>Generates the text for its current style and size, reusing the previous result when unchanged.</summary>
        public void UpdateMesh()
        {
            if (!ConvertUssToTextGenerationSettings()) return;
            int hash = settings.GetHashCode();
            if (m_PreviousGenerationSettingsHash == hash && !isDirty)
            {
                AddTextInfoToTemporaryCache(hash);
                return;
            }
            RemoveTextInfoFromTemporaryCache();
            UpdateWithHash(hash);
        }

        public void ComputeSettingsAndUpdate() => UpdateMesh();

        public override void AddTextInfoToPermanentCache()
        {
            if (ConvertUssToTextGenerationSettings()) base.AddTextInfoToPermanentCache();
        }

        TextOverflowMode GetTextOverflowMode()
        {
            var style = m_TextElement.computedValues;
            if (style.textOverflow == TextOverflow.Clip) return TextOverflowMode.Masking;
            if (style.textOverflow != TextOverflow.Ellipsis) return TextOverflowMode.Overflow;
            if (!TextLibraryCanElide()) return TextOverflowMode.Masking;
            return style.overflow == OverflowInternal.Hidden ? TextOverflowMode.Ellipsis : TextOverflowMode.Overflow;
        }

        /// <summary>Fills the thread's generation settings from the element's computed style; the
        /// text box is the content rect, or the unrounded measured size the layout rounded to it.</summary>
        internal virtual bool ConvertUssToTextGenerationSettings()
        {
            var style = m_TextElement.computedValues;
            var tgs = settings;
            tgs.text = string.Empty;
            tgs.isIMGUI = false;
            tgs.textSettings = GetTextSettingsFrom(m_TextElement);
            if (tgs.textSettings == null) return false;
            tgs.fontAsset = GetFontAsset(m_TextElement);
            if (tgs.fontAsset == null) return false;
            tgs.screenRect = new Rect(0f, 0f, m_TextElement.contentRect.width, m_TextElement.contentRect.height);
            tgs.extraPadding = GetVertexPadding(tgs.fontAsset);
            tgs.renderedText = m_TextElement.isElided && !TextLibraryCanElide() ? new RenderedText(m_TextElement.elidedText) : new RenderedText(m_TextElement.renderedTextString);
            tgs.isPlaceholder = m_TextElement.showPlaceholderText;
            tgs.fontSize = style.fontSize.value;
            tgs.fontStyle = TextGeneratorUtilities.LegacyStyleToNewStyle(style.unityFontStyleAndWeight);
            tgs.isEditorRenderingModeBitmap = false;
            tgs.textAlignment = TextGeneratorUtilities.LegacyAlignmentToNewAlignment(style.unityTextAlign);
            tgs.textWrappingMode = ToTextWrappingMode(style.whiteSpace, m_TextElement.isInputField && !m_TextElement.edition.multiline);
            tgs.richText = m_TextElement.enableRichText;
            tgs.overflowMode = GetTextOverflowMode();
            tgs.characterSpacing = style.letterSpacing.value;
            tgs.wordSpacing = style.wordSpacing.value;
            tgs.paragraphSpacing = style.unityParagraphSpacing.value;
            tgs.color = style.color;
            tgs.shouldConvertToLinearSpace = false;
            tgs.parseControlCharacters = m_TextElement.parseEscapeSequences;
            tgs.isRightToLeft = m_TextElement.languageDirection == LanguageDirection.RTL;
            tgs.emojiFallbackSupport = m_TextElement.emojiFallbackSupport;
            var size = m_TextElement.contentRect.size;
            if (Mathf.Abs(size.x - RoundedSizes.x) < 0.01f && Mathf.Abs(size.y - RoundedSizes.y) < 0.01f)
            {
                size = MeasuredSizes;
            }
            else
            {
                RoundedSizes = size;
                MeasuredSizes = size;
            }
            tgs.screenRect = new Rect(Vector2.zero, size);
            return true;
        }

        internal static TextWrappingMode ToTextWrappingMode(WhiteSpace whiteSpace, bool isSingleLineInputField)
        {
            if (isSingleLineInputField)
                return whiteSpace == WhiteSpace.Pre || whiteSpace == WhiteSpace.PreWrap ? TextWrappingMode.PreserveWhitespaceNoWrap : TextWrappingMode.NoWrap;
            switch (whiteSpace)
            {
                case WhiteSpace.NoWrap: return TextWrappingMode.NoWrap;
                case WhiteSpace.PreWrap: return TextWrappingMode.PreserveWhitespace;
                case WhiteSpace.Pre: return TextWrappingMode.PreserveWhitespaceNoWrap;
                default: return TextWrappingMode.Normal;
            }
        }

        internal bool TextLibraryCanElide() => m_TextElement.computedValues.unityTextOverflowPosition == TextOverflowPosition.End;

        /// <summary>The padding around glyph quads: room for the outline and shadow, at least the minimum, in atlas units.</summary>
        internal float GetVertexPadding(FontAsset fontAsset)
        {
            var style = m_TextElement.computedValues;
            float outline = style.unityTextOutlineWidth / 2f;
            float offsetX = Mathf.Abs(style.textShadow.offset.x);
            float offsetY = Mathf.Abs(style.textShadow.offset.y);
            float blur = Mathf.Abs(style.textShadow.blurRadius);
            if (outline <= 0f && offsetX <= 0f && offsetY <= 0f && blur <= 0f) return k_MinPadding;
            float a = Mathf.Max(offsetX + blur, outline);
            float b = Mathf.Max(offsetY + blur, outline);
            float padding = Mathf.Max(a, b) + k_MinPadding;
            float unitsPerPixel = ConvertPixelUnitsToTextCoreRelativeUnits(style.fontSize.value, fontAsset);
            int atlasPadding = fontAsset.atlasPadding + 1;
            return Mathf.Min(padding * unitsPerPixel * atlasPadding, atlasPadding);
        }

        public bool IsElided() => string.IsNullOrEmpty(m_TextElement.text) || m_IsEllided;

        /// <summary>The element's font asset: its font definition's asset, else the panel text settings'
        /// asset for its font, else the settings' default.</summary>
        internal static FontAsset GetFontAsset(VisualElement ve)
        {
            var style = ve.computedValues;
            if (style.unityFontDefinition.fontAsset != null) return style.unityFontDefinition.fontAsset;
            var textSettings = GetTextSettingsFrom(ve);
            if (!Equals(style.unityFontDefinition.font, null)) return textSettings.GetCachedFontAsset(style.unityFontDefinition.font);
            if (!Equals(style.unityFont, null)) return textSettings.GetCachedFontAsset(style.unityFont);
            return textSettings?.defaultFontAsset;
        }

        internal static bool IsFontAssigned(VisualElement ve) => ve.computedValues.unityFont != null || !ve.computedValues.unityFontDefinition.IsEmpty();

        internal static TextSettings GetTextSettingsFrom(VisualElement ve)
        {
            if (ve.panel is RuntimePanel runtime && runtime.ownerObject is PanelSettings panelSettings)
                return panelSettings.textSettings != null ? panelSettings.textSettings : PanelTextSettings.defaultPanelTextSettings;
            return PanelTextSettings.defaultPanelTextSettings;
        }
    }
}
