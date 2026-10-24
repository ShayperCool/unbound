namespace UnityEngine.TextCore.Text
{
    internal partial class TextGenerator
    {
        /// <summary>The layout state at a character, saved where a line may break (and at the last
        /// valid character, the start of the line, ellipsis candidates) to go back to.</summary>
        struct LayoutState
        {
            internal int previousWordBreak;
            internal int totalCharacterCount;
            internal int visibleCharacterCount;
            internal int visibleSpaceCount;
            internal int visibleLinkCount;
            internal int firstCharacterIndex;
            internal int firstVisibleCharacterIndex;
            internal int lastVisibleCharIndex;
            internal int lineNumber;
            internal float maxCapHeight;
            internal float maxAscender;
            internal float maxDescender;
            internal float maxLineAscender;
            internal float maxLineDescender;
            internal float startOfLineAscender;
            internal float xAdvance;
            internal float preferredWidth;
            internal float preferredHeight;
            internal float pageAscender;
            internal FontStyles fontStyle;
            internal float fontScaleMultiplier;
            internal int italicAngle;
            internal float currentFontSize;
            internal float baselineOffset;
            internal float lineOffset;
            internal LineInfo lineInfo;
            internal Color32 vertexColor;
            internal Color32 underlineColor;
            internal Color32 strikethroughColor;
            internal Color32 highlightColor;
            internal HighlightState highlightState;
            internal FontStyleStack basicStyleStack;
            internal TextProcessingStack<int> italicAngleStack;
            internal TextProcessingStack<Color32> colorStack;
            internal TextProcessingStack<Color32> underlineColorStack;
            internal TextProcessingStack<Color32> strikethroughColorStack;
            internal TextProcessingStack<HighlightState> highlightStateStack;
            internal TextProcessingStack<float> sizeStack;
            internal TextProcessingStack<float> indentStack;
            internal TextProcessingStack<TextFontWeight> fontWeightStack;
            internal TextProcessingStack<float> baselineStack;
            internal TextProcessingStack<int> actionStack;
            internal TextProcessingStack<MaterialReference> materialReferenceStack;
            internal TextProcessingStack<TextAlignment> lineJustificationStack;
            internal int lastBaseGlyphIndex;
            internal FontAsset currentFontAsset;
            internal Material currentMaterial;
            internal int currentMaterialIndex;
            internal Extents meshExtents;
            internal bool tagNoParsing;
            internal bool isNonBreakingSpace;
            internal bool isDrivenLineSpacing;
            internal Vector3 fxScale;
            internal Quaternion fxRotation;
        }

        LayoutState m_SavedWordWrapState;
        LayoutState m_SavedLineState;
        LayoutState m_SavedEllipsisState;
        LayoutState m_SavedLastValidState;
        LayoutState m_SavedSoftLineBreakState;
        TextProcessingStack<LayoutState> m_EllipsisInsertionCandidateStack = new TextProcessingStack<LayoutState>(8, 8);

        void SaveWordWrappingState(ref LayoutState state, int index, int count, TextInfo textInfo)
        {
            state.currentFontAsset = m_CurrentFontAsset;
            state.currentMaterial = m_CurrentMaterial;
            state.currentMaterialIndex = m_CurrentMaterialIndex;
            state.previousWordBreak = index;
            state.totalCharacterCount = count;
            state.visibleCharacterCount = m_LineVisibleCharacterCount;
            state.visibleSpaceCount = m_LineVisibleSpaceCount;
            state.visibleLinkCount = textInfo.linkCount;
            state.firstCharacterIndex = m_FirstCharacterOfLine;
            state.firstVisibleCharacterIndex = m_FirstVisibleCharacterOfLine;
            state.lastVisibleCharIndex = m_LastVisibleCharacterOfLine;
            state.fontStyle = m_FontStyleInternal;
            state.italicAngle = m_ItalicAngle;
            state.fontScaleMultiplier = m_FontScaleMultiplier;
            state.currentFontSize = m_CurrentFontSize;
            state.xAdvance = m_XAdvance;
            state.maxCapHeight = m_MaxCapHeight;
            state.maxAscender = m_MaxAscender;
            state.maxDescender = m_MaxDescender;
            state.maxLineAscender = m_MaxLineAscender;
            state.maxLineDescender = m_MaxLineDescender;
            state.startOfLineAscender = m_StartOfLineAscender;
            state.preferredWidth = m_PreferredWidth;
            state.preferredHeight = m_PreferredHeight;
            state.meshExtents = m_MeshExtents;
            state.pageAscender = m_PageAscender;
            state.lineNumber = m_LineNumber;
            state.lineOffset = m_LineOffset;
            state.baselineOffset = m_BaselineOffset;
            state.isDrivenLineSpacing = m_IsDrivenLineSpacing;
            state.vertexColor = m_HtmlColor;
            state.underlineColor = m_UnderlineColor;
            state.strikethroughColor = m_StrikethroughColor;
            state.highlightColor = m_HighlightColor;
            state.highlightState = m_HighlightState;
            state.isNonBreakingSpace = m_IsNonBreakingSpace;
            state.tagNoParsing = m_TagNoParsing;
            state.fxScale = m_FXScale;
            state.fxRotation = m_FXRotation;
            state.basicStyleStack = m_FontStyleStack;
            state.italicAngleStack = m_ItalicAngleStack;
            state.colorStack = m_ColorStack;
            state.underlineColorStack = m_UnderlineColorStack;
            state.strikethroughColorStack = m_StrikethroughColorStack;
            state.highlightStateStack = m_HighlightStateStack;
            state.sizeStack = m_SizeStack;
            state.indentStack = m_IndentStack;
            state.fontWeightStack = m_FontWeightStack;
            state.baselineStack = m_BaselineOffsetStack;
            state.actionStack = m_ActionStack;
            state.materialReferenceStack = m_MaterialReferenceStack;
            state.lineJustificationStack = m_LineJustificationStack;
            state.lastBaseGlyphIndex = m_LastBaseGlyphIndex;
            if (m_LineNumber < textInfo.lineInfo.Length) state.lineInfo = textInfo.lineInfo[m_LineNumber];
        }

        /// <summary>Goes back to a saved state; returns the processing index it was saved at.</summary>
        int RestoreWordWrappingState(ref LayoutState state, TextInfo textInfo)
        {
            m_CurrentFontAsset = state.currentFontAsset;
            m_CurrentMaterial = state.currentMaterial;
            m_CurrentMaterialIndex = state.currentMaterialIndex;
            m_CharacterCount = state.totalCharacterCount + 1;
            m_LineVisibleCharacterCount = state.visibleCharacterCount;
            m_LineVisibleSpaceCount = state.visibleSpaceCount;
            textInfo.linkCount = state.visibleLinkCount;
            m_FirstCharacterOfLine = state.firstCharacterIndex;
            m_FirstVisibleCharacterOfLine = state.firstVisibleCharacterIndex;
            m_LastVisibleCharacterOfLine = state.lastVisibleCharIndex;
            m_FontStyleInternal = state.fontStyle;
            m_ItalicAngle = state.italicAngle;
            m_FontScaleMultiplier = state.fontScaleMultiplier;
            m_CurrentFontSize = state.currentFontSize;
            m_XAdvance = state.xAdvance;
            m_MaxCapHeight = state.maxCapHeight;
            m_MaxAscender = state.maxAscender;
            m_MaxDescender = state.maxDescender;
            m_MaxLineAscender = state.maxLineAscender;
            m_MaxLineDescender = state.maxLineDescender;
            m_StartOfLineAscender = state.startOfLineAscender;
            m_PreferredWidth = state.preferredWidth;
            m_PreferredHeight = state.preferredHeight;
            m_MeshExtents = state.meshExtents;
            m_PageAscender = state.pageAscender;
            m_LineNumber = state.lineNumber;
            m_LineOffset = state.lineOffset;
            m_BaselineOffset = state.baselineOffset;
            m_IsDrivenLineSpacing = state.isDrivenLineSpacing;
            m_HtmlColor = state.vertexColor;
            m_UnderlineColor = state.underlineColor;
            m_StrikethroughColor = state.strikethroughColor;
            m_HighlightColor = state.highlightColor;
            m_HighlightState = state.highlightState;
            m_IsNonBreakingSpace = state.isNonBreakingSpace;
            m_TagNoParsing = state.tagNoParsing;
            m_FXScale = state.fxScale;
            m_FXRotation = state.fxRotation;
            m_FontStyleStack = state.basicStyleStack;
            m_ItalicAngleStack = state.italicAngleStack;
            m_ColorStack = state.colorStack;
            m_UnderlineColorStack = state.underlineColorStack;
            m_StrikethroughColorStack = state.strikethroughColorStack;
            m_HighlightStateStack = state.highlightStateStack;
            m_SizeStack = state.sizeStack;
            m_IndentStack = state.indentStack;
            m_FontWeightStack = state.fontWeightStack;
            m_BaselineOffsetStack = state.baselineStack;
            m_ActionStack = state.actionStack;
            m_MaterialReferenceStack = state.materialReferenceStack;
            m_LineJustificationStack = state.lineJustificationStack;
            m_LastBaseGlyphIndex = state.lastBaseGlyphIndex;
            if (m_LineNumber < textInfo.lineInfo.Length) textInfo.lineInfo[m_LineNumber] = state.lineInfo;
            return state.previousWordBreak;
        }
    }
}
