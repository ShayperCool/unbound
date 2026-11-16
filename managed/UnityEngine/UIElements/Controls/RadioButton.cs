using Unity.Properties;
using System;
using System.Collections.Generic;

// RadioButton and RadioButtonGroup (Unity 6000.0 Manual "RadioButton" and "RadioButtonGroup",
// Scripting API of both types: "A RadioButton lets users select a single choice when used along with
// other RadioButtons in a group. You can use a GroupBox to group RadioButtons. Otherwise, the panel
// acts as the default group"; RadioButtonGroup's value is the index of the checked option).
// Measured (Unity 6000.0.71f1, tests/projects/radiogroups): a radio button joins its group when it is
// attached to a panel and leaves it when detached (GroupBoxUtility), so buttons outside a panel are
// not grouped; checking one through `value` sends its own change event first, then unchecks the
// others in the order they joined; SetValueWithoutNotify checks without unchecking the others; a
// RadioButtonGroup's options are the radio buttons below it in hierarchy order, the ones made from
// `choices` first.
namespace UnityEngine.UIElements
{
    public partial class RadioButton : BaseBoolField, IGroupBoxOption
    {
        public new static readonly string ussClassName = "unity-radio-button";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string checkmarkBackgroundUssClassName = ussClassName + "__checkmark-background";
        public static readonly string checkmarkUssClassName = ussClassName + "__checkmark";
        public static readonly string textUssClassName = ussClassName + "__text";

        readonly VisualElement m_CheckmarkBackground;

        public override bool value
        {
            get => base.value;
            set
            {
                if (base.value == value) return;
                base.value = value;
                UpdateCheckmark();
                if (value) this.OnOptionSelected();
            }
        }

        public RadioButton() : this(null) { }

        public RadioButton(string label) : base(label)
        {
            AddToClassList(ussClassName);
            visualInput.AddToClassList(inputUssClassName);
            labelElement.AddToClassList(labelUssClassName);
            m_CheckMark.AddToClassList(checkmarkUssClassName);
            m_CheckmarkBackground = new VisualElement { pickingMode = PickingMode.Ignore };
            m_CheckmarkBackground.AddToClassList(checkmarkBackgroundUssClassName);
            visualInput.Insert(visualInput.IndexOf(m_CheckMark), m_CheckmarkBackground);
            m_CheckmarkBackground.Add(m_CheckMark);
            UpdateCheckmark();
            RegisterCallback<AttachToPanelEvent>(_ => this.RegisterGroupBoxOption());
            RegisterCallback<DetachFromPanelEvent>(_ => this.UnregisterGroupBoxOption());
        }

        protected override void InitLabel()
        {
            base.InitLabel();
            m_Label.AddToClassList(textUssClassName);
        }

        /// <summary>A checked radio button stays checked when clicked.</summary>
        protected override void ToggleValue()
        {
            if (!value) value = true;
        }

        [global::System.@ObsoleteAttribute("[UI Toolkit] Please set the value property instead.", false)]
        public void SetSelected(bool selected) => ((IGroupBoxOption)this).SetSelected(selected);

        // Through `value`, so that options deselected by their group send their change events.
        void IGroupBoxOption.SetSelected(bool selected) => value = selected;

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            UpdateCheckmark();
        }

        void UpdateCheckmark() => m_CheckMark.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                visualInput.pseudoStates &= ~PseudoStates.Checked;
                pseudoStates &= ~PseudoStates.Checked;
                m_CheckMark.RemoveFromHierarchy();
                return;
            }
            if (m_CheckMark.hierarchy.parent != m_CheckmarkBackground) m_CheckmarkBackground.Add(m_CheckMark);
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

    public partial class RadioButtonGroup : BaseField<int>, IGroupBox
    {
        internal static readonly BindingId choicesProperty = nameof(choices);
        public new static readonly string ussClassName = "unity-radio-button-group";
        public static readonly string containerUssClassName = ussClassName + "__container";
        internal static readonly string containerName = "contentContainer";
        internal static readonly string choicesContainerName = "choicesContentContainer";

        readonly VisualElement m_ChoicesContainer;
        readonly VisualElement m_ContentContainer;
        readonly List<RadioButton> m_RegisteredRadioButtons = new List<RadioButton>();
        readonly List<string> m_Choices = new List<string>();
        readonly EventCallback<ChangeEvent<bool>> m_RadioButtonValueChanged;
        RadioButton m_SelectedRadioButton;
        IVisualElementScheduledItem m_ScheduledUpdate;

        /// <summary>The texts of the group's radio buttons; setting it renames the buttons made from
        /// choices, makes more or removes the extra ones.</summary>
        [CreateProperty]
        public IEnumerable<string> choices
        {
            get
            {
                using var pooled = Pool.ListPool<RadioButton>.Get(out var buttons);
                GetAllRadioButtons(buttons);
                foreach (var button in buttons) yield return button.text;
            }
            set
            {
                if (value != null ? SameChoices(value) : m_Choices.Count == 0) return;
                m_Choices.Clear();
                if (value != null) m_Choices.AddRange(value);
                RebuildChoiceButtons();
                NotifyPropertyChanged(choicesProperty);
            }
        }

        internal List<string> choicesList
        {
            get => m_Choices;
            set => choices = value;
        }

        public override VisualElement contentContainer => m_ContentContainer ?? this;

        public RadioButtonGroup() : this(null) { }

        public RadioButtonGroup(string label, List<string> radioButtonChoices = null) : base(label, null)
        {
            AddToClassList(ussClassName);
            m_RadioButtonValueChanged = OnRadioButtonValueChanged;
            if (Unbound.Runtime.Compat.DefaultConstructorStateMeasured)
            {
                visualInput.Add(m_ChoicesContainer = new VisualElement { name = choicesContainerName });
                m_ChoicesContainer.AddToClassList(containerUssClassName);
                visualInput.Add(m_ContentContainer = new VisualElement { name = containerName });
                m_ContentContainer.AddToClassList(containerUssClassName);
                visualInput.focusable = false;
                delegatesFocus = true;
            }
            else
            {
                // Releases without a measured default state keep the options in the input element.
                m_ChoicesContainer = m_ContentContainer = visualInput;
                visualInput.AddToClassList(containerUssClassName);
            }
            choices = radioButtonChoices;
            value = -1;
        }

        bool SameChoices(IEnumerable<string> other)
        {
            int i = 0;
            foreach (var choice in other)
            {
                if (i >= m_Choices.Count || !string.Equals(m_Choices[i], choice)) return false;
                i++;
            }
            return i == m_Choices.Count;
        }

        void RebuildChoiceButtons()
        {
            if (m_Choices.Count == 0 && ReferenceEquals(m_ChoicesContainer, m_ContentContainer))
            {
                // One shared container: remove only the buttons made from choices.
                for (int j = m_ChoicesContainer.childCount - 1; j >= 0; j--)
                    if (m_ChoicesContainer[j] is RadioButton made && m_MadeFromChoices.Contains(made)) made.RemoveFromHierarchy();
                m_MadeFromChoices.Clear();
                return;
            }
            if (m_Choices.Count == 0)
            {
                m_ChoicesContainer.Clear();
                m_MadeFromChoices.Clear();
                return;
            }
            int i = 0;
            foreach (var choice in m_Choices)
            {
                if (i < m_MadeFromChoices.Count)
                {
                    m_MadeFromChoices[i].text = choice;
                    ScheduleRadioButtons();
                }
                else
                {
                    var button = new RadioButton { text = choice };
                    m_MadeFromChoices.Add(button);
                    m_ChoicesContainer.Add(button);
                }
                i++;
            }
            // The extra buttons leave their group, which unregisters them.
            for (int j = m_MadeFromChoices.Count - 1; j >= i; j--)
            {
                m_MadeFromChoices[j].RemoveFromHierarchy();
                m_MadeFromChoices.RemoveAt(j);
            }
        }

        readonly List<RadioButton> m_MadeFromChoices = new List<RadioButton>();

        /// <summary>The group's radio buttons in hierarchy order, into a list of the caller's (a
        /// change event sent while one is in use may need another).</summary>
        void GetAllRadioButtons(List<RadioButton> buttons)
        {
            buttons.Clear();
            this.Query<RadioButton>().ForEach(buttons.Add);
        }

        int IndexOf(RadioButton button)
        {
            using var pooled = Pool.ListPool<RadioButton>.Get(out var buttons);
            GetAllRadioButtons(buttons);
            return buttons.IndexOf(button);
        }

        void OnRadioButtonValueChanged(ChangeEvent<bool> evt)
        {
            if (!evt.newValue) return;
            value = IndexOf(evt.target as RadioButton);
            evt.StopPropagation();
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            UpdateRadioButtons(true);
        }

        /// <summary>Checks the button at the value's index and unchecks the others; with no such
        /// button, unchecks the registered ones. Only while the group is in a panel.</summary>
        void UpdateRadioButtons(bool notify)
        {
            m_ScheduledUpdate?.Pause();
            if (panel == null) return;
            using var pooled = Pool.ListPool<RadioButton>.Get(out var buttons);
            GetAllRadioButtons(buttons);
            if (value >= 0 && value < buttons.Count)
            {
                m_SelectedRadioButton = buttons[value];
                if (notify) m_SelectedRadioButton.value = true;
                else m_SelectedRadioButton.SetValueWithoutNotify(true);
                foreach (var button in buttons)
                {
                    if (button == m_SelectedRadioButton) continue;
                    if (notify) button.value = false;
                    else button.SetValueWithoutNotify(false);
                }
            }
            else
            {
                using var registered = Pool.ListPool<RadioButton>.Get(out var copy);
                copy.AddRange(m_RegisteredRadioButtons);
                foreach (var button in copy)
                {
                    if (notify) button.value = false;
                    else button.SetValueWithoutNotify(false);
                }
            }
        }

        /// <summary>Reconciles the buttons on the next scheduler update, once however often asked.</summary>
        void ScheduleRadioButtons()
        {
            if (m_ScheduledUpdate == null) m_ScheduledUpdate = schedule.Execute(() => UpdateRadioButtons(false));
            else if (!m_ScheduledUpdate.isActive) m_ScheduledUpdate.Resume();
        }

        void RegisterRadioButton(RadioButton button)
        {
            if (m_RegisteredRadioButtons.Contains(button)) return;
            m_RegisteredRadioButtons.Add(button);
            button.RegisterValueChangedCallback(m_RadioButtonValueChanged);
            // A button checked before it joined sets the value of a group with none.
            if (value == -1 && button.value) SetValueWithoutNotify(IndexOf(button));
            ScheduleRadioButtons();
        }

        void UnregisterRadioButton(RadioButton button)
        {
            if (!m_RegisteredRadioButtons.Remove(button)) return;
            button.UnregisterValueChangedCallback(m_RadioButtonValueChanged);
            UpdateRadioButtons(false);
        }

        void IGroupBox.OnOptionAdded(IGroupBoxOption option)
        {
            if (!(option is RadioButton button)) throw new ArgumentException("[UI Toolkit] Internal group box error. Expected a radio button element.");
            RegisterRadioButton(button);
        }

        void IGroupBox.OnOptionRemoved(IGroupBoxOption option)
        {
            if (!(option is RadioButton button)) throw new ArgumentException("[UI Toolkit] Internal group box error. Expected a radio button element.");
            UnregisterRadioButton(button);
            if (m_SelectedRadioButton != button) return;
            m_SelectedRadioButton = null;
            value = -1;
        }
    }
}
