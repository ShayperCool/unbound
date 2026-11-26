using System;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// EditorGUI controls of Unity's C# (EditorGUI.cs, EditorGUILayout.cs, GUI/FoldoutHeader.cs of
    /// 6000.0): the progress bar, the popups, int popups and delayed fields of a property, the
    /// drop-down button, foldout header groups, disabled groups, the link button, the logarithmic
    /// int slider. Measured against the Unity 6000.0.71f1 Editor in Play mode
    /// (tests/projects/editorcontrolsb2): values, control ids, GUI.changed, layout rects, the
    /// exceptions outside OnGUI and scripted mouse and key events.
    /// </summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_ProgressBarHash = "s_ProgressBarHash".GetHashCode();
        static readonly int s_FoldoutHeaderHash = "FoldoutHeader".GetHashCode();
        static int s_FoldoutHeaderGroupActive;

        /// <summary>Whether the controls of this file are measured for the release family; other
        /// families keep the unmeasured answer (not implemented, said once, the value given back).</summary>
        internal static bool MiscMeasured(string member)
        {
            if (Compat.EditorValueFieldsMeasured) return true;
            Stubs.NotImplemented(member);
            return false;
        }

        public static void ProgressBar(Rect position, float value, string text)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.ProgressBar")) return;
            ProgressBar(position, value, GUIContent.Temp(text), false, EditorStyles.progressBarBack, EditorStyles.progressBarBar, EditorStyles.progressBarText);
        }

        /// <summary>A progress bar: the background, the bar's part (an animated bar for -1) and the
        /// text, shortened with "..." in the middle to fit; true for a mouse down on it.</summary>
        internal static bool ProgressBar(Rect position, float value, GUIContent content, bool textOnBar,
            GUIStyle progressBarBackgroundStyle, GUIStyle progressBarStyle, GUIStyle progressBarTextStyle)
        {
            // Outside OnGUI there is no event: Unity's read of it throws (measured).
            RequireGUI();
            bool mouseHover = position.Contains(Event.current.mousePosition);
            int id = GUIUtility.GetControlID(s_ProgressBarHash, FocusType.Keyboard, position);
            Event evt = Event.current;
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (mouseHover)
                    {
                        Event.current.Use();
                        return true;
                    }
                    break;
                case EventType.Repaint:
                    progressBarBackgroundStyle.Draw(position, mouseHover, false, false, false);
                    if (value > 0.0f)
                    {
                        value = Mathf.Clamp01(value);
                        var barRect = new Rect(position);
                        barRect.width *= value;
                        if (barRect.width >= 1f)
                            progressBarStyle.Draw(barRect, GUIContent.none, mouseHover, false, false, false);
                    }
                    else if (value == -1.0f)
                    {
                        float barWidth = position.width * 0.2f;
                        float halfBarWidth = barWidth / 2.0f;
                        float cos = Mathf.Cos((float)EditorApplication.timeSinceStartup * 2f);
                        float rb = position.x + halfBarWidth;
                        float re = position.xMax - halfBarWidth;
                        float scale = (re - rb) / 2f;
                        float cursor = scale * cos;
                        var barRect = new Rect(position.x + cursor + scale, position.y, barWidth, position.height);
                        progressBarStyle.Draw(barRect, GUIContent.none, mouseHover, false, false, false);
                    }

                    var contentTextToDisplay = content;
                    var contentWidth = progressBarTextStyle.CalcSize(contentTextToDisplay).x;
                    if (contentWidth > position.width)
                    {
                        int numberOfVisibleCharacters = (int)((position.width / contentWidth) * content.text.Length);
                        // Shortened from both sides until it fits (big outer and small inner characters).
                        int i = 0;
                        do
                        {
                            int numberOfVisibleCharactersBySide = numberOfVisibleCharacters / 2 - 2 - i;
                            contentTextToDisplay.text = content.text.Substring(0, numberOfVisibleCharactersBySide) + "..." + content.text.Substring(content.text.Length - numberOfVisibleCharactersBySide, numberOfVisibleCharactersBySide);
                            ++i;
                        }
                        while (progressBarTextStyle.CalcSize(contentTextToDisplay).x > position.width);
                    }
                    progressBarTextStyle.Draw(position, contentTextToDisplay, mouseHover, false, false, false);
                    break;
            }
            return false;
        }

        /// <summary>A popup of an int property (the property's value is the index).</summary>
        internal static void Popup(Rect position, SerializedProperty property, GUIContent[] displayedOptions, GUIContent label)
        {
            label = BeginProperty(position, label, property);
            BeginChangeCheck();
            int idx = Popup(position, label, property.hasMultipleDifferentValues ? -1 : property.intValue, displayedOptions);
            if (EndChangeCheck())
                property.intValue = idx;
            EndProperty();
        }

        public static void IntPopup(Rect position, SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues) =>
            IntPopup(position, property, displayedOptions, optionValues, null);

        public static void IntPopup(Rect position, SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues, GUIContent label)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.IntPopup")) return;
            IntPopupInternal(position, property, displayedOptions, optionValues, label);
        }

        internal static void IntPopupInternal(Rect position, SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues, GUIContent label)
        {
            label = BeginProperty(position, label, property);
            BeginChangeCheck();
            int newValue = IntPopupInternal(position, label, property.intValue, displayedOptions, optionValues, EditorStyles.popup);
            if (EndChangeCheck())
                property.intValue = newValue;
            EndProperty();
        }

        public static void DelayedIntField(Rect position, SerializedProperty property) => DelayedIntField(position, property, null);

        public static void DelayedIntField(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.DelayedIntField")) return;
            DelayedIntFieldInternal(position, property, label);
        }

        internal static void DelayedIntFieldInternal(Rect position, SerializedProperty property, GUIContent label)
        {
            label = BeginProperty(position, label, property);
            BeginChangeCheck();
            int newValue = DelayedIntFieldInternal(position, label, property.intValue, EditorStyles.numberField);
            if (EndChangeCheck())
                property.intValue = newValue;
            EndProperty();
        }

        public static void DelayedFloatField(Rect position, SerializedProperty property) => DelayedFloatField(position, property, null);

        public static void DelayedFloatField(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.DelayedFloatField")) return;
            DelayedFloatFieldInternal(position, property, label);
        }

        internal static void DelayedFloatFieldInternal(Rect position, SerializedProperty property, GUIContent label)
        {
            label = BeginProperty(position, label, property);
            BeginChangeCheck();
            float newValue = DelayedFloatFieldInternal(position, label, property.floatValue, EditorStyles.numberField);
            if (EndChangeCheck())
                property.floatValue = newValue;
            EndProperty();
        }

        public static void DelayedTextField(Rect position, SerializedProperty property) => DelayedTextField(position, property, null);

        public static void DelayedTextField(Rect position, SerializedProperty property, GUIContent label)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.DelayedTextField")) return;
            DelayedTextFieldHelper(position, property, label, EditorStyles.textField);
        }

        internal static void DelayedTextFieldHelper(Rect position, SerializedProperty property, GUIContent label, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_TextFieldHash, FocusType.Keyboard, position);
            DelayedTextFieldInternal(position, id, property, null, label, style);
        }

        internal static void DelayedTextFieldInternal(Rect position, int id, SerializedProperty property, string allowedLetters, GUIContent label, GUIStyle style)
        {
            label = BeginProperty(position, label, property);
            BeginChangeCheck();
            string newValue = DelayedTextFieldInternal(position, id, label, property.stringValue, allowedLetters, style);
            if (EndChangeCheck())
                property.stringValue = newValue;
            EndProperty();
        }

        public static bool DropdownButton(Rect position, GUIContent content, FocusType focusType)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.DropdownButton")) return false;
            // Outside OnGUI there is no event: Unity's read of it throws (measured).
            RequireGUI();
            return DropdownButton(position, content, focusType, EditorStyles.miniPullDown);
        }

        public static bool BeginFoldoutHeaderGroup(Rect position, bool foldout, string content, GUIStyle style = null, Action<Rect> menuAction = null, GUIStyle menuIcon = null) =>
            BeginFoldoutHeaderGroup(position, foldout, GUIContent.Temp(content), style, menuAction, menuIcon);

        /// <summary>A foldout header: a toggle drawn in the header style that the arrow keys fold
        /// when it has the focus, with a menu icon calling <paramref name="menuAction"/> on a click;
        /// a header inside another shows an error box instead and answers false.</summary>
        public static bool BeginFoldoutHeaderGroup(Rect position, bool foldout, GUIContent content, GUIStyle style = null, Action<Rect> menuAction = null, GUIStyle menuIcon = null)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.BeginFoldoutHeaderGroup")) return foldout;
            // Without the inspector's default margins.
            if (EditorGUIUtility.hierarchyMode)
            {
                position.xMin -= EditorStyles.inspectorDefaultMargins.padding.left - EditorStyles.inspectorDefaultMargins.padding.right;
                position.xMax += EditorStyles.inspectorDefaultMargins.padding.right;
            }
            if (style == null)
                style = EditorStyles.foldoutHeader;
            s_FoldoutHeaderGroupActive++;
            if (s_FoldoutHeaderGroupActive > 1)
            {
                HelpBox(position, L10n.Tr("You can't nest Foldout Headers, end it with EndFoldoutHeaderGroup."), MessageType.Error);
                return false;
            }
            // Outside OnGUI there is no event: Unity's read of it throws, the group counted (measured).
            RequireGUI();
            const float iconSize = 16;
            Rect menuRect = new Rect
            {
                x = position.xMax - style.padding.right - iconSize,
                y = position.y + style.padding.top,
                size = Vector2.one * iconSize,
            };
            bool menuIconHover = menuRect.Contains(Event.current.mousePosition);
            bool menuIconActive = menuIconHover && Event.current.type == EventType.MouseDown && Event.current.button == 0;
            if (menuAction != null && menuIconActive)
            {
                menuAction.Invoke(menuRect);
                Event.current.Use();
            }
            int id = GUIUtility.GetControlID(s_FoldoutHeaderHash, FocusType.Keyboard, position);
            if (Event.current.type == EventType.KeyDown && GUIUtility.keyboardControl == id)
            {
                KeyCode kc = Event.current.keyCode;
                if (kc == KeyCode.LeftArrow && foldout || kc == KeyCode.RightArrow && !foldout)
                {
                    foldout = !foldout;
                    GUI.changed = true;
                    Event.current.Use();
                }
            }
            else
                foldout = EditorGUIInternal.DoToggleForward(position, id, foldout, content, style);

            if (menuAction != null && Event.current.type == EventType.Repaint)
            {
                if (menuIcon == null) menuIcon = EditorStyles.foldoutHeaderIcon;
                menuIcon.Draw(menuRect, menuIconHover, menuIconActive, false, false);
            }
            return foldout;
        }

        public static void EndFoldoutHeaderGroup()
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.EndFoldoutHeaderGroup")) return;
            s_FoldoutHeaderGroupActive--;
        }

        /// <summary>Disables the controls until <see cref="EndDisabled"/> (GUI.enabled kept on a stack).</summary>
        internal static void BeginDisabled(bool disabled)
        {
            s_EnabledStack.Push(GUI.enabled);
            GUI.enabled &= !disabled;
        }

        internal static void EndDisabled()
        {
            // The stack may have been cleared.
            if (s_EnabledStack.Count > 0)
                GUI.enabled = s_EnabledStack.Pop();
        }

        /// <summary>Whether a text field is being edited.</summary>
        internal static bool IsEditingTextField() => RecycledTextEditor.s_ActuallyEditing && activeEditor != null;

        public static int LogarithmicIntSlider(Rect position, string label, int sliderValue, int leftValue, int rightValue, int logbase, int textFieldMin, int textFieldMax) =>
            LogarithmicIntSlider(position, EditorGUIUtility.TempContent(label), sliderValue, leftValue, rightValue, logbase, textFieldMin, textFieldMax);

        public static int LogarithmicIntSlider(Rect position, GUIContent label, int sliderValue, int leftValue, int rightValue, int logbase, int textFieldMin, int textFieldMax)
        {
            if (!MiscMeasured("UnityEditor.EditorGUI.LogarithmicIntSlider")) return sliderValue;
            return LogarithmicIntSlider(position, label, sliderValue, leftValue, rightValue, EditorStyles.numberField, logbase, textFieldMin, textFieldMax);
        }

        /// <summary>An int slider on a logarithmic scale (Unity's C# passes no text field style on).</summary>
        internal static int LogarithmicIntSlider(Rect position, GUIContent label, int sliderValue, int leftValue, int rightValue, GUIStyle textfieldStyle, int logbase, int textFieldMin, int textFieldMax)
        {
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            Rect controlRect = PrefixLabel(position, id, label);
            Rect dragZone = LabelHasContent(label) ? DragZoneRect(position) : new Rect();
            return Mathf.RoundToInt(DoSlider(controlRect, dragZone, id, sliderValue, leftValue, rightValue, kIntFieldFormatString, textFieldMin, textFieldMax, power: 1, logbase: logbase));
        }
    }

    public sealed partial class EditorGUILayout
    {
        /// <summary>The rect of the last control the layout forms of EditorGUI.MiscControls.cs made
        /// (Unity's field, which its other layout forms set too; Unbound's do not yet).</summary>
        internal static Rect s_LastRect;

        public static void IntPopup(SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues, params GUILayoutOption[] options) =>
            IntPopup(property, displayedOptions, optionValues, null, options);

        public static void IntPopup(SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues, GUIContent label, params GUILayoutOption[] options)
        {
            if (!EditorGUI.MiscMeasured("UnityEditor.EditorGUILayout.IntPopup")) return;
            Rect r = s_LastRect = GetControlRect(true, EditorGUI.kSingleLineHeight, EditorStyles.popup, options);
            EditorGUI.IntPopup(r, property, displayedOptions, optionValues, label);
        }

        [Obsolete("This function is obsolete and the style is not used.")]
        public static void IntPopup(SerializedProperty property, GUIContent[] displayedOptions, int[] optionValues, GUIContent label, GUIStyle style, params GUILayoutOption[] options) =>
            IntPopup(property, displayedOptions, optionValues, label, options);

        public static void DelayedIntField(SerializedProperty property, params GUILayoutOption[] options) => DelayedIntField(property, null, options);

        public static void DelayedIntField(SerializedProperty property, GUIContent label, params GUILayoutOption[] options)
        {
            if (!EditorGUI.MiscMeasured("UnityEditor.EditorGUILayout.DelayedIntField")) return;
            Rect r = s_LastRect = GetControlRect(EditorGUI.LabelHasContent(label), EditorGUI.kSingleLineHeight, EditorStyles.numberField, options);
            EditorGUI.DelayedIntField(r, property, label);
        }

        public static bool DropdownButton(GUIContent content, FocusType focusType, params GUILayoutOption[] options)
        {
            if (!EditorGUI.MiscMeasured("UnityEditor.EditorGUILayout.DropdownButton")) return false;
            return DropdownButton(content, focusType, "MiniPullDown", options);
        }

        public static bool DropdownButton(GUIContent content, FocusType focusType, GUIStyle style, params GUILayoutOption[] options)
        {
            if (!EditorGUI.MiscMeasured("UnityEditor.EditorGUILayout.DropdownButton")) return false;
            s_LastRect = GUILayoutUtility.GetRect(content, style, options);
            return EditorGUI.DropdownButton(s_LastRect, content, focusType, style);
        }

        public static bool BeginFoldoutHeaderGroup(bool foldout, string content, GUIStyle style = null, Action<Rect> menuAction = null, GUIStyle menuIcon = null) =>
            BeginFoldoutHeaderGroup(foldout, GUIContent.Temp(content), style, menuAction, menuIcon);

        public static bool BeginFoldoutHeaderGroup(bool foldout, GUIContent content, GUIStyle style = null, Action<Rect> menuAction = null, GUIStyle menuIcon = null)
        {
            if (!EditorGUI.MiscMeasured("UnityEditor.EditorGUILayout.BeginFoldoutHeaderGroup")) return foldout;
            if (style == null) style = EditorStyles.foldoutHeader;
            var backgroundRect = GUILayoutUtility.GetRect(content, style);
            return EditorGUI.BeginFoldoutHeaderGroup(backgroundRect, foldout, content, style, menuAction, menuIcon);
        }

        public static void EndFoldoutHeaderGroup() => EditorGUI.EndFoldoutHeaderGroup();

        public static bool LinkButton(string label, params GUILayoutOption[] options) => LinkButton(EditorGUIUtility.TempContent(label), options);

        /// <summary>A button drawn as a link: underlined (a Handles line, which Unbound does not
        /// draw), with the link cursor.</summary>
        public static bool LinkButton(GUIContent label, params GUILayoutOption[] options)
        {
            if (!EditorGUI.MiscMeasured("UnityEditor.EditorGUILayout.LinkButton")) return false;
            var position = s_LastRect = GUILayoutUtility.GetRect(label, EditorStyles.linkLabel, options);
            Handles.color = EditorStyles.linkLabel.normal.textColor;
            Handles.DrawLine(new Vector3(position.xMin + EditorStyles.linkLabel.padding.left, position.yMax), new Vector3(position.xMax - EditorStyles.linkLabel.padding.right, position.yMax));
            if (Event.current.type == EventType.Repaint)
                Stubs.Ignored("UnityEditor.EditorGUILayout.LinkButton underline", "the link's underline is not drawn (Unbound draws no handles)");
            Handles.color = Color.white;
            EditorGUIUtility.AddCursorRect(position, MouseCursor.Link);
            return GUI.Button(position, label, EditorStyles.linkLabel);
        }
    }

    public sealed partial class EditorGUIUtility
    {
        /// <summary>An ExecuteCommand event of a command name (the other values a new event's; Unity
        /// also copies the current input state into it: measured only outside OnGUI, where it has
        /// none, tests/projects/editorcontrolsb2 commandEvent).</summary>
        public static Event CommandEvent(string commandName) => new Event { type = EventType.ExecuteCommand, commandName = commandName };

        /// <summary>Unity's mouse jumping for a drag past the screen's edge: Unbound's mouse does not
        /// wrap (the Editor answers nothing either way, measured).</summary>
        public static void SetWantsMouseJumping(int wantz) =>
            Stubs.Ignored("UnityEditor.EditorGUIUtility.SetWantsMouseJumping", "the cursor does not wrap at the screen's edges while dragging");

        /// <summary>The object the object picker shows: none, as Unbound shows no picker window
        /// (the Editor's answer without an open picker, measured).</summary>
        [Unbound.EditorFaithful("measured: tests/projects/editorcontrolsb2 picker (no open picker)")]
        public static Object GetObjectPickerObject() => null;

        /// <summary>The control id the open object picker belongs to: 0 without one (measured).</summary>
        [Unbound.EditorFaithful("measured: tests/projects/editorcontrolsb2 picker (no open picker)")]
        public static int GetObjectPickerControlID() => 0;

        public static void ShowObjectPicker<T>(Object obj, bool allowSceneObjects, string searchFilter, int controlID) where T : Object =>
            Stubs.Ignored("UnityEditor.EditorGUIUtility.ShowObjectPicker", "the object picker window is not shown: Unbound has no editor windows");
    }
}
