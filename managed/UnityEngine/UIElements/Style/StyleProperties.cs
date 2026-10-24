// The computed style of an element: one field per USS longhand property, with Unity's initial values
// and inheritance, filled from matched style sheet rules and inline styles. Written from a property
// table (names, USS names, types, initial values, how each value is read).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;
using Cursor = UnityEngine.UIElements.Cursor;

namespace Unbound.UIElements
{
    internal sealed partial class ComputedValues
    {
        public Align alignContent;
        public Align alignItems;
        public Align alignSelf;
        public Color backgroundColor;
        public Background backgroundImage;
        public BackgroundPosition backgroundPositionX;
        public BackgroundPosition backgroundPositionY;
        public BackgroundRepeat backgroundRepeat;
        public BackgroundSize backgroundSize;
        public Color borderBottomColor;
        public Length borderBottomLeftRadius;
        public Length borderBottomRightRadius;
        public float borderBottomWidth;
        public Color borderLeftColor;
        public float borderLeftWidth;
        public Color borderRightColor;
        public float borderRightWidth;
        public Color borderTopColor;
        public Length borderTopLeftRadius;
        public Length borderTopRightRadius;
        public float borderTopWidth;
        public Length bottom;
        public Color color;
        public Cursor cursor;
        public DisplayStyle display;
        public Length flexBasis;
        public FlexDirection flexDirection;
        public float flexGrow;
        public float flexShrink;
        public Wrap flexWrap;
        public Length fontSize;
        public Length height;
        public Justify justifyContent;
        public Length left;
        public Length letterSpacing;
        public Length marginBottom;
        public Length marginLeft;
        public Length marginRight;
        public Length marginTop;
        public Length maxHeight;
        public Length maxWidth;
        public Length minHeight;
        public Length minWidth;
        public float opacity;
        public OverflowInternal overflow;
        public Length paddingBottom;
        public Length paddingLeft;
        public Length paddingRight;
        public Length paddingTop;
        public Position position;
        public Length right;
        public Rotate rotate;
        public Scale scale;
        public TextOverflow textOverflow;
        public TextShadow textShadow;
        public Length top;
        public TransformOrigin transformOrigin;
        public List<TimeValue> transitionDelay;
        public List<TimeValue> transitionDuration;
        public List<StylePropertyName> transitionProperty;
        public List<EasingFunction> transitionTimingFunction;
        public Translate translate;
        public Color unityBackgroundImageTintColor;
        public EditorTextRenderingMode unityEditorTextRenderingMode;
        public Font unityFont;
        public FontDefinition unityFontDefinition;
        public FontStyle unityFontStyleAndWeight;
        public OverflowClipBox unityOverflowClipBox;
        public Length unityParagraphSpacing;
        public int unitySliceBottom;
        public int unitySliceLeft;
        public int unitySliceRight;
        public float unitySliceScale;
        public int unitySliceTop;
        public SliceType unitySliceType;
        public TextAnchor unityTextAlign;
        public TextGeneratorType unityTextGenerator;
        public Color unityTextOutlineColor;
        public float unityTextOutlineWidth;
        public TextOverflowPosition unityTextOverflowPosition;
        public Visibility visibility;
        public WhiteSpace whiteSpace;
        public Length width;
        public Length wordSpacing;
        public Dictionary<string, UssValue> customProperties;
        public float dpiScaling = 1f;

        static ComputedValues s_Initial;
        internal static ComputedValues Initial => s_Initial ??= CreateInitial();

        static ComputedValues CreateInitial()
        {
            var c = new ComputedValues();
            c.SetAllInitial();
            return c;
        }

        internal void SetAllInitial()
        {
            alignContent = Align.FlexStart;
            alignItems = Align.Stretch;
            alignSelf = Align.Auto;
            backgroundColor = Color.clear;
            backgroundImage = default(Background);
            backgroundPositionX = BackgroundPosition.Initial();
            backgroundPositionY = BackgroundPosition.Initial();
            backgroundRepeat = BackgroundRepeat.Initial();
            backgroundSize = BackgroundSize.Initial();
            borderBottomColor = Color.clear;
            borderBottomLeftRadius = new Length(0f);
            borderBottomRightRadius = new Length(0f);
            borderBottomWidth = 0f;
            borderLeftColor = Color.clear;
            borderLeftWidth = 0f;
            borderRightColor = Color.clear;
            borderRightWidth = 0f;
            borderTopColor = Color.clear;
            borderTopLeftRadius = new Length(0f);
            borderTopRightRadius = new Length(0f);
            borderTopWidth = 0f;
            bottom = Length.Auto();
            color = Color.black;
            cursor = default(Cursor);
            display = DisplayStyle.Flex;
            flexBasis = Length.Auto();
            flexDirection = FlexDirection.Column;
            flexGrow = 0f;
            flexShrink = 1f;
            flexWrap = Wrap.NoWrap;
            fontSize = new Length(14f);
            height = Length.Auto();
            justifyContent = Justify.FlexStart;
            left = Length.Auto();
            letterSpacing = new Length(0f);
            marginBottom = new Length(0f);
            marginLeft = new Length(0f);
            marginRight = new Length(0f);
            marginTop = new Length(0f);
            maxHeight = Length.None();
            maxWidth = Length.None();
            minHeight = Length.Auto();
            minWidth = Length.Auto();
            opacity = 1f;
            overflow = OverflowInternal.Visible;
            paddingBottom = new Length(0f);
            paddingLeft = new Length(0f);
            paddingRight = new Length(0f);
            paddingTop = new Length(0f);
            position = Position.Relative;
            right = Length.Auto();
            rotate = Rotate.None();
            scale = Scale.None();
            textOverflow = TextOverflow.Clip;
            textShadow = default(TextShadow);
            top = Length.Auto();
            transformOrigin = TransformOrigin.Initial();
            transitionDelay = new List<TimeValue> { new TimeValue(0f) };
            transitionDuration = new List<TimeValue> { new TimeValue(0f) };
            transitionProperty = new List<StylePropertyName> { new StylePropertyName("all") };
            transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.Ease) };
            translate = Translate.None();
            unityBackgroundImageTintColor = Color.white;
            unityEditorTextRenderingMode = EditorTextRenderingMode.SDF;
            unityFont = null;
            unityFontDefinition = default(FontDefinition);
            unityFontStyleAndWeight = FontStyle.Normal;
            unityOverflowClipBox = OverflowClipBox.PaddingBox;
            unityParagraphSpacing = new Length(0f);
            unitySliceBottom = 0;
            unitySliceLeft = 0;
            unitySliceRight = 0;
            unitySliceScale = 1f;
            unitySliceTop = 0;
            unitySliceType = SliceType.Sliced;
            unityTextAlign = TextAnchor.UpperLeft;
            unityTextGenerator = TextGeneratorType.Standard;
            unityTextOutlineColor = Color.clear;
            unityTextOutlineWidth = 0f;
            unityTextOverflowPosition = TextOverflowPosition.End;
            visibility = Visibility.Visible;
            whiteSpace = WhiteSpace.Normal;
            width = Length.Auto();
            wordSpacing = new Length(0f);
        }

        internal ComputedValues Clone()
        {
            var c = (ComputedValues)MemberwiseClone();
            c.transitionDelay = transitionDelay == null ? null : new List<TimeValue>(transitionDelay);
            c.transitionDuration = transitionDuration == null ? null : new List<TimeValue>(transitionDuration);
            c.transitionProperty = transitionProperty == null ? null : new List<StylePropertyName>(transitionProperty);
            c.transitionTimingFunction = transitionTimingFunction == null ? null : new List<EasingFunction>(transitionTimingFunction);
            if (customProperties != null) c.customProperties = new Dictionary<string, UssValue>(customProperties);
            return c;
        }

        /// <summary>A style for a child: inherited properties from its parent, the rest initial.</summary>
        internal static ComputedValues CreateFor(ComputedValues parent)
        {
            var c = Initial.Clone();
            if (parent == null) return c;
            c.color = parent.color;
            c.fontSize = parent.fontSize;
            c.letterSpacing = parent.letterSpacing;
            c.textShadow = parent.textShadow;
            c.unityEditorTextRenderingMode = parent.unityEditorTextRenderingMode;
            c.unityFont = parent.unityFont;
            c.unityFontDefinition = parent.unityFontDefinition;
            c.unityFontStyleAndWeight = parent.unityFontStyleAndWeight;
            c.unityParagraphSpacing = parent.unityParagraphSpacing;
            c.unityTextAlign = parent.unityTextAlign;
            c.unityTextGenerator = parent.unityTextGenerator;
            c.unityTextOutlineColor = parent.unityTextOutlineColor;
            c.unityTextOutlineWidth = parent.unityTextOutlineWidth;
            c.visibility = parent.visibility;
            c.whiteSpace = parent.whiteSpace;
            c.wordSpacing = parent.wordSpacing;
            return c;
        }

        internal void ApplyInitial(StylePropertyId id)
        {
            var i = Initial;
            switch (id)
            {
                case StylePropertyId.AlignContent: alignContent = i.alignContent; break;
                case StylePropertyId.AlignItems: alignItems = i.alignItems; break;
                case StylePropertyId.AlignSelf: alignSelf = i.alignSelf; break;
                case StylePropertyId.BackgroundColor: backgroundColor = i.backgroundColor; break;
                case StylePropertyId.BackgroundImage: backgroundImage = i.backgroundImage; break;
                case StylePropertyId.BackgroundPositionX: backgroundPositionX = i.backgroundPositionX; break;
                case StylePropertyId.BackgroundPositionY: backgroundPositionY = i.backgroundPositionY; break;
                case StylePropertyId.BackgroundRepeat: backgroundRepeat = i.backgroundRepeat; break;
                case StylePropertyId.BackgroundSize: backgroundSize = i.backgroundSize; break;
                case StylePropertyId.BorderBottomColor: borderBottomColor = i.borderBottomColor; break;
                case StylePropertyId.BorderBottomLeftRadius: borderBottomLeftRadius = i.borderBottomLeftRadius; break;
                case StylePropertyId.BorderBottomRightRadius: borderBottomRightRadius = i.borderBottomRightRadius; break;
                case StylePropertyId.BorderBottomWidth: borderBottomWidth = i.borderBottomWidth; break;
                case StylePropertyId.BorderLeftColor: borderLeftColor = i.borderLeftColor; break;
                case StylePropertyId.BorderLeftWidth: borderLeftWidth = i.borderLeftWidth; break;
                case StylePropertyId.BorderRightColor: borderRightColor = i.borderRightColor; break;
                case StylePropertyId.BorderRightWidth: borderRightWidth = i.borderRightWidth; break;
                case StylePropertyId.BorderTopColor: borderTopColor = i.borderTopColor; break;
                case StylePropertyId.BorderTopLeftRadius: borderTopLeftRadius = i.borderTopLeftRadius; break;
                case StylePropertyId.BorderTopRightRadius: borderTopRightRadius = i.borderTopRightRadius; break;
                case StylePropertyId.BorderTopWidth: borderTopWidth = i.borderTopWidth; break;
                case StylePropertyId.Bottom: bottom = i.bottom; break;
                case StylePropertyId.Color: color = i.color; break;
                case StylePropertyId.Cursor: cursor = i.cursor; break;
                case StylePropertyId.Display: display = i.display; break;
                case StylePropertyId.FlexBasis: flexBasis = i.flexBasis; break;
                case StylePropertyId.FlexDirection: flexDirection = i.flexDirection; break;
                case StylePropertyId.FlexGrow: flexGrow = i.flexGrow; break;
                case StylePropertyId.FlexShrink: flexShrink = i.flexShrink; break;
                case StylePropertyId.FlexWrap: flexWrap = i.flexWrap; break;
                case StylePropertyId.FontSize: fontSize = i.fontSize; break;
                case StylePropertyId.Height: height = i.height; break;
                case StylePropertyId.JustifyContent: justifyContent = i.justifyContent; break;
                case StylePropertyId.Left: left = i.left; break;
                case StylePropertyId.LetterSpacing: letterSpacing = i.letterSpacing; break;
                case StylePropertyId.MarginBottom: marginBottom = i.marginBottom; break;
                case StylePropertyId.MarginLeft: marginLeft = i.marginLeft; break;
                case StylePropertyId.MarginRight: marginRight = i.marginRight; break;
                case StylePropertyId.MarginTop: marginTop = i.marginTop; break;
                case StylePropertyId.MaxHeight: maxHeight = i.maxHeight; break;
                case StylePropertyId.MaxWidth: maxWidth = i.maxWidth; break;
                case StylePropertyId.MinHeight: minHeight = i.minHeight; break;
                case StylePropertyId.MinWidth: minWidth = i.minWidth; break;
                case StylePropertyId.Opacity: opacity = i.opacity; break;
                case StylePropertyId.Overflow: overflow = i.overflow; break;
                case StylePropertyId.PaddingBottom: paddingBottom = i.paddingBottom; break;
                case StylePropertyId.PaddingLeft: paddingLeft = i.paddingLeft; break;
                case StylePropertyId.PaddingRight: paddingRight = i.paddingRight; break;
                case StylePropertyId.PaddingTop: paddingTop = i.paddingTop; break;
                case StylePropertyId.Position: position = i.position; break;
                case StylePropertyId.Right: right = i.right; break;
                case StylePropertyId.Rotate: rotate = i.rotate; break;
                case StylePropertyId.Scale: scale = i.scale; break;
                case StylePropertyId.TextOverflow: textOverflow = i.textOverflow; break;
                case StylePropertyId.TextShadow: textShadow = i.textShadow; break;
                case StylePropertyId.Top: top = i.top; break;
                case StylePropertyId.TransformOrigin: transformOrigin = i.transformOrigin; break;
                case StylePropertyId.TransitionDelay: transitionDelay = new List<TimeValue>(i.transitionDelay); break;
                case StylePropertyId.TransitionDuration: transitionDuration = new List<TimeValue>(i.transitionDuration); break;
                case StylePropertyId.TransitionProperty: transitionProperty = new List<StylePropertyName>(i.transitionProperty); break;
                case StylePropertyId.TransitionTimingFunction: transitionTimingFunction = new List<EasingFunction>(i.transitionTimingFunction); break;
                case StylePropertyId.Translate: translate = i.translate; break;
                case StylePropertyId.UnityBackgroundImageTintColor: unityBackgroundImageTintColor = i.unityBackgroundImageTintColor; break;
                case StylePropertyId.UnityEditorTextRenderingMode: unityEditorTextRenderingMode = i.unityEditorTextRenderingMode; break;
                case StylePropertyId.UnityFont: unityFont = i.unityFont; break;
                case StylePropertyId.UnityFontDefinition: unityFontDefinition = i.unityFontDefinition; break;
                case StylePropertyId.UnityFontStyleAndWeight: unityFontStyleAndWeight = i.unityFontStyleAndWeight; break;
                case StylePropertyId.UnityOverflowClipBox: unityOverflowClipBox = i.unityOverflowClipBox; break;
                case StylePropertyId.UnityParagraphSpacing: unityParagraphSpacing = i.unityParagraphSpacing; break;
                case StylePropertyId.UnitySliceBottom: unitySliceBottom = i.unitySliceBottom; break;
                case StylePropertyId.UnitySliceLeft: unitySliceLeft = i.unitySliceLeft; break;
                case StylePropertyId.UnitySliceRight: unitySliceRight = i.unitySliceRight; break;
                case StylePropertyId.UnitySliceScale: unitySliceScale = i.unitySliceScale; break;
                case StylePropertyId.UnitySliceTop: unitySliceTop = i.unitySliceTop; break;
                case StylePropertyId.UnitySliceType: unitySliceType = i.unitySliceType; break;
                case StylePropertyId.UnityTextAlign: unityTextAlign = i.unityTextAlign; break;
                case StylePropertyId.UnityTextGenerator: unityTextGenerator = i.unityTextGenerator; break;
                case StylePropertyId.UnityTextOutlineColor: unityTextOutlineColor = i.unityTextOutlineColor; break;
                case StylePropertyId.UnityTextOutlineWidth: unityTextOutlineWidth = i.unityTextOutlineWidth; break;
                case StylePropertyId.UnityTextOverflowPosition: unityTextOverflowPosition = i.unityTextOverflowPosition; break;
                case StylePropertyId.Visibility: visibility = i.visibility; break;
                case StylePropertyId.WhiteSpace: whiteSpace = i.whiteSpace; break;
                case StylePropertyId.Width: width = i.width; break;
                case StylePropertyId.WordSpacing: wordSpacing = i.wordSpacing; break;
                default: ApplyShorthandInitial(id); break;
            }
        }

        internal void ApplyUnset(StylePropertyId id, ComputedValues parent)
        {
            switch (id)
            {
                case StylePropertyId.Color: color = parent != null ? parent.color : Initial.color; break;
                case StylePropertyId.FontSize: fontSize = parent != null ? parent.fontSize : Initial.fontSize; break;
                case StylePropertyId.LetterSpacing: letterSpacing = parent != null ? parent.letterSpacing : Initial.letterSpacing; break;
                case StylePropertyId.TextShadow: textShadow = parent != null ? parent.textShadow : Initial.textShadow; break;
                case StylePropertyId.UnityEditorTextRenderingMode: unityEditorTextRenderingMode = parent != null ? parent.unityEditorTextRenderingMode : Initial.unityEditorTextRenderingMode; break;
                case StylePropertyId.UnityFont: unityFont = parent != null ? parent.unityFont : Initial.unityFont; break;
                case StylePropertyId.UnityFontDefinition: unityFontDefinition = parent != null ? parent.unityFontDefinition : Initial.unityFontDefinition; break;
                case StylePropertyId.UnityFontStyleAndWeight: unityFontStyleAndWeight = parent != null ? parent.unityFontStyleAndWeight : Initial.unityFontStyleAndWeight; break;
                case StylePropertyId.UnityParagraphSpacing: unityParagraphSpacing = parent != null ? parent.unityParagraphSpacing : Initial.unityParagraphSpacing; break;
                case StylePropertyId.UnityTextAlign: unityTextAlign = parent != null ? parent.unityTextAlign : Initial.unityTextAlign; break;
                case StylePropertyId.UnityTextGenerator: unityTextGenerator = parent != null ? parent.unityTextGenerator : Initial.unityTextGenerator; break;
                case StylePropertyId.UnityTextOutlineColor: unityTextOutlineColor = parent != null ? parent.unityTextOutlineColor : Initial.unityTextOutlineColor; break;
                case StylePropertyId.UnityTextOutlineWidth: unityTextOutlineWidth = parent != null ? parent.unityTextOutlineWidth : Initial.unityTextOutlineWidth; break;
                case StylePropertyId.Visibility: visibility = parent != null ? parent.visibility : Initial.visibility; break;
                case StylePropertyId.WhiteSpace: whiteSpace = parent != null ? parent.whiteSpace : Initial.whiteSpace; break;
                case StylePropertyId.WordSpacing: wordSpacing = parent != null ? parent.wordSpacing : Initial.wordSpacing; break;
                default: ApplyInitial(id); break;
            }
        }

        /// <summary>The longhand property the reader is on, from its USS value.</summary>
        internal bool ApplyLonghand(StylePropertyId id, UssReader r)
        {
            switch (id)
            {
                case StylePropertyId.AlignContent: alignContent = (Align)r.ReadEnum(StyleEnumType.Align, 0); return true;
                case StylePropertyId.AlignItems: alignItems = (Align)r.ReadEnum(StyleEnumType.Align, 0); return true;
                case StylePropertyId.AlignSelf: alignSelf = (Align)r.ReadEnum(StyleEnumType.Align, 0); return true;
                case StylePropertyId.BackgroundColor: backgroundColor = r.ReadColor(0); return true;
                case StylePropertyId.BackgroundImage: backgroundImage = r.ReadBackground(0); return true;
                case StylePropertyId.BackgroundPositionX: backgroundPositionX = r.ReadBackgroundPositionX(0); return true;
                case StylePropertyId.BackgroundPositionY: backgroundPositionY = r.ReadBackgroundPositionY(0); return true;
                case StylePropertyId.BackgroundRepeat: backgroundRepeat = r.ReadBackgroundRepeat(0); return true;
                case StylePropertyId.BackgroundSize: backgroundSize = r.ReadBackgroundSize(0); return true;
                case StylePropertyId.BorderBottomColor: borderBottomColor = r.ReadColor(0); return true;
                case StylePropertyId.BorderBottomLeftRadius: borderBottomLeftRadius = r.ReadLength(0); return true;
                case StylePropertyId.BorderBottomRightRadius: borderBottomRightRadius = r.ReadLength(0); return true;
                case StylePropertyId.BorderBottomWidth: borderBottomWidth = r.ReadFloat(0); return true;
                case StylePropertyId.BorderLeftColor: borderLeftColor = r.ReadColor(0); return true;
                case StylePropertyId.BorderLeftWidth: borderLeftWidth = r.ReadFloat(0); return true;
                case StylePropertyId.BorderRightColor: borderRightColor = r.ReadColor(0); return true;
                case StylePropertyId.BorderRightWidth: borderRightWidth = r.ReadFloat(0); return true;
                case StylePropertyId.BorderTopColor: borderTopColor = r.ReadColor(0); return true;
                case StylePropertyId.BorderTopLeftRadius: borderTopLeftRadius = r.ReadLength(0); return true;
                case StylePropertyId.BorderTopRightRadius: borderTopRightRadius = r.ReadLength(0); return true;
                case StylePropertyId.BorderTopWidth: borderTopWidth = r.ReadFloat(0); return true;
                case StylePropertyId.Bottom: bottom = r.ReadLength(0); return true;
                case StylePropertyId.Color: color = r.ReadColor(0); return true;
                case StylePropertyId.Cursor: cursor = r.ReadCursor(0); return true;
                case StylePropertyId.Display: display = (DisplayStyle)r.ReadEnum(StyleEnumType.DisplayStyle, 0); return true;
                case StylePropertyId.FlexBasis: flexBasis = r.ReadLength(0); return true;
                case StylePropertyId.FlexDirection: flexDirection = (FlexDirection)r.ReadEnum(StyleEnumType.FlexDirection, 0); return true;
                case StylePropertyId.FlexGrow: flexGrow = r.ReadFloat(0); return true;
                case StylePropertyId.FlexShrink: flexShrink = r.ReadFloat(0); return true;
                case StylePropertyId.FlexWrap: flexWrap = (Wrap)r.ReadEnum(StyleEnumType.Wrap, 0); return true;
                case StylePropertyId.FontSize: fontSize = r.ReadLength(0); return true;
                case StylePropertyId.Height: height = r.ReadLength(0); return true;
                case StylePropertyId.JustifyContent: justifyContent = (Justify)r.ReadEnum(StyleEnumType.Justify, 0); return true;
                case StylePropertyId.Left: left = r.ReadLength(0); return true;
                case StylePropertyId.LetterSpacing: letterSpacing = r.ReadLength(0); return true;
                case StylePropertyId.MarginBottom: marginBottom = r.ReadLength(0); return true;
                case StylePropertyId.MarginLeft: marginLeft = r.ReadLength(0); return true;
                case StylePropertyId.MarginRight: marginRight = r.ReadLength(0); return true;
                case StylePropertyId.MarginTop: marginTop = r.ReadLength(0); return true;
                case StylePropertyId.MaxHeight: maxHeight = r.ReadLength(0); return true;
                case StylePropertyId.MaxWidth: maxWidth = r.ReadLength(0); return true;
                case StylePropertyId.MinHeight: minHeight = r.ReadLength(0); return true;
                case StylePropertyId.MinWidth: minWidth = r.ReadLength(0); return true;
                case StylePropertyId.Opacity: opacity = r.ReadFloat(0); return true;
                case StylePropertyId.Overflow: overflow = (OverflowInternal)r.ReadEnum(StyleEnumType.OverflowInternal, 0); return true;
                case StylePropertyId.PaddingBottom: paddingBottom = r.ReadLength(0); return true;
                case StylePropertyId.PaddingLeft: paddingLeft = r.ReadLength(0); return true;
                case StylePropertyId.PaddingRight: paddingRight = r.ReadLength(0); return true;
                case StylePropertyId.PaddingTop: paddingTop = r.ReadLength(0); return true;
                case StylePropertyId.Position: position = (Position)r.ReadEnum(StyleEnumType.Position, 0); return true;
                case StylePropertyId.Right: right = r.ReadLength(0); return true;
                case StylePropertyId.Rotate: rotate = r.ReadRotate(0); return true;
                case StylePropertyId.Scale: scale = r.ReadScale(0); return true;
                case StylePropertyId.TextOverflow: textOverflow = (TextOverflow)r.ReadEnum(StyleEnumType.TextOverflow, 0); return true;
                case StylePropertyId.TextShadow: textShadow = r.ReadTextShadow(0); return true;
                case StylePropertyId.Top: top = r.ReadLength(0); return true;
                case StylePropertyId.TransformOrigin: transformOrigin = r.ReadTransformOrigin(0); return true;
                case StylePropertyId.TransitionDelay: transitionDelay = r.ReadListTimeValue(0); return true;
                case StylePropertyId.TransitionDuration: transitionDuration = r.ReadListTimeValue(0); return true;
                case StylePropertyId.TransitionProperty: transitionProperty = r.ReadListStylePropertyName(0); return true;
                case StylePropertyId.TransitionTimingFunction: transitionTimingFunction = r.ReadListEasingFunction(0); return true;
                case StylePropertyId.Translate: translate = r.ReadTranslate(0); return true;
                case StylePropertyId.UnityBackgroundImageTintColor: unityBackgroundImageTintColor = r.ReadColor(0); return true;
                case StylePropertyId.UnityEditorTextRenderingMode: unityEditorTextRenderingMode = (EditorTextRenderingMode)r.ReadEnum(StyleEnumType.EditorTextRenderingMode, 0); return true;
                case StylePropertyId.UnityFont: unityFont = r.ReadFont(0); return true;
                case StylePropertyId.UnityFontDefinition: unityFontDefinition = r.ReadFontDefinition(0); return true;
                case StylePropertyId.UnityFontStyleAndWeight: unityFontStyleAndWeight = (FontStyle)r.ReadEnum(StyleEnumType.FontStyle, 0); return true;
                case StylePropertyId.UnityOverflowClipBox: unityOverflowClipBox = (OverflowClipBox)r.ReadEnum(StyleEnumType.OverflowClipBox, 0); return true;
                case StylePropertyId.UnityParagraphSpacing: unityParagraphSpacing = r.ReadLength(0); return true;
                case StylePropertyId.UnitySliceBottom: unitySliceBottom = r.ReadInt(0); return true;
                case StylePropertyId.UnitySliceLeft: unitySliceLeft = r.ReadInt(0); return true;
                case StylePropertyId.UnitySliceRight: unitySliceRight = r.ReadInt(0); return true;
                case StylePropertyId.UnitySliceScale: unitySliceScale = r.ReadFloat(0); return true;
                case StylePropertyId.UnitySliceTop: unitySliceTop = r.ReadInt(0); return true;
                case StylePropertyId.UnitySliceType: unitySliceType = (SliceType)r.ReadEnum(StyleEnumType.SliceType, 0); return true;
                case StylePropertyId.UnityTextAlign: unityTextAlign = (TextAnchor)r.ReadEnum(StyleEnumType.TextAnchor, 0); return true;
                case StylePropertyId.UnityTextGenerator: unityTextGenerator = (TextGeneratorType)r.ReadEnum(StyleEnumType.TextGeneratorType, 0); return true;
                case StylePropertyId.UnityTextOutlineColor: unityTextOutlineColor = r.ReadColor(0); return true;
                case StylePropertyId.UnityTextOutlineWidth: unityTextOutlineWidth = r.ReadFloat(0); return true;
                case StylePropertyId.UnityTextOverflowPosition: unityTextOverflowPosition = (TextOverflowPosition)r.ReadEnum(StyleEnumType.TextOverflowPosition, 0); return true;
                case StylePropertyId.Visibility: visibility = (Visibility)r.ReadEnum(StyleEnumType.Visibility, 0); return true;
                case StylePropertyId.WhiteSpace: whiteSpace = (WhiteSpace)r.ReadEnum(StyleEnumType.WhiteSpace, 0); return true;
                case StylePropertyId.Width: width = r.ReadLength(0); return true;
                case StylePropertyId.WordSpacing: wordSpacing = r.ReadLength(0); return true;
            }
            return false;
        }
    }

    internal static partial class StyleProperties
    {
        internal static readonly Dictionary<string, StylePropertyId> NameToId = new Dictionary<string, StylePropertyId>
        {
            ["align-content"] = StylePropertyId.AlignContent,
            ["align-items"] = StylePropertyId.AlignItems,
            ["align-self"] = StylePropertyId.AlignSelf,
            ["background-color"] = StylePropertyId.BackgroundColor,
            ["background-image"] = StylePropertyId.BackgroundImage,
            ["background-position-x"] = StylePropertyId.BackgroundPositionX,
            ["background-position-y"] = StylePropertyId.BackgroundPositionY,
            ["background-repeat"] = StylePropertyId.BackgroundRepeat,
            ["background-size"] = StylePropertyId.BackgroundSize,
            ["border-bottom-color"] = StylePropertyId.BorderBottomColor,
            ["border-bottom-left-radius"] = StylePropertyId.BorderBottomLeftRadius,
            ["border-bottom-right-radius"] = StylePropertyId.BorderBottomRightRadius,
            ["border-bottom-width"] = StylePropertyId.BorderBottomWidth,
            ["border-left-color"] = StylePropertyId.BorderLeftColor,
            ["border-left-width"] = StylePropertyId.BorderLeftWidth,
            ["border-right-color"] = StylePropertyId.BorderRightColor,
            ["border-right-width"] = StylePropertyId.BorderRightWidth,
            ["border-top-color"] = StylePropertyId.BorderTopColor,
            ["border-top-left-radius"] = StylePropertyId.BorderTopLeftRadius,
            ["border-top-right-radius"] = StylePropertyId.BorderTopRightRadius,
            ["border-top-width"] = StylePropertyId.BorderTopWidth,
            ["bottom"] = StylePropertyId.Bottom,
            ["color"] = StylePropertyId.Color,
            ["cursor"] = StylePropertyId.Cursor,
            ["display"] = StylePropertyId.Display,
            ["flex-basis"] = StylePropertyId.FlexBasis,
            ["flex-direction"] = StylePropertyId.FlexDirection,
            ["flex-grow"] = StylePropertyId.FlexGrow,
            ["flex-shrink"] = StylePropertyId.FlexShrink,
            ["flex-wrap"] = StylePropertyId.FlexWrap,
            ["font-size"] = StylePropertyId.FontSize,
            ["height"] = StylePropertyId.Height,
            ["justify-content"] = StylePropertyId.JustifyContent,
            ["left"] = StylePropertyId.Left,
            ["letter-spacing"] = StylePropertyId.LetterSpacing,
            ["margin-bottom"] = StylePropertyId.MarginBottom,
            ["margin-left"] = StylePropertyId.MarginLeft,
            ["margin-right"] = StylePropertyId.MarginRight,
            ["margin-top"] = StylePropertyId.MarginTop,
            ["max-height"] = StylePropertyId.MaxHeight,
            ["max-width"] = StylePropertyId.MaxWidth,
            ["min-height"] = StylePropertyId.MinHeight,
            ["min-width"] = StylePropertyId.MinWidth,
            ["opacity"] = StylePropertyId.Opacity,
            ["overflow"] = StylePropertyId.Overflow,
            ["padding-bottom"] = StylePropertyId.PaddingBottom,
            ["padding-left"] = StylePropertyId.PaddingLeft,
            ["padding-right"] = StylePropertyId.PaddingRight,
            ["padding-top"] = StylePropertyId.PaddingTop,
            ["position"] = StylePropertyId.Position,
            ["right"] = StylePropertyId.Right,
            ["rotate"] = StylePropertyId.Rotate,
            ["scale"] = StylePropertyId.Scale,
            ["text-overflow"] = StylePropertyId.TextOverflow,
            ["text-shadow"] = StylePropertyId.TextShadow,
            ["top"] = StylePropertyId.Top,
            ["transform-origin"] = StylePropertyId.TransformOrigin,
            ["transition-delay"] = StylePropertyId.TransitionDelay,
            ["transition-duration"] = StylePropertyId.TransitionDuration,
            ["transition-property"] = StylePropertyId.TransitionProperty,
            ["transition-timing-function"] = StylePropertyId.TransitionTimingFunction,
            ["translate"] = StylePropertyId.Translate,
            ["-unity-background-image-tint-color"] = StylePropertyId.UnityBackgroundImageTintColor,
            ["-unity-editor-text-rendering-mode"] = StylePropertyId.UnityEditorTextRenderingMode,
            ["-unity-font"] = StylePropertyId.UnityFont,
            ["-unity-font-definition"] = StylePropertyId.UnityFontDefinition,
            ["-unity-font-style"] = StylePropertyId.UnityFontStyleAndWeight,
            ["-unity-overflow-clip-box"] = StylePropertyId.UnityOverflowClipBox,
            ["-unity-paragraph-spacing"] = StylePropertyId.UnityParagraphSpacing,
            ["-unity-slice-bottom"] = StylePropertyId.UnitySliceBottom,
            ["-unity-slice-left"] = StylePropertyId.UnitySliceLeft,
            ["-unity-slice-right"] = StylePropertyId.UnitySliceRight,
            ["-unity-slice-scale"] = StylePropertyId.UnitySliceScale,
            ["-unity-slice-top"] = StylePropertyId.UnitySliceTop,
            ["-unity-slice-type"] = StylePropertyId.UnitySliceType,
            ["-unity-text-align"] = StylePropertyId.UnityTextAlign,
            ["-unity-text-generator"] = StylePropertyId.UnityTextGenerator,
            ["-unity-text-outline-color"] = StylePropertyId.UnityTextOutlineColor,
            ["-unity-text-outline-width"] = StylePropertyId.UnityTextOutlineWidth,
            ["-unity-text-overflow-position"] = StylePropertyId.UnityTextOverflowPosition,
            ["visibility"] = StylePropertyId.Visibility,
            ["white-space"] = StylePropertyId.WhiteSpace,
            ["width"] = StylePropertyId.Width,
            ["word-spacing"] = StylePropertyId.WordSpacing,
            ["all"] = StylePropertyId.All,
            ["margin"] = StylePropertyId.Margin,
            ["padding"] = StylePropertyId.Padding,
            ["border-width"] = StylePropertyId.BorderWidth,
            ["border-color"] = StylePropertyId.BorderColor,
            ["border-radius"] = StylePropertyId.BorderRadius,
            ["flex"] = StylePropertyId.Flex,
            ["background-position"] = StylePropertyId.BackgroundPosition,
            ["-unity-background-scale-mode"] = StylePropertyId.UnityBackgroundScaleMode,
            ["-unity-text-outline"] = StylePropertyId.UnityTextOutline,
            ["transition"] = StylePropertyId.Transition,
        };

        internal static readonly HashSet<StylePropertyId> Inherited = new HashSet<StylePropertyId>
        {
            StylePropertyId.Color,
            StylePropertyId.FontSize,
            StylePropertyId.LetterSpacing,
            StylePropertyId.TextShadow,
            StylePropertyId.UnityEditorTextRenderingMode,
            StylePropertyId.UnityFont,
            StylePropertyId.UnityFontDefinition,
            StylePropertyId.UnityFontStyleAndWeight,
            StylePropertyId.UnityParagraphSpacing,
            StylePropertyId.UnityTextAlign,
            StylePropertyId.UnityTextGenerator,
            StylePropertyId.UnityTextOutlineColor,
            StylePropertyId.UnityTextOutlineWidth,
            StylePropertyId.Visibility,
            StylePropertyId.WhiteSpace,
            StylePropertyId.WordSpacing,
        };
    }

    /// <summary>The values code sets through VisualElement.style, applied over the matched rules.</summary>
    internal sealed partial class InlineStyle : IStyle
    {
        readonly VisualElement m_Element;
        internal InlineStyle(VisualElement element) { m_Element = element; }
        void Changed(StylePropertyId id) => m_Element.UaStyleChanged();

        StyleEnum<Align> m_alignContent; bool m_alignContentSet;
        StyleEnum<Align> m_alignItems; bool m_alignItemsSet;
        StyleEnum<Align> m_alignSelf; bool m_alignSelfSet;
        StyleColor m_backgroundColor; bool m_backgroundColorSet;
        StyleBackground m_backgroundImage; bool m_backgroundImageSet;
        StyleBackgroundPosition m_backgroundPositionX; bool m_backgroundPositionXSet;
        StyleBackgroundPosition m_backgroundPositionY; bool m_backgroundPositionYSet;
        StyleBackgroundRepeat m_backgroundRepeat; bool m_backgroundRepeatSet;
        StyleBackgroundSize m_backgroundSize; bool m_backgroundSizeSet;
        StyleColor m_borderBottomColor; bool m_borderBottomColorSet;
        StyleLength m_borderBottomLeftRadius; bool m_borderBottomLeftRadiusSet;
        StyleLength m_borderBottomRightRadius; bool m_borderBottomRightRadiusSet;
        StyleFloat m_borderBottomWidth; bool m_borderBottomWidthSet;
        StyleColor m_borderLeftColor; bool m_borderLeftColorSet;
        StyleFloat m_borderLeftWidth; bool m_borderLeftWidthSet;
        StyleColor m_borderRightColor; bool m_borderRightColorSet;
        StyleFloat m_borderRightWidth; bool m_borderRightWidthSet;
        StyleColor m_borderTopColor; bool m_borderTopColorSet;
        StyleLength m_borderTopLeftRadius; bool m_borderTopLeftRadiusSet;
        StyleLength m_borderTopRightRadius; bool m_borderTopRightRadiusSet;
        StyleFloat m_borderTopWidth; bool m_borderTopWidthSet;
        StyleLength m_bottom; bool m_bottomSet;
        StyleColor m_color; bool m_colorSet;
        StyleCursor m_cursor; bool m_cursorSet;
        StyleEnum<DisplayStyle> m_display; bool m_displaySet;
        StyleLength m_flexBasis; bool m_flexBasisSet;
        StyleEnum<FlexDirection> m_flexDirection; bool m_flexDirectionSet;
        StyleFloat m_flexGrow; bool m_flexGrowSet;
        StyleFloat m_flexShrink; bool m_flexShrinkSet;
        StyleEnum<Wrap> m_flexWrap; bool m_flexWrapSet;
        StyleLength m_fontSize; bool m_fontSizeSet;
        StyleLength m_height; bool m_heightSet;
        StyleEnum<Justify> m_justifyContent; bool m_justifyContentSet;
        StyleLength m_left; bool m_leftSet;
        StyleLength m_letterSpacing; bool m_letterSpacingSet;
        StyleLength m_marginBottom; bool m_marginBottomSet;
        StyleLength m_marginLeft; bool m_marginLeftSet;
        StyleLength m_marginRight; bool m_marginRightSet;
        StyleLength m_marginTop; bool m_marginTopSet;
        StyleLength m_maxHeight; bool m_maxHeightSet;
        StyleLength m_maxWidth; bool m_maxWidthSet;
        StyleLength m_minHeight; bool m_minHeightSet;
        StyleLength m_minWidth; bool m_minWidthSet;
        StyleFloat m_opacity; bool m_opacitySet;
        StyleEnum<Overflow> m_overflow; bool m_overflowSet;
        StyleLength m_paddingBottom; bool m_paddingBottomSet;
        StyleLength m_paddingLeft; bool m_paddingLeftSet;
        StyleLength m_paddingRight; bool m_paddingRightSet;
        StyleLength m_paddingTop; bool m_paddingTopSet;
        StyleEnum<Position> m_position; bool m_positionSet;
        StyleLength m_right; bool m_rightSet;
        StyleRotate m_rotate; bool m_rotateSet;
        StyleScale m_scale; bool m_scaleSet;
        StyleEnum<TextOverflow> m_textOverflow; bool m_textOverflowSet;
        StyleTextShadow m_textShadow; bool m_textShadowSet;
        StyleLength m_top; bool m_topSet;
        StyleTransformOrigin m_transformOrigin; bool m_transformOriginSet;
        StyleList<TimeValue> m_transitionDelay; bool m_transitionDelaySet;
        StyleList<TimeValue> m_transitionDuration; bool m_transitionDurationSet;
        StyleList<StylePropertyName> m_transitionProperty; bool m_transitionPropertySet;
        StyleList<EasingFunction> m_transitionTimingFunction; bool m_transitionTimingFunctionSet;
        StyleTranslate m_translate; bool m_translateSet;
        StyleColor m_unityBackgroundImageTintColor; bool m_unityBackgroundImageTintColorSet;
        StyleEnum<EditorTextRenderingMode> m_unityEditorTextRenderingMode; bool m_unityEditorTextRenderingModeSet;
        StyleFont m_unityFont; bool m_unityFontSet;
        StyleFontDefinition m_unityFontDefinition; bool m_unityFontDefinitionSet;
        StyleEnum<FontStyle> m_unityFontStyleAndWeight; bool m_unityFontStyleAndWeightSet;
        StyleEnum<OverflowClipBox> m_unityOverflowClipBox; bool m_unityOverflowClipBoxSet;
        StyleLength m_unityParagraphSpacing; bool m_unityParagraphSpacingSet;
        StyleInt m_unitySliceBottom; bool m_unitySliceBottomSet;
        StyleInt m_unitySliceLeft; bool m_unitySliceLeftSet;
        StyleInt m_unitySliceRight; bool m_unitySliceRightSet;
        StyleFloat m_unitySliceScale; bool m_unitySliceScaleSet;
        StyleInt m_unitySliceTop; bool m_unitySliceTopSet;
        StyleEnum<SliceType> m_unitySliceType; bool m_unitySliceTypeSet;
        StyleEnum<TextAnchor> m_unityTextAlign; bool m_unityTextAlignSet;
        StyleEnum<TextGeneratorType> m_unityTextGenerator; bool m_unityTextGeneratorSet;
        StyleColor m_unityTextOutlineColor; bool m_unityTextOutlineColorSet;
        StyleFloat m_unityTextOutlineWidth; bool m_unityTextOutlineWidthSet;
        StyleEnum<TextOverflowPosition> m_unityTextOverflowPosition; bool m_unityTextOverflowPositionSet;
        StyleEnum<Visibility> m_visibility; bool m_visibilitySet;
        StyleEnum<WhiteSpace> m_whiteSpace; bool m_whiteSpaceSet;
        StyleLength m_width; bool m_widthSet;
        StyleLength m_wordSpacing; bool m_wordSpacingSet;

        public StyleEnum<Align> alignContent
        {
            get => m_alignContentSet ? m_alignContent : new StyleEnum<Align>(StyleKeyword.Null);
            set { if (m_alignContentSet && m_alignContent == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_alignContentSet) return; m_alignContent = value; m_alignContentSet = set; Changed(StylePropertyId.AlignContent); }
        }
        public StyleEnum<Align> alignItems
        {
            get => m_alignItemsSet ? m_alignItems : new StyleEnum<Align>(StyleKeyword.Null);
            set { if (m_alignItemsSet && m_alignItems == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_alignItemsSet) return; m_alignItems = value; m_alignItemsSet = set; Changed(StylePropertyId.AlignItems); }
        }
        public StyleEnum<Align> alignSelf
        {
            get => m_alignSelfSet ? m_alignSelf : new StyleEnum<Align>(StyleKeyword.Null);
            set { if (m_alignSelfSet && m_alignSelf == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_alignSelfSet) return; m_alignSelf = value; m_alignSelfSet = set; Changed(StylePropertyId.AlignSelf); }
        }
        public StyleColor backgroundColor
        {
            get => m_backgroundColorSet ? m_backgroundColor : new StyleColor(StyleKeyword.Null);
            set { if (m_backgroundColorSet && m_backgroundColor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_backgroundColorSet) return; m_backgroundColor = value; m_backgroundColorSet = set; Changed(StylePropertyId.BackgroundColor); }
        }
        public StyleBackground backgroundImage
        {
            get => m_backgroundImageSet ? m_backgroundImage : new StyleBackground(StyleKeyword.Null);
            set { if (m_backgroundImageSet && m_backgroundImage == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_backgroundImageSet) return; m_backgroundImage = value; m_backgroundImageSet = set; Changed(StylePropertyId.BackgroundImage); }
        }
        public StyleBackgroundPosition backgroundPositionX
        {
            get => m_backgroundPositionXSet ? m_backgroundPositionX : new StyleBackgroundPosition(StyleKeyword.Null);
            set { if (m_backgroundPositionXSet && m_backgroundPositionX == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_backgroundPositionXSet) return; m_backgroundPositionX = value; m_backgroundPositionXSet = set; Changed(StylePropertyId.BackgroundPositionX); }
        }
        public StyleBackgroundPosition backgroundPositionY
        {
            get => m_backgroundPositionYSet ? m_backgroundPositionY : new StyleBackgroundPosition(StyleKeyword.Null);
            set { if (m_backgroundPositionYSet && m_backgroundPositionY == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_backgroundPositionYSet) return; m_backgroundPositionY = value; m_backgroundPositionYSet = set; Changed(StylePropertyId.BackgroundPositionY); }
        }
        public StyleBackgroundRepeat backgroundRepeat
        {
            get => m_backgroundRepeatSet ? m_backgroundRepeat : new StyleBackgroundRepeat(StyleKeyword.Null);
            set { if (m_backgroundRepeatSet && m_backgroundRepeat == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_backgroundRepeatSet) return; m_backgroundRepeat = value; m_backgroundRepeatSet = set; Changed(StylePropertyId.BackgroundRepeat); }
        }
        public StyleBackgroundSize backgroundSize
        {
            get => m_backgroundSizeSet ? m_backgroundSize : new StyleBackgroundSize(StyleKeyword.Null);
            set { if (m_backgroundSizeSet && m_backgroundSize == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_backgroundSizeSet) return; m_backgroundSize = value; m_backgroundSizeSet = set; Changed(StylePropertyId.BackgroundSize); }
        }
        public StyleColor borderBottomColor
        {
            get => m_borderBottomColorSet ? m_borderBottomColor : new StyleColor(StyleKeyword.Null);
            set { if (m_borderBottomColorSet && m_borderBottomColor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderBottomColorSet) return; m_borderBottomColor = value; m_borderBottomColorSet = set; Changed(StylePropertyId.BorderBottomColor); }
        }
        public StyleLength borderBottomLeftRadius
        {
            get => m_borderBottomLeftRadiusSet ? m_borderBottomLeftRadius : new StyleLength(StyleKeyword.Null);
            set { if (m_borderBottomLeftRadiusSet && m_borderBottomLeftRadius == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderBottomLeftRadiusSet) return; m_borderBottomLeftRadius = value; m_borderBottomLeftRadiusSet = set; Changed(StylePropertyId.BorderBottomLeftRadius); }
        }
        public StyleLength borderBottomRightRadius
        {
            get => m_borderBottomRightRadiusSet ? m_borderBottomRightRadius : new StyleLength(StyleKeyword.Null);
            set { if (m_borderBottomRightRadiusSet && m_borderBottomRightRadius == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderBottomRightRadiusSet) return; m_borderBottomRightRadius = value; m_borderBottomRightRadiusSet = set; Changed(StylePropertyId.BorderBottomRightRadius); }
        }
        public StyleFloat borderBottomWidth
        {
            get => m_borderBottomWidthSet ? m_borderBottomWidth : new StyleFloat(StyleKeyword.Null);
            set { if (m_borderBottomWidthSet && m_borderBottomWidth == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderBottomWidthSet) return; m_borderBottomWidth = value; m_borderBottomWidthSet = set; Changed(StylePropertyId.BorderBottomWidth); }
        }
        public StyleColor borderLeftColor
        {
            get => m_borderLeftColorSet ? m_borderLeftColor : new StyleColor(StyleKeyword.Null);
            set { if (m_borderLeftColorSet && m_borderLeftColor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderLeftColorSet) return; m_borderLeftColor = value; m_borderLeftColorSet = set; Changed(StylePropertyId.BorderLeftColor); }
        }
        public StyleFloat borderLeftWidth
        {
            get => m_borderLeftWidthSet ? m_borderLeftWidth : new StyleFloat(StyleKeyword.Null);
            set { if (m_borderLeftWidthSet && m_borderLeftWidth == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderLeftWidthSet) return; m_borderLeftWidth = value; m_borderLeftWidthSet = set; Changed(StylePropertyId.BorderLeftWidth); }
        }
        public StyleColor borderRightColor
        {
            get => m_borderRightColorSet ? m_borderRightColor : new StyleColor(StyleKeyword.Null);
            set { if (m_borderRightColorSet && m_borderRightColor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderRightColorSet) return; m_borderRightColor = value; m_borderRightColorSet = set; Changed(StylePropertyId.BorderRightColor); }
        }
        public StyleFloat borderRightWidth
        {
            get => m_borderRightWidthSet ? m_borderRightWidth : new StyleFloat(StyleKeyword.Null);
            set { if (m_borderRightWidthSet && m_borderRightWidth == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderRightWidthSet) return; m_borderRightWidth = value; m_borderRightWidthSet = set; Changed(StylePropertyId.BorderRightWidth); }
        }
        public StyleColor borderTopColor
        {
            get => m_borderTopColorSet ? m_borderTopColor : new StyleColor(StyleKeyword.Null);
            set { if (m_borderTopColorSet && m_borderTopColor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderTopColorSet) return; m_borderTopColor = value; m_borderTopColorSet = set; Changed(StylePropertyId.BorderTopColor); }
        }
        public StyleLength borderTopLeftRadius
        {
            get => m_borderTopLeftRadiusSet ? m_borderTopLeftRadius : new StyleLength(StyleKeyword.Null);
            set { if (m_borderTopLeftRadiusSet && m_borderTopLeftRadius == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderTopLeftRadiusSet) return; m_borderTopLeftRadius = value; m_borderTopLeftRadiusSet = set; Changed(StylePropertyId.BorderTopLeftRadius); }
        }
        public StyleLength borderTopRightRadius
        {
            get => m_borderTopRightRadiusSet ? m_borderTopRightRadius : new StyleLength(StyleKeyword.Null);
            set { if (m_borderTopRightRadiusSet && m_borderTopRightRadius == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderTopRightRadiusSet) return; m_borderTopRightRadius = value; m_borderTopRightRadiusSet = set; Changed(StylePropertyId.BorderTopRightRadius); }
        }
        public StyleFloat borderTopWidth
        {
            get => m_borderTopWidthSet ? m_borderTopWidth : new StyleFloat(StyleKeyword.Null);
            set { if (m_borderTopWidthSet && m_borderTopWidth == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_borderTopWidthSet) return; m_borderTopWidth = value; m_borderTopWidthSet = set; Changed(StylePropertyId.BorderTopWidth); }
        }
        public StyleLength bottom
        {
            get => m_bottomSet ? m_bottom : new StyleLength(StyleKeyword.Null);
            set { if (m_bottomSet && m_bottom == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_bottomSet) return; m_bottom = value; m_bottomSet = set; Changed(StylePropertyId.Bottom); }
        }
        public StyleColor color
        {
            get => m_colorSet ? m_color : new StyleColor(StyleKeyword.Null);
            set { if (m_colorSet && m_color == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_colorSet) return; m_color = value; m_colorSet = set; Changed(StylePropertyId.Color); }
        }
        public StyleCursor cursor
        {
            get => m_cursorSet ? m_cursor : new StyleCursor(StyleKeyword.Null);
            set { if (m_cursorSet && m_cursor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_cursorSet) return; m_cursor = value; m_cursorSet = set; Changed(StylePropertyId.Cursor); }
        }
        public StyleEnum<DisplayStyle> display
        {
            get => m_displaySet ? m_display : new StyleEnum<DisplayStyle>(StyleKeyword.Null);
            set { if (m_displaySet && m_display == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_displaySet) return; m_display = value; m_displaySet = set; Changed(StylePropertyId.Display); }
        }
        public StyleLength flexBasis
        {
            get => m_flexBasisSet ? m_flexBasis : new StyleLength(StyleKeyword.Null);
            set { if (m_flexBasisSet && m_flexBasis == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_flexBasisSet) return; m_flexBasis = value; m_flexBasisSet = set; Changed(StylePropertyId.FlexBasis); }
        }
        public StyleEnum<FlexDirection> flexDirection
        {
            get => m_flexDirectionSet ? m_flexDirection : new StyleEnum<FlexDirection>(StyleKeyword.Null);
            set { if (m_flexDirectionSet && m_flexDirection == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_flexDirectionSet) return; m_flexDirection = value; m_flexDirectionSet = set; Changed(StylePropertyId.FlexDirection); }
        }
        public StyleFloat flexGrow
        {
            get => m_flexGrowSet ? m_flexGrow : new StyleFloat(StyleKeyword.Null);
            set { if (m_flexGrowSet && m_flexGrow == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_flexGrowSet) return; m_flexGrow = value; m_flexGrowSet = set; Changed(StylePropertyId.FlexGrow); }
        }
        public StyleFloat flexShrink
        {
            get => m_flexShrinkSet ? m_flexShrink : new StyleFloat(StyleKeyword.Null);
            set { if (m_flexShrinkSet && m_flexShrink == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_flexShrinkSet) return; m_flexShrink = value; m_flexShrinkSet = set; Changed(StylePropertyId.FlexShrink); }
        }
        public StyleEnum<Wrap> flexWrap
        {
            get => m_flexWrapSet ? m_flexWrap : new StyleEnum<Wrap>(StyleKeyword.Null);
            set { if (m_flexWrapSet && m_flexWrap == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_flexWrapSet) return; m_flexWrap = value; m_flexWrapSet = set; Changed(StylePropertyId.FlexWrap); }
        }
        public StyleLength fontSize
        {
            get => m_fontSizeSet ? m_fontSize : new StyleLength(StyleKeyword.Null);
            set { if (m_fontSizeSet && m_fontSize == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_fontSizeSet) return; m_fontSize = value; m_fontSizeSet = set; Changed(StylePropertyId.FontSize); }
        }
        public StyleLength height
        {
            get => m_heightSet ? m_height : new StyleLength(StyleKeyword.Null);
            set { if (m_heightSet && m_height == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_heightSet) return; m_height = value; m_heightSet = set; Changed(StylePropertyId.Height); }
        }
        public StyleEnum<Justify> justifyContent
        {
            get => m_justifyContentSet ? m_justifyContent : new StyleEnum<Justify>(StyleKeyword.Null);
            set { if (m_justifyContentSet && m_justifyContent == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_justifyContentSet) return; m_justifyContent = value; m_justifyContentSet = set; Changed(StylePropertyId.JustifyContent); }
        }
        public StyleLength left
        {
            get => m_leftSet ? m_left : new StyleLength(StyleKeyword.Null);
            set { if (m_leftSet && m_left == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_leftSet) return; m_left = value; m_leftSet = set; Changed(StylePropertyId.Left); }
        }
        public StyleLength letterSpacing
        {
            get => m_letterSpacingSet ? m_letterSpacing : new StyleLength(StyleKeyword.Null);
            set { if (m_letterSpacingSet && m_letterSpacing == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_letterSpacingSet) return; m_letterSpacing = value; m_letterSpacingSet = set; Changed(StylePropertyId.LetterSpacing); }
        }
        public StyleLength marginBottom
        {
            get => m_marginBottomSet ? m_marginBottom : new StyleLength(StyleKeyword.Null);
            set { if (m_marginBottomSet && m_marginBottom == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_marginBottomSet) return; m_marginBottom = value; m_marginBottomSet = set; Changed(StylePropertyId.MarginBottom); }
        }
        public StyleLength marginLeft
        {
            get => m_marginLeftSet ? m_marginLeft : new StyleLength(StyleKeyword.Null);
            set { if (m_marginLeftSet && m_marginLeft == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_marginLeftSet) return; m_marginLeft = value; m_marginLeftSet = set; Changed(StylePropertyId.MarginLeft); }
        }
        public StyleLength marginRight
        {
            get => m_marginRightSet ? m_marginRight : new StyleLength(StyleKeyword.Null);
            set { if (m_marginRightSet && m_marginRight == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_marginRightSet) return; m_marginRight = value; m_marginRightSet = set; Changed(StylePropertyId.MarginRight); }
        }
        public StyleLength marginTop
        {
            get => m_marginTopSet ? m_marginTop : new StyleLength(StyleKeyword.Null);
            set { if (m_marginTopSet && m_marginTop == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_marginTopSet) return; m_marginTop = value; m_marginTopSet = set; Changed(StylePropertyId.MarginTop); }
        }
        public StyleLength maxHeight
        {
            get => m_maxHeightSet ? m_maxHeight : new StyleLength(StyleKeyword.Null);
            set { if (m_maxHeightSet && m_maxHeight == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_maxHeightSet) return; m_maxHeight = value; m_maxHeightSet = set; Changed(StylePropertyId.MaxHeight); }
        }
        public StyleLength maxWidth
        {
            get => m_maxWidthSet ? m_maxWidth : new StyleLength(StyleKeyword.Null);
            set { if (m_maxWidthSet && m_maxWidth == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_maxWidthSet) return; m_maxWidth = value; m_maxWidthSet = set; Changed(StylePropertyId.MaxWidth); }
        }
        public StyleLength minHeight
        {
            get => m_minHeightSet ? m_minHeight : new StyleLength(StyleKeyword.Null);
            set { if (m_minHeightSet && m_minHeight == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_minHeightSet) return; m_minHeight = value; m_minHeightSet = set; Changed(StylePropertyId.MinHeight); }
        }
        public StyleLength minWidth
        {
            get => m_minWidthSet ? m_minWidth : new StyleLength(StyleKeyword.Null);
            set { if (m_minWidthSet && m_minWidth == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_minWidthSet) return; m_minWidth = value; m_minWidthSet = set; Changed(StylePropertyId.MinWidth); }
        }
        public StyleFloat opacity
        {
            get => m_opacitySet ? m_opacity : new StyleFloat(StyleKeyword.Null);
            set { if (m_opacitySet && m_opacity == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_opacitySet) return; m_opacity = value; m_opacitySet = set; Changed(StylePropertyId.Opacity); }
        }
        public StyleEnum<Overflow> overflow
        {
            get => m_overflowSet ? m_overflow : new StyleEnum<Overflow>(StyleKeyword.Null);
            set { if (m_overflowSet && m_overflow == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_overflowSet) return; m_overflow = value; m_overflowSet = set; Changed(StylePropertyId.Overflow); }
        }
        public StyleLength paddingBottom
        {
            get => m_paddingBottomSet ? m_paddingBottom : new StyleLength(StyleKeyword.Null);
            set { if (m_paddingBottomSet && m_paddingBottom == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_paddingBottomSet) return; m_paddingBottom = value; m_paddingBottomSet = set; Changed(StylePropertyId.PaddingBottom); }
        }
        public StyleLength paddingLeft
        {
            get => m_paddingLeftSet ? m_paddingLeft : new StyleLength(StyleKeyword.Null);
            set { if (m_paddingLeftSet && m_paddingLeft == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_paddingLeftSet) return; m_paddingLeft = value; m_paddingLeftSet = set; Changed(StylePropertyId.PaddingLeft); }
        }
        public StyleLength paddingRight
        {
            get => m_paddingRightSet ? m_paddingRight : new StyleLength(StyleKeyword.Null);
            set { if (m_paddingRightSet && m_paddingRight == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_paddingRightSet) return; m_paddingRight = value; m_paddingRightSet = set; Changed(StylePropertyId.PaddingRight); }
        }
        public StyleLength paddingTop
        {
            get => m_paddingTopSet ? m_paddingTop : new StyleLength(StyleKeyword.Null);
            set { if (m_paddingTopSet && m_paddingTop == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_paddingTopSet) return; m_paddingTop = value; m_paddingTopSet = set; Changed(StylePropertyId.PaddingTop); }
        }
        public StyleEnum<Position> position
        {
            get => m_positionSet ? m_position : new StyleEnum<Position>(StyleKeyword.Null);
            set { if (m_positionSet && m_position == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_positionSet) return; m_position = value; m_positionSet = set; Changed(StylePropertyId.Position); }
        }
        public StyleLength right
        {
            get => m_rightSet ? m_right : new StyleLength(StyleKeyword.Null);
            set { if (m_rightSet && m_right == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_rightSet) return; m_right = value; m_rightSet = set; Changed(StylePropertyId.Right); }
        }
        public StyleRotate rotate
        {
            get => m_rotateSet ? m_rotate : new StyleRotate(StyleKeyword.Null);
            set { if (m_rotateSet && m_rotate == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_rotateSet) return; m_rotate = value; m_rotateSet = set; Changed(StylePropertyId.Rotate); }
        }
        public StyleScale scale
        {
            get => m_scaleSet ? m_scale : new StyleScale(StyleKeyword.Null);
            set { if (m_scaleSet && m_scale == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_scaleSet) return; m_scale = value; m_scaleSet = set; Changed(StylePropertyId.Scale); }
        }
        public StyleEnum<TextOverflow> textOverflow
        {
            get => m_textOverflowSet ? m_textOverflow : new StyleEnum<TextOverflow>(StyleKeyword.Null);
            set { if (m_textOverflowSet && m_textOverflow == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_textOverflowSet) return; m_textOverflow = value; m_textOverflowSet = set; Changed(StylePropertyId.TextOverflow); }
        }
        public StyleTextShadow textShadow
        {
            get => m_textShadowSet ? m_textShadow : new StyleTextShadow(StyleKeyword.Null);
            set { if (m_textShadowSet && m_textShadow == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_textShadowSet) return; m_textShadow = value; m_textShadowSet = set; Changed(StylePropertyId.TextShadow); }
        }
        public StyleLength top
        {
            get => m_topSet ? m_top : new StyleLength(StyleKeyword.Null);
            set { if (m_topSet && m_top == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_topSet) return; m_top = value; m_topSet = set; Changed(StylePropertyId.Top); }
        }
        public StyleTransformOrigin transformOrigin
        {
            get => m_transformOriginSet ? m_transformOrigin : new StyleTransformOrigin(StyleKeyword.Null);
            set { if (m_transformOriginSet && m_transformOrigin == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_transformOriginSet) return; m_transformOrigin = value; m_transformOriginSet = set; Changed(StylePropertyId.TransformOrigin); }
        }
        public StyleList<TimeValue> transitionDelay
        {
            get => m_transitionDelaySet ? m_transitionDelay : new StyleList<TimeValue>(StyleKeyword.Null);
            set { if (m_transitionDelaySet && m_transitionDelay == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_transitionDelaySet) return; m_transitionDelay = value; m_transitionDelaySet = set; Changed(StylePropertyId.TransitionDelay); }
        }
        public StyleList<TimeValue> transitionDuration
        {
            get => m_transitionDurationSet ? m_transitionDuration : new StyleList<TimeValue>(StyleKeyword.Null);
            set { if (m_transitionDurationSet && m_transitionDuration == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_transitionDurationSet) return; m_transitionDuration = value; m_transitionDurationSet = set; Changed(StylePropertyId.TransitionDuration); }
        }
        public StyleList<StylePropertyName> transitionProperty
        {
            get => m_transitionPropertySet ? m_transitionProperty : new StyleList<StylePropertyName>(StyleKeyword.Null);
            set { if (m_transitionPropertySet && m_transitionProperty == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_transitionPropertySet) return; m_transitionProperty = value; m_transitionPropertySet = set; Changed(StylePropertyId.TransitionProperty); }
        }
        public StyleList<EasingFunction> transitionTimingFunction
        {
            get => m_transitionTimingFunctionSet ? m_transitionTimingFunction : new StyleList<EasingFunction>(StyleKeyword.Null);
            set { if (m_transitionTimingFunctionSet && m_transitionTimingFunction == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_transitionTimingFunctionSet) return; m_transitionTimingFunction = value; m_transitionTimingFunctionSet = set; Changed(StylePropertyId.TransitionTimingFunction); }
        }
        public StyleTranslate translate
        {
            get => m_translateSet ? m_translate : new StyleTranslate(StyleKeyword.Null);
            set { if (m_translateSet && m_translate == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_translateSet) return; m_translate = value; m_translateSet = set; Changed(StylePropertyId.Translate); }
        }
        public StyleColor unityBackgroundImageTintColor
        {
            get => m_unityBackgroundImageTintColorSet ? m_unityBackgroundImageTintColor : new StyleColor(StyleKeyword.Null);
            set { if (m_unityBackgroundImageTintColorSet && m_unityBackgroundImageTintColor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityBackgroundImageTintColorSet) return; m_unityBackgroundImageTintColor = value; m_unityBackgroundImageTintColorSet = set; Changed(StylePropertyId.UnityBackgroundImageTintColor); }
        }
        public StyleEnum<EditorTextRenderingMode> unityEditorTextRenderingMode
        {
            get => m_unityEditorTextRenderingModeSet ? m_unityEditorTextRenderingMode : new StyleEnum<EditorTextRenderingMode>(StyleKeyword.Null);
            set { if (m_unityEditorTextRenderingModeSet && m_unityEditorTextRenderingMode == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityEditorTextRenderingModeSet) return; m_unityEditorTextRenderingMode = value; m_unityEditorTextRenderingModeSet = set; Changed(StylePropertyId.UnityEditorTextRenderingMode); }
        }
        public StyleFont unityFont
        {
            get => m_unityFontSet ? m_unityFont : new StyleFont(StyleKeyword.Null);
            set { if (m_unityFontSet && m_unityFont == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityFontSet) return; m_unityFont = value; m_unityFontSet = set; Changed(StylePropertyId.UnityFont); }
        }
        public StyleFontDefinition unityFontDefinition
        {
            get => m_unityFontDefinitionSet ? m_unityFontDefinition : new StyleFontDefinition(StyleKeyword.Null);
            set { if (m_unityFontDefinitionSet && m_unityFontDefinition == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityFontDefinitionSet) return; m_unityFontDefinition = value; m_unityFontDefinitionSet = set; Changed(StylePropertyId.UnityFontDefinition); }
        }
        public StyleEnum<FontStyle> unityFontStyleAndWeight
        {
            get => m_unityFontStyleAndWeightSet ? m_unityFontStyleAndWeight : new StyleEnum<FontStyle>(StyleKeyword.Null);
            set { if (m_unityFontStyleAndWeightSet && m_unityFontStyleAndWeight == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityFontStyleAndWeightSet) return; m_unityFontStyleAndWeight = value; m_unityFontStyleAndWeightSet = set; Changed(StylePropertyId.UnityFontStyleAndWeight); }
        }
        public StyleEnum<OverflowClipBox> unityOverflowClipBox
        {
            get => m_unityOverflowClipBoxSet ? m_unityOverflowClipBox : new StyleEnum<OverflowClipBox>(StyleKeyword.Null);
            set { if (m_unityOverflowClipBoxSet && m_unityOverflowClipBox == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityOverflowClipBoxSet) return; m_unityOverflowClipBox = value; m_unityOverflowClipBoxSet = set; Changed(StylePropertyId.UnityOverflowClipBox); }
        }
        public StyleLength unityParagraphSpacing
        {
            get => m_unityParagraphSpacingSet ? m_unityParagraphSpacing : new StyleLength(StyleKeyword.Null);
            set { if (m_unityParagraphSpacingSet && m_unityParagraphSpacing == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityParagraphSpacingSet) return; m_unityParagraphSpacing = value; m_unityParagraphSpacingSet = set; Changed(StylePropertyId.UnityParagraphSpacing); }
        }
        public StyleInt unitySliceBottom
        {
            get => m_unitySliceBottomSet ? m_unitySliceBottom : new StyleInt(StyleKeyword.Null);
            set { if (m_unitySliceBottomSet && m_unitySliceBottom == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unitySliceBottomSet) return; m_unitySliceBottom = value; m_unitySliceBottomSet = set; Changed(StylePropertyId.UnitySliceBottom); }
        }
        public StyleInt unitySliceLeft
        {
            get => m_unitySliceLeftSet ? m_unitySliceLeft : new StyleInt(StyleKeyword.Null);
            set { if (m_unitySliceLeftSet && m_unitySliceLeft == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unitySliceLeftSet) return; m_unitySliceLeft = value; m_unitySliceLeftSet = set; Changed(StylePropertyId.UnitySliceLeft); }
        }
        public StyleInt unitySliceRight
        {
            get => m_unitySliceRightSet ? m_unitySliceRight : new StyleInt(StyleKeyword.Null);
            set { if (m_unitySliceRightSet && m_unitySliceRight == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unitySliceRightSet) return; m_unitySliceRight = value; m_unitySliceRightSet = set; Changed(StylePropertyId.UnitySliceRight); }
        }
        public StyleFloat unitySliceScale
        {
            get => m_unitySliceScaleSet ? m_unitySliceScale : new StyleFloat(StyleKeyword.Null);
            set { if (m_unitySliceScaleSet && m_unitySliceScale == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unitySliceScaleSet) return; m_unitySliceScale = value; m_unitySliceScaleSet = set; Changed(StylePropertyId.UnitySliceScale); }
        }
        public StyleInt unitySliceTop
        {
            get => m_unitySliceTopSet ? m_unitySliceTop : new StyleInt(StyleKeyword.Null);
            set { if (m_unitySliceTopSet && m_unitySliceTop == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unitySliceTopSet) return; m_unitySliceTop = value; m_unitySliceTopSet = set; Changed(StylePropertyId.UnitySliceTop); }
        }
        public StyleEnum<SliceType> unitySliceType
        {
            get => m_unitySliceTypeSet ? m_unitySliceType : new StyleEnum<SliceType>(StyleKeyword.Null);
            set { if (m_unitySliceTypeSet && m_unitySliceType == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unitySliceTypeSet) return; m_unitySliceType = value; m_unitySliceTypeSet = set; Changed(StylePropertyId.UnitySliceType); }
        }
        public StyleEnum<TextAnchor> unityTextAlign
        {
            get => m_unityTextAlignSet ? m_unityTextAlign : new StyleEnum<TextAnchor>(StyleKeyword.Null);
            set { if (m_unityTextAlignSet && m_unityTextAlign == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityTextAlignSet) return; m_unityTextAlign = value; m_unityTextAlignSet = set; Changed(StylePropertyId.UnityTextAlign); }
        }
        public StyleEnum<TextGeneratorType> unityTextGenerator
        {
            get => m_unityTextGeneratorSet ? m_unityTextGenerator : new StyleEnum<TextGeneratorType>(StyleKeyword.Null);
            set { if (m_unityTextGeneratorSet && m_unityTextGenerator == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityTextGeneratorSet) return; m_unityTextGenerator = value; m_unityTextGeneratorSet = set; Changed(StylePropertyId.UnityTextGenerator); }
        }
        public StyleColor unityTextOutlineColor
        {
            get => m_unityTextOutlineColorSet ? m_unityTextOutlineColor : new StyleColor(StyleKeyword.Null);
            set { if (m_unityTextOutlineColorSet && m_unityTextOutlineColor == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityTextOutlineColorSet) return; m_unityTextOutlineColor = value; m_unityTextOutlineColorSet = set; Changed(StylePropertyId.UnityTextOutlineColor); }
        }
        public StyleFloat unityTextOutlineWidth
        {
            get => m_unityTextOutlineWidthSet ? m_unityTextOutlineWidth : new StyleFloat(StyleKeyword.Null);
            set { if (m_unityTextOutlineWidthSet && m_unityTextOutlineWidth == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityTextOutlineWidthSet) return; m_unityTextOutlineWidth = value; m_unityTextOutlineWidthSet = set; Changed(StylePropertyId.UnityTextOutlineWidth); }
        }
        public StyleEnum<TextOverflowPosition> unityTextOverflowPosition
        {
            get => m_unityTextOverflowPositionSet ? m_unityTextOverflowPosition : new StyleEnum<TextOverflowPosition>(StyleKeyword.Null);
            set { if (m_unityTextOverflowPositionSet && m_unityTextOverflowPosition == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_unityTextOverflowPositionSet) return; m_unityTextOverflowPosition = value; m_unityTextOverflowPositionSet = set; Changed(StylePropertyId.UnityTextOverflowPosition); }
        }
        public StyleEnum<Visibility> visibility
        {
            get => m_visibilitySet ? m_visibility : new StyleEnum<Visibility>(StyleKeyword.Null);
            set { if (m_visibilitySet && m_visibility == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_visibilitySet) return; m_visibility = value; m_visibilitySet = set; Changed(StylePropertyId.Visibility); }
        }
        public StyleEnum<WhiteSpace> whiteSpace
        {
            get => m_whiteSpaceSet ? m_whiteSpace : new StyleEnum<WhiteSpace>(StyleKeyword.Null);
            set { if (m_whiteSpaceSet && m_whiteSpace == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_whiteSpaceSet) return; m_whiteSpace = value; m_whiteSpaceSet = set; Changed(StylePropertyId.WhiteSpace); }
        }
        public StyleLength width
        {
            get => m_widthSet ? m_width : new StyleLength(StyleKeyword.Null);
            set { if (m_widthSet && m_width == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_widthSet) return; m_width = value; m_widthSet = set; Changed(StylePropertyId.Width); }
        }
        public StyleLength wordSpacing
        {
            get => m_wordSpacingSet ? m_wordSpacing : new StyleLength(StyleKeyword.Null);
            set { if (m_wordSpacingSet && m_wordSpacing == value) return; bool set = value.keyword != StyleKeyword.Null; if (!set && !m_wordSpacingSet) return; m_wordSpacing = value; m_wordSpacingSet = set; Changed(StylePropertyId.WordSpacing); }
        }

        [Obsolete("unityBackgroundScaleMode is deprecated. Use background-* properties instead.")]
        public StyleEnum<ScaleMode> unityBackgroundScaleMode
        {
            get => new StyleEnum<ScaleMode>(BackgroundPropertyHelper.ResolveUnityBackgroundScaleMode(backgroundPositionX.value, backgroundPositionY.value, backgroundRepeat.value, backgroundSize.value, out _));
            set
            {
                var mode = value.value;
                backgroundPositionX = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(mode);
                backgroundPositionY = BackgroundPropertyHelper.ConvertScaleModeToBackgroundPosition(mode);
                backgroundRepeat = BackgroundPropertyHelper.ConvertScaleModeToBackgroundRepeat(mode);
                backgroundSize = BackgroundPropertyHelper.ConvertScaleModeToBackgroundSize(mode);
            }
        }

        /// <summary>Applies the set values to a computed style (Initial resets to the initial value).</summary>
        internal void ApplyTo(ComputedValues c)
        {
            var i = ComputedValues.Initial;
            if (m_alignContentSet) c.alignContent = m_alignContent.keyword == StyleKeyword.Initial ? i.alignContent : (Align)(int)(object)m_alignContent.value;
            if (m_alignItemsSet) c.alignItems = m_alignItems.keyword == StyleKeyword.Initial ? i.alignItems : (Align)(int)(object)m_alignItems.value;
            if (m_alignSelfSet) c.alignSelf = m_alignSelf.keyword == StyleKeyword.Initial ? i.alignSelf : (Align)(int)(object)m_alignSelf.value;
            if (m_backgroundColorSet) c.backgroundColor = m_backgroundColor.keyword == StyleKeyword.Initial ? i.backgroundColor : m_backgroundColor.value;
            if (m_backgroundImageSet) c.backgroundImage = m_backgroundImage.keyword == StyleKeyword.Initial ? i.backgroundImage : m_backgroundImage.value;
            if (m_backgroundPositionXSet) c.backgroundPositionX = m_backgroundPositionX.keyword == StyleKeyword.Initial ? i.backgroundPositionX : m_backgroundPositionX.value;
            if (m_backgroundPositionYSet) c.backgroundPositionY = m_backgroundPositionY.keyword == StyleKeyword.Initial ? i.backgroundPositionY : m_backgroundPositionY.value;
            if (m_backgroundRepeatSet) c.backgroundRepeat = m_backgroundRepeat.keyword == StyleKeyword.Initial ? i.backgroundRepeat : m_backgroundRepeat.value;
            if (m_backgroundSizeSet) c.backgroundSize = m_backgroundSize.keyword == StyleKeyword.Initial ? i.backgroundSize : m_backgroundSize.value;
            if (m_borderBottomColorSet) c.borderBottomColor = m_borderBottomColor.keyword == StyleKeyword.Initial ? i.borderBottomColor : m_borderBottomColor.value;
            if (m_borderBottomLeftRadiusSet) c.borderBottomLeftRadius = m_borderBottomLeftRadius.keyword == StyleKeyword.Initial ? i.borderBottomLeftRadius : m_borderBottomLeftRadius.value;
            if (m_borderBottomRightRadiusSet) c.borderBottomRightRadius = m_borderBottomRightRadius.keyword == StyleKeyword.Initial ? i.borderBottomRightRadius : m_borderBottomRightRadius.value;
            if (m_borderBottomWidthSet) c.borderBottomWidth = m_borderBottomWidth.keyword == StyleKeyword.Initial ? i.borderBottomWidth : m_borderBottomWidth.value;
            if (m_borderLeftColorSet) c.borderLeftColor = m_borderLeftColor.keyword == StyleKeyword.Initial ? i.borderLeftColor : m_borderLeftColor.value;
            if (m_borderLeftWidthSet) c.borderLeftWidth = m_borderLeftWidth.keyword == StyleKeyword.Initial ? i.borderLeftWidth : m_borderLeftWidth.value;
            if (m_borderRightColorSet) c.borderRightColor = m_borderRightColor.keyword == StyleKeyword.Initial ? i.borderRightColor : m_borderRightColor.value;
            if (m_borderRightWidthSet) c.borderRightWidth = m_borderRightWidth.keyword == StyleKeyword.Initial ? i.borderRightWidth : m_borderRightWidth.value;
            if (m_borderTopColorSet) c.borderTopColor = m_borderTopColor.keyword == StyleKeyword.Initial ? i.borderTopColor : m_borderTopColor.value;
            if (m_borderTopLeftRadiusSet) c.borderTopLeftRadius = m_borderTopLeftRadius.keyword == StyleKeyword.Initial ? i.borderTopLeftRadius : m_borderTopLeftRadius.value;
            if (m_borderTopRightRadiusSet) c.borderTopRightRadius = m_borderTopRightRadius.keyword == StyleKeyword.Initial ? i.borderTopRightRadius : m_borderTopRightRadius.value;
            if (m_borderTopWidthSet) c.borderTopWidth = m_borderTopWidth.keyword == StyleKeyword.Initial ? i.borderTopWidth : m_borderTopWidth.value;
            if (m_bottomSet) c.bottom = m_bottom.keyword == StyleKeyword.Initial ? i.bottom : m_bottom.value;
            if (m_colorSet) c.color = m_color.keyword == StyleKeyword.Initial ? i.color : m_color.value;
            if (m_cursorSet) c.cursor = m_cursor.keyword == StyleKeyword.Initial ? i.cursor : m_cursor.value;
            if (m_displaySet) c.display = m_display.keyword == StyleKeyword.Initial ? i.display : (DisplayStyle)(int)(object)m_display.value;
            if (m_flexBasisSet) c.flexBasis = m_flexBasis.keyword == StyleKeyword.Initial ? i.flexBasis : m_flexBasis.value;
            if (m_flexDirectionSet) c.flexDirection = m_flexDirection.keyword == StyleKeyword.Initial ? i.flexDirection : (FlexDirection)(int)(object)m_flexDirection.value;
            if (m_flexGrowSet) c.flexGrow = m_flexGrow.keyword == StyleKeyword.Initial ? i.flexGrow : m_flexGrow.value;
            if (m_flexShrinkSet) c.flexShrink = m_flexShrink.keyword == StyleKeyword.Initial ? i.flexShrink : m_flexShrink.value;
            if (m_flexWrapSet) c.flexWrap = m_flexWrap.keyword == StyleKeyword.Initial ? i.flexWrap : (Wrap)(int)(object)m_flexWrap.value;
            if (m_fontSizeSet) c.fontSize = m_fontSize.keyword == StyleKeyword.Initial ? i.fontSize : m_fontSize.value;
            if (m_heightSet) c.height = m_height.keyword == StyleKeyword.Initial ? i.height : m_height.value;
            if (m_justifyContentSet) c.justifyContent = m_justifyContent.keyword == StyleKeyword.Initial ? i.justifyContent : (Justify)(int)(object)m_justifyContent.value;
            if (m_leftSet) c.left = m_left.keyword == StyleKeyword.Initial ? i.left : m_left.value;
            if (m_letterSpacingSet) c.letterSpacing = m_letterSpacing.keyword == StyleKeyword.Initial ? i.letterSpacing : m_letterSpacing.value;
            if (m_marginBottomSet) c.marginBottom = m_marginBottom.keyword == StyleKeyword.Initial ? i.marginBottom : m_marginBottom.value;
            if (m_marginLeftSet) c.marginLeft = m_marginLeft.keyword == StyleKeyword.Initial ? i.marginLeft : m_marginLeft.value;
            if (m_marginRightSet) c.marginRight = m_marginRight.keyword == StyleKeyword.Initial ? i.marginRight : m_marginRight.value;
            if (m_marginTopSet) c.marginTop = m_marginTop.keyword == StyleKeyword.Initial ? i.marginTop : m_marginTop.value;
            if (m_maxHeightSet) c.maxHeight = m_maxHeight.keyword == StyleKeyword.Initial ? i.maxHeight : m_maxHeight.value;
            if (m_maxWidthSet) c.maxWidth = m_maxWidth.keyword == StyleKeyword.Initial ? i.maxWidth : m_maxWidth.value;
            if (m_minHeightSet) c.minHeight = m_minHeight.keyword == StyleKeyword.Initial ? i.minHeight : m_minHeight.value;
            if (m_minWidthSet) c.minWidth = m_minWidth.keyword == StyleKeyword.Initial ? i.minWidth : m_minWidth.value;
            if (m_opacitySet) c.opacity = m_opacity.keyword == StyleKeyword.Initial ? i.opacity : m_opacity.value;
            if (m_overflowSet) c.overflow = m_overflow.keyword == StyleKeyword.Initial ? i.overflow : (OverflowInternal)(int)(object)m_overflow.value;
            if (m_paddingBottomSet) c.paddingBottom = m_paddingBottom.keyword == StyleKeyword.Initial ? i.paddingBottom : m_paddingBottom.value;
            if (m_paddingLeftSet) c.paddingLeft = m_paddingLeft.keyword == StyleKeyword.Initial ? i.paddingLeft : m_paddingLeft.value;
            if (m_paddingRightSet) c.paddingRight = m_paddingRight.keyword == StyleKeyword.Initial ? i.paddingRight : m_paddingRight.value;
            if (m_paddingTopSet) c.paddingTop = m_paddingTop.keyword == StyleKeyword.Initial ? i.paddingTop : m_paddingTop.value;
            if (m_positionSet) c.position = m_position.keyword == StyleKeyword.Initial ? i.position : (Position)(int)(object)m_position.value;
            if (m_rightSet) c.right = m_right.keyword == StyleKeyword.Initial ? i.right : m_right.value;
            if (m_rotateSet) c.rotate = m_rotate.keyword == StyleKeyword.Initial ? i.rotate : m_rotate.value;
            if (m_scaleSet) c.scale = m_scale.keyword == StyleKeyword.Initial ? i.scale : m_scale.value;
            if (m_textOverflowSet) c.textOverflow = m_textOverflow.keyword == StyleKeyword.Initial ? i.textOverflow : (TextOverflow)(int)(object)m_textOverflow.value;
            if (m_textShadowSet) c.textShadow = m_textShadow.keyword == StyleKeyword.Initial ? i.textShadow : m_textShadow.value;
            if (m_topSet) c.top = m_top.keyword == StyleKeyword.Initial ? i.top : m_top.value;
            if (m_transformOriginSet) c.transformOrigin = m_transformOrigin.keyword == StyleKeyword.Initial ? i.transformOrigin : m_transformOrigin.value;
            if (m_transitionDelaySet) c.transitionDelay = m_transitionDelay.keyword == StyleKeyword.Initial ? new List<TimeValue>(i.transitionDelay) : m_transitionDelay.value == null ? new List<TimeValue>() : new List<TimeValue>(m_transitionDelay.value);
            if (m_transitionDurationSet) c.transitionDuration = m_transitionDuration.keyword == StyleKeyword.Initial ? new List<TimeValue>(i.transitionDuration) : m_transitionDuration.value == null ? new List<TimeValue>() : new List<TimeValue>(m_transitionDuration.value);
            if (m_transitionPropertySet) c.transitionProperty = m_transitionProperty.keyword == StyleKeyword.Initial ? new List<StylePropertyName>(i.transitionProperty) : m_transitionProperty.value == null ? new List<StylePropertyName>() : new List<StylePropertyName>(m_transitionProperty.value);
            if (m_transitionTimingFunctionSet) c.transitionTimingFunction = m_transitionTimingFunction.keyword == StyleKeyword.Initial ? new List<EasingFunction>(i.transitionTimingFunction) : m_transitionTimingFunction.value == null ? new List<EasingFunction>() : new List<EasingFunction>(m_transitionTimingFunction.value);
            if (m_translateSet) c.translate = m_translate.keyword == StyleKeyword.Initial ? i.translate : m_translate.value;
            if (m_unityBackgroundImageTintColorSet) c.unityBackgroundImageTintColor = m_unityBackgroundImageTintColor.keyword == StyleKeyword.Initial ? i.unityBackgroundImageTintColor : m_unityBackgroundImageTintColor.value;
            if (m_unityEditorTextRenderingModeSet) c.unityEditorTextRenderingMode = m_unityEditorTextRenderingMode.keyword == StyleKeyword.Initial ? i.unityEditorTextRenderingMode : (EditorTextRenderingMode)(int)(object)m_unityEditorTextRenderingMode.value;
            if (m_unityFontSet) c.unityFont = m_unityFont.keyword == StyleKeyword.Initial ? i.unityFont : m_unityFont.value;
            if (m_unityFontDefinitionSet) c.unityFontDefinition = m_unityFontDefinition.keyword == StyleKeyword.Initial ? i.unityFontDefinition : m_unityFontDefinition.value;
            if (m_unityFontStyleAndWeightSet) c.unityFontStyleAndWeight = m_unityFontStyleAndWeight.keyword == StyleKeyword.Initial ? i.unityFontStyleAndWeight : (FontStyle)(int)(object)m_unityFontStyleAndWeight.value;
            if (m_unityOverflowClipBoxSet) c.unityOverflowClipBox = m_unityOverflowClipBox.keyword == StyleKeyword.Initial ? i.unityOverflowClipBox : (OverflowClipBox)(int)(object)m_unityOverflowClipBox.value;
            if (m_unityParagraphSpacingSet) c.unityParagraphSpacing = m_unityParagraphSpacing.keyword == StyleKeyword.Initial ? i.unityParagraphSpacing : m_unityParagraphSpacing.value;
            if (m_unitySliceBottomSet) c.unitySliceBottom = m_unitySliceBottom.keyword == StyleKeyword.Initial ? i.unitySliceBottom : m_unitySliceBottom.value;
            if (m_unitySliceLeftSet) c.unitySliceLeft = m_unitySliceLeft.keyword == StyleKeyword.Initial ? i.unitySliceLeft : m_unitySliceLeft.value;
            if (m_unitySliceRightSet) c.unitySliceRight = m_unitySliceRight.keyword == StyleKeyword.Initial ? i.unitySliceRight : m_unitySliceRight.value;
            if (m_unitySliceScaleSet) c.unitySliceScale = m_unitySliceScale.keyword == StyleKeyword.Initial ? i.unitySliceScale : m_unitySliceScale.value;
            if (m_unitySliceTopSet) c.unitySliceTop = m_unitySliceTop.keyword == StyleKeyword.Initial ? i.unitySliceTop : m_unitySliceTop.value;
            if (m_unitySliceTypeSet) c.unitySliceType = m_unitySliceType.keyword == StyleKeyword.Initial ? i.unitySliceType : (SliceType)(int)(object)m_unitySliceType.value;
            if (m_unityTextAlignSet) c.unityTextAlign = m_unityTextAlign.keyword == StyleKeyword.Initial ? i.unityTextAlign : (TextAnchor)(int)(object)m_unityTextAlign.value;
            if (m_unityTextGeneratorSet) c.unityTextGenerator = m_unityTextGenerator.keyword == StyleKeyword.Initial ? i.unityTextGenerator : (TextGeneratorType)(int)(object)m_unityTextGenerator.value;
            if (m_unityTextOutlineColorSet) c.unityTextOutlineColor = m_unityTextOutlineColor.keyword == StyleKeyword.Initial ? i.unityTextOutlineColor : m_unityTextOutlineColor.value;
            if (m_unityTextOutlineWidthSet) c.unityTextOutlineWidth = m_unityTextOutlineWidth.keyword == StyleKeyword.Initial ? i.unityTextOutlineWidth : m_unityTextOutlineWidth.value;
            if (m_unityTextOverflowPositionSet) c.unityTextOverflowPosition = m_unityTextOverflowPosition.keyword == StyleKeyword.Initial ? i.unityTextOverflowPosition : (TextOverflowPosition)(int)(object)m_unityTextOverflowPosition.value;
            if (m_visibilitySet) c.visibility = m_visibility.keyword == StyleKeyword.Initial ? i.visibility : (Visibility)(int)(object)m_visibility.value;
            if (m_whiteSpaceSet) c.whiteSpace = m_whiteSpace.keyword == StyleKeyword.Initial ? i.whiteSpace : (WhiteSpace)(int)(object)m_whiteSpace.value;
            if (m_widthSet) c.width = m_width.keyword == StyleKeyword.Initial ? i.width : m_width.value;
            if (m_wordSpacingSet) c.wordSpacing = m_wordSpacing.keyword == StyleKeyword.Initial ? i.wordSpacing : m_wordSpacing.value;
        }

        internal bool IsValueSet(StylePropertyId id)
        {
            switch (id)
            {
                case StylePropertyId.AlignContent: return m_alignContentSet;
                case StylePropertyId.AlignItems: return m_alignItemsSet;
                case StylePropertyId.AlignSelf: return m_alignSelfSet;
                case StylePropertyId.BackgroundColor: return m_backgroundColorSet;
                case StylePropertyId.BackgroundImage: return m_backgroundImageSet;
                case StylePropertyId.BackgroundPositionX: return m_backgroundPositionXSet;
                case StylePropertyId.BackgroundPositionY: return m_backgroundPositionYSet;
                case StylePropertyId.BackgroundRepeat: return m_backgroundRepeatSet;
                case StylePropertyId.BackgroundSize: return m_backgroundSizeSet;
                case StylePropertyId.BorderBottomColor: return m_borderBottomColorSet;
                case StylePropertyId.BorderBottomLeftRadius: return m_borderBottomLeftRadiusSet;
                case StylePropertyId.BorderBottomRightRadius: return m_borderBottomRightRadiusSet;
                case StylePropertyId.BorderBottomWidth: return m_borderBottomWidthSet;
                case StylePropertyId.BorderLeftColor: return m_borderLeftColorSet;
                case StylePropertyId.BorderLeftWidth: return m_borderLeftWidthSet;
                case StylePropertyId.BorderRightColor: return m_borderRightColorSet;
                case StylePropertyId.BorderRightWidth: return m_borderRightWidthSet;
                case StylePropertyId.BorderTopColor: return m_borderTopColorSet;
                case StylePropertyId.BorderTopLeftRadius: return m_borderTopLeftRadiusSet;
                case StylePropertyId.BorderTopRightRadius: return m_borderTopRightRadiusSet;
                case StylePropertyId.BorderTopWidth: return m_borderTopWidthSet;
                case StylePropertyId.Bottom: return m_bottomSet;
                case StylePropertyId.Color: return m_colorSet;
                case StylePropertyId.Cursor: return m_cursorSet;
                case StylePropertyId.Display: return m_displaySet;
                case StylePropertyId.FlexBasis: return m_flexBasisSet;
                case StylePropertyId.FlexDirection: return m_flexDirectionSet;
                case StylePropertyId.FlexGrow: return m_flexGrowSet;
                case StylePropertyId.FlexShrink: return m_flexShrinkSet;
                case StylePropertyId.FlexWrap: return m_flexWrapSet;
                case StylePropertyId.FontSize: return m_fontSizeSet;
                case StylePropertyId.Height: return m_heightSet;
                case StylePropertyId.JustifyContent: return m_justifyContentSet;
                case StylePropertyId.Left: return m_leftSet;
                case StylePropertyId.LetterSpacing: return m_letterSpacingSet;
                case StylePropertyId.MarginBottom: return m_marginBottomSet;
                case StylePropertyId.MarginLeft: return m_marginLeftSet;
                case StylePropertyId.MarginRight: return m_marginRightSet;
                case StylePropertyId.MarginTop: return m_marginTopSet;
                case StylePropertyId.MaxHeight: return m_maxHeightSet;
                case StylePropertyId.MaxWidth: return m_maxWidthSet;
                case StylePropertyId.MinHeight: return m_minHeightSet;
                case StylePropertyId.MinWidth: return m_minWidthSet;
                case StylePropertyId.Opacity: return m_opacitySet;
                case StylePropertyId.Overflow: return m_overflowSet;
                case StylePropertyId.PaddingBottom: return m_paddingBottomSet;
                case StylePropertyId.PaddingLeft: return m_paddingLeftSet;
                case StylePropertyId.PaddingRight: return m_paddingRightSet;
                case StylePropertyId.PaddingTop: return m_paddingTopSet;
                case StylePropertyId.Position: return m_positionSet;
                case StylePropertyId.Right: return m_rightSet;
                case StylePropertyId.Rotate: return m_rotateSet;
                case StylePropertyId.Scale: return m_scaleSet;
                case StylePropertyId.TextOverflow: return m_textOverflowSet;
                case StylePropertyId.TextShadow: return m_textShadowSet;
                case StylePropertyId.Top: return m_topSet;
                case StylePropertyId.TransformOrigin: return m_transformOriginSet;
                case StylePropertyId.TransitionDelay: return m_transitionDelaySet;
                case StylePropertyId.TransitionDuration: return m_transitionDurationSet;
                case StylePropertyId.TransitionProperty: return m_transitionPropertySet;
                case StylePropertyId.TransitionTimingFunction: return m_transitionTimingFunctionSet;
                case StylePropertyId.Translate: return m_translateSet;
                case StylePropertyId.UnityBackgroundImageTintColor: return m_unityBackgroundImageTintColorSet;
                case StylePropertyId.UnityEditorTextRenderingMode: return m_unityEditorTextRenderingModeSet;
                case StylePropertyId.UnityFont: return m_unityFontSet;
                case StylePropertyId.UnityFontDefinition: return m_unityFontDefinitionSet;
                case StylePropertyId.UnityFontStyleAndWeight: return m_unityFontStyleAndWeightSet;
                case StylePropertyId.UnityOverflowClipBox: return m_unityOverflowClipBoxSet;
                case StylePropertyId.UnityParagraphSpacing: return m_unityParagraphSpacingSet;
                case StylePropertyId.UnitySliceBottom: return m_unitySliceBottomSet;
                case StylePropertyId.UnitySliceLeft: return m_unitySliceLeftSet;
                case StylePropertyId.UnitySliceRight: return m_unitySliceRightSet;
                case StylePropertyId.UnitySliceScale: return m_unitySliceScaleSet;
                case StylePropertyId.UnitySliceTop: return m_unitySliceTopSet;
                case StylePropertyId.UnitySliceType: return m_unitySliceTypeSet;
                case StylePropertyId.UnityTextAlign: return m_unityTextAlignSet;
                case StylePropertyId.UnityTextGenerator: return m_unityTextGeneratorSet;
                case StylePropertyId.UnityTextOutlineColor: return m_unityTextOutlineColorSet;
                case StylePropertyId.UnityTextOutlineWidth: return m_unityTextOutlineWidthSet;
                case StylePropertyId.UnityTextOverflowPosition: return m_unityTextOverflowPositionSet;
                case StylePropertyId.Visibility: return m_visibilitySet;
                case StylePropertyId.WhiteSpace: return m_whiteSpaceSet;
                case StylePropertyId.Width: return m_widthSet;
                case StylePropertyId.WordSpacing: return m_wordSpacingSet;
            }
            return false;
        }
    }

    /// <summary>What resolvedStyle answers: layout results for box values, computed values otherwise.</summary>
    internal sealed partial class ResolvedStyle : IResolvedStyle
    {
        readonly VisualElement ve;
        internal ResolvedStyle(VisualElement element) { ve = element; }
        ComputedValues cs => ve.computedValues;
        public Align alignContent => cs.alignContent;
        public Align alignItems => cs.alignItems;
        public Align alignSelf => cs.alignSelf;
        public Color backgroundColor => cs.backgroundColor;
        public Background backgroundImage => cs.backgroundImage;
        public BackgroundPosition backgroundPositionX => cs.backgroundPositionX;
        public BackgroundPosition backgroundPositionY => cs.backgroundPositionY;
        public BackgroundRepeat backgroundRepeat => cs.backgroundRepeat;
        public BackgroundSize backgroundSize => cs.backgroundSize;
        public Color borderBottomColor => cs.borderBottomColor;
        public float borderBottomLeftRadius => cs.borderBottomLeftRadius.value;
        public float borderBottomRightRadius => cs.borderBottomRightRadius.value;
        public float borderBottomWidth => ve.yogaNode.LayoutBorderBottom;
        public Color borderLeftColor => cs.borderLeftColor;
        public float borderLeftWidth => ve.yogaNode.LayoutBorderLeft;
        public Color borderRightColor => cs.borderRightColor;
        public float borderRightWidth => ve.yogaNode.LayoutBorderRight;
        public Color borderTopColor => cs.borderTopColor;
        public float borderTopLeftRadius => cs.borderTopLeftRadius.value;
        public float borderTopRightRadius => cs.borderTopRightRadius.value;
        public float borderTopWidth => ve.yogaNode.LayoutBorderTop;
        public float bottom => ve.yogaNode.LayoutBottom;
        public Color color => cs.color;
        public DisplayStyle display => cs.display;
        public StyleFloat flexBasis => new StyleFloat(ve.yogaNode.ComputedFlexBasis);
        public FlexDirection flexDirection => cs.flexDirection;
        public float flexGrow => cs.flexGrow;
        public float flexShrink => cs.flexShrink;
        public Wrap flexWrap => cs.flexWrap;
        public float fontSize => cs.fontSize.value;
        public float height => ve.yogaNode.LayoutHeight;
        public Justify justifyContent => cs.justifyContent;
        public float left => ve.yogaNode.LayoutX;
        public float letterSpacing => cs.letterSpacing.value;
        public float marginBottom => ve.yogaNode.LayoutMarginBottom;
        public float marginLeft => ve.yogaNode.LayoutMarginLeft;
        public float marginRight => ve.yogaNode.LayoutMarginRight;
        public float marginTop => ve.yogaNode.LayoutMarginTop;
        public StyleFloat maxHeight => ve.ResolveLengthValue(cs.maxHeight, false);
        public StyleFloat maxWidth => ve.ResolveLengthValue(cs.maxWidth, true);
        public StyleFloat minHeight => ve.ResolveLengthValue(cs.minHeight, false);
        public StyleFloat minWidth => ve.ResolveLengthValue(cs.minWidth, true);
        public float opacity => cs.opacity;
        public float paddingBottom => ve.yogaNode.LayoutPaddingBottom;
        public float paddingLeft => ve.yogaNode.LayoutPaddingLeft;
        public float paddingRight => ve.yogaNode.LayoutPaddingRight;
        public float paddingTop => ve.yogaNode.LayoutPaddingTop;
        public Position position => cs.position;
        public float right => ve.yogaNode.LayoutRight;
        public Rotate rotate => cs.rotate;
        public Scale scale => cs.scale;
        public TextOverflow textOverflow => cs.textOverflow;
        public float top => ve.yogaNode.LayoutY;
        public Vector3 transformOrigin => ve.ResolveTransformOrigin();
        public IEnumerable<TimeValue> transitionDelay => cs.transitionDelay;
        public IEnumerable<TimeValue> transitionDuration => cs.transitionDuration;
        public IEnumerable<StylePropertyName> transitionProperty => cs.transitionProperty;
        public IEnumerable<EasingFunction> transitionTimingFunction => cs.transitionTimingFunction;
        public Vector3 translate => ve.ResolveTranslate();
        public Color unityBackgroundImageTintColor => cs.unityBackgroundImageTintColor;
        public EditorTextRenderingMode unityEditorTextRenderingMode => cs.unityEditorTextRenderingMode;
        public Font unityFont => cs.unityFont;
        public FontDefinition unityFontDefinition => cs.unityFontDefinition;
        public FontStyle unityFontStyleAndWeight => cs.unityFontStyleAndWeight;
        public float unityParagraphSpacing => cs.unityParagraphSpacing.value;
        public int unitySliceBottom => cs.unitySliceBottom;
        public int unitySliceLeft => cs.unitySliceLeft;
        public int unitySliceRight => cs.unitySliceRight;
        public float unitySliceScale => cs.unitySliceScale;
        public int unitySliceTop => cs.unitySliceTop;
        public SliceType unitySliceType => cs.unitySliceType;
        public TextAnchor unityTextAlign => cs.unityTextAlign;
        public TextGeneratorType unityTextGenerator => cs.unityTextGenerator;
        public Color unityTextOutlineColor => cs.unityTextOutlineColor;
        public float unityTextOutlineWidth => cs.unityTextOutlineWidth;
        public TextOverflowPosition unityTextOverflowPosition => cs.unityTextOverflowPosition;
        public Visibility visibility => cs.visibility;
        public WhiteSpace whiteSpace => cs.whiteSpace;
        public float width => ve.yogaNode.LayoutWidth;
        public float wordSpacing => cs.wordSpacing.value;
        [Obsolete("unityBackgroundScaleMode is deprecated. Use background-* properties instead.")]
        public StyleEnum<ScaleMode> unityBackgroundScaleMode => BackgroundPropertyHelper.ResolveUnityBackgroundScaleMode(cs.backgroundPositionX, cs.backgroundPositionY, cs.backgroundRepeat, cs.backgroundSize, out _);
    }
}

namespace UnityEngine.UIElements
{
    using Unbound.UIElements;

    public partial interface IStyle
    {
        StyleEnum<Align> alignContent { get; set; }
        StyleEnum<Align> alignItems { get; set; }
        StyleEnum<Align> alignSelf { get; set; }
        StyleColor backgroundColor { get; set; }
        StyleBackground backgroundImage { get; set; }
        StyleBackgroundPosition backgroundPositionX { get; set; }
        StyleBackgroundPosition backgroundPositionY { get; set; }
        StyleBackgroundRepeat backgroundRepeat { get; set; }
        StyleBackgroundSize backgroundSize { get; set; }
        StyleColor borderBottomColor { get; set; }
        StyleLength borderBottomLeftRadius { get; set; }
        StyleLength borderBottomRightRadius { get; set; }
        StyleFloat borderBottomWidth { get; set; }
        StyleColor borderLeftColor { get; set; }
        StyleFloat borderLeftWidth { get; set; }
        StyleColor borderRightColor { get; set; }
        StyleFloat borderRightWidth { get; set; }
        StyleColor borderTopColor { get; set; }
        StyleLength borderTopLeftRadius { get; set; }
        StyleLength borderTopRightRadius { get; set; }
        StyleFloat borderTopWidth { get; set; }
        StyleLength bottom { get; set; }
        StyleColor color { get; set; }
        StyleCursor cursor { get; set; }
        StyleEnum<DisplayStyle> display { get; set; }
        StyleLength flexBasis { get; set; }
        StyleEnum<FlexDirection> flexDirection { get; set; }
        StyleFloat flexGrow { get; set; }
        StyleFloat flexShrink { get; set; }
        StyleEnum<Wrap> flexWrap { get; set; }
        StyleLength fontSize { get; set; }
        StyleLength height { get; set; }
        StyleEnum<Justify> justifyContent { get; set; }
        StyleLength left { get; set; }
        StyleLength letterSpacing { get; set; }
        StyleLength marginBottom { get; set; }
        StyleLength marginLeft { get; set; }
        StyleLength marginRight { get; set; }
        StyleLength marginTop { get; set; }
        StyleLength maxHeight { get; set; }
        StyleLength maxWidth { get; set; }
        StyleLength minHeight { get; set; }
        StyleLength minWidth { get; set; }
        StyleFloat opacity { get; set; }
        StyleEnum<Overflow> overflow { get; set; }
        StyleLength paddingBottom { get; set; }
        StyleLength paddingLeft { get; set; }
        StyleLength paddingRight { get; set; }
        StyleLength paddingTop { get; set; }
        StyleEnum<Position> position { get; set; }
        StyleLength right { get; set; }
        StyleRotate rotate { get; set; }
        StyleScale scale { get; set; }
        StyleEnum<TextOverflow> textOverflow { get; set; }
        StyleTextShadow textShadow { get; set; }
        StyleLength top { get; set; }
        StyleTransformOrigin transformOrigin { get; set; }
        StyleList<TimeValue> transitionDelay { get; set; }
        StyleList<TimeValue> transitionDuration { get; set; }
        StyleList<StylePropertyName> transitionProperty { get; set; }
        StyleList<EasingFunction> transitionTimingFunction { get; set; }
        StyleTranslate translate { get; set; }
        StyleColor unityBackgroundImageTintColor { get; set; }
        StyleEnum<EditorTextRenderingMode> unityEditorTextRenderingMode { get; set; }
        StyleFont unityFont { get; set; }
        StyleFontDefinition unityFontDefinition { get; set; }
        StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; }
        StyleEnum<OverflowClipBox> unityOverflowClipBox { get; set; }
        StyleLength unityParagraphSpacing { get; set; }
        StyleInt unitySliceBottom { get; set; }
        StyleInt unitySliceLeft { get; set; }
        StyleInt unitySliceRight { get; set; }
        StyleFloat unitySliceScale { get; set; }
        StyleInt unitySliceTop { get; set; }
        StyleEnum<SliceType> unitySliceType { get; set; }
        StyleEnum<TextAnchor> unityTextAlign { get; set; }
        StyleEnum<TextGeneratorType> unityTextGenerator { get; set; }
        StyleColor unityTextOutlineColor { get; set; }
        StyleFloat unityTextOutlineWidth { get; set; }
        StyleEnum<TextOverflowPosition> unityTextOverflowPosition { get; set; }
        StyleEnum<Visibility> visibility { get; set; }
        StyleEnum<WhiteSpace> whiteSpace { get; set; }
        StyleLength width { get; set; }
        StyleLength wordSpacing { get; set; }
        [Obsolete("unityBackgroundScaleMode is deprecated. Use background-* properties instead.")]
        StyleEnum<ScaleMode> unityBackgroundScaleMode { get; set; }
    }

    public partial interface IResolvedStyle
    {
        Align alignContent { get; }
        Align alignItems { get; }
        Align alignSelf { get; }
        Color backgroundColor { get; }
        Background backgroundImage { get; }
        BackgroundPosition backgroundPositionX { get; }
        BackgroundPosition backgroundPositionY { get; }
        BackgroundRepeat backgroundRepeat { get; }
        BackgroundSize backgroundSize { get; }
        Color borderBottomColor { get; }
        float borderBottomLeftRadius { get; }
        float borderBottomRightRadius { get; }
        float borderBottomWidth { get; }
        Color borderLeftColor { get; }
        float borderLeftWidth { get; }
        Color borderRightColor { get; }
        float borderRightWidth { get; }
        Color borderTopColor { get; }
        float borderTopLeftRadius { get; }
        float borderTopRightRadius { get; }
        float borderTopWidth { get; }
        float bottom { get; }
        Color color { get; }
        DisplayStyle display { get; }
        StyleFloat flexBasis { get; }
        FlexDirection flexDirection { get; }
        float flexGrow { get; }
        float flexShrink { get; }
        Wrap flexWrap { get; }
        float fontSize { get; }
        float height { get; }
        Justify justifyContent { get; }
        float left { get; }
        float letterSpacing { get; }
        float marginBottom { get; }
        float marginLeft { get; }
        float marginRight { get; }
        float marginTop { get; }
        StyleFloat maxHeight { get; }
        StyleFloat maxWidth { get; }
        StyleFloat minHeight { get; }
        StyleFloat minWidth { get; }
        float opacity { get; }
        float paddingBottom { get; }
        float paddingLeft { get; }
        float paddingRight { get; }
        float paddingTop { get; }
        Position position { get; }
        float right { get; }
        Rotate rotate { get; }
        Scale scale { get; }
        TextOverflow textOverflow { get; }
        float top { get; }
        Vector3 transformOrigin { get; }
        IEnumerable<TimeValue> transitionDelay { get; }
        IEnumerable<TimeValue> transitionDuration { get; }
        IEnumerable<StylePropertyName> transitionProperty { get; }
        IEnumerable<EasingFunction> transitionTimingFunction { get; }
        Vector3 translate { get; }
        Color unityBackgroundImageTintColor { get; }
        EditorTextRenderingMode unityEditorTextRenderingMode { get; }
        Font unityFont { get; }
        FontDefinition unityFontDefinition { get; }
        FontStyle unityFontStyleAndWeight { get; }
        float unityParagraphSpacing { get; }
        int unitySliceBottom { get; }
        int unitySliceLeft { get; }
        int unitySliceRight { get; }
        float unitySliceScale { get; }
        int unitySliceTop { get; }
        SliceType unitySliceType { get; }
        TextAnchor unityTextAlign { get; }
        TextGeneratorType unityTextGenerator { get; }
        Color unityTextOutlineColor { get; }
        float unityTextOutlineWidth { get; }
        TextOverflowPosition unityTextOverflowPosition { get; }
        Visibility visibility { get; }
        WhiteSpace whiteSpace { get; }
        float width { get; }
        float wordSpacing { get; }
        [Obsolete("unityBackgroundScaleMode is deprecated. Use background-* properties instead.")]
        StyleEnum<ScaleMode> unityBackgroundScaleMode { get; }
    }

    public partial class VisualElement
    {
        Align IResolvedStyle.alignContent => resolvedStyle.alignContent;
        Align IResolvedStyle.alignItems => resolvedStyle.alignItems;
        Align IResolvedStyle.alignSelf => resolvedStyle.alignSelf;
        Color IResolvedStyle.backgroundColor => resolvedStyle.backgroundColor;
        Background IResolvedStyle.backgroundImage => resolvedStyle.backgroundImage;
        BackgroundPosition IResolvedStyle.backgroundPositionX => resolvedStyle.backgroundPositionX;
        BackgroundPosition IResolvedStyle.backgroundPositionY => resolvedStyle.backgroundPositionY;
        BackgroundRepeat IResolvedStyle.backgroundRepeat => resolvedStyle.backgroundRepeat;
        BackgroundSize IResolvedStyle.backgroundSize => resolvedStyle.backgroundSize;
        Color IResolvedStyle.borderBottomColor => resolvedStyle.borderBottomColor;
        float IResolvedStyle.borderBottomLeftRadius => resolvedStyle.borderBottomLeftRadius;
        float IResolvedStyle.borderBottomRightRadius => resolvedStyle.borderBottomRightRadius;
        float IResolvedStyle.borderBottomWidth => resolvedStyle.borderBottomWidth;
        Color IResolvedStyle.borderLeftColor => resolvedStyle.borderLeftColor;
        float IResolvedStyle.borderLeftWidth => resolvedStyle.borderLeftWidth;
        Color IResolvedStyle.borderRightColor => resolvedStyle.borderRightColor;
        float IResolvedStyle.borderRightWidth => resolvedStyle.borderRightWidth;
        Color IResolvedStyle.borderTopColor => resolvedStyle.borderTopColor;
        float IResolvedStyle.borderTopLeftRadius => resolvedStyle.borderTopLeftRadius;
        float IResolvedStyle.borderTopRightRadius => resolvedStyle.borderTopRightRadius;
        float IResolvedStyle.borderTopWidth => resolvedStyle.borderTopWidth;
        float IResolvedStyle.bottom => resolvedStyle.bottom;
        Color IResolvedStyle.color => resolvedStyle.color;
        DisplayStyle IResolvedStyle.display => resolvedStyle.display;
        StyleFloat IResolvedStyle.flexBasis => resolvedStyle.flexBasis;
        FlexDirection IResolvedStyle.flexDirection => resolvedStyle.flexDirection;
        float IResolvedStyle.flexGrow => resolvedStyle.flexGrow;
        float IResolvedStyle.flexShrink => resolvedStyle.flexShrink;
        Wrap IResolvedStyle.flexWrap => resolvedStyle.flexWrap;
        float IResolvedStyle.fontSize => resolvedStyle.fontSize;
        float IResolvedStyle.height => resolvedStyle.height;
        Justify IResolvedStyle.justifyContent => resolvedStyle.justifyContent;
        float IResolvedStyle.left => resolvedStyle.left;
        float IResolvedStyle.letterSpacing => resolvedStyle.letterSpacing;
        float IResolvedStyle.marginBottom => resolvedStyle.marginBottom;
        float IResolvedStyle.marginLeft => resolvedStyle.marginLeft;
        float IResolvedStyle.marginRight => resolvedStyle.marginRight;
        float IResolvedStyle.marginTop => resolvedStyle.marginTop;
        StyleFloat IResolvedStyle.maxHeight => resolvedStyle.maxHeight;
        StyleFloat IResolvedStyle.maxWidth => resolvedStyle.maxWidth;
        StyleFloat IResolvedStyle.minHeight => resolvedStyle.minHeight;
        StyleFloat IResolvedStyle.minWidth => resolvedStyle.minWidth;
        float IResolvedStyle.opacity => resolvedStyle.opacity;
        float IResolvedStyle.paddingBottom => resolvedStyle.paddingBottom;
        float IResolvedStyle.paddingLeft => resolvedStyle.paddingLeft;
        float IResolvedStyle.paddingRight => resolvedStyle.paddingRight;
        float IResolvedStyle.paddingTop => resolvedStyle.paddingTop;
        Position IResolvedStyle.position => resolvedStyle.position;
        float IResolvedStyle.right => resolvedStyle.right;
        Rotate IResolvedStyle.rotate => resolvedStyle.rotate;
        Scale IResolvedStyle.scale => resolvedStyle.scale;
        TextOverflow IResolvedStyle.textOverflow => resolvedStyle.textOverflow;
        float IResolvedStyle.top => resolvedStyle.top;
        Vector3 IResolvedStyle.transformOrigin => resolvedStyle.transformOrigin;
        IEnumerable<TimeValue> IResolvedStyle.transitionDelay => resolvedStyle.transitionDelay;
        IEnumerable<TimeValue> IResolvedStyle.transitionDuration => resolvedStyle.transitionDuration;
        IEnumerable<StylePropertyName> IResolvedStyle.transitionProperty => resolvedStyle.transitionProperty;
        IEnumerable<EasingFunction> IResolvedStyle.transitionTimingFunction => resolvedStyle.transitionTimingFunction;
        Vector3 IResolvedStyle.translate => resolvedStyle.translate;
        Color IResolvedStyle.unityBackgroundImageTintColor => resolvedStyle.unityBackgroundImageTintColor;
        EditorTextRenderingMode IResolvedStyle.unityEditorTextRenderingMode => resolvedStyle.unityEditorTextRenderingMode;
        Font IResolvedStyle.unityFont => resolvedStyle.unityFont;
        FontDefinition IResolvedStyle.unityFontDefinition => resolvedStyle.unityFontDefinition;
        FontStyle IResolvedStyle.unityFontStyleAndWeight => resolvedStyle.unityFontStyleAndWeight;
        float IResolvedStyle.unityParagraphSpacing => resolvedStyle.unityParagraphSpacing;
        int IResolvedStyle.unitySliceBottom => resolvedStyle.unitySliceBottom;
        int IResolvedStyle.unitySliceLeft => resolvedStyle.unitySliceLeft;
        int IResolvedStyle.unitySliceRight => resolvedStyle.unitySliceRight;
        float IResolvedStyle.unitySliceScale => resolvedStyle.unitySliceScale;
        int IResolvedStyle.unitySliceTop => resolvedStyle.unitySliceTop;
        SliceType IResolvedStyle.unitySliceType => resolvedStyle.unitySliceType;
        TextAnchor IResolvedStyle.unityTextAlign => resolvedStyle.unityTextAlign;
        TextGeneratorType IResolvedStyle.unityTextGenerator => resolvedStyle.unityTextGenerator;
        Color IResolvedStyle.unityTextOutlineColor => resolvedStyle.unityTextOutlineColor;
        float IResolvedStyle.unityTextOutlineWidth => resolvedStyle.unityTextOutlineWidth;
        TextOverflowPosition IResolvedStyle.unityTextOverflowPosition => resolvedStyle.unityTextOverflowPosition;
        Visibility IResolvedStyle.visibility => resolvedStyle.visibility;
        WhiteSpace IResolvedStyle.whiteSpace => resolvedStyle.whiteSpace;
        float IResolvedStyle.width => resolvedStyle.width;
        float IResolvedStyle.wordSpacing => resolvedStyle.wordSpacing;
#pragma warning disable CS0618
        StyleEnum<ScaleMode> IResolvedStyle.unityBackgroundScaleMode => resolvedStyle.unityBackgroundScaleMode;
#pragma warning restore CS0618
    }
}
