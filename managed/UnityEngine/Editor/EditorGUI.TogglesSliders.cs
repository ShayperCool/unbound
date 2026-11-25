using UnityEngine;

namespace UnityEditor
{
    // Unity's toggles, property sliders and drop-shadow labels (UnityCsReference 6000.0,
    // EditorGUI.cs, EditorGUIInternal.cs and EditorGUILayout.cs); measured:
    // tests/projects/editortogglefields.
    public sealed partial class EditorGUI
    {
        static readonly int s_ToggleHash = "s_ToggleHash".GetHashCode();

        // The forms with the Editor skin's toggle style: only this release family's styles are
        // measured; another family's report themselves and keep the value given, as Popup's do.
        public static bool Toggle(Rect position, bool value)
        {
            if (Unmeasured("UnityEditor.EditorGUI.Toggle")) return value;
            int id = GUIUtility.GetControlID(s_ToggleHash, FocusType.Keyboard, position);
            return EditorGUIInternal.DoToggleForward(IndentedRect(position), id, value, GUIContent.none, EditorStyles.toggle);
        }

        public static bool Toggle(Rect position, string label, bool value) => Toggle(position, EditorGUIUtility.TempContent(label), value);

        public static bool Toggle(Rect position, bool value, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_ToggleHash, FocusType.Keyboard, position);
            return EditorGUIInternal.DoToggleForward(position, id, value, GUIContent.none, style);
        }

        public static bool Toggle(Rect position, string label, bool value, GUIStyle style) => Toggle(position, EditorGUIUtility.TempContent(label), value, style);

        public static bool Toggle(Rect position, GUIContent label, bool value)
        {
            if (Unmeasured("UnityEditor.EditorGUI.Toggle")) return value;
            int id = GUIUtility.GetControlID(s_ToggleHash, FocusType.Keyboard, position);
            return EditorGUIInternal.DoToggleForward(PrefixLabel(position, id, label), id, value, GUIContent.none, EditorStyles.toggle, false);
        }

        public static bool Toggle(Rect position, GUIContent label, bool value, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_ToggleHash, FocusType.Keyboard, position);
            return EditorGUIInternal.DoToggleForward(PrefixLabel(position, id, label), id, value, GUIContent.none, style);
        }

        internal static float Slider(Rect position, GUIContent label, float value, float leftValue, float rightValue, GUIStyle textfieldStyle) =>
            PowerSlider(position, label, value, leftValue, rightValue, textfieldStyle, 1.0f);

        internal static float Slider(Rect position, GUIContent label, float value, float sliderMin, float sliderMax, float textFieldMin, float textFieldMax) =>
            Slider(position, label, value, sliderMin, sliderMax, textFieldMin, textFieldMax,
                EditorStyles.numberField, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, null,
                GUI.skin.horizontalSliderThumbExtent);

        internal static float Slider(Rect position, GUIContent label, float value, float sliderMin, float sliderMax, float textFieldMin, float textFieldMax
            , GUIStyle textfieldStyle, GUIStyle sliderStyle, GUIStyle thumbStyle, Texture2D sliderBackground, GUIStyle thumbStyleExtent)
        {
            var id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            var controlRect = PrefixLabel(position, id, label);
            var dragZone = LabelHasContent(label) ? DragZoneRect(position) : new Rect(); // Ensure dragzone is empty when we have no label
            return DoSlider(controlRect, dragZone, id, value, sliderMin, sliderMax, kFloatFieldFormatString, textFieldMin, textFieldMax, 1f, 1f,
                textfieldStyle, sliderStyle, thumbStyle, sliderBackground, thumbStyleExtent);
        }

        internal static float PowerSlider(Rect position, GUIContent label, float sliderValue, float leftValue, float rightValue, GUIStyle textfieldStyle, float power)
        {
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            Rect controlRect = PrefixLabel(position, id, label);
            Rect dragZone = LabelHasContent(label) ? DragZoneRect(position) : new Rect(); // Ensure dragzone is empty when we have no label
            return DoSlider(controlRect, dragZone, id, sliderValue, leftValue, rightValue, kFloatFieldFormatString, textfieldStyle, power);
        }

        public static void Slider(Rect position, SerializedProperty property, float leftValue, float rightValue) =>
            Slider(position, property, leftValue, rightValue, property.displayName);

        public static void Slider(Rect position, SerializedProperty property, float leftValue, float rightValue, string label) =>
            Slider(position, property, leftValue, rightValue, EditorGUIUtility.TempContent(label));

        public static void Slider(Rect position, SerializedProperty property, float leftValue, float rightValue, GUIContent label)
        {
            label = BeginProperty(position, label, property);

            BeginChangeCheck();

            float newValue = Slider(position, label, property.floatValue, leftValue, rightValue);
            if (EndChangeCheck())
            {
                property.floatValue = newValue;
            }

            EndProperty();
        }

        internal static void Slider(Rect position, SerializedProperty property, float sliderLeftValue, float sliderRightValue, float textLeftValue, float textRightValue, GUIContent label)
        {
            label = BeginProperty(position, label, property);

            BeginChangeCheck();

            float newValue = Slider(position, label, property.floatValue, sliderLeftValue, sliderRightValue, textLeftValue, textRightValue);
            if (EndChangeCheck())
            {
                property.floatValue = newValue;
            }

            EndProperty();
        }

        internal static int IntSlider(Rect position, int value, int leftValue, int rightValue, float power = -1, float logbase = 1,
            GUIStyle textfieldStyle = null, GUIStyle sliderStyle = null, GUIStyle thumbStyle = null, Texture2D sliderBackground = null, GUIStyle thumbStyleExtent = null)
        {
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            return Mathf.RoundToInt(DoSlider(IndentedRect(position), DragZoneRect(position), id, value, leftValue, rightValue, kIntFieldFormatString, power, logbase,
                textfieldStyle ?? EditorStyles.numberField,
                sliderStyle ?? GUI.skin.horizontalSlider,
                thumbStyle ?? GUI.skin.horizontalSliderThumb,
                sliderBackground,
                thumbStyleExtent ?? GUI.skin.horizontalSliderThumbExtent));
        }

        public static void IntSlider(Rect position, SerializedProperty property, int leftValue, int rightValue) =>
            IntSlider(position, property, leftValue, rightValue, property.displayName);

        public static void IntSlider(Rect position, SerializedProperty property, int leftValue, int rightValue, string label) =>
            IntSlider(position, property, leftValue, rightValue, EditorGUIUtility.TempContent(label));

        public static void IntSlider(Rect position, SerializedProperty property, int leftValue, int rightValue, GUIContent label)
        {
            label = BeginProperty(position, label, property);

            BeginChangeCheck();

            int newValue = IntSlider(position, label, property.intValue, leftValue, rightValue);
            if (EndChangeCheck())
            {
                property.intValue = newValue;
            }

            EndProperty();
        }

        // (Outside OnGUI the Editor throws before it looks the style up by name: no "Unable to find
        // style" warning, tests/projects/editortogglefields outside/* and logs.)
        public static void DropShadowLabel(Rect position, string text)
        {
            RequireGUI();
            DoDropShadowLabel(position, EditorGUIUtility.TempContent(text), "PreOverlayLabel", .6f);
        }

        public static void DropShadowLabel(Rect position, GUIContent content)
        {
            RequireGUI();
            DoDropShadowLabel(position, content, "PreOverlayLabel", .6f);
        }

        public static void DropShadowLabel(Rect position, string text, GUIStyle style) =>
            DoDropShadowLabel(position, EditorGUIUtility.TempContent(text), style, .6f);

        public static void DropShadowLabel(Rect position, GUIContent content, GUIStyle style) =>
            DoDropShadowLabel(position, content, style, .6f);

        internal static void DoDropShadowLabel(Rect position, GUIContent content, GUIStyle style, float shadowOpa)
        {
            // (Outside OnGUI there is no event: Mono's NullReferenceException, as the Editor's.)
            RequireGUI();
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            DrawLabelShadow(position, content, style, shadowOpa);
            style.Draw(position, content, false, false, false, false);
        }

        internal static void DrawLabelShadow(Rect position, GUIContent content, GUIStyle style, float shadowOpa)
        {
            Color temp = GUI.color, temp2 = GUI.contentColor, temp3 = GUI.backgroundColor;

            // Draw only background
            GUI.contentColor = new Color(0, 0, 0, 0);
            style.Draw(position, content, false, false, false, false);

            // Blur foreground
            position.y += 1;
            GUI.backgroundColor = new Color(0, 0, 0, 0);
            GUI.contentColor = temp2;
            Draw4(position, content, 1, GUI.color.a * shadowOpa, style);
            Draw4(position, content, 2, GUI.color.a * shadowOpa * .42f, style);

            // Draw final foreground
            GUI.color = temp;
            GUI.backgroundColor = temp3;
        }

        static void Draw4(Rect position, GUIContent content, float offset, float alpha, GUIStyle style)
        {
            GUI.color = new Color(0, 0, 0, alpha);
            position.y -= offset;
            style.Draw(position, content, false, false, false, false);
            position.y += offset * 2;
            style.Draw(position, content, false, false, false, false);
            position.y -= offset;
            position.x -= offset;
            style.Draw(position, content, false, false, false, false);
            position.x += offset * 2;
            style.Draw(position, content, false, false, false, false);
        }
    }

    internal sealed partial class EditorGUIInternal
    {
        static GUIStyle s_MixedToggleStyle;

        // (Unity's field initializer reads EditorStyles.toggleMixed when the class is first used.)
        internal static GUIStyle mixedToggleStyle
        {
            get { return s_MixedToggleStyle ??= EditorStyles.toggleMixed; }
            set { s_MixedToggleStyle = value; }
        }

        internal static bool DoToggleForward(Rect position, int id, bool value, GUIContent content, GUIStyle style, bool expandWidth = true)
        {
            // (Outside OnGUI there is no event: Mono's NullReferenceException, as the Editor's.)
            EditorGUI.RequireGUI();
            Event evt = Event.current;

            if (EditorGUI.showMixedValue)
                style = mixedToggleStyle;

            // Ignore mouse clicks that are not with the primary (left) mouse button so those can be grabbed by other things later.
            EventType origType = evt.type;
            bool nonLeftClick = (evt.type == EventType.MouseDown && evt.button != 0);
            if (nonLeftClick)
                evt.type = EventType.Ignore;
            bool returnValue;
            bool computedValue = EditorGUI.showMixedValue ? false : value;
            if (expandWidth)
            {
                returnValue = DoToggle(position, id, computedValue, content, style);
            }
            else
            {
                var toggleSize = style.CalcSize(content);
                var visibleRect = new Rect(position.position.x, position.position.y, toggleSize.x, position.height);
                returnValue = DoToggle(visibleRect, id, computedValue, content, style);
            }
            if (nonLeftClick)
                evt.type = origType;
            else if (evt.type != origType)
                EditorGUIUtility.keyboardControl = id; // If control used event, give it keyboard focus.
            return returnValue;
        }
    }

    public sealed partial class EditorGUILayout
    {
        // (The skin-styled forms are measured per family, as EditorGUI's; another family's take no
        // layout space.)
        public static bool Toggle(bool value, params GUILayoutOption[] options)
        {
            if (EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Toggle")) return value;
            Rect r = GetToggleRect(false, options);
            return EditorGUI.Toggle(r, value);
        }

        public static bool Toggle(string label, bool value, params GUILayoutOption[] options) =>
            Toggle(EditorGUIUtility.TempContent(label), value, options);

        public static bool Toggle(GUIContent label, bool value, params GUILayoutOption[] options)
        {
            if (EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Toggle")) return value;
            Rect r = GetToggleRect(true, options);
            return EditorGUI.Toggle(r, label, value);
        }

        public static bool Toggle(bool value, GUIStyle style, params GUILayoutOption[] options)
        {
            Rect r = GetToggleRect(false, options);
            return EditorGUI.Toggle(r, value, style);
        }

        public static bool Toggle(string label, bool value, GUIStyle style, params GUILayoutOption[] options) =>
            Toggle(EditorGUIUtility.TempContent(label), value, style, options);

        public static bool Toggle(GUIContent label, bool value, GUIStyle style, params GUILayoutOption[] options)
        {
            Rect r = GetToggleRect(true, options);
            return EditorGUI.Toggle(r, label, value, style);
        }

        internal static Rect GetToggleRect(bool hasLabel, params GUILayoutOption[] options)
        {
            // Toggle is 14 pixels wide while float fields are EditorGUIUtility.fieldWidth pixels wide.
            // Store difference in variable and add to min and max width values used for float fields.
            float toggleAdjust = (14 - EditorGUIUtility.fieldWidth);
            return GUILayoutUtility.GetRect(
                hasLabel ? kLabelFloatMinW + toggleAdjust : EditorGUIUtility.fieldWidth + toggleAdjust,
                kLabelFloatMaxW + toggleAdjust,
                EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorStyles.numberField, options);
        }

        internal static Rect GetSliderRect(bool hasLabel, GUIStyle sliderStyle, params GUILayoutOption[] options)
        {
            return GUILayoutUtility.GetRect(
                hasLabel ? kLabelFloatMinW : EditorGUIUtility.fieldWidth,
                kLabelFloatMaxW + EditorGUI.kSpacing + EditorGUI.kSliderMaxW,
                EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, sliderStyle, options);
        }

        static void GetSliderParts(GUIStyle baseStyle, ref GUIStyle textFieldStyle, ref GUIStyle thumbStyle, ref GUIStyle thumbExtentStyle)
        {
            string baseName = baseStyle.name;
            thumbStyle = GUI.skin.FindStyle(baseName + "Thumb") ?? thumbStyle;
            thumbExtentStyle = GUI.skin.FindStyle(baseName + "ThumbExtent") ?? thumbExtentStyle;
            textFieldStyle = GUI.skin.FindStyle(baseName + "TextField") ?? textFieldStyle;
        }

        static void GetHorizontalSliderParts(GUIStyle baseStyle, out GUIStyle textFieldStyle, out GUIStyle thumbStyle, out GUIStyle thumbExtentStyle)
        {
            thumbStyle = GUI.skin.horizontalSliderThumb;
            thumbExtentStyle = GUI.skin.horizontalSliderThumbExtent;
            textFieldStyle = EditorStyles.numberField;

            GetSliderParts(baseStyle, ref textFieldStyle, ref thumbStyle, ref thumbExtentStyle);
        }

        internal static float Slider(GUIContent label, float value, float sliderLeftValue, float sliderRightValue, float textLeftValue, float textRightValue, params GUILayoutOption[] options) =>
            Slider(label, value, sliderLeftValue, sliderRightValue, textLeftValue, textRightValue, EditorStyles.numberField,
                GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, null, GUI.skin.horizontalSliderThumbExtent, options);

        internal static float Slider(GUIContent label, float value, float sliderLeftValue, float sliderRightValue, float textLeftValue, float textRightValue, GUIStyle sliderStyle, params GUILayoutOption[] options)
        {
            GUIStyle sliderThumbStyle, sliderThumbStyleExtent, sliderTextFieldStyle;

            GetHorizontalSliderParts(sliderStyle, out sliderTextFieldStyle, out sliderThumbStyle, out sliderThumbStyleExtent);

            return Slider(label, value, sliderLeftValue, sliderRightValue, textLeftValue, textRightValue, sliderTextFieldStyle, sliderStyle,
                sliderThumbStyle, null, sliderThumbStyleExtent, options);
        }

        internal static float Slider(GUIContent label, float value, float sliderLeftValue, float sliderRightValue, float textLeftValue, float textRightValue
            , GUIStyle sliderTextField, GUIStyle sliderStyle, GUIStyle sliderThumbStyle, Texture2D sliderBackground, GUIStyle sliderThumbStyleExtent, params GUILayoutOption[] options)
        {
            Rect r = GetSliderRect(true, sliderStyle, options);
            return EditorGUI.Slider(r, label, value, sliderLeftValue, sliderRightValue, textLeftValue, textRightValue, sliderTextField, sliderStyle, sliderThumbStyle, sliderBackground, sliderThumbStyleExtent);
        }

        public static void Slider(SerializedProperty property, float leftValue, float rightValue, params GUILayoutOption[] options)
        {
            Rect r = GetSliderRect(false, options);
            EditorGUI.Slider(r, property, leftValue, rightValue);
        }

        public static void Slider(SerializedProperty property, float leftValue, float rightValue, string label, params GUILayoutOption[] options) =>
            Slider(property, leftValue, rightValue, EditorGUIUtility.TempContent(label), options);

        public static void Slider(SerializedProperty property, float leftValue, float rightValue, GUIContent label, params GUILayoutOption[] options)
        {
            Rect r = GetSliderRect(true, options);
            EditorGUI.Slider(r, property, leftValue, rightValue, label);
        }

        internal static void Slider(SerializedProperty property, float sliderLeftValue, float sliderRightValue, float textLeftValue, float textRightValue, GUIContent label, params GUILayoutOption[] options)
        {
            Rect r = GetSliderRect(true, options);
            EditorGUI.Slider(r, property, sliderLeftValue, sliderRightValue, textLeftValue, textRightValue, label);
        }

        internal static int IntSlider(int value, int leftValue, int rightValue, float power, GUIStyle sliderStyle, params GUILayoutOption[] options)
        {
            GUIStyle sliderThumbStyle, sliderThumbStyleExtent, sliderTextFieldStyle;

            GetHorizontalSliderParts(sliderStyle, out sliderTextFieldStyle, out sliderThumbStyle, out sliderThumbStyleExtent);

            return IntSlider(value, leftValue, rightValue, power, sliderTextFieldStyle, sliderStyle, sliderThumbStyle, null, sliderThumbStyleExtent, options);
        }

        internal static int IntSlider(int value, int leftValue, int rightValue, float power,
            GUIStyle textfieldStyle, GUIStyle sliderStyle, GUIStyle thumbStyle, Texture2D sliderBackground, GUIStyle thumbStyleExtent, params GUILayoutOption[] options)
        {
            Rect r = GetSliderRect(false, sliderStyle, options);
            return EditorGUI.IntSlider(r, value, leftValue, rightValue, power, logbase: 1, textfieldStyle, sliderStyle, thumbStyle, sliderBackground, thumbStyleExtent);
        }

        public static void IntSlider(SerializedProperty property, int leftValue, int rightValue, params GUILayoutOption[] options)
        {
            Rect r = GetSliderRect(false, options);
            EditorGUI.IntSlider(r, property, leftValue, rightValue, property.displayName);
        }

        public static void IntSlider(SerializedProperty property, int leftValue, int rightValue, string label, params GUILayoutOption[] options) =>
            IntSlider(property, leftValue, rightValue, EditorGUIUtility.TempContent(label), options);

        public static void IntSlider(SerializedProperty property, int leftValue, int rightValue, GUIContent label, params GUILayoutOption[] options)
        {
            Rect r = GetSliderRect(true, options);
            EditorGUI.IntSlider(r, property, leftValue, rightValue, label);
        }
    }
}
