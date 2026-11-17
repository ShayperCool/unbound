using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Properties;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// The options of a toggle button group: up to 64 on/off options as a bit mask with a length
    /// (Unity 6000.0 Scripting API ToggleButtonGroupState). Bits at and beyond the length are kept
    /// clear by the constructor and the whole-set operations. Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/uitkcontrols).
    /// </summary>
    [Serializable]
    public partial struct ToggleButtonGroupState : IEquatable<ToggleButtonGroupState>, IComparable<ToggleButtonGroupState>
    {
        internal const int maxLength = 64;

        [SerializeField] ulong m_Data;
        [SerializeField] int m_Length;

        public ToggleButtonGroupState(ulong optionsBitMask, int length)
        {
            if (length < 0 || length > maxLength)
                throw new ArgumentOutOfRangeException(nameof(length), string.Format("length of {0} should be greater than or equal to 0 and less than or equal to {1}.", length, maxLength));
            m_Data = optionsBitMask;
            m_Length = length;
            ResetOptions(m_Length);
        }

        public int length
        {
            get => m_Length;
            internal set => m_Length = value;
        }

        internal ulong data => m_Data;

        public bool this[int index]
        {
            get
            {
                CheckIndex(index);
                ulong bit = 1UL << (index & 63);
                return (m_Data & bit) == bit;
            }
            set
            {
                CheckIndex(index);
                ulong bit = 1UL << (index & 63);
                if (value) m_Data |= bit;
                else m_Data &= ~bit;
            }
        }

        void CheckIndex(int index)
        {
            if (index < 0 || index >= m_Length)
                throw new ArgumentOutOfRangeException(nameof(index), string.Format("index of {0} should be in the range of 0 and {1} inclusively.", index, m_Length - 1));
        }

        public Span<int> GetActiveOptions(Span<int> activeOptionsIndices) => CollectOptions(activeOptionsIndices, true);

        public Span<int> GetInactiveOptions(Span<int> inactiveOptionsIndices) => CollectOptions(inactiveOptionsIndices, false);

        Span<int> CollectOptions(Span<int> indices, bool active)
        {
            if (indices.Length < m_Length)
                throw new ArgumentException(string.Format("indices' length ({0}) should be equal to or greater than the ToggleButtonGroupState's length ({1}).", indices.Length, m_Length));
            int count = 0;
            for (int i = 0; i < m_Length; i++)
                if (this[i] == active) indices[count++] = i;
            return indices.Slice(0, count);
        }

        public void SetAllOptions()
        {
            m_Data = ulong.MaxValue;
            ResetOptions(m_Length);
        }

        public void ResetAllOptions() => m_Data = 0;

        public void ToggleAllOptions()
        {
            m_Data = ~m_Data;
            ResetOptions(m_Length);
        }

        public static ToggleButtonGroupState CreateFromOptions(IList<bool> options)
        {
            int count = options.Count;
            var state = new ToggleButtonGroupState(0, count);
            for (int i = 0; i < count; i++) state[i] = options[i];
            return state;
        }

        public static ToggleButtonGroupState FromEnumFlags<T>(T options, int length = -1) where T : Enum
        {
            CheckFlags<T>();
            if (length == -1) length = BitsOf(typeof(T), 0);
            return new ToggleButtonGroupState(BitsOf(options), length);
        }

        public static T ToEnumFlags<T>(ToggleButtonGroupState options, bool acceptsLengthMismatch = true) where T : Enum
        {
            CheckFlags<T>();
            int size = BitsOf(typeof(T), -1);
            if (!acceptsLengthMismatch && options.m_Length != size)
                throw new ArgumentException("Cannot sync to enum flag since the ToggleButtonGroupState has a different amount of options.");
            return (T)Enum.Parse(typeof(T), options.m_Data.ToString());
        }

        /// <summary>1 when equal, else -1, as the Unity 6000.0.71f1 Editor returns (tests/projects/uitkcontrols).</summary>
        public int CompareTo(ToggleButtonGroupState other) => other == this ? 1 : -1;

        public static bool Compare<T>(ToggleButtonGroupState options, T value) where T : Enum
        {
            CheckFlags<T>();
            return options.m_Data == BitsOf(value);
        }

        static void CheckFlags<T>() where T : Enum
        {
            if (!TypeTraits<T>.IsEnumFlags) throw new ArgumentException("Enum type T is not a flag enum type.");
        }

        /// <summary>The number of options an enum's underlying type holds, else <paramref name="otherwise"/>.</summary>
        static int BitsOf(Type enumType, int otherwise) => Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte or TypeCode.Byte => 8,
            TypeCode.Int16 or TypeCode.UInt16 => 16,
            TypeCode.Int32 or TypeCode.UInt32 => 32,
            TypeCode.Int64 or TypeCode.UInt64 => 64,
            _ => otherwise,
        };

        /// <summary>An enum value's options: its low 32 bits, sign-extended (the release reads the value
        /// as a 32-bit integer); narrower enums read their own bits.</summary>
        static ulong BitsOf<T>(T value) where T : Enum => Unsafe.SizeOf<T>() switch
        {
            1 => Unsafe.As<T, byte>(ref value),
            2 => Unsafe.As<T, ushort>(ref value),
            _ => (ulong)(long)Unsafe.As<T, int>(ref value),
        };

        void ResetOptions(int startingIndex)
        {
            for (int i = startingIndex; i < maxLength; i++) m_Data &= ~(1UL << (i & 63));
        }

        public static bool operator ==(ToggleButtonGroupState lhs, ToggleButtonGroupState rhs) => lhs.Equals(rhs);

        public static bool operator !=(ToggleButtonGroupState lhs, ToggleButtonGroupState rhs) => !(lhs == rhs);

        public bool Equals(ToggleButtonGroupState other) => m_Data == other.m_Data && m_Length == other.m_Length;

        public override bool Equals(object obj) => obj is ToggleButtonGroupState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(m_Data, m_Length);

        public override string ToString() => Convert.ToString((long)m_Data, 2).PadLeft(length, '0');
    }

    /// <summary>Options for a ToggleButtonGroupState field drawn in an inspector.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed partial class ToggleButtonGroupStatePropertiesAttribute : PropertyAttribute
    {
        public ToggleButtonGroupStatePropertiesAttribute(bool allowMultipleSelection = true, bool allowEmptySelection = true, int length = -1)
        {
            this.allowMultipleSelection = allowMultipleSelection;
            this.allowEmptySelection = allowEmptySelection;
            this.length = length;
        }

        public bool allowMultipleSelection { get; }

        public bool allowEmptySelection { get; }

        public int length { get; }
    }

    /// <summary>
    /// A row of buttons whose checked states are the field's value (Unity 6000.0 Manual
    /// "ToggleButtonGroup"). Buttons added to it join in hierarchy order, get the group's USS classes
    /// (first, middle, last or standalone) and toggle their option when clicked; other elements added
    /// go to the group itself. With single selection a click selects only its option; with
    /// allowEmptySelection the selected option can be cleared; otherwise one option stays selected.
    /// Matches the Unity 6000.0.71f1 Editor (tests/projects/uitkcontrols).
    /// </summary>
    [UxmlElement(null, typeof(Button))]
    public partial class ToggleButtonGroup : BaseField<ToggleButtonGroupState>
    {
        static readonly string k_MaxToggleButtonGroupMessage = string.Format("The number of buttons added to ToggleButtonGroup exceeds the maximum allowed ({0}). The newly added button will not be treated as part of this control.", ToggleButtonGroupState.maxLength);
        internal static readonly BindingId isMultipleSelectionProperty = nameof(isMultipleSelection);
        internal static readonly BindingId allowEmptySelectionProperty = nameof(allowEmptySelection);

        public new static readonly string ussClassName = "unity-toggle-button-group";
        public static readonly string containerUssClassName = ussClassName + "__container";
        public static readonly string buttonGroupClassName = "unity-button-group";
        public static readonly string buttonClassName = buttonGroupClassName + "__button";
        public static readonly string buttonLeftClassName = buttonClassName + "--left";
        public static readonly string buttonMidClassName = buttonClassName + "--mid";
        public static readonly string buttonRightClassName = buttonClassName + "--right";
        public static readonly string buttonStandaloneClassName = buttonClassName + "--standalone";
        public static readonly string emptyStateLabelClassName = buttonGroupClassName + "__empty-label";

        const string k_EmptyStateLabel = "Group has no buttons.";

        readonly VisualElement m_ButtonGroupContainer;
        List<Button> m_Buttons = new List<Button>();
        VisualElement m_EmptyLabel;
        bool m_IsMultipleSelection;
        bool m_AllowEmptySelection;

        [CreateProperty]
        public bool isMultipleSelection
        {
            get => m_IsMultipleSelection;
            set
            {
                if (m_IsMultipleSelection == value) return;
                var state = this.value;
                Span<int> buffer = stackalloc int[state.length];
                var active = state.GetActiveOptions(buffer);
                // Leaving multiple selection keeps only the first selected option.
                if (active.Length > 1 && m_Buttons.Count > 0)
                {
                    state.ResetAllOptions();
                    state[active[0]] = true;
                    SetValueWithoutNotify(state);
                }
                m_IsMultipleSelection = value;
                NotifyPropertyChanged(isMultipleSelectionProperty);
            }
        }

        [CreateProperty]
        public bool allowEmptySelection
        {
            get => m_AllowEmptySelection;
            set
            {
                if (m_AllowEmptySelection == value) return;
                if (!value)
                {
                    // Disallowing an empty selection selects the first option when none is.
                    var state = this.value;
                    Span<int> buffer = stackalloc int[state.length];
                    var active = state.GetActiveOptions(buffer);
                    if (active.Length == 0 && m_Buttons.Count > 0)
                    {
                        state[0] = true;
                        SetValueWithoutNotify(state);
                    }
                }
                m_AllowEmptySelection = value;
                NotifyPropertyChanged(allowEmptySelectionProperty);
            }
        }

        public ToggleButtonGroup() : this(null) { }

        public ToggleButtonGroup(string label) : this(label, new ToggleButtonGroupState(0, ToggleButtonGroupState.maxLength)) { }

        public ToggleButtonGroup(ToggleButtonGroupState toggleButtonGroupState) : this(null, toggleButtonGroupState) { }

        public ToggleButtonGroup(string label, ToggleButtonGroupState toggleButtonGroupState) : base(label)
        {
            AddToClassList(ussClassName);
            var container = new VisualElement { name = containerUssClassName };
            container.AddToClassList(buttonGroupClassName);
            container.focusable = false;
            visualInput = container;
            m_ButtonGroupContainer = visualInput;
            m_ButtonGroupContainer.elementAdded += OnButtonGroupContainerElementAdded;
            m_ButtonGroupContainer.elementRemoved += OnButtonGroupContainerElementRemoved;
            SetValueWithoutNotify(toggleButtonGroupState);
        }

        public override VisualElement contentContainer => m_ButtonGroupContainer ?? this;

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue)
            {
                foreach (var button in m_Buttons)
                {
                    button.pseudoStates &= ~PseudoStates.Checked;
                    button.IncrementVersion(VersionChangeType.Repaint);
                }
            }
            else
            {
                SetValueWithoutNotify(value);
            }
        }

        public override void SetValueWithoutNotify(ToggleButtonGroupState newValue)
        {
            if (newValue.length == 0)
            {
                // A group of no options shows a label in place of its buttons.
                newValue = new ToggleButtonGroupState(0, 0);
                if (m_EmptyLabel == null)
                {
                    m_EmptyLabel = new Label(k_EmptyStateLabel) { name = emptyStateLabelClassName };
                    m_EmptyLabel.AddToClassList(emptyStateLabelClassName);
                }
                visualInput.Insert(0, m_EmptyLabel);
            }
            else
            {
                m_EmptyLabel?.RemoveFromHierarchy();
            }
            base.SetValueWithoutNotify(newValue);
            UpdateButtonStates(newValue);
        }

        void OnButtonGroupContainerElementAdded(VisualElement ve, int index)
        {
            if (!(ve is Button button))
            {
                // Only buttons are options: other elements move to the group itself.
                if (ve != m_EmptyLabel) hierarchy.Add(ve);
                return;
            }
            if (m_Buttons.Count + 1 > ToggleButtonGroupState.maxLength)
            {
                Debug.LogWarning(k_MaxToggleButtonGroupMessage);
                return;
            }
            button.AddToClassList(buttonClassName);
            button.clickable.clickedWithEventInfo += OnOptionChange;
            m_Buttons = m_ButtonGroupContainer.Query<Button>().ToList();
            UpdateButtonsStyling();
            bool changed = false;
            var state = value;
            if (m_Buttons.Count < value.length || m_Buttons.Count > ToggleButtonGroupState.maxLength)
            {
                // The value never has more options than buttons.
            }
            else
            {
                state.length = m_Buttons.Count;
                changed = true;
            }
            if (value.data == 0 && !allowEmptySelection)
            {
                state[0] = true;
                changed = true;
            }
            if (changed) value = state;
        }

        void OnButtonGroupContainerElementRemoved(VisualElement ve)
        {
            if (!(ve is Button button)) return;
            var state = value;
            int index = m_Buttons.IndexOf(button);
            Span<int> buffer = stackalloc int[state.length];
            var active = state.GetActiveOptions(buffer);
            bool wasActive = active.IndexOf(index) != -1;
            button.clickable.clickedWithEventInfo -= OnOptionChange;
            if (wasActive) m_Buttons[index].pseudoStates &= ~PseudoStates.Checked;
            m_Buttons.Remove(button);
            UpdateButtonsStyling();
            state.length = m_Buttons.Count;
            if (m_Buttons.Count == 0)
            {
                state.ResetAllOptions();
                SetValueWithoutNotify(state);
                return;
            }
            if (!wasActive) return;
            state[index] = false;
            if (!allowEmptySelection && active.Length == 1) state[0] = true;
            value = state;
        }

        void UpdateButtonStates(ToggleButtonGroupState options)
        {
            Span<int> buffer = stackalloc int[value.length];
            var active = options.GetActiveOptions(buffer);
            for (int i = 0; i < m_Buttons.Count; i++)
            {
                if (active.IndexOf(i) == -1) m_Buttons[i].pseudoStates &= ~PseudoStates.Checked;
                else m_Buttons[i].pseudoStates |= PseudoStates.Checked;
                m_Buttons[i].IncrementVersion(VersionChangeType.Repaint);
            }
        }

        void OnOptionChange(EventBase evt)
        {
            var button = evt.target as Button;
            int index = m_Buttons.IndexOf(button);
            var state = value;
            Span<int> buffer = stackalloc int[state.length];
            var active = state.GetActiveOptions(buffer);
            if (showMixedValue)
            {
                var cleared = value;
                cleared.ResetAllOptions();
                if (value != cleared) SetValueWithoutNotify(cleared);
            }
            if (isMultipleSelection)
            {
                // The last selected option stays selected unless the selection may be empty.
                if (!allowEmptySelection && active.Length == 1 && state[index]) return;
                state[index] = !state[index];
            }
            else if (allowEmptySelection && active.Length == 1 && state[active[0]])
            {
                state[active[0]] = false;
                if (index != active[0]) state[index] = true;
            }
            else
            {
                state.ResetAllOptions();
                state[index] = true;
            }
            value = state;
        }

        void UpdateButtonsStyling()
        {
            int count = m_Buttons.Count;
            for (int i = 0; i < count; i++)
            {
                var button = m_Buttons[i];
                bool standalone = count == 1;
                bool left = i == 0 && !standalone;
                bool right = i == count - 1 && !standalone;
                bool mid = !left && !right && !standalone;
                button.EnableInClassList(buttonStandaloneClassName, standalone);
                button.EnableInClassList(buttonLeftClassName, left);
                button.EnableInClassList(buttonRightClassName, right);
                button.EnableInClassList(buttonMidClassName, mid);
            }
        }
    }
}
