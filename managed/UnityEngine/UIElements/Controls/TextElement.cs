using System;
using Unbound.UIElements;

namespace UnityEngine.UIElements
{
    public partial interface INotifyValueChanged<T>
    {
        T value { get; set; }
        void SetValueWithoutNotify(T newValue);
    }

    /// <summary>An element that shows text: measured and drawn with the TextCore text engine. Made
    /// selectable (and editable, inside text fields) it keeps a cursor and selection and handles
    /// pointer and keyboard editing as Unity's text manipulators do.</summary>
    public partial class TextElement : BindableElement, INotifyValueChanged<string>, ITextEdition, ITextSelection
    {
        public static readonly string ussClassName = "unity-text-element";
        public static readonly string selectableUssClassName = ussClassName + "__selectable";
        internal static readonly string k_EllipsisText = "...";
        const string k_ZeroWidthSpace = "​";

        string m_Text = string.Empty;
        bool m_EnableRichText = true;
        bool m_EmojiFallbackSupport = true;
        bool m_ParseEscapeSequences;
        bool m_DisplayTooltipWhenElided = true;
        internal string elidedText;
        internal readonly UaTextHandle uaText;

        // edition
        internal bool isInputField;
        bool m_Multiline;
        bool m_IsReadOnly = true;
        int m_MaxLength = -1;
        string m_PlaceholderText = "";
        string m_RenderedText;
        string m_OriginalText;
        char m_MaskChar;
        bool m_IsPassword;
        bool m_HidePlaceholderTextOnFocus;
        bool m_AutoCorrection;
        bool m_HideMobileInput;
        internal TouchScreenKeyboardType m_KeyboardType = TouchScreenKeyboardType.Default;
        internal TouchScreenKeyboard m_TouchScreenKeyboard;
        internal Action<bool> onIsReadOnlyChanged;
        UaTextEditingManipulator m_Editing;

        // selection
        UaTextSelectingManipulator m_Selecting;
        bool m_IsSelectable;
        bool m_DoubleClickSelectsWord = true;
        bool m_TripleClickSelectsLine = true;
        bool m_SelectAllOnFocus;
        bool m_SelectAllOnMouseUp;
        Color m_SelectionColor = new Color(0.239f, 0.502f, 0.875f, 0.65f);
        Color m_CursorColor = new Color(0.706f, 0.706f, 0.706f, 1f);
        float m_CursorWidth = 1f;

        public TextElement()
        {
            requireMeasureFunction = true;
            tabIndex = -1;
            uaText = new UaTextHandle(this);
            AddToClassList(ussClassName);
            edition.GetDefaultValueType = () => string.Empty;
        }

        public virtual string text
        {
            get => ((INotifyValueChanged<string>)this).value;
            set => ((INotifyValueChanged<string>)this).value = value;
        }

        public bool enableRichText
        {
            get => m_EnableRichText;
            set
            {
                if (m_EnableRichText == value) return;
                m_EnableRichText = value;
                MarkDirtyText();
            }
        }

        public bool emojiFallbackSupport
        {
            get => m_EmojiFallbackSupport;
            set
            {
                if (m_EmojiFallbackSupport == value) return;
                m_EmojiFallbackSupport = value;
                MarkDirtyText();
            }
        }

        public bool parseEscapeSequences
        {
            get => m_ParseEscapeSequences;
            set
            {
                if (m_ParseEscapeSequences == value) return;
                m_ParseEscapeSequences = value;
                MarkDirtyText();
            }
        }

        public bool displayTooltipWhenElided
        {
            get => m_DisplayTooltipWhenElided;
            set => m_DisplayTooltipWhenElided = value;
        }

        public bool isElided { get; private set; }

        internal bool hasFocus => elementPanel != null && elementPanel.focusController?.GetLeafFocusedElement() == this;

        string INotifyValueChanged<string>.value
        {
            get => m_Text ?? string.Empty;
            set
            {
                if (m_Text == value) return;
                if (panel != null)
                {
                    using (var e = ChangeEvent<string>.GetPooled(text, value))
                    {
                        e.elementTarget = this;
                        ((INotifyValueChanged<string>)this).SetValueWithoutNotify(value);
                        SendEvent(e);
                    }
                    return;
                }
                ((INotifyValueChanged<string>)this).SetValueWithoutNotify(value);
            }
        }

        void INotifyValueChanged<string>.SetValueWithoutNotify(string newValue)
        {
            newValue = ((ITextEdition)this).CullString(newValue);
            if (m_Text != newValue)
            {
                m_RenderedText = newValue;
                m_Text = newValue;
                uaText.MarkDirty();
                var c = computedValues;
                if (c.height.IsAuto() || c.height.IsNone() || c.width.IsAuto() || c.width.IsNone())
                {
                    UaMarkMeasureDirty();
                    IncrementVersion(VersionChangeType.Layout | VersionChangeType.Repaint);
                }
                else
                {
                    IncrementVersion(VersionChangeType.Repaint);
                }
            }
            if (m_Editing != null) m_Editing.editing.text = newValue;
        }

        public void MarkDirtyText()
        {
            uaText.MarkDirty();
            UaMarkMeasureDirty();
            IncrementVersion(VersionChangeType.Layout | VersionChangeType.Repaint);
        }

        internal override void OnUaStyleResolved(ComputedValues previous, ComputedValues current)
        {
            if (!UaTextHandle.SameTextStyle(previous, current))
            {
                uaText.MarkDirty();
                UaMarkMeasureDirty();
            }
        }

        /// <summary>The text that is laid out: the placeholder, the masked text or the text, with a
        /// zero-width space after the text of editable (or selectable disabled) elements.</summary>
        internal string renderedTextString
        {
            get
            {
                if (showPlaceholderText) return m_PlaceholderText + k_ZeroWidthSpace;
                if (effectiveMaskChar != '\0') return new string(effectiveMaskChar, m_RenderedText?.Length ?? 0) + k_ZeroWidthSpace;
                if (!edition.isReadOnly || ((pseudoStates & PseudoStates.Disabled) != 0 && isSelectable)) return (m_RenderedText ?? string.Empty) + k_ZeroWidthSpace;
                return m_RenderedText ?? string.Empty;
            }
        }

        internal string originalText => m_OriginalText;

        public Vector2 MeasureTextSize(string textToMeasure, float width, MeasureMode widthMode, float height, MeasureMode heightMode) =>
            uaText.Measure(textToMeasure, width, widthMode, height, heightMode);

        protected internal override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode) =>
            uaText.Measure(renderedTextString, desiredWidth, widthMode, desiredHeight, heightMode);

        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (evt.target == this)
            {
                if (evt is GeometryChangedEvent)
                {
                    UpdateVisibleText();
                    return;
                }
                if (evt is AttachToPanelEvent || evt is DetachFromPanelEvent) return;
            }
            EditionHandleEvent(evt);
        }

        void UpdateVisibleText()
        {
            bool elide = computedValues.textOverflow == TextOverflow.Ellipsis && computedValues.overflow == OverflowInternal.Hidden;
            if (!elide)
            {
                isElided = false;
                return;
            }
            elidedText = uaText.Elide(text, k_EllipsisText, contentRect.width, computedValues.unityTextOverflowPosition);
            isElided = !string.Equals(elidedText, text, StringComparison.Ordinal);
        }

        void EditionHandleEvent(EventBase evt)
        {
            if (!selection.isSelectable) return;
            m_Selecting?.HandleEventBubbleUp(evt);
            if (!edition.isReadOnly) m_Editing?.HandleEventBubbleUp(evt);
        }

        internal void UaSelectNone() => selection.SelectNone();

        // ---- ITextEdition ----

        internal ITextEdition edition => this;

        internal UaTextEditingManipulator uaEditing => m_Editing;

        bool ITextEdition.multiline
        {
            get => m_Multiline;
            set
            {
                if (value == m_Multiline) return;
                if (!edition.isReadOnly) m_Editing.editing.multiline = value;
                m_Multiline = value;
            }
        }

        TouchScreenKeyboard ITextEdition.touchScreenKeyboard => m_TouchScreenKeyboard;

        TouchScreenKeyboardType ITextEdition.keyboardType
        {
            get => m_KeyboardType;
            set => m_KeyboardType = value;
        }

        bool ITextEdition.hideMobileInput
        {
            get => m_HideMobileInput;
            set => m_HideMobileInput = value;
        }

        bool ITextEdition.isReadOnly
        {
            get => m_IsReadOnly || !enabledInHierarchy;
            set
            {
                if (value == m_IsReadOnly) return;
                m_Editing = value ? null : new UaTextEditingManipulator(this);
                m_IsReadOnly = value;
                onIsReadOnlyChanged?.Invoke(value);
            }
        }

        int ITextEdition.maxLength
        {
            get => m_MaxLength;
            set
            {
                if (m_MaxLength == value) return;
                m_MaxLength = value;
                text = edition.CullString(text);
            }
        }

        string ITextEdition.placeholder
        {
            get => m_PlaceholderText;
            set
            {
                if (value == m_PlaceholderText) return;
                if (!string.IsNullOrEmpty(value) && (text == null || text.Equals(edition.GetDefaultValueType())))
                    text = "";
                m_PlaceholderText = value;
                OnPlaceholderChanged?.Invoke();
                uaText.MarkDirty();
                UaMarkMeasureDirty();
                MarkDirtyRepaint();
            }
        }

        bool ITextEdition.isDelayed { get; set; }

        Func<char, bool> ITextEdition.AcceptCharacter { get; set; }

        Action<bool> ITextEdition.UpdateScrollOffset { get; set; }

        Action ITextEdition.UpdateValueFromText { get; set; }

        Action ITextEdition.UpdateTextFromValue { get; set; }

        Action ITextEdition.MoveFocusToCompositeRoot { get; set; }

        internal Action OnPlaceholderChanged { get; set; }

        Func<string> ITextEdition.GetDefaultValueType { get; set; }

        char ITextEdition.maskChar
        {
            get => m_MaskChar;
            set
            {
                if (m_MaskChar == value) return;
                m_MaskChar = value;
                if (edition.isPassword)
                {
                    uaText.MarkDirty();
                    IncrementVersion(VersionChangeType.Repaint);
                }
            }
        }

        char effectiveMaskChar => edition.isPassword ? m_MaskChar : '\0';

        bool ITextEdition.isPassword
        {
            get => m_IsPassword;
            set
            {
                if (m_IsPassword == value) return;
                m_IsPassword = value;
                uaText.MarkDirty();
                IncrementVersion(VersionChangeType.Repaint);
            }
        }

        bool ITextEdition.hidePlaceholderOnFocus
        {
            get => m_HidePlaceholderTextOnFocus;
            set => m_HidePlaceholderTextOnFocus = value;
        }

        bool ITextEdition.autoCorrection
        {
            get => m_AutoCorrection;
            set => m_AutoCorrection = value;
        }

        internal bool needsPlaceholderIfTextIsEmpty => m_PlaceholderText.Length > 0 && !(edition.hidePlaceholderOnFocus && hasFocus);

        internal bool showPlaceholderText => needsPlaceholderIfTextIsEmpty && string.IsNullOrEmpty(text);

        void ITextEdition.ResetValueAndText() => m_OriginalText = text = null;

        void ITextEdition.SaveValueAndText() => m_OriginalText = text;

        void ITextEdition.RestoreValueAndText() => text = m_OriginalText;

        void ITextEdition.UpdateText(string value)
        {
            if (m_TouchScreenKeyboard != null && m_TouchScreenKeyboard.text != value) m_TouchScreenKeyboard.text = value;
            if (text == value) return;
            using (var e = InputEvent.GetPooled(text, value))
            {
                e.elementTarget = parent;
                ((INotifyValueChanged<string>)this).SetValueWithoutNotify(value);
                parent?.SendEvent(e);
            }
        }

        string ITextEdition.CullString(string s)
        {
            int max = edition.maxLength;
            return max >= 0 && s != null && s.Length > max ? s.Substring(0, max) : s;
        }

        // ---- ITextSelection ----

        public ITextSelection selection => this;

        internal UaTextSelectingManipulator uaSelecting => m_Selecting ??= new UaTextSelectingManipulator(this);

        bool ITextSelection.isSelectable
        {
            get => m_IsSelectable && focusable;
            set
            {
                if (value == m_IsSelectable) return;
                focusable = value;
                m_IsSelectable = value;
                if (value) _ = uaSelecting;
                EnableInClassList(selectableUssClassName, value);
            }
        }

        internal bool isSelectable
        {
            get => selection.isSelectable;
            set => selection.isSelectable = value;
        }

        int ITextSelection.cursorIndex
        {
            get => selection.isSelectable ? uaSelecting.selecting.cursorIndex : -1;
            set
            {
                if (selection.isSelectable) uaSelecting.selecting.cursorIndex = value;
            }
        }

        int ITextSelection.selectIndex
        {
            get => selection.isSelectable ? uaSelecting.selecting.selectIndex : -1;
            set
            {
                if (selection.isSelectable) uaSelecting.selecting.selectIndex = value;
            }
        }

        bool ITextSelection.doubleClickSelectsWord
        {
            get => m_DoubleClickSelectsWord;
            set => m_DoubleClickSelectsWord = value;
        }

        internal bool doubleClickSelectsWord
        {
            get => selection.doubleClickSelectsWord;
            set => selection.doubleClickSelectsWord = value;
        }

        bool ITextSelection.tripleClickSelectsLine
        {
            get => m_TripleClickSelectsLine;
            set => m_TripleClickSelectsLine = value;
        }

        internal bool tripleClickSelectsLine
        {
            get => selection.tripleClickSelectsLine;
            set => selection.tripleClickSelectsLine = value;
        }

        bool ITextSelection.selectAllOnFocus
        {
            get => m_SelectAllOnFocus;
            set => m_SelectAllOnFocus = value;
        }

        bool ITextSelection.selectAllOnMouseUp
        {
            get => m_SelectAllOnMouseUp;
            set => m_SelectAllOnMouseUp = value;
        }

        Vector2 ITextSelection.cursorPosition =>
            uaText.GetCursorPositionFromStringIndexUsingLineHeight(selection.cursorIndex) + contentRect.min;

        float ITextSelection.lineHeightAtCursorPosition => uaText.GetLineHeightFromCharacterIndex(selection.cursorIndex);

        float ITextSelection.cursorWidth
        {
            get => m_CursorWidth;
            set => m_CursorWidth = value;
        }

        Color ITextSelection.selectionColor
        {
            get => m_SelectionColor;
            set
            {
                if (m_SelectionColor == value) return;
                m_SelectionColor = value;
                MarkDirtyRepaint();
            }
        }

        internal Color selectionColor
        {
            get => m_SelectionColor;
            set => ((ITextSelection)this).selectionColor = value;
        }

        Color ITextSelection.cursorColor
        {
            get => m_CursorColor;
            set
            {
                if (m_CursorColor == value) return;
                m_CursorColor = value;
                MarkDirtyRepaint();
            }
        }

        internal Color cursorColor
        {
            get => m_CursorColor;
            set => ((ITextSelection)this).cursorColor = value;
        }

        void ITextSelection.SelectAll()
        {
            if (selection.isSelectable) uaSelecting.selecting.SelectAll();
        }

        void ITextSelection.SelectNone()
        {
            if (selection.isSelectable) uaSelecting.selecting.SelectNone();
        }

        void ITextSelection.SelectRange(int cursorIndex, int selectionIndex)
        {
            if (!selection.isSelectable) return;
            uaSelecting.selecting.cursorIndex = cursorIndex;
            uaSelecting.selecting.selectIndex = selectionIndex;
        }

        bool ITextSelection.HasSelection() => selection.isSelectable && uaSelecting.selecting.hasSelection;

        void ITextSelection.MoveTextEnd()
        {
            if (selection.isSelectable) uaSelecting.selecting.MoveTextEnd();
        }
    }

    public partial class Label : TextElement
    {
        public new static readonly string ussClassName = "unity-label";

        public Label() : this(string.Empty) { }

        public Label(string text)
        {
            AddToClassList(ussClassName);
            this.text = text;
        }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Pointer selection and selection keys of a selectable text element (Unity's TextSelectingManipulator).</summary>
    internal sealed class UaTextSelectingManipulator
    {
        internal readonly UaTextSelecting selecting;
        readonly TextElement m_TextElement;
        bool m_SelectAllOnMouseUp;
        Vector2 m_ClickStartPosition;
        bool m_Dragged;
        int m_ConsecutiveMouseDownCount;
        long m_LastMouseDownTimeStamp;
        readonly Event m_ImguiEvent = new Event();

        internal bool isClicking { get; private set; }

        internal UaTextSelectingManipulator(TextElement textElement)
        {
            m_TextElement = textElement;
            selecting = new UaTextSelecting(textElement.uaText);
            selecting.OnCursorIndexChange += OnIndexChange;
            selecting.OnSelectIndexChange += OnIndexChange;
            selecting.OnRevealCursorChange += () => m_TextElement.IncrementVersion(VersionChangeType.Repaint);
        }

        void OnIndexChange()
        {
            m_TextElement.IncrementVersion(VersionChangeType.Repaint);
            if (selecting.hasSelection && m_TextElement.focusController != null) m_TextElement.focusController.selectedTextElement = m_TextElement;
            if (selecting.revealCursor) m_TextElement.edition.UpdateScrollOffset?.Invoke(false);
        }

        internal void HandleEventBubbleUp(EventBase evt)
        {
            switch (evt)
            {
                case FocusEvent _: OnFocusEvent(); break;
                case BlurEvent _: m_SelectAllOnMouseUp = m_TextElement.selection.selectAllOnMouseUp; break;
                case KeyDownEvent key: OnKeyDown(key); break;
                case PointerDownEvent down: OnPointerDownEvent(down); break;
                case PointerMoveEvent move: OnPointerMoveEvent(move); break;
                case PointerUpEvent up: OnPointerUpEvent(up); break;
            }
        }

        void OnFocusEvent()
        {
            m_SelectAllOnMouseUp = false;
            if (isClicking) m_SelectAllOnMouseUp = m_TextElement.selection.selectAllOnMouseUp;
            selecting.OnFocus(m_TextElement.selection.selectAllOnFocus && !isClicking);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (!m_TextElement.hasFocus) return;
            evt.GetEquivalentImguiEvent(m_ImguiEvent);
            if (selecting.HandleKeyEvent(m_ImguiEvent)) evt.StopPropagation();
        }

        void OnPointerDownEvent(PointerDownEvent evt)
        {
            Vector2 position = (Vector2)evt.localPosition - m_TextElement.contentRect.min;
            if (evt.button != 0) return;
            if (evt.timestamp - m_LastMouseDownTimeStamp < Event.GetDoubleClickTime()) m_ConsecutiveMouseDownCount++;
            else m_ConsecutiveMouseDownCount = 1;
            if (m_ConsecutiveMouseDownCount == 2 && m_TextElement.selection.doubleClickSelectsWord)
            {
                if (selecting.cursorIndex == 0 && selecting.cursorIndex != selecting.selectIndex) selecting.MoveCursorToPosition_Internal(position, evt.shiftKey);
                selecting.SelectCurrentWord();
                selecting.MouseDragSelectsWholeWords(true);
                selecting.DblClickSnap(TextEditor.DblClickSnapping.WORDS);
            }
            else if (m_ConsecutiveMouseDownCount == 3 && m_TextElement.selection.tripleClickSelectsLine)
            {
                selecting.SelectCurrentParagraph();
                selecting.MouseDragSelectsWholeWords(true);
                selecting.DblClickSnap(TextEditor.DblClickSnapping.PARAGRAPHS);
            }
            else
            {
                selecting.MoveCursorToPosition_Internal(position, evt.shiftKey);
                m_TextElement.edition.UpdateScrollOffset?.Invoke(false);
                selecting.MouseDragSelectsWholeWords(false);
                selecting.DblClickSnap(TextEditor.DblClickSnapping.WORDS);
            }
            m_LastMouseDownTimeStamp = evt.timestamp;
            isClicking = true;
            m_TextElement.CapturePointer(evt.pointerId);
            m_ClickStartPosition = position;
            evt.StopPropagation();
        }

        void OnPointerMoveEvent(PointerMoveEvent evt)
        {
            if (!isClicking) return;
            Vector2 position = (Vector2)evt.localPosition - m_TextElement.contentRect.min;
            m_Dragged = m_Dragged || (m_ClickStartPosition - position).sqrMagnitude >= 16f;
            if (m_Dragged)
            {
                selecting.SelectToPosition(position);
                m_TextElement.edition.UpdateScrollOffset?.Invoke(false);
                m_SelectAllOnMouseUp = m_TextElement.selection.selectAllOnMouseUp && !selecting.hasSelection;
            }
            evt.StopPropagation();
        }

        void OnPointerUpEvent(PointerUpEvent evt)
        {
            if (evt.button != 0 || !isClicking) return;
            if (m_SelectAllOnMouseUp) selecting.SelectAll();
            m_SelectAllOnMouseUp = false;
            m_Dragged = false;
            isClicking = false;
            m_TextElement.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }
    }

    /// <summary>Keyboard editing of an editable text element (Unity's TextEditingManipulator with its
    /// keyboard event handler).</summary>
    internal sealed class UaTextEditingManipulator
    {
        readonly TextElement m_TextElement;
        internal readonly UaTextEditing editing;
        readonly Event m_ImguiEvent = new Event();
        bool m_Changed;
        bool m_ShouldInvokeUpdateValue;

        internal UaTextEditingManipulator(TextElement textElement)
        {
            m_TextElement = textElement;
            editing = new UaTextEditing(textElement.uaSelecting.selecting, textElement.text) { multiline = textElement.edition.multiline };
        }

        internal void HandleEventBubbleUp(EventBase evt)
        {
            if (m_TextElement.edition.isReadOnly) return;
            if (evt is FocusInEvent) OnFocusInEvent();
            else if (evt is FocusOutEvent) editing.OnBlur();
            switch (evt)
            {
                case KeyDownEvent key: OnKeyDown(key); break;
                case FocusEvent _: m_TextElement.edition.SaveValueAndText(); break;
                case NavigationMoveEvent move: OnNavigationEvent(move, move.deviceType); break;
                case NavigationSubmitEvent submit: OnNavigationEvent(submit, submit.deviceType); break;
                case NavigationCancelEvent cancel: OnNavigationEvent(cancel, cancel.deviceType); break;
            }
        }

        void OnFocusInEvent()
        {
            m_TextElement.edition.SaveValueAndText();
            if (m_TextElement.focusController != null) m_TextElement.focusController.selectedTextElement = m_TextElement;
        }

        void OnNavigationEvent(EventBase evt, NavigationDeviceType deviceType)
        {
            if (deviceType != NavigationDeviceType.Keyboard && deviceType != NavigationDeviceType.Unknown) return;
            evt.StopPropagation();
            m_TextElement.focusController.IgnoreEvent(evt);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            if (!m_TextElement.hasFocus) return;
            m_Changed = false;
            evt.GetEquivalentImguiEvent(m_ImguiEvent);
            var edition = m_TextElement.edition;
            if (editing.HandleKeyEvent(m_ImguiEvent))
            {
                if (m_TextElement.text != editing.text) m_Changed = true;
                evt.StopPropagation();
                Finish(evt);
                return;
            }
            char c = evt.character;
            if ((evt.actionKey && (!evt.altKey || c == '\0')) || (evt.keyCode >= KeyCode.F1 && evt.keyCode <= KeyCode.F15) ||
                (evt.keyCode >= KeyCode.F16 && evt.keyCode <= KeyCode.F24) || (evt.altKey && c == '\0') ||
                (c == '\t' && evt.keyCode == KeyCode.None && evt.modifiers == EventModifiers.None))
                return;
            if (evt.keyCode == KeyCode.Tab)
            {
                if (!edition.multiline || evt.shiftKey)
                {
                    if (evt.UaShouldSendNavigationMoveEvent())
                    {
                        m_TextElement.focusController.FocusNextInDirection(m_TextElement, evt.shiftKey ? VisualElementFocusChangeDirection.left : VisualElementFocusChangeDirection.right);
                        evt.StopPropagation();
                    }
                    return;
                }
                if (!evt.UaShouldSendNavigationMoveEvent()) return;
            }
            if (!edition.multiline && (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return)) m_ShouldInvokeUpdateValue = true;
            evt.StopPropagation();
            bool newline = c == '\n' || c == '\r';
            bool leaveField = edition.multiline ? c == '\n' && evt.shiftKey : newline && !evt.altKey;
            if ((edition.multiline ? c == '\n' : newline) && leaveField)
            {
                ApplyTextIfNeeded();
                edition.MoveFocusToCompositeRoot?.Invoke();
                return;
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                edition.RestoreValueAndText();
                edition.UpdateValueFromText?.Invoke();
                edition.MoveFocusToCompositeRoot?.Invoke();
            }
            if (evt.keyCode == KeyCode.Tab) c = '\t';
            if (edition.AcceptCharacter != null && !edition.AcceptCharacter(c))
            {
                ApplyTextIfNeeded();
                return;
            }
            if (c >= ' ' || evt.keyCode == KeyCode.Tab || (edition.multiline && !evt.altKey && newline))
                m_Changed = editing.Insert(c);
            Finish(evt);
        }

        void Finish(KeyDownEvent evt)
        {
            if (m_Changed || m_ShouldInvokeUpdateValue) UpdateLabel();
            m_TextElement.edition.UpdateScrollOffset?.Invoke(evt.keyCode == KeyCode.Backspace);
        }

        void ApplyTextIfNeeded()
        {
            if (!m_ShouldInvokeUpdateValue) return;
            m_TextElement.edition.UpdateValueFromText?.Invoke();
            m_ShouldInvokeUpdateValue = false;
        }

        void UpdateLabel()
        {
            var edition = m_TextElement.edition;
            edition.UpdateText(editing.text);
            if (!edition.isDelayed || m_ShouldInvokeUpdateValue)
            {
                edition.UpdateValueFromText?.Invoke();
                m_ShouldInvokeUpdateValue = false;
            }
        }
    }
}
