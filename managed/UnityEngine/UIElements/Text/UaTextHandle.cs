using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>A text element's text as the text engine lays it out: measured for layout,
    /// generated into glyph quads for rendering.</summary>
    internal sealed partial class UaTextHandle
    {
        readonly TextElement m_Element;
        bool m_Dirty = true;

        internal UaTextHandle(TextElement element) { m_Element = element; }

        internal bool isElided { get; private set; }

        internal void MarkDirty() => m_Dirty = true;

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
    }
}
