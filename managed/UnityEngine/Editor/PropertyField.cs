using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit PropertyField for supported scalar, enum, Vector2, and Vector3
// SerializedProperty types (bool, int, long, uint, ulong, float, double, string, ordinary and
// [Flags] enum, Vector2, Vector3) and SerializedPropertyBindEvent. Documented in Unity 6000.0
// Scripting API ("PropertyField", "PropertyField.RegisterValueChangeCallback"); matches the
// Unity 6000.0.71f1 Editor, tests/projects/propertyfield, tests/projects/enumbinding,
// tests/projects/enumflagsbinding, tests/projects/vector2binding, and
// tests/projects/vector3binding.

namespace UnityEditor.UIElements
{
    [EventCategory(EventCategory.Bind)]
    internal partial class SerializedPropertyBindEvent : EventBase<SerializedPropertyBindEvent>
    {
        SerializedProperty m_BindProperty;

        public SerializedProperty bindProperty => m_BindProperty;

        static SerializedPropertyBindEvent() => SetCreateFunction(() => new SerializedPropertyBindEvent());

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit()
        {
            propagation = EventPropagation.None;
            m_BindProperty = null;
        }

        public static SerializedPropertyBindEvent GetPooled(SerializedProperty obj)
        {
            var evt = EventBase<SerializedPropertyBindEvent>.GetPooled();
            evt.m_BindProperty = obj;
            return evt;
        }

        public SerializedPropertyBindEvent() => LocalInit();
    }

    public partial class PropertyField : VisualElement, IBindable
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField] string bindingPath;
            [SerializeField] internal string label;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags bindingPath_UxmlAttributeFlags;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags label_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(bindingPath), "binding-path", null),
                    new UxmlAttributeNames(nameof(label), "label", null),
                });
            }

            public override object CreateInstance() => new PropertyField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                var field = (PropertyField)obj;
                if (ShouldWriteAttributeValue(bindingPath_UxmlAttributeFlags)) field.bindingPath = bindingPath;
                if (ShouldWriteAttributeValue(label_UxmlAttributeFlags)) field.label = label;
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<PropertyField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription m_PropertyPath;
            readonly UxmlStringAttributeDescription m_Label;

            public UxmlTraits()
            {
                m_PropertyPath = new UxmlStringAttributeDescription { name = "binding-path" };
                m_Label = new UxmlStringAttributeDescription { name = "label", defaultValue = null };
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve is PropertyField propertyField)
                {
                    propertyField.label = m_Label.GetValueFromBag(bag, cc);
                    string path = m_PropertyPath.GetValueFromBag(bag, cc);
                    if (!string.IsNullOrEmpty(path)) propertyField.bindingPath = path;
                }
            }
        }

        static readonly BindingId labelProperty = nameof(label);

        internal static readonly string foldoutTitleBoundLabelProperty = "unity-foldout-bound-title";
        internal static readonly string decoratorDrawersContainerClassName = "unity-decorator-drawers-container";
        internal static readonly string listViewBoundFieldProperty = "unity-list-view-property-field-bound";

        public static readonly string ussClassName = "unity-property-field";
        public static readonly string labelUssClassName = ussClassName + "__label";
        public static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string inspectorElementUssClassName = ussClassName + "__inspector-property";
        internal static readonly string imguiContainerPropertyUssClassName = ussClassName + "__imgui-container-property";

        static readonly Func<Toggle> s_CreateToggle = () => new Toggle();
        static readonly Func<IntegerField> s_CreateIntegerField = () => new IntegerField();
        static readonly Func<LongField> s_CreateLongField = () => new LongField();
        static readonly Func<UnsignedIntegerField> s_CreateUnsignedIntegerField = () => new UnsignedIntegerField();
        static readonly Func<UnsignedLongField> s_CreateUnsignedLongField = () => new UnsignedLongField();
        static readonly Func<FloatField> s_CreateFloatField = () => new FloatField();
        static readonly Func<DoubleField> s_CreateDoubleField = () => new DoubleField();
        static readonly Func<TextField> s_CreateTextField = () => new TextField();
        static readonly Func<Vector2Field> s_CreateVector2Field = () => new Vector2Field();
        static readonly Func<Vector3Field> s_CreateVector3Field = () => new Vector3Field();

        string m_Label;
        SerializedObject m_SerializedObject;
        SerializedProperty m_SerializedProperty;
        VisualElement m_ChildField;

        public IBinding binding { get; set; }
        public string bindingPath { get; set; }

        [CreateProperty]
        public string label
        {
            get => m_Label;
            set
            {
                if (m_Label == value) return;
                m_Label = value;
                Rebind();
                NotifyPropertyChanged(in labelProperty);
            }
        }

        internal SerializedObject serializedObject => m_SerializedObject;

        public PropertyField() : this(null, null) { }

        public PropertyField(SerializedProperty property) : this(property, null) { }

        public PropertyField(SerializedProperty property, string label)
        {
            AddToClassList(ussClassName);
            this.label = label;
            if (property != null) bindingPath = property.propertyPath;
        }

        [EventInterest(new[] { typeof(SerializedPropertyBindEvent) })]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (evt is SerializedPropertyBindEvent bindEvt)
            {
                Reset(bindEvt.bindProperty);
                evt.StopPropagation();
            }
        }

        [EventInterest(EventInterestOptions.Inherit)]
        [Obsolete("ExecuteDefaultActionAtTarget override has been removed because default event handling was migrated to HandleEventBubbleUp. Please use HandleEventBubbleUp.", false)]
        [Unbound.EditorFaithful("Unity keeps an empty obsolete override after migrating handling to HandleEventBubbleUp")]
        protected override void ExecuteDefaultActionAtTarget(EventBase evt) { }

        void Reset(SerializedProperty newProperty)
        {
            if (newProperty == null) return;
            var propertyType = newProperty.propertyType;
            bool needsRebuild = true;
            if (m_SerializedObject != null && m_SerializedObject.isValid
                && m_SerializedProperty != null && m_SerializedProperty.isValid
                && propertyType == m_SerializedProperty.propertyType)
            {
                needsRebuild = false;
            }
            m_SerializedProperty = newProperty;
            m_SerializedObject = newProperty.serializedObject;

            if (m_ChildField != null && !needsRebuild)
            {
                var updated = CreateOrUpdateFieldFromProperty(newProperty, m_ChildField);
                if (!ReferenceEquals(updated, m_ChildField))
                {
                    m_ChildField.Unbind();
                    int index = IndexOf(m_ChildField);
                    if (index >= 0)
                    {
                        m_ChildField.RemoveFromHierarchy();
                        m_ChildField = updated;
                        if (m_ChildField != null) hierarchy.Insert(index, m_ChildField);
                    }
                    else
                    {
                        m_ChildField = updated;
                        if (m_ChildField != null) hierarchy.Add(m_ChildField);
                    }
                }
                return;
            }

            m_ChildField?.Unbind();
            m_ChildField = null;
            Clear();
            if (m_SerializedProperty == null || !m_SerializedProperty.isValid) return;

            m_ChildField = CreateOrUpdateFieldFromProperty(m_SerializedProperty, null);
            if (m_ChildField != null) hierarchy.Add(m_ChildField);
        }

        void Rebind()
        {
            if (m_SerializedProperty == null || !m_SerializedProperty.isValid) return;
            var obj = m_SerializedProperty.serializedObject;
            this.Unbind();
            this.Bind(obj);
        }

        void OnFieldValueChanged(EventBase evt)
        {
            if (ReferenceEquals(evt.target, m_ChildField) && m_SerializedProperty != null && m_SerializedProperty.isValid)
                DispatchPropertyChangedEvent();
        }

        void DispatchPropertyChangedEvent()
        {
            using var evt = SerializedPropertyChangeEvent.GetPooled(m_SerializedProperty);
            evt.elementTarget = this;
            SendEvent(evt);
        }

        public void RegisterValueChangeCallback(EventCallback<SerializedPropertyChangeEvent> callback)
        {
            if (callback == null) return;
            RegisterCallback<SerializedPropertyChangeEvent>(evt =>
            {
                if (ReferenceEquals(evt.target, this)) callback(evt);
            });
        }

        TField ConfigureField<TField, TValue>(TField field, SerializedProperty property, Func<TField> factory)
            where TField : BaseField<TValue>
        {
            if (field == null)
            {
                field = factory();
                field.RegisterValueChangedCallback(OnFieldValueChanged);
            }
            string fieldLabel = label ?? property.displayName;
            field.bindingPath = property.propertyPath;
            field.SetProperty(BaseField<TValue>.serializedPropertyCopyName, property.Copy());
            field.name = "unity-input-" + property.propertyPath;
            field.label = fieldLabel;
            ConfigureFieldStyles<TField, TValue>(field);
            return field;
        }

        internal static void ConfigureFieldStyles<TField, TValue>(TField field) where TField : BaseField<TValue>
        {
            field.labelElement.AddToClassList(labelUssClassName);
            field.visualInput.AddToClassList(inputUssClassName);
            field.AddToClassList(BaseField<TValue>.alignedFieldUssClassName);
        }

        VisualElement CreateOrUpdateFieldFromProperty(SerializedProperty property, object originalField)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return ConfigureField<Toggle, bool>(originalField as Toggle, property, s_CreateToggle);
                case SerializedPropertyType.Integer:
                    if (property.numericType == SerializedPropertyNumericType.Int64 || property.type == "long")
                        return ConfigureField<LongField, long>(originalField as LongField, property, s_CreateLongField);
                    if (property.numericType == SerializedPropertyNumericType.UInt64 || property.type == "ulong")
                        return ConfigureField<UnsignedLongField, ulong>(originalField as UnsignedLongField, property, s_CreateUnsignedLongField);
                    if (property.numericType == SerializedPropertyNumericType.UInt32 || property.type == "uint")
                        return ConfigureField<UnsignedIntegerField, uint>(originalField as UnsignedIntegerField, property, s_CreateUnsignedIntegerField);
                    var intField = ConfigureField<IntegerField, int>(originalField as IntegerField, property, s_CreateIntegerField);
                    if (intField != null) intField.isDelayed = false;
                    return intField;
                case SerializedPropertyType.Float:
                    if (property.numericType == SerializedPropertyNumericType.Double || property.type == "double")
                        return ConfigureField<DoubleField, double>(originalField as DoubleField, property, s_CreateDoubleField);
                    return ConfigureField<FloatField, float>(originalField as FloatField, property, s_CreateFloatField);
                case SerializedPropertyType.String:
                    var textField = ConfigureField<TextField, string>(originalField as TextField, property, s_CreateTextField);
                    if (textField != null) textField.maxLength = -1;
                    return textField;
                case SerializedPropertyType.Enum:
                {
                    var enumType = property.ManagedEnumType;
                    if (enumType != null && enumType.IsDefined(typeof(FlagsAttribute), false))
                    {
                        var flagsData = EnumDataUtility.GetCachedEnumData(enumType);
                        if (originalField is EnumFlagsField existingFlags)
                        {
                            existingFlags.choices = new List<string>(flagsData.displayNames);
                            existingFlags.value = (Enum)Enum.ToObject(enumType, property.intValue);
                        }
                        return ConfigureField<EnumFlagsField, Enum>(
                            originalField as EnumFlagsField,
                            property,
                            () => new EnumFlagsField
                            {
                                choices = new List<string>(flagsData.displayNames),
                                value = (Enum)Enum.ToObject(enumType, property.intValue),
                            });
                    }
                    EnumData? enumData = enumType != null
                        ? EnumDataUtility.GetCachedEnumData(enumType, excludeObsolete: false)
                        : (EnumData?)null;
                    string[] enumDisplayNames = property.enumDisplayNames;
                    var popupEntries = new List<string>(enumData.HasValue ? enumData.Value.displayNames : enumDisplayNames);
                    int propertyFieldIndex;
                    int rawIndex = property.enumValueIndex;
                    if (rawIndex < 0 || rawIndex >= enumDisplayNames.Length)
                        propertyFieldIndex = -1;
                    else
                        propertyFieldIndex = enumData.HasValue
                            ? Array.IndexOf(enumData.Value.displayNames, enumDisplayNames[rawIndex])
                            : rawIndex;
                    if (originalField is PopupField<string> existingPopup)
                    {
                        existingPopup.choices = popupEntries;
                        existingPopup.index = propertyFieldIndex;
                    }
                    return ConfigureField<PopupField<string>, string>(
                        originalField as PopupField<string>,
                        property,
                        () => new PopupField<string>(popupEntries, rawIndex) { index = propertyFieldIndex });
                }
                case SerializedPropertyType.Vector2:
                    return ConfigureField<Vector2Field, Vector2>(originalField as Vector2Field, property, s_CreateVector2Field);
                case SerializedPropertyType.Vector3:
                    return ConfigureField<Vector3Field, Vector3>(originalField as Vector3Field, property, s_CreateVector3Field);
                default:
                    Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.UIElements.PropertyField ({property.propertyType})");
                    return null;
            }
        }
    }
}
