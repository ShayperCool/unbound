using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>The laid out characters of a text: per character its pen position and advance, per
    /// line its top, height and first character (positions relative to the content rect).</summary>
    internal sealed class UaTextLayout
    {
        internal string text = string.Empty;
        internal float[] x = Array.Empty<float>();
        internal float[] advance = Array.Empty<float>();
        internal int[] line = Array.Empty<int>();
        internal int[] lineFirst = { 0 };
        internal float[] lineTop = { 0f };
        internal float[] lineHeight = { 0f };
        internal int lineCount = 1;
    }

    /// <summary>A text element's text as the text engine lays it out: measured for layout,
    /// generated into glyph quads for rendering, and queried for cursor positions.</summary>
    internal sealed partial class UaTextHandle
    {
        readonly TextElement m_Element;
        bool m_Dirty = true;
        UaTextLayout m_Layout;
        float m_LayoutWidth = float.NaN;

        internal UaTextHandle(TextElement element) { m_Element = element; }

        internal bool isElided { get; private set; }

        internal void MarkDirty()
        {
            m_Dirty = true;
            m_Layout = null;
        }

        /// <summary>The text the cursor moves through: the element's rendered text.</summary>
        internal string layoutText => m_Element.renderedTextString;

        internal bool isPlaceholder => m_Element.showPlaceholderText;

        internal static bool SameTextStyle(ComputedValues a, ComputedValues b) =>
            a.fontSize == b.fontSize && a.unityFont == b.unityFont && a.unityFontDefinition == b.unityFontDefinition &&
            a.unityFontStyleAndWeight == b.unityFontStyleAndWeight && a.letterSpacing == b.letterSpacing && a.wordSpacing == b.wordSpacing &&
            a.unityParagraphSpacing == b.unityParagraphSpacing && a.whiteSpace == b.whiteSpace && a.unityTextAlign == b.unityTextAlign &&
            a.textOverflow == b.textOverflow && a.unityTextOverflowPosition == b.unityTextOverflowPosition;

        internal Vector2 Measure(string text, float width, VisualElement.MeasureMode widthMode, float height, VisualElement.MeasureMode heightMode)
        {
            float w = float.NaN, h = float.NaN;
            if (!IsFontAssigned()) return new Vector2(w, h);
            float ppp = m_Element.panel != null ? m_Element.scaledPixelsPerPoint : 1f;
            if (ppp <= 0f) return Vector2.zero;
            if (widthMode != VisualElement.MeasureMode.Exactly || heightMode != VisualElement.MeasureMode.Exactly)
            {
                var size = ComputeTextSize(text, width, height);
                w = size.x;
                h = size.y;
            }
            if (widthMode == VisualElement.MeasureMode.Exactly) w = width;
            else if (widthMode == VisualElement.MeasureMode.AtMost) w = Mathf.Min(w, width);
            if (heightMode == VisualElement.MeasureMode.Exactly) h = height;
            else if (heightMode == VisualElement.MeasureMode.AtMost) h = Mathf.Min(h, height);
            return new Vector2(UaLayout.CeilToPixelGrid(w, ppp, 0f), UaLayout.CeilToPixelGrid(h, ppp, 0f));
        }

        bool IsFontAssigned()
        {
            var c = m_Element.computedValues;
            return c.unityFont != null || c.unityFontDefinition.font != null || c.unityFontDefinition.fontAsset != null;
        }

        partial void ComputeTextSizeImpl(string text, float width, float height, ref Vector2 size);

        Vector2 ComputeTextSize(string text, float width, float height)
        {
            var size = new Vector2(float.NaN, float.NaN);
            ComputeTextSizeImpl(text, width, height, ref size);
            return size;
        }

        /// <summary>The text with an ellipsis where it overflows the width (filled by the text engine).</summary>
        internal string Elide(string text, string ellipsis, float width, TextOverflowPosition position)
        {
            string result = text;
            ElideImpl(text, ellipsis, width, position, ref result);
            isElided = !string.Equals(result, text, StringComparison.Ordinal);
            return result;
        }

        partial void ElideImpl(string text, string ellipsis, float width, TextOverflowPosition position, ref string result);

        partial void LayoutImpl(string text, float width, ref UaTextLayout layout);

        /// <summary>The characters of the rendered text laid out at the element's content width.</summary>
        internal UaTextLayout layout
        {
            get
            {
                float width = m_Element.contentRect.width;
                if (m_Layout != null && !m_Dirty && (width == m_LayoutWidth || (float.IsNaN(width) && float.IsNaN(m_LayoutWidth)))) return m_Layout;
                var text = layoutText;
                UaTextLayout result = null;
                LayoutImpl(text, width, ref result);
                m_Layout = result ?? FallbackLayout(text);
                m_LayoutWidth = width;
                m_Dirty = false;
                return m_Layout;
            }
        }

        /// <summary>One line of evenly advancing characters, until the text engine provides positions.</summary>
        UaTextLayout FallbackLayout(string text)
        {
            float size = m_Element.computedValues.fontSize.value;
            if (size <= 0f) size = 12f;
            var l = new UaTextLayout { text = text, x = new float[text.Length], advance = new float[text.Length], line = new int[text.Length] };
            float pen = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                l.x[i] = pen;
                l.advance[i] = text[i] == '​' ? 0f : size * 0.5f;
                pen += l.advance[i];
            }
            l.lineHeight[0] = size * 1.2f;
            return l;
        }

        /// <summary>The caret position before a character: its pen x, the bottom of its line.</summary>
        internal Vector2 GetCursorPositionFromStringIndexUsingLineHeight(int index)
        {
            var l = layout;
            if (l.text.Length == 0) return new Vector2(0f, l.lineHeight[0]);
            int line;
            float x;
            if (index < l.text.Length)
            {
                index = Math.Max(0, index);
                line = l.line[index];
                x = l.x[index];
            }
            else
            {
                int last = l.text.Length - 1;
                line = l.line[last];
                x = l.x[last] + l.advance[last];
            }
            return new Vector2(x, l.lineTop[line] + l.lineHeight[line]);
        }

        internal float GetLineHeightFromCharacterIndex(int index)
        {
            var l = layout;
            if (l.text.Length == 0) return l.lineHeight[0];
            int line = l.line[Mathf.Clamp(index, 0, l.text.Length - 1)];
            return l.lineHeight[line];
        }

        /// <summary>The cursor index nearest to a position in the content rect.</summary>
        internal int GetCursorIndexFromPosition(Vector2 position)
        {
            var l = layout;
            if (l.text.Length == 0) return 0;
            int line = l.lineCount - 1;
            for (int i = 0; i < l.lineCount; i++)
            {
                if (position.y < l.lineTop[i] + l.lineHeight[i])
                {
                    line = i;
                    break;
                }
            }
            int first = l.lineFirst[line];
            int end = line + 1 < l.lineCount ? l.lineFirst[line + 1] : l.text.Length;
            for (int i = first; i < end; i++)
            {
                if (l.text[i] == '\n') return i;
                if (position.x < l.x[i] + l.advance[i] * 0.5f) return i;
            }
            int count = l.text.Length;
            if (count > 0 && l.text[count - 1] == '​' && end == count) return count - 1;
            return end;
        }

        internal int LineUpCharacterPosition(int index)
        {
            var p = GetCursorPositionFromStringIndexUsingLineHeight(index);
            var l = layout;
            int line = l.text.Length == 0 ? 0 : l.line[Mathf.Clamp(index, 0, l.text.Length - 1)];
            if (line == 0) return 0;
            return GetCursorIndexFromPosition(new Vector2(p.x, l.lineTop[line - 1] + l.lineHeight[line - 1] * 0.5f));
        }

        internal int LineDownCharacterPosition(int index)
        {
            var p = GetCursorPositionFromStringIndexUsingLineHeight(index);
            var l = layout;
            int line = l.text.Length == 0 ? 0 : l.line[Mathf.Clamp(index, 0, l.text.Length - 1)];
            if (line >= l.lineCount - 1) return l.text.Length > 0 && l.text[l.text.Length - 1] == '​' ? l.text.Length - 1 : l.text.Length;
            return GetCursorIndexFromPosition(new Vector2(p.x, l.lineTop[line + 1] + l.lineHeight[line + 1] * 0.5f));
        }
    }
}
