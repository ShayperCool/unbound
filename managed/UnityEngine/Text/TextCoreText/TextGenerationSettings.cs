using System;
using System.Collections.Generic;

namespace UnityEngine.TextCore.Text
{
    /// <summary>What to generate: the text, the rect it lays out in (its width wraps lines; its
    /// height bounds overflow), the font asset and size, style, alignment, wrapping, overflow and
    /// spacing. The constants are the features UI Toolkit's text never changes.</summary>
    internal partial class TextGenerationSettings : IEquatable<TextGenerationSettings>
    {
        internal static Func<bool> IsEditorTextRenderingModeBitmap;

        RenderedText m_RenderedText;
        string m_CachedRenderedText;

        public Rect screenRect;
        public float pixelsPerPoint = 1f;
        public bool isEditorRenderingModeBitmap;
        public FontAsset fontAsset;
        public FontStyles fontStyle = FontStyles.Normal;
        public TextSettings textSettings;
        public TextAlignment textAlignment = TextAlignment.TopLeft;
        public TextOverflowMode overflowMode = TextOverflowMode.Overflow;
        public const float wordWrappingRatio = 0.4f;
        public Color color = Color.white;
        public bool shouldConvertToLinearSpace = true;
        public float fontSize = 18f;
        public const bool autoSize = false;
        public const float fontSizeMin = 0f;
        public const float fontSizeMax = 0f;
        /// <summary>The OpenType features generation applies: kerning only.</summary>
        internal static readonly List<OTL_FeatureTag> fontFeatures = new List<OTL_FeatureTag> { OTL_FeatureTag.kern };
        public bool emojiFallbackSupport = true;
        public bool richText;
        public bool isRightToLeft;
        public float extraPadding = 6f;
        public bool parseControlCharacters = true;
        public bool isPlaceholder;
        public const bool tagNoParsing = false;
        public float characterSpacing;
        public float wordSpacing;
        public const float lineSpacing = 0f;
        public float paragraphSpacing;
        public const float lineSpacingMax = 0f;
        public TextWrappingMode textWrappingMode = TextWrappingMode.Normal;
        public const int maxVisibleCharacters = 99999;
        public const int maxVisibleWords = 99999;
        public const int maxVisibleLines = 99999;
        public const int firstVisibleCharacter = 0;
        public const bool useMaxVisibleDescender = false;
        public TextFontWeight fontWeight = TextFontWeight.Regular;
        public bool isIMGUI;
        public const float charWidthMaxAdj = 0f;

        public TextGenerationSettings() { }

        internal TextGenerationSettings(TextGenerationSettings tgs)
        {
            m_RenderedText = tgs.m_RenderedText;
            m_CachedRenderedText = tgs.m_CachedRenderedText;
            screenRect = tgs.screenRect;
            pixelsPerPoint = tgs.pixelsPerPoint;
            fontAsset = tgs.fontAsset;
            fontStyle = tgs.fontStyle;
            textSettings = tgs.textSettings;
            textAlignment = tgs.textAlignment;
            overflowMode = tgs.overflowMode;
            shouldConvertToLinearSpace = tgs.shouldConvertToLinearSpace;
            fontSize = tgs.fontSize;
            emojiFallbackSupport = tgs.emojiFallbackSupport;
            richText = tgs.richText;
            isRightToLeft = tgs.isRightToLeft;
            extraPadding = tgs.extraPadding;
            parseControlCharacters = tgs.parseControlCharacters;
            isPlaceholder = tgs.isPlaceholder;
            characterSpacing = tgs.characterSpacing;
            wordSpacing = tgs.wordSpacing;
            paragraphSpacing = tgs.paragraphSpacing;
            textWrappingMode = tgs.textWrappingMode;
            fontWeight = tgs.fontWeight;
            isIMGUI = tgs.isIMGUI;
        }

        public RenderedText renderedText
        {
            get => m_RenderedText;
            set
            {
                m_RenderedText = value;
                m_CachedRenderedText = null;
            }
        }

        public string text
        {
            get => m_CachedRenderedText ??= m_RenderedText.CreateString();
            set => renderedText = new RenderedText(value);
        }

        static bool BitmapMode() => IsEditorTextRenderingModeBitmap != null && IsEditorTextRenderingModeBitmap();

        public bool Equals(TextGenerationSettings other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (BitmapMode() && !pixelsPerPoint.Equals(other.pixelsPerPoint)) return false;
            return m_RenderedText.Equals(other.m_RenderedText) && screenRect.Equals(other.screenRect) && Equals(fontAsset, other.fontAsset) &&
                fontStyle == other.fontStyle && Equals(textSettings, other.textSettings) && textAlignment == other.textAlignment &&
                overflowMode == other.overflowMode && color.Equals(other.color) && fontSize.Equals(other.fontSize) &&
                shouldConvertToLinearSpace == other.shouldConvertToLinearSpace && emojiFallbackSupport == other.emojiFallbackSupport &&
                richText == other.richText && isRightToLeft == other.isRightToLeft && extraPadding == other.extraPadding &&
                parseControlCharacters == other.parseControlCharacters && isPlaceholder == other.isPlaceholder &&
                characterSpacing.Equals(other.characterSpacing) && wordSpacing.Equals(other.wordSpacing) &&
                paragraphSpacing.Equals(other.paragraphSpacing) && textWrappingMode == other.textWrappingMode &&
                fontWeight == other.fontWeight && isIMGUI == other.isIMGUI;
        }

        public override bool Equals(object obj) => obj is TextGenerationSettings other && obj.GetType() == GetType() && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(m_RenderedText);
            hash.Add(screenRect);
            hash.Add(fontAsset);
            hash.Add((int)fontStyle);
            hash.Add(textSettings);
            hash.Add((int)textAlignment);
            hash.Add((int)overflowMode);
            hash.Add(color);
            hash.Add(shouldConvertToLinearSpace);
            hash.Add(fontSize);
            hash.Add(emojiFallbackSupport);
            hash.Add(richText);
            hash.Add(isRightToLeft);
            hash.Add(extraPadding);
            hash.Add(parseControlCharacters);
            hash.Add(isPlaceholder);
            hash.Add(characterSpacing);
            hash.Add(wordSpacing);
            hash.Add(paragraphSpacing);
            hash.Add((int)textWrappingMode);
            hash.Add((int)fontWeight);
            hash.Add(isIMGUI);
            if (BitmapMode()) hash.Add(pixelsPerPoint);
            return hash.ToHashCode();
        }

        public static bool operator ==(TextGenerationSettings left, TextGenerationSettings right) => Equals(left, right);

        public static bool operator !=(TextGenerationSettings left, TextGenerationSettings right) => !Equals(left, right);

        public override string ToString() =>
            $"text: {text}\n screenRect: {screenRect}\n fontAsset: {fontAsset}\n fontStyle: {fontStyle}\n textSettings: {textSettings}\n " +
            $"textAlignment: {textAlignment}\n overflowMode: {overflowMode}\n textWrappingMode: {textWrappingMode}\n color: {color}\n " +
            $"fontSize: {fontSize}\n richText: {richText}\n isRightToLeft: {isRightToLeft}\n extraPadding: {extraPadding}\n " +
            $"parseControlCharacters: {parseControlCharacters}\n characterSpacing: {characterSpacing}\n wordSpacing: {wordSpacing}\n " +
            $"paragraphSpacing: {paragraphSpacing}\n fontWeight: {fontWeight}\n shouldConvertToLinearSpace: {shouldConvertToLinearSpace}\n isPlaceholder: {isPlaceholder}";
    }
}
