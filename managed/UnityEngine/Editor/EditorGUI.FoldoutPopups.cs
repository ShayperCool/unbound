using System;
using UnityEngine;

namespace UnityEditor
{
    // Unity's foldouts, int popups, min-max sliders and selectable labels (UnityCsReference 6000.0,
    // EditorGUI.cs and EditorGUILayout.cs); measured: tests/projects/editorfoldoutfields.
    public sealed partial class EditorGUI
    {
        const double kFoldoutExpandTimeout = 0.7;
        static readonly int s_MinMaxSliderHash = "EditorMinMaxSlider".GetHashCode();
        static readonly int s_SelectableLabelHash = "s_SelectableLabel".GetHashCode();
        static int s_DragUpdatedOverID;
        static double s_FoldoutDestTime;

        // The forms with the Editor skin's styles: only this release family's styles are measured;
        // another family's report themselves and keep the value given, as EditorGUI.Popup's do.
        public static bool Foldout(Rect position, bool foldout, string content) =>
            Unmeasured("UnityEditor.EditorGUI.Foldout") ? foldout : Foldout(position, foldout, content, EditorStyles.foldout);
        public static bool Foldout(Rect position, bool foldout, string content, GUIStyle style) => Foldout(position, foldout, EditorGUIUtility.TempContent(content), style);
        public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick) =>
            Unmeasured("UnityEditor.EditorGUI.Foldout") ? foldout : Foldout(position, foldout, EditorGUIUtility.TempContent(content), toggleOnLabelClick, EditorStyles.foldout);
        public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick, GUIStyle style) => Foldout(position, foldout, EditorGUIUtility.TempContent(content), toggleOnLabelClick, style);
        public static bool Foldout(Rect position, bool foldout, GUIContent content) =>
            Unmeasured("UnityEditor.EditorGUI.Foldout") ? foldout : Foldout(position, foldout, content, EditorStyles.foldout);
        public static bool Foldout(Rect position, bool foldout, GUIContent content, GUIStyle style) => Foldout(position, foldout, content, false, style);
        public static bool Foldout(Rect position, bool foldout, GUIContent content, bool toggleOnLabelClick) =>
            Unmeasured("UnityEditor.EditorGUI.Foldout") ? foldout : Foldout(position, foldout, content, toggleOnLabelClick, EditorStyles.foldout);
        public static bool Foldout(Rect position, bool foldout, GUIContent content, bool toggleOnLabelClick, GUIStyle style) => FoldoutInternal(position, foldout, content, toggleOnLabelClick, style);

        internal static bool FoldoutInternal(Rect position, bool foldout, GUIContent content, bool toggleOnLabelClick, GUIStyle style)
        {
            Rect origPosition = position;
            position = style.margin.Remove(position);
            if (EditorGUIUtility.hierarchyMode)
            {
                int offset = (EditorStyles.foldout.padding.left - EditorStyles.label.padding.left);
                position.xMin -= offset;
            }

            int id = GUIUtility.GetControlID(s_FoldoutHash, FocusType.Keyboard, position);
            // (Outside OnGUI there is no event: Mono's NullReferenceException, as the Editor's.)
            RequireGUI();
            EventType eventType = Event.current.type;
            // A disabled foldout still takes mouse events, so it can be expanded and collapsed.
            if (!GUI.enabled && GUIClip.enabled && (Event.current.rawType == EventType.MouseDown || Event.current.rawType == EventType.MouseDrag || Event.current.rawType == EventType.MouseUp))
                eventType = Event.current.rawType;

            Rect clickRect = position;
            if (!toggleOnLabelClick)
            {
                clickRect.width = style.padding.left;
                clickRect.x += indent;
            }
            switch (eventType)
            {
                case EventType.MouseDown:
                    if (clickRect.Contains(Event.current.mousePosition) && Event.current.button == 0)
                    {
                        GUIUtility.keyboardControl = GUIUtility.hotControl = id;
                        Event.current.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        Event.current.Use();
                        if (clickRect.Contains(Event.current.mousePosition))
                        {
                            GUI.changed = true;
                            return !foldout;
                        }
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id) Event.current.Use();
                    break;
                case EventType.Repaint:
                    // (EditorStyles.foldoutSelected is GUIStyle.none in Unity's EditorStyles.)
                    GUIStyle.none.Draw(position, GUIContent.none, id, s_DragUpdatedOverID == id);
                    Rect drawRect = new Rect(position.x + indent, position.y, EditorGUIUtility.labelWidth - indent, position.height);
                    if (showMixedValue && !foldout)
                    {
                        style.Draw(drawRect, content, id, false, false);
                        BeginHandleMixedValueContentColor();
                        Rect fieldPosition = origPosition;
                        fieldPosition.xMin += EditorGUIUtility.labelWidth;
                        EditorStyles.label.Draw(fieldPosition, s_MixedValueContent, id, false);
                        EndHandleMixedValueContentColor();
                    }
                    else
                    {
                        style.Draw(drawRect, content, id, foldout, false);
                    }
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl == id)
                    {
                        KeyCode kc = Event.current.keyCode;
                        if (kc == KeyCode.LeftArrow && foldout || (kc == KeyCode.RightArrow && foldout == false))
                        {
                            foldout = !foldout;
                            GUI.changed = true;
                            Event.current.Use();
                        }
                    }
                    break;
                case EventType.DragUpdated:
                    if (s_DragUpdatedOverID == id)
                    {
                        if (position.Contains(Event.current.mousePosition))
                        {
                            if (Time.realtimeSinceStartup > s_FoldoutDestTime)
                            {
                                foldout = true;
                                Event.current.Use();
                            }
                        }
                        else
                        {
                            s_DragUpdatedOverID = 0;
                        }
                    }
                    else if (position.Contains(Event.current.mousePosition))
                    {
                        s_DragUpdatedOverID = id;
                        s_FoldoutDestTime = Time.realtimeSinceStartup + kFoldoutExpandTimeout;
                        Event.current.Use();
                    }
                    break;
                case EventType.DragExited:
                    if (s_DragUpdatedOverID == id)
                    {
                        s_DragUpdatedOverID = 0;
                        Event.current.Use();
                    }
                    break;
            }
            return foldout;
        }

        public static int IntPopup(Rect position, int selectedValue, string[] displayedOptions, int[] optionValues) =>
            Unmeasured("UnityEditor.EditorGUI.IntPopup") ? selectedValue : IntPopup(position, selectedValue, displayedOptions, optionValues, EditorStyles.popup);
        public static int IntPopup(Rect position, int selectedValue, string[] displayedOptions, int[] optionValues, GUIStyle style) =>
            IntPopup(position, GUIContent.none, selectedValue, Contents(displayedOptions), optionValues, style);
        public static int IntPopup(Rect position, int selectedValue, GUIContent[] displayedOptions, int[] optionValues) =>
            Unmeasured("UnityEditor.EditorGUI.IntPopup") ? selectedValue : IntPopup(position, selectedValue, displayedOptions, optionValues, EditorStyles.popup);
        public static int IntPopup(Rect position, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, GUIStyle style) =>
            IntPopup(position, GUIContent.none, selectedValue, displayedOptions, optionValues, style);
        public static int IntPopup(Rect position, GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues) =>
            Unmeasured("UnityEditor.EditorGUI.IntPopup") ? selectedValue : IntPopup(position, label, selectedValue, displayedOptions, optionValues, EditorStyles.popup);
        public static int IntPopup(Rect position, GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, GUIStyle style) =>
            IntPopupInternal(position, label, selectedValue, displayedOptions, optionValues, style);
        public static int IntPopup(Rect position, string label, int selectedValue, string[] displayedOptions, int[] optionValues) =>
            Unmeasured("UnityEditor.EditorGUI.IntPopup") ? selectedValue : IntPopup(position, label, selectedValue, displayedOptions, optionValues, EditorStyles.popup);
        public static int IntPopup(Rect position, string label, int selectedValue, string[] displayedOptions, int[] optionValues, GUIStyle style) =>
            IntPopup(position, EditorGUIUtility.TempContent(label), selectedValue, Contents(displayedOptions), optionValues, style);

        static int IntPopupInternal(Rect position, GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, GUIStyle style)
        {
            // value --> index
            int i;
            if (showMixedValue)
                i = -1;
            else if (optionValues != null)
            {
                for (i = 0; (i < optionValues.Length) && (selectedValue != optionValues[i]); ++i)
                {
                }
            }
            else
                i = selectedValue;

            i = Popup(position, label, i, displayedOptions, style);

            if (optionValues == null) return i;
            // index --> value
            if (i < 0 || i >= optionValues.Length) return selectedValue;
            return optionValues[i];
        }

        public static void MinMaxSlider(Rect position, string label, ref float minValue, ref float maxValue, float minLimit, float maxLimit) =>
            MinMaxSlider(position, EditorGUIUtility.TempContent(label), ref minValue, ref maxValue, minLimit, maxLimit);

        // (Every form draws with the skin's min-max thumb, so every form is measured per family.)
        public static void MinMaxSlider(Rect position, GUIContent label, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
        {
            if (Unmeasured("UnityEditor.EditorGUI.MinMaxSlider")) return;
            int id = GUIUtility.GetControlID(s_MinMaxSliderHash, FocusType.Passive);
            DoMinMaxSlider(PrefixLabel(position, id, label), id, ref minValue, ref maxValue, minLimit, maxLimit);
        }

        public static void MinMaxSlider(Rect position, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
        {
            if (Unmeasured("UnityEditor.EditorGUI.MinMaxSlider")) return;
            DoMinMaxSlider(IndentedRect(position), GUIUtility.GetControlID(s_MinMaxSliderHash, FocusType.Passive), ref minValue, ref maxValue, minLimit, maxLimit);
        }

        [Obsolete("Switch the order of the first two parameters.")]
        public static void MinMaxSlider(GUIContent label, Rect position, ref float minValue, ref float maxValue, float minLimit, float maxLimit) =>
            MinMaxSlider(position, label, ref minValue, ref maxValue, minLimit, maxLimit);

        static void DoMinMaxSlider(Rect position, int id, ref float minValue, ref float maxValue, float minLimit, float maxLimit)
        {
            float size = maxValue - minValue;
            BeginChangeCheck();
            // (Outside OnGUI GUI.skin throws first: "You can only call GUI functions from inside OnGUI.")
            EditorGUIExt.DoMinMaxSlider(position, id, ref minValue, ref size, minLimit, maxLimit, minLimit, maxLimit, GUI.skin.horizontalSlider, EditorStyles.minMaxHorizontalSliderThumb, true);
            if (EndChangeCheck())
                maxValue = minValue + size;
        }

        public static void SelectableLabel(Rect position, string text)
        {
            if (Unmeasured("UnityEditor.EditorGUI.SelectableLabel")) return;
            SelectableLabel(position, text, EditorStyles.label);
        }

        public static void SelectableLabel(Rect position, string text, GUIStyle style) => SelectableLabelInternal(position, text, style);

        static bool SendEventToTextEditor(int id, Event e)
        {
            var sendEventToTextEditor = true;
            if (GUIUtility.keyboardControl == id && e.GetTypeForControl(id) == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.LeftArrow:
                    case KeyCode.RightArrow:
                    case KeyCode.UpArrow:
                    case KeyCode.DownArrow:
                    case KeyCode.Home:
                    case KeyCode.End:
                    case KeyCode.PageUp:
                    case KeyCode.PageDown:
                        break;
                    case KeyCode.Space:
                        GUIUtility.hotControl = 0;
                        GUIUtility.keyboardControl = 0;
                        break;
                    default:
                        sendEventToTextEditor = false;
                        break;
                }
            }
            if (e.type == EventType.ExecuteCommand && (e.commandName == "Paste" || e.commandName == "Cut") && GUIUtility.keyboardControl == id)
                sendEventToTextEditor = false;
            return sendEventToTextEditor;
        }

        internal static void SelectableLabelInternal(Rect position, string text, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_SelectableLabelHash, FocusType.Keyboard, position);
            RequireGUI();
            var sendEventToTextEditor = SendEventToTextEditor(id, Event.current);
            // The cursor is hidden while the label edits (Unity's CursorColorScope).
            var settings = GUI.skin.settings;
            var cursor = settings.cursorColor;
            settings.cursorColor = Color.clear;
            try
            {
                RecycledTextEditor.s_AllowContextCutOrPaste = false;
                if (sendEventToTextEditor)
                    DoTextField(s_RecycledEditor, id, IndentedRect(position), text, style, string.Empty, out _, false, true, false);
            }
            finally { settings.cursorColor = cursor; }
        }
    }

    public sealed partial class EditorGUILayout
    {
        // (The skin-styled forms are measured per family, as EditorGUI's; another family's take no
        // layout space.)
        public static bool Foldout(bool foldout, string content) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Foldout") ? foldout : Foldout(foldout, content, EditorStyles.foldout);
        public static bool Foldout(bool foldout, string content, GUIStyle style) => Foldout(foldout, EditorGUIUtility.TempContent(content), false, style);
        public static bool Foldout(bool foldout, GUIContent content) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Foldout") ? foldout : Foldout(foldout, content, EditorStyles.foldout);
        public static bool Foldout(bool foldout, GUIContent content, GUIStyle style) => Foldout(foldout, content, false, style);
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Foldout") ? foldout : Foldout(foldout, content, toggleOnLabelClick, EditorStyles.foldout);
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick, GUIStyle style) => FoldoutInternal(foldout, EditorGUIUtility.TempContent(content), toggleOnLabelClick, style);
        public static bool Foldout(bool foldout, GUIContent content, bool toggleOnLabelClick) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Foldout") ? foldout : Foldout(foldout, content, toggleOnLabelClick, EditorStyles.foldout);
        public static bool Foldout(bool foldout, GUIContent content, bool toggleOnLabelClick, GUIStyle style) => FoldoutInternal(foldout, content, toggleOnLabelClick, style);

        internal static bool FoldoutInternal(bool foldout, GUIContent content, bool toggleOnLabelClick, GUIStyle style)
        {
            Rect r = GUILayoutUtility.GetRect(EditorGUIUtility.fieldWidth, EditorGUIUtility.fieldWidth, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, style);
            return EditorGUI.Foldout(r, foldout, content, toggleOnLabelClick, style);
        }

        public static int IntPopup(int selectedValue, string[] displayedOptions, int[] optionValues, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.IntPopup") ? selectedValue : IntPopup(selectedValue, displayedOptions, optionValues, EditorStyles.popup, options);
        public static int IntPopup(int selectedValue, string[] displayedOptions, int[] optionValues, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.IntPopup(GetControlRect(false, EditorGUI.kSingleLineHeight, style, options), selectedValue, displayedOptions, optionValues, style);
        public static int IntPopup(int selectedValue, GUIContent[] displayedOptions, int[] optionValues, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.IntPopup") ? selectedValue : IntPopup(selectedValue, displayedOptions, optionValues, EditorStyles.popup, options);
        public static int IntPopup(int selectedValue, GUIContent[] displayedOptions, int[] optionValues, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.IntPopup(GetControlRect(false, EditorGUI.kSingleLineHeight, style, options), GUIContent.none, selectedValue, displayedOptions, optionValues, style);
        public static int IntPopup(string label, int selectedValue, string[] displayedOptions, int[] optionValues, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.IntPopup") ? selectedValue : IntPopup(label, selectedValue, displayedOptions, optionValues, EditorStyles.popup, options);
        public static int IntPopup(string label, int selectedValue, string[] displayedOptions, int[] optionValues, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.IntPopup(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, selectedValue, displayedOptions, optionValues, style);
        public static int IntPopup(GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.IntPopup") ? selectedValue : IntPopup(label, selectedValue, displayedOptions, optionValues, EditorStyles.popup, options);
        public static int IntPopup(GUIContent label, int selectedValue, GUIContent[] displayedOptions, int[] optionValues, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.IntPopup(GetControlRect(true, EditorGUI.kSingleLineHeight, style, options), label, selectedValue, displayedOptions, optionValues, style);

        public static void MinMaxSlider(ref float minValue, ref float maxValue, float minLimit, float maxLimit, params GUILayoutOption[] options)
        {
            if (EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.MinMaxSlider")) return;
            EditorGUI.MinMaxSlider(GetSliderRect(false, options), ref minValue, ref maxValue, minLimit, maxLimit);
        }
        public static void MinMaxSlider(string label, ref float minValue, ref float maxValue, float minLimit, float maxLimit, params GUILayoutOption[] options) =>
            MinMaxSlider(EditorGUIUtility.TempContent(label), ref minValue, ref maxValue, minLimit, maxLimit, options);
        public static void MinMaxSlider(GUIContent label, ref float minValue, ref float maxValue, float minLimit, float maxLimit, params GUILayoutOption[] options)
        {
            if (EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.MinMaxSlider")) return;
            EditorGUI.MinMaxSlider(GetSliderRect(true, options), label, ref minValue, ref maxValue, minLimit, maxLimit);
        }

        public static void SelectableLabel(string text, params GUILayoutOption[] options)
        {
            if (EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.SelectableLabel")) return;
            SelectableLabel(text, EditorStyles.label, options);
        }

        public static void SelectableLabel(string text, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.SelectableLabel(GetControlRect(false, EditorGUI.kSingleLineHeight * 2, style, options), text, style);
    }
}
