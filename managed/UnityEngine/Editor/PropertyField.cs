using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit PropertyField for supported scalar, enum, Color/Color32 (including
// ColorUsageAttribute), Vector2, Vector3, Vector4, Vector2Int, Vector3Int, Rect, RectInt, Bounds,
// BoundsInt, AnimationCurve, Gradient (including GradientUsageAttribute), ObjectReference, Hash128,
// LayerMask, RenderingLayerMask, non-array Generic inline-foldout, and array/list SerializedProperty
// types (bool, int, long, uint, ulong, float, double, string, ordinary and [Flags] enum,
// Color/Color32, Vector2, Vector3, Vector4 and Generic inline Foldout with lazy child
// PropertyFields, ListView for arrays/lists, Vector2Int, Vector3Int, Rect, RectInt, Bounds,
// BoundsInt, AnimationCurve, Gradient, ObjectReference, Hash128, LayerMask, and
// RenderingLayerMask), SerializedPropertyBindEvent, and SerializedObjectBindEvent. Documented in
// Unity 6000.0 Scripting API ("PropertyField", "PropertyField.RegisterValueChangeCallback",
// "ListView", "ColorUsageAttribute", "GradientUsageAttribute"); matches the Unity 6000.0.71f1
// Editor, tests/projects/propertyfield, tests/projects/enumbinding, tests/projects/enumflagsbinding,
// tests/projects/colorbinding, tests/projects/vector2binding, tests/projects/vector3binding,
// tests/projects/vector4binding, tests/projects/vector2intbinding,
// tests/projects/vector3intbinding, tests/projects/rectbinding, tests/projects/rectintbinding,
// tests/projects/boundsbinding, tests/projects/boundsintbinding, tests/projects/curvebinding,
// tests/projects/gradientbinding, tests/projects/objectbinding, tests/projects/hash128binding,
// tests/projects/maskbinding, tests/projects/genericfoldoutbinding, and
// tests/projects/arraylistbinding.

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

    [EventCategory(EventCategory.Bind)]
    internal partial class SerializedObjectBindEvent : EventBase<SerializedObjectBindEvent>
    {
        SerializedObject m_BindObject;

        public SerializedObject bindObject => m_BindObject;

        static SerializedObjectBindEvent() => SetCreateFunction(() => new SerializedObjectBindEvent());

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit()
        {
            propagation = EventPropagation.None;
            m_BindObject = null;
        }

        public static SerializedObjectBindEvent GetPooled(SerializedObject obj)
        {
            var evt = EventBase<SerializedObjectBindEvent>.GetPooled();
            evt.m_BindObject = obj;
            return evt;
        }

        public SerializedObjectBindEvent() => LocalInit();
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
        static readonly string listViewNamePrefix = "unity-list-";
        static readonly string buttonGroupNamePrefix = "unity-button-group-";

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
        static readonly Func<TextField> s_CreateCharTextField = () => new TextField { maxLength = 1 };
        static readonly Func<ColorField> s_CreateColorField = () => new ColorField();
        static readonly Func<Vector2Field> s_CreateVector2Field = () => new Vector2Field();
        static readonly Func<Vector3Field> s_CreateVector3Field = () => new Vector3Field();
        static readonly Func<Vector2IntField> s_CreateVector2IntField = () => new Vector2IntField();
        static readonly Func<Vector3IntField> s_CreateVector3IntField = () => new Vector3IntField();
        static readonly Func<RectField> s_CreateRectField = () => new RectField();
        static readonly Func<RectIntField> s_CreateRectIntField = () => new RectIntField();
        static readonly Func<BoundsField> s_CreateBoundsField = () => new BoundsField();
        static readonly Func<BoundsIntField> s_CreateBoundsIntField = () => new BoundsIntField();
        static readonly Func<CurveField> s_CreateCurveField = () => new CurveField();
        static readonly Func<GradientField> s_CreateGradientField = () => new GradientField();
        static readonly Func<ObjectField> s_CreateObjectField = () => new ObjectField();
        static readonly Func<Hash128Field> s_CreateHash128Field = () => new Hash128Field();
        static readonly Func<LayerMaskField> s_CreateLayerMaskField = () => new LayerMaskField();
        static readonly Func<RenderingLayerMaskField> s_CreateRenderingLayerMaskField = () => new RenderingLayerMaskField();
        static readonly Func<ListView> s_CreateListView = () => new ListView();
        static readonly Func<ToggleButtonGroup> s_CreateToggleButtonGroup = () => new ToggleButtonGroup();

        string m_Label;
        SerializedObject m_SerializedObject;
        SerializedProperty m_SerializedProperty;
        VisualElement m_ChildField;
        VisualElement m_CustomPropertyGUI;
        VisualElement m_ChildrenContainer;
        List<PropertyField> m_ChildrenProperties;

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
                        hierarchy.Insert(index, m_ChildField);
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
            m_CustomPropertyGUI?.Unbind();
            m_CustomPropertyGUI = null;
            TrimChildrenContainerSize(0);
            m_ChildrenContainer = null;
            Clear();
            if (m_SerializedProperty == null || !m_SerializedProperty.isValid) return;

            // [ColorUsage] and [GradientUsage] give the field Unity's ColorUsageDrawer and
            // GradientUsageDrawer build; a collection's attribute applies to each element.
            var declaringField = m_SerializedProperty.UnboundDeclaringField;
            ReportUnsupportedDrawers(m_SerializedProperty, declaringField);
            if (m_SerializedProperty.propertyType == SerializedPropertyType.Color
                && declaringField?.GetCustomAttribute<ColorUsageAttribute>(false) is ColorUsageAttribute colorUsage)
            {
                var colorField = new ColorField(label ?? m_SerializedProperty.displayName)
                {
                    showAlpha = colorUsage.showAlpha,
                    hdr = colorUsage.hdr,
                    bindingPath = m_SerializedProperty.propertyPath
                };
                colorField.AddToClassList(BaseField<Color>.alignedFieldUssClassName);
                colorField.RegisterValueChangedCallback(OnCustomPropertyFieldValueChanged);
                m_CustomPropertyGUI = colorField;
                hierarchy.Add(colorField);
                return;
            }

            if (m_SerializedProperty.propertyType == SerializedPropertyType.Gradient
                && declaringField?.GetCustomAttribute<GradientUsageAttribute>(false) is GradientUsageAttribute gradientUsage)
            {
                var gradientField = new GradientField(label ?? m_SerializedProperty.displayName)
                {
                    hdr = gradientUsage.hdr,
                    colorSpace = gradientUsage.colorSpace,
                    bindingPath = m_SerializedProperty.propertyPath
                };
                gradientField.AddToClassList(BaseField<Gradient>.alignedFieldUssClassName);
                gradientField.RegisterValueChangedCallback(OnCustomPropertyFieldValueChanged);
                m_CustomPropertyGUI = gradientField;
                hierarchy.Add(gradientField);
                return;
            }

            m_ChildField = CreateOrUpdateFieldFromProperty(m_SerializedProperty, null);
            if (m_ChildField != null) hierarchy.Add(m_ChildField);
        }

        void TrimChildrenContainerSize(int targetSize)
        {
            if (m_ChildrenProperties == null) return;
            while (m_ChildrenProperties.Count > targetSize)
            {
                int index = m_ChildrenProperties.Count - 1;
                var childPf = m_ChildrenProperties[index];
                childPf.Unbind();
                childPf.RemoveFromHierarchy();
                m_ChildrenProperties.RemoveAt(index);
            }
        }

        void RefreshChildrenProperties(SerializedProperty property, bool bindNewFields)
        {
            if (m_ChildrenContainer == null || property == null) return;
            var iter = property.Copy();
            var endProperty = iter.GetEndProperty();
            int count = 0;
            m_ChildrenProperties ??= new List<PropertyField>();
            if (iter.NextVisible(true))
            {
                while (!SerializedProperty.EqualContents(iter, endProperty))
                {
                    PropertyField childPf;
                    string propertyPath = iter.propertyPath;
                    if (count < m_ChildrenProperties.Count)
                    {
                        childPf = m_ChildrenProperties[count];
                        childPf.bindingPath = propertyPath;
                    }
                    else
                    {
                        childPf = new PropertyField(iter);
                        m_ChildrenProperties.Add(childPf);
                        childPf.bindingPath = propertyPath;
                    }
                    childPf.name = "unity-property-field-" + propertyPath;
                    if (bindNewFields) childPf.Bind(iter.serializedObject);
                    m_ChildrenContainer.Add(childPf);
                    count++;
                    if (!iter.NextVisible(false)) break;
                }
            }
            TrimChildrenContainerSize(count);
        }

        VisualElement CreateFoldout(SerializedProperty property, object originalField)
        {
            property = property.Copy();
            var foldout = originalField as Foldout ?? new Foldout();
            bool hasExplicitLabel = !string.IsNullOrEmpty(label);
            foldout.text = hasExplicitLabel ? label : property.displayName;
            foldout.bindingPath = property.propertyPath;
            foldout.name = "unity-foldout-" + property.propertyPath;
            var toggle = foldout.toggle;
            toggle.acceptClicksIfDisabled = true;
            var titleLabel = toggle.boolFieldLabelElement;
            if (titleLabel != null)
            {
                if (hasExplicitLabel)
                {
                    titleLabel.text = foldout.text;
                }
                else
                {
                    titleLabel.bindingPath = property.propertyPath;
                    titleLabel.SetProperty(foldoutTitleBoundLabelProperty, true);
                }
            }
            m_ChildrenContainer = foldout;
            if (property.isExpanded)
            {
                RefreshChildrenProperties(property, bindNewFields: false);
            }
            else
            {
                foldout.RegisterValueChangedCallback(RefreshOnlyWhenExpanded);
            }
            return foldout;
        }

        void RefreshOnlyWhenExpanded(ChangeEvent<bool> evt)
        {
            if (evt.newValue && evt.target is Foldout foldout && ReferenceEquals(evt.target, m_ChildField))
            {
                foldout.UnregisterCallback<ChangeEvent<bool>>(RefreshOnlyWhenExpanded);
                RefreshChildrenProperties(m_SerializedProperty, bindNewFields: true);
            }
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

        // A drawer's element reports its changes a frame later, merged: Unity schedules one
        // SerializedPropertyChangeEvent per change and sends it only when the last one runs.
        int m_PropertyChangedCounter;

        void OnCustomPropertyFieldValueChanged(EventBase evt)
        {
            m_PropertyChangedCounter++;
            schedule.Execute(ExecuteAsyncDispatchPropertyChangedEvent);
        }

        void ExecuteAsyncDispatchPropertyChangedEvent()
        {
            m_PropertyChangedCounter--;
            if (m_PropertyChangedCounter > 0) return;
            m_PropertyChangedCounter = 0;
            if (m_SerializedProperty != null && m_SerializedProperty.isValid) DispatchPropertyChangedEvent();
        }

        // Unity's PropertyField builds the GUI of a property's drawer (its type's
        // [CustomPropertyDrawer], or a drawer attribute such as [Range], [Min], [Delayed],
        // [Multiline], [TextArea]) and its decorators ([Header], [Space], custom DecoratorDrawers).
        // Only [ColorUsage] and [GradientUsage] are built here; the others are reported instead of
        // being dropped without a word, and the property gets its default field.
        static void ReportUnsupportedDrawers(SerializedProperty property, FieldInfo field)
        {
            bool element = field != null && field != property.UnboundField;
            bool collection = property.isArray && property.propertyType != SerializedPropertyType.String;
            if (field != null)
            {
                foreach (var attribute in field.GetCustomAttributes<PropertyAttribute>(true))
                {
                    if (attribute is ColorUsageAttribute || attribute is GradientUsageAttribute) continue;
                    bool decorator = attribute is HeaderAttribute || attribute is SpaceAttribute || PropertyDrawers.DecoratorFor(attribute) != null;
                    bool drawer = attribute is RangeAttribute || attribute is MinAttribute || attribute is DelayedAttribute
                        || attribute is MultilineAttribute || attribute is TextAreaAttribute
                        || PropertyDrawers.DrawerFor(property, null, attribute) != null;
                    if ((decorator && !element) || (drawer && !collection))
                        Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.UIElements.PropertyField ({(decorator ? "decorator" : "drawer")} of {attribute.GetType().Name})");
                }
            }
            if (!collection && PropertyDrawers.DrawerFor(property, field, null) is PropertyDrawer typeDrawer)
                Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.UIElements.PropertyField (drawer {typeDrawer.GetType().Name})");
            else if (!collection && field != null && typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(ElementTypeOf(field.FieldType, element)))
                Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.PropertyField (drawer of UnityEvent)");
        }

        static Type ElementTypeOf(Type type, bool element)
        {
            if (!element) return type;
            if (type.IsArray) return type.GetElementType();
            return type.IsGenericType ? type.GetGenericArguments()[0] : type;
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
            // A [Tooltip] property keeps its tooltip when the label is cut off, instead of the
            // label's full text.
            if (property.UnboundDeclaringField is FieldInfo declaring)
                field.labelElement.displayTooltipWhenElided = !declaring.IsDefined(typeof(TooltipAttribute), false);
            return field;
        }

        internal static void ConfigureFieldStyles<TField, TValue>(TField field) where TField : BaseField<TValue>
        {
            field.labelElement.AddToClassList(labelUssClassName);
            field.visualInput.AddToClassList(inputUssClassName);
            field.AddToClassList(BaseField<TValue>.alignedFieldUssClassName);
        }

        VisualElement ConfigureListView(ListView listView, SerializedProperty property, Func<ListView> factory)
        {
            if (listView == null)
            {
                listView = factory();
                listView.showBorder = true;
                listView.selectionType = SelectionType.Multiple;
                listView.showAddRemoveFooter = true;
                listView.showBoundCollectionSize = true;
                listView.showFoldoutHeader = true;
                listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
                listView.showAlternatingRowBackgrounds = AlternatingRowBackground.None;
                listView.itemsSourceSizeChanged += DispatchPropertyChangedEvent;
            }
            var copy = property.Copy();
            string listName = listViewNamePrefix + property.propertyPath;
            listView.headerTitle = string.IsNullOrEmpty(label) ? copy.displayName : label;
            listView.userData = copy;
            listView.bindingPath = property.propertyPath;
            listView.viewDataKey = listName;
            listView.name = listName;
            listView.SetProperty(listViewBoundFieldProperty, this);
            listView.SetProperty(BaseField<string>.serializedPropertyCopyName, copy);
            var toggle = listView.headerFoldout?.toggle;
            if (toggle != null) toggle.acceptClicksIfDisabled = true;
            return listView;
        }

        VisualElement ConfigureToggleButtonGroup(ToggleButtonGroup buttonGroup, SerializedProperty property, Func<ToggleButtonGroup> factory)
        {
            var propertyCopy = property.Copy();
            if (buttonGroup == null)
            {
                buttonGroup = factory();
                buttonGroup.AddToClassList(BaseField<bool>.alignedFieldUssClassName);
                buttonGroup.AddToClassList(ToggleButtonGroup.ussClassName + "--fixed-size");
                buttonGroup.RegisterValueChangedCallback(OnToggleGroupChanged);
            }
            var lengthProperty = propertyCopy.FindPropertyRelative("m_Length");
            var dataProperty = propertyCopy.FindPropertyRelative("m_Data");
            int count = lengthProperty.intValue;
            if (property.UnboundDeclaringField?.GetCustomAttribute<ToggleButtonGroupStatePropertiesAttribute>(false) is ToggleButtonGroupStatePropertiesAttribute attr)
            {
                if (attr.length >= 0)
                {
                    count = attr.length;
                    lengthProperty.intValue = count;
                    lengthProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                }
                buttonGroup.allowEmptySelection = attr.allowEmptySelection;
                buttonGroup.isMultipleSelection = attr.allowMultipleSelection;
            }
            string groupName = buttonGroupNamePrefix + property.propertyPath;
            string groupLabel = label ?? property.displayName;
            buttonGroup.userData = propertyCopy;
            buttonGroup.bindingPath = property.propertyPath;
            buttonGroup.SetProperty(BaseField<ToggleButtonGroupState>.serializedPropertyCopyName, propertyCopy);
            buttonGroup.name = groupName;
            buttonGroup.label = groupLabel;
            buttonGroup.Q(null, ToggleButtonGroup.buttonGroupClassName).Clear();
            buttonGroup.TrackPropertyValue(propertyCopy);
            buttonGroup.TrackPropertyValue(propertyCopy, OnPropertyChanged);
            buttonGroup.TrackPropertyValue(lengthProperty, OnPropertyChanged);
            buttonGroup.TrackPropertyValue(dataProperty, OnPropertyChanged);
            for (int i = 0; i < count; i++)
                buttonGroup.Add(new Button { text = i.ToString() });
            return buttonGroup;

            void OnPropertyChanged(SerializedProperty _)
            {
                int currentLength = buttonGroup.value.length;
                int targetLength = lengthProperty.intValue;
                ulong data = dataProperty.ulongValue;
                if (data == 0UL && targetLength == 64) targetLength = 0;
                if (currentLength < targetLength)
                {
                    for (int i = currentLength; i < targetLength; i++)
                        buttonGroup.Add(new Button { text = i.ToString() });
                }
                else if (currentLength > targetLength)
                {
                    for (int i = targetLength; i < currentLength; i++)
                    {
                        var btn = buttonGroup.Query<Button>(null, ToggleButtonGroup.buttonClassName).Last();
                        if (btn == null) break;
                        btn.RemoveFromHierarchy();
                    }
                }
                buttonGroup.SetValueWithoutNotify((ToggleButtonGroupState)propertyCopy.structValue);
            }

            void OnToggleGroupChanged(ChangeEvent<ToggleButtonGroupState> evt)
            {
                propertyCopy.structValue = evt.newValue;
                propertyCopy.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        VisualElement CreateOrUpdateFieldFromProperty(SerializedProperty property, object originalField)
        {
            if (((property.propertyType == SerializedPropertyType.Vector4
                  || property.propertyType == SerializedPropertyType.Quaternion)
                 || (property.propertyType == SerializedPropertyType.Generic
                     && !property.isArray
                     && property.type != "ToggleButtonGroupState"))
                && property.hasVisibleChildren)
                return CreateFoldout(property, originalField);
            TrimChildrenContainerSize(0);
            m_ChildrenContainer = null;
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
                case SerializedPropertyType.Character:
                {
                    var charField = originalField as TextField;
                    if (charField != null) charField.maxLength = 1;
                    return ConfigureField<TextField, string>(charField, property, s_CreateCharTextField);
                }
                case SerializedPropertyType.Quaternion:
                    return null;
                case SerializedPropertyType.Color:
                    return ConfigureField<ColorField, Color>(originalField as ColorField, property, s_CreateColorField);
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
                        ? EnumDataUtility.GetCachedEnumData(enumType)
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
                case SerializedPropertyType.Vector2Int:
                    return ConfigureField<Vector2IntField, Vector2Int>(originalField as Vector2IntField, property, s_CreateVector2IntField);
                case SerializedPropertyType.Vector3Int:
                    return ConfigureField<Vector3IntField, Vector3Int>(originalField as Vector3IntField, property, s_CreateVector3IntField);
                case SerializedPropertyType.Rect:
                    return ConfigureField<RectField, Rect>(originalField as RectField, property, s_CreateRectField);
                case SerializedPropertyType.RectInt:
                    return ConfigureField<RectIntField, RectInt>(originalField as RectIntField, property, s_CreateRectIntField);
                case SerializedPropertyType.Bounds:
                    return ConfigureField<BoundsField, Bounds>(originalField as BoundsField, property, s_CreateBoundsField);
                case SerializedPropertyType.BoundsInt:
                    return ConfigureField<BoundsIntField, BoundsInt>(originalField as BoundsIntField, property, s_CreateBoundsIntField);
                case SerializedPropertyType.AnimationCurve:
                    return ConfigureField<CurveField, AnimationCurve>(originalField as CurveField, property, s_CreateCurveField);
                case SerializedPropertyType.Gradient:
                    return ConfigureField<GradientField, Gradient>(originalField as GradientField, property, s_CreateGradientField);
                case SerializedPropertyType.ObjectReference:
                {
                    var objectField = ConfigureField<ObjectField, UnityEngine.Object>(originalField as ObjectField, property, s_CreateObjectField);
                    objectField.SetObjectTypeWithoutDisplayUpdate(property.ObjectReferenceFieldType);
                    objectField.UpdateDisplay();
                    return objectField;
                }
                case SerializedPropertyType.Hash128:
                    return ConfigureField<Hash128Field, Hash128>(originalField as Hash128Field, property, s_CreateHash128Field);
                case SerializedPropertyType.LayerMask:
                    return ConfigureField<LayerMaskField, int>(originalField as LayerMaskField, property, s_CreateLayerMaskField);
                case SerializedPropertyType.RenderingLayerMask:
                    return ConfigureField<RenderingLayerMaskField, uint>(originalField as RenderingLayerMaskField, property, s_CreateRenderingLayerMaskField);
                case SerializedPropertyType.Generic:
                    if (property.type == "ToggleButtonGroupState")
                        return ConfigureToggleButtonGroup(originalField as ToggleButtonGroup, property, s_CreateToggleButtonGroup);
                    return property.isArray ? ConfigureListView(originalField as ListView, property, s_CreateListView) : null;
                default:
                    Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.UIElements.PropertyField ({property.propertyType})");
                    return null;
            }
        }
    }
}
