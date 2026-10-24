using System;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    internal partial class TextGenerator
    {
        /// <summary>Resets the per-generation layout state (sizes, styles, stacks, line metrics).</summary>
        void BeginLayout(TextGenerationSettings generationSettings)
        {
            m_FontScaleMultiplier = 1f;
            m_CurrentFontSize = m_FontSize;
            m_SizeStack.SetDefault(m_CurrentFontSize);
            m_FontStyleInternal = generationSettings.fontStyle;
            m_FontWeightInternal = StartingWeight(generationSettings);
            m_FontWeightStack.SetDefault(m_FontWeightInternal);
            m_FontStyleStack.Clear();
            m_LineJustification = generationSettings.textAlignment;
            m_LineJustificationStack.SetDefault(m_LineJustification);
            m_BaselineOffset = 0f;
            m_BaselineOffsetStack.Clear();
            m_FXScale = Vector3.one;
            m_LineOffset = 0f;
            m_LineHeight = k_Unset;
            m_CSpacing = 0f;
            m_MonoSpacing = 0f;
            m_XAdvance = 0f;
            m_TagLineIndent = 0f;
            m_TagIndent = 0f;
            m_IndentStack.SetDefault(0f);
            m_TagNoParsing = false;
            m_CharacterCount = 0;
            m_FirstCharacterOfLine = 0;
            m_MaxLineAscender = k_Unset;
            m_MaxLineDescender = k_LargeFloat;
            m_LineNumber = 0;
            m_StartOfLineAscender = 0f;
            m_IsDrivenLineSpacing = false;
            m_LastBaseGlyphIndex = int.MinValue;
        }

        /// <summary>
        /// Places the characters along lines (TextCore's ParsingPhase): per character its scale,
        /// kerning, quad (padding, synthesized bold and italic), advance; lines break at the last
        /// word boundary when a character passes the rect's width, and overflow of its height is
        /// truncated or ended with an ellipsis as the overflow mode says.
        /// </summary>
        void LayoutCharacters(TextInfo textInfo, TextGenerationSettings generationSettings, out uint charCode, out float maxVisibleDescender)
        {
            var textSettings = generationSettings.textSettings;
            var fontAsset = generationSettings.fontAsset;
            m_CurrentMaterial = fontAsset.material;
            m_CurrentMaterialIndex = 0;
            m_MaterialReferenceStack.SetDefault(new MaterialReference(m_CurrentMaterialIndex, m_CurrentFontAsset, null, m_CurrentMaterial, m_Padding));
            int totalCharacterCount = m_TotalCharacterCount;
            float baseScale = (float)((double)m_FontSize / fontAsset.m_FaceInfo.pointSize * fontAsset.m_FaceInfo.scale);
            float elementScale = baseScale;
            float emScale = m_FontSize * 0.01f;
            charCode = 0;
            BeginLayout(generationSettings);
            float padding = 0f;
            m_FontColor32 = generationSettings.color;
            m_HtmlColor = m_FontColor32;
            m_UnderlineColor = m_HtmlColor;
            m_StrikethroughColor = m_HtmlColor;
            m_ColorStack.SetDefault(m_HtmlColor);
            m_UnderlineColorStack.SetDefault(m_HtmlColor);
            m_StrikethroughColorStack.SetDefault(m_HtmlColor);
            m_HighlightStateStack.SetDefault(new HighlightState(m_HtmlColor, Offset.zero));
            m_ItalicAngle = m_CurrentFontAsset.italicStyleSlant;
            m_ItalicAngleStack.SetDefault(m_ItalicAngle);
            m_ActionStack.Clear();
            m_FXRotation = Quaternion.identity;
            float lineGap = (float)((double)m_CurrentFontAsset.faceInfo.lineHeight - ((double)m_CurrentFontAsset.m_FaceInfo.ascentLine - m_CurrentFontAsset.m_FaceInfo.descentLine));
            m_LastCharacterOfLine = 0;
            m_FirstVisibleCharacterOfLine = 0;
            m_LastVisibleCharacterOfLine = 0;
            m_LineVisibleCharacterCount = 0;
            m_LineVisibleSpaceCount = 0;
            bool isFirstVisibleOfLinePending = true;
            m_FirstOverflowCharacterIndex = -1;
            bool kerning = TextGenerationSettings.fontFeatures.Contains(OTL_FeatureTag.kern);
            float areaWidth = m_MarginWidth > 0f ? m_MarginWidth : 0f;
            float areaHeight = m_MarginHeight > 0f ? m_MarginHeight : 0f;
            m_MarginLeft = 0f;
            m_MarginRight = 0f;
            m_Width = -1f;
            float lineWidth = LineWidth(areaWidth, m_MarginLeft, m_MarginRight);
            m_MeshExtents.min = TextGeneratorUtilities.largePositiveVector2;
            m_MeshExtents.max = TextGeneratorUtilities.largeNegativeVector2;
            textInfo.ClearLineInfo();
            m_MaxCapHeight = 0f;
            m_MaxAscender = 0f;
            m_MaxDescender = 0f;
            m_PageAscender = 0f;
            maxVisibleDescender = 0f;
            bool isMaxVisibleDescenderSet = false;
            bool isFirstWordOfLine = true;
            m_IsNonBreakingSpace = false;
            const bool isLastBreakingChar = false;
            int lastSoftLineBreak = 0;
            var substitution = new CharacterSubstitution(-1, 0);
            bool isSoftHyphenIgnored = false;
            var wrapMode = generationSettings.textWrappingMode;
            SaveWordWrappingState(ref m_SavedWordWrapState, -1, -1, textInfo);
            SaveWordWrappingState(ref m_SavedLineState, -1, -1, textInfo);
            SaveWordWrappingState(ref m_SavedEllipsisState, -1, -1, textInfo);
            SaveWordWrappingState(ref m_SavedLastValidState, -1, -1, textInfo);
            SaveWordWrappingState(ref m_SavedSoftLineBreakState, -1, -1, textInfo);
            m_EllipsisInsertionCandidateStack.Clear();
            m_IsTextTruncated = false;
            int ellipsisRestarts = 0;
            Vector3 topLeft = default, bottomLeft = default, topRight = default, bottomRight = default;

            for (int i = 0; i < m_TextProcessingArray.Length && m_TextProcessingArray[i].unicode != 0; i++)
            {
                charCode = m_TextProcessingArray[i].unicode;
                if (ellipsisRestarts > 5)
                {
                    Debug.LogError("Line breaking recursion max threshold hit... Character [" + charCode + "] index: " + i);
                    substitution.index = m_CharacterCount;
                    substitution.unicode = k_EndOfText;
                }
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
                        textInfo.textElementInfo[m_CharacterCount].textElement = m_CurrentFontAsset.characterLookupTable[k_EndOfText];
                        m_IsTextTruncated = true;
                    }
                    else if (charCode == k_Ellipsis)
                    {
                        ref var ellipsis = ref textInfo.textElementInfo[m_CharacterCount];
                        ellipsis.textElement = m_Ellipsis.character;
                        ellipsis.elementType = TextElementType.Character;
                        ellipsis.fontAsset = m_Ellipsis.fontAsset;
                        ellipsis.material = m_Ellipsis.material;
                        ellipsis.materialReferenceIndex = m_Ellipsis.materialIndex;
                        m_MaterialReferences[m_Underline.materialIndex].referenceCount++;
                        m_IsTextTruncated = true;
                        substitution.index = m_CharacterCount + 1;
                        substitution.unicode = k_EndOfText;
                    }
                }
                if (m_CharacterCount < 0 && charCode != k_EndOfText)
                {
                    ref var skipped = ref textInfo.textElementInfo[m_CharacterCount];
                    skipped.isVisible = false;
                    skipped.character = k_ZeroWidthSpace;
                    skipped.lineNumber = 0;
                    m_CharacterCount++;
                    continue;
                }

                // Case styles: small caps draw lower case letters as capitals at 80%.
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

                // Scale and vertical metrics of the element.
                float baselineShift = 0f;
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
                    // A line feed substituted after its line's first character keeps the previous size.
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
                    elementScale = (float)((double)fontScale * m_FontScaleMultiplier * m_CachedTextElement.m_Scale * m_CachedTextElement.m_Glyph.scale);
                    baselineShift = (float)((double)face.baseline * fontScale * m_FontScaleMultiplier * face.scale);
                    textInfo.textElementInfo[m_CharacterCount].elementType = TextElementType.Character;
                    textInfo.textElementInfo[m_CharacterCount].scale = elementScale;
                    padding = m_Padding;
                }
                float unhiddenScale = elementScale;
                if (charCode == k_SoftHyphen || charCode == k_EndOfText) elementScale = 0f;

                ref var info = ref textInfo.textElementInfo[m_CharacterCount];
                info.character = charCode;
                info.pointSize = m_CurrentFontSize;
                info.color = m_HtmlColor;
                info.underlineColor = m_UnderlineColor;
                info.strikethroughColor = m_StrikethroughColor;
                info.highlightState = m_HighlightState;
                info.style = m_FontStyleInternal;
                if (m_FontWeightInternal == TextFontWeight.Bold) info.style |= FontStyles.Bold;
                var metrics = info.alternativeGlyph?.metrics ?? m_CachedTextElement.m_Glyph.metrics;
                bool isWhiteSpace = charCode <= 0xFFFF && char.IsWhiteSpace((char)charCode);

                // Kerning with the next and the previous character.
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
                // Mark positioning needs GPOS mark records, which the font engine does not read yet.
                bool isBaseGlyph = TextGeneratorUtilities.IsBaseGlyph(charCode);
                if (isBaseGlyph) m_LastBaseGlyphIndex = m_CharacterCount;
                elementAscender += adjustment.yPlacement;
                elementDescender += adjustment.yPlacement;

                if (generationSettings.isRightToLeft)
                {
                    m_XAdvance = (float)((double)m_XAdvance - (double)metrics.horizontalAdvance * (1.0 - m_CharWidthAdjDelta) * elementScale);
                    if (isWhiteSpace || charCode == k_ZeroWidthSpace) m_XAdvance = (float)((double)m_XAdvance - (double)generationSettings.wordSpacing * emScale);
                }
                float monoAdvance = 0f;
                if (m_MonoSpacing != 0f && charCode != k_ZeroWidthSpace)
                {
                    double cell = m_DuoSpace && (charCode == '.' || charCode == ':' || charCode == ',') ? (double)m_MonoSpacing / 4.0 : (double)m_MonoSpacing / 2.0;
                    monoAdvance = (float)((cell - ((double)metrics.width / 2.0 + metrics.horizontalBearingX) * elementScale) * (1.0 - m_CharWidthAdjDelta));
                    m_XAdvance += monoAdvance;
                }

                // Synthesized bold: SDF atlases dilate the glyph (and the quad grows by the dilation);
                // bold advances by the asset's bold spacing.
                bool isSdf = m_CurrentFontAsset.atlasRenderMode != GlyphRenderMode.SMOOTH && m_CurrentFontAsset.atlasRenderMode != GlyphRenderMode.COLOR;
                float stylePadding;
                float boldSpacing;
                bool isSynthesizedBold = m_TextElementType == TextElementType.Character && !isUsingAlternateTypeface && (info.style & FontStyles.Bold) == FontStyles.Bold;
                if (isSdf)
                {
                    float gradientScale = generationSettings.isIMGUI && m_CurrentMaterial.HasFloat(TextShaderUtilities.ID_GradientScale)
                        ? m_CurrentMaterial.GetFloat(TextShaderUtilities.ID_GradientScale)
                        : (float)(m_CurrentFontAsset.atlasPadding + 1);
                    stylePadding = (float)((double)(isSynthesizedBold ? m_CurrentFontAsset.boldStyleWeight : m_CurrentFontAsset.m_RegularStyleWeight) / 4.0 * gradientScale);
                    if ((double)stylePadding + padding > gradientScale) padding = gradientScale - stylePadding;
                }
                else stylePadding = 0f;
                boldSpacing = isSynthesizedBold ? m_CurrentFontAsset.boldStyleSpacing : 0f;

                // The quad.
                topLeft.x = (float)((double)m_XAdvance + ((double)metrics.horizontalBearingX * m_FXScale.x - padding - stylePadding + adjustment.xPlacement) * elementScale * (1.0 - m_CharWidthAdjDelta));
                topLeft.y = (float)((double)baselineShift + ((double)metrics.horizontalBearingY + padding + adjustment.yPlacement) * elementScale - m_LineOffset + m_BaselineOffset);
                topLeft.z = 0f;
                bottomLeft.x = topLeft.x;
                bottomLeft.y = (float)((double)topLeft.y - ((double)metrics.height + (double)padding * 2.0) * elementScale);
                bottomLeft.z = 0f;
                topRight.x = (float)((double)bottomLeft.x + ((double)metrics.width * m_FXScale.x + (double)padding * 2.0 + (double)stylePadding * 2.0) * elementScale * (1.0 - m_CharWidthAdjDelta));
                topRight.y = topLeft.y;
                topRight.z = 0f;
                bottomRight.x = topRight.x;
                bottomRight.y = bottomLeft.y;
                bottomRight.z = 0f;
                if (charCode == k_ZeroWidthSpace)
                {
                    topLeft = Vector3.zero;
                    bottomLeft = Vector3.zero;
                    topRight = Vector3.zero;
                    bottomRight = Vector3.zero;
                }
                // Synthesized italic: a shear about the middle of the cap height.
                if (m_TextElementType == TextElementType.Character && !isUsingAlternateTypeface && (m_FontStyleInternal & FontStyles.Italic) == FontStyles.Italic)
                {
                    var face = m_CurrentFontAsset.m_FaceInfo;
                    float slant = (float)m_ItalicAngle * 0.01f;
                    float midline = (float)(((double)face.capLine - ((double)face.baseline + m_BaselineOffset)) / 2.0 * m_FontScaleMultiplier * face.scale);
                    var topShift = new Vector3((float)((double)slant * (((double)metrics.horizontalBearingY + padding + stylePadding - midline) * elementScale)), 0f, 0f);
                    var bottomShift = new Vector3((float)((double)slant * (((double)metrics.horizontalBearingY - metrics.height - padding - stylePadding - midline) * elementScale)), 0f, 0f);
                    topLeft += topShift;
                    bottomLeft += bottomShift;
                    topRight += topShift;
                    bottomRight += bottomShift;
                }
                if (m_FXRotation != Quaternion.identity)
                {
                    var rotation = Matrix4x4.Rotate(m_FXRotation);
                    var pivot = (topRight + bottomLeft) / 2f;
                    topLeft = rotation.MultiplyPoint3x4(topLeft - pivot) + pivot;
                    bottomLeft = rotation.MultiplyPoint3x4(bottomLeft - pivot) + pivot;
                    topRight = rotation.MultiplyPoint3x4(topRight - pivot) + pivot;
                    bottomRight = rotation.MultiplyPoint3x4(bottomRight - pivot) + pivot;
                }
                info.bottomLeft = bottomLeft;
                info.topLeft = topLeft;
                info.topRight = topRight;
                info.bottomRight = bottomRight;
                info.origin = (float)((double)m_XAdvance + (double)adjustment.xPlacement * elementScale);
                info.baseLine = (float)((double)baselineShift - m_LineOffset + m_BaselineOffset + (double)adjustment.yPlacement * elementScale);
                info.aspectRatio = (float)(((double)topRight.x - bottomLeft.x) / ((double)topLeft.y - bottomLeft.y));

                // Line metrics: spaces only count at the start of a line.
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
                if (m_LineOffset == 0f && (isFirstOfLine || !isWhiteSpace)) m_PageAscender = m_PageAscender > ascender ? m_PageAscender : ascender;

                info.isVisible = false;
                bool isPreserveWhitespace = wrapMode == TextWrappingMode.PreserveWhitespace || wrapMode == TextWrappingMode.PreserveWhitespaceNoWrap;
                bool drawn = charCode == k_Tab || (isPreserveWhitespace && (isWhiteSpace || charCode == k_ZeroWidthSpace)) ||
                    (!isWhiteSpace && charCode != k_ZeroWidthSpace && charCode != k_SoftHyphen && charCode != k_EndOfText) ||
                    (charCode == k_SoftHyphen && !isSoftHyphenIgnored);
                if (drawn)
                {
                    info.isVisible = true;
                    float marginLeft = m_MarginLeft;
                    float marginRight = m_MarginRight;
                    if (isInjected)
                    {
                        marginLeft = textInfo.lineInfo[m_LineNumber].marginLeft;
                        marginRight = textInfo.lineInfo[m_LineNumber].marginRight;
                    }
                    lineWidth = m_Width != -1f ? Mathf.Min(LineWidth(areaWidth, marginLeft, marginRight), m_Width) : LineWidth(areaWidth, marginLeft, marginRight);
                    float extent = (float)((double)Mathf.Abs(m_XAdvance) + (double)(!generationSettings.isRightToLeft ? metrics.horizontalAdvance : 0f) * (1.0 - m_CharWidthAdjDelta) * (charCode == k_SoftHyphen ? unhiddenScale : elementScale));
                    float textHeight = TextHeight();
                    int characterIndex = m_CharacterCount;

                    // The text is taller than the rect.
                    if (textHeight > (double)areaHeight + 0.0001f)
                    {
                        if (m_FirstOverflowCharacterIndex == -1) m_FirstOverflowCharacterIndex = m_CharacterCount;
                        switch (generationSettings.overflowMode)
                        {
                            case TextOverflowMode.Truncate:
                            case TextOverflowMode.Linked:
                                i = RestoreWordWrappingState(ref m_SavedLastValidState, textInfo);
                                substitution.index = characterIndex;
                                substitution.unicode = k_EndOfText;
                                continue;
                            case TextOverflowMode.Ellipsis when m_LineNumber > 0:
                                InsertEllipsis(ref i, ref substitution, ref ellipsisRestarts, textInfo);
                                continue;
                        }
                    }

                    // The character passes the line's width: break the line.
                    if (isBaseGlyph && extent > lineWidth)
                    {
                        if (wrapMode != TextWrappingMode.NoWrap && wrapMode != TextWrappingMode.PreserveWhitespaceNoWrap && m_CharacterCount != m_FirstCharacterOfLine)
                        {
                            i = RestoreWordWrappingState(ref m_SavedWordWrapState, textInfo);
                            float nextLineOffset;
                            if (m_LineHeight == k_Unset)
                            {
                                float nextAscender = textInfo.textElementInfo[m_CharacterCount].adjustedAscender;
                                nextLineOffset = (float)((m_LineOffset > 0f && !m_IsDrivenLineSpacing ? (double)m_MaxLineAscender - m_StartOfLineAscender : 0.0) - m_MaxLineDescender + nextAscender + ((double)lineGap + m_LineSpacingDelta) * baseScale + 0.0 * emScale);
                            }
                            else
                            {
                                nextLineOffset = (float)((double)m_LineHeight + 0.0 * emScale);
                                m_IsDrivenLineSpacing = true;
                            }
                            float nextTextHeight = (float)((double)m_MaxAscender + nextLineOffset + m_LineOffset - textInfo.textElementInfo[m_CharacterCount].adjustedDescender);
                            if (textInfo.textElementInfo[m_CharacterCount - 1].character == k_SoftHyphen && !isSoftHyphenIgnored &&
                                (generationSettings.overflowMode == TextOverflowMode.Overflow || nextTextHeight < (double)areaHeight + 0.0001f))
                            {
                                // Break at the soft hyphen, which is drawn as a hyphen.
                                substitution.index = m_CharacterCount - 1;
                                substitution.unicode = k_HyphenMinus;
                                i--;
                                m_CharacterCount--;
                                continue;
                            }
                            isSoftHyphenIgnored = false;
                            if (textInfo.textElementInfo[m_CharacterCount].character == k_SoftHyphen)
                            {
                                isSoftHyphenIgnored = true;
                                continue;
                            }
                            int previousWordBreak = m_SavedSoftLineBreakState.previousWordBreak;
                            if (isFirstWordOfLine && previousWordBreak != -1 && previousWordBreak != lastSoftLineBreak)
                            {
                                i = RestoreWordWrappingState(ref m_SavedSoftLineBreakState, textInfo);
                                lastSoftLineBreak = previousWordBreak;
                                if (textInfo.textElementInfo[m_CharacterCount - 1].character == k_SoftHyphen)
                                {
                                    substitution.index = m_CharacterCount - 1;
                                    substitution.unicode = k_HyphenMinus;
                                    i--;
                                    m_CharacterCount--;
                                    continue;
                                }
                            }
                            if (!(nextTextHeight > (double)areaHeight + 0.0001f))
                            {
                                InsertNewLine(i, baseScale, elementScale, emScale, boldSpacing, characterSpacing, lineWidth, lineGap, ref isMaxVisibleDescenderSet, ref maxVisibleDescender, generationSettings, textInfo);
                                isFirstVisibleOfLinePending = true;
                                isFirstWordOfLine = true;
                                continue;
                            }
                            if (m_FirstOverflowCharacterIndex == -1) m_FirstOverflowCharacterIndex = m_CharacterCount;
                            switch (generationSettings.overflowMode)
                            {
                                case TextOverflowMode.Overflow:
                                case TextOverflowMode.Masking:
                                case TextOverflowMode.ScrollRect:
                                    InsertNewLine(i, baseScale, elementScale, emScale, boldSpacing, characterSpacing, lineWidth, lineGap, ref isMaxVisibleDescenderSet, ref maxVisibleDescender, generationSettings, textInfo);
                                    isFirstVisibleOfLinePending = true;
                                    isFirstWordOfLine = true;
                                    continue;
                                case TextOverflowMode.Truncate:
                                    i = RestoreWordWrappingState(ref m_SavedLastValidState, textInfo);
                                    substitution.index = characterIndex;
                                    substitution.unicode = k_EndOfText;
                                    continue;
                                case TextOverflowMode.Ellipsis:
                                    InsertEllipsis(ref i, ref substitution, ref ellipsisRestarts, textInfo);
                                    continue;
                                case TextOverflowMode.Linked:
                                    substitution.index = m_CharacterCount;
                                    substitution.unicode = k_EndOfText;
                                    continue;
                            }
                        }
                        else
                        {
                            switch (generationSettings.overflowMode)
                            {
                                case TextOverflowMode.Truncate:
                                    i = RestoreWordWrappingState(ref m_SavedWordWrapState, textInfo);
                                    substitution.index = characterIndex;
                                    substitution.unicode = k_EndOfText;
                                    continue;
                                case TextOverflowMode.Ellipsis:
                                    InsertEllipsis(ref i, ref substitution, ref ellipsisRestarts, textInfo);
                                    continue;
                                case TextOverflowMode.Linked:
                                    i = RestoreWordWrappingState(ref m_SavedWordWrapState, textInfo);
                                    substitution.index = m_CharacterCount;
                                    substitution.unicode = k_EndOfText;
                                    continue;
                            }
                        }
                    }

                    if (isWhiteSpace)
                    {
                        info.isVisible = false;
                        m_LineVisibleSpaceCount = ++textInfo.lineInfo[m_LineNumber].spaceCount;
                        textInfo.lineInfo[m_LineNumber].marginLeft = marginLeft;
                        textInfo.lineInfo[m_LineNumber].marginRight = marginRight;
                        textInfo.spaceCount++;
                        if (charCode == k_NoBreakSpace) textInfo.lineInfo[m_LineNumber].controlCharacterCount++;
                    }
                    else if (charCode == k_SoftHyphen)
                    {
                        info.isVisible = false;
                    }
                    else
                    {
                        if (m_TextElementType == TextElementType.Character) SaveGlyphVertexInfo(padding, stylePadding, m_HtmlColor, generationSettings, textInfo);
                        if (isFirstVisibleOfLinePending)
                        {
                            isFirstVisibleOfLinePending = false;
                            m_FirstVisibleCharacterOfLine = m_CharacterCount;
                        }
                        m_LineVisibleCharacterCount++;
                        m_LastVisibleCharacterOfLine = m_CharacterCount;
                        textInfo.lineInfo[m_LineNumber].marginLeft = marginLeft;
                        textInfo.lineInfo[m_LineNumber].marginRight = marginRight;
                    }
                }
                else
                {
                    if (generationSettings.overflowMode == TextOverflowMode.Linked && (charCode == k_LineFeed || charCode == k_VerticalTab))
                    {
                        float textHeight = TextHeight();
                        int characterIndex = m_CharacterCount;
                        if (textHeight > (double)areaHeight + 0.0001f)
                        {
                            if (m_FirstOverflowCharacterIndex == -1) m_FirstOverflowCharacterIndex = m_CharacterCount;
                            i = RestoreWordWrappingState(ref m_SavedLastValidState, textInfo);
                            substitution.index = characterIndex;
                            substitution.unicode = k_EndOfText;
                            continue;
                        }
                    }
                    if ((charCode == k_LineFeed || charCode == k_VerticalTab || charCode == k_NoBreakSpace || charCode == k_FigureSpace || charCode == k_LineSeparator ||
                         charCode == k_ParagraphSeparator || char.IsSeparator((char)charCode)) && charCode != k_SoftHyphen && charCode != k_ZeroWidthSpace && charCode != k_WordJoiner)
                    {
                        textInfo.lineInfo[m_LineNumber].spaceCount++;
                        textInfo.spaceCount++;
                    }
                    if (charCode == k_NoBreakSpace) textInfo.lineInfo[m_LineNumber].controlCharacterCount++;
                }

                // Where an ellipsis could replace the rest of the text.
                if (generationSettings.overflowMode == TextOverflowMode.Ellipsis && (!isInjected || charCode == k_HyphenMinus))
                {
                    var ellipsisFace = m_Ellipsis.fontAsset.m_FaceInfo;
                    float ellipsisFontScale = (float)((double)m_CurrentFontSize / ellipsisFace.pointSize * ellipsisFace.scale);
                    float ellipsisScale = (float)((double)ellipsisFontScale * m_FontScaleMultiplier * m_Ellipsis.character.m_Scale * m_Ellipsis.character.m_Glyph.scale);
                    float marginLeft = m_MarginLeft;
                    float marginRight = m_MarginRight;
                    if (charCode == k_LineFeed && m_CharacterCount != m_FirstCharacterOfLine)
                    {
                        ellipsisFontScale = (float)((double)textInfo.textElementInfo[m_CharacterCount - 1].pointSize / ellipsisFace.pointSize * ellipsisFace.scale);
                        ellipsisScale = (float)((double)ellipsisFontScale * m_FontScaleMultiplier * m_Ellipsis.character.m_Scale * m_Ellipsis.character.m_Glyph.scale);
                        marginLeft = textInfo.lineInfo[m_LineNumber].marginLeft;
                        marginRight = textInfo.lineInfo[m_LineNumber].marginRight;
                    }
                    float withEllipsis = (float)((double)Mathf.Abs(m_XAdvance) + (double)(!generationSettings.isRightToLeft ? m_Ellipsis.character.m_Glyph.metrics.horizontalAdvance : 0f) * (1.0 - m_CharWidthAdjDelta) * ellipsisScale);
                    float availableWidth = m_Width != -1f ? Mathf.Min(LineWidth(areaWidth, marginLeft, marginRight), m_Width) : LineWidth(areaWidth, marginLeft, marginRight);
                    if (withEllipsis < availableWidth)
                    {
                        SaveWordWrappingState(ref m_SavedEllipsisState, i, m_CharacterCount, textInfo);
                        m_EllipsisInsertionCandidateStack.Push(m_SavedEllipsisState);
                    }
                }

                info.lineNumber = m_LineNumber;
                if ((charCode != k_LineFeed && charCode != k_VerticalTab && charCode != k_CarriageReturn && !isInjected) || textInfo.lineInfo[m_LineNumber].characterCount == 1)
                    textInfo.lineInfo[m_LineNumber].alignment = m_LineJustification;

                // Advance.
                if (charCode != k_ZeroWidthSpace)
                {
                    if (charCode == k_Tab)
                    {
                        float tab = (float)((double)m_CurrentFontAsset.m_FaceInfo.tabWidth * (float)(int)m_CurrentFontAsset.tabMultiple * elementScale);
                        float nextStop = (float)((double)Mathf.Ceil(m_XAdvance / tab) * tab);
                        m_XAdvance = nextStop > m_XAdvance ? nextStop : m_XAdvance + tab;
                    }
                    else if (m_MonoSpacing != 0f)
                    {
                        float cell = (float)(m_DuoSpace && (charCode == '.' || charCode == ':' || charCode == ',') ? (double)m_MonoSpacing / 2.0 - monoAdvance : (double)m_MonoSpacing - monoAdvance);
                        m_XAdvance = (float)((double)m_XAdvance + ((double)cell + ((double)m_CurrentFontAsset.regularStyleSpacing + characterSpacing) * emScale + m_CSpacing) * (1.0 - m_CharWidthAdjDelta));
                        if (isWhiteSpace || charCode == k_ZeroWidthSpace) m_XAdvance = (float)((double)m_XAdvance + (double)generationSettings.wordSpacing * emScale);
                    }
                    else if (generationSettings.isRightToLeft)
                    {
                        m_XAdvance = (float)((double)m_XAdvance - ((double)adjustment.xAdvance * elementScale + ((double)m_CurrentFontAsset.regularStyleSpacing + characterSpacing + boldSpacing) * emScale + m_CSpacing) * (1.0 - m_CharWidthAdjDelta));
                        if (isWhiteSpace || charCode == k_ZeroWidthSpace) m_XAdvance = (float)((double)m_XAdvance - (double)generationSettings.wordSpacing * emScale);
                    }
                    else
                    {
                        m_XAdvance = (float)((double)m_XAdvance + (((double)metrics.horizontalAdvance * m_FXScale.x + adjustment.xAdvance) * elementScale + ((double)m_CurrentFontAsset.regularStyleSpacing + characterSpacing + boldSpacing) * emScale + m_CSpacing) * (1.0 - m_CharWidthAdjDelta));
                        if (isWhiteSpace || charCode == k_ZeroWidthSpace) m_XAdvance = (float)((double)m_XAdvance + (double)generationSettings.wordSpacing * emScale);
                    }
                }
                info.xAdvance = m_XAdvance;
                if (charCode == k_CarriageReturn) m_XAdvance = 0f + m_TagIndent;

                // The end of a line (hard break, substituted hyphen, end of text).
                if (charCode == k_LineFeed || charCode == k_VerticalTab || charCode == k_EndOfText || charCode == k_LineSeparator ||
                    (charCode == k_HyphenMinus && isInjected) || m_CharacterCount == totalCharacterCount - 1)
                {
                    float ascenderDelta = m_MaxLineAscender - m_StartOfLineAscender;
                    if (m_LineOffset > 0f && Math.Abs(ascenderDelta) > 0.01f && !m_IsDrivenLineSpacing)
                    {
                        TextGeneratorUtilities.AdjustLineOffset(m_FirstCharacterOfLine, m_CharacterCount, ascenderDelta, textInfo);
                        m_MaxDescender -= ascenderDelta;
                        m_LineOffset += ascenderDelta;
                        if (m_SavedEllipsisState.lineNumber == m_LineNumber)
                        {
                            m_SavedEllipsisState = m_EllipsisInsertionCandidateStack.Pop();
                            m_SavedEllipsisState.startOfLineAscender += ascenderDelta;
                            m_SavedEllipsisState.lineOffset += ascenderDelta;
                            m_EllipsisInsertionCandidateStack.Push(m_SavedEllipsisState);
                        }
                    }
                    float lineAscender = m_MaxLineAscender - m_LineOffset;
                    float lineDescender = m_MaxLineDescender - m_LineOffset;
                    m_MaxDescender = m_MaxDescender < lineDescender ? m_MaxDescender : lineDescender;
                    if (!isMaxVisibleDescenderSet) maxVisibleDescender = m_MaxDescender;
                    ref var line = ref textInfo.lineInfo[m_LineNumber];
                    line.firstCharacterIndex = m_FirstCharacterOfLine;
                    line.firstVisibleCharacterIndex = m_FirstVisibleCharacterOfLine = m_FirstCharacterOfLine > m_FirstVisibleCharacterOfLine ? m_FirstCharacterOfLine : m_FirstVisibleCharacterOfLine;
                    line.lastCharacterIndex = m_LastCharacterOfLine = m_CharacterCount;
                    line.lastVisibleCharacterIndex = m_LastVisibleCharacterOfLine = m_LastVisibleCharacterOfLine < m_FirstVisibleCharacterOfLine ? m_FirstVisibleCharacterOfLine : m_LastVisibleCharacterOfLine;
                    int firstExtent = m_FirstVisibleCharacterOfLine;
                    int lastExtent = m_LastVisibleCharacterOfLine;
                    if ((generationSettings.textWrappingMode == TextWrappingMode.PreserveWhitespace || generationSettings.textWrappingMode == TextWrappingMode.PreserveWhitespaceNoWrap) &&
                        textInfo.textElementInfo[m_LastCharacterOfLine].xAdvance != 0f)
                    {
                        firstExtent = m_FirstCharacterOfLine;
                        lastExtent = m_LastCharacterOfLine;
                    }
                    line.characterCount = line.lastCharacterIndex - line.firstCharacterIndex + 1;
                    line.visibleCharacterCount = m_LineVisibleCharacterCount;
                    line.visibleSpaceCount = line.lastVisibleCharacterIndex + 1 - line.firstCharacterIndex - m_LineVisibleCharacterCount;
                    line.lineExtents.min = new Vector2(textInfo.textElementInfo[firstExtent].bottomLeft.x, lineDescender);
                    line.lineExtents.max = new Vector2(textInfo.textElementInfo[lastExtent].topRight.x, lineAscender);
                    line.length = generationSettings.isIMGUI ? textInfo.textElementInfo[lastExtent].xAdvance : (float)((double)line.lineExtents.max.x - (double)padding * elementScale);
                    line.width = lineWidth;
                    if (line.characterCount == 1) line.alignment = m_LineJustification;
                    float trailingSpacing = (float)((((double)m_CurrentFontAsset.regularStyleSpacing + characterSpacing + boldSpacing) * emScale + m_CSpacing) * (1.0 - m_CharWidthAdjDelta));
                    int advanceSource = textInfo.textElementInfo[m_LastVisibleCharacterOfLine].isVisible ? m_LastVisibleCharacterOfLine : m_LastCharacterOfLine;
                    line.maxAdvance = (float)((double)textInfo.textElementInfo[advanceSource].xAdvance + (generationSettings.isRightToLeft ? (double)trailingSpacing : 0.0 - trailingSpacing));
                    line.baseline = 0f - m_LineOffset;
                    line.ascender = lineAscender;
                    line.descender = lineDescender;
                    line.lineHeight = (float)((double)lineAscender - lineDescender + (double)lineGap * baseScale);
                    if (charCode == k_LineFeed || charCode == k_VerticalTab || charCode == k_HyphenMinus || charCode == k_LineSeparator || charCode == k_ParagraphSeparator)
                    {
                        SaveWordWrappingState(ref m_SavedLineState, i, m_CharacterCount, textInfo);
                        m_LineNumber++;
                        isFirstVisibleOfLinePending = true;
                        isFirstWordOfLine = true;
                        m_FirstCharacterOfLine = m_CharacterCount + 1;
                        m_LineVisibleCharacterCount = 0;
                        m_LineVisibleSpaceCount = 0;
                        if (m_LineNumber >= textInfo.lineInfo.Length) TextGeneratorUtilities.ResizeLineExtents(m_LineNumber, textInfo);
                        float nextAscender = textInfo.textElementInfo[m_CharacterCount].adjustedAscender;
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
                        SaveWordWrappingState(ref m_SavedWordWrapState, i, m_CharacterCount, textInfo);
                        SaveWordWrappingState(ref m_SavedLastValidState, i, m_CharacterCount, textInfo);
                        m_CharacterCount++;
                        continue;
                    }
                    if (charCode == k_EndOfText) i = m_TextProcessingArray.Length;
                }

                if (info.isVisible)
                {
                    m_MeshExtents.min.x = Mathf.Min(m_MeshExtents.min.x, info.bottomLeft.x);
                    m_MeshExtents.min.y = Mathf.Min(m_MeshExtents.min.y, info.bottomLeft.y);
                    m_MeshExtents.max.x = Mathf.Max(m_MeshExtents.max.x, info.topRight.x);
                    m_MeshExtents.max.y = Mathf.Max(m_MeshExtents.max.y, info.topRight.y);
                }

                // Where the line may break after this character.
                if ((wrapMode != TextWrappingMode.NoWrap && wrapMode != TextWrappingMode.PreserveWhitespaceNoWrap) || generationSettings.overflowMode == TextOverflowMode.Truncate ||
                    generationSettings.overflowMode == TextOverflowMode.Ellipsis || generationSettings.overflowMode == TextOverflowMode.Linked)
                {
                    bool saveWordWrap = false;
                    bool saveSoftBreak = false;
                    bool hyphenAfterSpace = charCode == k_HyphenMinus && m_CharacterCount > 0 && char.IsWhiteSpace((char)textInfo.textElementInfo[m_CharacterCount - 1].character);
                    if ((isWhiteSpace || charCode == k_ZeroWidthSpace || (charCode == k_HyphenMinus && !hyphenAfterSpace) || charCode == k_SoftHyphen) &&
                        (!m_IsNonBreakingSpace | isLastBreakingChar) && charCode != k_NoBreakSpace && charCode != k_FigureSpace && charCode != k_NonBreakingHyphen &&
                        charCode != k_NarrowNoBreakSpace && charCode != k_WordJoiner)
                    {
                        isFirstWordOfLine = false;
                        saveWordWrap = true;
                        m_SavedSoftLineBreakState.previousWordBreak = -1;
                    }
                    else if (!m_IsNonBreakingSpace && ((TextGeneratorUtilities.IsHangul(charCode) && !textSettings.lineBreakingRules.useModernHangulLineBreakingRules) || TextGeneratorUtilities.IsCJK(charCode)))
                    {
                        bool isLeading = textSettings.lineBreakingRules.leadingCharactersLookup.Contains(charCode);
                        bool nextIsFollowing = m_CharacterCount < totalCharacterCount - 1 && textSettings.lineBreakingRules.followingCharactersLookup.Contains(textInfo.textElementInfo[m_CharacterCount + 1].character);
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
                    if (saveWordWrap) SaveWordWrappingState(ref m_SavedWordWrapState, i, m_CharacterCount, textInfo);
                    if (saveSoftBreak) SaveWordWrappingState(ref m_SavedSoftLineBreakState, i, m_CharacterCount, textInfo);
                }
                SaveWordWrappingState(ref m_SavedLastValidState, i, m_CharacterCount, textInfo);
                m_CharacterCount++;
            }
            CloseAllLinkTags(textInfo);
        }

        /// <summary>The width a line may take (Unity adds 0.0001 against rounding).</summary>
        static float LineWidth(float area, float marginLeft, float marginRight) => (float)((double)area + 0.0001f - marginLeft - marginRight);

        /// <summary>The height of the lines so far: the first line's ascender down to the current
        /// line's descender (a taller start of line pushes it down).</summary>
        float TextHeight() =>
            (float)((double)m_MaxAscender - ((double)m_MaxLineDescender - m_LineOffset) + (m_LineOffset > 0f && !m_IsDrivenLineSpacing ? (double)m_MaxLineAscender - m_StartOfLineAscender : 0.0));

        /// <summary>Ends the text with an ellipsis at the last place one fits; from the start of
        /// the text when none does (the text is then empty).</summary>
        void InsertEllipsis(ref int i, ref CharacterSubstitution substitution, ref int restarts, TextInfo textInfo)
        {
            if (m_EllipsisInsertionCandidateStack.Count == 0)
            {
                i = -1;
                m_CharacterCount = 0;
                substitution.index = 0;
                substitution.unicode = k_EndOfText;
                m_FirstCharacterOfLine = 0;
                return;
            }
            var state = m_EllipsisInsertionCandidateStack.Pop();
            i = RestoreWordWrappingState(ref state, textInfo);
            i--;
            m_CharacterCount--;
            substitution.index = m_CharacterCount;
            substitution.unicode = k_Ellipsis;
            restarts++;
        }

        /// <summary>Ends the current line before the character at <see cref="m_CharacterCount"/>
        /// (a soft break) and starts the next one.</summary>
        void InsertNewLine(int i, float baseScale, float currentElementScale, float currentEmScale, float boldSpacingAdjustment, float characterSpacingAdjustment, float width, float lineGap,
            ref bool isMaxVisibleDescenderSet, ref float maxVisibleDescender, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            float ascenderDelta = m_MaxLineAscender - m_StartOfLineAscender;
            if (m_LineOffset > 0f && Math.Abs(ascenderDelta) > 0.01f && !m_IsDrivenLineSpacing)
            {
                TextGeneratorUtilities.AdjustLineOffset(m_FirstCharacterOfLine, m_CharacterCount, ascenderDelta, textInfo);
                m_MaxDescender -= ascenderDelta;
                m_LineOffset += ascenderDelta;
            }
            float lineAscender = m_MaxLineAscender - m_LineOffset;
            float lineDescender = m_MaxLineDescender - m_LineOffset;
            m_MaxDescender = m_MaxDescender < lineDescender ? m_MaxDescender : lineDescender;
            if (!isMaxVisibleDescenderSet) maxVisibleDescender = m_MaxDescender;
            ref var line = ref textInfo.lineInfo[m_LineNumber];
            line.firstCharacterIndex = m_FirstCharacterOfLine;
            line.firstVisibleCharacterIndex = m_FirstVisibleCharacterOfLine = m_FirstCharacterOfLine > m_FirstVisibleCharacterOfLine ? m_FirstCharacterOfLine : m_FirstVisibleCharacterOfLine;
            line.lastCharacterIndex = m_LastCharacterOfLine = m_CharacterCount - 1 > 0 ? m_CharacterCount - 1 : 0;
            line.lastVisibleCharacterIndex = m_LastVisibleCharacterOfLine = m_LastVisibleCharacterOfLine < m_FirstVisibleCharacterOfLine ? m_FirstVisibleCharacterOfLine : m_LastVisibleCharacterOfLine;
            line.characterCount = line.lastCharacterIndex - line.firstCharacterIndex + 1;
            line.visibleCharacterCount = m_LineVisibleCharacterCount;
            line.visibleSpaceCount = line.lastVisibleCharacterIndex + 1 - line.firstCharacterIndex - m_LineVisibleCharacterCount;
            line.lineExtents.min = new Vector2(textInfo.textElementInfo[m_FirstVisibleCharacterOfLine].bottomLeft.x, lineDescender);
            line.lineExtents.max = new Vector2(textInfo.textElementInfo[m_LastVisibleCharacterOfLine].topRight.x, lineAscender);
            line.length = generationSettings.isIMGUI ? textInfo.textElementInfo[m_LastVisibleCharacterOfLine].xAdvance : line.lineExtents.max.x;
            line.width = width;
            // The line's advance ends at its last visible character, without its trailing spacing.
            float kerningAdvance = textInfo.textElementInfo[m_LastVisibleCharacterOfLine].adjustedHorizontalAdvance;
            float trailing = (float)(((double)kerningAdvance * currentElementScale + ((double)m_CurrentFontAsset.regularStyleSpacing + characterSpacingAdjustment + boldSpacingAdjustment) * currentEmScale + m_CSpacing) * 1.0);
            float maxAdvance = line.maxAdvance = (float)((double)textInfo.textElementInfo[m_LastVisibleCharacterOfLine].xAdvance + (generationSettings.isRightToLeft ? (double)trailing : 0.0 - trailing));
            textInfo.textElementInfo[m_LastVisibleCharacterOfLine].xAdvance = maxAdvance;
            line.baseline = 0f - m_LineOffset;
            line.ascender = lineAscender;
            line.descender = lineDescender;
            line.lineHeight = (float)((double)lineAscender - lineDescender + (double)lineGap * baseScale);
            m_FirstCharacterOfLine = m_CharacterCount;
            m_LineVisibleCharacterCount = 0;
            m_LineVisibleSpaceCount = 0;
            SaveWordWrappingState(ref m_SavedLineState, i, m_CharacterCount - 1, textInfo);
            m_LineNumber++;
            if (m_LineNumber >= textInfo.lineInfo.Length) TextGeneratorUtilities.ResizeLineExtents(m_LineNumber, textInfo);
            if (m_LineHeight == k_Unset)
            {
                float nextAscender = textInfo.textElementInfo[m_CharacterCount].adjustedAscender;
                float lineDelta = (float)(0.0 - m_MaxLineDescender + nextAscender + ((double)lineGap + m_LineSpacingDelta) * baseScale + 0.0 * currentEmScale);
                m_LineOffset += lineDelta;
                m_StartOfLineAscender = nextAscender;
            }
            else
            {
                m_LineOffset = (float)((double)m_LineOffset + ((double)m_LineHeight + 0.0 * currentEmScale));
            }
            m_MaxLineAscender = k_Unset;
            m_MaxLineDescender = k_LargeFloat;
            m_XAdvance = 0f + m_TagIndent;
        }

        /// <summary>A visible character's vertices before placement: its quad, color (white on
        /// color atlases, no more opaque than the text) and atlas uvs around the glyph's padding.</summary>
        void SaveGlyphVertexInfo(float padding, float stylePadding, Color32 vertexColor, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            ref var info = ref textInfo.textElementInfo[m_CharacterCount];
            info.vertexBottomLeft.position = info.bottomLeft;
            info.vertexTopLeft.position = info.topLeft;
            info.vertexTopRight.position = info.topRight;
            info.vertexBottomRight.position = info.bottomRight;
            vertexColor.a = m_FontColor32.a < vertexColor.a ? m_FontColor32.a : vertexColor.a;
            bool isColorAtlas = ((int)m_CurrentFontAsset.m_AtlasRenderMode & 0x10000) == 0x10000;
            if (isColorAtlas) vertexColor = new Color32(255, 255, 255, vertexColor.a);
            info.vertexBottomLeft.color = vertexColor;
            info.vertexTopLeft.color = vertexColor;
            info.vertexTopRight.color = vertexColor;
            info.vertexBottomRight.color = vertexColor;
            var rect = info.alternativeGlyph?.glyphRect ?? m_CachedTextElement.m_Glyph.glyphRect;
            float width = m_CurrentFontAsset.atlasWidth;
            float height = m_CurrentFontAsset.atlasHeight;
            // The style padding does not widen the uvs.
            float left = (float)(((double)rect.x - padding - 0f) / width);
            float bottom = (float)(((double)rect.y - padding - 0f) / height);
            float top = (float)(((double)rect.y + padding + 0f + (float)rect.height) / height);
            float right = (float)(((double)rect.x + padding + 0f + (float)rect.width) / width);
            info.vertexBottomLeft.uv = new Vector4(left, bottom, 0f, 0f);
            info.vertexTopLeft.uv = new Vector4(left, top, 0f, 0f);
            info.vertexTopRight.uv = new Vector4(right, top, 0f, 0f);
            info.vertexBottomRight.uv = new Vector4(right, bottom, 0f, 0f);
        }
    }
}
