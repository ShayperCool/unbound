using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit bit-mask field base and EnumFlagsField for [Flags] enum values.
// Documented in Unity 6000.0 Scripting API ("BaseMaskField", "EnumFlagsField");
// matches the Unity 6000.0.71f1 Editor, tests/projects/enumflagsbinding.

namespace UnityEngine.UIElements
{
    internal partial class EnumFlagsFieldValueDecoratorAttribute : PropertyAttribute
    {
        public EnumFlagsFieldValueDecoratorAttribute() { }
    }
}

namespace UnityEditor.UIElements
{
    public abstract partial class BaseMaskField<TChoice> : BasePopupField<TChoice, string>
    {
        internal static readonly BindingId choicesMasksProperty = nameof(choicesMasks);

        static readonly int s_NothingIndex = 0;
        static readonly int s_EverythingIndex = 1;
        static readonly int s_TotalIndex = 2;
        static readonly string s_MixedLabel = "Mixed...";
        static readonly string s_EverythingLabel = "Everything";
        static readonly string s_NothingLabel = "Nothing";

        List<string> m_UserChoices;
        List<int> m_UserChoicesMasks;
        int m_FullChoiceMask;

        internal int fullChoiceMask => m_FullChoiceMask;

        public override List<string> choices
        {
            get => m_UserChoices;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (m_UserChoices == null) m_UserChoices = new List<string>();
                else m_UserChoices.Clear();
                m_UserChoices.AddRange(value);

                if (m_Choices == null) m_Choices = new List<string>();
                else m_Choices.Clear();

                ComputeFullChoiceMask();
                m_Choices.Add(GetNothingName());
                m_Choices.Add(GetEverythingName());
                m_Choices.AddRange(m_UserChoices);
                SetValueWithoutNotify(rawValue);
                NotifyPropertyChanged(in BasePopupField<TChoice, string>.choicesProperty);
            }
        }

        [CreateProperty]
        public virtual List<int> choicesMasks
        {
            get => m_UserChoicesMasks;
            set
            {
                if (value == null)
                {
                    m_UserChoicesMasks = null;
                }
                else
                {
                    if (m_UserChoicesMasks == null) m_UserChoicesMasks = new List<int>();
                    else m_UserChoicesMasks.Clear();
                    m_UserChoicesMasks.AddRange(value);
                    ComputeFullChoiceMask();
                    SetValueWithoutNotify(rawValue);
                }
                NotifyPropertyChanged(in choicesMasksProperty);
            }
        }

        public override TChoice value
        {
            get => base.value;
            set => base.value = MaskToValue(UpdateMaskIfEverything(ValueToMask(value)));
        }

        internal abstract TChoice MaskToValue(int newMask);
        internal abstract int ValueToMask(TChoice value);

        internal BaseMaskField(string label) : base(label)
        {
            textElement.RegisterCallback<GeometryChangedEvent>(OnTextElementGeometryChanged);
            m_AutoCloseMenu = false;
        }

        void OnTextElementGeometryChanged(GeometryChangedEvent evt)
        {
            int mask = ValueToMask(value);
            if (mask != -1 && mask != 0 && !IsPowerOf2(mask)
                && ((textElement.text == s_MixedLabel && evt.oldRect.width < evt.newRect.width)
                    || (textElement.text != s_MixedLabel && evt.oldRect.width > evt.newRect.width)))
            {
                textElement.text = GetMixedString();
            }
        }

        internal virtual string GetNothingName() => s_NothingLabel;

        internal virtual string GetEverythingName() => s_EverythingLabel;

        void ComputeFullChoiceMask()
        {
            if (m_UserChoices == null || m_UserChoices.Count == 0)
            {
                m_FullChoiceMask = 0;
            }
            else if (m_UserChoicesMasks != null && m_UserChoicesMasks.Count == m_UserChoices.Count)
            {
                if (m_UserChoices.Count < 32)
                {
                    int mask = 0;
                    for (int i = 0; i < m_UserChoicesMasks.Count; i++)
                    {
                        int itemMask = m_UserChoicesMasks[i];
                        if (itemMask != -1) mask |= itemMask;
                    }
                    m_FullChoiceMask = mask;
                    return;
                }
                m_FullChoiceMask = -1;
            }
            else if (m_UserChoices.Count >= 32)
            {
                m_FullChoiceMask = -1;
            }
            else
            {
                m_FullChoiceMask = (1 << m_UserChoices.Count) - 1;
            }
        }

        internal bool IsPowerOf2(int itemIndex) => unchecked((itemIndex & (itemIndex - 1)) == 0);

        internal override string GetValueToDisplay() => GetDisplayedValue(ValueToMask(value));

        internal override string GetListItemToDisplay(TChoice item) => GetDisplayedValue(ValueToMask(item));

        internal string GetDisplayedValue(int itemIndex)
        {
            string result = "";
            if (itemIndex == 0)
            {
                result = m_Choices[s_NothingIndex];
            }
            else if (itemIndex == -1)
            {
                result = m_Choices[s_EverythingIndex];
            }
            else if (IsPowerOf2(itemIndex))
            {
                int idx = 0;
                if (m_UserChoicesMasks != null)
                {
                    for (int k = 0; k < m_UserChoicesMasks.Count; k++)
                    {
                        int mask = m_UserChoicesMasks[k];
                        if (mask != -1 && (mask & itemIndex) == itemIndex)
                        {
                            idx = k;
                            break;
                        }
                    }
                }
                else
                {
                    while ((1 << idx) != itemIndex) idx++;
                }
                idx += s_TotalIndex;
                if (idx < m_Choices.Count) result = m_Choices[idx];
            }
            else
            {
                if (m_UserChoicesMasks != null)
                {
                    for (int j = 0; j < m_UserChoicesMasks.Count; j++)
                    {
                        if (m_UserChoicesMasks[j] == itemIndex)
                        {
                            result = m_Choices[j + s_TotalIndex];
                            break;
                        }
                    }
                }
                if (string.IsNullOrEmpty(result)) result = GetMixedString();
            }
            return result;
        }

        string GetMixedString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < m_Choices.Count; i++)
            {
                string choice = m_Choices[i];
                int mask = GetMaskValueOfItem(choice);
                if (IsItemSelected(mask))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(choice);
                }
            }
            string text = sb.ToString();
            var measured = textElement.MeasureTextSize(text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
            float resolvedWidth = textElement.resolvedStyle.width;
            if (float.IsNaN(resolvedWidth) || measured.x > resolvedWidth)
                text = s_MixedLabel;
            return text;
        }

        public override void SetValueWithoutNotify(TChoice newValue) =>
            base.SetValueWithoutNotify(MaskToValue(UpdateMaskIfEverything(ValueToMask(newValue))));

        internal override void AddMenuItems(IGenericMenu menu)
        {
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            for (int i = 0; i < m_Choices.Count; i++)
            {
                string item = m_Choices[i];
                int mask = GetMaskValueOfItem(item);
                bool isChecked = IsItemSelected(mask) && !showMixedValue;
                menu.AddItem(GetListItemToDisplay(MaskToValue(mask)), isChecked, () => ChangeValueFromMenu(item));
            }
        }

        bool IsItemSelected(int maskOfItem)
        {
            int current = ValueToMask(value);
            if (maskOfItem == 0) return current == 0;
            return (maskOfItem & current) == maskOfItem;
        }

        void UpdateMenuItems()
        {
            if (!(m_GenericMenu is GenericDropdownMenu dropdown)) return;
            for (int i = 0; i < m_Choices.Count; i++)
            {
                string choice = m_Choices[i];
                int mask = GetMaskValueOfItem(choice);
                dropdown.UpdateItem(GetListItemToDisplay(MaskToValue(mask)), IsItemSelected(mask));
            }
        }

        private protected virtual int UpdateMaskIfEverything(int currentMask)
        {
            int result = currentMask;
            if (m_FullChoiceMask != 0)
                result = (currentMask & m_FullChoiceMask) == m_FullChoiceMask ? -1 : (result & m_FullChoiceMask);
            return result;
        }

        internal void ChangeValueFromMenu(string menuItem)
        {
            int current = showMixedValue ? 0 : ValueToMask(value);
            int itemMask = GetMaskValueOfItem(menuItem);
            if (itemMask == 0)
            {
                current = 0;
            }
            else if (itemMask == -1)
            {
                current = -1;
            }
            else
            {
                current = (current & itemMask) == itemMask ? (current & ~itemMask) : (current | itemMask);
                current = UpdateMaskIfEverything(current);
            }
            value = MaskToValue(current);
            UpdateMenuItems();
        }

        int GetMaskValueOfItem(string item)
        {
            int idx = m_Choices.IndexOf(item);
            if (idx == 0) return 0;
            if (idx == 1) return -1;
            if (idx > 0)
            {
                if (m_UserChoicesMasks != null && m_UserChoicesMasks.Count == m_UserChoices.Count)
                    return m_UserChoicesMasks[idx - s_TotalIndex];
                return 1 << (idx - s_TotalIndex);
            }
            return 0;
        }
    }

    public partial class EnumFlagsField : BaseMaskField<Enum>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<Enum>.UxmlSerializedData
        {
#pragma warning disable 649
            [UxmlAttribute("type")] [SerializeField] [UxmlTypeReference(typeof(Enum))] string typeAsString;
            [UxmlAttribute("value")] [EnumFlagsFieldValueDecorator] [SerializeField] string valueAsString;
            [SerializeField] bool includeObsoleteValues;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags typeAsString_UxmlAttributeFlags;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags valueAsString_UxmlAttributeFlags;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags includeObsoleteValues_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<Enum>.UxmlSerializedData.Register();
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(typeAsString), "type", typeof(Enum)),
                    new UxmlAttributeNames(nameof(valueAsString), "value", null),
                    new UxmlAttributeNames(nameof(includeObsoleteValues), "include-obsolete-values", null),
                });
            }

            public override object CreateInstance() => new EnumFlagsField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                var field = (EnumFlagsField)obj;
                if (ShouldWriteAttributeValue(includeObsoleteValues_UxmlAttributeFlags)) field.includeObsoleteValues = includeObsoleteValues;
                if (ShouldWriteAttributeValue(typeAsString_UxmlAttributeFlags)) field.typeAsString = typeAsString;
                if (ShouldWriteAttributeValue(valueAsString_UxmlAttributeFlags)) field.valueAsString = valueAsString;
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<EnumFlagsField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<Enum>.UxmlTraits
        {
            readonly UxmlTypeAttributeDescription<Enum> m_Type = new UxmlTypeAttributeDescription<Enum> { name = "type" };
            readonly UxmlStringAttributeDescription m_Value = new UxmlStringAttributeDescription { name = "value" };
            readonly UxmlBoolAttributeDescription m_IncludeObsoleteValues = new UxmlBoolAttributeDescription { name = "include-obsolete-values" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var field = (EnumFlagsField)ve;
                var type = m_Type.GetValueFromBag(bag, cc);
                if (type == null)
                {
                    field.m_EnumType = null;
                    field.value = null;
                    return;
                }
                string text = null;
                object parsed = null;
                if (m_Value.TryGetValueFromBag(bag, cc, ref text) && !Enum.TryParse(type, text, false, out parsed))
                {
                    UnityEngine.Debug.LogErrorFormat("EnumFlagsField: Could not parse value of '{0}', because it isn't defined in the {1} enum.", text, type.FullName);
                    field.PopulateDataFromType(type);
                    field.value = null;
                    return;
                }
                var val = (Enum)(text != null && parsed != null ? parsed : Enum.ToObject(type, 0));
                field.Init(val, m_IncludeObsoleteValues.GetValueFromBag(bag, cc));
            }
        }

        public new static readonly string ussClassName = "unity-enum-flags-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        Type m_EnumType;
        bool m_IncludeObsoleteValues;
        EnumData m_EnumData;

        internal Type type => m_EnumType;

        internal bool includeObsoleteValues
        {
            get => m_IncludeObsoleteValues;
            set => m_IncludeObsoleteValues = value;
        }

        internal string typeAsString
        {
            get => UxmlUtility.TypeToString(m_EnumType);
            set
            {
                m_EnumType = UxmlUtility.ParseType(value);
                if (m_EnumType == null)
                {
                    this.value = null;
                    textElement.text = string.Empty;
                }
            }
        }

        internal string valueAsString
        {
            get => value?.ToString();
            set
            {
                if (type != null)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (Enum.TryParse(type, value, ignoreCase: false, out object result) && result is Enum defaultValue)
                        {
                            Init(defaultValue, includeObsoleteValues);
                            return;
                        }
                        PopulateDataFromType(type);
                        this.value = null;
                    }
                    else
                    {
                        Init((Enum)Enum.ToObject(type, 0), includeObsoleteValues);
                    }
                }
                else
                {
                    this.value = null;
                }
            }
        }

        public EnumFlagsField() : this(null, null, includeObsoleteValues: false) { }

        public EnumFlagsField(Enum defaultValue) : this(null, defaultValue, includeObsoleteValues: false) { }

        public EnumFlagsField(Enum defaultValue, bool includeObsoleteValues) : this(null, defaultValue, includeObsoleteValues) { }

        public EnumFlagsField(string label) : base(label)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }

        public EnumFlagsField(string label, Enum defaultValue) : this(label, defaultValue, includeObsoleteValues: false) { }

        public EnumFlagsField(string label, Enum defaultValue, bool includeObsoleteValues) : this(label)
        {
            if (defaultValue != null) Init(defaultValue, includeObsoleteValues);
        }

        public void Init(Enum defaultValue, bool includeObsoleteValues = false)
        {
            if (defaultValue == null) throw new ArgumentNullException(nameof(defaultValue));
            m_IncludeObsoleteValues = includeObsoleteValues;
            PopulateDataFromType(defaultValue.GetType());
            if (!m_EnumData.flags)
                UnityEngine.Debug.LogWarning("EnumMaskField is not bound to enum type with the [Flags] attribute");
            choicesMasks = new List<int>(m_EnumData.flagValues);
            choices = new List<string>(m_EnumData.displayNames);
            SetValueWithoutNotify(defaultValue);
        }

        internal override Enum MaskToValue(int newMask) =>
            m_EnumType == null ? null : EnumDataUtility.IntToEnumFlags(m_EnumType, newMask);

        internal override int ValueToMask(Enum value) =>
            m_EnumType == null ? 0 : EnumDataUtility.EnumFlagsToInt(m_EnumData, value);

        internal void PopulateDataFromType(Type enumType)
        {
            m_EnumType = enumType;
            m_EnumData = EnumDataUtility.GetCachedEnumData(m_EnumType, !includeObsoleteValues);
        }

        internal override string GetNothingName()
        {
            int[] flagValues = m_EnumData.flagValues;
            if (flagValues == null || flagValues.Length == 0) return base.GetNothingName();
            for (int i = 0; i < flagValues.Length; i++)
            {
                if (flagValues[i] == 0) return m_EnumData.displayNames[i];
            }
            return base.GetNothingName();
        }

        internal override string GetEverythingName()
        {
            int[] flagValues = m_EnumData.flagValues;
            if (flagValues == null || flagValues.Length == 0) return base.GetEverythingName();
            for (int i = 0; i < flagValues.Length; i++)
            {
                if (flagValues[i] == -1) return m_EnumData.displayNames[i];
            }
            return base.GetEverythingName();
        }
    }
}
