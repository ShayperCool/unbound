using System;
using System.Globalization;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// EditorGUI's text, password, number and delayed fields and its sliders, as Unity 6000.0's C#
    /// does them: one recycled text editor edits the field with keyboard focus, number fields parse
    /// as they are typed (expressions when committed), delayed fields return their text only when
    /// editing ends, a field's label drags its value, and a slider pairs GUI.Slider with a number
    /// field (measured: tests/projects/editorvaluefields, Layout and Repaint answers, control ids,
    /// GUI.changed, layout rects and a scripted mouse and keyboard sequence). Other families report
    /// these fields not implemented and return the value they are given.
    /// </summary>
    public sealed partial class EditorGUI
    {
        static readonly int s_TextFieldHash = "EditorTextField".GetHashCode();
        static readonly int s_TextAreaHash = "EditorTextField".GetHashCode();
        static readonly int s_PasswordFieldHash = "PasswordField".GetHashCode();
        static readonly int s_DelayedTextFieldHash = "DelayedEditorTextField".GetHashCode();
        static readonly int s_SliderHash = "EditorSlider".GetHashCode();
        static readonly int s_SliderKnobHash = "EditorSliderKnob".GetHashCode();

        const float kDragDeadzone = 16;
        internal const float kSpacing = 5;
        internal const float kSliderMinW = 50;
        internal const float kSliderMaxW = 100;
        const string k_MultiEditValueString = "<>";

        internal static string kFloatFieldFormatString = UINumericFieldsUtils.k_FloatFieldFormatString;
        internal static string kDoubleFieldFormatString = UINumericFieldsUtils.k_DoubleFieldFormatString;
        internal static string kIntFieldFormatString = UINumericFieldsUtils.k_IntFieldFormatString;
        internal static readonly string s_AllowedCharactersForFloat = UINumericFieldsUtils.k_AllowedCharactersForFloat;
        internal static readonly string s_AllowedCharactersForInt = UINumericFieldsUtils.k_AllowedCharactersForInt;

        internal static RecycledTextEditor s_RecycledEditorInternal;
        internal static DelayedTextEditor s_DelayedTextEditorInternal;
        internal static RecycledTextEditor s_RecycledEditor => s_RecycledEditorInternal ??= new RecycledTextEditor();
        internal static DelayedTextEditor s_DelayedTextEditor => s_DelayedTextEditorInternal ??= new DelayedTextEditor();
        internal static RecycledTextEditor activeEditor;

        internal static string s_OriginalText = "";
        internal static string s_RecycledCurrentEditingString;
        internal static bool s_DragToPosition = false;
        internal static bool s_Dragged = false;
        internal static bool s_SelectAllOnMouseUp = true;

        enum DragCandidateState { NotDragging, InitiatedDragging, CurrentlyDragging }
        static DragCandidateState s_DragCandidateState = DragCandidateState.NotDragging;
        static double s_DragStartValue = 0;
        static long s_DragStartIntValue = 0;
        static Vector2 s_DragStartPos;
        static double s_DragSensitivity = 0;

        // The default styles, read only on the measured family: elsewhere the fields report
        // themselves not implemented before they would use a style.
        internal static GUIStyle NumberStyle => Compat.EditorValueFieldsMeasured ? EditorStyles.numberField : null;
        internal static GUIStyle TextStyle => Compat.EditorValueFieldsMeasured ? EditorStyles.textField : null;

        static bool FieldsMeasured(string member, out bool measured)
        {
            measured = Compat.EditorValueFieldsMeasured;
            if (!measured) Stubs.NotImplemented("UnityEditor.EditorGUI." + member);
            return measured;
        }

        internal partial class RecycledTextEditor : TextEditor
        {
            internal static bool s_ActuallyEditing = false;
            internal static bool s_EditingWasCompleted = false;
            internal static bool s_AllowContextCutOrPaste = true;

            IMECompositionMode m_IMECompositionModeBackup;

            // Unity asks the view being drawn whether it has focus; a game's OnGUI draws in the
            // Game view, which has it while the application has.
            internal bool IsEditingControl(int id) =>
                GUIUtility.keyboardControl == id && controlID == id && s_ActuallyEditing && EditorGUIUtility.HasCurrentWindowKeyFocus();

            public virtual void BeginEditing(int id, string newText, Rect position, GUIStyle style, bool multiline, bool passwordField)
            {
                if (IsEditingControl(id)) return;
                activeEditor?.EndEditing();
                activeEditor = this;
                controlID = id;
                text = s_OriginalText = newText;
                isMultiline = multiline;
                this.position = position;
                this.style = style;
                isPasswordField = passwordField;
                s_ActuallyEditing = true;
                scrollOffset = Vector2.zero;
                UnityEditor.Undo.IncrementCurrentGroup();
                // Composition (an input method) is on while a field is edited.
                m_IMECompositionModeBackup = GUIUtility.imeCompositionMode;
                GUIUtility.imeCompositionMode = IMECompositionMode.On;
            }

            public virtual void EndEditing()
            {
                if (activeEditor == this) activeEditor = null;
                controlID = 0;
                SelectNone();
                s_ActuallyEditing = false;
                s_EditingWasCompleted = false;
                s_AllowContextCutOrPaste = true;
                UnityEditor.Undo.IncrementCurrentGroup();
                GUIUtility.imeCompositionMode = m_IMECompositionModeBackup;
            }
        }

        /// <summary>A delayed field's editor: when the field loses keyboard focus, the Editor's view
        /// sends itself a commit command that hands the field its text. A game's OnGUI gets none
        /// (tests/projects/editorvaluefields delayedAway.*: a field left by a click keeps its value),
        /// and Unbound has no editor view that would send it, so only the command commits.</summary>
        internal sealed partial class DelayedTextEditor : RecycledTextEditor
        {
            int controlThatHadFocus = 0, messageControl = 0;
            internal string controlThatHadFocusValue = "";
            const string CommitCommand = "DelayedControlShouldCommit";

            public void BeginGUI()
            {
                if (GUIUtility.keyboardControl == controlID)
                {
                    controlThatHadFocus = GUIUtility.keyboardControl;
                    controlThatHadFocusValue = text;
                }
                else controlThatHadFocus = 0;
            }

            public void EndGUI(EventType type)
            {
                if (controlThatHadFocus != 0 && controlThatHadFocus != GUIUtility.keyboardControl)
                {
                    messageControl = controlThatHadFocus;
                    controlThatHadFocus = 0;
                }
            }

            /// <summary>The Editor commits a field whose editing ends outside any event through the
            /// view that had focus; there is none here.</summary>
            public override void EndEditing() => base.EndEditing();

            public string OnGUI(int id, string value, out bool changed)
            {
                Event evt = Event.current;
                if (evt.type == EventType.ExecuteCommand && evt.commandName == CommitCommand && id == messageControl)
                {
                    changed = !showMixedValue || controlThatHadFocusValue != k_MultiEditValueString
                        ? value != controlThatHadFocusValue : false;
                    evt.Use();
                    messageControl = 0;
                    return controlThatHadFocusValue;
                }
                changed = false;
                return value;
            }
        }

        public static void FocusTextInControl(string name)
        {
            GUI.FocusControl(name);
            EditorGUIUtility.editingTextField = true;
        }

        internal static bool LabelHasContent(GUIContent label) =>
            label == null || label.text != string.Empty || label.image != null;

        static bool IsPrintableChar(char c)
        {
            if (c < 32) return false;
            return true;
        }

        internal static bool MightBePrintableKey(Event evt)
        {
            if (evt.command || evt.control) return false;
            if (evt.keyCode >= KeyCode.Mouse0 && evt.keyCode <= KeyCode.Mouse6) return false;
            if (evt.keyCode >= KeyCode.JoystickButton0 && evt.keyCode <= KeyCode.Joystick8Button19) return false;
            if (evt.keyCode >= KeyCode.F1 && evt.keyCode <= KeyCode.F15 || evt.keyCode >= KeyCode.F16 && evt.keyCode <= KeyCode.F24) return false;
            switch (evt.keyCode)
            {
                case KeyCode.AltGr: case KeyCode.Backspace: case KeyCode.CapsLock: case KeyCode.Clear:
                case KeyCode.Delete: case KeyCode.DownArrow: case KeyCode.End: case KeyCode.Escape:
                case KeyCode.Help: case KeyCode.Home: case KeyCode.Insert: case KeyCode.LeftAlt:
                case KeyCode.LeftArrow: case KeyCode.LeftCommand: case KeyCode.LeftControl: case KeyCode.LeftShift:
                case KeyCode.LeftWindows: case KeyCode.Menu: case KeyCode.Numlock: case KeyCode.PageDown:
                case KeyCode.PageUp: case KeyCode.Pause: case KeyCode.Print: case KeyCode.RightAlt:
                case KeyCode.RightArrow: case KeyCode.RightCommand: case KeyCode.RightControl: case KeyCode.RightShift:
                case KeyCode.RightWindows: case KeyCode.ScrollLock: case KeyCode.SysReq: case KeyCode.UpArrow:
                    return false;
                case KeyCode.None:
                    return IsPrintableChar(evt.character);
                default:
                    return true;
            }
        }

        static EventType GetEventTypeForControlAllowDisabledContextMenuPaste(Event evt, int id)
        {
            if (GUI.enabled) return evt.GetTypeForControl(id);
            GUI.enabled = true;
            var type = evt.GetTypeForControl(id);
            GUI.enabled = false;
            if (type == EventType.Repaint || type == EventType.Layout || type == EventType.Used) return type;
            if (type == EventType.ContextClick) return type;
            if (type == EventType.MouseDown && evt.button == 1) return type;
            if ((type == EventType.ValidateCommand || type == EventType.ExecuteCommand) && evt.commandName == "Copy") return type;
            return EventType.Ignore;
        }

        static bool HasKeyboardFocus(int controlID) =>
            GUIUtility.keyboardControl == controlID && EditorGUIUtility.HasCurrentWindowKeyFocus();

        internal static string DoTextField(RecycledTextEditor editor, int id, Rect position, string text, GUIStyle style, string allowedletters, out bool changed, bool reset, bool multiline, bool passwordField)
        {
            EditorGUI.RequireGUI();
            Event evt = Event.current;
            string origText = text;
            if (text == null) text = string.Empty;
            if (showMixedValue) text = k_MultiEditValueString;

            if (HasKeyboardFocus(id) && Event.current.type != EventType.Layout)
            {
                if (editor.IsEditingControl(id))
                {
                    bool requireUpdateScrollOffset = editor.position.height != position.height;
                    editor.position = position;
                    editor.style = style;
                    editor.controlID = id;
                    editor.isMultiline = multiline;
                    editor.isPasswordField = passwordField;
                    editor.DetectFocusChange();
                    if (requireUpdateScrollOffset) editor.UpdateScrollOffset();
                }
                else if (EditorGUIUtility.editingTextField || (evt.GetTypeForControl(id) == EventType.ExecuteCommand && evt.commandName == "NewKeyboardFocus"))
                {
                    // A Tab or focus set from code: begin editing with everything selected.
                    editor.BeginEditing(id, text, position, style, multiline, passwordField);
                    if (GUI.skin.settings.cursorColor.a > 0) editor.SelectAll();
                    if (evt.GetTypeForControl(id) == EventType.ExecuteCommand) evt.Use();
                }
            }

            if (editor.controlID == id && GUIUtility.keyboardControl != id || (evt.type == EventType.ValidateCommand && evt.commandName == "UndoRedoPerformed"))
                editor.EndEditing();

            bool mayHaveChanged = false;
            string textBeforeKey = editor.text;
            var wasEnabled = GUI.enabled;

            switch (GetEventTypeForControlAllowDisabledContextMenuPaste(evt, id))
            {
                case EventType.ValidateCommand:
                    if (GUIUtility.keyboardControl == id)
                    {
                        switch (evt.commandName)
                        {
                            case "Cut":
                            case "Copy":
                                if (editor.hasSelection) evt.Use();
                                break;
                            case "Paste":
                                if (editor.CanPaste()) evt.Use();
                                break;
                            case "SelectAll":
                            case "Delete":
                                evt.Use();
                                break;
                            case "UndoRedoPerformed":
                                editor.text = text;
                                evt.Use();
                                break;
                        }
                    }
                    break;
                case EventType.ExecuteCommand:
                    if (GUIUtility.keyboardControl == id)
                    {
                        switch (evt.commandName)
                        {
                            case "OnLostFocus":
                                activeEditor?.EndEditing();
                                evt.Use();
                                break;
                            case "Cut":
                                editor.BeginEditing(id, text, position, style, multiline, passwordField);
                                editor.Cut();
                                mayHaveChanged = true;
                                break;
                            case "Copy":
                                if (wasEnabled) editor.Copy();
                                else if (!passwordField) GUIUtility.systemCopyBuffer = text;
                                evt.Use();
                                break;
                            case "Paste":
                                editor.BeginEditing(id, text, position, style, multiline, passwordField);
                                editor.Paste();
                                mayHaveChanged = true;
                                break;
                            case "SelectAll":
                                editor.SelectAll();
                                evt.Use();
                                break;
                            case "Delete":
                                editor.BeginEditing(id, text, position, style, multiline, passwordField);
                                if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX) editor.Delete();
                                else editor.Cut();
                                mayHaveChanged = true;
                                evt.Use();
                                break;
                        }
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        if (s_Dragged && s_DragToPosition)
                        {
                            editor.MoveSelectionToAltCursor();
                            mayHaveChanged = true;
                        }
                        else if (s_SelectAllOnMouseUp)
                        {
                            if (GUI.skin.settings.cursorColor.a > 0) editor.SelectAll();
                            s_SelectAllOnMouseUp = false;
                        }
                        else if (!s_Dragged && evt.button == 0)
                        {
                            if (editor.HasClickedOnHREF(Event.current.mousePosition, out string href)) Application.OpenURL(href);
                        }
                        editor.MouseDragSelectsWholeWords(false);
                        s_DragToPosition = true;
                        s_Dragged = false;
                        if (evt.button == 0)
                        {
                            GUIUtility.hotControl = 0;
                            evt.Use();
                        }
                    }
                    break;
                case EventType.MouseDown:
                    if (position.Contains(evt.mousePosition) && evt.button == 0)
                    {
                        if (editor.IsEditingControl(id))
                        {
                            if (Event.current.clickCount == 2 && GUI.skin.settings.doubleClickSelectsWord)
                            {
                                editor.MoveCursorToPosition(Event.current.mousePosition);
                                editor.SelectCurrentWord();
                                editor.MouseDragSelectsWholeWords(true);
                                editor.DblClickSnap(TextEditor.DblClickSnapping.WORDS);
                                s_DragToPosition = false;
                            }
                            else if (Event.current.clickCount == 3 && GUI.skin.settings.tripleClickSelectsLine)
                            {
                                editor.MoveCursorToPosition(Event.current.mousePosition);
                                editor.SelectCurrentParagraph();
                                editor.MouseDragSelectsWholeWords(true);
                                editor.DblClickSnap(TextEditor.DblClickSnapping.PARAGRAPHS);
                                s_DragToPosition = false;
                            }
                            else
                            {
                                editor.MoveCursorToPosition(Event.current.mousePosition);
                                s_SelectAllOnMouseUp = false;
                            }
                        }
                        else
                        {
                            GUIUtility.keyboardControl = id;
                            editor.BeginEditing(id, text, position, style, multiline, passwordField);
                            editor.MoveCursorToPosition(Event.current.mousePosition);
                            if (GUI.skin.settings.cursorColor.a > 0) s_SelectAllOnMouseUp = true;
                        }
                        GUIUtility.hotControl = id;
                        evt.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        if (!evt.shift && editor.hasSelection && s_DragToPosition)
                        {
                            editor.MoveAltCursorToPosition(Event.current.mousePosition);
                        }
                        else
                        {
                            if (evt.shift) editor.MoveCursorToPosition(Event.current.mousePosition);
                            else editor.SelectToPosition(Event.current.mousePosition);
                            s_DragToPosition = false;
                            s_SelectAllOnMouseUp = !editor.hasSelection;
                        }
                        s_Dragged = true;
                        evt.Use();
                    }
                    break;
                case EventType.ContextClick:
                    if (position.Contains(evt.mousePosition))
                    {
                        if (!editor.IsEditingControl(id))
                        {
                            GUIUtility.keyboardControl = id;
                            if (wasEnabled)
                            {
                                editor.BeginEditing(id, text, position, style, multiline, passwordField);
                                editor.MoveCursorToPosition(Event.current.mousePosition);
                            }
                        }
                        Stubs.Ignored("UnityEditor.EditorGUI.TextFieldContextMenu", "shows no cut/copy/paste menu");
                        Event.current.Use();
                    }
                    break;
                case EventType.KeyDown:
                    var nonPrintableTab = false;
                    if (GUIUtility.keyboardControl == id)
                    {
                        char c = evt.character;
                        if (editor.IsEditingControl(id) && editor.HandleKeyEvent(evt))
                        {
                            evt.Use();
                            mayHaveChanged = true;
                            break;
                        }
                        if (evt.keyCode == KeyCode.Escape)
                        {
                            if (editor.IsEditingControl(id))
                            {
                                // The search field styles (ToolbarSearchTextField, SearchTextField, ...) by name.
                                if (style.name.Contains("SearchText"))
                                    s_OriginalText = "";
                                editor.text = s_OriginalText;
                                editor.EndEditing();
                                mayHaveChanged = true;
                            }
                        }
                        else if (c == '\n' || c == 3)
                        {
                            if (!editor.IsEditingControl(id))
                            {
                                editor.BeginEditing(id, text, position, style, multiline, passwordField);
                                editor.SelectAll();
                            }
                            else
                            {
                                if (!multiline || (evt.alt || evt.shift || evt.control))
                                {
                                    editor.EndEditing();
                                }
                                else
                                {
                                    editor.Insert(c);
                                    mayHaveChanged = true;
                                    break;
                                }
                            }
                            evt.Use();
                        }
                        else if (c == '\t' || evt.keyCode == KeyCode.Tab)
                        {
                            if (multiline && editor.IsEditingControl(id))
                            {
                                bool validTabCharacter = allowedletters == null || allowedletters.IndexOf(c) != -1;
                                bool validTabEvent = !(evt.alt || evt.shift || evt.control) && c == '\t';
                                if (validTabEvent && validTabCharacter)
                                {
                                    editor.Insert(c);
                                    mayHaveChanged = true;
                                }
                            }
                            else nonPrintableTab = true;
                        }
                        else if (c == 25 || c == 27)
                        {
                            nonPrintableTab = true;
                        }
                        else if (editor.IsEditingControl(id))
                        {
                            bool validCharacter = (allowedletters == null || allowedletters.IndexOf(c) != -1) && IsPrintableChar(c);
                            if (validCharacter)
                            {
                                editor.Insert(c);
                                mayHaveChanged = true;
                            }
                            else if (Input.compositionString != "")
                            {
                                editor.ReplaceSelection("");
                                mayHaveChanged = true;
                            }
                        }
                        if (editor.IsEditingControl(id) && MightBePrintableKey(evt) && !nonPrintableTab)
                            evt.Use();
                    }
                    break;
                case EventType.Repaint:
                    string drawText;
                    if (editor.IsEditingControl(id))
                    {
                        if (showMixedValue && editor.text == k_MultiEditValueString) drawText = string.Empty;
                        else drawText = passwordField ? "".PadRight(editor.text.Length, '*') : editor.text;
                    }
                    else if (showMixedValue) drawText = "—";
                    else drawText = passwordField ? "".PadRight(text.Length, '*') : text;

                    if (!editor.IsEditingControl(id))
                        style.Draw(position, EditorGUIUtility.TempContent(drawText), id, false, position.Contains(Event.current.mousePosition));
                    else
                        editor.DrawCursor(drawText);

                    if (position.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
                        EditorGUIUtility.AddCursorRect(position, MouseCursor.Text);
                    break;
                case EventType.ScrollWheel:
                    editor.UpdateScrollOffset();
                    break;
            }

            if (GUIUtility.keyboardControl == id)
                Stubs.Ignored("UnityEngine.GUIUtility.textFieldInput", "a focused editor text field does not tell the GUI that text is being input");

            changed = false;
            if (mayHaveChanged)
            {
                changed = textBeforeKey != editor.text;
                evt.Use();
            }
            if (changed)
            {
                GUI.changed = true;
                return editor.text;
            }
            RecycledTextEditor.s_AllowContextCutOrPaste = true;
            return origText;
        }

        internal static string TextFieldInternal(Rect position, string text, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_TextFieldHash, FocusType.Keyboard, position);
            return DoTextField(s_RecycledEditor, id, IndentedRect(position), text, style, null, out _, false, false, false);
        }

        internal static string TextFieldInternal(Rect position, GUIContent label, string text, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_TextFieldHash, FocusType.Keyboard, position);
            return DoTextField(s_RecycledEditor, id, PrefixLabel(position, id, label), text, style, null, out _, false, false, false);
        }

        internal static string TextAreaInternal(Rect position, string text, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_TextAreaHash, FocusType.Keyboard, position);
            return DoTextField(s_RecycledEditor, id, IndentedRect(position), text, style, null, out _, false, true, false);
        }

        internal static string PasswordFieldInternal(Rect position, string password, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_PasswordFieldHash, FocusType.Keyboard, position);
            return DoTextField(s_RecycledEditor, id, IndentedRect(position), password, style, null, out _, false, false, true);
        }

        internal static string PasswordFieldInternal(Rect position, GUIContent label, string password, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_PasswordFieldHash, FocusType.Keyboard, position);
            return DoTextField(s_RecycledEditor, id, PrefixLabel(position, id, label), password, style, null, out _, false, false, true);
        }

        // ---------------------------------------------------------------- numbers

        internal partial struct NumberFieldValue
        {
            public NumberFieldValue(double v) { isDouble = true; doubleVal = v; longVal = 0; expression = null; success = false; }
            public NumberFieldValue(long v) { isDouble = false; doubleVal = 0; longVal = v; expression = null; success = false; }

            public bool isDouble;
            public double doubleVal;
            public long longVal;
            public ExpressionEvaluator.Expression expression;
            public bool success;
            public bool hasResult => success || expression != null;
        }

        static float NiceMouseDelta()
        {
            var evt = Event.current;
            return NumericFieldDraggerUtility.NiceDelta(evt.delta, NumericFieldDraggerUtility.Acceleration(evt.shift, evt.alt));
        }

        internal static void DragNumberValue(Rect dragHotZone, int id, bool isDouble, ref double doubleVal, ref long longVal, double dragSensitivity)
        {
            NumberFieldValue v = default;
            v.isDouble = isDouble;
            v.doubleVal = doubleVal;
            v.longVal = longVal;
            DragNumberValue(dragHotZone, id, ref v, dragSensitivity);
            doubleVal = v.doubleVal;
            longVal = v.longVal;
        }

        static void DragNumberValue(Rect dragHotZone, int id, ref NumberFieldValue value, double dragSensitivity)
        {
            Event evt = Event.current;
            switch (evt.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUIUtility.HitTest(dragHotZone, evt) && evt.button == 0)
                    {
                        EditorGUIUtility.editingTextField = false;
                        GUIUtility.hotControl = id;
                        activeEditor?.EndEditing();
                        evt.Use();
                        GUIUtility.keyboardControl = id;
                        s_DragCandidateState = DragCandidateState.InitiatedDragging;
                        s_DragStartValue = value.doubleVal;
                        s_DragStartIntValue = value.longVal;
                        s_DragStartPos = evt.mousePosition;
                        s_DragSensitivity = dragSensitivity;
                        evt.Use();
                        Stubs.Ignored("UnityEditor.EditorGUIUtility.SetWantsMouseJumping", "the cursor does not wrap at the screen's edges while a number is dragged");
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && s_DragCandidateState != DragCandidateState.NotDragging)
                    {
                        GUIUtility.hotControl = 0;
                        s_DragCandidateState = DragCandidateState.NotDragging;
                        evt.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        switch (s_DragCandidateState)
                        {
                            case DragCandidateState.InitiatedDragging:
                                if ((Event.current.mousePosition - s_DragStartPos).sqrMagnitude > kDragDeadzone)
                                {
                                    s_DragCandidateState = DragCandidateState.CurrentlyDragging;
                                    GUIUtility.keyboardControl = id;
                                }
                                evt.Use();
                                break;
                            case DragCandidateState.CurrentlyDragging:
                                if (value.isDouble)
                                {
                                    value.doubleVal += NiceMouseDelta() * s_DragSensitivity;
                                    value.doubleVal = Mathf.RoundBasedOnMinimumDifference(value.doubleVal, s_DragSensitivity);
                                }
                                else
                                {
                                    value.longVal += (long)Math.Round(NiceMouseDelta() * s_DragSensitivity);
                                }
                                value.success = true;
                                GUI.changed = true;
                                evt.Use();
                                break;
                        }
                    }
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.hotControl == id && evt.keyCode == KeyCode.Escape && s_DragCandidateState != DragCandidateState.NotDragging)
                    {
                        value.doubleVal = s_DragStartValue;
                        value.longVal = s_DragStartIntValue;
                        value.success = true;
                        GUI.changed = true;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;
                case EventType.Repaint:
                    EditorGUIUtility.AddCursorRect(dragHotZone, MouseCursor.SlideArrow);
                    break;
            }
        }

        internal static float DoFloatField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, float value, string formatString, GUIStyle style, bool draggable) =>
            DoFloatField(editor, position, dragHotZone, id, value, formatString, style, draggable,
                Event.current.GetTypeForControl(id) == EventType.MouseDown ? (float)NumericFieldDraggerUtility.CalculateFloatDragSensitivity(s_DragStartValue) : 0.0f);

        internal static float DoFloatField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, float value, string formatString, GUIStyle style, bool draggable, float dragSensitivity)
        {
            long dummy = 0;
            double doubleValue = value;
            DoNumberField(editor, position, dragHotZone, id, true, ref doubleValue, ref dummy, formatString, style, draggable, dragSensitivity);
            return Mathf.ClampToFloat(doubleValue);
        }

        internal static int DoIntField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, int value, string formatString, GUIStyle style, bool draggable, float dragSensitivity)
        {
            double dummy = 0f;
            long longValue = value;
            DoNumberField(editor, position, dragHotZone, id, false, ref dummy, ref longValue, formatString, style, draggable, dragSensitivity);
            return Mathf.ClampToInt(longValue);
        }

        internal static double DoDoubleField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, double value, string formatString, GUIStyle style, bool draggable) =>
            DoDoubleField(editor, position, dragHotZone, id, value, formatString, style, draggable,
                Event.current.GetTypeForControl(id) == EventType.MouseDown ? NumericFieldDraggerUtility.CalculateFloatDragSensitivity(s_DragStartValue) : 0.0);

        internal static double DoDoubleField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, double value, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
        {
            long dummy = 0;
            DoNumberField(editor, position, dragHotZone, id, true, ref value, ref dummy, formatString, style, draggable, dragSensitivity);
            return value;
        }

        internal static long DoLongField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id, long value, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
        {
            double dummy = 0f;
            DoNumberField(editor, position, dragHotZone, id, false, ref dummy, ref value, formatString, style, draggable, dragSensitivity);
            return value;
        }

        internal static void DoNumberField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id,
            bool isDouble, ref double doubleVal, ref long longVal, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
        {
            NumberFieldValue val = default;
            val.isDouble = isDouble;
            val.doubleVal = doubleVal;
            val.longVal = longVal;
            DoNumberField(editor, position, dragHotZone, id, ref val, formatString, style, draggable, dragSensitivity);
            if (val.success)
            {
                doubleVal = val.doubleVal;
                longVal = val.longVal;
            }
        }

        static string NumberText(in NumberFieldValue value, string formatString) =>
            value.isDouble ? value.doubleVal.ToString(formatString, CultureInfo.InvariantCulture) : value.longVal.ToString(formatString, CultureInfo.InvariantCulture);

        internal static void StringToNumericValue(in string str, ref NumberFieldValue value)
        {
            if (value.isDouble) StringToDouble(str, ref value);
            else StringToLong(str, ref value);
        }

        internal static void EvaluateExpressionOnNumberFieldValue(ref NumberFieldValue value)
        {
            if (value.isDouble) value.success = value.expression.Evaluate(ref value.doubleVal);
            else value.success = value.expression.Evaluate(ref value.longVal);
        }

        internal static void GetInitialValue(ref NumberFieldValue value)
        {
            if (value.isDouble)
            {
                if (UINumericFieldsUtils.TryConvertStringToDouble(s_OriginalText, out double oldValue) && oldValue != value.doubleVal)
                    value.doubleVal = oldValue;
            }
            else
            {
                if (UINumericFieldsUtils.TryConvertStringToLong(s_OriginalText, out long oldValue) && oldValue != value.longVal)
                    value.longVal = oldValue;
            }
        }

        internal static void UpdateNumberValueIfNeeded(ref NumberFieldValue value, in string str)
        {
            StringToNumericValue(str, ref value);
            if (!value.success && value.expression != null)
            {
                GetInitialValue(ref value);
                EvaluateExpressionOnNumberFieldValue(ref value);
            }
            GUI.changed = value.success || value.expression != null;
        }

        internal static void DoNumberField(RecycledTextEditor editor, Rect position, Rect dragHotZone, int id,
            ref NumberFieldValue value, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
        {
            string allowedCharacters = value.isDouble ? s_AllowedCharactersForFloat : s_AllowedCharactersForInt;
            if (draggable && GUI.enabled) DragNumberValue(dragHotZone, id, ref value, dragSensitivity);

            EditorGUI.RequireGUI();
            Event evt = Event.current;
            string str;
            if (HasKeyboardFocus(id) || (evt.type == EventType.MouseDown && evt.button == 0 && position.Contains(evt.mousePosition)))
            {
                if (!editor.IsEditingControl(id))
                {
                    str = s_RecycledCurrentEditingString = NumberText(value, formatString);
                }
                else
                {
                    str = s_RecycledCurrentEditingString;
                    if (evt.type == EventType.ValidateCommand && evt.commandName == "UndoRedoPerformed") str = NumberText(value, formatString);
                }
            }
            else
            {
                str = NumberText(value, formatString);
            }

            str = DoTextField(editor, id, position, str, style, allowedCharacters, out bool changed, false, false, false);
            if (GUIUtility.keyboardControl == id && changed)
            {
                s_RecycledCurrentEditingString = str;
                UpdateNumberValueIfNeeded(ref value, str);
            }
        }

        internal static bool StringToDouble(string str, out double value)
        {
            NumberFieldValue v = default;
            StringToDouble(str, ref v);
            value = v.doubleVal;
            return v.success;
        }

        static void StringToDouble(string str, ref NumberFieldValue value) =>
            value.success = UINumericFieldsUtils.TryConvertStringToDouble(str, out value.doubleVal, out value.expression);

        internal static bool StringToLong(string str, out long value)
        {
            NumberFieldValue v = default;
            StringToLong(str, ref v);
            value = v.longVal;
            return v.success;
        }

        static void StringToLong(string str, ref NumberFieldValue value)
        {
            value.expression = null;
            value.success = UINumericFieldsUtils.TryConvertStringToLong(str, out value.longVal, out value.expression);
        }

        internal static float FloatFieldInternal(Rect position, float value, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            return DoFloatField(s_RecycledEditor, IndentedRect(position), new Rect(0, 0, 0, 0), id, value, kFloatFieldFormatString, style, false);
        }

        internal static float FloatFieldInternal(Rect position, GUIContent label, float value, GUIStyle style)
        {
            var v = new NumberFieldValue(value);
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            Rect position2 = PrefixLabel(position, id, label);
            position.xMax = position2.x;
            var dragSensitivity = Event.current.GetTypeForControl(id) == EventType.MouseDown ? (float)NumericFieldDraggerUtility.CalculateFloatDragSensitivity(s_DragStartValue) : 0.0f;
            DoNumberField(s_RecycledEditor, position2, position, id, ref v, kFloatFieldFormatString, style, true, dragSensitivity);
            return Mathf.ClampToFloat(v.doubleVal);
        }

        internal static double DoubleFieldInternal(Rect position, double value, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            return DoDoubleField(s_RecycledEditor, IndentedRect(position), new Rect(0, 0, 0, 0), id, value, kDoubleFieldFormatString, style, false);
        }

        internal static double DoubleFieldInternal(Rect position, GUIContent label, double value, GUIStyle style)
        {
            var v = new NumberFieldValue(value);
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            Rect position2 = PrefixLabel(position, id, label);
            position.xMax = position2.x;
            var dragSensitivity = Event.current.GetTypeForControl(id) == EventType.MouseDown ? NumericFieldDraggerUtility.CalculateFloatDragSensitivity(s_DragStartValue) : 0.0;
            DoNumberField(s_RecycledEditor, position2, position, id, ref v, kDoubleFieldFormatString, style, true, dragSensitivity);
            return v.doubleVal;
        }

        internal static int IntFieldInternal(Rect position, int value, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            return DoIntField(s_RecycledEditor, IndentedRect(position), new Rect(0, 0, 0, 0), id, value, kIntFieldFormatString, style, false, NumericFieldDraggerUtility.CalculateIntDragSensitivity(value));
        }

        internal static int IntFieldInternal(Rect position, GUIContent label, int value, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            Rect position2 = PrefixLabel(position, id, label);
            position.xMax = position2.x;
            return DoIntField(s_RecycledEditor, position2, position, id, value, kIntFieldFormatString, style, true, NumericFieldDraggerUtility.CalculateIntDragSensitivity(value));
        }

        internal static long LongFieldInternal(Rect position, long value, GUIStyle style)
        {
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            return DoLongField(s_RecycledEditor, IndentedRect(position), new Rect(0, 0, 0, 0), id, value, kIntFieldFormatString, style, false, NumericFieldDraggerUtility.CalculateIntDragSensitivity(value));
        }

        internal static long LongFieldInternal(Rect position, GUIContent label, long value, GUIStyle style)
        {
            var v = new NumberFieldValue(value);
            int id = GUIUtility.GetControlID(s_FloatFieldHash, FocusType.Keyboard, position);
            Rect position2 = PrefixLabel(position, id, label);
            position.xMax = position2.x;
            DoNumberField(s_RecycledEditor, position2, position, id, ref v, kIntFieldFormatString, style, true, NumericFieldDraggerUtility.CalculateIntDragSensitivity(v.longVal));
            return v.longVal;
        }

        // ---------------------------------------------------------------- delayed fields

        internal static string DelayedTextFieldInternal(Rect position, int id, GUIContent label, string value, string allowedLetters, GUIStyle style)
        {
            string str;
            if (HasKeyboardFocus(id))
            {
                str = !s_DelayedTextEditor.IsEditingControl(id) ? (s_RecycledCurrentEditingString = value) : s_RecycledCurrentEditingString;
                Event evt = Event.current;
                if (evt.type == EventType.ValidateCommand && evt.commandName == "UndoRedoPerformed") str = value;
            }
            else str = value;

            bool wasChanged = GUI.changed;
            str = s_DelayedTextEditor.OnGUI(id, str, out bool changed);
            GUI.changed = false;
            if (!changed)
            {
                str = DoTextField(s_DelayedTextEditor, id, PrefixLabel(position, id, label), str, style, allowedLetters, out changed, false, false, false);
                GUI.changed = false;
                if (GUIUtility.keyboardControl == id)
                {
                    if (!s_DelayedTextEditor.IsEditingControl(id))
                    {
                        if (value != str && (!showMixedValue || k_MultiEditValueString != str))
                        {
                            GUI.changed = true;
                            value = str;
                        }
                    }
                    else s_RecycledCurrentEditingString = str;
                }
            }
            else
            {
                GUI.changed = true;
                value = str;
            }
            GUI.changed |= wasChanged;
            return value;
        }

        internal static void DelayedNumberFieldInternal(Rect position, Rect dragHotZone, int id, bool isDouble,
            ref double doubleVal, ref long longVal, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
        {
            NumberFieldValue val = default;
            val.isDouble = isDouble;
            val.doubleVal = doubleVal;
            val.longVal = longVal;
            DelayedNumberFieldInternal(position, dragHotZone, id, ref val, formatString, style, draggable, dragSensitivity);
            if (val.success)
            {
                doubleVal = val.doubleVal;
                longVal = val.longVal;
            }
        }

        static void DelayedNumberFieldInternal(Rect position, Rect dragHotZone, int id,
            ref NumberFieldValue value, string formatString, GUIStyle style, bool draggable, double dragSensitivity)
        {
            string allowedCharacters = value.isDouble ? s_AllowedCharactersForFloat : s_AllowedCharactersForInt;
            Event evt = Event.current;
            string str;
            if (HasKeyboardFocus(id) || (evt.type == EventType.MouseDown && evt.button == 0 && position.Contains(evt.mousePosition)))
            {
                if (!s_DelayedTextEditor.IsEditingControl(id) && s_DragCandidateState == DragCandidateState.NotDragging)
                {
                    str = s_RecycledCurrentEditingString = NumberText(value, formatString);
                }
                else
                {
                    str = s_RecycledCurrentEditingString;
                    if (evt.type == EventType.ValidateCommand && evt.commandName == "UndoRedoPerformed") str = NumberText(value, formatString);
                }
            }
            else str = NumberText(value, formatString);

            if (draggable)
            {
                double dragDouble = 0;
                long dragLong = 0;
                bool isConverted = value.isDouble ? StringToDouble(str, out dragDouble) : StringToLong(str, out dragLong);
                if (isConverted)
                {
                    DragNumberValue(dragHotZone, id, value.isDouble, ref dragDouble, ref dragLong, dragSensitivity);
                    str = value.isDouble ? dragDouble.ToString(formatString, CultureInfo.InvariantCulture) : dragLong.ToString(formatString, CultureInfo.InvariantCulture);
                }
            }

            bool wasChanged = GUI.changed;
            str = s_DelayedTextEditor.OnGUI(id, str, out bool changed);
            GUI.changed = false;
            if (!changed)
            {
                str = DoTextField(s_DelayedTextEditor, id, position, str, style, allowedCharacters, out changed, false, false, false);
                GUI.changed = false;
                if (GUIUtility.keyboardControl == id)
                {
                    s_RecycledCurrentEditingString = str;
                    if (!s_DelayedTextEditor.IsEditingControl(id) && s_DragCandidateState == DragCandidateState.NotDragging)
                    {
                        bool equalValues;
                        if (value.isDouble)
                            equalValues = double.TryParse(str, out var strToDouble) && Math.Abs(value.doubleVal - strToDouble) < double.Epsilon;
                        else
                            equalValues = str.Equals(value.longVal);
                        if (!equalValues && (!showMixedValue || k_MultiEditValueString != str))
                        {
                            GUI.changed = true;
                            if (value.isDouble) StringToDouble(str, ref value);
                            else StringToLong(str, ref value);
                        }
                    }
                }
            }
            else
            {
                UpdateNumberValueIfNeeded(ref value, str);
            }
            GUI.changed |= wasChanged;
        }

        internal static bool SetDelayedDraggable(ref Rect position, ref Rect dragHotzone, GUIContent label, int id)
        {
            // Fields without labels are not draggable.
            bool draggable = label != GUIContent.none;
            if (draggable)
            {
                dragHotzone = position;
                position = PrefixLabel(position, id, label);
                dragHotzone.xMax = position.x;
            }
            else position = IndentedRect(position);
            return draggable;
        }

        internal static float DelayedFloatFieldInternal(Rect position, GUIContent label, float value, GUIStyle style)
        {
            bool wasChanged = GUI.changed;
            int id = GUIUtility.GetControlID(s_DelayedTextFieldHash, FocusType.Keyboard, position);
            long dummy = 0;
            double doubleValue = value;
            Rect dragHotzone = default;
            bool draggable = SetDelayedDraggable(ref position, ref dragHotzone, label, id);
            BeginChangeCheck();
            DelayedNumberFieldInternal(position, dragHotzone, id, true, ref doubleValue, ref dummy, kFloatFieldFormatString, style, draggable,
                Event.current.GetTypeForControl(id) == EventType.MouseDown ? (float)NumericFieldDraggerUtility.CalculateFloatDragSensitivity(s_DragStartValue) : 0.0f);
            if (EndChangeCheck())
            {
                if ((float)doubleValue != value) return (float)doubleValue;
                GUI.changed = wasChanged;
            }
            return value;
        }

        internal static double DelayedDoubleFieldInternal(Rect position, GUIContent label, double value, GUIStyle style)
        {
            bool wasChanged = GUI.changed;
            int id = GUIUtility.GetControlID(s_DelayedTextFieldHash, FocusType.Keyboard, position);
            long dummy = 0;
            double newDoubleValue = value;
            Rect dragHotzone = default;
            bool draggable = SetDelayedDraggable(ref position, ref dragHotzone, label, id);
            BeginChangeCheck();
            DelayedNumberFieldInternal(position, dragHotzone, id, true, ref newDoubleValue, ref dummy, kFloatFieldFormatString, style, draggable,
                Event.current.GetTypeForControl(id) == EventType.MouseDown ? (float)NumericFieldDraggerUtility.CalculateFloatDragSensitivity(s_DragStartValue) : 0.0f);
            if (EndChangeCheck())
            {
                if (newDoubleValue != value) return newDoubleValue;
                GUI.changed = wasChanged;
            }
            return value;
        }

        internal static int DelayedIntFieldInternal(Rect position, GUIContent label, int value, GUIStyle style)
        {
            bool wasChanged = GUI.changed;
            int id = GUIUtility.GetControlID(s_DelayedTextFieldHash, FocusType.Keyboard, position);
            double dummy = 0;
            long longValue = value;
            Rect dragHotzone = default;
            bool draggable = SetDelayedDraggable(ref position, ref dragHotzone, label, id);
            BeginChangeCheck();
            DelayedNumberFieldInternal(position, dragHotzone, id, false, ref dummy, ref longValue, kIntFieldFormatString, style, draggable,
                Event.current.GetTypeForControl(id) == EventType.MouseDown ? (float)NumericFieldDraggerUtility.CalculateIntDragSensitivity(value) : 0.0f);
            if (EndChangeCheck())
            {
                if ((int)longValue != value) return (int)longValue;
                GUI.changed = wasChanged;
            }
            return value;
        }

        // ---------------------------------------------------------------- sliders

        static float PowPreserveSign(float f, float p)
        {
            var result = Mathf.Pow(Mathf.Abs(f), p);
            return f < 0.0f ? -result : result;
        }

        static float GetClosestPowerOfTen(float positiveNumber) =>
            positiveNumber <= 0f ? 1f : Mathf.Pow(10f, Mathf.RoundToInt(Mathf.Log10(positiveNumber)));

        static float RoundToMultipleOf(float value, float roundingValue) =>
            roundingValue == 0f ? value : Mathf.Round(value / roundingValue) * roundingValue;

        static Rect DragZoneRect(Rect position, bool hasLabel = true) =>
            new Rect(position.x, position.y, hasLabel ? EditorGUIUtility.labelWidth : 0, position.height);

        internal static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float left, float right, string formatString, float power = 1f, float logbase = 1f) =>
            DoSlider(position, dragZonePosition, id, value, left, right, formatString, power, logbase, EditorStyles.numberField, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, null, GUI.skin.horizontalSliderThumbExtent);

        internal static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float left, float right, string formatString, float textFieldMin, float textFieldMax, float power = 1f, float logbase = 1f) =>
            DoSlider(position, dragZonePosition, id, value, left, right, formatString, textFieldMin, textFieldMax, power, logbase, EditorStyles.numberField, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, null, GUI.skin.horizontalSliderThumbExtent);

        internal static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float left, float right, string formatString, GUIStyle textfieldStyle, float power = 1f, float logbase = 1f) =>
            DoSlider(position, dragZonePosition, id, value, left, right, formatString, power, logbase, textfieldStyle, GUI.skin.horizontalSlider, GUI.skin.horizontalSliderThumb, null, GUI.skin.horizontalSliderThumbExtent);

        static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float left, float right, string formatString, float power, float logbase, GUIStyle textfieldStyle, GUIStyle sliderStyle, GUIStyle thumbStyle, Texture2D sliderBackground, GUIStyle thumbStyleExtent) =>
            DoSlider(position, dragZonePosition, id, value, left, right, formatString, left, right, power, logbase, textfieldStyle, sliderStyle, thumbStyle, sliderBackground, thumbStyleExtent);

        static float DoSlider(Rect position, Rect dragZonePosition, int id, float value, float sliderMin, float sliderMax, string formatString,
            float textFieldMin, float textFieldMax, float power, float logbase, GUIStyle textfieldStyle, GUIStyle sliderStyle, GUIStyle thumbStyle,
            Texture2D sliderBackground, GUIStyle thumbStyleExtent)
        {
            int sliderId = GUIUtility.GetControlID(s_SliderKnobHash, FocusType.Passive, position);
            sliderMin = Mathf.Clamp(sliderMin, float.MinValue, float.MaxValue);
            sliderMax = Mathf.Clamp(sliderMax, float.MinValue, float.MaxValue);

            float w = position.width;
            if (w >= kSliderMinW + kSpacing + EditorGUIUtility.fieldWidth)
            {
                float sWidth = w - kSpacing - EditorGUIUtility.fieldWidth;
                BeginChangeCheck();
                // While the text field is not edited, the slider shares the keyboard focus.
                if (GUIUtility.keyboardControl == id && !s_RecycledEditor.IsEditingControl(id))
                    GUIUtility.keyboardControl = sliderId;

                var remapLeft = sliderMin;
                var remapRight = sliderMax;
                var newSliderValue = value;
                if (power != 1f)
                {
                    remapLeft = PowPreserveSign(sliderMin, 1f / power);
                    remapRight = PowPreserveSign(sliderMax, 1f / power);
                    newSliderValue = PowPreserveSign(value, 1f / power);
                }
                else if (logbase != 1f)
                {
                    remapLeft = Mathf.Log(sliderMin, logbase);
                    remapRight = Mathf.Log(sliderMax, logbase);
                    newSliderValue = Mathf.Log(value, logbase);
                }

                Rect sliderRect = new Rect(position.x, position.y, sWidth, position.height);
                if (sliderBackground != null && Event.current.type == EventType.Repaint)
                {
                    var bgRect = sliderStyle.overflow.Add(sliderStyle.padding.Remove(sliderRect));
                    Graphics.DrawTexture(bgRect, sliderBackground, new Rect(.5f / sliderBackground.width, .5f / sliderBackground.height, 1 - 1f / sliderBackground.width, 1 - 1f / sliderBackground.height), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f));
                }

                newSliderValue = GUI.Slider(sliderRect, newSliderValue, 0, remapLeft, remapRight, sliderStyle,
                    showMixedValue ? GUI.skin.sliderMixed : thumbStyle, true, sliderId, showMixedValue ? GUI.skin.sliderMixed : thumbStyleExtent);
                if (power != 1f)
                {
                    newSliderValue = PowPreserveSign(newSliderValue, power);
                    newSliderValue = Mathf.Clamp(newSliderValue, Mathf.Min(sliderMin, sliderMax), Mathf.Max(sliderMin, sliderMax));
                }
                else if (logbase != 1f)
                {
                    newSliderValue = Mathf.Pow(logbase, newSliderValue);
                }

                if (GUIUtility.keyboardControl == sliderId || GUIUtility.hotControl == sliderId)
                    GUIUtility.keyboardControl = id;

                if (GUIUtility.keyboardControl == id && Event.current.type == EventType.KeyDown && !s_RecycledEditor.IsEditingControl(id)
                    && (Event.current.keyCode == KeyCode.LeftArrow || Event.current.keyCode == KeyCode.RightArrow))
                {
                    float delta = GetClosestPowerOfTen(Mathf.Abs((sliderMax - sliderMin) * 0.01f));
                    if (formatString == kIntFieldFormatString && delta < 1) delta = 1;
                    if (Event.current.shift) delta *= 10;
                    if (Event.current.keyCode == KeyCode.LeftArrow) newSliderValue -= delta * 0.5001f;
                    else newSliderValue += delta * 0.5001f;
                    newSliderValue = RoundToMultipleOf(newSliderValue, delta);
                    GUI.changed = true;
                    Event.current.Use();
                }
                if (EndChangeCheck())
                {
                    float valuesPerPixel = (sliderMax - sliderMin) / (sWidth - GUI.skin.horizontalSlider.padding.horizontal - GUI.skin.horizontalSliderThumb.fixedWidth);
                    newSliderValue = Mathf.RoundBasedOnMinimumDifference(newSliderValue, Mathf.Abs(valuesPerPixel));
                    value = Mathf.Clamp(newSliderValue, Mathf.Min(sliderMin, sliderMax), Mathf.Max(sliderMin, sliderMax));
                    if (s_RecycledEditor.IsEditingControl(id)) s_RecycledEditor.EndEditing();
                }

                BeginChangeCheck();
                var style = textfieldStyle ?? EditorStyles.numberField;
                position.x += sWidth + kSpacing;
                position.width = EditorGUIUtility.fieldWidth;
                if (style.fixedHeight > 0) position.y += (position.height - style.fixedHeight) / 2;
                var newTextFieldValue = DoFloatField(s_RecycledEditor, position, dragZonePosition, id, value, formatString, style, true);
                if (EndChangeCheck())
                    value = Mathf.Clamp(newTextFieldValue, Mathf.Min(textFieldMin, textFieldMax), Mathf.Max(textFieldMin, textFieldMax));
            }
            else
            {
                var style = textfieldStyle ?? EditorStyles.numberField;
                w = Mathf.Min(EditorGUIUtility.fieldWidth, w);
                position.x = position.xMax - w;
                position.width = w;
                if (style.fixedHeight > 0) position.y += (position.height - style.fixedHeight) / 2;
                value = DoFloatField(s_RecycledEditor, position, dragZonePosition, id, value, formatString, style, true);
                value = Mathf.Clamp(value, Mathf.Min(textFieldMin, textFieldMax), Mathf.Max(textFieldMin, textFieldMax));
            }
            return value;
        }

        // ---------------------------------------------------------------- public fields

        public static string TextField(Rect position, string text) => TextField(position, text, TextStyle);
        public static string TextField(Rect position, string text, GUIStyle style) =>
            FieldsMeasured(nameof(TextField), out _) ? TextFieldInternal(position, text, style) : text;
        public static string TextField(Rect position, string label, string text) => TextField(position, label, text, TextStyle);
        public static string TextField(Rect position, string label, string text, GUIStyle style) => TextField(position, EditorGUIUtility.TempContent(label), text, style);
        public static string TextField(Rect position, GUIContent label, string text) => TextField(position, label, text, TextStyle);
        public static string TextField(Rect position, GUIContent label, string text, GUIStyle style) =>
            FieldsMeasured(nameof(TextField), out _) ? TextFieldInternal(position, label, text, style) : text;

        public static string DelayedTextField(Rect position, string text) => DelayedTextField(position, text, TextStyle);
        public static string DelayedTextField(Rect position, string text, GUIStyle style) => DelayedTextField(position, GUIContent.none, text, style);
        public static string DelayedTextField(Rect position, string label, string text) => DelayedTextField(position, label, text, TextStyle);
        public static string DelayedTextField(Rect position, string label, string text, GUIStyle style) => DelayedTextField(position, EditorGUIUtility.TempContent(label), text, style);
        public static string DelayedTextField(Rect position, GUIContent label, string text) => DelayedTextField(position, label, text, TextStyle);
        public static string DelayedTextField(Rect position, GUIContent label, string text, GUIStyle style)
        {
            if (!FieldsMeasured(nameof(DelayedTextField), out _)) return text;
            int id = GUIUtility.GetControlID(s_TextFieldHash, FocusType.Keyboard, position);
            return DelayedTextFieldInternal(position, id, label, text, null, style);
        }
        public static string DelayedTextField(Rect position, GUIContent label, int controlId, string text) => DelayedTextField(position, label, controlId, text, TextStyle);
        public static string DelayedTextField(Rect position, GUIContent label, int controlId, string text, GUIStyle style) =>
            FieldsMeasured(nameof(DelayedTextField), out _) ? DelayedTextFieldInternal(position, controlId, label, text, null, style) : text;

        public static string TextArea(Rect position, string text) => TextArea(position, text, TextStyle);
        public static string TextArea(Rect position, string text, GUIStyle style) =>
            FieldsMeasured(nameof(TextArea), out _) ? TextAreaInternal(position, text, style) : text;

        public static string PasswordField(Rect position, string password) => PasswordField(position, password, TextStyle);
        public static string PasswordField(Rect position, string password, GUIStyle style) =>
            FieldsMeasured(nameof(PasswordField), out _) ? PasswordFieldInternal(position, password, style) : password;
        public static string PasswordField(Rect position, string label, string password) => PasswordField(position, label, password, TextStyle);
        public static string PasswordField(Rect position, string label, string password, GUIStyle style) => PasswordField(position, EditorGUIUtility.TempContent(label), password, style);
        public static string PasswordField(Rect position, GUIContent label, string password) => PasswordField(position, label, password, TextStyle);
        public static string PasswordField(Rect position, GUIContent label, string password, GUIStyle style) =>
            FieldsMeasured(nameof(PasswordField), out _) ? PasswordFieldInternal(position, label, password, style) : password;

        public static float FloatField(Rect position, float value) => FloatField(position, value, NumberStyle);
        public static float FloatField(Rect position, float value, GUIStyle style) =>
            FieldsMeasured(nameof(FloatField), out _) ? FloatFieldInternal(position, value, style) : value;
        public static float FloatField(Rect position, string label, float value) => FloatField(position, label, value, NumberStyle);
        public static float FloatField(Rect position, string label, float value, GUIStyle style) => FloatField(position, EditorGUIUtility.TempContent(label), value, style);
        public static float FloatField(Rect position, GUIContent label, float value) => FloatField(position, label, value, NumberStyle);
        public static float FloatField(Rect position, GUIContent label, float value, GUIStyle style) =>
            FieldsMeasured(nameof(FloatField), out _) ? FloatFieldInternal(position, label, value, style) : value;

        public static float DelayedFloatField(Rect position, float value) => DelayedFloatField(position, value, NumberStyle);
        public static float DelayedFloatField(Rect position, float value, GUIStyle style) => DelayedFloatField(position, GUIContent.none, value, style);
        public static float DelayedFloatField(Rect position, string label, float value) => DelayedFloatField(position, label, value, NumberStyle);
        public static float DelayedFloatField(Rect position, string label, float value, GUIStyle style) => DelayedFloatField(position, EditorGUIUtility.TempContent(label), value, style);
        public static float DelayedFloatField(Rect position, GUIContent label, float value) => DelayedFloatField(position, label, value, NumberStyle);
        public static float DelayedFloatField(Rect position, GUIContent label, float value, GUIStyle style) =>
            FieldsMeasured(nameof(DelayedFloatField), out _) ? DelayedFloatFieldInternal(position, label, value, style) : value;

        public static double DoubleField(Rect position, double value) => DoubleField(position, value, NumberStyle);
        public static double DoubleField(Rect position, double value, GUIStyle style) =>
            FieldsMeasured(nameof(DoubleField), out _) ? DoubleFieldInternal(position, value, style) : value;
        public static double DoubleField(Rect position, string label, double value) => DoubleField(position, label, value, NumberStyle);
        public static double DoubleField(Rect position, string label, double value, GUIStyle style) => DoubleField(position, EditorGUIUtility.TempContent(label), value, style);
        public static double DoubleField(Rect position, GUIContent label, double value) => DoubleField(position, label, value, NumberStyle);
        public static double DoubleField(Rect position, GUIContent label, double value, GUIStyle style) =>
            FieldsMeasured(nameof(DoubleField), out _) ? DoubleFieldInternal(position, label, value, style) : value;

        public static double DelayedDoubleField(Rect position, double value) => DelayedDoubleField(position, value, NumberStyle);
        public static double DelayedDoubleField(Rect position, double value, GUIStyle style) =>
            FieldsMeasured(nameof(DelayedDoubleField), out _) ? DelayedDoubleFieldInternal(position, null, value, style) : value;
        public static double DelayedDoubleField(Rect position, string label, double value) => DelayedDoubleField(position, label, value, NumberStyle);
        public static double DelayedDoubleField(Rect position, string label, double value, GUIStyle style) => DelayedDoubleField(position, EditorGUIUtility.TempContent(label), value, style);
        public static double DelayedDoubleField(Rect position, GUIContent label, double value) => DelayedDoubleField(position, label, value, NumberStyle);
        public static double DelayedDoubleField(Rect position, GUIContent label, double value, GUIStyle style) =>
            FieldsMeasured(nameof(DelayedDoubleField), out _) ? DelayedDoubleFieldInternal(position, label, value, style) : value;

        public static int IntField(Rect position, int value) => IntField(position, value, NumberStyle);
        public static int IntField(Rect position, int value, GUIStyle style) =>
            FieldsMeasured(nameof(IntField), out _) ? IntFieldInternal(position, value, style) : value;
        public static int IntField(Rect position, string label, int value) => IntField(position, label, value, NumberStyle);
        public static int IntField(Rect position, string label, int value, GUIStyle style) => IntField(position, EditorGUIUtility.TempContent(label), value, style);
        public static int IntField(Rect position, GUIContent label, int value) => IntField(position, label, value, NumberStyle);
        public static int IntField(Rect position, GUIContent label, int value, GUIStyle style) =>
            FieldsMeasured(nameof(IntField), out _) ? IntFieldInternal(position, label, value, style) : value;

        public static int DelayedIntField(Rect position, int value) => DelayedIntField(position, value, NumberStyle);
        public static int DelayedIntField(Rect position, int value, GUIStyle style) => DelayedIntField(position, GUIContent.none, value, style);
        public static int DelayedIntField(Rect position, string label, int value) => DelayedIntField(position, label, value, NumberStyle);
        public static int DelayedIntField(Rect position, string label, int value, GUIStyle style) => DelayedIntField(position, EditorGUIUtility.TempContent(label), value, style);
        public static int DelayedIntField(Rect position, GUIContent label, int value) => DelayedIntField(position, label, value, NumberStyle);
        public static int DelayedIntField(Rect position, GUIContent label, int value, GUIStyle style) =>
            FieldsMeasured(nameof(DelayedIntField), out _) ? DelayedIntFieldInternal(position, label, value, style) : value;

        public static long LongField(Rect position, long value) => LongField(position, value, NumberStyle);
        public static long LongField(Rect position, long value, GUIStyle style) =>
            FieldsMeasured(nameof(LongField), out _) ? LongFieldInternal(position, value, style) : value;
        public static long LongField(Rect position, string label, long value) => LongField(position, label, value, NumberStyle);
        public static long LongField(Rect position, string label, long value, GUIStyle style) => LongField(position, EditorGUIUtility.TempContent(label), value, style);
        public static long LongField(Rect position, GUIContent label, long value) => LongField(position, label, value, NumberStyle);
        public static long LongField(Rect position, GUIContent label, long value, GUIStyle style) =>
            FieldsMeasured(nameof(LongField), out _) ? LongFieldInternal(position, label, value, style) : value;

        public static float Slider(Rect position, float value, float leftValue, float rightValue)
        {
            if (!FieldsMeasured(nameof(Slider), out _)) return value;
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            return DoSlider(IndentedRect(position), DragZoneRect(position, false), id, value, leftValue, rightValue, kFloatFieldFormatString);
        }

        public static float Slider(Rect position, string label, float value, float leftValue, float rightValue) =>
            Slider(position, EditorGUIUtility.TempContent(label), value, leftValue, rightValue);

        public static float Slider(Rect position, GUIContent label, float value, float leftValue, float rightValue)
        {
            if (!FieldsMeasured(nameof(Slider), out _)) return value;
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            Rect controlRect = PrefixLabel(position, id, label);
            Rect dragZone = LabelHasContent(label) ? DragZoneRect(position) : new Rect();
            return DoSlider(controlRect, dragZone, id, value, leftValue, rightValue, kFloatFieldFormatString, NumberStyle);
        }

        public static int IntSlider(Rect position, int value, int leftValue, int rightValue)
        {
            if (!FieldsMeasured(nameof(IntSlider), out _)) return value;
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            return Mathf.RoundToInt(DoSlider(IndentedRect(position), DragZoneRect(position), id, value, leftValue, rightValue, kIntFieldFormatString));
        }

        public static int IntSlider(Rect position, string label, int value, int leftValue, int rightValue) =>
            IntSlider(position, EditorGUIUtility.TempContent(label), value, leftValue, rightValue);

        public static int IntSlider(Rect position, GUIContent label, int value, int leftValue, int rightValue)
        {
            if (!FieldsMeasured(nameof(IntSlider), out _)) return value;
            int id = GUIUtility.GetControlID(s_SliderHash, FocusType.Keyboard, position);
            return Mathf.RoundToInt(DoSlider(PrefixLabel(position, id, label), DragZoneRect(position), id, value, leftValue, rightValue, kIntFieldFormatString, NumberStyle));
        }
    }

    public sealed partial class EditorGUIUtility
    {
        static readonly GUIContent s_Text = new GUIContent();

        internal static GUIContent TempContent(string t)
        {
            s_Text.image = null;
            s_Text.text = t;
            s_Text.tooltip = null;
            return s_Text;
        }
    }

    public sealed partial class EditorGUILayout
    {
        internal static float kLabelFloatMinW => EditorGUIUtility.labelWidth + EditorGUIUtility.fieldWidth + EditorGUI.kSpacing;
        internal static float kLabelFloatMaxW => EditorGUIUtility.labelWidth + EditorGUIUtility.fieldWidth + EditorGUI.kSpacing;

        static Rect NumberRect(bool hasLabel, GUIStyle style, GUILayoutOption[] options) =>
            GetControlRect(hasLabel, EditorGUIUtility.singleLineHeight, style, options);

        internal static Rect GetSliderRect(bool hasLabel, params GUILayoutOption[] options)
        {
            return GUILayoutUtility.GetRect(hasLabel ? kLabelFloatMinW : EditorGUIUtility.fieldWidth,
                kLabelFloatMaxW + EditorGUI.kSpacing + EditorGUI.kSliderMaxW,
                EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight, GUI.skin.horizontalSlider, options);
        }

        public static string TextField(string text, params GUILayoutOption[] options) => TextField(text, EditorGUI.TextStyle, options);
        public static string TextField(string text, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.TextField(NumberRect(false, style, options), text, style);
        public static string TextField(string label, string text, params GUILayoutOption[] options) => TextField(label, text, EditorGUI.TextStyle, options);
        public static string TextField(string label, string text, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.TextField(NumberRect(true, style, options), label, text, style);
        public static string TextField(GUIContent label, string text, params GUILayoutOption[] options) => TextField(label, text, EditorGUI.TextStyle, options);
        public static string TextField(GUIContent label, string text, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.TextField(NumberRect(true, style, options), label, text, style);

        public static string DelayedTextField(string text, params GUILayoutOption[] options) => DelayedTextField(text, EditorGUI.TextStyle, options);
        public static string DelayedTextField(string text, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedTextField(NumberRect(false, style, options), text, style);
        public static string DelayedTextField(string label, string text, params GUILayoutOption[] options) => DelayedTextField(label, text, EditorGUI.TextStyle, options);
        public static string DelayedTextField(string label, string text, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedTextField(NumberRect(true, style, options), label, text, style);
        public static string DelayedTextField(GUIContent label, string text, params GUILayoutOption[] options) => DelayedTextField(label, text, EditorGUI.TextStyle, options);
        public static string DelayedTextField(GUIContent label, string text, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedTextField(NumberRect(true, style, options), label, text, style);

        public static string TextArea(string text, params GUILayoutOption[] options) => TextArea(text, EditorGUI.TextStyle, options);
        public static string TextArea(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            if (!Compat.EditorValueFieldsMeasured)
            {
                Stubs.NotImplemented("UnityEditor.EditorGUILayout.TextArea");
                return text;
            }
            return EditorGUI.TextArea(GUILayoutUtility.GetRect(EditorGUIUtility.TempContent(text), style, options), text, style);
        }

        public static string PasswordField(string password, params GUILayoutOption[] options) => PasswordField(password, EditorGUI.TextStyle, options);
        public static string PasswordField(string password, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.PasswordField(NumberRect(false, style, options), password, style);
        public static string PasswordField(string label, string password, params GUILayoutOption[] options) => PasswordField(label, password, EditorGUI.TextStyle, options);
        public static string PasswordField(string label, string password, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.PasswordField(NumberRect(true, style, options), label, password, style);
        public static string PasswordField(GUIContent label, string password, params GUILayoutOption[] options) => PasswordField(label, password, EditorGUI.TextStyle, options);
        public static string PasswordField(GUIContent label, string password, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.PasswordField(NumberRect(true, style, options), label, password, style);

        public static float FloatField(float value, params GUILayoutOption[] options) => EditorGUI.FloatField(NumberRect(false, EditorGUI.NumberStyle, options), value);
        public static float FloatField(float value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.FloatField(NumberRect(false, style, options), value, style);
        public static float FloatField(string label, float value, params GUILayoutOption[] options) => EditorGUI.FloatField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static float FloatField(string label, float value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.FloatField(NumberRect(true, style, options), label, value, style);
        public static float FloatField(GUIContent label, float value, params GUILayoutOption[] options) => EditorGUI.FloatField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static float FloatField(GUIContent label, float value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.FloatField(NumberRect(true, style, options), label, value, style);

        public static float DelayedFloatField(float value, params GUILayoutOption[] options) => EditorGUI.DelayedFloatField(NumberRect(false, EditorGUI.NumberStyle, options), value);
        public static float DelayedFloatField(float value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedFloatField(NumberRect(false, style, options), value, style);
        public static float DelayedFloatField(string label, float value, params GUILayoutOption[] options) => EditorGUI.DelayedFloatField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static float DelayedFloatField(string label, float value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedFloatField(NumberRect(true, style, options), label, value, style);
        public static float DelayedFloatField(GUIContent label, float value, params GUILayoutOption[] options) => EditorGUI.DelayedFloatField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static float DelayedFloatField(GUIContent label, float value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedFloatField(NumberRect(true, style, options), label, value, style);

        public static double DoubleField(double value, params GUILayoutOption[] options) => EditorGUI.DoubleField(NumberRect(false, EditorGUI.NumberStyle, options), value);
        public static double DoubleField(double value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DoubleField(NumberRect(false, style, options), value, style);
        public static double DoubleField(string label, double value, params GUILayoutOption[] options) => EditorGUI.DoubleField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static double DoubleField(string label, double value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DoubleField(NumberRect(true, style, options), label, value, style);
        public static double DoubleField(GUIContent label, double value, params GUILayoutOption[] options) => EditorGUI.DoubleField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static double DoubleField(GUIContent label, double value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DoubleField(NumberRect(true, style, options), label, value, style);

        public static double DelayedDoubleField(double value, params GUILayoutOption[] options) => EditorGUI.DelayedDoubleField(NumberRect(false, EditorGUI.NumberStyle, options), value);
        public static double DelayedDoubleField(double value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedDoubleField(NumberRect(false, style, options), value, style);
        public static double DelayedDoubleField(string label, double value, params GUILayoutOption[] options) => EditorGUI.DelayedDoubleField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static double DelayedDoubleField(string label, double value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedDoubleField(NumberRect(true, style, options), label, value, style);
        public static double DelayedDoubleField(GUIContent label, double value, params GUILayoutOption[] options) => EditorGUI.DelayedDoubleField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static double DelayedDoubleField(GUIContent label, double value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedDoubleField(NumberRect(true, style, options), label, value, style);

        public static int IntField(int value, params GUILayoutOption[] options) => EditorGUI.IntField(NumberRect(false, EditorGUI.NumberStyle, options), value);
        public static int IntField(int value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.IntField(NumberRect(false, style, options), value, style);
        public static int IntField(string label, int value, params GUILayoutOption[] options) => EditorGUI.IntField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static int IntField(string label, int value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.IntField(NumberRect(true, style, options), label, value, style);
        public static int IntField(GUIContent label, int value, params GUILayoutOption[] options) => EditorGUI.IntField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static int IntField(GUIContent label, int value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.IntField(NumberRect(true, style, options), label, value, style);

        public static int DelayedIntField(int value, params GUILayoutOption[] options) => EditorGUI.DelayedIntField(NumberRect(false, EditorGUI.NumberStyle, options), value);
        public static int DelayedIntField(int value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedIntField(NumberRect(false, style, options), value, style);
        public static int DelayedIntField(string label, int value, params GUILayoutOption[] options) => EditorGUI.DelayedIntField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static int DelayedIntField(string label, int value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedIntField(NumberRect(true, style, options), label, value, style);
        public static int DelayedIntField(GUIContent label, int value, params GUILayoutOption[] options) => EditorGUI.DelayedIntField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static int DelayedIntField(GUIContent label, int value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.DelayedIntField(NumberRect(true, style, options), label, value, style);

        public static long LongField(long value, params GUILayoutOption[] options) => EditorGUI.LongField(NumberRect(false, EditorGUI.NumberStyle, options), value);
        public static long LongField(long value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.LongField(NumberRect(false, style, options), value, style);
        public static long LongField(string label, long value, params GUILayoutOption[] options) => EditorGUI.LongField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static long LongField(string label, long value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.LongField(NumberRect(true, style, options), label, value, style);
        public static long LongField(GUIContent label, long value, params GUILayoutOption[] options) => EditorGUI.LongField(NumberRect(true, EditorGUI.NumberStyle, options), label, value);
        public static long LongField(GUIContent label, long value, GUIStyle style, params GUILayoutOption[] options) => EditorGUI.LongField(NumberRect(true, style, options), label, value, style);

        public static float Slider(float value, float leftValue, float rightValue, params GUILayoutOption[] options) =>
            EditorGUI.Slider(GetSliderRect(false, options), value, leftValue, rightValue);
        public static float Slider(string label, float value, float leftValue, float rightValue, params GUILayoutOption[] options) =>
            Slider(EditorGUIUtility.TempContent(label), value, leftValue, rightValue, options);
        public static float Slider(GUIContent label, float value, float leftValue, float rightValue, params GUILayoutOption[] options) =>
            EditorGUI.Slider(GetSliderRect(true, options), label, value, leftValue, rightValue);

        public static int IntSlider(int value, int leftValue, int rightValue, params GUILayoutOption[] options) =>
            EditorGUI.IntSlider(GetSliderRect(false, options), value, leftValue, rightValue);
        public static int IntSlider(string label, int value, int leftValue, int rightValue, params GUILayoutOption[] options) =>
            IntSlider(EditorGUIUtility.TempContent(label), value, leftValue, rightValue, options);
        public static int IntSlider(GUIContent label, int value, int leftValue, int rightValue, params GUILayoutOption[] options) =>
            EditorGUI.IntSlider(GetSliderRect(true, options), label, value, leftValue, rightValue);
    }
}
