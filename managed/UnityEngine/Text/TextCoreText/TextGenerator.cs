using System;
using System.Collections.Generic;
using UnityEngine.TextCore.LowLevel;

namespace UnityEngine.TextCore.Text
{
    /// <summary>
    /// TextCore's text generator, as UI Toolkit runs it. Text goes through three passes:
    /// <list type="bullet">
    /// <item>preparation: escapes and line-break tags resolved into processing elements, each
    /// character's text element found (font asset, weight and italic typefaces, fallbacks; dynamic
    /// font assets render new glyphs into their atlas) and counted per material;</item>
    /// <item>layout (<see cref="GenerateText"/>): characters placed along lines in TextCore's
    /// units (y up, the first baseline at 0), lines broken at word boundaries when they exceed the
    /// rect's width, overflow handled against its height;</item>
    /// <item>placement: lines aligned in the rect and every visible character's quad written to the
    /// vertex buffer of its material, y down from the rect's top.</item>
    /// </list>
    /// <see cref="GetPreferredValues"/> runs a lighter layout that only measures. The arithmetic
    /// follows Unity's float operation for operation, so positions match to the bit.
    /// </summary>
    internal partial class TextGenerator
    {
        public delegate void MissingCharacterEventCallback(uint unicode, int stringIndex, TextInfo text, FontAsset fontAsset);

        /// <summary>A character the generator inserts (ellipsis, the underline's glyph).</summary>
        protected partial struct SpecialCharacter
        {
            public Character character;
            public FontAsset fontAsset;
            public Material material;
            public int materialIndex;

            public SpecialCharacter(Character character, int materialIndex)
            {
                this.character = character;
                fontAsset = character.textAsset as FontAsset;
                material = fontAsset != null ? fontAsset.material : null;
                this.materialIndex = materialIndex;
            }
        }

        const float k_Unset = -32767f;
        const float k_LargeFloat = 32767f;
        const uint k_EndOfText = 3;
        const uint k_Tab = 9;
        const uint k_LineFeed = 10;
        const uint k_VerticalTab = 11;
        const uint k_CarriageReturn = 13;
        const uint k_Space = 32;
        const uint k_HyphenMinus = 45;
        const uint k_LessThan = 60;
        const uint k_Underscore = 95;
        const uint k_NoBreakSpace = 160;
        const uint k_SoftHyphen = 173;
        const uint k_FigureSpace = 0x2007;
        const uint k_NonBreakingHyphen = 0x2011;
        const uint k_ZeroWidthSpace = 0x200B;
        const uint k_NarrowNoBreakSpace = 0x202F;
        const uint k_WordJoiner = 0x2060;
        const uint k_Ellipsis = 0x2026;
        const uint k_LineSeparator = 0x2028;
        const uint k_ParagraphSeparator = 0x2029;
        const uint k_WhiteSquare = 0x25A1;
        /// <summary>A processing element consumed by the one before it (a variation selector).</summary>
        const uint k_Consumed = 26;

        static TextGenerator s_TextGenerator;

        // Unity-visible state.
        internal TextProcessingElement[] m_TextProcessingArray = new TextProcessingElement[8];
        internal int m_InternalTextProcessingArraySize;
        protected bool m_VertexBufferAutoSizeReduction;
        internal HighlightState m_HighlightState = new HighlightState(Color.white, Offset.zero);
        protected bool m_IsIgnoringAlignment;
        protected bool m_IsTextTruncated;
        protected TextProcessingStack<int>[] m_TextStyleStacks = new TextProcessingStack<int>[8];
        protected int m_TextStyleStackDepth;
        internal MaterialReference[] m_MaterialReferences = new MaterialReference[8];
        protected SpecialCharacter m_Ellipsis;
        protected SpecialCharacter m_Underline;

        /// <summary>The source text as code units (zero terminated).</summary>
        uint[] m_Backing = new uint[4];
        int m_BackingCount;

        readonly Dictionary<int, int> m_MaterialReferenceIndexLookup = new Dictionary<int, int>();
        readonly Vector3[] m_RectCorners = new Vector3[4];
        TextElementInfo[] m_MeasureElements;

        // Layout state (what a word-wrap state saves).
        float m_MarginWidth;
        float m_MarginHeight;
        float m_PreferredWidth;
        float m_PreferredHeight;
        FontAsset m_CurrentFontAsset;
        Material m_CurrentMaterial;
        int m_CurrentMaterialIndex;
        TextProcessingStack<MaterialReference> m_MaterialReferenceStack = new TextProcessingStack<MaterialReference>(new MaterialReference[16]);
        float m_Padding;
        int m_TotalCharacterCount;
        float m_FontSize;
        float m_FontScaleMultiplier;
        float m_CurrentFontSize;
        TextProcessingStack<float> m_SizeStack = new TextProcessingStack<float>(16);
        FontStyles m_FontStyleInternal;
        FontStyleStack m_FontStyleStack;
        TextFontWeight m_FontWeightInternal = TextFontWeight.Regular;
        TextProcessingStack<TextFontWeight> m_FontWeightStack = new TextProcessingStack<TextFontWeight>(8);
        TextAlignment m_LineJustification;
        TextProcessingStack<TextAlignment> m_LineJustificationStack = new TextProcessingStack<TextAlignment>(16);
        float m_BaselineOffset;
        TextProcessingStack<float> m_BaselineOffsetStack = new TextProcessingStack<float>(new float[16]);
        Color32 m_FontColor32;
        Color32 m_HtmlColor;
        Color32 m_UnderlineColor;
        Color32 m_StrikethroughColor;
        TextProcessingStack<Color32> m_ColorStack = new TextProcessingStack<Color32>(new Color32[16]);
        TextProcessingStack<Color32> m_UnderlineColorStack = new TextProcessingStack<Color32>(new Color32[16]);
        TextProcessingStack<Color32> m_StrikethroughColorStack = new TextProcessingStack<Color32>(new Color32[16]);
        TextProcessingStack<HighlightState> m_HighlightStateStack = new TextProcessingStack<HighlightState>(new HighlightState[16]);
        TextProcessingStack<int> m_ItalicAngleStack = new TextProcessingStack<int>(new int[16]);
        TextProcessingStack<int> m_ActionStack = new TextProcessingStack<int>(new int[16]);
        TextProcessingStack<float> m_IndentStack = new TextProcessingStack<float>(new float[16]);
        float m_LineOffset;
        float m_LineHeight;
        bool m_IsDrivenLineSpacing;
        float m_CSpacing;
        float m_MonoSpacing;
        bool m_DuoSpace;
        float m_XAdvance;
        float m_TagLineIndent;
        float m_TagIndent;
        bool m_TagNoParsing;
        int m_CharacterCount;
        int m_FirstCharacterOfLine;
        int m_LastCharacterOfLine;
        int m_FirstVisibleCharacterOfLine;
        int m_LastVisibleCharacterOfLine;
        float m_MaxLineAscender;
        float m_MaxLineDescender;
        int m_LineNumber;
        int m_LineVisibleCharacterCount;
        int m_LineVisibleSpaceCount;
        int m_FirstOverflowCharacterIndex;
        float m_MarginLeft;
        float m_MarginRight;
        float m_Width;
        Extents m_MeshExtents;
        float m_MaxCapHeight;
        float m_MaxAscender;
        float m_MaxDescender;
        bool m_IsNonBreakingSpace;
        TextElementType m_TextElementType;
        bool m_IsTextLayoutPhase;
        TextElement m_CachedTextElement;
        Color32 m_HighlightColor;
        float m_CharWidthAdjDelta;
        float m_MaxFontSize;
        float m_MinFontSize;
        float m_StartOfLineAscender;
        float m_LineSpacingDelta;
        int m_ItalicAngle;
        Vector3 m_FXScale;
        Quaternion m_FXRotation;
        int m_LastBaseGlyphIndex;
        float m_PageAscender;
        bool m_IsCalculatingPreferredValues;

        public TextGenerator() { }

        /// <summary>Whether generation runs on a job thread (font assets are then not added to).</summary>
        internal static bool IsExecutingJob { get; set; }

        public bool isTextTruncated => m_IsTextTruncated;

        public static event MissingCharacterEventCallback OnMissingCharacter;

        internal static TextGenerator GetTextGenerator() => s_TextGenerator ??= new TextGenerator();

        static bool OnMainThread => !IsExecutingJob;

        // ---- Entry points ----

        /// <summary>Lays out the text and writes its characters, lines and meshes into
        /// <paramref name="textInfo"/>; the kerning of glyphs added since the last call is read first.</summary>
        public void GenerateText(TextGenerationSettings settings, TextInfo textInfo)
        {
            bool onMainThread = OnMainThread;
            if (settings.fontAsset == null || settings.fontAsset.characterLookupTable == null)
            {
                Debug.LogWarning("Can't Generate Mesh, No Font Asset has been assigned.");
                return;
            }
            if (textInfo == null)
            {
                Debug.LogError("Null TextInfo provided to TextGenerator. Cannot update its content.");
                return;
            }
            Prepare(settings, textInfo);
            if (onMainThread) FontAsset.UpdateFontAssetsInUpdateQueue();
            GenerateTextMesh(settings, textInfo);
        }

        internal void GenerateTextMesh(TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            if (generationSettings.fontAsset == null || generationSettings.fontAsset.characterLookupTable == null)
            {
                Debug.LogWarning("Can't Generate Mesh! No Font Asset has been assigned.");
                return;
            }
            textInfo?.Clear();
            if (generationSettings.fontSize <= 0f || m_TextProcessingArray == null || m_TextProcessingArray.Length == 0 || m_TextProcessingArray[0].unicode == 0)
            {
                textInfo.ClearMeshInfo(true);
                m_PreferredWidth = 0f;
                m_PreferredHeight = 0f;
                return;
            }
            LayoutCharacters(textInfo, generationSettings, out uint lastCharacter, out float maxVisibleDescender);
            if (m_CharacterCount == 0 || (m_CharacterCount == 1 && lastCharacter == k_EndOfText))
            {
                textInfo.ClearMeshInfo(true);
                return;
            }
            PlaceCharacters(textInfo, generationSettings, maxVisibleDescender);
            for (int i = 1; i < textInfo.materialCount; i++) textInfo.meshInfo[i].ClearUnusedVertices();
        }

        /// <summary>The size the text needs: its widest line and the height of its lines, rounded
        /// up to the hundredth past (as Unity: <c>(int)(v * 100 + 1) / 100</c>). Glyphs new to a
        /// dynamic font asset are added, but their kerning is only read by the next
        /// <see cref="GenerateText"/> or <see cref="FontAsset.UpdateFontAssetsInUpdateQueue"/>.</summary>
        public Vector2 GetPreferredValues(TextGenerationSettings settings, TextInfo textInfo)
        {
            if (settings.fontAsset == null || settings.fontAsset.characterLookupTable == null)
            {
                Debug.LogWarning("Can't Generate Mesh, No Font Asset has been assigned.");
                return Vector2.zero;
            }
            if (settings.fontSize <= 0f) return Vector2.zero;
            Prepare(settings, textInfo);
            if (settings.textSettings == null) return Vector2.zero;
            float fontSize = m_FontSize;
            m_MinFontSize = 0f;
            m_MaxFontSize = 0f;
            m_CharWidthAdjDelta = 0f;
            var margins = new Vector2(m_MarginWidth != 0f ? m_MarginWidth : k_LargeFloat, m_MarginHeight != 0f ? m_MarginHeight : k_LargeFloat);
            return MeasurePreferred(ref fontSize, margins, settings, textInfo);
        }

        /// <summary>The layout pass alone (after <see cref="Prepare"/>).</summary>
        public void ParsingPhase(TextInfo textInfo, TextGenerationSettings generationSettings, out uint charCode, out float maxVisibleDescender) =>
            LayoutCharacters(textInfo, generationSettings, out charCode, out maxVisibleDescender);

        /// <summary>The placement pass alone (after <see cref="ParsingPhase"/>).</summary>
        public void LayoutPhase(TextInfo textInfo, TextGenerationSettings generationSettings, float maxVisibleDescender) =>
            PlaceCharacters(textInfo, generationSettings, maxVisibleDescender);

        protected virtual Vector2 CalculatePreferredValues(ref float fontSize, Vector2 marginSize, bool isTextAutoSizingEnabled, TextGenerationSettings generationSettings, TextInfo textInfo) =>
            MeasurePreferred(ref fontSize, marginSize, generationSettings, textInfo);

        /// <summary>Resolves the text into processing elements and their text elements (adding
        /// glyphs to dynamic font assets) and sizes the text info's buffers.</summary>
        internal void Prepare(TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            m_Padding = generationSettings.extraPadding;
            m_CurrentFontAsset = generationSettings.fontAsset;
            m_FontStyleInternal = generationSettings.fontStyle;
            m_FontWeightInternal = StartingWeight(generationSettings);
            GetSpecialCharacters(generationSettings);
            ComputeMarginSize(generationSettings.screenRect);
            PopulateTextBackingArray(generationSettings.renderedText);
            PopulateTextProcessingArray(generationSettings);
            SetArraySizes(m_TextProcessingArray, generationSettings, textInfo);
            m_FontSize = generationSettings.fontSize;
            m_MaxFontSize = 0f;
            m_MinFontSize = 0f;
            m_LineSpacingDelta = 0f;
            m_CharWidthAdjDelta = 0f;
        }

        /// <summary>Adds the text's characters to its font assets ahead of generation (UI Toolkit
        /// does this on the main thread before generating in jobs).</summary>
        internal bool PrepareFontAsset(TextGenerationSettings generationSettings)
        {
            m_CurrentFontAsset = generationSettings.fontAsset;
            m_FontStyleInternal = generationSettings.fontStyle;
            m_FontWeightInternal = StartingWeight(generationSettings);
            if (!GetSpecialCharacters(generationSettings)) return false;
            PopulateTextBackingArray(generationSettings.renderedText);
            PopulateTextProcessingArray(generationSettings);
            return PopulateFontAsset(generationSettings, m_TextProcessingArray);
        }

        static TextFontWeight StartingWeight(TextGenerationSettings settings) =>
            (settings.fontStyle & FontStyles.Bold) == FontStyles.Bold ? TextFontWeight.Bold : settings.fontWeight;

        void ComputeMarginSize(Rect rect)
        {
            m_MarginWidth = rect.width;
            m_MarginHeight = rect.height;
            m_RectCorners[0] = new Vector3(0f, 0f, 0f);
            m_RectCorners[1] = new Vector3(0f, rect.height, 0f);
            m_RectCorners[2] = new Vector3(rect.width, rect.height, 0f);
            m_RectCorners[3] = new Vector3(rect.width, 0f, 0f);
        }

        // ---- Special characters ----

        protected bool GetSpecialCharacters(TextGenerationSettings generationSettings)
        {
            if (!GetEllipsisSpecialCharacter(generationSettings)) return false;
            return GetUnderlineSpecialCharacter(generationSettings) && m_Underline.character != null;
        }

        protected bool GetEllipsisSpecialCharacter(TextGenerationSettings generationSettings)
        {
            if (!FindSpecialCharacter(k_Ellipsis, generationSettings, out var character)) return false;
            if (character != null) m_Ellipsis = new SpecialCharacter(character, 0);
            return true;
        }

        protected bool GetUnderlineSpecialCharacter(TextGenerationSettings generationSettings)
        {
            if (!FindSpecialCharacter(k_Underscore, generationSettings, out var character)) return false;
            if (character == null) return true;
            m_Underline = new SpecialCharacter(character, m_CurrentMaterialIndex);
            if (m_Underline.fontAsset.GetHashCode() == m_CurrentFontAsset.GetHashCode()) return true;
            var textSettings = generationSettings.textSettings;
            if (textSettings.matchMaterialPreset && m_CurrentMaterial != null && m_CurrentMaterial.GetHashCode() != m_Underline.fontAsset.material.GetHashCode())
            {
                m_Underline.material = MaterialManager.GetFallbackMaterial(m_CurrentMaterial, m_Underline.fontAsset.material);
                if (m_Underline.material == null) return false;
            }
            else
            {
                m_Underline.material = m_Underline.fontAsset.material;
            }
            m_Underline.materialIndex = MaterialReference.AddMaterialReference(m_Underline.material, m_Underline.fontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
            m_MaterialReferences[m_Underline.materialIndex].referenceCount = 0;
            return true;
        }

        /// <summary>A character the generator inserts, in the current font asset, its fallbacks, the
        /// settings' fallbacks and OS fallbacks, then the default font asset. False when it cannot
        /// be looked up from a job.</summary>
        bool FindSpecialCharacter(uint unicode, TextGenerationSettings generationSettings, out Character character)
        {
            var fontAsset = m_CurrentFontAsset ?? generationSettings.fontAsset;
            var textSettings = generationSettings.textSettings;
            bool populateLigatures = TextGenerationSettings.fontFeatures.Contains(OTL_FeatureTag.liga);
            character = FontAssetUtilities.GetCharacterFromFontAsset(unicode, fontAsset, false, m_FontStyleInternal, m_FontWeightInternal, out _, populateLigatures);
            if (character == null && fontAsset.m_FallbackFontAssetTable != null && fontAsset.m_FallbackFontAssetTable.Count > 0)
                character = FontAssetUtilities.GetCharacterFromFontAssetsInternal(unicode, fontAsset, fontAsset.m_FallbackFontAssetTable, null, true, m_FontStyleInternal, m_FontWeightInternal, out _, populateLigatures);
            if (character == null)
            {
                if (textSettings.GetStaticFallbackOSFontAsset() == null && !OnMainThread) return false;
                character = FontAssetUtilities.GetCharacterFromFontAssetsInternal(unicode, fontAsset, textSettings.GetFallbackFontAssets(FallbackPixelSize(generationSettings)), textSettings.fallbackOSFontAssets, true, m_FontStyleInternal, m_FontWeightInternal, out _, populateLigatures);
            }
            if (character == null && textSettings.defaultFontAsset != null)
                character = FontAssetUtilities.GetCharacterFromFontAsset(unicode, textSettings.defaultFontAsset, true, m_FontStyleInternal, m_FontWeightInternal, out _, populateLigatures);
            return true;
        }

        static int FallbackPixelSize(TextGenerationSettings settings) =>
            settings.isEditorRenderingModeBitmap ? (int)(settings.fontSize * settings.pixelsPerPoint) : -1;

        protected void DoMissingGlyphCallback(uint unicode, int stringIndex, FontAsset fontAsset, TextInfo textInfo) =>
            OnMissingCharacter?.Invoke(unicode, stringIndex, textInfo, fontAsset);

        // ---- Processing elements ----

        void PopulateTextBackingArray(in RenderedText sourceText)
        {
            int count = sourceText.CharacterCount;
            if (count >= m_Backing.Length) Array.Resize(ref m_Backing, Mathf.NextPowerOfTwo(count + 1));
            int i = 0;
            foreach (char c in sourceText) m_Backing[i++] = c;
            m_Backing[i] = 0;
            m_BackingCount = i;
        }

        uint Backing(int i) => i < m_Backing.Length ? m_Backing[i] : 0;

        void AppendElement(ref int writeIndex, int stringIndex, int length, uint unicode)
        {
            if (writeIndex == m_TextProcessingArray.Length) TextGeneratorUtilities.ResizeInternalArray(ref m_TextProcessingArray);
            m_TextProcessingArray[writeIndex++] = new TextProcessingElement
            {
                elementType = TextProcessingElementType.TextCharacterElement,
                stringIndex = stringIndex,
                length = length,
                unicode = unicode,
            };
        }

        /// <summary>The characters to lay out: <c>\n \r \t \v \\ \uXXXX \UXXXXXXXX</c> escapes
        /// (when control characters are parsed), surrogate pairs, CR LF, and the &lt;br&gt;,
        /// &lt;cr&gt;, &lt;nbsp&gt;, &lt;zwsp&gt;, &lt;zwj&gt; and &lt;shy&gt; tags (outside &lt;noparse&gt;)
        /// as the characters they stand for. Other tags stay as text for the layout to parse.</summary>
        void PopulateTextProcessingArray(TextGenerationSettings generationSettings)
        {
            int count = m_BackingCount;
            if (m_TextProcessingArray.Length < count) TextGeneratorUtilities.ResizeInternalArray(ref m_TextProcessingArray, count);
            TextProcessingStack<int>.SetDefault(m_TextStyleStacks, 0);
            m_TextStyleStackDepth = 0;
            int writeIndex = 0;
            bool noParse = false;
            bool escapes = generationSettings.parseControlCharacters;
            for (int i = 0; i < count; i++)
            {
                uint c = m_Backing[i];
                if (c == 0) break;
                if (c == '\\' && i < count - 1)
                {
                    uint next = m_Backing[i + 1];
                    if (next == '\\')
                    {
                        if (escapes) i++;
                    }
                    else if (escapes && (next == 'n' || next == 'r' || next == 't' || next == 'v'))
                    {
                        uint code = next == 'n' ? k_LineFeed : next == 'r' ? k_CarriageReturn : next == 't' ? k_Tab : k_VerticalTab;
                        AppendElement(ref writeIndex, i, 1, code);
                        i++;
                        continue;
                    }
                    else if (escapes && next == 'u' && count > i + 5 && IsHex(i + 2, 4))
                    {
                        AppendElement(ref writeIndex, i, 6, GetHex(i + 2, 4));
                        i += 5;
                        continue;
                    }
                    else if (escapes && next == 'U' && count > i + 9 && IsHex(i + 2, 8))
                    {
                        AppendElement(ref writeIndex, i, 10, GetHex(i + 2, 8));
                        i += 9;
                        continue;
                    }
                }
                if (c >= 0xD800 && c <= 0xDBFF && count > i + 1 && m_Backing[i + 1] >= 0xDC00 && m_Backing[i + 1] <= 0xDFFF)
                {
                    AppendElement(ref writeIndex, i, 2, TextGeneratorUtilities.ConvertToUTF32(c, m_Backing[i + 1]));
                    i++;
                    continue;
                }
                if (c == k_CarriageReturn && i + 1 < count && m_Backing[i + 1] == k_LineFeed)
                {
                    AppendElement(ref writeIndex, i, 2, k_LineFeed);
                    i++;
                    continue;
                }
                if (c == k_LessThan && generationSettings.richText)
                {
                    var tag = (MarkupTag)TextGeneratorUtilities.GetMarkupTagHashCode(m_Backing, i + 1);
                    if (tag == MarkupTag.NO_PARSE) noParse = true;
                    else if (tag == MarkupTag.SLASH_NO_PARSE) noParse = false;
                    else if (!noParse)
                    {
                        // The tag's characters: "<br>", "<cr>", "<nbsp>", "<zwsp>", "<zwj>", "<shy>".
                        int length = tag switch
                        {
                            MarkupTag.BR or MarkupTag.CR => 4,
                            MarkupTag.NBSP or MarkupTag.ZWSP => 6,
                            MarkupTag.ZWJ or MarkupTag.SHY => 5,
                            _ => 0,
                        };
                        if (length > 0)
                        {
                            uint code = tag switch
                            {
                                MarkupTag.BR => k_LineFeed,
                                MarkupTag.CR => k_CarriageReturn,
                                MarkupTag.NBSP => k_NoBreakSpace,
                                MarkupTag.ZWSP => k_ZeroWidthSpace,
                                MarkupTag.ZWJ => 0x200D,
                                _ => k_SoftHyphen,
                            };
                            AppendElement(ref writeIndex, i, length, code);
                            i += length - 1;
                            continue;
                        }
                    }
                }
                AppendElement(ref writeIndex, i, 1, c);
            }
            m_TextStyleStackDepth = 0;
            if (writeIndex == m_TextProcessingArray.Length) TextGeneratorUtilities.ResizeInternalArray(ref m_TextProcessingArray);
            m_TextProcessingArray[writeIndex].unicode = 0;
            m_InternalTextProcessingArraySize = writeIndex;
        }

        bool IsHex(int start, int digits)
        {
            for (int k = 0; k < digits; k++)
            {
                uint c = Backing(start + k);
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            }
            return true;
        }

        uint GetHex(int start, int digits)
        {
            uint value = 0;
            for (int k = 0; k < digits; k++) value = (value << 4) + TextGeneratorUtilities.HexToInt((char)Backing(start + k));
            return value;
        }

        // ---- Text elements ----

        /// <summary>The case a character is looked up and drawn in, by the uppercase, lowercase and
        /// small caps styles.</summary>
        uint ApplyCase(uint unicode)
        {
            if ((m_FontStyleInternal & FontStyles.UpperCase) == FontStyles.UpperCase)
                return char.IsLower((char)unicode) ? char.ToUpper((char)unicode) : unicode;
            if ((m_FontStyleInternal & FontStyles.LowerCase) == FontStyles.LowerCase)
                return char.IsUpper((char)unicode) ? char.ToLower((char)unicode) : unicode;
            if ((m_FontStyleInternal & FontStyles.SmallCaps) == FontStyles.SmallCaps && char.IsLower((char)unicode))
                return char.ToUpper((char)unicode);
            return unicode;
        }

        void ResetLookupState(TextGenerationSettings generationSettings)
        {
            m_TotalCharacterCount = 0;
            m_IsTextLayoutPhase = false;
            m_TagNoParsing = false;
            m_FontStyleInternal = generationSettings.fontStyle;
            m_FontStyleStack.Clear();
            m_FontWeightInternal = StartingWeight(generationSettings);
            m_FontWeightStack.SetDefault(m_FontWeightInternal);
            m_CurrentFontAsset = generationSettings.fontAsset;
            m_CurrentMaterial = generationSettings.fontAsset.material;
            m_CurrentMaterialIndex = 0;
            m_MaterialReferenceStack.SetDefault(new MaterialReference(m_CurrentMaterialIndex, m_CurrentFontAsset, null, m_CurrentMaterial, m_Padding));
            m_MaterialReferenceIndexLookup.Clear();
            MaterialReference.AddMaterialReference(m_CurrentMaterial, m_CurrentFontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
        }

        /// <summary>The ellipsis' material when overflow ends in one (in a fallback font asset it
        /// draws with that font's material); false when that cannot be set up from a job.</summary>
        bool PrepareEllipsis(TextGenerationSettings generationSettings, bool forLookupOnly)
        {
            if (generationSettings.overflowMode != TextOverflowMode.Ellipsis) return true;
            GetEllipsisSpecialCharacter(generationSettings);
            if (m_Ellipsis.character == null)
            {
                if (forLookupOnly) return true;
                generationSettings.overflowMode = TextOverflowMode.Truncate;
                if (generationSettings.textSettings.displayWarnings)
                    Debug.LogWarning("The character used for Ellipsis is not available in font asset [" + m_CurrentFontAsset.name + "] or any potential fallbacks. Switching Text Overflow mode to Truncate.");
                return true;
            }
            if (m_Ellipsis.fontAsset.GetHashCode() == m_CurrentFontAsset.GetHashCode()) return true;
            var textSettings = generationSettings.textSettings;
            if (textSettings.matchMaterialPreset && m_CurrentMaterial.GetHashCode() != m_Ellipsis.fontAsset.material.GetHashCode())
            {
                if (forLookupOnly && !OnMainThread) return false;
                m_Ellipsis.material = MaterialManager.GetFallbackMaterial(m_CurrentMaterial, m_Ellipsis.fontAsset.material);
            }
            else
            {
                m_Ellipsis.material = m_Ellipsis.fontAsset.material;
            }
            m_Ellipsis.materialIndex = MaterialReference.AddMaterialReference(m_Ellipsis.material, m_Ellipsis.fontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
            m_MaterialReferences[m_Ellipsis.materialIndex].referenceCount = 0;
            return true;
        }

        /// <summary>Finds every character's text element (recording it in the text info), counts
        /// the quads of each material and sizes the mesh buffers.</summary>
        int SetArraySizes(TextProcessingElement[] elements, TextGenerationSettings generationSettings, TextInfo textInfo)
        {
            var textSettings = generationSettings.textSettings;
            ResetLookupState(generationSettings);
            textInfo ??= new TextInfo();
            if (textInfo.textElementInfo.Length < m_InternalTextProcessingArraySize)
                TextInfo.Resize(ref textInfo.textElementInfo, m_InternalTextProcessingArraySize, false);
            m_TextElementType = TextElementType.Character;
            PrepareEllipsis(generationSettings, false);
            bool populateLigatures = TextGenerationSettings.fontFeatures.Contains(OTL_FeatureTag.liga);
            for (int i = 0; i < elements.Length && elements[i].unicode != 0; i++)
            {
                if (textInfo.textElementInfo == null || m_TotalCharacterCount >= textInfo.textElementInfo.Length)
                    TextInfo.Resize(ref textInfo.textElementInfo, m_TotalCharacterCount + 1, true);
                uint unicode = elements[i].unicode;
                if (generationSettings.richText && unicode == k_LessThan && ValidateHtmlTag(elements, i + 1, out int endIndex, generationSettings, textInfo, out _))
                {
                    i = endIndex;
                    continue;
                }
                var previousFontAsset = m_CurrentFontAsset;
                var previousMaterial = m_CurrentMaterial;
                int previousMaterialIndex = m_CurrentMaterialIndex;
                if (m_TextElementType == TextElementType.Character) unicode = ApplyCase(unicode);
                uint next = i + 1 < elements.Length ? elements[i + 1].unicode : 0;
                var element = FindTextElement(generationSettings, ref elements[i], ref unicode, next, textInfo, true, out bool alternative);
                if (element == null) continue;
                ref var info = ref textInfo.textElementInfo[m_TotalCharacterCount];
                info.alternativeGlyph = null;
                bool switchedFont = false;
                if (element.elementType == TextElementType.Character)
                {
                    if (element.textAsset.instanceID != m_CurrentFontAsset.instanceID)
                    {
                        switchedFont = true;
                        m_CurrentFontAsset = element.textAsset as FontAsset;
                    }
                    if (IsVariationSelector(next))
                    {
                        if (!m_CurrentFontAsset.TryGetGlyphVariantIndexInternal(unicode, next, out uint variant))
                        {
                            variant = m_CurrentFontAsset.GetGlyphVariantIndex(unicode, next);
                            m_CurrentFontAsset.TryAddGlyphVariantIndexInternal(unicode, next, variant);
                        }
                        if (variant != 0 && m_CurrentFontAsset.TryAddGlyphInternal(variant, out var glyph)) info.alternativeGlyph = glyph;
                        elements[i + 1].unicode = k_Consumed;
                        i++;
                    }
                }
                info.elementType = TextElementType.Character;
                info.textElement = element;
                info.isUsingAlternateTypeface = alternative;
                info.character = (ushort)unicode;
                info.index = elements[i].stringIndex;
                info.stringLength = elements[i].length;
                info.fontAsset = m_CurrentFontAsset;
                if (switchedFont && m_CurrentFontAsset.instanceID != generationSettings.fontAsset.instanceID)
                {
                    m_CurrentMaterial = textSettings.matchMaterialPreset
                        ? MaterialManager.GetFallbackMaterial(m_CurrentMaterial, m_CurrentFontAsset.material)
                        : m_CurrentFontAsset.material;
                    m_CurrentMaterialIndex = MaterialReference.AddMaterialReference(m_CurrentMaterial, m_CurrentFontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
                }
                if (element.glyph.atlasIndex > 0)
                {
                    m_CurrentMaterial = MaterialManager.GetFallbackMaterial(m_CurrentFontAsset, m_CurrentMaterial, element.glyph.atlasIndex);
                    m_CurrentMaterialIndex = MaterialReference.AddMaterialReference(m_CurrentMaterial, m_CurrentFontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
                    switchedFont = true;
                }
                if (!char.IsWhiteSpace((char)unicode) && unicode != k_ZeroWidthSpace)
                {
                    if (generationSettings.isIMGUI && m_MaterialReferences[m_CurrentMaterialIndex].referenceCount >= 16383)
                        m_CurrentMaterialIndex = MaterialReference.AddMaterialReference(new Material(m_CurrentMaterial), m_CurrentFontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
                    m_MaterialReferences[m_CurrentMaterialIndex].referenceCount++;
                }
                info.material = m_CurrentMaterial;
                info.materialReferenceIndex = m_CurrentMaterialIndex;
                m_MaterialReferences[m_CurrentMaterialIndex].isFallbackMaterial = switchedFont;
                if (switchedFont)
                {
                    m_MaterialReferences[m_CurrentMaterialIndex].fallbackMaterial = previousMaterial;
                    m_CurrentFontAsset = previousFontAsset;
                    m_CurrentMaterial = previousMaterial;
                    m_CurrentMaterialIndex = previousMaterialIndex;
                }
                m_TotalCharacterCount++;
            }
            if (m_IsCalculatingPreferredValues)
            {
                m_IsCalculatingPreferredValues = false;
                return m_TotalCharacterCount;
            }
            textInfo.spriteCount = 0;
            int materialCount = textInfo.materialCount = m_MaterialReferenceIndexLookup.Count;
            if (materialCount > textInfo.meshInfo.Length) TextInfo.Resize(ref textInfo.meshInfo, materialCount, false);
            if (m_VertexBufferAutoSizeReduction && textInfo.textElementInfo.Length - m_TotalCharacterCount > 256)
                TextInfo.Resize(ref textInfo.textElementInfo, Mathf.Max(m_TotalCharacterCount + 1, 256), true);
            for (int m = 0; m < materialCount; m++)
            {
                ref var mesh = ref textInfo.meshInfo[m];
                int quads = m_MaterialReferences[m].referenceCount;
                if (mesh.vertexData == null || mesh.vertexBufferSize < quads * 4)
                {
                    if (mesh.vertexData == null) mesh = new MeshInfo(quads + 1, generationSettings.isIMGUI);
                    else mesh.ResizeMeshInfo(quads > 1024 ? quads + 256 : Mathf.NextPowerOfTwo(quads), generationSettings.isIMGUI);
                }
                else if (mesh.vertexBufferSize - quads * 4 > 1024)
                {
                    mesh.ResizeMeshInfo(quads > 1024 ? quads + 256 : Mathf.Max(Mathf.NextPowerOfTwo(quads), 256), generationSettings.isIMGUI);
                }
                mesh.material = m_MaterialReferences[m].material;
                mesh.glyphRenderMode = m_MaterialReferences[m].fontAsset.atlasRenderMode;
            }
            return m_TotalCharacterCount;
        }

        static bool IsVariationSelector(uint c) => (c >= 0xFE00 && c <= 0xFE0F) || (c >= 0xE0100 && c <= 0xE01EF);

        /// <summary>The text element of a character: emoji fallbacks first for emoji, then the
        /// font chain (<see cref="GetTextElement"/>); a missing character is replaced (and the
        /// element rewritten) by the missing-character glyph, else a space, else end of text.</summary>
        TextElement FindTextElement(TextGenerationSettings generationSettings, ref TextProcessingElement source, ref uint unicode, uint next, TextInfo textInfo, bool reportMissing, out bool alternative)
        {
            var textSettings = generationSettings.textSettings;
            bool populateLigatures = TextGenerationSettings.fontFeatures.Contains(OTL_FeatureTag.liga);
            alternative = false;
            TextElement element = null;
            if (generationSettings.emojiFallbackSupport && IsEmojiCandidate(unicode, next) && textSettings.emojiFallbackTextAssets != null && textSettings.emojiFallbackTextAssets.Count > 0)
                element = FontAssetUtilities.GetTextElementFromTextAssets(unicode, m_CurrentFontAsset, textSettings.emojiFallbackTextAssets, true, m_FontStyleInternal, m_FontWeightInternal, out alternative, populateLigatures);
            element ??= GetTextElement(generationSettings, unicode, m_CurrentFontAsset, m_FontStyleInternal, m_FontWeightInternal, out alternative, populateLigatures);
            if (element != null) return element;
            if (reportMissing) DoMissingGlyphCallback(unicode, source.stringIndex, m_CurrentFontAsset, textInfo);
            uint missing = unicode;
            uint replacement = textSettings.missingCharacterUnicode == 0 ? k_WhiteSquare : (uint)textSettings.missingCharacterUnicode;
            source.unicode = unicode = replacement;
            element = FontAssetUtilities.GetCharacterFromFontAsset(replacement, m_CurrentFontAsset, true, m_FontStyleInternal, m_FontWeightInternal, out alternative, populateLigatures)
                ?? FontAssetUtilities.GetCharacterFromFontAssetsInternal(replacement, m_CurrentFontAsset, textSettings.GetFallbackFontAssets(FallbackPixelSize(generationSettings)), textSettings.fallbackOSFontAssets, true, m_FontStyleInternal, m_FontWeightInternal, out alternative, populateLigatures);
            if (element == null && textSettings.defaultFontAsset != null)
                element = FontAssetUtilities.GetCharacterFromFontAsset(replacement, textSettings.defaultFontAsset, true, m_FontStyleInternal, m_FontWeightInternal, out alternative, populateLigatures);
            if (element == null)
            {
                source.unicode = unicode = k_Space;
                element = FontAssetUtilities.GetCharacterFromFontAsset(k_Space, m_CurrentFontAsset, true, m_FontStyleInternal, m_FontWeightInternal, out alternative, populateLigatures);
            }
            if (element == null)
            {
                source.unicode = unicode = k_EndOfText;
                element = FontAssetUtilities.GetCharacterFromFontAsset(k_EndOfText, m_CurrentFontAsset, true, m_FontStyleInternal, m_FontWeightInternal, out alternative, populateLigatures);
            }
            if (textSettings.displayWarnings && element != null)
                Debug.LogWarning((missing > 0xFFFF ? $"The character with Unicode value \\U{missing:X8}" : $"The character with Unicode value \\u{missing:X4}") +
                    $" was not found in the [{generationSettings.fontAsset.name}] font asset or any potential fallbacks. It was replaced by Unicode character \\u{element.unicode:X4}.");
            return element;
        }

        static bool IsEmojiCandidate(uint unicode, uint next) => false;

        /// <summary>A character's element in a font asset or its fallbacks, then the generation font
        /// asset and its fallbacks, the settings' fallbacks and OS fallbacks, and the default font
        /// asset. Characters found in another font asset are cached with this one.</summary>
        TextElement GetTextElement(TextGenerationSettings generationSettings, uint unicode, FontAsset fontAsset, FontStyles fontStyle, TextFontWeight fontWeight, out bool isUsingAlternativeTypeface, bool populateLigatures)
        {
            bool onMainThread = OnMainThread;
            var textSettings = generationSettings.textSettings;
            var character = FontAssetUtilities.GetCharacterFromFontAsset(unicode, fontAsset, false, fontStyle, fontWeight, out isUsingAlternativeTypeface, populateLigatures);
            if (character != null) return character;
            if (!onMainThread && (fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic || fontAsset.atlasPopulationMode == AtlasPopulationMode.DynamicOS)) return null;
            if (fontAsset.m_FallbackFontAssetTable != null && fontAsset.m_FallbackFontAssetTable.Count > 0)
                character = FontAssetUtilities.GetCharacterFromFontAssetsInternal(unicode, fontAsset, fontAsset.m_FallbackFontAssetTable, null, true, fontStyle, fontWeight, out isUsingAlternativeTypeface, populateLigatures);
            if (character != null) return CacheFound(fontAsset, unicode, character, fontStyle, fontWeight, isUsingAlternativeTypeface);
            bool isGenerationAsset = onMainThread ? fontAsset.instanceID == generationSettings.fontAsset.instanceID : fontAsset == generationSettings.fontAsset;
            if (!isGenerationAsset)
            {
                character = FontAssetUtilities.GetCharacterFromFontAsset(unicode, generationSettings.fontAsset, false, fontStyle, fontWeight, out isUsingAlternativeTypeface, populateLigatures);
                if (character != null)
                {
                    m_CurrentMaterialIndex = 0;
                    m_CurrentMaterial = m_MaterialReferences[0].material;
                    fontAsset.AddCharacterToLookupCache(unicode, character, fontStyle, fontWeight);
                    return character;
                }
                var fallbacks = generationSettings.fontAsset.m_FallbackFontAssetTable;
                if (fallbacks != null && fallbacks.Count > 0)
                    character = FontAssetUtilities.GetCharacterFromFontAssetsInternal(unicode, fontAsset, fallbacks, null, true, fontStyle, fontWeight, out isUsingAlternativeTypeface, populateLigatures);
                if (character != null) return CacheFound(fontAsset, unicode, character, fontStyle, fontWeight, isUsingAlternativeTypeface);
            }
            if (textSettings.GetStaticFallbackOSFontAsset() == null && !onMainThread) return null;
            character = FontAssetUtilities.GetCharacterFromFontAssetsInternal(unicode, fontAsset, textSettings.GetFallbackFontAssets(FallbackPixelSize(generationSettings)), textSettings.fallbackOSFontAssets, true, fontStyle, fontWeight, out isUsingAlternativeTypeface, populateLigatures);
            if (character != null) return CacheFound(fontAsset, unicode, character, fontStyle, fontWeight, isUsingAlternativeTypeface);
            if (textSettings.defaultFontAsset != null)
                character = FontAssetUtilities.GetCharacterFromFontAsset(unicode, textSettings.defaultFontAsset, true, fontStyle, fontWeight, out isUsingAlternativeTypeface, populateLigatures);
            if (character != null) return CacheFound(fontAsset, unicode, character, fontStyle, fontWeight, isUsingAlternativeTypeface);
            return null;
        }

        static TextElement CacheFound(FontAsset fontAsset, uint unicode, Character character, FontStyles fontStyle, TextFontWeight fontWeight, bool alternative)
        {
            if (alternative) fontAsset.AddCharacterToLookupCache(unicode, character, fontStyle, fontWeight);
            else fontAsset.AddCharacterToLookupCache(unicode, character, FontStyles.Normal, TextFontWeight.Regular);
            return character;
        }

        /// <summary><see cref="SetArraySizes"/> without a text info: finds (and adds) the characters'
        /// glyphs. False when a job would have to load something from the main thread.</summary>
        bool PopulateFontAsset(TextGenerationSettings generationSettings, TextProcessingElement[] elements)
        {
            bool onMainThread = OnMainThread;
            var textSettings = generationSettings.textSettings;
            ResetLookupState(generationSettings);
            m_TextElementType = TextElementType.Character;
            if (!PrepareEllipsis(generationSettings, true)) return false;
            bool populateLigatures = TextGenerationSettings.fontFeatures.Contains(OTL_FeatureTag.liga);
            for (int i = 0; i < elements.Length && elements[i].unicode != 0; i++)
            {
                uint unicode = elements[i].unicode;
                if (generationSettings.richText && unicode == k_LessThan)
                {
                    if (ValidateHtmlTag(elements, i + 1, out int endIndex, generationSettings, null, out bool threadSuccess))
                    {
                        i = endIndex;
                        continue;
                    }
                    if (!threadSuccess) return false;
                }
                var previousFontAsset = m_CurrentFontAsset;
                var previousMaterial = m_CurrentMaterial;
                int previousMaterialIndex = m_CurrentMaterialIndex;
                if (m_TextElementType == TextElementType.Character) unicode = ApplyCase(unicode);
                if (!onMainThread && m_CurrentFontAsset.m_CharacterLookupDictionary == null) return false;
                uint next = i + 1 < elements.Length ? elements[i + 1].unicode : 0;
                TextElement element;
                if (onMainThread) element = FindTextElement(generationSettings, ref elements[i], ref unicode, next, null, false, out _);
                else
                {
                    element = GetTextElement(generationSettings, unicode, m_CurrentFontAsset, m_FontStyleInternal, m_FontWeightInternal, out _, populateLigatures);
                    if (element == null) return false;
                }
                if (element == null) continue;
                bool switchedFont = false;
                if (element.elementType == TextElementType.Character)
                {
                    bool sameAsset = onMainThread ? element.textAsset.instanceID == m_CurrentFontAsset.instanceID : element.textAsset == m_CurrentFontAsset;
                    if (!sameAsset)
                    {
                        switchedFont = true;
                        m_CurrentFontAsset = element.textAsset as FontAsset;
                    }
                    if (IsVariationSelector(next))
                    {
                        if (!m_CurrentFontAsset.TryGetGlyphVariantIndexInternal(unicode, next, out uint variant))
                        {
                            if (!onMainThread) return false;
                            variant = m_CurrentFontAsset.GetGlyphVariantIndex(unicode, next);
                            m_CurrentFontAsset.TryAddGlyphVariantIndexInternal(unicode, next, variant);
                        }
                        if (variant != 0) m_CurrentFontAsset.TryAddGlyphInternal(variant, out _);
                        elements[i + 1].unicode = k_Consumed;
                        i++;
                    }
                }
                if (switchedFont && m_CurrentFontAsset.instanceID != generationSettings.fontAsset.instanceID)
                {
                    if (!onMainThread && textSettings.matchMaterialPreset) return false;
                    m_CurrentMaterial = textSettings.matchMaterialPreset
                        ? MaterialManager.GetFallbackMaterial(m_CurrentMaterial, m_CurrentFontAsset.material)
                        : m_CurrentFontAsset.material;
                    m_CurrentMaterialIndex = MaterialReference.AddMaterialReference(m_CurrentMaterial, m_CurrentFontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
                }
                if (element.glyph.atlasIndex > 0)
                {
                    if (!onMainThread) return false;
                    m_CurrentMaterial = MaterialManager.GetFallbackMaterial(m_CurrentFontAsset, m_CurrentMaterial, element.glyph.atlasIndex);
                    m_CurrentMaterialIndex = MaterialReference.AddMaterialReference(m_CurrentMaterial, m_CurrentFontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
                    switchedFont = true;
                }
                if (!char.IsWhiteSpace((char)unicode) && unicode != k_ZeroWidthSpace)
                {
                    if (generationSettings.isIMGUI && m_MaterialReferences[m_CurrentMaterialIndex].referenceCount >= 16383)
                        m_CurrentMaterialIndex = MaterialReference.AddMaterialReference(new Material(m_CurrentMaterial), m_CurrentFontAsset, ref m_MaterialReferences, m_MaterialReferenceIndexLookup);
                    m_MaterialReferences[m_CurrentMaterialIndex].referenceCount++;
                }
                m_MaterialReferences[m_CurrentMaterialIndex].isFallbackMaterial = switchedFont;
                if (switchedFont)
                {
                    m_MaterialReferences[m_CurrentMaterialIndex].fallbackMaterial = previousMaterial;
                    m_CurrentFontAsset = previousFontAsset;
                    m_CurrentMaterial = previousMaterial;
                    m_CurrentMaterialIndex = previousMaterialIndex;
                }
                m_TotalCharacterCount++;
            }
            return true;
        }
    }
}
