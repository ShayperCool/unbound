using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;

// The search picker's UI Toolkit object field: a label, and an input holding the object's display
// (icon and label: EditorGUIUtility.ObjectContent without a null icon) and the picker button. Its
// structure, defaults, display, GameObject-to-component reading and UXML data match the Unity
// 6000.0.71f1 Editor (tests/projects/searchobjectfield). The search picker window it opens
// (ShowObjectSelector) and the IMGUI DoObjectField draw nothing in Unbound: they stay stubs.

namespace UnityEngine.Search
{
    /// <summary>How a search view opens (the release's values).</summary>
    [Flags]
    public enum SearchViewFlags
    {
        None = 0,
        Debug = 16,
        NoIndexing = 32,
        Packages = 256,
        OpenLeftSidePanel = 2048,
        OpenInspectorPreview = 4096,
        Centered = 8192,
        HideSearchBar = 16384,
        CompactView = 32768,
        ListView = 65536,
        GridView = 131072,
        TableView = 262144,
        EnableSearchQuery = 524288,
        DisableInspectorPreview = 1048576,
        DisableSavedSearchQuery = 2097152,
        OpenInBuilderMode = 4194304,
        OpenInTextMode = 8388608,
        DisableBuilderModeToggle = 16777216,
        Borderless = 33554432,
        DisableQueryHelpers = 67108864,
        DisableNoResultTips = 134217728,
        IgnoreSavedSearches = 268435456,
        ObjectPicker = 536870912,
        ObjectPickerAdvancedUI = 1073741824,
        ContextSwitchPreservedMask = 33560576,
    }
}

namespace UnityEditor.Search
{
    using UnityEngine.Search;

    public partial class ObjectField : BaseField<UnityEngine.Object>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BaseField<UnityEngine.Object>.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField]
            [UxmlTypeReference(typeof(UnityEngine.Object))]
            string type;

            [SerializeField]
            [UxmlIgnore]
            [HideInInspector]
            UxmlAttributeFlags type_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [RegisterUxmlCache]
            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                BaseField<UnityEngine.Object>.UxmlSerializedData.Register();
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames("type", "type", typeof(UnityEngine.Object)),
                });
            }

            public override object CreateInstance() => new ObjectField();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                if (ShouldWriteAttributeValue(type_UxmlAttributeFlags))
                    ((ObjectField)obj).objectType = UxmlUtility.ParseType(type, typeof(UnityEngine.Object));
            }
        }

        sealed class ObjectFieldDisplay : VisualElement
        {
            static readonly string displayUssClassName = "unity-object-field-display";
            static readonly string iconUssClassName = displayUssClassName + "__icon";
            static readonly string displayLabelUssClassName = displayUssClassName + "__label";

            readonly ObjectField m_ObjectField;
            readonly Image m_ObjectIcon;
            readonly Label m_ObjectLabel;

            public ObjectFieldDisplay(ObjectField objectField)
            {
                AddToClassList(displayUssClassName);
                m_ObjectIcon = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                m_ObjectIcon.AddToClassList(iconUssClassName);
                m_ObjectLabel = new Label { pickingMode = PickingMode.Ignore };
                m_ObjectLabel.AddToClassList(displayLabelUssClassName);
                m_ObjectField = objectField;
                Update();
                Add(m_ObjectIcon);
                Add(m_ObjectLabel);
            }

            public void Update()
            {
                var content = EditorGUIUtility.ObjectContent(m_ObjectField.value, m_ObjectField.objectType, showNullIcon: false);
                m_ObjectIcon.image = content.image;
                m_ObjectLabel.text = content.text;
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

        internal new static readonly string ussClassName = "unity-object-field";
        internal new static readonly string labelUssClassName = ussClassName + "__label";
        internal new static readonly string inputUssClassName = ussClassName + "__input";
        internal static readonly string objectUssClassName = ussClassName + "__object";
        internal static readonly string selectorUssClassName = ussClassName + "__selector";

        /// <summary>The Editor's single line height for this control (18 pixels).</summary>
        const float SingleLineHeight = 18f;

        Type m_ObjectType = typeof(UnityEngine.Object);
        readonly ObjectFieldDisplay m_ObjectFieldDisplay;

        [Obsolete("Use singleLineHeight instead. (UnityUpgradable) -> singleLineHeight", true)]
        public static float kSingleLineHeight => singleLineHeight;

        public static float singleLineHeight => SingleLineHeight;

        public Type objectType
        {
            get => m_ObjectType;
            set
            {
                if (m_ObjectType == value) return;
                m_ObjectType = value;
                m_ObjectFieldDisplay.Update();
            }
        }

        public SearchContext searchContext { get; set; }

        public SearchViewFlags searchViewFlags { get; set; }

        public override void SetValueWithoutNotify(UnityEngine.Object newValue)
        {
            newValue = UnityEditor.UIElements.ObjectField.ComponentOf(newValue, objectType);
            bool changed = !EqualityComparer<UnityEngine.Object>.Default.Equals(value, newValue);
            base.SetValueWithoutNotify(newValue);
            if (changed) m_ObjectFieldDisplay.Update();
        }

        public ObjectField() : this(null) { }

        public ObjectField(string label) : base(label, null)
        {
            visualInput.focusable = false;
            labelElement.focusable = false;
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            m_ObjectFieldDisplay = new ObjectFieldDisplay(this) { focusable = true };
            m_ObjectFieldDisplay.AddToClassList(objectUssClassName);
            var selector = new ObjectFieldSelector(this);
            selector.AddToClassList(selectorUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            visualInput.Add(m_ObjectFieldDisplay);
            visualInput.Add(selector);
            UnityEditor.UIElements.ObjectField.UpdateOnProjectChanges(this, m_ObjectFieldDisplay.Update);
            searchViewFlags = SearchViewFlags.ObjectPicker;
        }

        /// <summary>The search picker window: Unbound has none.</summary>
        [Unbound.Stub]
        internal void ShowObjectSelector() => Unbound.Runtime.Stubs.NotImplemented("UnityEditor.Search.ObjectField.ShowObjectSelector");
    }
}
