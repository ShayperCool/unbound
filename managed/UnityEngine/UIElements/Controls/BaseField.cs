using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    /// <summary>A labelled field holding a value: a Label then an input element, as a composite
    /// focus root delegating focus to its input.</summary>
    public abstract partial class BaseField<TValueType> : BindableElement, INotifyValueChanged<TValueType>
    {
        public static readonly string ussClassName = "unity-base-field";
        public static readonly string labelUssClassName = ussClassName + "__label";
        public static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string noLabelVariantUssClassName = ussClassName + "--no-label";
        public static readonly string labelDraggerVariantUssClassName = labelUssClassName + "--with-dragger";
        public static readonly string mixedValueLabelUssClassName = labelUssClassName + "--mixed-value";
        public static readonly string alignedFieldUssClassName = ussClassName + "__aligned";
        protected internal static readonly string mixedValueString = "—";

        VisualElement m_VisualInput;
        TValueType m_Value;
        bool m_ShowMixedValue;
        Label m_MixedValueLabel;
        bool m_SkipValidation;

        internal VisualElement visualInput
        {
            get => m_VisualInput;
            set
            {
                if (m_VisualInput != null)
                {
                    if (m_VisualInput.parent == this) m_VisualInput.RemoveFromHierarchy();
                    m_VisualInput = null;
                }
                m_VisualInput = value ?? new VisualElement { pickingMode = PickingMode.Ignore };
                m_VisualInput.focusable = true;
                m_VisualInput.AddToClassList(inputUssClassName);
                Add(m_VisualInput);
            }
        }

        protected TValueType rawValue
        {
            get => m_Value;
            set => m_Value = value;
        }

        internal DispatchMode dispatchMode { get; set; } = DispatchMode.Default;

        public virtual TValueType value
        {
            get => m_Value;
            set
            {
                if (EqualsCurrentValue(value) && !showMixedValue) return;
                var previous = m_Value;
                SetValueWithoutNotify(value);
                showMixedValue = false;
                if (panel == null) return;
                using (var evt = ChangeEvent<TValueType>.GetPooled(previous, m_Value))
                {
                    evt.elementTarget = this;
                    SendEvent(evt, dispatchMode);
                }
            }
        }

        public Label labelElement { get; private set; }

        public string label
        {
            get => labelElement.text;
            set
            {
                if (labelElement.text == value) return;
                labelElement.text = value;
                if (string.IsNullOrEmpty(labelElement.text))
                {
                    AddToClassList(noLabelVariantUssClassName);
                    labelElement.RemoveFromHierarchy();
                }
                else if (!Contains(labelElement))
                {
                    hierarchy.Insert(0, labelElement);
                    RemoveFromClassList(noLabelVariantUssClassName);
                }
            }
        }

        public bool showMixedValue
        {
            get => m_ShowMixedValue;
            set
            {
                if (value == m_ShowMixedValue || (value && !canSwitchToMixedValue)) return;
                m_ShowMixedValue = value;
                UpdateMixedValueContent();
            }
        }

        private protected virtual bool canSwitchToMixedValue => true;

        protected Label mixedValueLabel
        {
            get
            {
                if (m_MixedValueLabel == null)
                {
                    m_MixedValueLabel = new Label(mixedValueString) { focusable = true, tabIndex = -1 };
                    m_MixedValueLabel.AddToClassList(labelUssClassName);
                    m_MixedValueLabel.AddToClassList(mixedValueLabelUssClassName);
                }
                return m_MixedValueLabel;
            }
        }

        internal event Func<TValueType, TValueType> onValidateValue;

        internal BaseField(string label)
        {
            isCompositeRoot = true;
            focusable = true;
            tabIndex = 0;
            excludeFromFocusRing = true;
            delegatesFocus = true;
            AddToClassList(ussClassName);
            labelElement = new Label { focusable = true, tabIndex = -1 };
            labelElement.AddToClassList(labelUssClassName);
            if (label != null) this.label = label;
            else AddToClassList(noLabelVariantUssClassName);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
            m_VisualInput = null;
        }

        protected BaseField(string label, VisualElement visualInput) : this(label)
        {
            this.visualInput = visualInput;
        }

        internal virtual bool EqualsCurrentValue(TValueType value) => EqualityComparer<TValueType>.Default.Equals(m_Value, value);

        void OnAttachToPanel(AttachToPanelEvent e) => RegisterEditingCallbacks();

        void OnDetachFromPanel(DetachFromPanelEvent e)
        {
            UnregisterEditingCallbacks();
            onValidateValue = null;
        }

        internal virtual void RegisterEditingCallbacks()
        {
            RegisterCallback<FocusInEvent>(StartEditing);
            RegisterCallback<FocusOutEvent>(EndEditing);
        }

        internal virtual void UnregisterEditingCallbacks()
        {
            UnregisterCallback<FocusInEvent>(StartEditing);
            UnregisterCallback<FocusOutEvent>(EndEditing);
        }

        internal void StartEditing(EventBase e) { }

        internal void EndEditing(EventBase e) { }

        internal TValueType ValidatedValue(TValueType value) => onValidateValue != null ? onValidateValue(value) : value;

        protected override void HandleEventBubbleUp(EventBase evt)
        {
            if (evt is TooltipEvent tooltip)
            {
                if (tooltip.elementTarget == labelElement || !string.IsNullOrEmpty(labelElement?.tooltip) || string.IsNullOrEmpty(label))
                    tooltip.rect = !string.IsNullOrEmpty(label) && string.IsNullOrEmpty(labelElement.tooltip) ? labelElement.worldBound : worldBound;
                else
                    tooltip.StopImmediatePropagation();
                return;
            }
            base.HandleEventBubbleUp(evt);
        }

        protected virtual void UpdateMixedValueContent() => throw new NotImplementedException();

        public virtual void SetValueWithoutNotify(TValueType newValue)
        {
            m_Value = m_SkipValidation ? newValue : ValidatedValue(newValue);
            MarkDirtyRepaint();
            if (showMixedValue) UpdateMixedValueContent();
        }

        internal void SetValueWithoutValidation(TValueType newValue)
        {
            m_SkipValidation = true;
            value = newValue;
            m_SkipValidation = false;
        }
    }

    /// <summary>A field toggled by clicks: a checkmark inside the input, then an optional text label.</summary>
    public abstract partial class BaseBoolField : BaseField<bool>
    {
        protected Label m_Label;
        protected internal readonly VisualElement m_CheckMark;
        internal readonly Clickable m_Clickable;
        string m_OriginalText;

        internal Label boolFieldLabelElement => m_Label;

        internal bool acceptClicksIfDisabled
        {
            get => m_Clickable.acceptClicksIfDisabled;
            set => m_Clickable.acceptClicksIfDisabled = value;
        }

        public bool toggleOnLabelClick { get; set; } = true;

        internal bool toggleOnTextClick { get; set; } = true;

        public string text
        {
            get => m_Label?.text;
            set
            {
                if (string.CompareOrdinal(m_Label?.text, value) == 0) return;
                if (!string.IsNullOrEmpty(value))
                {
                    InitLabel();
                    m_Label.text = value;
                }
                else if (m_Label != null)
                {
                    m_Label.RemoveFromHierarchy();
                    m_Label.text = value;
                }
            }
        }

        public BaseBoolField(string label) : base(label, null)
        {
            m_CheckMark = new VisualElement { name = "unity-checkmark", pickingMode = PickingMode.Ignore };
            visualInput.Add(m_CheckMark);
            visualInput.pickingMode = PickingMode.Position;
            labelElement.focusable = false;
            text = null;
            this.AddManipulator(m_Clickable = new Clickable(OnClickEvent));
            RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            ToggleValue();
            evt.StopPropagation();
        }

        protected virtual void InitLabel()
        {
            if (m_Label == null) m_Label = new Label();
            else if (m_Label.parent != null) return;
            if (m_CheckMark.hierarchy.parent != visualInput)
            {
                visualInput.Add(m_Label);
                return;
            }
            visualInput.Insert(visualInput.IndexOf(m_CheckMark) + 1, m_Label);
        }

        public override void SetValueWithoutNotify(bool newValue)
        {
            if (newValue)
            {
                visualInput.pseudoStates |= PseudoStates.Checked;
                pseudoStates |= PseudoStates.Checked;
            }
            else
            {
                visualInput.pseudoStates &= ~PseudoStates.Checked;
                pseudoStates &= ~PseudoStates.Checked;
            }
            base.SetValueWithoutNotify(newValue);
        }

        void OnClickEvent(EventBase evt)
        {
            if (evt.eventTypeId == MouseUpEvent.TypeId())
            {
                var mouse = (IMouseEvent)evt;
                if (!ShouldIgnoreClick(mouse.mousePosition) && mouse.button == 0) ToggleValue();
            }
            else if (evt.eventTypeId == PointerUpEvent.TypeId() || evt.eventTypeId == ClickEvent.TypeId())
            {
                var pointer = (IPointerEvent)evt;
                if (!ShouldIgnoreClick(pointer.position) && pointer.button == 0) ToggleValue();
            }
        }

        bool ShouldIgnoreClick(Vector3 position)
        {
            if (!toggleOnLabelClick && labelElement.worldBound.Contains(position)) return true;
            if (!toggleOnTextClick && m_Label != null && m_Label.worldBound.Contains(position)) return true;
            return false;
        }

        protected virtual void ToggleValue() => value = !value;

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                visualInput.pseudoStates &= ~PseudoStates.Checked;
                pseudoStates &= ~PseudoStates.Checked;
                m_CheckMark.RemoveFromHierarchy();
                visualInput.Add(mixedValueLabel);
                m_OriginalText = text;
                text = "";
            }
            else
            {
                mixedValueLabel.RemoveFromHierarchy();
                visualInput.Add(m_CheckMark);
                if (m_OriginalText != null) text = m_OriginalText;
            }
        }

        internal override void RegisterEditingCallbacks()
        {
            RegisterCallback<PointerUpEvent>(StartEditing);
            RegisterCallback<FocusOutEvent>(EndEditing);
        }

        internal override void UnregisterEditingCallbacks()
        {
            UnregisterCallback<PointerUpEvent>(StartEditing);
            UnregisterCallback<FocusOutEvent>(EndEditing);
        }
    }

    public partial class Toggle : BaseBoolField
    {
        public new static readonly string ussClassName = "unity-toggle";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        [Obsolete]
        public static readonly string noTextVariantUssClassName = ussClassName + "--no-text";
        public static readonly string checkmarkUssClassName = ussClassName + "__checkmark";
        public static readonly string textUssClassName = ussClassName + "__text";
        public static readonly string mixedValuesUssClassName = ussClassName + "__mixed-values";

        public Toggle() : this(null) { }

        public Toggle(string label) : base(label)
        {
            AddToClassList(ussClassName);
            visualInput.AddToClassList(inputUssClassName);
            labelElement.AddToClassList(labelUssClassName);
            m_CheckMark.AddToClassList(checkmarkUssClassName);
        }

        protected override void InitLabel()
        {
            base.InitLabel();
            m_Label.AddToClassList(textUssClassName);
        }

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                visualInput.pseudoStates &= ~PseudoStates.Checked;
                pseudoStates &= ~PseudoStates.Checked;
                m_CheckMark.AddToClassList(mixedValuesUssClassName);
                return;
            }
            m_CheckMark.RemoveFromClassList(mixedValuesUssClassName);
            if (value)
            {
                visualInput.pseudoStates |= PseudoStates.Checked;
                pseudoStates |= PseudoStates.Checked;
            }
            else
            {
                visualInput.pseudoStates &= ~PseudoStates.Checked;
                pseudoStates &= ~PseudoStates.Checked;
            }
        }
    }
}
