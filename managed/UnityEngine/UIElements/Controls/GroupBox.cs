using System;
using System.Runtime.CompilerServices;
using Unity.Properties;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    /// <summary>An option of a group (a RadioButton): selected or not by its group.</summary>
    internal partial interface IGroupBoxOption
    {
        void SetSelected(bool selected);
    }

    /// <summary>An element that groups the options below it (GroupBox, RadioButtonGroup, and the
    /// panel for options under no group box).</summary>
    internal partial interface IGroupBox
    {
        void OnOptionAdded(IGroupBoxOption option);
        void OnOptionRemoved(IGroupBoxOption option);
    }

    /// <summary>A group box whose options are managed by a <typeparamref name="T"/> instead of the default manager.</summary>
    internal partial interface IGroupBox<T> : IGroupBox where T : IGroupManager
    {
    }

    /// <summary>Tracks a group's options and its selected one.</summary>
    internal partial interface IGroupManager
    {
        void Init(IGroupBox groupBox);
        IGroupBoxOption GetSelectedOption();
        void OnOptionSelectionChanged(IGroupBoxOption selectedOption);
        void RegisterOption(IGroupBoxOption option);
        void UnregisterOption(IGroupBoxOption option);
    }

    /// <summary>One selected option per group: selecting an option deselects the others, in the
    /// order they joined the group.</summary>
    internal partial class DefaultGroupManager : IGroupManager
    {
        readonly List<IGroupBoxOption> m_GroupOptions = new List<IGroupBoxOption>();
        IGroupBoxOption m_SelectedOption;
        IGroupBox m_GroupBox;

        public void Init(IGroupBox groupBox) => m_GroupBox = groupBox;

        public IGroupBoxOption GetSelectedOption() => m_SelectedOption;

        public void OnOptionSelectionChanged(IGroupBoxOption selectedOption)
        {
            if (m_SelectedOption == selectedOption) return;
            m_SelectedOption = selectedOption;
            foreach (var option in m_GroupOptions) option.SetSelected(option == m_SelectedOption);
        }

        public void RegisterOption(IGroupBoxOption option)
        {
            if (m_GroupOptions.Contains(option)) return;
            m_GroupOptions.Add(option);
            m_GroupBox.OnOptionAdded(option);
        }

        public void UnregisterOption(IGroupBoxOption option)
        {
            m_GroupOptions.Remove(option);
            m_GroupBox.OnOptionRemoved(option);
        }
    }

    /// <summary>
    /// Which group an option belongs to (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/radiogroups): an option joins a group when it is attached to a panel (the
    /// nearest group box above it, else the panel) and leaves it when detached, so options outside
    /// any panel are in no group. Each group has one manager while it is in a panel.
    /// </summary>
    internal static partial class GroupBoxUtility
    {
        static readonly ConditionalWeakTable<IGroupBox, IGroupManager> s_GroupManagers = new ConditionalWeakTable<IGroupBox, IGroupManager>();
        static readonly ConditionalWeakTable<IGroupBoxOption, IGroupManager> s_OptionManagers = new ConditionalWeakTable<IGroupBoxOption, IGroupManager>();
        static readonly Type s_GenericGroupBox = typeof(IGroupBox<>);

        public static void RegisterGroupBoxOption<T>(this T option) where T : VisualElement, IGroupBoxOption
        {
            IGroupBox group = null;
            for (var parent = option.hierarchy.parent; parent != null && group == null; parent = parent.hierarchy.parent)
                group = parent as IGroupBox;
            var manager = FindOrCreateGroupManager(group ?? option.elementPanel);
            manager.RegisterOption(option);
            s_OptionManagers.AddOrUpdate(option, manager);
        }

        public static void UnregisterGroupBoxOption<T>(this T option) where T : VisualElement, IGroupBoxOption
        {
            if (!s_OptionManagers.TryGetValue(option, out var manager)) return;
            manager.UnregisterOption(option);
            s_OptionManagers.Remove(option);
        }

        public static void OnOptionSelected<T>(this T selectedOption) where T : VisualElement, IGroupBoxOption
        {
            if (s_OptionManagers.TryGetValue(selectedOption, out var manager)) manager.OnOptionSelectionChanged(selectedOption);
        }

        public static IGroupBoxOption GetSelectedOption(this IGroupBox groupBox) =>
            s_GroupManagers.TryGetValue(groupBox, out var manager) ? manager.GetSelectedOption() : null;

        public static IGroupManager GetGroupManager(this IGroupBox groupBox) =>
            s_GroupManagers.TryGetValue(groupBox, out var manager) ? manager : null;

        /// <summary>The group's manager: the type its IGroupBox&lt;T&gt; names, else a default one. A
        /// group box element's manager is forgotten when it leaves its panel; a panel's goes with it.</summary>
        static IGroupManager FindOrCreateGroupManager(IGroupBox groupBox)
        {
            if (s_GroupManagers.TryGetValue(groupBox, out var existing)) return existing;
            Type managerType = null;
            foreach (var implemented in groupBox.GetType().GetInterfaces())
            {
                if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == s_GenericGroupBox)
                {
                    managerType = implemented.GetGenericArguments()[0];
                    break;
                }
            }
            var manager = managerType != null ? (IGroupManager)Activator.CreateInstance(managerType) : new DefaultGroupManager();
            manager.Init(groupBox);
            if (groupBox is VisualElement element) element.RegisterCallback<DetachFromPanelEvent>(OnGroupBoxDetachedFromPanel);
            s_GroupManagers.AddOrUpdate(groupBox, manager);
            return manager;
        }

        static void OnGroupBoxDetachedFromPanel(DetachFromPanelEvent evt)
        {
            if (evt.currentTarget is IGroupBox groupBox) s_GroupManagers.Remove(groupBox);
        }
    }

    /// <summary>A container that groups the radio buttons below it, with an optional title label
    /// made when the text is first set and removed when it is cleared.</summary>
    public partial class GroupBox : BindableElement, IGroupBox
    {
        internal static readonly BindingId textProperty = nameof(text);
        public static readonly string ussClassName = "unity-group-box";
        public static readonly string labelUssClassName = ussClassName + "__label";

        Label m_TitleLabel;

        internal Label titleLabel => m_TitleLabel;

        [CreateProperty]
        public string text
        {
            get => m_TitleLabel?.text;
            set
            {
                var previous = text;
                if (!string.IsNullOrEmpty(value))
                {
                    if (m_TitleLabel == null)
                    {
                        m_TitleLabel = new Label(value);
                        m_TitleLabel.AddToClassList(labelUssClassName);
                        Insert(0, m_TitleLabel);
                    }
                    m_TitleLabel.text = value;
                }
                else if (m_TitleLabel != null)
                {
                    m_TitleLabel.RemoveFromHierarchy();
                    m_TitleLabel = null;
                }
                if (string.CompareOrdinal(previous, text) != 0) NotifyPropertyChanged(textProperty);
            }
        }

        public GroupBox() : this(null) { }

        public GroupBox(string text)
        {
            AddToClassList(ussClassName);
            this.text = text;
        }

        void IGroupBox.OnOptionAdded(IGroupBoxOption option) { }

        void IGroupBox.OnOptionRemoved(IGroupBoxOption option) { }
    }

    internal abstract partial class BaseVisualElementPanel : IGroupBox
    {
        // The panel groups the options under no group box; it keeps no state of its own about them.
        void IGroupBox.OnOptionAdded(IGroupBoxOption option) { }

        void IGroupBox.OnOptionRemoved(IGroupBoxOption option) { }
    }
}
