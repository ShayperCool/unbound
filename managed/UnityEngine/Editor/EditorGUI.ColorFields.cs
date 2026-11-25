using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// EditorGUI's colour, curve and gradient fields as Unity 6000.0's C# does them: keyboard-
    /// focusable controls that open their editor window on a click or Space/Return (the colour
    /// picker, the eyedropper, the curve editor, the gradient editor: Unbound shows none, said
    /// once, and the GUI exits as the Editor's does), take the window's change as its command, and
    /// a colour field copies and pastes its colour with Control-C/V through the clipboard's text
    /// (measured: tests/projects/editorobjectfields). Other families report the fields not
    /// implemented and return the value they are given.
    /// </summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_ColorHash = "s_ColorHash".GetHashCode();
        static readonly int s_CurveHash = "s_CurveHash".GetHashCode();
        static readonly int s_GradientHash = "s_GradientHash".GetHashCode();
        static int s_ColorPickID;
        static int s_CurveID;
        static int s_GradientID;

        const float kEyedropperSize = 20f;

        internal static GUIStyle ColorFieldStyle => Compat.EditorValueFieldsMeasured ? EditorStyles.colorField : null;
        internal static Color kCurveColor = Color.green;
        internal static Color kCurveBGColor = new Color(0.337f, 0.337f, 0.337f, 1f);

        // The commands the Editor's colour picker, eyedropper, curve and gradient windows send back.
        const string k_ColorPickerChanged = "ColorPickerChanged";
        const string k_EyeDropperClicked = "EyeDropperClicked";
        const string k_CurveChanged = "CurveChanged";
        const string k_GradientPickerChanged = "GradientPickerChanged";

        public static Color ColorField(Rect position, Color value)
        {
            if (!PickerFieldsMeasured("ColorField")) return value;
            int id = GUIUtility.GetControlID(s_ColorHash, FocusType.Keyboard, position);
            return DoColorField(IndentedRect(position), id, value, true, true, false);
        }

        internal static Color ColorField(Rect position, Color value, bool showEyedropper, bool showAlpha)
        {
            if (!PickerFieldsMeasured("ColorField")) return value;
            int id = GUIUtility.GetControlID(s_ColorHash, FocusType.Keyboard, position);
            return DoColorField(position, id, value, showEyedropper, showAlpha, false);
        }

        public static Color ColorField(Rect position, string label, Color value) =>
            ColorField(position, EditorGUIUtility.TempContent(label), value);

        public static Color ColorField(Rect position, GUIContent label, Color value) =>
            ColorField(position, label, value, true, true, false);

        internal static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper, bool showAlpha) =>
            ColorField(position, label, value, showEyedropper, showAlpha, false);

        public static Color ColorField(Rect position, GUIContent label, Color value, bool showEyedropper, bool showAlpha, bool hdr)
        {
            if (!PickerFieldsMeasured("ColorField")) return value;
            int id = GUIUtility.GetControlID(s_ColorHash, FocusType.Keyboard, position);
            return DoColorField(PrefixLabel(position, id, label), id, value, showEyedropper, showAlpha, hdr);
        }

        static Color DoColorField(Rect position, int id, Color value, bool showEyedropper, bool showAlpha, bool hdr)
        {
            Event evt = Event.current;
            GUIStyle style = EditorStyles.colorField;
            Color origColor = value;
            value = showMixedValue ? Color.white : value;
            bool hovered = position.Contains(evt.mousePosition);
            bool hoveredEyedropper = new Rect(position.x + position.width - kEyedropperSize, position.y, kEyedropperSize, position.height).Contains(evt.mousePosition);

            var wasEnabled = GUI.enabled;
            var eventType = GetEventTypeForControlAllowDisabledContextMenuPaste(evt, id);

            switch (eventType)
            {
                case EventType.MouseDown:
                    if (showEyedropper) hovered ^= hoveredEyedropper;

                    if (hovered)
                    {
                        switch (evt.button)
                        {
                            case 0:
                                GUIUtility.keyboardControl = id;
                                showMixedValue = false;
                                ShowColorPicker();
                                GUIUtility.ExitGUI();
                                break;
                            case 1:
                                GUIUtility.keyboardControl = id;
                                Stubs.Ignored("UnityEditor.EditorGUI.ColorField's context menu", "is not shown: Unbound shows no editor menus (Copy and Paste stay on Control-C/V)");
                                evt.Use();
                                return origColor;
                        }
                    }

                    if (showEyedropper && hoveredEyedropper && wasEnabled)
                    {
                        GUIUtility.keyboardControl = id;
                        Stubs.Ignored("UnityEditor.EyeDropper", "the eyedropper is not shown: Unbound has no editor windows, so the colour field keeps its colour");
                        s_ColorPickID = id;
                        GUIUtility.ExitGUI();
                    }
                    break;

                case EventType.Repaint:
                    Rect position2 = showEyedropper ? style.padding.Remove(position) : EditorStyles.colorPickerBox.padding.Remove(position);
                    if (showEyedropper) style.Draw(position, GUIContent.none, id, false, hovered);
                    else EditorStyles.colorPickerBox.Draw(position, GUIContent.none, id, false, hovered);
                    EditorGUIUtility.DrawColorSwatch(position2, value, showAlpha, hdr);
                    break;

                case EventType.ValidateCommand:
                    switch (evt.commandName)
                    {
                        case EventCommandNames.Copy:
                        case EventCommandNames.Paste:
                            evt.Use();
                            break;
                    }
                    break;

                case EventType.ExecuteCommand:
                    if (showEyedropper && evt.commandName == EventCommandNames.NewKeyboardFocus)
                        s_ColorPickID = 0;

                    if (GUIUtility.keyboardControl == id)
                    {
                        switch (evt.commandName)
                        {
                            case k_EyeDropperClicked:
                            case k_ColorPickerChanged:
                                // Unbound has no picker or eyedropper that would send these.
                                break;
                            case EventCommandNames.Copy:
                                Clipboard.colorValue = value;
                                evt.Use();
                                break;
                            case EventCommandNames.Paste:
                                PasteColor(ref origColor, showAlpha, hdr);
                                break;
                        }
                    }
                    break;

                case EventType.KeyDown:
                    if (MainActionKeyForControl(evt, id))
                    {
                        evt.Use();
                        showMixedValue = false;
                        ShowColorPicker();
                        GUIUtility.ExitGUI();
                    }
                    else if (evt.modifiers == EventModifiers.Control && GUIUtility.keyboardControl == id)
                    {
                        if (evt.keyCode == KeyCode.C)
                        {
                            Clipboard.colorValue = value;
                            evt.Use();
                        }
                        else if (evt.keyCode == KeyCode.V)
                        {
                            PasteColor(ref origColor, showAlpha, hdr);
                        }
                    }
                    break;
            }
            return origColor;
        }

        static void ShowColorPicker() =>
            Stubs.Ignored("UnityEditor.ColorPicker", "the colour picker is not shown: Unbound has no editor windows, so the colour field keeps its colour");

        static void PasteColor(ref Color origColor, bool showAlpha, bool hdr)
        {
            if (!Clipboard.hasColor) return;
            Color pasted = Clipboard.colorValue;
            if (!hdr && pasted.maxColorComponent > 1f)
                pasted = pasted.RGBMultiplied(1f / pasted.maxColorComponent);
            // A field that shows no alpha keeps its alpha.
            if (!showAlpha) pasted.a = origColor.a;
            origColor = pasted;
            GUI.changed = true;
            Event.current.Use();
        }

        public static AnimationCurve CurveField(Rect position, AnimationCurve value)
        {
            if (!PickerFieldsMeasured("CurveField")) return value;
            int id = GUIUtility.GetControlID(s_CurveHash, FocusType.Keyboard, position);
            return DoCurveField(IndentedRect(position), id, value, kCurveColor, new Rect(), null);
        }

        public static AnimationCurve CurveField(Rect position, string label, AnimationCurve value) =>
            CurveField(position, EditorGUIUtility.TempContent(label), value);

        public static AnimationCurve CurveField(Rect position, GUIContent label, AnimationCurve value)
        {
            if (!PickerFieldsMeasured("CurveField")) return value;
            int id = GUIUtility.GetControlID(s_CurveHash, FocusType.Keyboard, position);
            return DoCurveField(PrefixLabel(position, id, label), id, value, kCurveColor, new Rect(), null);
        }

        public static AnimationCurve CurveField(Rect position, AnimationCurve value, Color color, Rect ranges)
        {
            if (!PickerFieldsMeasured("CurveField")) return value;
            int id = GUIUtility.GetControlID(s_CurveHash, FocusType.Keyboard, position);
            return DoCurveField(IndentedRect(position), id, value, color, ranges, null);
        }

        public static AnimationCurve CurveField(Rect position, string label, AnimationCurve value, Color color, Rect ranges) =>
            CurveField(position, EditorGUIUtility.TempContent(label), value, color, ranges);

        public static AnimationCurve CurveField(Rect position, GUIContent label, AnimationCurve value, Color color, Rect ranges)
        {
            if (!PickerFieldsMeasured("CurveField")) return value;
            int id = GUIUtility.GetControlID(s_CurveHash, FocusType.Keyboard, position);
            return DoCurveField(PrefixLabel(position, id, label), id, value, color, ranges, null);
        }

        public static void CurveField(Rect position, SerializedProperty property, Color color, Rect ranges) =>
            CurveField(position, property, color, ranges, null);

        public static void CurveField(Rect position, SerializedProperty property, Color color, Rect ranges, GUIContent label)
        {
            if (!PickerFieldsMeasured("CurveField")) return;
            label = BeginProperty(position, label, property);
            int id = GUIUtility.GetControlID(s_CurveHash, FocusType.Keyboard, position);
            DoCurveField(PrefixLabel(position, id, label), id, null, color, ranges, property);
            EndProperty();
        }

        internal static AnimationCurve DoCurveField(Rect position, int id, AnimationCurve value, Color color, Rect ranges, SerializedProperty property)
        {
            Event evt = Event.current;
            position.width = Mathf.Max(position.width, 2);
            position.height = Mathf.Max(position.height, 2);

            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && position.Contains(evt.mousePosition))
                    {
                        s_CurveID = id;
                        GUIUtility.keyboardControl = id;
                        ShowCurveEditor();
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                    break;
                case EventType.Repaint:
                    Rect position2 = position;
                    position2.y += 1;
                    position2.height -= 1;
                    if (ranges != new Rect()) EditorGUIUtility.DrawCurveSwatch(position2, value, property, color, kCurveBGColor, ranges);
                    else EditorGUIUtility.DrawCurveSwatch(position2, value, property, color, kCurveBGColor);
                    EditorStyles.colorPickerBox.Draw(position2, GUIContent.none, id, false);
                    break;
                case EventType.ExecuteCommand:
                    // Unbound has no curve editor that would send its change (k_CurveChanged).
                    break;
                case EventType.KeyDown:
                    if (MainActionKeyForControl(evt, id))
                    {
                        s_CurveID = id;
                        ShowCurveEditor();
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                    break;
            }
            return value;
        }

        static void ShowCurveEditor() =>
            Stubs.Ignored("UnityEditor.CurveEditorWindow", "the curve editor is not shown: Unbound has no editor windows, so the curve field keeps its curve");

        public static Gradient GradientField(Rect position, Gradient gradient)
        {
            if (!PickerFieldsMeasured("GradientField")) return gradient;
            int id = GUIUtility.GetControlID(s_GradientHash, FocusType.Keyboard, position);
            return DoGradientField(position, id, gradient, null, false, ColorSpace.Gamma);
        }

        public static Gradient GradientField(Rect position, string label, Gradient gradient) =>
            GradientField(position, EditorGUIUtility.TempContent(label), gradient);

        public static Gradient GradientField(Rect position, GUIContent label, Gradient gradient) =>
            GradientField(position, label, gradient, false);

        public static Gradient GradientField(Rect position, GUIContent label, Gradient gradient, bool hdr) =>
            GradientField(position, label, gradient, hdr, ColorSpace.Gamma);

        public static Gradient GradientField(Rect position, GUIContent label, Gradient gradient, bool hdr, ColorSpace colorSpace)
        {
            if (!PickerFieldsMeasured("GradientField")) return gradient;
            int id = GUIUtility.GetControlID(s_GradientHash, FocusType.Keyboard, position);
            return DoGradientField(PrefixLabel(position, id, label), id, gradient, null, hdr, colorSpace);
        }

        internal static Gradient GradientField(Rect position, GUIContent label, SerializedProperty property)
        {
            if (!PickerFieldsMeasured("GradientField")) return null;
            int id = GUIUtility.GetControlID(s_GradientHash, FocusType.Keyboard, position);
            return DoGradientField(PrefixLabel(position, id, label), id, null, property, false, ColorSpace.Gamma);
        }

        internal static Gradient DoGradientField(Rect position, int id, Gradient value, SerializedProperty property, bool hdr, ColorSpace space)
        {
            Event evt = Event.current;
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (position.Contains(evt.mousePosition))
                    {
                        if (evt.button == 0)
                        {
                            s_GradientID = id;
                            GUIUtility.keyboardControl = id;
                            ShowGradientPicker();
                            GUIUtility.ExitGUI();
                        }
                        else if (evt.button == 1 && property != null)
                        {
                            Stubs.Ignored("UnityEditor.GradientContextMenu", "a gradient property's context menu is not shown: Unbound shows no editor menus");
                        }
                    }
                    break;
                case EventType.Repaint:
                {
                    // A field without a gradient throws drawing its swatch, as the Editor's does.
                    if (property == null && value == null) throw new NullReferenceException("Object reference not set to an instance of an object");
                    Stubs.Ignored("UnityEditor.GradientEditor.DrawGradientSwatch", "a gradient field's swatch is not drawn (only its frame)");
                    EditorStyles.colorPickerBox.Draw(position, GUIContent.none, id);
                    break;
                }
                case EventType.ExecuteCommand:
                    // Unbound has no gradient editor that would send its change (k_GradientPickerChanged).
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl == id && (evt.keyCode == KeyCode.Space || evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                    {
                        evt.Use();
                        ShowGradientPicker();
                        GUIUtility.ExitGUI();
                    }
                    break;
            }
            return value;
        }

        static void ShowGradientPicker() =>
            Stubs.Ignored("UnityEditor.GradientPicker", "the gradient editor is not shown: Unbound has no editor windows, so the gradient field keeps its gradient");
    }

    public sealed partial class EditorGUILayout
    {
        public static Color ColorField(Color value, params GUILayoutOption[] options) =>
            EditorGUI.ColorField(GetControlRect(false, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), value);

        public static Color ColorField(string label, Color value, params GUILayoutOption[] options) =>
            EditorGUI.ColorField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), label, value);

        public static Color ColorField(GUIContent label, Color value, params GUILayoutOption[] options) =>
            EditorGUI.ColorField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), label, value);

        public static Color ColorField(GUIContent label, Color value, bool showEyedropper, bool showAlpha, bool hdr, params GUILayoutOption[] options) =>
            EditorGUI.ColorField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), label, value, showEyedropper, showAlpha, hdr);

        // The curve fields take the colour field's style for their rect, as the Editor's do.
        public static AnimationCurve CurveField(AnimationCurve value, params GUILayoutOption[] options) =>
            EditorGUI.CurveField(GetControlRect(false, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), value);

        public static AnimationCurve CurveField(string label, AnimationCurve value, params GUILayoutOption[] options) =>
            EditorGUI.CurveField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), label, value);

        public static AnimationCurve CurveField(GUIContent label, AnimationCurve value, params GUILayoutOption[] options) =>
            EditorGUI.CurveField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), label, value);

        public static AnimationCurve CurveField(AnimationCurve value, Color color, Rect ranges, params GUILayoutOption[] options) =>
            EditorGUI.CurveField(GetControlRect(false, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), value, color, ranges);

        public static AnimationCurve CurveField(string label, AnimationCurve value, Color color, Rect ranges, params GUILayoutOption[] options) =>
            EditorGUI.CurveField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), label, value, color, ranges);

        public static AnimationCurve CurveField(GUIContent label, AnimationCurve value, Color color, Rect ranges, params GUILayoutOption[] options) =>
            EditorGUI.CurveField(GetControlRect(true, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), label, value, color, ranges);

        public static void CurveField(SerializedProperty property, Color color, Rect ranges, params GUILayoutOption[] options) =>
            CurveField(property, color, ranges, null, options);

        public static void CurveField(SerializedProperty property, Color color, Rect ranges, GUIContent label, params GUILayoutOption[] options) =>
            EditorGUI.CurveField(GetControlRect(false, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options), property, color, ranges, label);

        // The gradient fields reserve their rect from GUILayoutUtility directly, as the Editor's do.
        static Rect GradientRect(bool hasLabel, GUILayoutOption[] options) =>
            GUILayoutUtility.GetRect(hasLabel ? kLabelFloatMinW : EditorGUIUtility.fieldWidth, kLabelFloatMaxW, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorGUI.ColorFieldStyle, options);

        public static Gradient GradientField(Gradient value, params GUILayoutOption[] options)
        {
            if (!Compat.EditorValueFieldsMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUILayout.GradientField"); return value; }
            return EditorGUI.GradientField(GradientRect(false, options), value);
        }

        public static Gradient GradientField(string label, Gradient value, params GUILayoutOption[] options)
        {
            if (!Compat.EditorValueFieldsMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUILayout.GradientField"); return value; }
            return EditorGUI.GradientField(GradientRect(true, options), label, value);
        }

        public static Gradient GradientField(GUIContent label, Gradient value, params GUILayoutOption[] options)
        {
            if (!Compat.EditorValueFieldsMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUILayout.GradientField"); return value; }
            return EditorGUI.GradientField(GradientRect(true, options), label, value);
        }

        public static Gradient GradientField(GUIContent label, Gradient value, bool hdr, params GUILayoutOption[] options)
        {
            if (!Compat.EditorValueFieldsMeasured) { Stubs.NotImplemented("UnityEditor.EditorGUILayout.GradientField"); return value; }
            return EditorGUI.GradientField(GradientRect(true, options), label, value, hdr);
        }
    }

    public sealed partial class EditorGUIUtility
    {
        static GUIStyle s_WhiteTextureStyle;

        internal static GUIStyle whiteTextureStyle => s_WhiteTextureStyle ??= new GUIStyle { normal = { background = Texture2D.whiteTexture } };

        public static void DrawColorSwatch(Rect position, Color color) => DrawColorSwatch(position, color, true);

        internal static void DrawColorSwatch(Rect position, Color color, bool showAlpha) => DrawColorSwatch(position, color, showAlpha, false);

        /// <summary>A colour's swatch: the colour, and its alpha as a white bar on black at the
        /// bottom (the mixed-value dash when values differ). An HDR colour's exposure sides and
        /// label are not drawn (said once).</summary>
        internal static void DrawColorSwatch(Rect position, Color color, bool showAlpha, bool hdr)
        {
            if (Event.current.type != EventType.Repaint) return;

            Color oldColor = GUI.color;
            Color oldBackgroundColor = GUI.backgroundColor;
            float a = GUI.enabled ? 1 : 2;

            GUI.color = EditorGUI.showMixedValue ? new Color(0.82f, 0.82f, 0.82f, a) * oldColor : new Color(color.r, color.g, color.b, a);
            if (hdr)
            {
                GUI.color = GUI.color.gamma;
                if (color.maxColorComponent > 1f)
                    Stubs.Ignored("UnityEditor.EditorGUIUtility.DrawColorSwatch HDR", "an HDR colour swatch's exposure gradients and label are not drawn");
            }
            GUI.backgroundColor = Color.white;

            GUIStyle gs = whiteTextureStyle;
            gs.Draw(position, false, false, false, false);

            if (!EditorGUI.showMixedValue)
            {
                if (showAlpha)
                {
                    GUI.color = new Color(0, 0, 0, a);
                    float alphaHeight = Mathf.Clamp(position.height * .2f, 2, 20);
                    Rect alphaBarRect = new Rect(position.x, position.yMax - alphaHeight, position.width, alphaHeight);
                    gs.Draw(alphaBarRect, false, false, false, false);

                    GUI.color = new Color(1, 1, 1, a);
                    alphaBarRect.width *= Mathf.Clamp01(color.a);
                    gs.Draw(alphaBarRect, false, false, false, false);
                }
            }
            else
            {
                EditorGUI.BeginHandleMixedValueContentColor();
                gs.Draw(position, EditorGUI.mixedValueContent, false, false, false, false);
                EditorGUI.EndHandleMixedValueContentColor();
            }

            GUI.color = oldColor;
            GUI.backgroundColor = oldBackgroundColor;
        }

        public static void DrawCurveSwatch(Rect position, AnimationCurve curve, SerializedProperty property, Color color, Color bgColor) =>
            DrawCurveSwatchInternal(position, bgColor);

        public static void DrawCurveSwatch(Rect position, AnimationCurve curve, SerializedProperty property, Color color, Color bgColor, Rect curveRanges) =>
            DrawCurveSwatchInternal(position, bgColor);

        /// <summary>A curve's swatch: its background (the curve itself is not drawn, said once).</summary>
        static void DrawCurveSwatchInternal(Rect position, Color bgColor)
        {
            if (Event.current.type != EventType.Repaint) return;
            Stubs.Ignored("UnityEditor.EditorGUIUtility.DrawCurveSwatch", "a curve field's curve is not drawn (only its background and frame)");
            Color old = GUI.color;
            GUI.color = bgColor;
            whiteTextureStyle.Draw(position, false, false, false, false);
            GUI.color = old;
        }
    }

    /// <summary>The Editor's clipboard for values: a colour as its HTML text (#RRGGBB, #RRGGBBAA)
    /// or Color(r,g,b,a) where it is not an 8-bit colour, in the system copy buffer.</summary>
    internal static partial class Clipboard
    {
        public static bool hasColor => ClipboardParser.ParseColor(EditorGUIUtility.systemCopyBuffer, out _);

        public static Color colorValue
        {
            get
            {
                ClipboardParser.ParseColor(EditorGUIUtility.systemCopyBuffer, out var color);
                return color;
            }
            set => EditorGUIUtility.systemCopyBuffer = ClipboardParser.WriteColor(value);
        }
    }

    internal static partial class ClipboardParser
    {
        static float[] ParseFloats(string text, string prefix, int count)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var sb = new StringBuilder();
            sb.Append('^').Append(prefix).Append("\\(");
            for (var i = 0; i < count; ++i)
            {
                if (i != 0) sb.Append(',');
                sb.Append("([^,]+)");
            }
            sb.Append("\\)");
            var match = Regex.Match(text, sb.ToString());
            if (!match.Success || match.Groups.Count <= count) return null;
            var res = new float[count];
            for (var i = 0; i < count; ++i)
            {
                if (float.TryParse(match.Groups[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) res[i] = f;
                else return null;
            }
            return res;
        }

        public static bool ParseColor(string text, out Color res)
        {
            res = Color.black;
            if (string.IsNullOrEmpty(text)) return false;
            if (ColorUtility.TryParseHtmlString(text, out res)) return true;
            var v = ParseFloats(text, "Color", 4);
            if (v == null) return false;
            res = new Color(v[0], v[1], v[2], v[3]);
            return true;
        }

        public static string WriteColor(Color val)
        {
            // A colour that 8 bits represent is written as HTML.
            Color32 ldr = val;
            Color hdrFromLdr = ldr;
            if (((Vector4)val - (Vector4)hdrFromLdr).sqrMagnitude < 0.0001f)
            {
                if (ldr.a == 255) return '#' + ColorUtility.ToHtmlStringRGB(val);
                return '#' + ColorUtility.ToHtmlStringRGBA(val);
            }
            return string.Format(CultureInfo.InvariantCulture, "Color({0:g9},{1:g9},{2:g9},{3:g9})", val.r, val.g, val.b, val.a);
        }
    }
}
