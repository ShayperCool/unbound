using System;

namespace UnityEngine.UIElements
{
    /// <summary>A field edited as text: its input (named unity-text-input) holds an editable,
    /// selectable TextElement that takes the focus for the field.</summary>
    public abstract partial class TextInputBaseField<TValueType> : BaseField<TValueType>
    {
        static readonly CustomStyleProperty<Color> s_SelectionColorProperty = new CustomStyleProperty<Color>("--unity-selection-color");
        static readonly CustomStyleProperty<Color> s_CursorColorProperty = new CustomStyleProperty<Color>("--unity-cursor-color");

        internal const int kMaxLengthNone = -1;
        internal const char kMaskCharDefault = '*';
        public new static readonly string ussClassName = "unity-base-text-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        internal static readonly string multilineContainerClassName = ussClassName + "__multiline-container";
        public static readonly string singleLineInputUssClassName = inputUssClassName + "--single-line";
        public static readonly string multilineInputUssClassName = inputUssClassName + "--multiline";
        public static readonly string placeholderUssClassName = inputUssClassName + "--placeholder";
        internal static readonly string multilineInputWithScrollViewUssClassName = multilineInputUssClassName + "--scroll-view";
        public static readonly string textInputUssName = "unity-text-input";

        readonly TextInputBase m_TextInputBase;
        internal bool m_UpdateTextFromValue;

        /// <summary>The input element of a text field: the inner TextElement, single line or in a
        /// multiline container, scrolled to keep the cursor visible.</summary>
        protected internal abstract partial class TextInputBase : VisualElement
        {
            internal ScrollView scrollView;
            internal VisualElement multilineContainer;
            public static readonly string innerComponentsModifierName = "--inner-input-field-component";
            public static readonly string innerTextElementUssClassName = TextElement.ussClassName + innerComponentsModifierName;
            internal static readonly string innerTextElementWithScrollViewUssClassName = TextElement.ussClassName + innerComponentsModifierName + "--scroll-view";
            public static readonly string horizontalVariantInnerTextElementUssClassName = TextElement.ussClassName + innerComponentsModifierName + "--horizontal";
            public static readonly string verticalVariantInnerTextElementUssClassName = TextElement.ussClassName + innerComponentsModifierName + "--vertical";
            public static readonly string verticalHorizontalVariantInnerTextElementUssClassName = TextElement.ussClassName + innerComponentsModifierName + "--vertical-horizontal";
            public static readonly string innerScrollviewUssClassName = ScrollView.ussClassName + innerComponentsModifierName;
            public static readonly string innerViewportUssClassName = ScrollView.viewportUssClassName + innerComponentsModifierName;
            public static readonly string innerContentContainerUssClassName = ScrollView.contentUssClassName + innerComponentsModifierName;
            internal Vector2 scrollOffset = Vector2.zero;
            bool m_ScrollViewWasClamped;
            Vector2 m_LastCursorPos = Vector2.zero;
            internal ScrollerVisibility verticalScrollerVisibility = ScrollerVisibility.Hidden;

            internal TextElement textElement { get; private set; }

            public ITextSelection textSelection => textElement.selection;

            public ITextEdition textEdition => textElement.edition;

            internal bool isDragging { get; set; }

            public string text
            {
                get => textElement.text;
                set
                {
                    if (textElement.text != value) textElement.text = value;
                }
            }

            internal string originalText => textElement.originalText;

            public bool isReadOnly
            {
                get => textEdition.isReadOnly;
                set => textEdition.isReadOnly = value;
            }

            public int maxLength
            {
                get => textEdition.maxLength;
                set => textEdition.maxLength = value;
            }

            public char maskChar
            {
                get => textEdition.maskChar;
                set => textEdition.maskChar = value;
            }

            public virtual bool isPasswordField
            {
                get => textEdition.isPassword;
                set => textEdition.isPassword = value;
            }

            public Color selectionColor
            {
                get => textElement.selectionColor;
                set => textElement.selectionColor = value;
            }

            public Color cursorColor
            {
                get => textElement.cursorColor;
                set => textElement.cursorColor = value;
            }

            public int cursorIndex => textSelection.cursorIndex;

            public int selectIndex => textSelection.selectIndex;

            public bool doubleClickSelectsWord
            {
                get => textSelection.doubleClickSelectsWord;
                set => textSelection.doubleClickSelectsWord = value;
            }

            public bool tripleClickSelectsLine
            {
                get => textSelection.tripleClickSelectsLine;
                set => textSelection.tripleClickSelectsLine = value;
            }

            internal TextInputBase()
            {
                delegatesFocus = true;
                textElement = new TextElement { isInputField = true };
                textElement.selection.isSelectable = true;
                textEdition.isReadOnly = false;
                textSelection.isSelectable = true;
                textSelection.selectAllOnFocus = true;
                textSelection.selectAllOnMouseUp = true;
                textElement.enableRichText = false;
                textElement.tabIndex = 0;
                textEdition.AcceptCharacter += AcceptCharacter;
                textEdition.UpdateScrollOffset += UpdateScrollOffset;
                textEdition.UpdateValueFromText += UpdateValueFromText;
                textEdition.UpdateTextFromValue += UpdateTextFromValue;
                textEdition.MoveFocusToCompositeRoot += MoveFocusToCompositeRoot;
                textEdition.GetDefaultValueType = GetDefaultValueType;
                AddToClassList(TextInputBaseField<TValueType>.inputUssClassName);
                name = TextInputBaseField<string>.textInputUssName;
                SetSingleLine();
                RegisterCallback<CustomStyleResolvedEvent>(OnInputCustomStyleResolved);
                tabIndex = -1;
            }

            protected virtual TValueType StringToValue(string str) => throw new NotSupportedException();

            internal void UpdateValueFromText() => ((TextInputBaseField<TValueType>)parent).UpdateValueFromText();

            internal void UpdateTextFromValue() => ((TextInputBaseField<TValueType>)parent).UpdateTextFromValue();

            internal void MoveFocusToCompositeRoot()
            {
                var field = (TextInputBaseField<TValueType>)parent;
                focusController.SwitchFocus(field);
                textEdition.keyboardType = TouchScreenKeyboardType.Default;
                textEdition.autoCorrection = false;
            }

            internal void SetSingleLine()
            {
                hierarchy.Clear();
                RemoveMultilineComponents();
                Add(textElement);
                AddToClassList(TextInputBaseField<TValueType>.singleLineInputUssClassName);
                textElement.AddToClassList(innerTextElementUssClassName);
                textElement.RegisterCallback<GeometryChangedEvent>(TextElementOnGeometryChangedEvent);
                if (scrollOffset != Vector2.zero)
                {
                    scrollOffset.y = 0f;
                    UpdateScrollOffset();
                }
            }

            internal void SetMultiline()
            {
                if (!textEdition.multiline) return;
                RemoveSingleLineComponents();
                RemoveMultilineComponents();
                if (verticalScrollerVisibility != ScrollerVisibility.Hidden && scrollView == null)
                {
                    scrollView = new ScrollView();
                    scrollView.Add(textElement);
                    Add(scrollView);
                    SetScrollViewMode();
                    scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                    scrollView.verticalScrollerVisibility = verticalScrollerVisibility;
                    scrollView.AddToClassList(innerScrollviewUssClassName);
                    scrollView.contentViewport.AddToClassList(innerViewportUssClassName);
                    scrollView.contentContainer.AddToClassList(innerContentContainerUssClassName);
                    scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(ScrollViewOnGeometryChangedEvent);
                    scrollView.verticalScroller.slider.RegisterValueChangedCallback(MakeSureScrollViewDoesNotLeakEvents);
                    scrollView.verticalScroller.slider.focusable = false;
                    scrollView.horizontalScroller.slider.RegisterValueChangedCallback(MakeSureScrollViewDoesNotLeakEvents);
                    scrollView.horizontalScroller.slider.focusable = false;
                    AddToClassList(TextInputBaseField<TValueType>.multilineInputWithScrollViewUssClassName);
                    textElement.AddToClassList(innerTextElementWithScrollViewUssClassName);
                }
                else if (multilineContainer == null)
                {
                    textElement.RegisterCallback<GeometryChangedEvent>(TextElementOnGeometryChangedEvent);
                    multilineContainer = new VisualElement();
                    multilineContainer.AddToClassList(TextInputBaseField<TValueType>.multilineContainerClassName);
                    multilineContainer.Add(textElement);
                    Add(multilineContainer);
                    SetMultilineContainerStyle();
                    AddToClassList(TextInputBaseField<TValueType>.multilineInputUssClassName);
                    textElement.AddToClassList(innerTextElementUssClassName);
                }
            }

            void MakeSureScrollViewDoesNotLeakEvents(ChangeEvent<float> evt) => evt.StopPropagation();

            void ScrollViewOnGeometryChangedEvent(GeometryChangedEvent e)
            {
                if (e.oldRect.size != e.newRect.size) UpdateScrollOffset();
            }

            void TextElementOnGeometryChangedEvent(GeometryChangedEvent e)
            {
                if (e.oldRect.size == e.newRect.size) return;
                bool widthChanged = Math.Abs(e.oldRect.size.x - e.newRect.size.x) > 1e-30f;
                UpdateScrollOffset(false, widthChanged);
            }

            internal void OnInputCustomStyleResolved(CustomStyleResolvedEvent e)
            {
                var custom = e.customStyle;
                if (custom.TryGetValue(s_SelectionColorProperty, out var selection)) textElement.selectionColor = selection;
                if (custom.TryGetValue(s_CursorColorProperty, out var cursor)) textElement.cursorColor = cursor;
                SetScrollViewMode();
                SetMultilineContainerStyle();
            }

            string GetDefaultValueType() => default(TValueType) == null ? "" : default(TValueType).ToString();

            internal virtual bool AcceptCharacter(char c) => !textEdition.isReadOnly && enabledInHierarchy;

            internal void UpdateScrollOffset(bool isBackspace = false) => UpdateScrollOffset(isBackspace, false);

            internal void UpdateScrollOffset(bool isBackspace, bool widthChanged)
            {
                var selection = textSelection;
                if (selection.cursorIndex < 0 || (selection.cursorIndex <= 0 && selection.selectIndex <= 0 && scrollOffset == Vector2.zero)) return;
                if (scrollView != null)
                {
                    scrollOffset = GetScrollOffset(scrollView.scrollOffset.x, scrollView.scrollOffset.y, scrollView.contentViewport.layout.width, isBackspace, widthChanged);
                    scrollView.scrollOffset = scrollOffset;
                    m_ScrollViewWasClamped = scrollOffset.x > scrollView.scrollOffset.x || scrollOffset.y > scrollView.scrollOffset.y;
                    return;
                }
                var position = textElement.transform.position;
                scrollOffset = GetScrollOffset(scrollOffset.x, scrollOffset.y, contentRect.width, isBackspace, widthChanged);
                position.y = -Mathf.Min(scrollOffset.y, Math.Abs(textElement.contentRect.height - contentRect.height));
                position.x = -scrollOffset.x;
                if (!position.Equals(textElement.transform.position)) textElement.transform.position = position;
            }

            Vector2 GetScrollOffset(float xOffset, float yOffset, float contentViewportWidth, bool isBackspace, bool widthChanged)
            {
                if (!textElement.hasFocus) return Vector2.zero;
                var cursorPosition = textSelection.cursorPosition;
                float cursorWidth = textSelection.cursorWidth;
                float x = xOffset, y = yOffset;
                if (Math.Abs(m_LastCursorPos.x - cursorPosition.x) > 0.05f || m_ScrollViewWasClamped || widthChanged)
                {
                    if (cursorPosition.x > xOffset + contentViewportWidth - cursorWidth || (xOffset > 0f && widthChanged))
                        x = Mathf.Max(Mathf.Ceil(cursorPosition.x + cursorWidth - contentViewportWidth), 0f);
                    else if (cursorPosition.x < xOffset + 5f)
                        x = Mathf.Max(cursorPosition.x - 5f, 0f);
                }
                if (textEdition.multiline && (Math.Abs(m_LastCursorPos.y - cursorPosition.y) > 0.05f || m_ScrollViewWasClamped))
                {
                    if (cursorPosition.y > contentRect.height + yOffset) y = cursorPosition.y - contentRect.height;
                    else if (cursorPosition.y < textSelection.lineHeightAtCursorPosition + yOffset + 0.05f) y = cursorPosition.y - textSelection.lineHeightAtCursorPosition;
                }
                m_LastCursorPos = cursorPosition;
                if (Math.Abs(xOffset - x) > 0.05f || Math.Abs(yOffset - y) > 0.05f) return new Vector2(x, y);
                return scrollView != null ? scrollView.scrollOffset : scrollOffset;
            }

            internal void SetScrollViewMode()
            {
                if (scrollView == null) return;
                textElement.RemoveFromClassList(verticalVariantInnerTextElementUssClassName);
                textElement.RemoveFromClassList(verticalHorizontalVariantInnerTextElementUssClassName);
                textElement.RemoveFromClassList(horizontalVariantInnerTextElementUssClassName);
                var whiteSpace = computedValues.whiteSpace;
                if (textEdition.multiline && (whiteSpace == WhiteSpace.Normal || whiteSpace == WhiteSpace.PreWrap))
                {
                    textElement.AddToClassList(verticalVariantInnerTextElementUssClassName);
                    scrollView.mode = ScrollViewMode.Vertical;
                }
                else if (textEdition.multiline)
                {
                    textElement.AddToClassList(verticalHorizontalVariantInnerTextElementUssClassName);
                    scrollView.mode = ScrollViewMode.VerticalAndHorizontal;
                }
                else
                {
                    textElement.AddToClassList(horizontalVariantInnerTextElementUssClassName);
                    scrollView.mode = ScrollViewMode.Horizontal;
                }
            }

            void SetMultilineContainerStyle()
            {
                if (multilineContainer == null) return;
                var whiteSpace = computedValues.whiteSpace;
                style.overflow = whiteSpace == WhiteSpace.Normal || whiteSpace == WhiteSpace.PreWrap ? Overflow.Hidden : (Overflow)2;
            }

            void RemoveSingleLineComponents()
            {
                RemoveFromClassList(TextInputBaseField<TValueType>.singleLineInputUssClassName);
                textElement.RemoveFromClassList(innerTextElementUssClassName);
                textElement.RemoveFromHierarchy();
                textElement.UnregisterCallback<GeometryChangedEvent>(TextElementOnGeometryChangedEvent);
            }

            void RemoveMultilineComponents()
            {
                if (scrollView != null)
                {
                    scrollView.RemoveFromHierarchy();
                    scrollView.contentContainer.UnregisterCallback<GeometryChangedEvent>(ScrollViewOnGeometryChangedEvent);
                    scrollView.verticalScroller.slider.UnregisterValueChangedCallback(MakeSureScrollViewDoesNotLeakEvents);
                    scrollView.horizontalScroller.slider.UnregisterValueChangedCallback(MakeSureScrollViewDoesNotLeakEvents);
                    scrollView = null;
                    textElement.RemoveFromClassList(verticalVariantInnerTextElementUssClassName);
                    textElement.RemoveFromClassList(verticalHorizontalVariantInnerTextElementUssClassName);
                    textElement.RemoveFromClassList(horizontalVariantInnerTextElementUssClassName);
                    RemoveFromClassList(TextInputBaseField<TValueType>.multilineInputWithScrollViewUssClassName);
                    textElement.RemoveFromClassList(innerTextElementWithScrollViewUssClassName);
                }
                if (multilineContainer != null)
                {
                    textElement.transform.position = Vector3.zero;
                    multilineContainer.RemoveFromHierarchy();
                    textElement.UnregisterCallback<GeometryChangedEvent>(TextElementOnGeometryChangedEvent);
                    multilineContainer = null;
                    RemoveFromClassList(TextInputBaseField<TValueType>.multilineInputUssClassName);
                }
            }

            internal bool SetVerticalScrollerVisibility(ScrollerVisibility sv)
            {
                if (!textEdition.multiline) return false;
                verticalScrollerVisibility = sv;
                if (scrollView == null) SetMultiline();
                else scrollView.verticalScrollerVisibility = verticalScrollerVisibility;
                return true;
            }

            public void SelectAll() => textSelection.SelectAll();
        }

        internal bool password
        {
            get => textEdition.isPassword;
            set => textEdition.isPassword = value;
        }

        internal bool selectWordByDoubleClick
        {
            get => textSelection.doubleClickSelectsWord;
            set => textSelection.doubleClickSelectsWord = value;
        }

        internal bool selectLineByTripleClick
        {
            get => textSelection.tripleClickSelectsLine;
            set => textSelection.tripleClickSelectsLine = value;
        }

        internal bool readOnly
        {
            get => isReadOnly;
            set => isReadOnly = value;
        }

        internal string placeholderText
        {
            get => textEdition.placeholder;
            set => textEdition.placeholder = value;
        }

        internal bool hidePlaceholderOnFocus
        {
            get => textEdition.hidePlaceholderOnFocus;
            set => textEdition.hidePlaceholderOnFocus = value;
        }

        protected internal TextInputBase textInputBase => m_TextInputBase;

        public ITextSelection textSelection => m_TextInputBase.textElement.selection;

        public ITextEdition textEdition => m_TextInputBase.textElement.edition;

        protected Action<bool> onIsReadOnlyChanged
        {
            get => m_TextInputBase.textElement.onIsReadOnlyChanged;
            set => m_TextInputBase.textElement.onIsReadOnlyChanged = value;
        }

        public bool isReadOnly
        {
            get => textEdition.isReadOnly;
            set => textEdition.isReadOnly = value;
        }

        public bool isPasswordField
        {
            get => textEdition.isPassword;
            set
            {
                if (textEdition.isPassword == value) return;
                textEdition.isPassword = value;
                m_TextInputBase.IncrementVersion(VersionChangeType.Repaint);
            }
        }

        public bool autoCorrection
        {
            get => textEdition.autoCorrection;
            set => textEdition.autoCorrection = value;
        }

        public bool hideMobileInput
        {
            get => textEdition.hideMobileInput;
            set => textEdition.hideMobileInput = value;
        }

        public TouchScreenKeyboardType keyboardType
        {
            get => textEdition.keyboardType;
            set => textEdition.keyboardType = value;
        }

        public TouchScreenKeyboard touchScreenKeyboard => textEdition.touchScreenKeyboard;

        public int maxLength
        {
            get => textEdition.maxLength;
            set => textEdition.maxLength = value;
        }

        public bool isDelayed
        {
            get => textEdition.isDelayed;
            set => textEdition.isDelayed = value;
        }

        public char maskChar
        {
            get => textEdition.maskChar;
            set => textEdition.maskChar = value;
        }

        [Obsolete("cursorColor is deprecated. Please use the corresponding USS property (--unity-cursor-color) instead.")]
        public Color selectionColor => m_TextInputBase.textElement.selectionColor;

        [Obsolete("cursorColor is deprecated. Please use the corresponding USS property (--unity-cursor-color) instead.")]
        public Color cursorColor => m_TextInputBase.textElement.cursorColor;

        public int cursorIndex
        {
            get => textSelection.cursorIndex;
            set => textSelection.cursorIndex = value;
        }

        public Vector2 cursorPosition => textSelection.cursorPosition;

        public int selectIndex
        {
            get => textSelection.selectIndex;
            set => textSelection.selectIndex = value;
        }

        public bool selectAllOnFocus
        {
            get => textSelection.selectAllOnFocus;
            set => textSelection.selectAllOnFocus = value;
        }

        public bool selectAllOnMouseUp
        {
            get => textSelection.selectAllOnMouseUp;
            set => textSelection.selectAllOnMouseUp = value;
        }

        public bool doubleClickSelectsWord
        {
            get => textSelection.doubleClickSelectsWord;
            set => textSelection.doubleClickSelectsWord = value;
        }

        public bool tripleClickSelectsLine
        {
            get => textSelection.tripleClickSelectsLine;
            set => textSelection.tripleClickSelectsLine = value;
        }

        public string text
        {
            get => m_TextInputBase.text;
            protected internal set => m_TextInputBase.text = value;
        }

        public bool emojiFallbackSupport
        {
            get => m_TextInputBase.textElement.emojiFallbackSupport;
            set
            {
                labelElement.emojiFallbackSupport = value;
                m_TextInputBase.textElement.emojiFallbackSupport = value;
            }
        }

        public ScrollerVisibility verticalScrollerVisibility
        {
            get => textInputBase.verticalScrollerVisibility;
            set
            {
                if (textInputBase.verticalScrollerVisibility != value) textInputBase.SetVerticalScrollerVisibility(value);
            }
        }

        private protected override bool canSwitchToMixedValue => !textInputBase.textElement.hasFocus;

        internal bool hasFocus => textInputBase.textElement.hasFocus;

        protected TextInputBaseField(int maxLength, char maskChar, TextInputBase textInputBase) : this(null, maxLength, maskChar, textInputBase) { }

        protected TextInputBaseField(string label, int maxLength, char maskChar, TextInputBase textInputBase) : base(label, textInputBase)
        {
            tabIndex = 0;
            delegatesFocus = true;
            labelElement.tabIndex = -1;
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            visualInput.AddToClassList(singleLineInputUssClassName);
            m_TextInputBase = textInputBase;
            m_TextInputBase.textEdition.maxLength = maxLength;
            m_TextInputBase.textEdition.maskChar = maskChar;
            RegisterCallback<CustomStyleResolvedEvent>(OnFieldCustomStyleResolved);
            textInputBase.textElement.OnPlaceholderChanged += OnPlaceholderChanged;
            m_UpdateTextFromValue = true;
        }

        public void SelectAll() => textSelection.SelectAll();

        public void SelectNone() => textSelection.SelectNone();

        public void SelectRange(int cursorIndex, int selectionIndex) => textSelection.SelectRange(cursorIndex, selectionIndex);

        [Obsolete("SetVerticalScrollerVisibility is deprecated. Use TextField.verticalScrollerVisibility instead.")]
        public bool SetVerticalScrollerVisibility(ScrollerVisibility sv) => textInputBase.SetVerticalScrollerVisibility(sv);

        public Vector2 MeasureTextSize(string textToMeasure, float width, MeasureMode widthMode, float height, MeasureMode heightMode) =>
            m_TextInputBase.textElement.MeasureTextSize(textToMeasure, width, widthMode, height, heightMode);

        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (textEdition.isReadOnly) return;
            if (evt.eventTypeId == NavigationSubmitEvent.TypeId() && evt.target != textInputBase.textElement)
            {
                textInputBase.textElement.Focus();
            }
            else if (evt.eventTypeId == NavigationMoveEvent.TypeId() && evt.target != textInputBase.textElement)
            {
                focusController.SwitchFocusOnEvent(textInputBase.textElement, evt);
            }
            else if (evt.eventTypeId == FocusInEvent.TypeId())
            {
                if (showMixedValue) ((INotifyValueChanged<string>)textInputBase.textElement).SetValueWithoutNotify(null);
            }
            else if (evt.eventTypeId == FocusEvent.TypeId())
            {
                UpdatePlaceholderClassList();
            }
            else if (evt.eventTypeId == BlurEvent.TypeId())
            {
                if (showMixedValue) UpdateMixedValueContent();
                UpdatePlaceholderClassList();
                textInputBase.UpdateScrollOffset();
            }
        }

        public override void SetValueWithoutNotify(TValueType newValue)
        {
            base.SetValueWithoutNotify(newValue);
            if (textInputBase.textElement.needsPlaceholderIfTextIsEmpty && string.IsNullOrEmpty(ValueToString(newValue)))
                visualInput.AddToClassList(placeholderUssClassName);
        }

        protected abstract string ValueToString(TValueType value);

        protected abstract TValueType StringToValue(string str);

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                if (m_UpdateTextFromValue) ((INotifyValueChanged<string>)textInputBase.textElement).SetValueWithoutNotify(mixedValueString);
                AddToClassList(mixedValueLabelUssClassName);
                visualInput?.AddToClassList(mixedValueLabelUssClassName);
            }
            else
            {
                UpdateTextFromValue();
                visualInput?.RemoveFromClassList(mixedValueLabelUssClassName);
                RemoveFromClassList(mixedValueLabelUssClassName);
            }
        }

        internal void OnPlaceholderChanged()
        {
            if (!string.IsNullOrEmpty(textEdition.placeholder)) RegisterCallback<ChangeEvent<TValueType>>(UpdatePlaceholderClassList);
            else UnregisterCallback<ChangeEvent<TValueType>>(UpdatePlaceholderClassList);
            UpdatePlaceholderClassList();
        }

        internal void UpdatePlaceholderClassList(ChangeEvent<TValueType> evt = null)
        {
            if (textInputBase.textElement.showPlaceholderText) visualInput.AddToClassList(placeholderUssClassName);
            else visualInput.RemoveFromClassList(placeholderUssClassName);
        }

        internal virtual void UpdateValueFromText() => value = StringToValue(text);

        internal virtual void UpdateTextFromValue() { }

        void OnFieldCustomStyleResolved(CustomStyleResolvedEvent e) => m_TextInputBase.OnInputCustomStyleResolved(e);
    }

    public partial class TextField : TextInputBaseField<string>
    {
        sealed class TextInput : TextInputBase
        {
            TextField parentTextField => (TextField)parent;

            public bool multiline
            {
                get => textEdition.multiline;
                set
                {
                    if ((value || string.IsNullOrEmpty(text) || !text.Contains("\n")) && textEdition.multiline == value) return;
                    textEdition.multiline = value;
                    if (value)
                    {
                        text = parentTextField.rawValue;
                        SetMultiline();
                    }
                    else
                    {
                        text = text.Replace("\n", "");
                        SetSingleLine();
                    }
                }
            }

            public override bool isPasswordField
            {
                set
                {
                    textEdition.isPassword = value;
                    if (value) multiline = false;
                }
            }

            protected override string StringToValue(string str) => str;
        }

        public new static readonly string ussClassName = "unity-text-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        TextInput textInput => (TextInput)textInputBase;

        public bool multiline
        {
            get => textInput.multiline;
            set => textInput.multiline = value;
        }

        public override string value
        {
            get => base.value;
            set
            {
                base.value = value;
                textEdition.UpdateText(rawValue);
            }
        }

        public TextField() : this(null) { }

        public TextField(int maxLength, bool multiline, bool isPasswordField, char maskChar) : this(null, maxLength, multiline, isPasswordField, maskChar) { }

        public TextField(string label) : this(label, -1, false, false, '*') { }

        public TextField(string label, int maxLength, bool multiline, bool isPasswordField, char maskChar) : base(label, maxLength, maskChar, new TextInput())
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            pickingMode = PickingMode.Ignore;
            SetValueWithoutNotify("");
            this.multiline = multiline;
            textEdition.isPassword = isPasswordField;
        }

        public override void SetValueWithoutNotify(string newValue)
        {
            base.SetValueWithoutNotify(newValue);
            var shown = rawValue;
            if (!multiline && rawValue != null) shown = rawValue.Replace("\n", "");
            ((INotifyValueChanged<string>)textInput.textElement).SetValueWithoutNotify(shown);
        }

        internal override void UpdateTextFromValue() => SetValueWithoutNotify(rawValue);

        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (isDelayed && evt?.eventTypeId == FocusOutEvent.TypeId())
            {
                var mode = dispatchMode;
                try
                {
                    dispatchMode = DispatchMode.Immediate;
                    value = text;
                }
                finally
                {
                    dispatchMode = mode;
                }
            }
        }

        protected override string ValueToString(string value) => value;

        protected override string StringToValue(string str) => str;
    }
}
