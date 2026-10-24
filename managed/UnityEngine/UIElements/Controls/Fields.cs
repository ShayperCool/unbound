using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    /// <summary>A bar filled in proportion to a value between a low and a high value, with a centred title.</summary>
    public abstract partial class AbstractProgressBar : BindableElement, INotifyValueChanged<float>
    {
        public static readonly string ussClassName = "unity-progress-bar";
        public static readonly string containerUssClassName = ussClassName + "__container";
        public static readonly string titleUssClassName = ussClassName + "__title";
        public static readonly string titleContainerUssClassName = ussClassName + "__title-container";
        public static readonly string progressUssClassName = ussClassName + "__progress";
        public static readonly string backgroundUssClassName = ussClassName + "__background";

        readonly VisualElement m_Background;
        readonly VisualElement m_Progress;
        readonly Label m_Title;
        float m_LowValue;
        float m_HighValue = 100f;
        float m_Value;

        public string title
        {
            get => m_Title.text;
            set => m_Title.text = value;
        }

        public float lowValue
        {
            get => m_LowValue;
            set
            {
                m_LowValue = value;
                SetProgress(m_Value);
            }
        }

        public float highValue
        {
            get => m_HighValue;
            set
            {
                m_HighValue = value;
                SetProgress(m_Value);
            }
        }

        public virtual float value
        {
            get => m_Value;
            set
            {
                if (EqualityComparer<float>.Default.Equals(m_Value, value)) return;
                if (panel != null)
                {
                    using (var evt = ChangeEvent<float>.GetPooled(m_Value, value))
                    {
                        evt.elementTarget = this;
                        SetValueWithoutNotify(value);
                        SendEvent(evt);
                    }
                    return;
                }
                SetValueWithoutNotify(value);
            }
        }

        public AbstractProgressBar()
        {
            AddToClassList(ussClassName);
            var container = new VisualElement { name = ussClassName };
            m_Background = new VisualElement();
            m_Background.AddToClassList(backgroundUssClassName);
            container.Add(m_Background);
            m_Progress = new VisualElement();
            m_Progress.AddToClassList(progressUssClassName);
            m_Background.Add(m_Progress);
            var titleContainer = new VisualElement();
            titleContainer.AddToClassList(titleContainerUssClassName);
            m_Background.Add(titleContainer);
            m_Title = new Label();
            m_Title.AddToClassList(titleUssClassName);
            titleContainer.Add(m_Title);
            container.AddToClassList(containerUssClassName);
            hierarchy.Add(container);
            RegisterCallback<GeometryChangedEvent>(e => SetProgress(value));
        }

        public void SetValueWithoutNotify(float newValue)
        {
            m_Value = newValue;
            SetProgress(value);
        }

        void SetProgress(float p)
        {
            float clampedValue = p < lowValue ? lowValue : p > highValue ? highValue : p;
            float right = CalculateOppositeProgressWidth(clampedValue);
            if (right >= 0f) m_Progress.style.right = right;
        }

        /// <summary>The space right of the progress: the background's whole pixels less its 2 border
        /// pixels, beyond the value's share.</summary>
        float CalculateOppositeProgressWidth(float width)
        {
            if (m_Background == null || m_Progress == null) return 0f;
            if (float.IsNaN(m_Background.layout.width)) return 0f;
            float full = Mathf.Floor(m_Background.layout.width - 2f);
            float filled = Mathf.Max(full * width / highValue, 0f);
            float opposite = full - filled;
            m_Progress.style.width = Mathf.Abs(full - opposite) < 0.1f ? new StyleLength(0f) : new StyleLength(StyleKeyword.Auto);
            return opposite;
        }
    }

    public partial class ProgressBar : AbstractProgressBar
    {
        public ProgressBar() { }
    }

    /// <summary>A toggle that shows or hides its content.</summary>
    public partial class Foldout : BindableElement, INotifyValueChanged<bool>
    {
        public static readonly string ussClassName = "unity-foldout";
        public static readonly string toggleUssClassName = ussClassName + "__toggle";
        public static readonly string contentUssClassName = ussClassName + "__content";
        public static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string checkmarkUssClassName = ussClassName + "__checkmark";
        public static readonly string textUssClassName = ussClassName + "__text";
        internal static readonly string toggleInspectorUssClassName = toggleUssClassName + "--inspector";
        internal static readonly string ussFoldoutDepthClassName = ussClassName + "--depth-";
        internal static readonly int ussFoldoutMaxDepth = 4;

        readonly Toggle m_Toggle = new Toggle();
        readonly VisualElement m_Container;
        bool m_Value;

        internal Toggle toggle => m_Toggle;

        public override VisualElement contentContainer => m_Container;

        public override bool focusable
        {
            get => base.focusable;
            set
            {
                base.focusable = value;
                m_Toggle.focusable = value;
            }
        }

        public bool toggleOnLabelClick
        {
            get => m_Toggle.toggleOnTextClick;
            set => m_Toggle.toggleOnTextClick = value;
        }

        public string text
        {
            get => m_Toggle.text;
            set
            {
                m_Toggle.text = value;
                m_Toggle.visualInput.Q(null, Toggle.textUssClassName)?.AddToClassList(textUssClassName);
            }
        }

        public bool value
        {
            get => m_Value;
            set
            {
                if (m_Value == value) return;
                using (var evt = ChangeEvent<bool>.GetPooled(m_Value, value))
                {
                    evt.elementTarget = this;
                    SetValueWithoutNotify(value);
                    SendEvent(evt);
                }
            }
        }

        public void SetValueWithoutNotify(bool newValue)
        {
            m_Value = newValue;
            m_Toggle.SetValueWithoutNotify(m_Value);
            contentContainer.style.display = newValue ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_Value) pseudoStates |= PseudoStates.Checked;
            else pseudoStates &= ~PseudoStates.Checked;
        }

        public Foldout()
        {
            AddToClassList(ussClassName);
            delegatesFocus = true;
            focusable = true;
            isEligibleToReceiveFocusFromDisabledChild = false;
            m_Container = new VisualElement { name = "unity-content" };
            m_Toggle.RegisterValueChangedCallback(evt =>
            {
                value = m_Toggle.value;
                evt.StopPropagation();
            });
            m_Toggle.AddToClassList(toggleUssClassName);
            m_Toggle.visualInput.AddToClassList(inputUssClassName);
            m_Toggle.visualInput.Q(null, Toggle.checkmarkUssClassName).AddToClassList(checkmarkUssClassName);
            m_Toggle.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            hierarchy.Add(m_Toggle);
            m_Container.AddToClassList(contentUssClassName);
            hierarchy.Add(m_Container);
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            SetValueWithoutNotify(true);
        }

        /// <summary>Left and right expand and collapse the foldout (Unity's keyboard navigation manipulator on its toggle).</summary>
        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt.direction == NavigationMoveEvent.Direction.Right) SetValueWithoutNotify(true);
            else if (evt.direction == NavigationMoveEvent.Direction.Left) SetValueWithoutNotify(false);
            else return;
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            for (int i = 0; i <= ussFoldoutMaxDepth; i++) RemoveFromClassList(ussFoldoutDepthClassName + i);
            RemoveFromClassList(ussFoldoutDepthClassName + "max");
            int depth = 0;
            for (var p = hierarchy.parent; p != null; p = p.hierarchy.parent)
                if (p is Foldout) depth++;
            AddToClassList(depth > ussFoldoutMaxDepth ? ussFoldoutDepthClassName + "max" : ussFoldoutDepthClassName + depth);
        }
    }

    internal partial interface IGenericMenu
    {
        void AddItem(string itemName, bool isChecked, Action action);
        void AddItem(string itemName, bool isChecked, Action<object> action, object data);
        void AddDisabledItem(string itemName, bool isChecked);
        void AddSeparator(string path);
        void DropDown(Rect position, VisualElement targetElement = null, bool anchored = false);
    }

    /// <summary>A field showing its value as text and an arrow; pressing it opens a menu of choices.</summary>
    public abstract partial class BasePopupField<TValueType, TValueChoice> : BaseField<TValueType>
    {
        sealed class PopupTextElement : TextElement
        {
            protected internal override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode)
            {
                var measured = text;
                if (string.IsNullOrEmpty(measured)) measured = " ";
                return MeasureTextSize(measured, desiredWidth, widthMode, desiredHeight, heightMode);
            }
        }

        internal List<TValueChoice> m_Choices;
        readonly TextElement m_TextElement;
        readonly VisualElement m_ArrowElement;
        internal Func<TValueChoice, string> m_FormatSelectedValueCallback;
        internal Func<TValueChoice, string> m_FormatListItemCallback;
        internal Func<IGenericMenu> createMenuCallback;
        internal IGenericMenu m_GenericMenu;
        internal bool m_AutoCloseMenu = true;

        public new static readonly string ussClassName = "unity-base-popup-field";
        public static readonly string textUssClassName = ussClassName + "__text";
        public static readonly string arrowUssClassName = ussClassName + "__arrow";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        protected TextElement textElement => m_TextElement;

        public virtual List<TValueChoice> choices
        {
            get => m_Choices;
            set
            {
                m_Choices = value ?? throw new ArgumentNullException(nameof(value));
                SetValueWithoutNotify(rawValue);
            }
        }

        public string text => m_TextElement.text;

        internal abstract string GetValueToDisplay();

        internal abstract string GetListItemToDisplay(TValueType item);

        internal abstract void AddMenuItems(IGenericMenu menu);

        public override void SetValueWithoutNotify(TValueType newValue)
        {
            base.SetValueWithoutNotify(newValue);
            ((INotifyValueChanged<string>)m_TextElement).SetValueWithoutNotify(GetValueToDisplay());
        }

        internal BasePopupField() : this(null) { }

        internal BasePopupField(string label) : base(label, null)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            m_TextElement = new PopupTextElement { pickingMode = PickingMode.Ignore };
            m_TextElement.AddToClassList(textUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            visualInput.Add(m_TextElement);
            m_ArrowElement = new VisualElement();
            m_ArrowElement.AddToClassList(arrowUssClassName);
            m_ArrowElement.pickingMode = PickingMode.Ignore;
            visualInput.Add(m_ArrowElement);
            choices = new List<TValueChoice>();
            RegisterCallback<PointerDownEvent>(e => ProcessPointerDown(e, e.button, e.pointerId));
            RegisterCallback<PointerUpEvent>(OnPointerUpEvent);
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.button == 0 && (e.pressedButtons & 1) != 0) ProcessPointerDown(e, e.button, e.pointerId);
            });
            RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0) e.StopPropagation();
            });
            RegisterCallback<NavigationSubmitEvent>(e =>
            {
                ShowMenu();
                e.StopPropagation();
            });
        }

        void OnPointerUpEvent(PointerUpEvent evt)
        {
            if (evt.button == 0 && ContainsPointer(evt.pointerId)) evt.StopPropagation();
        }

        bool ContainsPointer(int pointerId)
        {
            var under = elementPanel?.GetTopElementUnderPointer(pointerId);
            return this == under || visualInput == under;
        }

        void ProcessPointerDown(EventBase evt, int button, int pointerId)
        {
            if (button != 0 || !ContainsPointer(pointerId)) return;
            schedule.Execute(ShowMenu);
            evt.StopPropagation();
        }

        internal void ShowMenu()
        {
            m_GenericMenu = createMenuCallback != null ? createMenuCallback() : new GenericDropdownMenu();
            AddMenuItems(m_GenericMenu);
            m_GenericMenu.DropDown(visualInput.worldBound, this, true);
        }

        protected override void UpdateMixedValueContent()
        {
            if (showMixedValue) ((INotifyValueChanged<string>)m_TextElement).SetValueWithoutNotify(mixedValueString);
            textElement.EnableInClassList(mixedValueLabelUssClassName, showMixedValue);
        }
    }

    public partial class PopupField<T> : BasePopupField<T, T>
    {
        internal const int kPopupFieldDefaultIndex = -1;
        int m_Index = -1;
        public new static readonly string ussClassName = "unity-popup-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        public virtual Func<T, string> formatSelectedValueCallback
        {
            get => m_FormatSelectedValueCallback;
            set
            {
                m_FormatSelectedValueCallback = value;
                textElement.text = GetValueToDisplay();
            }
        }

        public virtual Func<T, string> formatListItemCallback
        {
            get => m_FormatListItemCallback;
            set => m_FormatListItemCallback = value;
        }

        public override T value
        {
            get => base.value;
            set
            {
                m_Index = m_Choices?.IndexOf(value) ?? -1;
                base.value = value;
            }
        }

        public int index
        {
            get => m_Index;
            set
            {
                if (value == m_Index) return;
                m_Index = value;
                this.value = m_Index >= 0 && m_Index < m_Choices.Count ? m_Choices[m_Index] : default;
            }
        }

        internal override string GetValueToDisplay()
        {
            if (m_FormatSelectedValueCallback != null) return m_FormatSelectedValueCallback(value);
            return value != null ? Unbound.UIElements.UaMenus.ParseMenuName(value.ToString()) : string.Empty;
        }

        internal override string GetListItemToDisplay(T item)
        {
            if (m_FormatListItemCallback != null) return m_FormatListItemCallback(item);
            return item != null && m_Choices.Contains(item) ? item.ToString() : string.Empty;
        }

        public override void SetValueWithoutNotify(T newValue)
        {
            m_Index = m_Choices?.IndexOf(newValue) ?? -1;
            base.SetValueWithoutNotify(newValue);
        }

        public PopupField() : this((string)null) { }

        public PopupField(string label = null) : base(label)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }

        public PopupField(List<T> choices, T defaultValue, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null)
            : this(null, choices, defaultValue, formatSelectedValueCallback, formatListItemCallback) { }

        public PopupField(string label, List<T> choices, T defaultValue, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null) : this(label)
        {
            if (defaultValue == null) throw new ArgumentNullException(nameof(defaultValue));
            this.choices = choices;
            if (!m_Choices.Contains(defaultValue)) throw new ArgumentException($"Default value {defaultValue} is not present in the list of possible values");
            SetValueWithoutNotify(defaultValue);
            this.formatListItemCallback = formatListItemCallback;
            this.formatSelectedValueCallback = formatSelectedValueCallback;
        }

        public PopupField(List<T> choices, int defaultIndex, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null)
            : this(null, choices, defaultIndex, formatSelectedValueCallback, formatListItemCallback) { }

        public PopupField(string label, List<T> choices, int defaultIndex, Func<T, string> formatSelectedValueCallback = null, Func<T, string> formatListItemCallback = null) : this(label)
        {
            this.choices = choices;
            SetIndexWithoutNotify(defaultIndex);
            this.formatListItemCallback = formatListItemCallback;
            this.formatSelectedValueCallback = formatSelectedValueCallback;
        }

        internal override void AddMenuItems(IGenericMenu menu)
        {
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            if (m_Choices == null) return;
            foreach (var item in m_Choices)
            {
                bool isChecked = EqualityComparer<T>.Default.Equals(item, value) && !showMixedValue;
                var chosen = item;
                menu.AddItem(GetListItemToDisplay(item), isChecked, () => value = chosen);
            }
        }

        internal void SetIndexWithoutNotify(int index)
        {
            m_Index = index;
            SetValueWithoutNotify(m_Index >= 0 && m_Index < m_Choices.Count ? m_Choices[m_Index] : default);
        }
    }

    public partial class DropdownField : PopupField<string>
    {
        internal int valueOverride { get; set; }

        public DropdownField() : this(null) { }

        public DropdownField(string label) : base(label) { }

        public DropdownField(List<string> choices, string defaultValue, Func<string, string> formatSelectedValueCallback = null, Func<string, string> formatListItemCallback = null)
            : this(null, choices, defaultValue, formatSelectedValueCallback, formatListItemCallback) { }

        public DropdownField(string label, List<string> choices, string defaultValue, Func<string, string> formatSelectedValueCallback = null, Func<string, string> formatListItemCallback = null)
            : base(label, choices, defaultValue, formatSelectedValueCallback, formatListItemCallback) { }

        public DropdownField(List<string> choices, int defaultIndex, Func<string, string> formatSelectedValueCallback = null, Func<string, string> formatListItemCallback = null)
            : this(null, choices, defaultIndex, formatSelectedValueCallback, formatListItemCallback) { }

        public DropdownField(string label, List<string> choices, int defaultIndex, Func<string, string> formatSelectedValueCallback = null, Func<string, string> formatListItemCallback = null)
            : base(label, choices, defaultIndex, formatSelectedValueCallback, formatListItemCallback) { }
    }
}

namespace Unbound.UIElements
{
    internal static class UaMenus
    {
        static readonly char[] s_Modifiers = { '&', '%', '^', '#', '_' };

        /// <summary>A menu item name without its trailing shortcut (" %g" and the like), as Unity's
        /// UIElementsUtility.ParseMenuName shows it.</summary>
        internal static string ParseMenuName(string menuName)
        {
            if (string.IsNullOrEmpty(menuName)) return string.Empty;
            var name = menuName.TrimEnd();
            int space = name.LastIndexOf(' ');
            if (space > -1 && name.Length > space + 1 && System.Array.IndexOf(s_Modifiers, name[space + 1]) > -1)
                name = name.Substring(0, space).TrimEnd();
            return name;
        }
    }
}
