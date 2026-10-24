using System;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    internal partial class TextGenerator
    {
        /// <summary>
        /// The preferred size (TextCore's CalculatePreferredValues): the layout pass reduced to
        /// what sizes the text: widest line extent (advance of each visible character's end) and
        /// height from the first line's ascender to the lowest descender, with line breaks where
        /// they would fall in <paramref name="marginSize"/>. It differs from the full layout in
        /// details Unity's does too (no glyph scale, no height overflow, no soft line breaks).
        /// </summary>
        Vector2 MeasurePreferred(ref float fontSize, Vector2 marginSize, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            var fontAsset = generationSettings.fontAsset;
            if (fontAsset == null || fontAsset.characterLookupTable == null)
            {
                Debug.LogWarning("Can't Generate Mesh! No Font Asset has been assigned.");
                return Vector2.zero;
            }
            if (m_TextProcessingArray == null || m_TextProcessingArray.Length == 0 || m_TextProcessingArray[0].unicode == 0) return Vector2.zero;
            m_CurrentFontAsset = fontAsset;
            m_CurrentMaterial = fontAsset.material;
            m_CurrentMaterialIndex = 0;
            m_MaterialReferenceStack.SetDefault(new MaterialReference(0, m_CurrentFontAsset, null, m_CurrentMaterial, m_Padding));
            int totalCharacterCount = m_TotalCharacterCount;
            if (m_MeasureElements == null || totalCharacterCount > m_MeasureElements.Length)
                m_MeasureElements = new TextElementInfo[totalCharacterCount > 1024 ? totalCharacterCount + 256 : Mathf.NextPowerOfTwo(totalCharacterCount)];
            var measured = m_MeasureElements;
            float baseScale = (float)((double)fontSize / fontAsset.faceInfo.pointSize * fontAsset.faceInfo.scale);
            float elementScale = baseScale;
            float emScale = fontSize * 0.01f;
            BeginLayout(generationSettings);
            m_CurrentFontSize = fontSize;
            m_SizeStack.SetDefault(m_CurrentFontSize);
            float lineGap = (float)((double)m_CurrentFontAsset.faceInfo.lineHeight - ((double)m_CurrentFontAsset.faceInfo.ascentLine - m_CurrentFontAsset.faceInfo.descentLine));
            bool kerning = TextGenerationSettings.fontFeatures.Contains(OTL_FeatureTag.kern);
            var textSettings = generationSettings.textSettings;
            float areaWidth = marginSize.x;
            m_MarginLeft = 0f;
            m_MarginRight = 0f;
            m_Width = -1f;
            float lineWidth = LineWidth(areaWidth, m_MarginLeft, m_MarginRight);
            var wrapMode = generationSettings.textWrappingMode;
            float preferredWidth = 0f;
            float preferredHeight = 0f;
            m_IsCalculatingPreferredValues = true;
            m_MaxCapHeight = 0f;
            m_MaxAscender = 0f;
            m_MaxDescender = 0f;
            bool isFirstWordOfLine = true;
            m_IsNonBreakingSpace = false;
            const bool isLastBreakingChar = false;
            var substitution = new CharacterSubstitution(-1, 0);
            bool isSoftHyphenIgnored = false;
            var wrapState = default(LayoutState);
            var lineState = default(LayoutState);
            var softBreakState = default(LayoutState);
            m_IsTextTruncated = false;

            for (int i = 0; i < m_TextProcessingArray.Length && m_TextProcessingArray[i].unicode != 0; i++)
            {
                uint charCode = m_TextProcessingArray[i].unicode;
                if (charCode == k_Consumed) continue;
                if (generationSettings.richText && charCode == k_LessThan)
                {
                    m_IsTextLayoutPhase = true;
                    m_TextElementType = TextElementType.Character;
                    if (ValidateHtmlTag(m_TextProcessingArray, i + 1, out int endIndex, generationSettings, textInfo, out _))
                    {
                        i = endIndex;
                        if (m_TextElementType == TextElementType.Character) continue;
                    }
                }
                else
                {
                    m_TextElementType = textInfo.textElementInfo[m_CharacterCount].elementType;
                    m_CurrentMaterialIndex = textInfo.textElementInfo[m_CharacterCount].materialReferenceIndex;
                    m_CurrentFontAsset = textInfo.textElementInfo[m_CharacterCount].fontAsset;
                }
                bool isUsingAlternateTypeface = textInfo.textElementInfo[m_CharacterCount].isUsingAlternateTypeface;
                m_IsTextLayoutPhase = false;
                bool isInjected = false;
                if (substitution.index == m_CharacterCount)
                {
                    charCode = substitution.unicode;
                    m_TextElementType = TextElementType.Character;
                    isInjected = true;
                    if (charCode == k_EndOfText)
                    {
                        measured[m_CharacterCount].textElement = m_CurrentFontAsset.characterLookupTable[k_EndOfText];
                        m_IsTextTruncated = true;
                    }
                    else if (charCode == k_Ellipsis)
                    {
                        measured[m_CharacterCount].textElement = m_Ellipsis.character;
                        measured[m_CharacterCount].elementType = TextElementType.Character;
                        measured[m_CharacterCount].fontAsset = m_Ellipsis.fontAsset;
                        measured[m_CharacterCount].material = m_Ellipsis.material;
                        measured[m_CharacterCount].materialReferenceIndex = m_Ellipsis.materialIndex;
                        m_IsTextTruncated = true;
                        substitution.index = m_CharacterCount + 1;
                        substitution.unicode = k_EndOfText;
                    }
                }
                if (m_CharacterCount < 0 && charCode != k_EndOfText)
                {
                    measured[m_CharacterCount].isVisible = false;
                    measured[m_CharacterCount].character = k_ZeroWidthSpace;
                    measured[m_CharacterCount].lineNumber = 0;
                    m_CharacterCount++;
                    continue;
                }
                float smallCapsMultiplier = 1f;
                if (m_TextElementType == TextElementType.Character)
                {
                    if ((m_FontStyleInternal & FontStyles.UpperCase) == FontStyles.UpperCase)
                    {
                        if (char.IsLower((char)charCode)) charCode = char.ToUpper((char)charCode);
                    }
                    else if ((m_FontStyleInternal & FontStyles.LowerCase) == FontStyles.LowerCase)
                    {
                        if (char.IsUpper((char)charCode)) charCode = char.ToLower((char)charCode);
                    }
                    else if ((m_FontStyleInternal & FontStyles.SmallCaps) == FontStyles.SmallCaps && char.IsLower((char)charCode))
                    {
                        smallCapsMultiplier = 0.8f;
                        charCode = char.ToUpper((char)charCode);
                    }
                }
                // The measure has no baseline shift.
                const float baselineShift = 0f;
                float elementAscender = 0f;
                float elementDescender = 0f;
                if (m_TextElementType == TextElementType.Character)
                {
                    m_CachedTextElement = textInfo.textElementInfo[m_CharacterCount].textElement;
                    if (m_CachedTextElement == null) continue;
                    m_CurrentFontAsset = textInfo.textElementInfo[m_CharacterCount].fontAsset;
                    m_CurrentMaterial = textInfo.textElementInfo[m_CharacterCount].material;
                    m_CurrentMaterialIndex = textInfo.textElementInfo[m_CharacterCount].materialReferenceIndex;
                    var face = m_CurrentFontAsset.m_FaceInfo;
                    float fontScale = isInjected && m_TextProcessingArray[i].unicode == k_LineFeed && m_CharacterCount != m_FirstCharacterOfLine
                        ? (float)((double)textInfo.textElementInfo[m_CharacterCount - 1].pointSize * smallCapsMultiplier / face.pointSize * face.scale)
                        : (float)((double)m_CurrentFontSize * smallCapsMultiplier / face.pointSize * face.scale);
                    if (isInjected && charCode == k_Ellipsis)
                    {
                        elementAscender = 0f;
                        elementDescender = 0f;
                    }
                    else
                    {
                        elementAscender = face.ascentLine;
                        elementDescender = face.descentLine;
                    }
                    elementScale = (float)((double)fontScale * m_FontScaleMultiplier * m_CachedTextElement.scale);
                    measured[m_CharacterCount].elementType = TextElementType.Character;
                }
                float unhiddenScale = elementScale;
                if (charCode == k_SoftHyphen || charCode == k_EndOfText) elementScale = 0f;
                ref var info = ref measured[m_CharacterCount];
                info.character = (ushort)charCode;
                info.style = m_FontStyleInternal;
                if (m_FontWeightInternal == TextFontWeight.Bold) info.style |= FontStyles.Bold;
                var metrics = textInfo.textElementInfo[m_CharacterCount].alternativeGlyph?.metrics ?? m_CachedTextElement.m_Glyph.metrics;
                bool isWhiteSpace = charCode <= 0xFFFF && char.IsWhiteSpace((char)charCode);
                var adjustment = default(GlyphValueRecord);
                float characterSpacing = generationSettings.characterSpacing;
                if (kerning && m_TextElementType == TextElementType.Character)
                {
                    uint glyph = m_CachedTextElement.m_GlyphIndex;
                    var pairs = m_CurrentFontAsset.m_FontFeatureTable.m_GlyphPairAdjustmentRecordLookup;
                    if (m_CharacterCount < totalCharacterCount - 1 && textInfo.textElementInfo[m_CharacterCount + 1].elementType == TextElementType.Character)
                    {
                        uint nextGlyph = textInfo.textElementInfo[m_CharacterCount + 1].textElement.m_GlyphIndex;
                        if (pairs.TryGetValue(FontFeatureTable.PairKey(glyph, nextGlyph), out var pair))
                        {
                            adjustment = pair.firstAdjustmentRecord.glyphValueRecord;
                            if ((pair.featureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) == FontFeatureLookupFlags.IgnoreSpacingAdjustments) characterSpacing = 0f;
                        }
                    }
                    if (m_CharacterCount >= 1)
                    {
                        uint previousGlyph = textInfo.textElementInfo[m_CharacterCount - 1].textElement.m_GlyphIndex;
                        if (textInfo.textElementInfo[m_CharacterCount - 1].elementType == TextElementType.Character &&
                            pairs.TryGetValue(FontFeatureTable.PairKey(previousGlyph, glyph), out var pair))
                        {
                            adjustment += pair.secondAdjustmentRecord.glyphValueRecord;
                            if ((pair.featureLookupFlags & FontFeatureLookupFlags.IgnoreSpacingAdjustments) == FontFeatureLookupFlags.IgnoreSpacingAdjustments) characterSpacing = 0f;
                        }
                    }
                    info.adjustedHorizontalAdvance = adjustment.xAdvance;
                }
                bool isBaseGlyph = TextGeneratorUtilities.IsBaseGlyph(charCode);
                if (isBaseGlyph) m_LastBaseGlyphIndex = m_CharacterCount;
                elementAscender += adjustment.yPlacement;
                elementDescender += adjustment.yPlacement;
                float monoAdvance = 0f;
                if (m_MonoSpacing != 0f && charCode != k_ZeroWidthSpace)
                {
                    var glyphMetrics = m_CachedTextElement.glyph.metrics;
                    monoAdvance = (float)(((double)m_MonoSpacing / 2.0 - ((double)glyphMetrics.width / 2.0 + glyphMetrics.horizontalBearingX) * elementScale) * (1.0 - m_CharWidthAdjDelta));
                    m_XAdvance += monoAdvance;
                }
                float boldSpacing = 0f;
                if (m_TextElementType == TextElementType.Character && !isUsingAlternateTypeface && (info.style & FontStyles.Bold) == FontStyles.Bold)
                    boldSpacing = m_CurrentFontAsset.boldStyleSpacing;
                info.origin = (float)((double)m_XAdvance + (double)adjustment.xPlacement * elementScale);
                info.baseLine = (float)((double)baselineShift - m_LineOffset + m_BaselineOffset + (double)adjustment.yPlacement * elementScale);
                float ascender = (float)((double)elementAscender * elementScale / smallCapsMultiplier + m_BaselineOffset);
                float descender = (float)((double)elementDescender * elementScale / smallCapsMultiplier + m_BaselineOffset);
                float adjustedAscender = ascender;
                float adjustedDescender = descender;
                bool isFirstOfLine = m_CharacterCount == m_FirstCharacterOfLine;
                if (isFirstOfLine || !isWhiteSpace)
                {
                    if (m_BaselineOffset != 0f)
                    {
                        adjustedAscender = Mathf.Max((float)(((double)ascender - m_BaselineOffset) / m_FontScaleMultiplier), adjustedAscender);
                        adjustedDescender = Mathf.Min((float)(((double)descender - m_BaselineOffset) / m_FontScaleMultiplier), adjustedDescender);
                    }
                    m_MaxLineAscender = Mathf.Max(adjustedAscender, m_MaxLineAscender);
                    m_MaxLineDescender = Mathf.Min(adjustedDescender, m_MaxLineDescender);
                    info.adjustedAscender = adjustedAscender;
                    info.adjustedDescender = adjustedDescender;
                    info.ascender = ascender - m_LineOffset;
                    m_MaxDescender = info.descender = descender - m_LineOffset;
                }
                else
                {
                    info.adjustedAscender = m_MaxLineAscender;
                    info.adjustedDescender = m_MaxLineDescender;
                    info.ascender = m_MaxLineAscender - m_LineOffset;
                    m_MaxDescender = info.descender = m_MaxLineDescender - m_LineOffset;
                }
                if (m_LineNumber == 0 && (isFirstOfLine || !isWhiteSpace))
                {
                    m_MaxAscender = m_MaxLineAscender;
                    m_MaxCapHeight = Mathf.Max(m_MaxCapHeight, (float)((double)m_CurrentFontAsset.m_FaceInfo.capLine * elementScale / smallCapsMultiplier));
                }
                if (m_LineOffset == 0f && (!isWhiteSpace || isFirstOfLine)) m_PageAscender = m_PageAscender > ascender ? m_PageAscender : ascender;

                bool isPreserveWhitespace = wrapMode == TextWrappingMode.PreserveWhitespace || wrapMode == TextWrappingMode.PreserveWhitespaceNoWrap;
                if (charCode == k_Tab || charCode == k_ZeroWidthSpace || (isPreserveWhitespace && (isWhiteSpace || charCode == k_ZeroWidthSpace)) ||
                    (!isWhiteSpace && charCode != k_ZeroWidthSpace && charCode != k_SoftHyphen && charCode != k_EndOfText) || (charCode == k_SoftHyphen && !isSoftHyphenIgnored))
                {
                    lineWidth = m_Width != -1f ? Mathf.Min(LineWidth(areaWidth, m_MarginLeft, m_MarginRight), m_Width) : LineWidth(areaWidth, m_MarginLeft, m_MarginRight);
                    float extent = (float)((double)Mathf.Abs(m_XAdvance) + (double)metrics.horizontalAdvance * (1.0 - m_CharWidthAdjDelta) * (charCode == k_SoftHyphen ? unhiddenScale : elementScale));
                    if (isBaseGlyph && extent > lineWidth && wrapMode != TextWrappingMode.NoWrap && wrapMode != TextWrappingMode.PreserveWhitespaceNoWrap && m_CharacterCount != m_FirstCharacterOfLine)
                    {
                        i = RestoreWordWrappingState(ref wrapState, textInfo);
                        if (measured[m_CharacterCount - 1].character == k_SoftHyphen && !isSoftHyphenIgnored && generationSettings.overflowMode == TextOverflowMode.Overflow)
                        {
                            substitution.index = m_CharacterCount - 1;
                            substitution.unicode = k_HyphenMinus;
                            i--;
                            m_CharacterCount--;
                            continue;
                        }
                        isSoftHyphenIgnored = false;
                        if (measured[m_CharacterCount].character == k_SoftHyphen)
                        {
                            isSoftHyphenIgnored = true;
                            continue;
                        }
                        float ascenderDelta = m_MaxLineAscender - m_StartOfLineAscender;
                        if (m_LineOffset > 0f && Math.Abs(ascenderDelta) > 0.01f && !m_IsDrivenLineSpacing)
                        {
                            m_MaxDescender -= ascenderDelta;
                            m_LineOffset += ascenderDelta;
                        }
                        float lineDescender = m_MaxLineDescender - m_LineOffset;
                        m_MaxDescender = m_MaxDescender < lineDescender ? m_MaxDescender : lineDescender;
                        m_FirstCharacterOfLine = m_CharacterCount;
                        m_LineVisibleCharacterCount = 0;
                        SaveWordWrappingState(ref lineState, i, m_CharacterCount - 1, textInfo);
                        m_LineNumber++;
                        float nextAscender = measured[m_CharacterCount].adjustedAscender;
                        if (m_LineHeight == k_Unset)
                        {
                            m_LineOffset = (float)((double)m_LineOffset + (0.0 - m_MaxLineDescender + nextAscender + ((double)lineGap + m_LineSpacingDelta) * baseScale + 0.0 * emScale));
                            m_IsDrivenLineSpacing = false;
                        }
                        else
                        {
                            m_LineOffset = (float)((double)m_LineOffset + ((double)m_LineHeight + 0.0 * emScale));
                            m_IsDrivenLineSpacing = true;
                        }
                        m_MaxLineAscender = k_Unset;
                        m_MaxLineDescender = k_LargeFloat;
                        m_StartOfLineAscender = nextAscender;
                        m_XAdvance = 0f + m_TagIndent;
                        isFirstWordOfLine = true;
                        continue;
                    }
                    preferredWidth = Mathf.Max(preferredWidth, (float)((double)extent + m_MarginLeft + m_MarginRight));
                    preferredHeight = Mathf.Max(preferredHeight, m_MaxAscender - m_MaxDescender);
                }
                if (m_LineOffset > 0f && !TextGeneratorUtilities.Approximately(m_MaxLineAscender, m_StartOfLineAscender) && !m_IsDrivenLineSpacing)
                {
                    float ascenderDelta = m_MaxLineAscender - m_StartOfLineAscender;
                    m_MaxDescender -= ascenderDelta;
                    m_LineOffset += ascenderDelta;
                    m_StartOfLineAscender += ascenderDelta;
                    wrapState.lineOffset = m_LineOffset;
                    wrapState.startOfLineAscender = m_StartOfLineAscender;
                }
                if (charCode == k_Tab)
                {
                    float tab = (float)((double)m_CurrentFontAsset.faceInfo.tabWidth * (float)(int)m_CurrentFontAsset.tabMultiple * elementScale);
                    float nextStop = (float)((double)Mathf.Ceil(m_XAdvance / tab) * tab);
                    m_XAdvance = nextStop > m_XAdvance ? nextStop : m_XAdvance + tab;
                }
                else if (charCode != k_ZeroWidthSpace)
                {
                    if (m_MonoSpacing != 0f)
                    {
                        float cell = (float)(m_DuoSpace && (charCode == '.' || charCode == ':' || charCode == ',') ? (double)m_MonoSpacing / 2.0 - monoAdvance : (double)m_MonoSpacing - monoAdvance);
                        m_XAdvance = (float)((double)m_XAdvance + ((double)cell + ((double)m_CurrentFontAsset.regularStyleSpacing + characterSpacing) * emScale + m_CSpacing) * (1.0 - m_CharWidthAdjDelta));
                        if (isWhiteSpace || charCode == k_ZeroWidthSpace) m_XAdvance = (float)((double)m_XAdvance + (double)generationSettings.wordSpacing * emScale);
                    }
                    else
                    {
                        m_XAdvance = (float)((double)m_XAdvance + (((double)metrics.horizontalAdvance * m_FXScale.x + adjustment.xAdvance) * elementScale + ((double)m_CurrentFontAsset.regularStyleSpacing + characterSpacing + boldSpacing) * emScale + m_CSpacing) * (1.0 - m_CharWidthAdjDelta));
                        if (isWhiteSpace || charCode == k_ZeroWidthSpace) m_XAdvance = (float)((double)m_XAdvance + (double)generationSettings.wordSpacing * emScale);
                    }
                }
                if (charCode == k_CarriageReturn) m_XAdvance = 0f + m_TagIndent;
                if (charCode == k_LineFeed || charCode == k_VerticalTab || charCode == k_EndOfText || charCode == k_LineSeparator || charCode == k_ParagraphSeparator || m_CharacterCount == totalCharacterCount - 1)
                {
                    float ascenderDelta = m_MaxLineAscender - m_StartOfLineAscender;
                    if (m_LineOffset > 0f && Math.Abs(ascenderDelta) > 0.01f && !m_IsDrivenLineSpacing)
                    {
                        m_MaxDescender -= ascenderDelta;
                        m_LineOffset += ascenderDelta;
                    }
                    float lineDescender = m_MaxLineDescender - m_LineOffset;
                    m_MaxDescender = m_MaxDescender < lineDescender ? m_MaxDescender : lineDescender;
                    if (charCode == k_LineFeed || charCode == k_VerticalTab || charCode == k_HyphenMinus || charCode == k_LineSeparator || charCode == k_ParagraphSeparator)
                    {
                        SaveWordWrappingState(ref lineState, i, m_CharacterCount, textInfo);
                        SaveWordWrappingState(ref wrapState, i, m_CharacterCount, textInfo);
                        m_LineNumber++;
                        m_FirstCharacterOfLine = m_CharacterCount + 1;
                        float nextAscender = measured[m_CharacterCount].adjustedAscender;
                        float paragraph = charCode == k_LineFeed || charCode == k_ParagraphSeparator ? generationSettings.paragraphSpacing : 0f;
                        if (m_LineHeight == k_Unset)
                        {
                            float lineDelta = (float)(0.0 - m_MaxLineDescender + nextAscender + ((double)lineGap + m_LineSpacingDelta) * baseScale + (0.0 + paragraph) * emScale);
                            m_LineOffset += lineDelta;
                            m_IsDrivenLineSpacing = false;
                        }
                        else
                        {
                            m_LineOffset = (float)((double)m_LineOffset + ((double)m_LineHeight + (0.0 + paragraph) * emScale));
                            m_IsDrivenLineSpacing = true;
                        }
                        m_MaxLineAscender = k_Unset;
                        m_MaxLineDescender = k_LargeFloat;
                        m_StartOfLineAscender = nextAscender;
                        m_XAdvance = (float)(0.0 + m_TagLineIndent + m_TagIndent);
                        m_CharacterCount++;
                        continue;
                    }
                    if (charCode == k_EndOfText) i = m_TextProcessingArray.Length;
                }
                if ((wrapMode != TextWrappingMode.NoWrap && wrapMode != TextWrappingMode.PreserveWhitespaceNoWrap) || generationSettings.overflowMode == TextOverflowMode.Truncate ||
                    generationSettings.overflowMode == TextOverflowMode.Ellipsis)
                {
                    bool saveWordWrap = false;
                    bool saveSoftBreak = false;
                    if ((isWhiteSpace || charCode == k_ZeroWidthSpace || charCode == k_HyphenMinus || charCode == k_SoftHyphen) && (!m_IsNonBreakingSpace | isLastBreakingChar) &&
                        charCode != k_NoBreakSpace && charCode != k_FigureSpace && charCode != k_NonBreakingHyphen && charCode != k_NarrowNoBreakSpace && charCode != k_WordJoiner)
                    {
                        if (charCode != k_HyphenMinus || m_CharacterCount <= 0 || !char.IsWhiteSpace((char)textInfo.textElementInfo[m_CharacterCount - 1].character))
                        {
                            isFirstWordOfLine = false;
                            saveWordWrap = true;
                            softBreakState.previousWordBreak = -1;
                        }
                    }
                    else if (!m_IsNonBreakingSpace && ((TextGeneratorUtilities.IsHangul(charCode) && !textSettings.lineBreakingRules.useModernHangulLineBreakingRules) || TextGeneratorUtilities.IsCJK(charCode)))
                    {
                        bool isLeading = textSettings.lineBreakingRules.leadingCharactersLookup.Contains(charCode);
                        bool nextIsFollowing = m_CharacterCount < totalCharacterCount - 1 && textSettings.lineBreakingRules.followingCharactersLookup.Contains(measured[m_CharacterCount + 1].character);
                        if (!isLeading)
                        {
                            if (!nextIsFollowing)
                            {
                                isFirstWordOfLine = false;
                                saveWordWrap = true;
                            }
                            if (isFirstWordOfLine)
                            {
                                if (isWhiteSpace) saveSoftBreak = true;
                                saveWordWrap = true;
                            }
                        }
                        else if (isFirstWordOfLine & isFirstOfLine)
                        {
                            if (isWhiteSpace) saveSoftBreak = true;
                            saveWordWrap = true;
                        }
                    }
                    else if (!m_IsNonBreakingSpace && m_CharacterCount + 1 < totalCharacterCount && TextGeneratorUtilities.IsCJK(textInfo.textElementInfo[m_CharacterCount + 1].character))
                    {
                        uint nextCharacter = textInfo.textElementInfo[m_CharacterCount + 1].character;
                        if (!textSettings.lineBreakingRules.leadingCharactersLookup.Contains(charCode) && !textSettings.lineBreakingRules.followingCharactersLookup.Contains(nextCharacter))
                            saveWordWrap = true;
                    }
                    else if (isFirstWordOfLine)
                    {
                        if ((isWhiteSpace && charCode != k_NoBreakSpace) || (charCode == k_SoftHyphen && !isSoftHyphenIgnored)) saveSoftBreak = true;
                        saveWordWrap = true;
                    }
                    if (saveWordWrap) SaveWordWrappingState(ref wrapState, i, m_CharacterCount, textInfo);
                    if (saveSoftBreak) SaveWordWrappingState(ref softBreakState, i, m_CharacterCount, textInfo);
                }
                m_CharacterCount++;
            }
            m_IsCalculatingPreferredValues = false;
            if (preferredWidth != 0f) preferredWidth = (float)(int)((double)preferredWidth * 100.0 + 1.0) / 100f;
            if (preferredHeight != 0f) preferredHeight = (float)(int)((double)preferredHeight * 100.0 + 1.0) / 100f;
            return new Vector2(preferredWidth, preferredHeight);
        }
    }
}
