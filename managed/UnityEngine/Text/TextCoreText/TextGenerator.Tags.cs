using System;

namespace UnityEngine.TextCore.Text
{
    internal partial class TextGenerator
    {
        const int k_MaxTagLength = 256;

        /// <summary>The characters of the tag being parsed (without the brackets), zero terminated.</summary>
        char[] m_HtmlTag = new char[k_MaxTagLength];
        RichTextTagAttribute[] m_XmlAttribute = new RichTextTagAttribute[8];
        float[] m_AttributeParameterValues = new float[16];

        /// <summary>
        /// Parses the tag after a '&lt;' at <paramref name="startIndex"/> and applies it to the
        /// layout state. A tag is <c>name[=value][ name=value]...</c>; values are numbers (with a
        /// px, em or % unit), #colors or strings (quoted or not), hashed case-insensitively. False
        /// when it is not a supported, well-formed tag: its characters are then laid out as text.
        /// <paramref name="isThreadSuccess"/> is false when the tag needs the main thread.
        /// </summary>
        bool ValidateHtmlTag(TextProcessingElement[] chars, int startIndex, out int endIndex, TextGenerationSettings generationSettings, TextInfo textInfo, out bool isThreadSuccess)
        {
            isThreadSuccess = true;
            endIndex = startIndex;
            if (!ReadTag(chars, startIndex, ref endIndex, out int tagLength, out var unit)) return false;
            int name = m_XmlAttribute[0].nameHashCode;
            if (m_TagNoParsing && name != (int)MarkupTag.SLASH_NO_PARSE) return false;
            if (name == (int)MarkupTag.SLASH_NO_PARSE)
            {
                m_TagNoParsing = false;
                return true;
            }
            if (m_HtmlTag[0] == '#' && (tagLength == 4 || tagLength == 5 || tagLength == 7 || tagLength == 9))
            {
                m_HtmlColor = TextGeneratorUtilities.HexCharsToColor(m_HtmlTag, 0, tagLength);
                m_ColorStack.Add(m_HtmlColor);
                return true;
            }
            return ApplyTag((MarkupTag)name, tagLength, unit, startIndex, generationSettings, textInfo, ref isThreadSuccess);
        }

        /// <summary>Reads a tag's characters and attributes up to its '&gt;'; returns the unit of
        /// its last numeric value.</summary>
        bool ReadTag(TextProcessingElement[] chars, int startIndex, ref int endIndex, out int count, out TagUnitType unit)
        {
            Array.Clear(m_XmlAttribute, 0, m_XmlAttribute.Length);
            count = 0;
            unit = TagUnitType.Pixels;
            int attribute = 0;
            // 0: reading a name, 1: reading a value, 2: after a value.
            int stage = 0;
            var valueType = TagValueType.None;
            bool sawSpace = false, closed = false, singleQuoted = false, doubleQuoted = false;
            for (int i = startIndex; i < chars.Length && chars[i].unicode != 0; i++)
            {
                if (count >= m_HtmlTag.Length) break;
                uint c = chars[i].unicode;
                if (c == '<') break;
                if (c == '>')
                {
                    closed = true;
                    endIndex = i;
                    m_HtmlTag[count] = '\0';
                    break;
                }
                m_HtmlTag[count++] = (char)c;
                ref var current = ref m_XmlAttribute[attribute];
                if (stage == 1)
                {
                    switch (valueType)
                    {
                        case TagValueType.None:
                            unit = TagUnitType.Pixels;
                            if (c == '+' || c == '-' || c == '.' || (c >= '0' && c <= '9'))
                            {
                                valueType = current.valueType = TagValueType.NumericalValue;
                                current.valueStartIndex = count - 1;
                                current.valueLength++;
                            }
                            else if (c == '#')
                            {
                                valueType = current.valueType = TagValueType.ColorValue;
                                current.valueStartIndex = count - 1;
                                current.valueLength++;
                            }
                            else if (c == '\'' || c == '"')
                            {
                                valueType = current.valueType = TagValueType.StringValue;
                                current.valueStartIndex = count;
                                if (c == '\'') singleQuoted = true;
                                else doubleQuoted = true;
                            }
                            else
                            {
                                valueType = current.valueType = TagValueType.StringValue;
                                current.valueStartIndex = count - 1;
                                current.valueHashCode = Hash(current.valueHashCode, c);
                                current.valueLength++;
                            }
                            break;
                        case TagValueType.NumericalValue:
                            if (c == 'p' || c == 'e' || c == '%' || c == ' ')
                            {
                                stage = 2;
                                valueType = TagValueType.None;
                                unit = current.unitType = c == 'e' ? TagUnitType.FontUnits : c == '%' ? TagUnitType.Percentage : TagUnitType.Pixels;
                                NextAttribute(ref attribute, false);
                            }
                            else current.valueLength++;
                            break;
                        case TagValueType.ColorValue:
                            if (c != ' ')
                            {
                                current.valueLength++;
                                break;
                            }
                            stage = 2;
                            valueType = TagValueType.None;
                            unit = TagUnitType.Pixels;
                            NextAttribute(ref attribute, false);
                            break;
                        case TagValueType.StringValue:
                            if ((!doubleQuoted || c != '"') && (!singleQuoted || c != '\''))
                            {
                                current.valueHashCode = Hash(current.valueHashCode, c);
                                current.valueLength++;
                                break;
                            }
                            stage = 2;
                            valueType = TagValueType.None;
                            unit = TagUnitType.Pixels;
                            NextAttribute(ref attribute, true);
                            break;
                    }
                }
                if (c == '=') stage = 1;
                if (stage == 0 && c == ' ')
                {
                    if (sawSpace) return false;
                    sawSpace = true;
                    stage = 2;
                    valueType = TagValueType.None;
                    unit = TagUnitType.Pixels;
                    NextAttribute(ref attribute, false);
                }
                if (stage == 0) m_XmlAttribute[attribute].nameHashCode = Hash(m_XmlAttribute[attribute].nameHashCode, c);
                if (stage == 2 && c == ' ') stage = 0;
            }
            return closed;
        }

        static int Hash(int hash, uint c) => ((hash << 5) + hash) ^ TextUtilities.ToUpperFast((char)c);

        void NextAttribute(ref int attribute, bool grow)
        {
            attribute++;
            if (grow && m_XmlAttribute.Length <= attribute) Array.Resize(ref m_XmlAttribute, Mathf.NextPowerOfTwo(attribute + 1));
            if (attribute >= m_XmlAttribute.Length) Array.Resize(ref m_XmlAttribute, Mathf.NextPowerOfTwo(attribute + 1));
            m_XmlAttribute[attribute] = default;
        }

        float AttributeValue(int attribute) =>
            TextGeneratorUtilities.ConvertToFloat(m_HtmlTag, m_XmlAttribute[attribute].valueStartIndex, m_XmlAttribute[attribute].valueLength);

        /// <summary>A length value in pixels: as is, in ems of the current size, or a percentage of
        /// <paramref name="percentOf"/> (null: percentages are invalid).</summary>
        bool Length(float value, TagUnitType unit, double? percentOf, out float pixels)
        {
            switch (unit)
            {
                case TagUnitType.Pixels:
                    pixels = value;
                    return true;
                case TagUnitType.FontUnits:
                    pixels = value * m_CurrentFontSize;
                    return true;
                case TagUnitType.Percentage when percentOf.HasValue:
                    pixels = (float)(percentOf.Value * value / 100.0);
                    return true;
                default:
                    pixels = 0f;
                    return false;
            }
        }

        void OpenStyle(FontStyles style)
        {
            m_FontStyleInternal |= style;
            m_FontStyleStack.Add(style);
        }

        /// <summary>Closes a style tag unless the settings set the style for all the text.</summary>
        void CloseStyle(FontStyles style, TextGenerationSettings settings)
        {
            if ((settings.fontStyle & style) != style && m_FontStyleStack.Remove(style) == 0) m_FontStyleInternal &= ~style;
        }

        bool ApplyTag(MarkupTag tag, int tagLength, TagUnitType unit, int startIndex, TextGenerationSettings generationSettings, TextInfo textInfo, ref bool isThreadSuccess)
        {
            float value;
            switch (tag)
            {
                case MarkupTag.BOLD:
                    OpenStyle(FontStyles.Bold);
                    m_FontWeightInternal = TextFontWeight.Bold;
                    return true;
                case MarkupTag.SLASH_BOLD:
                    if ((generationSettings.fontStyle & FontStyles.Bold) != FontStyles.Bold && m_FontStyleStack.Remove(FontStyles.Bold) == 0)
                    {
                        m_FontStyleInternal &= ~FontStyles.Bold;
                        m_FontWeightInternal = m_FontWeightStack.Peek();
                    }
                    return true;
                case MarkupTag.ITALIC:
                    OpenStyle(FontStyles.Italic);
                    if (m_XmlAttribute[1].nameHashCode == (int)MarkupTag.ANGLE)
                    {
                        m_ItalicAngle = (int)AttributeValue(1);
                        if (m_ItalicAngle < -180 || m_ItalicAngle > 180) return false;
                    }
                    else m_ItalicAngle = m_CurrentFontAsset.italicStyleSlant;
                    m_ItalicAngleStack.Add(m_ItalicAngle);
                    return true;
                case MarkupTag.SLASH_ITALIC:
                    if ((generationSettings.fontStyle & FontStyles.Italic) != FontStyles.Italic)
                    {
                        m_ItalicAngle = m_ItalicAngleStack.Remove();
                        if (m_FontStyleStack.Remove(FontStyles.Italic) == 0) m_FontStyleInternal &= ~FontStyles.Italic;
                    }
                    return true;
                case MarkupTag.STRIKETHROUGH:
                    OpenStyle(FontStyles.Strikethrough);
                    m_StrikethroughColor = LineColor(textInfo);
                    m_StrikethroughColorStack.Add(m_StrikethroughColor);
                    return true;
                case MarkupTag.SLASH_STRIKETHROUGH:
                    CloseStyle(FontStyles.Strikethrough, generationSettings);
                    m_StrikethroughColor = m_StrikethroughColorStack.Remove();
                    return true;
                case MarkupTag.UNDERLINE:
                    OpenStyle(FontStyles.Underline);
                    m_UnderlineColor = LineColor(textInfo);
                    m_UnderlineColorStack.Add(m_UnderlineColor);
                    return true;
                case MarkupTag.SLASH_UNDERLINE:
                    CloseStyle(FontStyles.Underline, generationSettings);
                    m_UnderlineColor = m_UnderlineColorStack.Remove();
                    return true;
                case MarkupTag.MARK:
                    return OpenHighlight(textInfo);
                case MarkupTag.SLASH_MARK:
                    if ((generationSettings.fontStyle & FontStyles.Highlight) != FontStyles.Highlight)
                    {
                        m_HighlightStateStack.Remove();
                        m_HighlightState = m_HighlightStateStack.current;
                        if (m_FontStyleStack.Remove(FontStyles.Highlight) == 0) m_FontStyleInternal &= ~FontStyles.Highlight;
                    }
                    return true;
                case MarkupTag.SUBSCRIPT:
                    OpenScript(m_CurrentFontAsset.faceInfo.subscriptSize, m_CurrentFontAsset.faceInfo.subscriptOffset, FontStyles.Subscript);
                    return true;
                case MarkupTag.SLASH_SUBSCRIPT:
                    CloseScript(m_CurrentFontAsset.faceInfo.subscriptSize, FontStyles.Subscript);
                    return true;
                case MarkupTag.SUPERSCRIPT:
                    OpenScript(m_CurrentFontAsset.faceInfo.superscriptSize, m_CurrentFontAsset.faceInfo.superscriptOffset, FontStyles.Superscript);
                    return true;
                case MarkupTag.SLASH_SUPERSCRIPT:
                    CloseScript(m_CurrentFontAsset.faceInfo.superscriptSize, FontStyles.Superscript);
                    return true;
                case MarkupTag.FONT_WEIGHT:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    int weight = (int)value;
                    if (weight >= 100 && weight <= 900 && weight % 100 == 0) m_FontWeightInternal = (TextFontWeight)weight;
                    m_FontWeightStack.Add(m_FontWeightInternal);
                    return true;
                case MarkupTag.SLASH_FONT_WEIGHT:
                    m_FontWeightStack.Remove();
                    m_FontWeightInternal = m_FontStyleInternal == FontStyles.Bold ? TextFontWeight.Bold : m_FontWeightStack.Peek();
                    return true;
                case MarkupTag.POSITION:
                    value = AttributeValue(0);
                    if (value == -32768f || !Length(value, unit, m_MarginWidth, out float position)) return false;
                    m_XAdvance = position;
                    return true;
                case MarkupTag.SLASH_POSITION:
                    m_IsIgnoringAlignment = false;
                    return true;
                case MarkupTag.VERTICAL_OFFSET:
                    value = AttributeValue(0);
                    if (value == -32768f || !Length(value, unit, null, out float offset)) return false;
                    m_BaselineOffset = offset;
                    return true;
                case MarkupTag.SLASH_VERTICAL_OFFSET:
                    m_BaselineOffset = 0f;
                    return true;
                case MarkupTag.PAGE:
                    return true;
                case MarkupTag.NO_BREAK:
                    m_IsNonBreakingSpace = true;
                    return true;
                case MarkupTag.SLASH_NO_BREAK:
                    m_IsNonBreakingSpace = false;
                    return true;
                case MarkupTag.SIZE:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    switch (unit)
                    {
                        case TagUnitType.Pixels:
                            m_CurrentFontSize = m_HtmlTag[5] == '+' || m_HtmlTag[5] == '-' ? m_FontSize + value : value;
                            break;
                        case TagUnitType.FontUnits:
                            m_CurrentFontSize = m_FontSize * value;
                            break;
                        case TagUnitType.Percentage:
                            m_CurrentFontSize = (float)((double)m_FontSize * value / 100.0);
                            break;
                        default:
                            return false;
                    }
                    m_SizeStack.Add(m_CurrentFontSize);
                    return true;
                case MarkupTag.SLASH_SIZE:
                    m_CurrentFontSize = m_SizeStack.Remove();
                    return true;
                case MarkupTag.FONT:
                    return OpenFont(generationSettings, ref isThreadSuccess);
                case MarkupTag.SLASH_FONT:
                {
                    var reference = m_MaterialReferenceStack.Remove();
                    m_CurrentFontAsset = reference.fontAsset;
                    m_CurrentMaterial = reference.material;
                    m_CurrentMaterialIndex = reference.index;
                    return true;
                }
                case MarkupTag.SLASH_MATERIAL:
                {
                    var reference = m_MaterialReferenceStack.Remove();
                    m_CurrentMaterial = reference.material;
                    m_CurrentMaterialIndex = reference.index;
                    return true;
                }
                case MarkupTag.SPACE:
                    value = AttributeValue(0);
                    if (value == -32768f || !Length(value, unit, null, out float space)) return false;
                    m_XAdvance += space;
                    return true;
                case MarkupTag.ALPHA:
                    if (m_XmlAttribute[0].valueLength != 3) return false;
                    m_HtmlColor.a = (byte)(TextGeneratorUtilities.HexToInt(m_HtmlTag[7]) * 16 + TextGeneratorUtilities.HexToInt(m_HtmlTag[8]));
                    return true;
                case MarkupTag.A:
                    OpenAnchor(startIndex, generationSettings, textInfo);
                    return true;
                case MarkupTag.SLASH_A:
                case MarkupTag.SLASH_LINK:
                    CloseLink(generationSettings, textInfo, tag == MarkupTag.SLASH_A ? "<a>" : "<link>");
                    return true;
                case MarkupTag.LINK:
                    if (m_IsTextLayoutPhase && !m_IsCalculatingPreferredValues && textInfo != null)
                    {
                        CloseLastLinkTag(textInfo);
                        ref var link = ref NewLink(textInfo);
                        link.hashCode = m_XmlAttribute[0].valueHashCode;
                        link.linkTextfirstCharacterIndex = m_CharacterCount;
                        link.linkIdFirstCharacterIndex = startIndex + m_XmlAttribute[0].valueStartIndex;
                        link.SetLinkId(m_HtmlTag, m_XmlAttribute[0].valueStartIndex, m_XmlAttribute[0].valueLength);
                        link.linkTextLength = -1;
                        textInfo.linkCount++;
                    }
                    return true;
                case MarkupTag.ALIGN:
                    switch ((MarkupTag)m_XmlAttribute[0].valueHashCode)
                    {
                        case MarkupTag.LEFT: m_LineJustification = TextAlignment.MiddleLeft; break;
                        case MarkupTag.RIGHT: m_LineJustification = TextAlignment.MiddleRight; break;
                        case MarkupTag.CENTER: m_LineJustification = TextAlignment.MiddleCenter; break;
                        case MarkupTag.JUSTIFIED: m_LineJustification = TextAlignment.MiddleJustified; break;
                        case MarkupTag.FLUSH: m_LineJustification = TextAlignment.MiddleFlush; break;
                        default: return false;
                    }
                    m_LineJustificationStack.Add(m_LineJustification);
                    return true;
                case MarkupTag.SLASH_ALIGN:
                    m_LineJustification = m_LineJustificationStack.Remove();
                    return true;
                case MarkupTag.WIDTH:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    if (unit == TagUnitType.FontUnits) return false;
                    m_Width = unit == TagUnitType.Percentage ? (float)((double)m_MarginWidth * value / 100.0) : value;
                    return true;
                case MarkupTag.SLASH_WIDTH:
                    m_Width = -1f;
                    return true;
                case MarkupTag.COLOR:
                    return OpenColor(tagLength, textInfo);
                case MarkupTag.SLASH_COLOR:
                    m_HtmlColor = m_ColorStack.Remove();
                    return true;
                case MarkupTag.CHARACTER_SPACE:
                    value = AttributeValue(0);
                    if (value == -32768f || !Length(value, unit, null, out float characterSpace)) return false;
                    m_CSpacing = characterSpace;
                    return true;
                case MarkupTag.SLASH_CHARACTER_SPACE:
                    if (!m_IsTextLayoutPhase || textInfo == null) return true;
                    if (m_CharacterCount > 0)
                    {
                        m_XAdvance -= m_CSpacing;
                        ElementInfo(textInfo, m_CharacterCount - 1).xAdvance = m_XAdvance;
                    }
                    m_CSpacing = 0f;
                    return true;
                case MarkupTag.MONOSPACE:
                    value = AttributeValue(0);
                    if (value == -32768f || !Length(value, m_XmlAttribute[0].unitType, null, out float monospace)) return false;
                    m_MonoSpacing = monospace;
                    if (m_XmlAttribute[1].nameHashCode == (int)MarkupTag.DUOSPACE) m_DuoSpace = AttributeValue(1) != 0f;
                    return true;
                case MarkupTag.SLASH_MONOSPACE:
                    m_MonoSpacing = 0f;
                    m_DuoSpace = false;
                    return true;
                case MarkupTag.INDENT:
                    value = AttributeValue(0);
                    if (value == -32768f || !Length(value, unit, m_MarginWidth, out float indent)) return true;
                    m_TagIndent = indent;
                    m_IndentStack.Add(m_TagIndent);
                    m_XAdvance = m_TagIndent;
                    return true;
                case MarkupTag.SLASH_INDENT:
                    m_TagIndent = m_IndentStack.Remove();
                    return true;
                case MarkupTag.LINE_INDENT:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    if (Length(value, unit, m_MarginWidth, out float lineIndent)) m_TagLineIndent = lineIndent;
                    m_XAdvance += m_TagLineIndent;
                    return true;
                case MarkupTag.SLASH_LINE_INDENT:
                    m_TagLineIndent = 0f;
                    return true;
                case MarkupTag.LOWERCASE:
                    OpenStyle(FontStyles.LowerCase);
                    return true;
                case MarkupTag.SLASH_LOWERCASE:
                    CloseStyle(FontStyles.LowerCase, generationSettings);
                    return true;
                case MarkupTag.UPPERCASE:
                case MarkupTag.ALLCAPS:
                    OpenStyle(FontStyles.UpperCase);
                    return true;
                case MarkupTag.SLASH_ALLCAPS:
                case MarkupTag.SLASH_UPPERCASE:
                    CloseStyle(FontStyles.UpperCase, generationSettings);
                    return true;
                case MarkupTag.SMALLCAPS:
                    OpenStyle(FontStyles.SmallCaps);
                    return true;
                case MarkupTag.SLASH_SMALLCAPS:
                    CloseStyle(FontStyles.SmallCaps, generationSettings);
                    return true;
                case MarkupTag.MARGIN:
                    return ApplyMargins(unit);
                case MarkupTag.SLASH_MARGIN:
                    m_MarginLeft = 0f;
                    m_MarginRight = 0f;
                    return true;
                case MarkupTag.MARGIN_LEFT:
                case MarkupTag.MARGIN_RIGHT:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    Length(value, unit, MarginBase(), out float margin);
                    if (tag == MarkupTag.MARGIN_LEFT) m_MarginLeft = margin >= 0f ? margin : 0f;
                    else m_MarginRight = margin >= 0f ? margin : 0f;
                    return true;
                case MarkupTag.LINE_HEIGHT:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    switch (unit)
                    {
                        case TagUnitType.Pixels:
                            m_LineHeight = value;
                            break;
                        case TagUnitType.FontUnits:
                            m_LineHeight = value * m_CurrentFontSize;
                            break;
                        case TagUnitType.Percentage:
                        {
                            float scale = (float)((double)m_CurrentFontSize / m_CurrentFontAsset.faceInfo.pointSize * m_CurrentFontAsset.faceInfo.scale);
                            m_LineHeight = (float)((double)generationSettings.fontAsset.faceInfo.lineHeight * value / 100.0 * scale);
                            break;
                        }
                    }
                    return true;
                case MarkupTag.SLASH_LINE_HEIGHT:
                    m_LineHeight = k_Unset;
                    return true;
                case MarkupTag.NO_PARSE:
                    m_TagNoParsing = true;
                    return true;
                case MarkupTag.ACTION:
                    if (m_IsTextLayoutPhase) m_ActionStack.Add(m_XmlAttribute[0].valueHashCode);
                    return true;
                case MarkupTag.SLASH_ACTION:
                    m_ActionStack.Remove();
                    return true;
                case MarkupTag.SCALE:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    m_FXScale = new Vector3(value, 1f, 1f);
                    return true;
                case MarkupTag.SLASH_SCALE:
                    m_FXScale = Vector3.one;
                    return true;
                case MarkupTag.ROTATE:
                    value = AttributeValue(0);
                    if (value == -32768f) return false;
                    m_FXRotation = Quaternion.Euler(0f, 0f, value);
                    return true;
                case MarkupTag.SLASH_ROTATE:
                    m_FXRotation = Quaternion.identity;
                    return true;
                default:
                    // Sprites, materials, gradients, classes and tables are not supported (Unity draws
                    // the latter two as text as well); unknown tags are text.
                    return false;
            }
        }

        static ref TextElementInfo ElementInfo(TextInfo textInfo, int index) => ref textInfo.textElementInfo[index];

        /// <summary>An underline's or strikethrough's color: its color attribute (no more opaque
        /// than the text), else the text's.</summary>
        Color32 LineColor(TextInfo textInfo)
        {
            if (m_XmlAttribute[1].nameHashCode != (int)MarkupTag.COLOR) return m_HtmlColor;
            var color = TextGeneratorUtilities.HexCharsToColor(m_HtmlTag, m_XmlAttribute[1].valueStartIndex, m_XmlAttribute[1].valueLength);
            color.a = m_HtmlColor.a < color.a ? m_HtmlColor.a : color.a;
            if (textInfo != null) textInfo.hasMultipleColors = true;
            return color;
        }

        bool OpenHighlight(TextInfo textInfo)
        {
            OpenStyle(FontStyles.Highlight);
            var color = new Color32(255, 255, 0, 64);
            var padding = Offset.zero;
            for (int i = 0; i < m_XmlAttribute.Length && m_XmlAttribute[i].nameHashCode != 0; i++)
            {
                switch ((MarkupTag)m_XmlAttribute[i].nameHashCode)
                {
                    case MarkupTag.MARK:
                        if (m_XmlAttribute[i].valueType == TagValueType.ColorValue)
                            color = TextGeneratorUtilities.HexCharsToColor(m_HtmlTag, m_XmlAttribute[0].valueStartIndex, m_XmlAttribute[0].valueLength);
                        break;
                    case MarkupTag.COLOR:
                        color = TextGeneratorUtilities.HexCharsToColor(m_HtmlTag, m_XmlAttribute[i].valueStartIndex, m_XmlAttribute[i].valueLength);
                        break;
                    case MarkupTag.PADDING:
                        if (TextGeneratorUtilities.GetAttributeParameters(m_HtmlTag, m_XmlAttribute[i].valueStartIndex, m_XmlAttribute[i].valueLength, ref m_AttributeParameterValues) != 4)
                            return false;
                        padding = new Offset(m_AttributeParameterValues[0], m_AttributeParameterValues[1], m_AttributeParameterValues[2], m_AttributeParameterValues[3]);
                        padding *= m_FontSize * 0.01f;
                        break;
                }
            }
            color.a = m_HtmlColor.a < color.a ? m_HtmlColor.a : color.a;
            m_HighlightState = new HighlightState(color, padding);
            m_HighlightStateStack.Push(m_HighlightState);
            if (textInfo != null) textInfo.hasMultipleColors = true;
            return true;
        }

        /// <summary>Sub- and superscripts: smaller by the face's size ratio, moved by its offset.</summary>
        void OpenScript(float sizeRatio, float offset, FontStyles style)
        {
            m_FontScaleMultiplier *= sizeRatio > 0f ? sizeRatio : 1f;
            m_BaselineOffsetStack.Push(m_BaselineOffset);
            float scale = (float)((double)m_CurrentFontSize / m_CurrentFontAsset.faceInfo.pointSize * m_CurrentFontAsset.faceInfo.scale);
            m_BaselineOffset = (float)((double)m_BaselineOffset + (double)offset * scale * m_FontScaleMultiplier);
            m_FontStyleStack.Add(style);
            m_FontStyleInternal |= style;
        }

        void CloseScript(float sizeRatio, FontStyles style)
        {
            if ((m_FontStyleInternal & style) != style) return;
            if (m_FontScaleMultiplier < 1f)
            {
                m_BaselineOffset = m_BaselineOffsetStack.Pop();
                m_FontScaleMultiplier /= sizeRatio > 0f ? sizeRatio : 1f;
            }
            if (m_FontStyleStack.Remove(style) == 0) m_FontStyleInternal &= ~style;
        }

        /// <summary>&lt;color=#hex&gt; or a named color.</summary>
        bool OpenColor(int tagLength, TextInfo textInfo)
        {
            if (textInfo != null) textInfo.hasMultipleColors = true;
            if (m_HtmlTag[6] == '#' || m_HtmlTag[7] == '#')
            {
                // "color=#..." or the quoted "color="#..."" (whose closing quote is not a digit).
                int start = m_HtmlTag[6] == '#' ? 6 : 7;
                int end = m_HtmlTag[6] == '#' ? tagLength : tagLength - 1;
                m_HtmlColor = TextGeneratorUtilities.HexCharsToColor(m_HtmlTag, start, end - start);
                m_ColorStack.Add(m_HtmlColor);
                return true;
            }
            Color32 color;
            switch ((MarkupTag)m_XmlAttribute[0].valueHashCode)
            {
                case MarkupTag.RED: color = Color.red; break;
                case MarkupTag.LIGHTBLUE: color = new Color32(173, 216, 230, 255); break;
                case MarkupTag.BLUE: color = Color.blue; break;
                case MarkupTag.GREY: color = new Color32(128, 128, 128, 255); break;
                case MarkupTag.BLACK: color = Color.black; break;
                case MarkupTag.GREEN: color = Color.green; break;
                case MarkupTag.WHITE: color = Color.white; break;
                case MarkupTag.ORANGE: color = new Color32(255, 128, 0, 255); break;
                case MarkupTag.PURPLE: color = new Color32(160, 32, 240, 255); break;
                case MarkupTag.YELLOW: color = Color.yellow; break;
                case MarkupTag.TEAL: color = new Color32(0, 128, 128, 255); break;
                case MarkupTag.CYAN: color = new Color32(0, 255, 255, 255); break;
                case MarkupTag.DARKBLUE: color = new Color32(0, 0, 160, 255); break;
                case MarkupTag.FUCHSIA: color = new Color32(255, 0, 255, 255); break;
                case MarkupTag.SILVER: color = new Color32(192, 192, 192, 255); break;
                case MarkupTag.BROWN: color = new Color32(165, 42, 42, 255); break;
                case MarkupTag.MAROON: color = new Color32(128, 0, 0, 255); break;
                case MarkupTag.OLIVE: color = new Color32(128, 128, 0, 255); break;
                case MarkupTag.NAVY: color = new Color32(0, 0, 128, 255); break;
                case MarkupTag.AQUA: color = new Color32(0, 255, 255, 255); break;
                case MarkupTag.MAGENTA: color = new Color32(255, 0, 255, 255); break;
                case MarkupTag.LIME: color = new Color32(0, 255, 0, 255); break;
                case MarkupTag.TRANSPARENT: color = new Color32(0, 0, 0, 0); break;
                default: return false;
            }
            m_HtmlColor = color;
            m_ColorStack.Add(m_HtmlColor);
            return true;
        }

        /// <summary>&lt;font="name"&gt;: a font asset already loaded under that name or in Resources
        /// (default: the text's own); material presets are not supported.</summary>
        bool OpenFont(TextGenerationSettings generationSettings, ref bool isThreadSuccess)
        {
            int fontName = m_XmlAttribute[0].valueHashCode;
            if (fontName == (int)MarkupTag.DEFAULT)
            {
                m_CurrentFontAsset = m_MaterialReferences[0].fontAsset;
                m_CurrentMaterial = m_MaterialReferences[0].material;
                m_CurrentMaterialIndex = 0;
                m_MaterialReferenceStack.Add(m_MaterialReferences[0]);
                return true;
            }
            if (!TextResourceManager.TryGetFontAssetByName(fontName, out var fontAsset) || fontAsset == null)
            {
                if (!OnMainThread)
                {
                    isThreadSuccess = false;
                    return false;
                }
                try
                {
                    fontAsset = Resources.Load<FontAsset>(generationSettings.textSettings.defaultFontAssetPath + new string(m_HtmlTag, m_XmlAttribute[0].valueStartIndex, m_XmlAttribute[0].valueLength));
                }
                catch (Exception)
                {
                    fontAsset = null;
                }
                if (fontAsset == null) return false;
            }
            if (m_XmlAttribute[1].nameHashCode != 0 || m_XmlAttribute[1].valueHashCode != 0) return false;
            m_CurrentMaterial = fontAsset.material;
            m_CurrentMaterialIndex = MaterialReference.AddMaterialReference(m_CurrentMaterial, fontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
            m_MaterialReferenceStack.Add(m_MaterialReferences[m_CurrentMaterialIndex]);
            m_CurrentFontAsset = fontAsset;
            return true;
        }

        double MarginBase() => (double)m_MarginWidth - (m_Width != -1f ? m_Width : 0f);

        /// <summary>&lt;margin=v&gt; (both sides) or &lt;margin left=v right=v&gt;; never negative.</summary>
        bool ApplyMargins(TagUnitType unit)
        {
            switch (m_XmlAttribute[0].valueType)
            {
                case TagValueType.NumericalValue:
                {
                    float value = AttributeValue(0);
                    if (value == -32768f) return false;
                    if (Length(value, unit, MarginBase(), out float margin)) m_MarginLeft = margin;
                    m_MarginLeft = m_MarginLeft >= 0f ? m_MarginLeft : 0f;
                    m_MarginRight = m_MarginLeft;
                    return true;
                }
                case TagValueType.None:
                    for (int i = 1; i < m_XmlAttribute.Length && m_XmlAttribute[i].nameHashCode != 0; i++)
                    {
                        var side = (MarkupTag)m_XmlAttribute[i].nameHashCode;
                        if (side != MarkupTag.LEFT && side != MarkupTag.RIGHT) continue;
                        float value = AttributeValue(i);
                        if (value == -32768f) return false;
                        if (side == MarkupTag.LEFT)
                        {
                            if (Length(value, m_XmlAttribute[i].unitType, MarginBase(), out float left)) m_MarginLeft = left;
                            m_MarginLeft = m_MarginLeft >= 0f ? m_MarginLeft : 0f;
                        }
                        else
                        {
                            if (Length(value, m_XmlAttribute[i].unitType, MarginBase(), out float right)) m_MarginRight = right;
                            m_MarginRight = m_MarginRight >= 0f ? m_MarginRight : 0f;
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }

        // ---- Links ----

        ref LinkInfo NewLink(TextInfo textInfo)
        {
            if (textInfo.linkCount + 1 > textInfo.linkInfo.Length) TextInfo.Resize(ref textInfo.linkInfo, textInfo.linkCount + 1);
            textInfo.linkInfo[textInfo.linkCount] = default;
            return ref textInfo.linkInfo[textInfo.linkCount];
        }

        void OpenAnchor(int startIndex, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            if (!m_IsTextLayoutPhase || m_IsCalculatingPreferredValues || textInfo == null) return;
            if (generationSettings.isIMGUI)
            {
                CloseLastLinkTag(textInfo);
                ref var link = ref NewLink(textInfo);
                link.hashCode = (int)MarkupTag.HREF;
                link.linkTextfirstCharacterIndex = m_CharacterCount;
                link.linkIdFirstCharacterIndex = 3;
                int end = m_XmlAttribute[1].valueLength;
                for (int i = m_XmlAttribute.Length - 1; i >= 1; i--)
                {
                    if (m_XmlAttribute[i].valueLength <= 0) continue;
                    end = m_XmlAttribute[i].valueLength + m_XmlAttribute[i].valueStartIndex;
                    break;
                }
                if (m_XmlAttribute[1].valueLength > 0) link.SetLinkId(m_HtmlTag, 2, end - 1);
                textInfo.linkCount++;
            }
            else if (m_XmlAttribute[1].nameHashCode == (int)MarkupTag.HREF)
            {
                CloseLastLinkTag(textInfo);
                ref var link = ref NewLink(textInfo);
                link.hashCode = (int)MarkupTag.HREF;
                link.linkTextfirstCharacterIndex = m_CharacterCount;
                link.linkIdFirstCharacterIndex = startIndex + m_XmlAttribute[1].valueStartIndex;
                link.SetLinkId(m_HtmlTag, m_XmlAttribute[1].valueStartIndex, m_XmlAttribute[1].valueLength);
                link.linkTextLength = -1;
                textInfo.linkCount++;
            }
        }

        void CloseLink(TextGenerationSettings generationSettings, TextInfo textInfo, string tagName)
        {
            if (!m_IsTextLayoutPhase || m_IsCalculatingPreferredValues || textInfo == null) return;
            if (textInfo.linkInfo.Length == 0 || textInfo.linkCount <= 0)
            {
                if (generationSettings.textSettings.displayWarnings)
                    Debug.LogWarning("There seems to be an issue with the formatting of the " + tagName + " tag. Possible issues include: missing or misplaced closing '>', missing or incorrect attribute, or unclosed quotes for attribute values. Please review the tag syntax.");
                return;
            }
            ref var last = ref textInfo.linkInfo[textInfo.linkCount - 1];
            last.linkTextLength = m_CharacterCount - last.linkTextfirstCharacterIndex;
        }

        internal void CloseLastLinkTag(TextInfo textInfo)
        {
            if (textInfo.linkInfo.Length != 0 && textInfo.linkCount > 0) CloseLinkTag(textInfo, textInfo.linkCount - 1);
        }

        internal void CloseAllLinkTags(TextInfo textInfo)
        {
            for (int i = textInfo.linkCount - 1; i >= 0; i--) CloseLinkTag(textInfo, i);
        }

        void CloseLinkTag(TextInfo textInfo, int index)
        {
            if (textInfo.linkInfo[index].linkTextLength == -1)
                textInfo.linkInfo[index].linkTextLength = m_CharacterCount - textInfo.linkInfo[index].linkTextfirstCharacterIndex;
        }
    }
}
