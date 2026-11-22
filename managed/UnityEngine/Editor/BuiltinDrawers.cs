using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// The UI Toolkit GUI of the Editor's drawers for the built-in property attributes, as a Unity
// 6000.0.71f1 PropertyField builds them (tests/projects/drawerdispatch `builtin.*`, `decorator.*`;
// tests/projects/bindingcontrols `usage.*`). Their IMGUI (OnGUI, heights) is the IMGUI
// EditorGUI's, which draws these attributes itself.

namespace UnityEditor
{
    [CustomPropertyDrawer(typeof(RangeAttribute))]
    internal sealed partial class RangeDrawer : PropertyDrawer
    {
        static readonly string s_InvalidTypeMessage = "Use Range with float or int.";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var range = (RangeAttribute)attribute;
            if (property.propertyType == SerializedPropertyType.Float)
            {
                var slider = new Slider(preferredLabel, range.min, range.max);
                slider.AddToClassList(BaseField<float>.alignedFieldUssClassName);
                slider.bindingPath = property.propertyPath;
                slider.showInputField = true;
                return slider;
            }
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                var slider = new SliderInt(preferredLabel, (int)range.min, (int)range.max);
                slider.AddToClassList(BaseField<int>.alignedFieldUssClassName);
                slider.bindingPath = property.propertyPath;
                slider.showInputField = true;
                return slider;
            }
            return new Label(s_InvalidTypeMessage);
        }
    }

    [CustomPropertyDrawer(typeof(MinAttribute))]
    internal sealed partial class MinDrawer : PropertyDrawer
    {
        static readonly string s_InvalidTypeMessage = "Use Min with float, int or Vector.";

        float Min => ((MinAttribute)attribute).min;

        // A field of the property's type whose values below the minimum are raised to it.
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            BindableElement field = null;
            float min = Min;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float when property.type == "float":
                    field = Validated(new FloatField(preferredLabel), v => Mathf.Max(min, v));
                    break;
                case SerializedPropertyType.Float when property.type == "double":
                    field = Validated(new DoubleField(preferredLabel), v => Math.Max(min, v));
                    break;
                case SerializedPropertyType.Integer when property.type == "int":
                    field = Validated(new IntegerField(preferredLabel), v => Mathf.Max((int)min, v));
                    break;
                case SerializedPropertyType.Integer when property.type == "long":
                    field = Validated(new LongField(preferredLabel), v => Math.Max((long)min, v));
                    break;
                case SerializedPropertyType.Integer when property.type == "uint":
                    field = Validated(new UnsignedIntegerField(preferredLabel), v => Math.Max((uint)min, v));
                    break;
                case SerializedPropertyType.Integer when property.type == "ulong":
                    field = Validated(new UnsignedLongField(preferredLabel), v => Math.Max((ulong)min, v));
                    break;
                case SerializedPropertyType.Vector2:
                    field = Validated(new Vector2Field(preferredLabel), v => new Vector2(Mathf.Max(min, v.x), Mathf.Max(min, v.y)));
                    break;
                case SerializedPropertyType.Vector2Int:
                    field = Validated(new Vector2IntField(preferredLabel), v => new Vector2Int(Mathf.Max((int)min, v.x), Mathf.Max((int)min, v.y)));
                    break;
                case SerializedPropertyType.Vector3:
                    field = Validated(new Vector3Field(preferredLabel), v => new Vector3(Mathf.Max(min, v.x), Mathf.Max(min, v.y), Mathf.Max(min, v.z)));
                    break;
                case SerializedPropertyType.Vector3Int:
                    field = Validated(new Vector3IntField(preferredLabel),
                        v => new Vector3Int(Mathf.Max((int)min, v.x), Mathf.Max((int)min, v.y), Mathf.Max((int)min, v.z)));
                    break;
                case SerializedPropertyType.Vector4:
                    field = Validated(new Vector4Field(preferredLabel),
                        v => new Vector4(Mathf.Max(min, v.x), Mathf.Max(min, v.y), Mathf.Max(min, v.z), Mathf.Max(min, v.w)));
                    break;
            }
            if (field == null) return new Label(s_InvalidTypeMessage);
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            field.bindingPath = property.propertyPath;
            return field;
        }

        static BaseField<TValue> Validated<TValue>(BaseField<TValue> field, Func<TValue, TValue> validate)
        {
            field.onValidateValue += validate;
            return field;
        }
    }

    [CustomPropertyDrawer(typeof(DelayedAttribute))]
    internal sealed partial class DelayedDrawer : PropertyDrawer
    {
        static readonly string s_InvalidTypeMessage = "Use Delayed with float, int, or string.";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            BindableElement field = null;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float when property.type == "float":
                    field = new FloatField(preferredLabel) { isDelayed = true };
                    break;
                case SerializedPropertyType.Float when property.type == "double":
                    field = new DoubleField(preferredLabel) { isDelayed = true };
                    break;
                case SerializedPropertyType.Integer when property.type == "int":
                    field = new IntegerField(preferredLabel) { isDelayed = true };
                    break;
                case SerializedPropertyType.Integer when property.type == "long":
                    field = new LongField(preferredLabel) { isDelayed = true };
                    break;
                case SerializedPropertyType.String:
                    field = new TextField(preferredLabel) { isDelayed = true };
                    break;
            }
            if (field == null) return new Label(s_InvalidTypeMessage);
            field.bindingPath = property.propertyPath;
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            return field;
        }
    }

    [CustomPropertyDrawer(typeof(MultilineAttribute))]
    internal sealed partial class MultilineDrawer : PropertyDrawer
    {
        static readonly string s_InvalidTypeMessage = "Use Multiline with string.";
        const int kLineHeight = 13;
        // EditorGUI's single line height.
        const float kSingleLineHeight = 18f;

        // A text field one line high plus kLineHeight for each further line.
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String) return new Label(s_InvalidTypeMessage);
            int lines = ((MultilineAttribute)attribute).lines;
            var field = new TextField(preferredLabel) { multiline = true, bindingPath = property.propertyPath };
            field.style.height = kSingleLineHeight + (lines - 1) * kLineHeight;
            field.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            return field;
        }
    }

    [CustomPropertyDrawer(typeof(TextAreaAttribute))]
    internal sealed partial class TextAreaDrawer : PropertyDrawer
    {
        static readonly string s_InvalidTypeMessage = "Use TextAreaDrawer with string.";
        const int kLineHeight = 15;
        // The height of a one-line text area.
        const float kOneLineHeight = 36f;

        // A scrolling text area kOneLineHeight high for one line plus kLineHeight for each further
        // line, between the attribute's minimum and maximum lines.
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String) return new Label(s_InvalidTypeMessage);
            var area = (TextAreaAttribute)attribute;
            float min = kOneLineHeight + (area.minLines - 1) * kLineHeight;
            float max = Mathf.Max(min, kOneLineHeight + (area.maxLines - 1) * kLineHeight);
            var field = new TextField { label = preferredLabel, multiline = true };
            field.style.flexDirection = FlexDirection.Column;
            field.style.whiteSpace = WhiteSpace.Normal;
            field.style.minHeight = min;
            field.style.maxHeight = max;
            field.verticalScrollerVisibility = ScrollerVisibility.Auto;
            field.bindingPath = property.propertyPath;
            return field;
        }
    }

    [CustomPropertyDrawer(typeof(ColorUsageAttribute))]
    internal sealed partial class ColorUsageDrawer : PropertyDrawer
    {
        static readonly string s_InvalidTypeMessage = "Use ColorUsageDrawer with color.";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Color) return new Label(s_InvalidTypeMessage);
            var usage = (ColorUsageAttribute)attribute;
            var field = new ColorField(preferredLabel) { showAlpha = usage.showAlpha, hdr = usage.hdr, bindingPath = property.propertyPath };
            field.AddToClassList(BaseField<Color>.alignedFieldUssClassName);
            return field;
        }
    }

    [CustomPropertyDrawer(typeof(GradientUsageAttribute))]
    internal sealed partial class GradientUsageDrawer : PropertyDrawer
    {
        static readonly string s_InvalidTypeMessage = "Use GradientUsageDrawer with gradient.";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Gradient) return new Label(s_InvalidTypeMessage);
            var usage = (GradientUsageAttribute)attribute;
            var field = new GradientField(preferredLabel) { hdr = usage.hdr, colorSpace = usage.colorSpace, bindingPath = property.propertyPath };
            field.AddToClassList(BaseField<Gradient>.alignedFieldUssClassName);
            return field;
        }
    }

    [CustomPropertyDrawer(typeof(SpaceAttribute))]
    internal sealed partial class SpaceDrawer : DecoratorDrawer
    {
        public const string spaceDrawerClassName = "unity-space-drawer";

        public override float GetHeight() => ((SpaceAttribute)attribute).height;

        public override VisualElement CreatePropertyGUI()
        {
            var space = new VisualElement();
            space.AddToClassList(spaceDrawerClassName);
            space.style.height = GetHeight();
            return space;
        }
    }

    [CustomPropertyDrawer(typeof(HeaderAttribute))]
    internal sealed partial class HeaderDrawer : DecoratorDrawer
    {
        public const string headerLabelClassName = "unity-header-drawer__label";

        public override VisualElement CreatePropertyGUI()
        {
            var label = new Label(((HeaderAttribute)attribute).header);
            label.AddToClassList(headerLabelClassName);
            return label;
        }
    }
}
