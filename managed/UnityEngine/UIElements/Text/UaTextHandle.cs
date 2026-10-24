using System;
using UnityEngine;
using RenderedText = UnityEngine.TextCore.Text.RenderedText;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>A text element's text as the TextCore engine lays it out (through its
    /// <see cref="UITKTextHandle"/>): measured for layout, generated for rendering, and queried for
    /// cursor positions (in the content rect, y down).</summary>
    internal sealed partial class UaTextHandle
    {
        readonly TextElement m_Element;
        internal readonly UITKTextHandle handle;

        internal UaTextHandle(TextElement element, UITKTextHandle textHandle)
        {
            m_Element = element;
            handle = textHandle;
        }

        internal bool isElided => handle.IsElided();

        internal void MarkDirty() => handle.SetDirty();

        /// <summary>The text the cursor moves through: the element's rendered text.</summary>
        internal string layoutText => m_Element.renderedTextString;

        internal bool isPlaceholder => m_Element.showPlaceholderText;

        internal static bool SameTextStyle(ComputedValues a, ComputedValues b) =>
            a.fontSize == b.fontSize && a.unityFont == b.unityFont && a.unityFontDefinition == b.unityFontDefinition &&
            a.unityFontStyleAndWeight == b.unityFontStyleAndWeight && a.letterSpacing == b.letterSpacing && a.wordSpacing == b.wordSpacing &&
            a.unityParagraphSpacing == b.unityParagraphSpacing && a.whiteSpace == b.whiteSpace && a.unityTextAlign == b.unityTextAlign &&
            a.textOverflow == b.textOverflow && a.unityTextOverflowPosition == b.unityTextOverflowPosition;

        /// <summary>The layout size of a text: its preferred size within the given constraints, ceiled
        /// to the pixel grid (the unrounded size is kept to lay the text out in the rounded box).</summary>
        internal Vector2 Measure(string text, float width, VisualElement.MeasureMode widthMode, float height, VisualElement.MeasureMode heightMode)
        {
            float w = float.NaN, h = float.NaN;
            if (!UITKTextHandle.IsFontAssigned(m_Element)) return new Vector2(w, h);
            float ppp = m_Element.panel != null ? m_Element.scaledPixelsPerPoint : 1f;
            if (ppp <= 0f) return Vector2.zero;
            if (widthMode != VisualElement.MeasureMode.Exactly || heightMode != VisualElement.MeasureMode.Exactly)
            {
                var size = handle.ComputeTextSize(new RenderedText(text), width, height);
                w = size.x;
                h = size.y;
            }
            if (widthMode == VisualElement.MeasureMode.Exactly) w = width;
            else if (widthMode == VisualElement.MeasureMode.AtMost) w = Mathf.Min(w, width);
            if (heightMode == VisualElement.MeasureMode.Exactly) h = height;
            else if (heightMode == VisualElement.MeasureMode.AtMost) h = Mathf.Min(h, height);
            var rounded = new Vector2(UaLayout.CeilToPixelGrid(w, ppp, 0f), UaLayout.CeilToPixelGrid(h, ppp, 0f));
            handle.MeasuredSizes = new Vector2(w, h);
            handle.RoundedSizes = rounded;
            return rounded;
        }

        /// <summary>Generates the text into the handle's permanent cache so that cursor queries see it.</summary>
        void EnsureTextInfo() => handle.AddTextInfoToPermanentCache();

        internal Vector2 GetCursorPositionFromStringIndexUsingLineHeight(int index)
        {
            EnsureTextInfo();
            return handle.textInfo.characterCount == 0 ? Vector2.zero : handle.GetCursorPositionFromStringIndexUsingLineHeight(index);
        }

        internal float GetLineHeightFromCharacterIndex(int index)
        {
            EnsureTextInfo();
            return handle.textInfo.characterCount == 0 ? 0f : handle.GetLineHeightFromCharacterIndex(index);
        }

        internal int GetCursorIndexFromPosition(Vector2 position)
        {
            EnsureTextInfo();
            return handle.textInfo.characterCount == 0 ? 0 : handle.GetCursorIndexFromPosition(position);
        }

        internal int LineUpCharacterPosition(int index)
        {
            EnsureTextInfo();
            return handle.textInfo.characterCount == 0 ? 0 : handle.LineUpCharacterPosition(index);
        }

        internal int LineDownCharacterPosition(int index)
        {
            EnsureTextInfo();
            return handle.textInfo.characterCount == 0 ? 0 : handle.LineDownCharacterPosition(index);
        }

        internal void ReleaseTextInfo()
        {
            handle.RemoveTextInfoFromPermanentCache();
            handle.RemoveTextInfoFromTemporaryCache();
        }
    }
}
