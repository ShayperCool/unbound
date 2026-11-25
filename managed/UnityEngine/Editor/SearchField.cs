using UnityEngine;

namespace UnityEditor.IMGUI.Controls
{
    // Unity 6000.0.71f1's C# (Editor/Mono/GUI/SearchField.cs).
    public partial class SearchField
    {
        int m_ControlID;
        bool m_WantsFocus;
        bool m_AutoSetFocusOnFindCommand = true;
        const float kMinWidth = 36f;
        const float kMaxWidth = 10000000f;
        const float kMinToolbarWidth = 29f;
        const float kMaxToolbarWidth = 200f;

        public delegate void SearchFieldCallback();
        public event SearchFieldCallback downOrUpArrowKeyPressed;

        public SearchField()
        {
            m_ControlID = GUIUtility.GetPermanentControlID();
        }

        public int searchFieldControlID
        {
            get { return m_ControlID; }
            set { m_ControlID = value; }
        }

        public bool autoSetFocusOnFindCommand
        {
            get { return m_AutoSetFocusOnFindCommand; }
            set { m_AutoSetFocusOnFindCommand = value; }
        }

        public void SetFocus()
        {
            m_WantsFocus = true;
        }

        public bool HasFocus()
        {
            return GUIUtility.keyboardControl == m_ControlID;
        }

        public string OnGUI(Rect rect, string text, GUIStyle style, GUIStyle cancelButtonStyle, GUIStyle emptyCancelButtonStyle)
        {
            CommandEventHandling();

            FocusAndKeyHandling();

            float cancelButtonWidth = cancelButtonStyle.fixedWidth;

            // Cancel button
            Rect buttonRect = rect;
            buttonRect.x += rect.width - cancelButtonWidth;
            buttonRect.width = cancelButtonWidth;
            if (Event.current.type == EventType.MouseUp && buttonRect.Contains(Event.current.mousePosition))
            {
                text = string.Empty;
                GUIUtility.keyboardControl = 0;
                GUI.changed = true;
            }

            // Search field
            Rect textRect = rect;
            text = EditorGUI.TextFieldInternal(m_ControlID, textRect, text, style, cancelButtonStyle);

            GUI.Button(buttonRect, GUIContent.none,
                !string.IsNullOrEmpty(text) ? cancelButtonStyle : emptyCancelButtonStyle);
            return text;
        }

        // The forms with the Editor skin's styles: only this release family's styles are measured;
        // another family's report themselves and keep the text given (taking no layout space), as
        // EditorGUI.Popup's do.
        public string OnGUI(Rect rect, string text)
        {
            if (EditorGUI.Unmeasured("UnityEditor.IMGUI.Controls.SearchField.OnGUI")) return text;
            return OnGUI(rect, text, EditorStyles.searchField, EditorStyles.searchFieldCancelButton, EditorStyles.searchFieldCancelButtonEmpty);
        }

        public string OnGUI(string text, params GUILayoutOption[] options)
        {
            if (EditorGUI.Unmeasured("UnityEditor.IMGUI.Controls.SearchField.OnGUI")) return text;
            Rect rect = GUILayoutUtility.GetRect(kMinWidth, kMaxWidth, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorStyles.searchField, options);
            return OnGUI(rect, text);
        }

        public string OnToolbarGUI(Rect rect, string text)
        {
            if (EditorGUI.Unmeasured("UnityEditor.IMGUI.Controls.SearchField.OnToolbarGUI")) return text;
            return OnGUI(rect, text, EditorStyles.toolbarSearchField, EditorStyles.toolbarSearchFieldCancelButton, EditorStyles.toolbarSearchFieldCancelButtonEmpty);
        }

        public string OnToolbarGUI(string text, params GUILayoutOption[] options)
        {
            if (EditorGUI.Unmeasured("UnityEditor.IMGUI.Controls.SearchField.OnToolbarGUI")) return text;
            Rect rect = GUILayoutUtility.GetRect(kMinToolbarWidth, kMaxToolbarWidth, EditorGUI.kSingleLineHeight, EditorGUI.kSingleLineHeight, EditorStyles.toolbarSearchField, options);
            return OnToolbarGUI(rect, text);
        }

        void FocusAndKeyHandling()
        {
            Event evt = Event.current;
            if (m_WantsFocus && evt.type == EventType.Repaint)
            {
                GUIUtility.keyboardControl = m_ControlID;
                EditorGUIUtility.editingTextField = true;
                m_WantsFocus = false;
            }

            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow) &&
                GUIUtility.keyboardControl == m_ControlID && GUIUtility.hotControl == 0)
            {
                if (downOrUpArrowKeyPressed != null)
                {
                    downOrUpArrowKeyPressed();
                    evt.Use();
                }
            }
        }

        void CommandEventHandling()
        {
            // (Outside OnGUI there is no event: Mono's NullReferenceException, as the Editor's.)
            EditorGUI.RequireGUI();
            Event evt = Event.current;

            if (evt.type != EventType.ExecuteCommand && evt.type != EventType.ValidateCommand)
                return;

            if (m_AutoSetFocusOnFindCommand && evt.commandName == EventCommandNames.Find)
            {
                if (evt.type == EventType.ExecuteCommand)
                    SetFocus();
                evt.Use();
            }
        }
    }
}
