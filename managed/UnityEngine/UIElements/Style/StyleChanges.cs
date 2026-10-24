using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    internal sealed partial class ComputedValues
    {
        /// <summary>What rendering must do about a new computed style (Unity's
        /// ComputedStyle.CompareChanges, its rendering flags): repaint (geometry), color, opacity,
        /// clipping (overflow, radii) and border width changes. A style that only differs in layout,
        /// transform or custom properties keeps the element's geometry; layout size changes repaint
        /// through the layout update.</summary>
        internal static VersionChangeType CompareRenderingChanges(ComputedValues x, ComputedValues y)
        {
            VersionChangeType change = 0;
            bool layout = x.display != y.display || x.flexGrow != y.flexGrow || x.flexShrink != y.flexShrink || x.flexWrap != y.flexWrap
                || x.flexDirection != y.flexDirection || x.justifyContent != y.justifyContent || x.bottom != y.bottom || x.left != y.left
                || x.right != y.right || x.top != y.top || x.height != y.height || x.width != y.width || x.paddingBottom != y.paddingBottom
                || x.paddingLeft != y.paddingLeft || x.paddingRight != y.paddingRight || x.paddingTop != y.paddingTop
                || x.marginBottom != y.marginBottom || x.marginLeft != y.marginLeft || x.marginRight != y.marginRight
                || x.marginTop != y.marginTop || x.position != y.position || x.alignContent != y.alignContent || x.alignItems != y.alignItems
                || x.alignSelf != y.alignSelf || x.flexBasis != y.flexBasis || x.maxHeight != y.maxHeight || x.maxWidth != y.maxWidth
                || x.minHeight != y.minHeight || x.minWidth != y.minWidth;
            if (x.borderBottomWidth != y.borderBottomWidth || x.borderLeftWidth != y.borderLeftWidth
                || x.borderRightWidth != y.borderRightWidth || x.borderTopWidth != y.borderTopWidth)
            {
                layout = true;
                change |= VersionChangeType.BorderWidth | VersionChangeType.Repaint;
            }
            if (x.color != y.color) change |= VersionChangeType.Color;
            if (!layout && (change & VersionChangeType.Repaint) == 0
                && (x.unityFont != y.unityFont || x.unityTextGenerator != y.unityTextGenerator || x.fontSize != y.fontSize
                    || x.unityFontDefinition != y.unityFontDefinition || x.whiteSpace != y.whiteSpace
                    || x.unityFontStyleAndWeight != y.unityFontStyleAndWeight || x.unityTextOutlineWidth != y.unityTextOutlineWidth
                    || x.letterSpacing != y.letterSpacing || x.wordSpacing != y.wordSpacing
                    || x.unityEditorTextRenderingMode != y.unityEditorTextRenderingMode || x.unityParagraphSpacing != y.unityParagraphSpacing))
            {
                layout = true;
                change |= VersionChangeType.Repaint;
            }
            if ((change & VersionChangeType.Repaint) == 0
                && (x.textShadow != y.textShadow || x.unityTextAlign != y.unityTextAlign || x.unityTextOutlineColor != y.unityTextOutlineColor))
                change |= VersionChangeType.Repaint;
            if (x.visibility != y.visibility) change |= VersionChangeType.Repaint | VersionChangeType.Picking;
            if ((change & VersionChangeType.Color) == 0
                && (x.backgroundColor != y.backgroundColor || x.borderBottomColor != y.borderBottomColor || x.borderLeftColor != y.borderLeftColor
                    || x.borderRightColor != y.borderRightColor || x.borderTopColor != y.borderTopColor))
                change |= VersionChangeType.Color;
            if (x.backgroundImage != y.backgroundImage) change |= VersionChangeType.Overflow | VersionChangeType.Repaint;
            if ((change & VersionChangeType.Repaint) == 0
                && (x.backgroundPositionX != y.backgroundPositionX || x.backgroundPositionY != y.backgroundPositionY
                    || x.backgroundRepeat != y.backgroundRepeat || x.backgroundSize != y.backgroundSize))
                change |= VersionChangeType.Repaint;
            if (x.borderBottomLeftRadius != y.borderBottomLeftRadius || x.borderBottomRightRadius != y.borderBottomRightRadius
                || x.borderTopLeftRadius != y.borderTopLeftRadius || x.borderTopRightRadius != y.borderTopRightRadius)
                change |= VersionChangeType.BorderRadius | VersionChangeType.Repaint;
            if (x.opacity != y.opacity) change |= VersionChangeType.Opacity;
            if (x.overflow != y.overflow)
            {
                layout = true;
                change |= VersionChangeType.Overflow;
            }
            if (!layout && (change & VersionChangeType.Repaint) == 0
                && (x.unitySliceType != y.unitySliceType || x.textOverflow != y.textOverflow || x.unitySliceScale != y.unitySliceScale))
                change |= VersionChangeType.Repaint;
            if (x.unityBackgroundImageTintColor != y.unityBackgroundImageTintColor) change |= VersionChangeType.Color;
            if ((change & VersionChangeType.Repaint) == 0
                && (x.unityOverflowClipBox != y.unityOverflowClipBox || x.unitySliceBottom != y.unitySliceBottom || x.unitySliceLeft != y.unitySliceLeft
                    || x.unitySliceRight != y.unitySliceRight || x.unitySliceTop != y.unitySliceTop
                    || x.unityTextOverflowPosition != y.unityTextOverflowPosition))
                change |= VersionChangeType.Repaint;
            return change;
        }
    }
}
