using System.Linq;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// PropertyField's controls for colour, object reference, curve, gradient, layer mask, enum,
    /// character and the vector, rect and bounds property types, and MultiPropertyField, as Unity
    /// 6000.0's DefaultPropertyField draws them (measured: tests/projects/editorobjectfields sp/*,
    /// values, control ids, heights and layout rects and scripted edits of a property). Other
    /// families keep the label in place of these controls and report them not implemented.
    /// </summary>
    public sealed partial class EditorGUI
    {
        /// <summary>The property's control when its type is one of these (true), inside the caller's
        /// BeginProperty; false for the other types.</summary>
        internal static bool DefaultPickerPropertyField(Rect position, SerializedProperty property, GUIContent label)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Color:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.ObjectReference:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.RenderingLayerMask:
                case SerializedPropertyType.Character:
                case SerializedPropertyType.AnimationCurve:
                case SerializedPropertyType.Gradient:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector4:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                    break;
                default:
                    return false;
            }
            // A type whose children show (a Vector4's) is a foldout, as the Editor's
            // HasVisibleChildFields decides; vectors, rects and bounds are inline.
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.RenderingLayerMask:
                    break;
                default:
                    if (property.hasVisibleChildren) return false;
                    break;
            }
            if (!Compat.EditorValueFieldsMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUI.PropertyField of a " + property.propertyType + " property");
                return false;
            }

            switch (property.propertyType)
            {
                case SerializedPropertyType.Color:
                {
                    BeginChangeCheck();
                    bool showAlpha = true, hdr = false;
                    var usage = property.UnboundDeclaringField?.GetCustomAttributes(typeof(ColorUsageAttribute), false).OfType<ColorUsageAttribute>().FirstOrDefault();
                    if (usage != null)
                    {
                        showAlpha = usage.showAlpha;
                        hdr = usage.hdr;
                    }
                    Color newColor = ColorField(position, label, property.colorValue, true, showAlpha, hdr);
                    if (EndChangeCheck()) property.colorValue = newColor;
                    break;
                }
                case SerializedPropertyType.Enum:
                    EnumPopup(position, property, label);
                    break;
                case SerializedPropertyType.ObjectReference:
                    ObjectFieldInternal(position, property, null, label, EditorStyles.objectField);
                    break;
                case SerializedPropertyType.LayerMask:
                    LayerMaskPropertyField(position, property, label);
                    break;
                case SerializedPropertyType.RenderingLayerMask:
                    RenderingLayerMaskField(position, label, property);
                    break;
                case SerializedPropertyType.Character:
                {
                    char[] value = { (char)property.intValue };
                    bool wasChanged = GUI.changed;
                    GUI.changed = false;
                    string newValue = TextField(position, label, new string(value));
                    if (GUI.changed)
                    {
                        if ((newValue.Length == 1 && newValue[0] != '\0') || (newValue.Length == 2 && newValue[1] == '\0'))
                            property.intValue = newValue[0];
                        else
                            GUI.changed = false;
                    }
                    GUI.changed |= wasChanged;
                    break;
                }
                case SerializedPropertyType.AnimationCurve:
                {
                    int id = GUIUtility.GetControlID(s_CurveHash, FocusType.Keyboard, position);
                    DoCurveField(PrefixLabel(position, id, label), id, null, kCurveColor, new Rect(), property);
                    break;
                }
                case SerializedPropertyType.Gradient:
                {
                    // (The Editor's gradient property takes the curve field's hash.)
                    int id = GUIUtility.GetControlID(s_CurveHash, FocusType.Keyboard, position);
                    DoGradientField(PrefixLabel(position, id, label), id, null, property, false, ColorSpace.Gamma);
                    break;
                }
                case SerializedPropertyType.Vector3: VectorPropertyField(position, property, label, s_XYZLabels); break;
                case SerializedPropertyType.Vector4: VectorPropertyField(position, property, label, s_XYZWLabels); break;
                case SerializedPropertyType.Vector2: VectorPropertyField(position, property, label, s_XYLabels); break;
                case SerializedPropertyType.Vector2Int: VectorPropertyField(position, property, label, s_XYLabels); break;
                case SerializedPropertyType.Vector3Int: VectorPropertyField(position, property, label, s_XYZLabels); break;
                case SerializedPropertyType.Rect: RectPropertyField(position, property, label, alignLabels: true); break;
                case SerializedPropertyType.RectInt: RectPropertyField(position, property, label, alignLabels: false); break;
                case SerializedPropertyType.Bounds: BoundsPropertyField(position, property, label, s_CenterLabel, s_ExtentLabel, kMiniLabelW); break;
                case SerializedPropertyType.BoundsInt: BoundsPropertyField(position, property, label, s_PositionLabel, s_SizeLabel, -1); break;
            }
            return true;
        }

        // A vector property: its prefix label, then a column per component.
        static void VectorPropertyField(Rect position, SerializedProperty property, GUIContent label, GUIContent[] subLabels)
        {
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, subLabels.Length);
            position.height = kSingleLineHeight;
            SerializedProperty cur = property.Copy();
            cur.Next(true);
            MultiPropertyFieldInternal(position, subLabels, cur, PropertyVisibility.All);
        }

        // A rect property: X and Y, then W and H on the second row (a Rect's sub-labels with the
        // label style aligned left while they draw).
        static void RectPropertyField(Rect position, SerializedProperty property, GUIContent label, bool alignLabels)
        {
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 2);
            position.height = kSingleLineHeight;
            SerializedProperty cur = property.Copy();
            cur.Next(true);
            var oldAlignment = EditorStyles.label.alignment;
            if (alignLabels) EditorStyles.label.alignment = TextAnchor.MiddleLeft;
            MultiPropertyFieldInternal(position, s_XYLabels, cur, PropertyVisibility.All, null, kMiniLabelW);
            position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            MultiPropertyFieldInternal(position, s_WHLabels, cur, PropertyVisibility.All, null, kMiniLabelW);
            if (alignLabels) EditorStyles.label.alignment = oldAlignment;
        }

        // A bounds property: its label on its own line, then the first and second rows with their
        // row labels.
        static void BoundsPropertyField(Rect position, SerializedProperty property, GUIContent label, GUIContent first, GUIContent second, float prefixLabelWidth)
        {
            bool hasLabel = LabelHasContent(label);
            if (hasLabel)
            {
                int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
                position = MultiFieldPrefixLabel(position, id, label, 3);
                if (EditorGUIUtility.wideMode) position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            }
            position.height = kSingleLineHeight;
            position = DrawBoundsFieldLabelsAndAdjustPositionForValues(position, EditorGUIUtility.wideMode && hasLabel, first, second);
            SerializedProperty cur = property.Copy();
            cur.Next(true);
            cur.Next(true);
            MultiPropertyFieldInternal(position, s_XYZLabels, cur, PropertyVisibility.All, null, prefixLabelWidth);
            cur.Next(true);
            position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            MultiPropertyFieldInternal(position, s_XYZLabels, cur, PropertyVisibility.All, null, prefixLabelWidth);
        }

        public static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator, GUIContent label) =>
            MultiPropertyField(position, subLabels, valuesIterator, label, PropertyVisibility.OnlyVisible);

        public static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator, GUIContent label, PropertyVisibility visibility)
        {
            if (!PickerFieldsMeasured("MultiPropertyField")) return;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, subLabels.Length);
            position.height = kSingleLineHeight;
            MultiPropertyFieldInternal(position, subLabels, valuesIterator, visibility);
        }

        public static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator) =>
            MultiPropertyField(position, subLabels, valuesIterator, PropertyVisibility.OnlyVisible);

        public static void MultiPropertyField(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator, PropertyVisibility visibility)
        {
            if (!PickerFieldsMeasured("MultiPropertyField")) return;
            MultiPropertyFieldInternal(position, subLabels, valuesIterator, visibility);
        }

        /// <summary>A column per sub-label, each drawing the iterator's property (then the next
        /// one, visible or not as asked) with its sub-label.</summary>
        internal static void MultiPropertyFieldInternal(Rect position, GUIContent[] subLabels, SerializedProperty valuesIterator, PropertyVisibility visibility, bool[] disabledMask = null, float prefixLabelWidth = -1)
        {
            int eCount = subLabels.Length;
            float w = (position.width - (eCount - 1) * kSpacingSubLabel) / eCount;
            Rect nr = new Rect(position) { width = w };
            float t = EditorGUIUtility.labelWidth;
            int l = indentLevel;
            indentLevel = 0;
            for (int i = 0; i < subLabels.Length; i++)
            {
                EditorGUIUtility.labelWidth = GetLabelWidth(subLabels[i], prefixLabelWidth);
                if (disabledMask != null) BeginDisabledGroup(disabledMask[i]);
                PropertyField(nr, valuesIterator, subLabels[i]);
                if (disabledMask != null) EndDisabledGroup();
                nr.x += w + kSpacingSubLabel;
                if (visibility == PropertyVisibility.All) valuesIterator.Next(false);
                else valuesIterator.NextVisible(false);
            }
            EditorGUIUtility.labelWidth = t;
            indentLevel = l;
        }
    }
}
