using Unity.Properties;
using System;
using System.Collections.Generic;
using System.Reflection;

// EnumField, from the Unity 6000.0 Manual page "EnumField" (UI Toolkit control reference) and the
// Scripting API page EnumField:
//   - "An EnumField lets users select a value from an enumeration"; "Makes a dropdown for switching
//     between enum values";
//   - Init(defaultValue[, includeObsoleteValues]) "Initializes the EnumField with a default value,
//     and initializes its underlying type", includeObsoleteValues "Set to true to display obsolete
//     values as choices"; the constructors' value is "Also used to detect Enum type";
//   - `text` "Return the text value of the currently selected enum";
//   - USS class names from the page's USS class table.
// The menu is the same dropdown menu DropdownField opens (IGenericMenu, GenericDropdownMenu).
// Unbound's choices (unmeasured; the pages say nothing):
//   - an item's text is the value's InspectorName when it has one, else its declared name as is;
//   - a value declared under several names shows, and checks in the menu, its first declared name;
//   - an obsolete value left out of the menu still shows its name; a value matching no declared
//     name (an undeclared or combined [Flags] value) shows an empty text; [Flags] enums get no other
//     treatment (EnumFlagsField is a separate control);
//   - Init sets the value without a ChangeEvent and throws ArgumentNullException for null;
//   - a value of another enum type throws ArgumentException; the first value set on a field
//     without a type gives it that value's type;
//   - a primary-button press on the input, or a navigation submit, opens the menu below the input.
namespace UnityEngine.UIElements
{
    public partial class EnumField : BaseField<Enum>
    {
        public new static readonly string ussClassName = "unity-enum-field";
        public static readonly string textUssClassName = ussClassName + "__text";
        public static readonly string arrowUssClassName = ussClassName + "__arrow";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        sealed class EnumTextElement : TextElement
        {
            protected internal override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode)
            {
                var measured = text;
                if (string.IsNullOrEmpty(measured)) measured = " ";
                return MeasureTextSize(measured, desiredWidth, widthMode, desiredHeight, heightMode);
            }
        }

        readonly struct Entry
        {
            public readonly Enum Value;
            public readonly string Name;
            public readonly bool Obsolete;

            public Entry(Enum value, string name, bool obsolete)
            {
                Value = value;
                Name = name;
                Obsolete = obsolete;
            }
        }

        internal Func<IGenericMenu> createMenuCallback;

        readonly TextElement m_TextElement;
        readonly VisualElement m_ArrowElement;
        readonly List<Entry> m_Entries = new List<Entry>();
        Type m_EnumType;
        bool m_IncludeObsoleteValues;

        internal Type type => m_EnumType;

        internal bool includeObsoleteValues
        {
            get => m_IncludeObsoleteValues;
            set
            {
                if (m_IncludeObsoleteValues == value) return;
                m_IncludeObsoleteValues = value;
                UpdateText();
            }
        }

        [CreateProperty(ReadOnly = true)]
        public string text => m_TextElement.text;

        public EnumField() : this(null, null) { }

        public EnumField(Enum defaultValue) : this(null, defaultValue) { }

        public EnumField(string label, Enum defaultValue = null) : base(label, null)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            visualInput.pickingMode = PickingMode.Position;
            m_TextElement = new EnumTextElement { pickingMode = PickingMode.Ignore };
            m_TextElement.AddToClassList(textUssClassName);
            visualInput.Add(m_TextElement);
            m_ArrowElement = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ArrowElement.AddToClassList(arrowUssClassName);
            visualInput.Add(m_ArrowElement);
            visualInput.RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
            if (defaultValue != null) Init(defaultValue);
            else UpdateText();
        }

        public void Init(Enum defaultValue) => Init(defaultValue, false);

        public void Init(Enum defaultValue, bool includeObsoleteValues)
        {
            if (defaultValue == null) throw new ArgumentNullException(nameof(defaultValue));
            m_IncludeObsoleteValues = includeObsoleteValues;
            SetType(defaultValue.GetType());
            SetValueWithoutNotify(defaultValue);
        }

        public override void SetValueWithoutNotify(Enum newValue)
        {
            if (newValue != null)
            {
                if (m_EnumType == null) SetType(newValue.GetType());
                else if (newValue.GetType() != m_EnumType)
                    throw new ArgumentException($"EnumField of type {m_EnumType.FullName} cannot take a value of type {newValue.GetType().FullName}.", nameof(newValue));
            }
            base.SetValueWithoutNotify(newValue);
            UpdateText();
        }

        protected override void UpdateMixedValueContent()
        {
            UpdateText();
            m_TextElement.EnableInClassList(mixedValueLabelUssClassName, showMixedValue);
        }

        internal void ShowMenu()
        {
            var menu = createMenuCallback != null ? createMenuCallback() : new GenericDropdownMenu();
            int current = showMixedValue ? -1 : IndexOf(rawValue, includeHidden: false);
            for (int i = 0; i < m_Entries.Count; i++)
            {
                var entry = m_Entries[i];
                if (entry.Obsolete && !m_IncludeObsoleteValues) continue;
                var chosen = entry.Value;
                menu.AddItem(entry.Name, i == current, () => value = chosen);
            }
            menu.DropDown(visualInput.worldBound, this, true);
        }

        void SetType(Type enumType)
        {
            m_EnumType = enumType;
            m_Entries.Clear();
            foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var inspectorName = (InspectorNameAttribute)Attribute.GetCustomAttribute(field, typeof(InspectorNameAttribute), false);
                bool obsolete = field.IsDefined(typeof(ObsoleteAttribute), false);
                m_Entries.Add(new Entry((Enum)field.GetValue(null), inspectorName != null ? inspectorName.displayName : field.Name, obsolete));
            }
        }

        /// <summary>The entry showing <paramref name="enumValue"/>: its first declared name in the
        /// menu, else (with <paramref name="includeHidden"/>) its first obsolete name.</summary>
        int IndexOf(Enum enumValue, bool includeHidden)
        {
            if (enumValue == null) return -1;
            int hidden = -1;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                if (!m_Entries[i].Value.Equals(enumValue)) continue;
                if (!m_Entries[i].Obsolete || m_IncludeObsoleteValues) return i;
                if (hidden < 0) hidden = i;
            }
            return includeHidden ? hidden : -1;
        }

        void UpdateText()
        {
            string shown;
            if (showMixedValue)
            {
                shown = mixedValueString;
            }
            else
            {
                int index = IndexOf(rawValue, includeHidden: true);
                shown = index >= 0 ? m_Entries[index].Name : string.Empty;
            }
            ((INotifyValueChanged<string>)m_TextElement).SetValueWithoutNotify(shown);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            schedule.Execute(ShowMenu);
            evt.StopPropagation();
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            ShowMenu();
            evt.StopPropagation();
        }
    }
}
