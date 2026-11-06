using System;
using System.Collections.Generic;

// RadioButton and RadioButtonGroup, from the Unity 6000.0 Manual pages "RadioButton" and
// "RadioButtonGroup" (UI Toolkit control reference) and the Scripting API pages of both types:
//   - "A RadioButton lets users select a single choice when used along with other RadioButtons in a
//     group. You can use a GroupBox to group RadioButtons. Otherwise, the panel acts as the default
//     group." A group is the nearest ancestor that is a group box (GroupBox, RadioButtonGroup), else
//     the root of the hierarchy (the panel's visual tree once attached).
//   - RadioButtonGroup: "Selecting one will deselect the others"; its value is the index of the
//     checked option ("the numbering sequence of the option in Value, starting at 0"); writing
//     `choices` "removes existing RadioButton elements, except those added explicitly to hierarchy,
//     and re-creates them to display the new list"; it has a container element.
//   - USS class names from the pages' USS class tables.
// Unbound's choices (unmeasured; the pages say nothing):
//   - a click checks an unchecked radio button and leaves a checked one checked;
//   - checking an option through `value` unchecks the others through their `value` (each sends its
//     ChangeEvent<bool>), then sets the group's `value` (ChangeEvent<int>), then the option's own
//     ChangeEvent<bool> is sent; SetValueWithoutNotify does the same silently;
//   - unchecking the group's checked option sets the group's value to -1;
//   - a new group's value is -1; a value outside the options checks none and is kept as given;
//   - option indices follow the hierarchy order of the group's radio buttons (nested groups
//     excluded); the buttons created from `choices` come first, showing each choice as their `text`;
//   - a radio button added to a RadioButtonGroup's container is reconciled with the group's value
//     (a checked one wins); removing one re-derives the value from the checked option (or -1);
//     radio buttons grouped by a GroupBox or the panel are reconciled only when one is checked;
//   - the group's container is its input element, and its content container;
//   - the mixed-value display removes the checkmark and clears the checked state;
//   - focus and keyboard navigation are BaseBoolField's (submit toggles, here: checks).
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
        bool m_NotifyOthers;
        bool m_SetByGroup;

        public override bool value
        {
            get => base.value;
            set
            {
                m_NotifyOthers = true;
                try
                {
                    base.value = value;
                }
                finally
                {
                    m_NotifyOthers = false;
                }
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
        }

        protected override void InitLabel()
        {
            base.InitLabel();
            m_Label.AddToClassList(textUssClassName);
        }

        protected override void ToggleValue()
        {
            if (!value) value = true;
        }

        [Obsolete("[UI Toolkit] Please set the value property instead.")]
        public void SetSelected(bool selected) => value = selected;

        public override void SetValueWithoutNotify(bool newValue)
        {
            bool notify = m_NotifyOthers;
            m_NotifyOthers = false;
            bool wasChecked = base.value;
            base.SetValueWithoutNotify(newValue);
            if (m_SetByGroup || wasChecked == newValue) return;
            if (newValue) OnChecked(notify);
            else OnUnchecked(notify);
        }

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

        /// <summary>Checks or unchecks this option on behalf of its group, without re-entering the
        /// group logic.</summary>
        internal void SetCheckedByGroup(bool on, bool notify)
        {
            m_SetByGroup = true;
            try
            {
                if (notify) value = on;
                else SetValueWithoutNotify(on);
            }
            finally
            {
                m_SetByGroup = false;
            }
        }

        void OnChecked(bool notify)
        {
            var root = GroupRootOf(this);
            if (root == null) return;
            var options = OptionsOf(root);
            foreach (var other in options)
                if (other != this && other.value) other.SetCheckedByGroup(false, notify);
            if (root is RadioButtonGroup group) group.OnOptionChecked(options.IndexOf(this), notify);
        }

        void OnUnchecked(bool notify)
        {
            if (!(GroupRootOf(this) is RadioButtonGroup group)) return;
            group.OnOptionUnchecked(OptionsOf(group).IndexOf(this), notify);
        }

        /// <summary>The element whose radio buttons form this option's group: the nearest group box
        /// ancestor, else the root of the hierarchy; null for a button without a parent.</summary>
        internal static VisualElement GroupRootOf(VisualElement option)
        {
            var current = option.hierarchy.parent;
            if (current == null) return null;
            while (true)
            {
                if (current is IGroupBox) return current;
                var up = current.hierarchy.parent;
                if (up == null) return current;
                current = up;
            }
        }

        /// <summary>The radio buttons of a group, in hierarchy order, without those of nested groups.</summary>
        internal static List<RadioButton> OptionsOf(VisualElement root)
        {
            var options = new List<RadioButton>();
            if (root != null) CollectOptions(root, options);
            return options;
        }

        static void CollectOptions(VisualElement element, List<RadioButton> options)
        {
            foreach (var child in element.hierarchy.Children())
            {
                if (child is RadioButton option)
                {
                    options.Add(option);
                    continue;
                }
                if (child is IGroupBox) continue;
                CollectOptions(child, options);
            }
        }
    }

    public partial class RadioButtonGroup : BaseField<int>, IGroupBox
    {
        public new static readonly string ussClassName = "unity-radio-button-group";
        public static readonly string containerUssClassName = ussClassName + "__container";

        readonly VisualElement m_Container;
        readonly List<RadioButton> m_CreatedOptions = new List<RadioButton>();
        List<string> m_Choices = new List<string>();
        bool m_Rebuilding;
        bool m_Syncing;

        public IEnumerable<string> choices
        {
            get => m_Choices;
            set
            {
                m_Rebuilding = true;
                try
                {
                    foreach (var created in m_CreatedOptions) created.RemoveFromHierarchy();
                    m_CreatedOptions.Clear();
                    m_Choices = value != null ? new List<string>(value) : new List<string>();
                    for (int i = 0; i < m_Choices.Count; i++)
                    {
                        var option = new RadioButton { text = m_Choices[i] };
                        m_CreatedOptions.Add(option);
                        m_Container.Insert(i, option);
                    }
                }
                finally
                {
                    m_Rebuilding = false;
                }
                SetValueWithoutNotify(rawValue);
            }
        }

        public override VisualElement contentContainer => m_Container ?? this;

        public RadioButtonGroup() : this(null) { }

        public RadioButtonGroup(string label, List<string> radioButtonChoices = null) : base(label, null)
        {
            AddToClassList(ussClassName);
            m_Container = visualInput;
            m_Container.AddToClassList(containerUssClassName);
            m_Container.elementAdded += OnElementAdded;
            m_Container.elementRemoved += OnElementRemoved;
            SetValueWithoutNotify(-1);
            choices = radioButtonChoices;
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            m_Syncing = true;
            try
            {
                var options = RadioButton.OptionsOf(this);
                for (int i = 0; i < options.Count; i++)
                {
                    bool on = i == rawValue;
                    if (options[i].value != on) options[i].SetCheckedByGroup(on, false);
                }
            }
            finally
            {
                m_Syncing = false;
            }
        }

        internal void OnOptionChecked(int index, bool notify)
        {
            if (m_Syncing) return;
            if (notify) value = index;
            else SetValueWithoutNotify(index);
        }

        internal void OnOptionUnchecked(int index, bool notify)
        {
            if (m_Syncing || index < 0 || index != rawValue) return;
            if (notify) value = -1;
            else SetValueWithoutNotify(-1);
        }

        void OnElementAdded(VisualElement child, int index)
        {
            if (m_Rebuilding || m_Syncing) return;
            var options = RadioButton.OptionsOf(this);
            int checkedIndex = child is RadioButton added && added.value ? options.IndexOf(added) : options.FindIndex(o => o.value);
            SetValueWithoutNotify(checkedIndex >= 0 ? checkedIndex : rawValue);
        }

        void OnElementRemoved(VisualElement child)
        {
            if (m_Rebuilding || m_Syncing) return;
            SetValueWithoutNotify(RadioButton.OptionsOf(this).FindIndex(o => o.value));
        }
    }

    // Declared here so that the hand-written code builds alone for `unbound gen-stubs`; their members
    // and the explicit implementations are generated.
    internal partial interface IGroupBox { }

    internal partial interface IGroupBoxOption { }
}
