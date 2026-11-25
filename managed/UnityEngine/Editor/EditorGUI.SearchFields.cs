using System;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    // Unity 6000.0.71f1's C# (EditorGUI.cs TextFieldInternal with a cancel button,
    // ToolbarSearchField, SearchField; EditorGUILayout.cs ToolbarSearchField).
    public sealed partial class EditorGUI
    {
        static readonly int s_SearchFieldHash = "EditorSearchField".GetHashCode();

        internal static string TextFieldInternal(int id, Rect position, string text, GUIStyle style, GUIStyle cancelButtonStyle) =>
            DoTextField(s_RecycledEditor, id, IndentedRect(position), text, style, null, out _, false, false, false, cancelButtonStyle);

        internal static string ToolbarSearchField(Rect position, string text, bool showWithPopupArrow)
        {
            int id = GUIUtility.GetControlID(s_SearchFieldHash, FocusType.Keyboard, position);
            return ToolbarSearchField(id, position, text, showWithPopupArrow);
        }

        internal static string ToolbarSearchField(int id, Rect position, string text, bool showWithPopupArrow) =>
            ToolbarSearchField(
                id,
                position,
                text,
                showWithPopupArrow ? EditorStyles.toolbarSearchFieldPopup : EditorStyles.toolbarSearchField,
                text != "" ? EditorStyles.toolbarSearchFieldCancelButton : EditorStyles.toolbarSearchFieldCancelButtonEmpty);

        internal static string ToolbarSearchField(int id, Rect position, string text, GUIStyle searchFieldStyle, GUIStyle cancelButtonStyle)
        {
            Rect textRect = position;
            const float k_CancelButtonWidth = 14f;

            Rect buttonRect = position;
            buttonRect.x += position.width - k_CancelButtonWidth;
            buttonRect.width = k_CancelButtonWidth;

            // (Outside OnGUI there is no event: Mono's NullReferenceException, as the Editor's.)
            RequireGUI();
            if (!string.IsNullOrEmpty(text))
                EditorGUIUtility.AddCursorRect(buttonRect, MouseCursor.Arrow);
            if (Event.current.type == EventType.MouseUp && buttonRect.Contains(Event.current.mousePosition))
            {
                s_RecycledEditor.text = text = "";
                GUI.changed = true;
            }
            text = DoTextField(s_RecycledEditor, id, textRect, text, searchFieldStyle, null, out _, false, false, false, cancelButtonStyle, true);
            GUI.Button(buttonRect, GUIContent.none, cancelButtonStyle);

            return text;
        }

        internal static string ToolbarSearchField(Rect position, string[] searchModes, ref int searchMode, string text)
        {
            int id = GUIUtility.GetControlID(s_SearchFieldHash, FocusType.Keyboard, position);
            return ToolbarSearchField(id, position, searchModes, ref searchMode, text);
        }

        internal static string ToolbarSearchField(int id, Rect position, string[] searchModes, ref int searchMode, string text) =>
            ToolbarSearchField(
                id,
                position,
                searchModes,
                ref searchMode,
                text,
                EditorStyles.toolbarSearchFieldPopup,
                EditorStyles.toolbarSearchField,
                text != "" ? EditorStyles.toolbarSearchFieldCancelButton : EditorStyles.toolbarSearchFieldCancelButtonEmpty);

        internal static string ToolbarSearchField(int id, Rect position, string[] searchModes, ref int searchMode, string text, GUIStyle searchFieldWithPopupStyle, GUIStyle searchFieldNoPopupStyle, GUIStyle cancelButtonStyle)
        {
            bool hasPopup = searchModes != null;
            if (hasPopup)
            {
                RequireGUI();
                searchMode = PopupCallbackInfo.GetSelectedValueForControl(id, searchMode);

                Rect popupPosition = position;
                popupPosition.width = 20;

                if (Event.current.type == EventType.MouseDown && popupPosition.Contains(Event.current.mousePosition))
                {
                    // (Unity opens the search modes' menu here; Unbound shows no editor menus, so
                    // no mode comes back.)
                    PopupCallbackInfo.instance = new PopupCallbackInfo(id);
                    Stubs.Ignored("EditorGUI.ToolbarSearchField's search mode menu", "is not shown: Unbound shows no editor menus, so the search mode stays");

                    if (s_RecycledEditor.IsEditingControl(id))
                        Event.current.Use();
                }
            }

            text = ToolbarSearchField(id, position, text, hasPopup ? searchFieldWithPopupStyle : searchFieldNoPopupStyle, cancelButtonStyle);

            if (hasPopup && text == "" && !s_RecycledEditor.IsEditingControl(id) && Event.current.type == EventType.Repaint)
            {
                using (new DisabledScope(true))
                {
                    var placeHolderTextRect = EditorStyles.toolbarSearchFieldPopup.padding.Remove(new Rect(position.x, position.y, position.width,
                        EditorStyles.toolbarSearchFieldPopup.fixedHeight > 0 ? EditorStyles.toolbarSearchFieldPopup.fixedHeight : position.height));
                    var oldFontSize = EditorStyles.label.fontSize;

                    EditorStyles.label.fontSize = EditorStyles.toolbarSearchFieldPopup.fontSize;
                    EditorStyles.label.Draw(placeHolderTextRect, EditorGUIUtility.TempContent(searchModes[searchMode]), false, false, false, false);
                    EditorStyles.label.fontSize = oldFontSize;
                }
            }

            return text;
        }

        internal static string SearchField(Rect position, string text)
        {
            int id = GUIUtility.GetControlID(s_SearchFieldHash, FocusType.Keyboard, position);
            RequireGUI();
            Rect textRect = position;
            textRect.width -= 15;
            text = DoTextField(s_RecycledEditor, id, textRect, text, EditorStyles.searchField, null, out _, false, false, false);
            Rect buttonRect = position;
            buttonRect.x += position.width - 15;
            buttonRect.width = 15;
            if (GUI.Button(buttonRect, GUIContent.none, text != "" ? EditorStyles.searchFieldCancelButton : EditorStyles.searchFieldCancelButtonEmpty) && text != "")
            {
                s_RecycledEditor.text = text = "";
                GUIUtility.keyboardControl = 0;
            }
            return text;
        }
    }

    public sealed partial class EditorGUILayout
    {
        internal static string ToolbarSearchField(string text, params GUILayoutOption[] options)
        {
            Rect r = GUILayoutUtility.GetRect(0, kLabelFloatMaxW * 1.5f, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorStyles.toolbarSearchField, options);
            int i = 0;
            return EditorGUI.ToolbarSearchField(r, null, ref i, text);
        }

        internal static string ToolbarSearchField(string text, string[] searchModes, ref int searchMode, params GUILayoutOption[] options)
        {
            Rect r = GUILayoutUtility.GetRect(0, kLabelFloatMaxW * 1.5f, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorStyles.toolbarSearchField, options);
            return EditorGUI.ToolbarSearchField(r, searchModes, ref searchMode, text);
        }
    }
}
