using System;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>EditorGUI.Popup and EditorGUILayout.Popup: a keyboard-focusable control (one
    /// control ID), its prefix label where it has one, the selected option drawn with the popup
    /// style; a click (or Space/Return while it has keyboard focus) opens the options' menu, and
    /// the menu's choice comes back as a PopupMenuChanged command to the control
    /// (tests/projects/imguiskin `popup/*`; Scripting API 6000.0 EditorGUI.Popup,
    /// EditorGUILayout.Popup).</summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_PopupHash = "EditorPopup".GetHashCode();
        static readonly GUIContent s_MixedValueContent = new GUIContent("—", "Mixed Values");
        static readonly Color s_MixedValueContentColor = new Color(1, 1, 1, 0.5f);

        public static int Popup(Rect position, int selectedIndex, string[] displayedOptions) =>
            Unmeasured("UnityEditor.EditorGUI.Popup") ? selectedIndex : Popup(position, selectedIndex, displayedOptions, EditorStyles.popup);

        public static int Popup(Rect position, int selectedIndex, string[] displayedOptions, GUIStyle style) =>
            DoPopup(IndentedRect(position), GUIUtility.GetControlID(s_PopupHash, FocusType.Keyboard, position), selectedIndex, Contents(displayedOptions), null, style);

        public static int Popup(Rect position, int selectedIndex, GUIContent[] displayedOptions) =>
            Unmeasured("UnityEditor.EditorGUI.Popup") ? selectedIndex : Popup(position, selectedIndex, displayedOptions, EditorStyles.popup);

        public static int Popup(Rect position, int selectedIndex, GUIContent[] displayedOptions, GUIStyle style) =>
            DoPopup(IndentedRect(position), GUIUtility.GetControlID(s_PopupHash, FocusType.Keyboard, position), selectedIndex, displayedOptions, null, style);

        public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions) =>
            Unmeasured("UnityEditor.EditorGUI.Popup") ? selectedIndex : Popup(position, label, selectedIndex, displayedOptions, EditorStyles.popup);

        public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions, GUIStyle style) =>
            PopupInternal(position, new GUIContent(label), selectedIndex, Contents(displayedOptions), style);

        public static int Popup(Rect position, GUIContent label, int selectedIndex, GUIContent[] displayedOptions) =>
            Unmeasured("UnityEditor.EditorGUI.Popup") ? selectedIndex : Popup(position, label, selectedIndex, displayedOptions, EditorStyles.popup);

        public static int Popup(Rect position, GUIContent label, int selectedIndex, GUIContent[] displayedOptions, GUIStyle style) =>
            PopupInternal(position, label, selectedIndex, displayedOptions, style);

        internal static int Popup(Rect position, GUIContent label, int selectedIndex, string[] displayedOptions, GUIStyle style) =>
            PopupInternal(position, label, selectedIndex, Contents(displayedOptions), style);

        internal static int Popup(Rect position, GUIContent label, int selectedIndex, string[] displayedOptions) =>
            Unmeasured("UnityEditor.EditorGUI.Popup") ? selectedIndex : Popup(position, label, selectedIndex, displayedOptions, EditorStyles.popup);

        /// <summary>Whether the popups of this release's family are not measured (its inspector
        /// styles and control rects are not): said once, and the popup keeps its selection,
        /// taking no control ID and no layout space.</summary>
        internal static bool Unmeasured(string member)
        {
            if (Compat.InspectorGuiStylesMeasured) return false;
            Stubs.NotImplemented(member);
            return true;
        }

        /// <summary>One content per option (a missing list throws, as the Editor's does).</summary>
        static GUIContent[] Contents(string[] texts)
        {
            var contents = new GUIContent[texts.Length];
            for (int i = 0; i < texts.Length; i++) contents[i] = new GUIContent(texts[i]);
            return contents;
        }

        static int PopupInternal(Rect position, GUIContent label, int selectedIndex, GUIContent[] displayedOptions, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_PopupHash, FocusType.Keyboard, position);
            if (label != null) position = PrefixLabel(position, id, label);
            return DoPopup(position, id, selectedIndex, displayedOptions, null, style);
        }

        /// <summary>The popup control <paramref name="controlID"/> at <paramref name="position"/>:
        /// the choice its menu sent back, else <paramref name="selected"/>.</summary>
        internal static int DoPopup(Rect position, int controlID, int selected, GUIContent[] popupValues, GUIStyle style) =>
            DoPopup(position, controlID, selected, popupValues, null, style);

        internal static int DoPopup(Rect position, int controlID, int selected, GUIContent[] popupValues, Func<int, bool> checkEnabled, GUIStyle style)
        {
            selected = PopupCallbackInfo.GetSelectedValueForControl(controlID, selected);
            var current = Event.current;
            switch (current.type)
            {
                case EventType.Repaint:
                    GUIContent content = showMixedValue ? s_MixedValueContent
                        : selected < 0 || selected >= popupValues.Length ? GUIContent.none
                        // (The option's name without a menu shortcut, as the Editor's DoPopup
                        // shows it; drawn, not measured.)
                        : new GUIContent(popupValues[selected]) { text = Unbound.UIElements.UaMenus.ParseMenuName(popupValues[selected].text) };
                    Color contentColor = GUI.contentColor;
                    if (showMixedValue) GUI.contentColor = contentColor * s_MixedValueContentColor;
                    try { style.Draw(position, content, controlID, false, position.Contains(current.mousePosition)); }
                    finally { GUI.contentColor = contentColor; }
                    break;
                case EventType.MouseDown:
                    if (current.button == 0 && position.Contains(current.mousePosition))
                    {
                        ShowPopupMenu(controlID);
                        GUIUtility.keyboardControl = controlID;
                        current.Use();
                    }
                    break;
                case EventType.KeyDown:
                    if (MainActionKeyForControl(current, controlID))
                    {
                        ShowPopupMenu(controlID);
                        current.Use();
                    }
                    break;
            }
            return selected;
        }

        /// <summary>Opens the options' menu for <paramref name="controlID"/>: Unbound shows no
        /// editor menus, so no choice comes back (said once).</summary>
        static void ShowPopupMenu(int controlID)
        {
            PopupCallbackInfo.instance = new PopupCallbackInfo(controlID);
            Stubs.Ignored("EditorGUI.Popup's menu", "is not shown: Unbound shows no editor menus, so the popup keeps its selection");
        }

        /// <summary>Space, Return or Enter without modifiers while the control has keyboard focus.</summary>
        internal static bool MainActionKeyForControl(Event current, int controlID)
        {
            if (GUIUtility.keyboardControl != controlID) return false;
            bool modifiers = current.alt || current.shift || current.command || current.control;
            return current.type == EventType.KeyDown && !modifiers &&
                (current.keyCode == KeyCode.Space || current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter);
        }

        /// <summary>The popup whose menu is open, and the choice its menu sends back.</summary>
        internal sealed partial class PopupCallbackInfo
        {
            public static PopupCallbackInfo instance;
            internal const string kPopupMenuChangedMessage = "PopupMenuChanged";
            readonly int m_ControlID;
            int m_SelectedIndex;

            public PopupCallbackInfo(int controlID) => m_ControlID = controlID;

            /// <summary>The menu's choice (its callback): the selection the popup then gets with
            /// the command (which Unbound, showing no menus, never sends).</summary>
            internal void SetEnumValueDelegate(object userData, string[] options, int selected) => m_SelectedIndex = selected;

            /// <summary>On the menu's command for <paramref name="controlID"/>: its choice (the
            /// GUI changed when it differs, or the value was mixed) and the command used up.</summary>
            public static int GetSelectedValueForControl(int controlID, int selected)
            {
                var current = Event.current;
                if (current.type != EventType.ExecuteCommand || current.commandName != kPopupMenuChangedMessage) return selected;
                if (instance == null)
                {
                    Debug.LogError("Popup menu has no instance");
                    return selected;
                }
                if (instance.m_ControlID == controlID)
                {
                    GUI.changed = showMixedValue || selected != instance.m_SelectedIndex;
                    selected = instance.m_SelectedIndex;
                    instance = null;
                    current.Use();
                }
                return selected;
            }
        }
    }

    public sealed partial class EditorGUILayout
    {
        public static int Popup(int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Popup") ? selectedIndex : Popup(selectedIndex, displayedOptions, EditorStyles.popup, options);

        public static int Popup(int selectedIndex, string[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.Popup(GetControlRect(false, EditorGUIUtility.singleLineHeight, style, options), selectedIndex, displayedOptions, style);

        public static int Popup(int selectedIndex, GUIContent[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Popup") ? selectedIndex : Popup(selectedIndex, displayedOptions, EditorStyles.popup, options);

        public static int Popup(int selectedIndex, GUIContent[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.Popup(GetControlRect(false, EditorGUIUtility.singleLineHeight, style, options), selectedIndex, displayedOptions, style);

        public static int Popup(string label, int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Popup") ? selectedIndex : Popup(label, selectedIndex, displayedOptions, EditorStyles.popup, options);

        public static int Popup(GUIContent label, int selectedIndex, string[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Popup") ? selectedIndex : Popup(label, selectedIndex, displayedOptions, EditorStyles.popup, options);

        public static int Popup(string label, int selectedIndex, string[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.Popup(GetControlRect(true, EditorGUIUtility.singleLineHeight, style, options), label, selectedIndex, displayedOptions, style);

        public static int Popup(GUIContent label, int selectedIndex, GUIContent[] displayedOptions, params GUILayoutOption[] options) =>
            EditorGUI.Unmeasured("UnityEditor.EditorGUILayout.Popup") ? selectedIndex : Popup(label, selectedIndex, displayedOptions, EditorStyles.popup, options);

        public static int Popup(GUIContent label, int selectedIndex, GUIContent[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.Popup(GetControlRect(true, EditorGUIUtility.singleLineHeight, style, options), label, selectedIndex, displayedOptions, style);

        internal static int Popup(GUIContent label, int selectedIndex, string[] displayedOptions, GUIStyle style, params GUILayoutOption[] options) =>
            EditorGUI.Popup(GetControlRect(true, EditorGUIUtility.singleLineHeight, style, options), label, selectedIndex, displayedOptions, style);
    }
}
