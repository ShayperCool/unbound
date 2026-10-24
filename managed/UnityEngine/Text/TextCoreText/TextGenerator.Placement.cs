using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    internal partial class TextGenerator
    {
        /// <summary>The offset of the first baseline in the rect, by the vertical alignment.</summary>
        Vector3 AnchorOffset(TextAlignment alignment, float maxVisibleDescender)
        {
            var corners = m_RectCorners;
            switch ((int)alignment & 0xFF00)
            {
                case (int)VerticalAlignment.Top:
                    return corners[1] + new Vector3(0f, 0f - m_MaxAscender, 0f);
                case (int)VerticalAlignment.Middle:
                    return (corners[0] + corners[1]) / 2f + new Vector3(0f, (float)(0.0 - ((double)m_MaxAscender + maxVisibleDescender) / 2.0), 0f);
                case (int)VerticalAlignment.Bottom:
                    return corners[0] + new Vector3(0f, 0f - maxVisibleDescender, 0f);
                case (int)VerticalAlignment.Baseline:
                    return (corners[0] + corners[1]) / 2f + new Vector3(0f, 0f, 0f);
                case (int)VerticalAlignment.Midline:
                    return (corners[0] + corners[1]) / 2f + new Vector3(0f, (float)(0.0 - ((double)m_MeshExtents.max.y + m_MeshExtents.min.y) / 2.0), 0f);
                case (int)VerticalAlignment.Capline:
                    return (corners[0] + corners[1]) / 2f + new Vector3(0f, (float)(0.0 - (double)m_MaxCapHeight / 2.0), 0f);
                default:
                    return Vector3.zero;
            }
        }

        /// <summary>
        /// Moves the laid out characters into the rect (TextCore's LayoutPhase): each line by its
        /// horizontal alignment (justified lines spread their extra width 40% over spaces and 60%
        /// over the other characters), the whole text by the vertical one; writes the visible
        /// characters' quads, words, underlines, strikethroughs and highlights.
        /// </summary>
        void PlaceCharacters(TextInfo textInfo, TextGenerationSettings generationSettings, float maxVisibleDescender)
        {
            int underlineVertexIndex = m_MaterialReferences[m_Underline.materialIndex].referenceCount * 4;
            textInfo.meshInfo[m_CurrentMaterialIndex].Clear(false);
            var anchor = AnchorOffset(generationSettings.textAlignment, maxVisibleDescender);
            var justification = Vector3.zero;
            int wordCount = 0;
            int lineCount = 0;
            int previousLine = 0;
            bool lineStartsWithSeparator = false;
            bool inWord = false;
            int wordStart = 0;
            var underline = new LineRun { underlineY = k_LargeFloat };
            var strikethrough = new LineRun();
            float strikethroughPointSize = 0f;
            float strikethroughBaseline = 0f;
            bool highlighting = false;
            Vector3 highlightStart = Vector3.zero, highlightEnd = Vector3.zero;
            var highlightState = new HighlightState(new Color32(255, 255, 0, 64), Offset.zero);
            float sdfScale = 0f;
            bool rtl = generationSettings.isRightToLeft;
            var elements = textInfo.textElementInfo;
            for (int i = 0; i < m_CharacterCount; i++)
            {
                var fontAsset = elements[i].fontAsset;
                char c = (char)elements[i].character;
                bool isWhiteSpace = char.IsWhiteSpace(c);
                int lineNumber = elements[i].lineNumber;
                var line = textInfo.lineInfo[lineNumber];
                lineCount = lineNumber + 1;
                var alignment = line.alignment;
                switch ((int)alignment & 0xFF)
                {
                    case (int)HorizontalAlignment.Left:
                        justification = rtl ? new Vector3(0f - line.maxAdvance, 0f, 0f) : new Vector3(0f + line.marginLeft, 0f, 0f);
                        break;
                    case (int)HorizontalAlignment.Center:
                        justification = new Vector3((float)((double)line.marginLeft + (double)line.width / 2.0 - (double)line.maxAdvance / 2.0), 0f, 0f);
                        break;
                    case (int)HorizontalAlignment.Geometry:
                        justification = new Vector3((float)((double)line.marginLeft + (double)line.width / 2.0 - ((double)line.lineExtents.min.x + line.lineExtents.max.x) / 2.0), 0f, 0f);
                        break;
                    case (int)HorizontalAlignment.Right:
                        justification = rtl ? new Vector3(line.marginLeft + line.width, 0f, 0f) : new Vector3((float)((double)line.marginLeft + line.width - line.maxAdvance), 0f, 0f);
                        break;
                    case (int)HorizontalAlignment.Justified:
                    case (int)HorizontalAlignment.Flush:
                        Justify(ref justification, ref lineStartsWithSeparator, i, c, lineNumber, previousLine, line, alignment, rtl, elements);
                        break;
                }
                var offset = anchor + justification;
                bool isVisible = elements[i].isVisible;
                if (isVisible)
                {
                    if (elements[i].elementType == TextElementType.Character)
                    {
                        // uv2: x 1, y the element's scale, negative for synthesized bold.
                        sdfScale = (float)((double)elements[i].scale * (1.0 - m_CharWidthAdjDelta) * 1.0);
                        if (!elements[i].isUsingAlternateTypeface && (elements[i].style & FontStyles.Bold) == FontStyles.Bold) sdfScale *= -1f;
                        SetScaleUvs(ref elements[i].vertexBottomLeft, sdfScale);
                        SetScaleUvs(ref elements[i].vertexTopLeft, sdfScale);
                        SetScaleUvs(ref elements[i].vertexTopRight, sdfScale);
                        SetScaleUvs(ref elements[i].vertexBottomRight, sdfScale);
                    }
                    if (i < 99999 && wordCount < 99999 && lineNumber < 99999)
                    {
                        elements[i].vertexBottomLeft.position += offset;
                        elements[i].vertexTopLeft.position += offset;
                        elements[i].vertexTopRight.position += offset;
                        elements[i].vertexBottomRight.position += offset;
                    }
                    else
                    {
                        elements[i].vertexBottomLeft.position = Vector3.zero;
                        elements[i].vertexTopLeft.position = Vector3.zero;
                        elements[i].vertexTopRight.position = Vector3.zero;
                        elements[i].vertexBottomRight.position = Vector3.zero;
                        elements[i].isVisible = false;
                    }
                    if (elements[i].elementType == TextElementType.Character)
                        TextGeneratorUtilities.FillCharacterVertexBuffers(i, generationSettings.shouldConvertToLinearSpace, generationSettings, textInfo);
                }
                elements[i].bottomLeft += offset;
                elements[i].topLeft += offset;
                elements[i].topRight += offset;
                elements[i].bottomRight += offset;
                elements[i].origin += offset.x;
                elements[i].xAdvance += offset.x;
                elements[i].ascender += offset.y;
                elements[i].descender += offset.y;
                elements[i].baseLine += offset.y;

                // A line's metrics move with it, at its end (or the text's).
                if (lineNumber != previousLine || i == m_CharacterCount - 1)
                {
                    if (lineNumber != previousLine) OffsetLine(textInfo, previousLine, offset, generationSettings);
                    if (i == m_CharacterCount - 1) OffsetLine(textInfo, lineNumber, offset, generationSettings);
                }

                // Words: runs of letters, digits and hyphens (apostrophes inside count).
                if (char.IsLetterOrDigit(c) || c == '-' || c == '\u00ad' || c == '\u2010' || c == '\u2011')
                {
                    if (!inWord)
                    {
                        inWord = true;
                        wordStart = i;
                    }
                    if (inWord && i == m_CharacterCount - 1)
                    {
                        AddWord(textInfo, wordStart, i, lineNumber);
                        wordCount++;
                    }
                }
                else if ((inWord || (i == 0 && (!char.IsPunctuation(c) | isWhiteSpace || c == '\u200b' || i == m_CharacterCount - 1))) &&
                         (i <= 0 || i >= elements.Length - 1 || i >= m_CharacterCount || (c != '\'' && c != '\u2019') ||
                          !char.IsLetterOrDigit((char)elements[i - 1].character) || !char.IsLetterOrDigit((char)elements[i + 1].character)))
                {
                    int wordEnd = i == m_CharacterCount - 1 && char.IsLetterOrDigit(c) ? i : i - 1;
                    inWord = false;
                    AddWord(textInfo, wordStart, wordEnd, lineNumber);
                    wordCount++;
                }

                PlaceUnderline(ref underline, i, c, isWhiteSpace, lineNumber, line, sdfScale, underlineVertexIndex, fontAsset, generationSettings, textInfo);
                PlaceStrikethrough(ref strikethrough, ref strikethroughPointSize, ref strikethroughBaseline, i, c, isWhiteSpace, lineNumber, line, offset, sdfScale, fontAsset, generationSettings, textInfo);
                PlaceHighlight(ref highlighting, ref highlightStart, ref highlightEnd, ref highlightState, i, c, isWhiteSpace, lineNumber, line, generationSettings, textInfo);
                previousLine = lineNumber;
            }
            textInfo.characterCount = m_CharacterCount;
            textInfo.spriteCount = 0;
            textInfo.lineCount = lineCount;
            textInfo.wordCount = wordCount == 0 || m_CharacterCount <= 0 ? 1 : wordCount;
        }

        static void SetScaleUvs(ref TextVertex vertex, float scale)
        {
            vertex.uv.w = scale;
            vertex.uv2 = new Vector2(1f, scale);
        }

        void OffsetLine(TextInfo textInfo, int lineNumber, Vector3 offset, TextGenerationSettings generationSettings)
        {
            ref var line = ref textInfo.lineInfo[lineNumber];
            bool preserve = generationSettings.textWrappingMode == TextWrappingMode.PreserveWhitespace || generationSettings.textWrappingMode == TextWrappingMode.PreserveWhitespaceNoWrap;
            int last = preserve ? line.lastCharacterIndex : line.lastVisibleCharacterIndex;
            line.baseline += offset.y;
            line.ascender += offset.y;
            line.descender += offset.y;
            line.maxAdvance += offset.x;
            line.lineExtents.min = new Vector2(textInfo.textElementInfo[line.firstCharacterIndex].bottomLeft.x, line.descender);
            line.lineExtents.max = new Vector2(textInfo.textElementInfo[last].topRight.x, line.ascender);
        }

        static void AddWord(TextInfo textInfo, int first, int last, int lineNumber)
        {
            int index = textInfo.wordCount;
            if (index + 1 > textInfo.wordInfo.Length) TextInfo.Resize(ref textInfo.wordInfo, textInfo.wordInfo.Length + 1);
            textInfo.wordInfo[index].firstCharacterIndex = first;
            textInfo.wordInfo[index].lastCharacterIndex = last;
            textInfo.wordInfo[index].characterCount = last - first + 1;
            textInfo.wordCount++;
            textInfo.lineInfo[lineNumber].wordCount++;
        }

        /// <summary>A justified line's offset at a character: the line's spare width goes 40% to
        /// its spaces and 60% to its other characters (all of it to the characters when there are no
        /// spaces). The last line, and lines ending in a control character, are only justified with
        /// Flush (or when they overflow).</summary>
        void Justify(ref Vector3 justification, ref bool lineStartsWithSeparator, int i, char c, int lineNumber, int previousLine, LineInfo line, TextAlignment alignment, bool rtl, TextElementInfo[] elements)
        {
            if (i > line.lastVisibleCharacterIndex || c == '\n' || c == '\u00ad' || c == '\u200b' || c == '\u2060' || c == '\u0003') return;
            char last = (char)elements[line.lastCharacterIndex].character;
            bool flush = ((int)alignment & (int)HorizontalAlignment.Flush) == (int)HorizontalAlignment.Flush;
            if (!(((!char.IsControl(last) && lineNumber < m_LineNumber) | flush) || line.maxAdvance > line.width))
            {
                justification = rtl ? new Vector3(line.marginLeft + line.width, 0f, 0f) : new Vector3(line.marginLeft, 0f, 0f);
                return;
            }
            if (lineNumber != previousLine || i == 0)
            {
                justification = rtl ? new Vector3(line.marginLeft + line.width, 0f, 0f) : new Vector3(line.marginLeft, 0f, 0f);
                lineStartsWithSeparator = char.IsSeparator(c);
                return;
            }
            float spare = rtl ? line.width + line.maxAdvance : line.width - line.maxAdvance;
            int characters = line.visibleCharacterCount - 1 + line.controlCharacterCount;
            int spaces = line.visibleSpaceCount - line.controlCharacterCount;
            if (lineStartsWithSeparator)
            {
                spaces--;
                characters++;
            }
            float ratio = spaces > 0 ? 0.4f : 1f;
            if (spaces < 1) spaces = 1;
            Vector3 step;
            if (c != '\u00a0' && (c == '\t' || char.IsSeparator(c))) step = new Vector3((float)((double)spare * (1.0 - ratio) / spaces), 0f, 0f);
            else step = new Vector3((float)((double)spare * ratio / characters), 0f, 0f);
            if (rtl) justification -= step;
            else justification += step;
        }

        /// <summary>An underline or strikethrough being drawn along characters.</summary>
        struct LineRun
        {
            internal bool active;
            internal Vector3 start;
            internal float startScale;
            internal float maxScale;
            internal float sdfScale;
            internal float underlineY;
            internal Color32 color;
        }

        void PlaceUnderline(ref LineRun run, int i, char c, bool isWhiteSpace, int lineNumber, LineInfo line, float sdfScale, int underlineVertexIndex, FontAsset fontAsset, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            var elements = textInfo.textElementInfo;
            if ((elements[i].style & FontStyles.Underline) != FontStyles.Underline)
            {
                if (run.active) EndUnderline(ref run, i - 1, generationSettings, textInfo);
                return;
            }
            bool drawable = !(i > 99999 || lineNumber > 99999);
            elements[i].underlineVertexIndex = underlineVertexIndex;
            if (!isWhiteSpace && c != '\u200b')
            {
                run.maxScale = Mathf.Max(run.maxScale, elements[i].scale);
                run.sdfScale = Mathf.Max(run.sdfScale, Mathf.Abs(sdfScale));
                run.underlineY = Mathf.Min(run.underlineY, (float)((double)elements[i].baseLine + (double)fontAsset.faceInfo.underlineOffset * run.maxScale));
            }
            if (!run.active && drawable && i <= line.lastVisibleCharacterIndex && c != '\n' && c != '\v' && c != '\r' && (i != line.lastVisibleCharacterIndex || !char.IsSeparator(c)))
            {
                run.active = true;
                run.startScale = elements[i].scale;
                if (run.maxScale == 0f)
                {
                    run.maxScale = run.startScale;
                    run.sdfScale = sdfScale;
                }
                run.start = new Vector3(elements[i].bottomLeft.x, run.underlineY, 0f);
                run.color = elements[i].underlineColor;
            }
            if (run.active && m_CharacterCount == 1)
            {
                EndUnderline(ref run, i, generationSettings, textInfo);
            }
            else if (run.active && (i == line.lastCharacterIndex || i >= line.lastVisibleCharacterIndex))
            {
                EndUnderline(ref run, isWhiteSpace || c == '\u200b' ? line.lastVisibleCharacterIndex : i, generationSettings, textInfo);
            }
            else if (run.active && !drawable)
            {
                EndUnderline(ref run, i - 1, generationSettings, textInfo);
            }
            else if (run.active && i < m_CharacterCount - 1 && !ColorUtilities.CompareColors(run.color, elements[i + 1].underlineColor))
            {
                EndUnderline(ref run, i, generationSettings, textInfo);
            }
        }

        void EndUnderline(ref LineRun run, int endCharacter, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            var end = new Vector3(textInfo.textElementInfo[endCharacter].topRight.x, run.underlineY, 0f);
            float endScale = textInfo.textElementInfo[endCharacter].scale;
            run.active = false;
            DrawUnderlineMesh(run.start, end, run.startScale, endScale, run.maxScale, run.sdfScale, run.color, generationSettings, textInfo);
            run.maxScale = 0f;
            run.sdfScale = 0f;
            run.underlineY = k_LargeFloat;
        }

        void PlaceStrikethrough(ref LineRun run, ref float pointSize, ref float baseline, int i, char c, bool isWhiteSpace, int lineNumber, LineInfo line, Vector3 offset, float sdfScale, FontAsset fontAsset, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            var elements = textInfo.textElementInfo;
            float strikethroughOffset = fontAsset.faceInfo.strikethroughOffset;
            if ((elements[i].style & FontStyles.Strikethrough) != FontStyles.Strikethrough)
            {
                if (run.active) EndStrikethrough(ref run, i - 1, strikethroughOffset, sdfScale, generationSettings, textInfo);
                return;
            }
            bool drawable = !(i > 99999 || lineNumber > 99999);
            elements[i].strikethroughVertexIndex = m_MaterialReferences[m_Underline.materialIndex].referenceCount * 4;
            if ((!run.active & drawable) && i <= line.lastVisibleCharacterIndex && c != '\n' && c != '\v' && c != '\r' && (i != line.lastVisibleCharacterIndex || !char.IsSeparator(c)))
            {
                run.active = true;
                pointSize = elements[i].pointSize;
                run.maxScale = elements[i].scale;
                run.start = new Vector3(elements[i].bottomLeft.x, (float)((double)elements[i].baseLine + (double)strikethroughOffset * run.maxScale), 0f);
                run.color = elements[i].strikethroughColor;
                baseline = elements[i].baseLine;
            }
            if (run.active && m_CharacterCount == 1)
            {
                EndStrikethrough(ref run, i, strikethroughOffset, sdfScale, generationSettings, textInfo);
            }
            else if (run.active && i == line.lastCharacterIndex)
            {
                EndStrikethrough(ref run, isWhiteSpace || c == '\u200b' ? line.lastVisibleCharacterIndex : i, strikethroughOffset, sdfScale, generationSettings, textInfo);
            }
            else if (run.active && i < m_CharacterCount && (elements[i + 1].pointSize != pointSize || !TextGeneratorUtilities.Approximately(elements[i + 1].baseLine + offset.y, baseline)))
            {
                EndStrikethrough(ref run, i <= line.lastVisibleCharacterIndex ? i : line.lastVisibleCharacterIndex, strikethroughOffset, sdfScale, generationSettings, textInfo);
            }
            else if (run.active && i < m_CharacterCount && fontAsset.GetHashCode() != elements[i + 1].fontAsset.GetHashCode())
            {
                EndStrikethrough(ref run, i, strikethroughOffset, sdfScale, generationSettings, textInfo);
            }
            else if (run.active && !drawable)
            {
                EndStrikethrough(ref run, i - 1, strikethroughOffset, sdfScale, generationSettings, textInfo);
            }
        }

        void EndStrikethrough(ref LineRun run, int endCharacter, float strikethroughOffset, float sdfScale, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            ref var element = ref textInfo.textElementInfo[endCharacter];
            var end = new Vector3(element.topRight.x, (float)((double)element.baseLine + (double)strikethroughOffset * run.maxScale), 0f);
            run.active = false;
            DrawUnderlineMesh(run.start, end, run.maxScale, run.maxScale, run.maxScale, sdfScale, run.color, generationSettings, textInfo);
        }

        void PlaceHighlight(ref bool active, ref Vector3 start, ref Vector3 end, ref HighlightState state, int i, char c, bool isWhiteSpace, int lineNumber, LineInfo line, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            var elements = textInfo.textElementInfo;
            if ((elements[i].style & FontStyles.Highlight) != FontStyles.Highlight)
            {
                if (active)
                {
                    active = false;
                    DrawTextHighlight(start, end, state.color, generationSettings, textInfo);
                }
                return;
            }
            bool drawable = !(i > 99999 || lineNumber > 99999);
            if (!active && drawable && i <= line.lastVisibleCharacterIndex && c != '\n' && c != '\v' && c != '\r' && (i != line.lastVisibleCharacterIndex || !char.IsSeparator(c)))
            {
                active = true;
                start = TextGeneratorUtilities.largePositiveVector2;
                end = TextGeneratorUtilities.largeNegativeVector2;
                state = elements[i].highlightState;
            }
            if (active)
            {
                var element = elements[i];
                var elementState = element.highlightState;
                bool changed = false;
                if (state != elementState)
                {
                    bool bitmap = element.fontAsset.IsBitmap();
                    end.x = isWhiteSpace | bitmap
                        ? (end.x - state.padding.right + element.origin) / 2f
                        : (end.x - state.padding.right + element.bottomLeft.x) / 2f;
                    if (bitmap)
                    {
                        start.y = Mathf.Min(start.y, element.bottomLeft.y);
                        end.y = Mathf.Max(end.y, element.topLeft.y);
                    }
                    else
                    {
                        start.y = Mathf.Min(start.y, element.descender);
                        end.y = Mathf.Max(end.y, element.ascender);
                    }
                    DrawTextHighlight(start, end, state.color, generationSettings, textInfo);
                    active = true;
                    start = !bitmap ? (Vector3)new Vector2(end.x, element.descender - elementState.padding.bottom) : (Vector3)new Vector2(end.x, element.bottomLeft.y - elementState.padding.bottom);
                    end = !(isWhiteSpace | bitmap)
                        ? (Vector3)new Vector2(element.topRight.x + elementState.padding.right, element.ascender + elementState.padding.top)
                        : (Vector3)new Vector2(element.xAdvance + elementState.padding.right, bitmap ? element.topLeft.y + elementState.padding.top : element.ascender + elementState.padding.top);
                    state = elementState;
                    changed = true;
                }
                if (!changed)
                {
                    bool bitmap = element.fontAsset.IsBitmap();
                    if (isWhiteSpace | bitmap)
                    {
                        start.x = Mathf.Min(start.x, element.origin - state.padding.left);
                        end.x = Mathf.Max(end.x, element.xAdvance + state.padding.right);
                    }
                    else
                    {
                        start.x = Mathf.Min(start.x, element.bottomLeft.x - state.padding.left);
                        end.x = Mathf.Max(end.x, element.topRight.x + state.padding.right);
                    }
                    if (bitmap)
                    {
                        start.y = Mathf.Min(start.y, element.bottomLeft.y - state.padding.bottom);
                        end.y = Mathf.Max(end.y, element.topLeft.y + state.padding.top);
                    }
                    else
                    {
                        start.y = Mathf.Min(start.y, element.descender - state.padding.bottom);
                        end.y = Mathf.Max(end.y, element.ascender + state.padding.top);
                    }
                }
            }
            if (active && (m_CharacterCount == 1 || i == line.lastCharacterIndex || i >= line.lastVisibleCharacterIndex || !drawable))
            {
                active = false;
                DrawTextHighlight(start, end, state.color, generationSettings, textInfo);
            }
        }

        /// <summary>An underline or strikethrough: three quads stretched from the underscore glyph
        /// (its two ends and its middle), in the underline character's material.</summary>
        void DrawUnderlineMesh(Vector3 start, Vector3 end, float startScale, float endScale, float maxScale, float sdfScale, Color32 underlineColor, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            GetUnderlineSpecialCharacter(generationSettings);
            if (m_Underline.character == null)
            {
                if (generationSettings.textSettings.displayWarnings)
                    Debug.LogWarning("Unable to add underline or strikethrough since the character [0x5F] used by these features is not present in the Font Asset assigned to this text object.");
                return;
            }
            int materialIndex = m_Underline.materialIndex;
            ref var mesh = ref textInfo.meshInfo[materialIndex];
            int first = mesh.vertexCount;
            if (first + 12 > mesh.vertexBufferSize) mesh.ResizeMeshInfo((first + 12) / 4, generationSettings.isIMGUI);
            start.y = Mathf.Min(start.y, end.y);
            end.y = Mathf.Min(start.y, end.y);
            var metrics = m_Underline.character.glyph.metrics;
            var rect = m_Underline.character.glyph.glyphRect;
            float thickness = m_Underline.fontAsset.faceInfo.underlineThickness;
            start.x = (float)((double)start.x + ((double)startScale - maxScale) * m_Padding);
            end.x = (float)((double)end.x + ((double)maxScale - endScale) * m_Padding);
            float capWidth = (float)(((double)metrics.width * 0.5 + m_Padding) * maxScale);
            float squeeze = 1f;
            float minimum = 2f * capWidth;
            float length = end.x - start.x;
            if (length < minimum)
            {
                squeeze = length / minimum;
                capWidth *= squeeze;
            }
            var v = mesh.vertexData;
            float x0 = start.x, x1 = start.x + capWidth, x2 = end.x - capWidth, x3 = end.x;
            float yBottom = (float)((double)start.y - ((double)thickness + m_Padding) * maxScale);
            float yTop = (float)((double)start.y + (double)m_Padding * maxScale);
            v[first].position = new Vector3(x0, yBottom);
            v[first + 1].position = new Vector3(x0, yTop);
            v[first + 2].position = new Vector3(x1, yTop);
            v[first + 3].position = new Vector3(x1, yBottom);
            v[first + 4].position = new Vector3(x1, yBottom);
            v[first + 5].position = new Vector3(x1, yTop);
            v[first + 6].position = new Vector3(x2, yTop);
            v[first + 7].position = new Vector3(x2, yBottom);
            v[first + 8].position = new Vector3(x2, yBottom);
            v[first + 9].position = new Vector3(x2, yTop);
            v[first + 10].position = new Vector3(x3, yTop);
            v[first + 11].position = new Vector3(x3, yBottom);
            float height = generationSettings.screenRect.height;
            for (int k = 0; k < 12; k++) v[first + k].position.y = v[first + k].position.y * -1f + height;
            float texelX = 1f / (float)m_Underline.fontAsset.atlasWidth;
            float texelY = 1f / (float)m_Underline.fontAsset.atlasHeight;
            float capUv = (float)(((double)rect.width * 0.5 + m_Padding) * squeeze * texelX);
            float u0 = (float)(((double)rect.x - m_Padding) * texelX);
            float u1 = u0 + capUv;
            float uMid = (float)(((double)rect.x + (double)rect.width * 0.5) * texelX);
            float u3 = (float)(((double)(rect.x + rect.width) + m_Padding) * texelX);
            float u2 = u3 - capUv;
            float vBottom = (float)(((double)rect.y - m_Padding) * texelY);
            float vTop = (float)(((double)(rect.y + rect.height) + m_Padding) * texelY);
            v[first].uv0 = new Vector2(u0, vBottom);
            v[first + 1].uv0 = new Vector2(u0, vTop);
            v[first + 2].uv0 = new Vector2(u1, vTop);
            v[first + 3].uv0 = new Vector2(u1, vBottom);
            v[first + 4].uv0 = new Vector2(uMid, vBottom);
            v[first + 5].uv0 = new Vector2(uMid, vTop);
            v[first + 6].uv0 = new Vector2(uMid, vTop);
            v[first + 7].uv0 = new Vector2(uMid, vBottom);
            v[first + 8].uv0 = new Vector2(u2, vBottom);
            v[first + 9].uv0 = new Vector2(u2, vTop);
            v[first + 10].uv0 = new Vector2(u3, vTop);
            v[first + 11].uv0 = new Vector2(u3, vBottom);
            float inverseLength = 1f / length;
            float along = (float)(((double)v[first + 2].position.x - start.x) * inverseLength);
            v[first].uv2 = new Vector2(0f, 0f);
            v[first + 1].uv2 = new Vector2(0f, 1f);
            v[first + 2].uv2 = new Vector2(along, 1f);
            v[first + 3].uv2 = new Vector2(along, 0f);
            float from = (float)(((double)v[first + 4].position.x - start.x) * inverseLength);
            along = (float)(((double)v[first + 6].position.x - start.x) * inverseLength);
            v[first + 4].uv2 = new Vector2(from, 0f);
            v[first + 5].uv2 = new Vector2(from, 1f);
            v[first + 6].uv2 = new Vector2(along, 1f);
            v[first + 7].uv2 = new Vector2(along, 0f);
            from = (float)(((double)v[first + 8].position.x - start.x) * inverseLength);
            v[first + 8].uv2 = new Vector2(from, 0f);
            v[first + 9].uv2 = new Vector2(from, 1f);
            v[first + 10].uv2 = new Vector2(1f, 1f);
            v[first + 11].uv2 = new Vector2(1f, 0f);
            underlineColor.a = m_FontColor32.a < underlineColor.a ? m_FontColor32.a : underlineColor.a;
            for (int k = 0; k < 12; k++) v[first + k].color = underlineColor;
            mesh.vertexCount += 12;
        }

        /// <summary>A highlight: a quad sampling the middle of the underscore glyph.</summary>
        void DrawTextHighlight(Vector3 start, Vector3 end, Color32 highlightColor, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            GetUnderlineSpecialCharacter(generationSettings);
            if (m_Underline.character == null)
            {
                if (generationSettings.textSettings.displayWarnings)
                    Debug.LogWarning("Unable to add highlight since the primary Font Asset doesn't contain the underline character.");
                return;
            }
            ref var mesh = ref textInfo.meshInfo[m_CurrentMaterialIndex];
            int first = mesh.vertexCount;
            if (first + 4 > mesh.vertexBufferSize) mesh.ResizeMeshInfo((first + 4) / 4, generationSettings.isIMGUI);
            var v = mesh.vertexData;
            v[first].position = start;
            v[first + 1].position = new Vector3(start.x, end.y, 0f);
            v[first + 2].position = end;
            v[first + 3].position = new Vector3(end.x, start.y, 0f);
            float height = generationSettings.screenRect.height;
            for (int k = 0; k < 4; k++) v[first + k].position.y = v[first + k].position.y * -1f + height;
            int atlasWidth = m_Underline.fontAsset.atlasWidth;
            int atlasHeight = m_Underline.fontAsset.atlasHeight;
            var rect = m_Underline.character.glyph.glyphRect;
            var center = new Vector2((float)(((double)rect.x + (double)rect.width / 2.0) / atlasWidth), (float)(((double)rect.y + (double)rect.height / 2.0) / atlasHeight));
            var texel = new Vector2(1f / (float)atlasWidth, 1f / (float)atlasHeight);
            v[first].uv0 = center - texel;
            v[first + 1].uv0 = center + new Vector2(0f - texel.x, texel.y);
            v[first + 2].uv0 = center + texel;
            v[first + 3].uv0 = center + new Vector2(texel.x, 0f - texel.y);
            var uv2 = new Vector2(0f, 1f);
            highlightColor.a = m_FontColor32.a < highlightColor.a ? m_FontColor32.a : highlightColor.a;
            for (int k = 0; k < 4; k++)
            {
                v[first + k].uv2 = uv2;
                v[first + k].color = highlightColor;
            }
            mesh.vertexCount += 4;
        }
    }

    internal static partial class ColorUtilities
    {
        internal static bool CompareColors(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        internal static Color32 MultiplyColors(Color32 c1, Color32 c2) =>
            new Color32((byte)(c1.r / 255f * (c2.r / 255f) * 255f), (byte)(c1.g / 255f * (c2.g / 255f) * 255f), (byte)(c1.b / 255f * (c2.b / 255f) * 255f), (byte)(c1.a / 255f * (c2.a / 255f) * 255f));
    }
}
