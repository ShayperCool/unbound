using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// EditorGUI's vector, rect and bounds fields and MultiFloatField/MultiIntField as Unity
    /// 6000.0's C# lays them out: columns of number fields with short sub-labels that drag their
    /// value, the prefix label beside the columns in wide mode and above them otherwise, a rect's
    /// and a bound's second row below the first (measured: tests/projects/editormultifields, values,
    /// control ids, layout rects and scripted clicks inside each sub-field). Other families report
    /// these fields not implemented and return the value they are given.
    /// </summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_FoldoutHash = "Foldout".GetHashCode();

        internal const float kSingleLineHeight = 18f;
        internal const float kStructHeaderLineHeight = 18f;
        internal const float kPrefixPaddingRight = 2f;
        internal const float kSpacingSubLabel = 4f;
        internal const float kIndentPerLevel = 15f;
        // The theme's --theme-multifield-vertical-spacing: 2 points between a multi-field's rows
        // (editormultifields layout/repaint/rects: a labelled Vector2Field is 18 + 2 + 18 high),
        // shared with the property heights that reserve their space.
        const float kMultiFieldVerticalSpacing = PropertyHeights.Spacing;

        static readonly GUIContent s_LargerChar = new GUIContent("W");
        static readonly float[] s_Vector2Floats = { 0, 0 };
        static readonly int[] s_Vector2Ints = { 0, 0 };
        static readonly GUIContent[] s_XYLabels = { new GUIContent("X"), new GUIContent("Y") };
        static readonly float[] s_Vector3Floats = { 0, 0, 0 };
        static readonly int[] s_Vector3Ints = { 0, 0, 0 };
        static readonly GUIContent[] s_XYZLabels = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z") };
        static readonly float[] s_Vector4Floats = { 0, 0, 0, 0 };
        static readonly GUIContent[] s_XYZWLabels = { new GUIContent("X"), new GUIContent("Y"), new GUIContent("Z"), new GUIContent("W") };
        static readonly GUIContent[] s_WHLabels = { new GUIContent("W"), new GUIContent("H") };
        static readonly GUIContent s_CenterLabel = new GUIContent("Center");
        static readonly GUIContent s_ExtentLabel = new GUIContent("Extent");
        static readonly GUIContent s_PositionLabel = new GUIContent("Position");
        static readonly GUIContent s_SizeLabel = new GUIContent("Size");

        internal static float kMiniLabelW => CalcPrefixLabelWidth(s_LargerChar);

        internal static float CalcPrefixLabelWidth(GUIContent label, GUIStyle style = null)
        {
            if (style == null) style = EditorStyles.label;
            return style.CalcSize(label).x;
        }

        internal static Rect MultiFieldPrefixLabel(Rect totalPosition, int id, GUIContent label, int columns) =>
            MultiFieldPrefixLabel(totalPosition, id, label, columns, 0, false);

        internal static Rect MultiFieldPrefixLabel(Rect totalPosition, int id, GUIContent label, int columns, float labelWidthIndent, bool setWideMode)
        {
            if (!LabelHasContent(label)) return IndentedRect(totalPosition);

            if (EditorGUIUtility.wideMode || setWideMode)
            {
                Rect labelPosition = new Rect(totalPosition.x + indent, totalPosition.y, EditorGUIUtility.labelWidth - indent - labelWidthIndent, kSingleLineHeight);
                Rect fieldPosition = totalPosition;
                fieldPosition.xMin += EditorGUIUtility.labelWidth + kPrefixPaddingRight;
                // Two columns take the widths of three, so that columns line up.
                if (columns == 2)
                {
                    float columnWidth = (fieldPosition.width - (3 - 1) * kSpacingSubLabel) / 3f;
                    fieldPosition.xMax -= columnWidth + kSpacingSubLabel;
                }
                HandlePrefixLabel(totalPosition, labelPosition, label, id);
                return fieldPosition;
            }
            else
            {
                Rect labelPosition = new Rect(totalPosition.x + indent, totalPosition.y, totalPosition.width - indent, kSingleLineHeight);
                Rect fieldPosition = totalPosition;
                fieldPosition.xMin += indent + kIndentPerLevel;
                fieldPosition.yMin += kSingleLineHeight + kMultiFieldVerticalSpacing;
                HandlePrefixLabel(totalPosition, labelPosition, label, id);
                return fieldPosition;
            }
        }

        internal static float GetLabelWidth(GUIContent Label, float prefixLabelWidth = -1)
        {
            float labelWidth = CalcPrefixLabelWidth(Label);
            if (labelWidth > kMiniLabelW) return prefixLabelWidth > 0 ? prefixLabelWidth : labelWidth;
            return prefixLabelWidth > 0 ? prefixLabelWidth : kMiniLabelW;
        }

        internal static void MultiFloatFieldInternal(Rect position, GUIContent[] subLabels, float[] values, float prefixLabelWidth = -1)
        {
            int eCount = values.Length;
            float w = (position.width - (eCount - 1) * kSpacingSubLabel) / eCount;
            Rect nr = new Rect(position) { width = w };
            float t = EditorGUIUtility.labelWidth;
            int l = indentLevel;
            indentLevel = 0;
            for (int i = 0; i < values.Length; i++)
            {
                EditorGUIUtility.labelWidth = GetLabelWidth(subLabels[i], prefixLabelWidth);
                values[i] = FloatField(nr, subLabels[i], values[i]);
                nr.x += w + kSpacingSubLabel;
            }
            EditorGUIUtility.labelWidth = t;
            indentLevel = l;
        }

        internal static void MultiIntFieldInternal(Rect position, GUIContent[] subLabels, int[] values)
        {
            int eCount = values.Length;
            float w = (position.width - (eCount - 1) * kSpacingSubLabel) / eCount;
            Rect nr = new Rect(position) { width = w };
            float t = EditorGUIUtility.labelWidth;
            int l = indentLevel;
            indentLevel = 0;
            for (int i = 0; i < values.Length; i++)
            {
                EditorGUIUtility.labelWidth = GetLabelWidth(subLabels[i]);
                values[i] = IntField(nr, subLabels[i], values[i]);
                nr.x += w + kSpacingSubLabel;
            }
            EditorGUIUtility.labelWidth = t;
            indentLevel = l;
        }

        public static void MultiFloatField(Rect position, GUIContent label, GUIContent[] subLabels, float[] values)
        {
            if (!FieldsMeasured(nameof(MultiFloatField), out _)) return;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, subLabels.Length);
            position.height = kSingleLineHeight;
            MultiFloatFieldInternal(position, subLabels, values);
        }

        public static void MultiFloatField(Rect position, GUIContent[] subLabels, float[] values)
        {
            if (!FieldsMeasured(nameof(MultiFloatField), out _)) return;
            MultiFloatFieldInternal(position, subLabels, values);
        }

        public static void MultiIntField(Rect position, GUIContent[] subLabels, int[] values)
        {
            if (!FieldsMeasured(nameof(MultiIntField), out _)) return;
            MultiIntFieldInternal(position, subLabels, values);
        }

        public static Vector2 Vector2Field(Rect position, string label, Vector2 value) => Vector2Field(position, EditorGUIUtility.TempContent(label), value);

        public static Vector2 Vector2Field(Rect position, GUIContent label, Vector2 value) => Vector2Field(position, label, value, false);

        internal static Vector2 Vector2Field(Rect position, GUIContent label, Vector2 value, bool setWideMode)
        {
            if (!FieldsMeasured(nameof(Vector2Field), out _)) return value;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 2, 0, setWideMode);
            position.height = kSingleLineHeight;
            return Vector2Field(position, value);
        }

        static Vector2 Vector2Field(Rect position, Vector2 value)
        {
            s_Vector2Floats[0] = value.x;
            s_Vector2Floats[1] = value.y;
            position.height = kSingleLineHeight;
            BeginChangeCheck();
            MultiFloatFieldInternal(position, s_XYLabels, s_Vector2Floats);
            if (EndChangeCheck())
            {
                value.x = s_Vector2Floats[0];
                value.y = s_Vector2Floats[1];
            }
            return value;
        }

        public static Vector3 Vector3Field(Rect position, string label, Vector3 value) => Vector3Field(position, EditorGUIUtility.TempContent(label), value);

        public static Vector3 Vector3Field(Rect position, GUIContent label, Vector3 value)
        {
            if (!FieldsMeasured(nameof(Vector3Field), out _)) return value;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 3);
            position.height = kSingleLineHeight;
            return Vector3Field(position, value);
        }

        static Vector3 Vector3Field(Rect position, Vector3 value)
        {
            s_Vector3Floats[0] = value.x;
            s_Vector3Floats[1] = value.y;
            s_Vector3Floats[2] = value.z;
            position.height = kSingleLineHeight;
            BeginChangeCheck();
            MultiFloatFieldInternal(position, s_XYZLabels, s_Vector3Floats);
            if (EndChangeCheck())
            {
                value.x = s_Vector3Floats[0];
                value.y = s_Vector3Floats[1];
                value.z = s_Vector3Floats[2];
            }
            return value;
        }

        public static Vector4 Vector4Field(Rect position, string label, Vector4 value) => Vector4Field(position, EditorGUIUtility.TempContent(label), value);

        public static Vector4 Vector4Field(Rect position, GUIContent label, Vector4 value)
        {
            if (!FieldsMeasured(nameof(Vector4Field), out _)) return value;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 4);
            position.height = kSingleLineHeight;
            return Vector4FieldNoIndent(position, value);
        }

        static Vector4 Vector4FieldNoIndent(Rect position, Vector4 value)
        {
            s_Vector4Floats[0] = value.x;
            s_Vector4Floats[1] = value.y;
            s_Vector4Floats[2] = value.z;
            s_Vector4Floats[3] = value.w;
            position.height = kSingleLineHeight;
            BeginChangeCheck();
            MultiFloatFieldInternal(position, s_XYZWLabels, s_Vector4Floats);
            if (EndChangeCheck())
            {
                value.x = s_Vector4Floats[0];
                value.y = s_Vector4Floats[1];
                value.z = s_Vector4Floats[2];
                value.w = s_Vector4Floats[3];
            }
            return value;
        }

        public static Vector2Int Vector2IntField(Rect position, string label, Vector2Int value) => Vector2IntField(position, EditorGUIUtility.TempContent(label), value);

        public static Vector2Int Vector2IntField(Rect position, GUIContent label, Vector2Int value)
        {
            if (!FieldsMeasured(nameof(Vector2IntField), out _)) return value;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 2);
            position.height = kSingleLineHeight;
            return Vector2IntField(position, value);
        }

        static Vector2Int Vector2IntField(Rect position, Vector2Int value)
        {
            s_Vector2Ints[0] = value.x;
            s_Vector2Ints[1] = value.y;
            position.height = kSingleLineHeight;
            BeginChangeCheck();
            MultiIntFieldInternal(position, s_XYLabels, s_Vector2Ints);
            if (EndChangeCheck())
            {
                value.x = s_Vector2Ints[0];
                value.y = s_Vector2Ints[1];
            }
            return value;
        }

        public static Vector3Int Vector3IntField(Rect position, string label, Vector3Int value) => Vector3IntField(position, EditorGUIUtility.TempContent(label), value);

        public static Vector3Int Vector3IntField(Rect position, GUIContent label, Vector3Int value)
        {
            if (!FieldsMeasured(nameof(Vector3IntField), out _)) return value;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 3);
            position.height = kSingleLineHeight;
            return Vector3IntField(position, value);
        }

        static Vector3Int Vector3IntField(Rect position, Vector3Int value)
        {
            s_Vector3Ints[0] = value.x;
            s_Vector3Ints[1] = value.y;
            s_Vector3Ints[2] = value.z;
            position.height = kSingleLineHeight;
            BeginChangeCheck();
            MultiIntFieldInternal(position, s_XYZLabels, s_Vector3Ints);
            if (EndChangeCheck())
            {
                value.x = s_Vector3Ints[0];
                value.y = s_Vector3Ints[1];
                value.z = s_Vector3Ints[2];
            }
            return value;
        }

        public static Rect RectField(Rect position, Rect value)
        {
            if (!FieldsMeasured(nameof(RectField), out _)) return value;
            return RectFieldNoIndent(IndentedRect(position), value);
        }

        static Rect RectFieldNoIndent(Rect position, Rect value)
        {
            position.height = kSingleLineHeight;
            s_Vector2Floats[0] = value.x;
            s_Vector2Floats[1] = value.y;
            BeginChangeCheck();
            var oldAlignment = EditorStyles.label.alignment;
            EditorStyles.label.alignment = TextAnchor.MiddleLeft;
            MultiFloatFieldInternal(position, s_XYLabels, s_Vector2Floats, kMiniLabelW);
            if (EndChangeCheck())
            {
                value.x = s_Vector2Floats[0];
                value.y = s_Vector2Floats[1];
            }
            position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            s_Vector2Floats[0] = value.width;
            s_Vector2Floats[1] = value.height;
            BeginChangeCheck();
            MultiFloatFieldInternal(position, s_WHLabels, s_Vector2Floats, kMiniLabelW);
            if (EndChangeCheck())
            {
                value.width = s_Vector2Floats[0];
                value.height = s_Vector2Floats[1];
            }
            EditorStyles.label.alignment = oldAlignment;
            return value;
        }

        public static Rect RectField(Rect position, string label, Rect value) => RectField(position, EditorGUIUtility.TempContent(label), value);

        public static Rect RectField(Rect position, GUIContent label, Rect value)
        {
            if (!FieldsMeasured(nameof(RectField), out _)) return value;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 2);
            return RectFieldNoIndent(position, value);
        }

        public static RectInt RectIntField(Rect position, RectInt value)
        {
            if (!FieldsMeasured(nameof(RectIntField), out _)) return value;
            position.height = kSingleLineHeight;
            s_Vector2Ints[0] = value.x;
            s_Vector2Ints[1] = value.y;
            BeginChangeCheck();
            MultiIntFieldInternal(position, s_XYLabels, s_Vector2Ints);
            if (EndChangeCheck())
            {
                value.x = s_Vector2Ints[0];
                value.y = s_Vector2Ints[1];
            }
            position.y += kSingleLineHeight + 2;
            s_Vector2Ints[0] = value.width;
            s_Vector2Ints[1] = value.height;
            BeginChangeCheck();
            MultiIntFieldInternal(position, s_WHLabels, s_Vector2Ints);
            if (EndChangeCheck())
            {
                value.width = s_Vector2Ints[0];
                value.height = s_Vector2Ints[1];
            }
            return value;
        }

        public static RectInt RectIntField(Rect position, string label, RectInt value) => RectIntField(position, EditorGUIUtility.TempContent(label), value);

        public static RectInt RectIntField(Rect position, GUIContent label, RectInt value)
        {
            if (!FieldsMeasured(nameof(RectIntField), out _)) return value;
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 2);
            position.height = kSingleLineHeight;
            return RectIntField(position, value);
        }

        static Rect DrawBoundsFieldLabelsAndAdjustPositionForValues(Rect position, bool drawOutside, GUIContent firstContent, GUIContent secondContent)
        {
            const float kBoundsTextWidth = 53;
            if (drawOutside) position.xMin -= kBoundsTextWidth;
            GUI.Label(position, firstContent, EditorStyles.label);
            position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            GUI.Label(position, secondContent, EditorStyles.label);
            position.y -= kSingleLineHeight + kMultiFieldVerticalSpacing;
            position.xMin += kBoundsTextWidth;
            return position;
        }

        public static Bounds BoundsField(Rect position, Bounds value)
        {
            if (!FieldsMeasured(nameof(BoundsField), out _)) return value;
            return BoundsFieldNoIndent(IndentedRect(position), value, false);
        }

        public static Bounds BoundsField(Rect position, string label, Bounds value) => BoundsField(position, EditorGUIUtility.TempContent(label), value);

        public static Bounds BoundsField(Rect position, GUIContent label, Bounds value)
        {
            if (!FieldsMeasured(nameof(BoundsField), out _)) return value;
            if (!LabelHasContent(label)) return BoundsFieldNoIndent(IndentedRect(position), value, false);
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 3);
            if (EditorGUIUtility.wideMode) position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            return BoundsFieldNoIndent(position, value, true);
        }

        static Bounds BoundsFieldNoIndent(Rect position, Bounds value, bool isBelowLabel)
        {
            position.height = kSingleLineHeight;
            position = DrawBoundsFieldLabelsAndAdjustPositionForValues(position, EditorGUIUtility.wideMode && isBelowLabel, s_CenterLabel, s_ExtentLabel);
            value.center = Vector3Field(position, value.center);
            position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            value.extents = Vector3Field(position, value.extents);
            return value;
        }

        public static BoundsInt BoundsIntField(Rect position, BoundsInt value)
        {
            if (!FieldsMeasured(nameof(BoundsIntField), out _)) return value;
            return BoundsIntFieldNoIndent(IndentedRect(position), value, false);
        }

        public static BoundsInt BoundsIntField(Rect position, string label, BoundsInt value) => BoundsIntField(position, EditorGUIUtility.TempContent(label), value);

        public static BoundsInt BoundsIntField(Rect position, GUIContent label, BoundsInt value)
        {
            if (!FieldsMeasured(nameof(BoundsIntField), out _)) return value;
            if (!LabelHasContent(label)) return BoundsIntFieldNoIndent(IndentedRect(position), value, false);
            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            position = MultiFieldPrefixLabel(position, id, label, 3);
            if (EditorGUIUtility.wideMode) position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            return BoundsIntFieldNoIndent(position, value, true);
        }

        static BoundsInt BoundsIntFieldNoIndent(Rect position, BoundsInt value, bool isBelowLabel)
        {
            position.height = kSingleLineHeight;
            position = DrawBoundsFieldLabelsAndAdjustPositionForValues(position, EditorGUIUtility.wideMode && isBelowLabel, s_PositionLabel, s_SizeLabel);
            value.position = Vector3IntField(position, value.position);
            position.y += kSingleLineHeight + kMultiFieldVerticalSpacing;
            value.size = Vector3IntField(position, value.size);
            return value;
        }
    }

    public sealed partial class EditorGUILayout
    {
        static Rect MultiRect(bool hasLabel, SerializedPropertyType type, GUIContent label, GUILayoutOption[] options) =>
            GetControlRect(hasLabel, EditorGUI.GetPropertyHeight(type, label), EditorGUI.NumberStyle, options);

        public static Vector2 Vector2Field(string label, Vector2 value, params GUILayoutOption[] options) => Vector2Field(EditorGUIUtility.TempContent(label), value, options);
        public static Vector2 Vector2Field(GUIContent label, Vector2 value, params GUILayoutOption[] options) =>
            EditorGUI.Vector2Field(MultiRect(true, SerializedPropertyType.Vector2, label, options), label, value);

        public static Vector3 Vector3Field(string label, Vector3 value, params GUILayoutOption[] options) => Vector3Field(EditorGUIUtility.TempContent(label), value, options);
        public static Vector3 Vector3Field(GUIContent label, Vector3 value, params GUILayoutOption[] options) =>
            EditorGUI.Vector3Field(MultiRect(true, SerializedPropertyType.Vector3, label, options), label, value);

        public static Vector4 Vector4Field(string label, Vector4 value, params GUILayoutOption[] options) => Vector4Field(EditorGUIUtility.TempContent(label), value, options);
        public static Vector4 Vector4Field(GUIContent label, Vector4 value, params GUILayoutOption[] options) =>
            EditorGUI.Vector4Field(MultiRect(true, SerializedPropertyType.Vector4, label, options), label, value);

        public static Vector2Int Vector2IntField(string label, Vector2Int value, params GUILayoutOption[] options) => Vector2IntField(EditorGUIUtility.TempContent(label), value, options);
        public static Vector2Int Vector2IntField(GUIContent label, Vector2Int value, params GUILayoutOption[] options) =>
            EditorGUI.Vector2IntField(MultiRect(true, SerializedPropertyType.Vector2Int, label, options), label, value);

        public static Vector3Int Vector3IntField(string label, Vector3Int value, params GUILayoutOption[] options) => Vector3IntField(EditorGUIUtility.TempContent(label), value, options);
        public static Vector3Int Vector3IntField(GUIContent label, Vector3Int value, params GUILayoutOption[] options) =>
            EditorGUI.Vector3IntField(MultiRect(true, SerializedPropertyType.Vector3Int, label, options), label, value);

        public static Rect RectField(Rect value, params GUILayoutOption[] options) =>
            EditorGUI.RectField(MultiRect(false, SerializedPropertyType.Rect, GUIContent.none, options), value);
        public static Rect RectField(string label, Rect value, params GUILayoutOption[] options) => RectField(EditorGUIUtility.TempContent(label), value, options);
        public static Rect RectField(GUIContent label, Rect value, params GUILayoutOption[] options) =>
            EditorGUI.RectField(MultiRect(EditorGUI.LabelHasContent(label), SerializedPropertyType.Rect, label, options), label, value);

        public static RectInt RectIntField(RectInt value, params GUILayoutOption[] options) =>
            EditorGUI.RectIntField(MultiRect(false, SerializedPropertyType.RectInt, GUIContent.none, options), value);
        public static RectInt RectIntField(string label, RectInt value, params GUILayoutOption[] options) => RectIntField(EditorGUIUtility.TempContent(label), value, options);
        public static RectInt RectIntField(GUIContent label, RectInt value, params GUILayoutOption[] options) =>
            EditorGUI.RectIntField(MultiRect(EditorGUI.LabelHasContent(label), SerializedPropertyType.RectInt, label, options), label, value);

        public static Bounds BoundsField(Bounds value, params GUILayoutOption[] options) =>
            EditorGUI.BoundsField(MultiRect(false, SerializedPropertyType.Bounds, GUIContent.none, options), value);
        public static Bounds BoundsField(string label, Bounds value, params GUILayoutOption[] options) => BoundsField(EditorGUIUtility.TempContent(label), value, options);
        public static Bounds BoundsField(GUIContent label, Bounds value, params GUILayoutOption[] options) =>
            EditorGUI.BoundsField(MultiRect(EditorGUI.LabelHasContent(label), SerializedPropertyType.Bounds, label, options), label, value);

        public static BoundsInt BoundsIntField(BoundsInt value, params GUILayoutOption[] options) =>
            EditorGUI.BoundsIntField(MultiRect(false, SerializedPropertyType.BoundsInt, GUIContent.none, options), value);
        public static BoundsInt BoundsIntField(string label, BoundsInt value, params GUILayoutOption[] options) => BoundsIntField(EditorGUIUtility.TempContent(label), value, options);
        public static BoundsInt BoundsIntField(GUIContent label, BoundsInt value, params GUILayoutOption[] options) =>
            EditorGUI.BoundsIntField(MultiRect(EditorGUI.LabelHasContent(label), SerializedPropertyType.BoundsInt, label, options), label, value);
    }
}
