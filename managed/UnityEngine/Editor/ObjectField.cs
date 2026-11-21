using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// Editor UI Toolkit ObjectField for ObjectReference SerializedProperty binding.
// Scoped to field state, objectType, allowSceneObjects, visual display hierarchy (icon, label,
// selector element), GameObject-to-Component coercion, null/missing-reference display, mixed-value
// display, UXML serialization/traits, and BindProperty/Bind/PropertyField integration without
// standalone ObjectSelector popup or drag-and-drop UI.
// Documented in Unity 6000.0 Scripting API ("ObjectField"); matches the Unity 6000.0.71f1 Editor,
// tests/projects/objectbinding.

namespace UnityEditor.UIElements
{
    public partial class ObjectField : BaseField<UnityEngine.Object>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<UnityEngine.Object>.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField]
            [UxmlTypeReference(typeof(UnityEngine.Object))]
            [UxmlAttribute("type")]
            string objectType;

            [SerializeField]
            bool allowSceneObjects;

            [HideInInspector]
            [UxmlIgnore]
            [SerializeField]
            UxmlAttributeFlags allowSceneObjects_UxmlAttributeFlags;

            [HideInInspector]
            [UxmlIgnore]
            [SerializeField]
            UxmlAttributeFlags objectType_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<UnityEngine.Object>.UxmlSerializedData.Register();
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames("allowSceneObjects", "allow-scene-objects", null),
                    new UxmlAttributeNames("objectType", "type", typeof(UnityEngine.Object)),
                });
            }

            public override object CreateInstance() => new ObjectField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                var objectField = (ObjectField)obj;
                if (ShouldWriteAttributeValue(allowSceneObjects_UxmlAttributeFlags))
                    objectField.allowSceneObjects = allowSceneObjects;
                if (ShouldWriteAttributeValue(objectType_UxmlAttributeFlags))
                    objectField.objectType = UxmlUtility.ParseType(objectType, typeof(UnityEngine.Object));
            }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ObjectField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : BaseField<UnityEngine.Object>.UxmlTraits
        {
            readonly UxmlBoolAttributeDescription m_AllowSceneObjects = new UxmlBoolAttributeDescription
            {
                name = "allow-scene-objects",
                defaultValue = true,
            };

            readonly UxmlTypeAttributeDescription<UnityEngine.Object> m_ObjectType = new UxmlTypeAttributeDescription<UnityEngine.Object>
            {
                name = "type",
                defaultValue = typeof(UnityEngine.Object),
            };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var field = (ObjectField)ve;
                field.allowSceneObjects = m_AllowSceneObjects.GetValueFromBag(bag, cc);
                field.objectType = m_ObjectType.GetValueFromBag(bag, cc);
            }
        }

        internal partial class ObjectFieldDisplay : VisualElement
        {
            readonly ObjectField m_ObjectField;
            readonly Image m_ObjectIcon;
            readonly Label m_ObjectLabel;

            static readonly string ussClassName = "unity-object-field-display";
            static readonly string iconUssClassName = ussClassName + "__icon";
            internal static readonly string labelUssClassName = ussClassName + "__label";
            internal static readonly string nullLabelUssClassName = labelUssClassName + "--value-null";

            public bool isObjectMissing { get; private set; }

            public ObjectFieldDisplay(ObjectField objectField)
            {
                AddToClassList(ussClassName);
                m_ObjectIcon = new Image
                {
                    scaleMode = ScaleMode.ScaleAndCrop,
                    pickingMode = PickingMode.Ignore,
                };
                m_ObjectIcon.AddToClassList(iconUssClassName);

                m_ObjectLabel = new Label
                {
                    pickingMode = PickingMode.Ignore,
                };
                m_ObjectLabel.AddToClassList(labelUssClassName);

                m_ObjectField = objectField;
                Update();
                Add(m_ObjectIcon);
                Add(m_ObjectLabel);
            }

            internal void ShowMixedValue(bool show)
            {
                if (show)
                {
                    m_ObjectLabel.text = mixedValueString;
                    m_ObjectLabel.AddToClassList(mixedValueLabelUssClassName);
                    m_ObjectIcon.image = null;
                }
                else
                {
                    m_ObjectLabel.RemoveFromClassList(mixedValueLabelUssClassName);
                    Update();
                }
            }

            public void Update()
            {
                isObjectMissing = false;
                var serializedProperty = m_ObjectField.GetProperty(serializedPropertyKey) as SerializedProperty;
                if (serializedProperty != null)
                {
                    if (!serializedProperty.isValid)
                    {
                        m_ObjectField.SetProperty(serializedPropertyKey, null);
                        return;
                    }
                    isObjectMissing = IsMissingObjectReference(serializedProperty);
                }

                var content = EditorGUIUtility.ObjectContentForProperty(m_ObjectField.value, m_ObjectField.objectType, serializedProperty);
                m_ObjectIcon.image = content.image;
                m_ObjectLabel.text = content.text;
                m_ObjectLabel.EnableInClassList(nullLabelUssClassName, m_ObjectField.value == null);
            }

            [EventInterest(new[]
            {
                typeof(MouseDownEvent),
                typeof(KeyDownEvent),
                typeof(DragUpdatedEvent),
                typeof(DragPerformEvent),
                typeof(DragLeaveEvent),
            })]
            protected override void HandleEventBubbleUp(EventBase evt)
            {
                base.HandleEventBubbleUp(evt);
                if (evt is KeyDownEvent keyDown
                    && (keyDown.keyCode == KeyCode.Delete || keyDown.keyCode == KeyCode.Backspace))
                {
                    m_ObjectField.SetProperty(serializedPropertyKey, null);
                    m_ObjectField.value = null;
                }
            }

            [EventInterest(new[] { typeof(MouseDownEvent) })]
            internal override void HandleEventBubbleUpDisabled(EventBase evt)
            {
                base.HandleEventBubbleUpDisabled(evt);
            }
        }

        sealed class ObjectFieldSelector : VisualElement
        {
            readonly ObjectField m_ObjectField;

            public ObjectFieldSelector(ObjectField objectField) => m_ObjectField = objectField;

            [EventInterest(new[] { typeof(MouseDownEvent) })]
            protected override void HandleEventBubbleUp(EventBase evt)
            {
                base.HandleEventBubbleUp(evt);
                if (evt is MouseDownEvent mouseDown && mouseDown.button == 0)
                {
                    m_ObjectField.ShowObjectSelector();
                    evt.StopPropagation();
                }
            }
        }

        internal static readonly BindingId objectTypeProperty = nameof(objectType);
        internal static readonly BindingId allowSceneObjectsProperty = nameof(allowSceneObjects);

        public new static readonly string ussClassName = "unity-object-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string objectUssClassName = ussClassName + "__object";
        public static readonly string selectorUssClassName = ussClassName + "__selector";

        internal static readonly PropertyName serializedPropertyKey = new PropertyName("--unity-object-field-serialized-property");

        Type m_ObjectType;
        bool m_AllowSceneObjects;
        readonly ObjectFieldDisplay m_ObjectFieldDisplay;
        readonly Action m_AsyncOnProjectOrHierarchyChangedCallback;
        readonly Action m_OnProjectOrHierarchyChangedCallback;

        event Action m_OnObjectSelectorShow;

        internal event Action onObjectSelectorShow
        {
            add => m_OnObjectSelectorShow += value;
            remove => m_OnObjectSelectorShow -= value;
        }

        internal event Action<UnityEngine.Object, bool> SelectorClosed;

        [CreateProperty]
        public Type objectType
        {
            get => m_ObjectType;
            set
            {
                if (m_ObjectType == value) return;
                m_ObjectType = value;
                UpdateDisplay();
                NotifyPropertyChanged(in objectTypeProperty);
            }
        }

        [CreateProperty]
        public bool allowSceneObjects
        {
            get => m_AllowSceneObjects;
            set
            {
                if (m_AllowSceneObjects == value) return;
                m_AllowSceneObjects = value;
                NotifyPropertyChanged(in allowSceneObjectsProperty);
            }
        }

        public ObjectField() : this(null) { }

        public ObjectField(string label) : base(label, null)
        {
            visualInput.focusable = false;
            labelElement.focusable = false;
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);

            allowSceneObjects = true;
            m_ObjectType = typeof(UnityEngine.Object);

            m_ObjectFieldDisplay = new ObjectFieldDisplay(this) { focusable = true };
            m_ObjectFieldDisplay.AddToClassList(objectUssClassName);

            var selector = new ObjectFieldSelector(this);
            selector.AddToClassList(selectorUssClassName);

            visualInput.AddToClassList(inputUssClassName);
            visualInput.Add(m_ObjectFieldDisplay);
            visualInput.Add(selector);

            m_OnProjectOrHierarchyChangedCallback = UpdateDisplay;
            m_AsyncOnProjectOrHierarchyChangedCallback = () => schedule.Execute(m_OnProjectOrHierarchyChangedCallback);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                EditorApplication.projectChanged += m_AsyncOnProjectOrHierarchyChangedCallback;
                EditorApplication.hierarchyChanged += m_AsyncOnProjectOrHierarchyChangedCallback;
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                EditorApplication.projectChanged -= m_AsyncOnProjectOrHierarchyChangedCallback;
                EditorApplication.hierarchyChanged -= m_AsyncOnProjectOrHierarchyChangedCallback;
            });
        }

        internal override bool EqualsCurrentValue(UnityEngine.Object value)
        {
            if (m_ObjectFieldDisplay.isObjectMissing) return false;
            return base.EqualsCurrentValue(value);
        }

        public override void SetValueWithoutNotify(UnityEngine.Object newValue)
        {
            newValue = TryReadComponentFromGameObject(newValue, objectType);
            bool changed = !EqualityComparer<UnityEngine.Object>.Default.Equals(value, newValue);
            base.SetValueWithoutNotify(newValue);
            if (changed) UpdateDisplay();
        }

        internal void SetObjectTypeWithoutDisplayUpdate(Type type) => m_ObjectType = type;

        protected override void UpdateMixedValueContent() => m_ObjectFieldDisplay?.ShowMixedValue(showMixedValue);

        internal virtual void UpdateDisplay() => UpdateMixedValueContent();

        internal static bool IsMissingObjectReference(SerializedProperty p) =>
            p.propertyType == SerializedPropertyType.ObjectReference
            && p.objectReferenceInstanceIDValue != 0
            && p.objectReferenceValue == null;

        internal void OnObjectChanged(UnityEngine.Object obj) => value = TryReadComponentFromGameObject(obj, objectType);

        internal void OnObjectSelectorClosed(UnityEngine.Object obj) => SelectorClosed?.Invoke(obj, false);

        [Unbound.Stub]
        internal void ShowObjectSelector() => Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.ObjectField.ShowObjectSelector");

        UnityEngine.Object TryReadComponentFromGameObject(UnityEngine.Object obj, Type type)
        {
            if (obj is GameObject gameObject && type != null && type.IsSubclassOf(typeof(Component)))
            {
                var component = gameObject.GetComponent(m_ObjectType);
                if (component != null) return component;
            }
            return obj;
        }
    }
}
