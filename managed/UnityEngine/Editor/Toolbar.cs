using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Properties;
using UnityEngine;
using UnityEngine.Internal;
using UnityEngine.UIElements;
using ConditionalAttribute = System.Diagnostics.ConditionalAttribute;

// The editor toolbar controls of UI Toolkit (UnityEditor.UIElements): Toolbar, ToolbarButton,
// ToolbarToggle, ToolbarMenu, ToolbarSpacer, SearchFieldBase, ToolbarSearchField,
// ToolbarPopupSearchField and ToolbarBreadcrumbs. Documented in the Unity 6000.0 Scripting API and
// the Manual's UI Toolkit control reference; their structure, USS classes, style sheets, focus,
// values, events and UXML attributes match the Unity 6000.0.71f1 Editor,
// tests/projects/toolbarcontrols.

namespace UnityEditor.UIElements
{
    public partial class Toolbar : VisualElement
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
            public override object CreateInstance() => new Toolbar();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<Toolbar>
        {
            public UxmlFactory() { }
        }

        public static readonly string ussClassName = "unity-toolbar";

        // The toolbar style sheet of the Editor's skin and UI font, from the Editor's resources:
        // "<sheet>_<font in lower case>.uss.asset".
        const string k_DarkSheetPath = "StyleSheets/Generated/ToolbarDark.uss.asset";
        const string k_LightSheetPath = "StyleSheets/Generated/ToolbarLight.uss.asset";
        static readonly Dictionary<string, StyleSheet> s_Sheets = new Dictionary<string, StyleSheet>();

        static string ForCurrentFont(string sheetPath) =>
            sheetPath.Replace(".uss", "_" + UnityEditor.Experimental.EditorResources.currentFontName.ToLowerInvariant() + ".uss");

        static readonly HashSet<string> s_Missing = new HashSet<string>();

        // The sheet is a default sheet, as the Editor marks it: any other sheet's rule of the same
        // property wins over it, whatever their specificity.
        static StyleSheet Sheet(string path)
        {
            if (s_Sheets.TryGetValue(path, out var sheet)) return sheet;
            sheet = Unbound.UIElements.UiResources.EditorLoad(path) as StyleSheet;
            if (sheet == null)
            {
                if (s_Missing.Add(path))
                    Debug.LogError($"Unbound: the Editor's toolbar style sheet '{path}' is not available (it comes from the Unity Editor's resources).");
                return null;
            }
            sheet.isDefaultStyleSheet = true;
            s_Sheets[path] = sheet;
            return sheet;
        }

        internal static void SetToolbarStyleSheet(VisualElement ve)
        {
            var sheet = Sheet(ForCurrentFont(EditorGUIUtility.isProSkin ? k_DarkSheetPath : k_LightSheetPath));
            if (sheet != null) ve.styleSheets.Add(sheet);
        }

        public Toolbar()
        {
            AddToClassList(ussClassName);
            SetToolbarStyleSheet(this);
        }
    }

    public partial class ToolbarButton : Button
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : Button.UxmlSerializedData
        {
            public override object CreateInstance() => new ToolbarButton();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToolbarButton, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : Button.UxmlTraits
        {
            public UxmlTraits() { }
        }

        public new static readonly string ussClassName = "unity-toolbar-button";

        public ToolbarButton(Action clickEvent) : base(clickEvent)
        {
            Toolbar.SetToolbarStyleSheet(this);
            RemoveFromClassList(Button.ussClassName);
            AddToClassList(ussClassName);
        }

        public ToolbarButton() : this(() => { }) { }
    }

    public partial class ToolbarToggle : Toggle
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : Toggle.UxmlSerializedData
        {
            public override object CreateInstance() => new ToolbarToggle();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToolbarToggle, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : Toggle.UxmlTraits
        {
            public UxmlTraits() => focusable.defaultValue = false;
        }

        public new static readonly string ussClassName = "unity-toolbar-toggle";

        public ToolbarToggle()
        {
            focusable = false;
            Toolbar.SetToolbarStyleSheet(this);
            RemoveFromClassList(Toggle.ussClassName);
            AddToClassList(ussClassName);
        }
    }

    public partial class ToolbarSpacer : VisualElement
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
            public override object CreateInstance() => new ToolbarSpacer();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToolbarSpacer>
        {
            public UxmlFactory() { }
        }

        internal static readonly BindingId flexProperty = "flex";
        public static readonly string ussClassName = "unity-toolbar-spacer";
        [Obsolete("The `fixedSpacerVariantUssClassName` style has been deprecated as is it now the default style.")]
        public static readonly string fixedSpacerVariantUssClassName = ussClassName + "--fixed";
        public static readonly string flexibleSpacerVariantUssClassName = ussClassName + "--flexible";

        [CreateProperty]
        public bool flex
        {
            get => ClassListContains(flexibleSpacerVariantUssClassName);
            set
            {
                if (flex == value) return;
                EnableInClassList(flexibleSpacerVariantUssClassName, value);
                NotifyPropertyChanged(in flexProperty);
            }
        }

        public ToolbarSpacer()
        {
            Toolbar.SetToolbarStyleSheet(this);
            AddToClassList(ussClassName);
        }
    }

    public partial interface IToolbarMenuElement
    {
        DropdownMenu menu { get; }
    }

    public static partial class ToolbarMenuElementExtensions
    {
        /// <summary>Opens the element's menu below it, when it has items. The Editor shows it as
        /// one of its own menus (DropdownMenu.DoDisplayEditorMenu), which Unbound has not: that is
        /// reported instead.</summary>
        public static void ShowMenu(this IToolbarMenuElement tbe)
        {
            if (tbe == null || !tbe.menu.MenuItems().Any() || !(tbe is VisualElement)) return;
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.ToolbarMenuElementExtensions.ShowMenu (the Editor's menu)");
        }
    }

    public partial class ToolbarMenu : TextElement, IToolbarMenuElement
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : TextElement.UxmlSerializedData
        {
            public override object CreateInstance() => new ToolbarMenu();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToolbarMenu, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : TextElement.UxmlTraits
        {
            public UxmlTraits() { }
        }

        public enum Variant
        {
            Default = 0,
            Popup = 1,
        }

        internal static readonly BindingId menuProperty = "menu";
        internal static readonly BindingId variantProperty = "variant";
        public new static readonly string ussClassName = "unity-toolbar-menu";
        public static readonly string popupVariantUssClassName = ussClassName + "--popup";
        public static readonly string textUssClassName = ussClassName + "__text";
        public static readonly string arrowUssClassName = ussClassName + "__arrow";

        readonly TextElement m_TextElement;
        readonly VisualElement m_ArrowElement;
        Variant m_Variant;

        [CreateProperty(ReadOnly = true)]
        public DropdownMenu menu { get; }

        /// <summary>The text, shown by the menu's text child.</summary>
        public override string text
        {
            get => base.text;
            set
            {
                m_TextElement.text = value;
                base.text = value;
            }
        }

        [CreateProperty]
        public Variant variant
        {
            get => m_Variant;
            set
            {
                var previous = m_Variant;
                m_Variant = value;
                EnableInClassList(popupVariantUssClassName, value == Variant.Popup);
                if (previous != m_Variant) NotifyPropertyChanged(in variantProperty);
            }
        }

        public ToolbarMenu()
        {
            Toolbar.SetToolbarStyleSheet(this);
            // The text shows through the text child, not the element itself.
            generateVisualContent = null;
            this.AddManipulator(new Clickable(this.ShowMenu));
            menu = new DropdownMenu();
            AddToClassList(ussClassName);
            m_TextElement = new TextElement { pickingMode = PickingMode.Ignore };
            m_TextElement.AddToClassList(textUssClassName);
            Add(m_TextElement);
            m_ArrowElement = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ArrowElement.AddToClassList(arrowUssClassName);
            Add(m_ArrowElement);
        }
    }

    public abstract partial class SearchFieldBase<TextInputType, T> : VisualElement, INotifyValueChanged<T>
        where TextInputType : TextInputBaseField<T>, new()
    {
        [Serializable]
        [ExcludeFromDocs]
        public new abstract partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField] string placeholderText;
            [SerializeField, UxmlIgnore, HideInInspector] UxmlAttributeFlags placeholderText_UxmlAttributeFlags;
#pragma warning restore 649

            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(placeholderText), "placeholder-text", null),
                });
            }

            [ExcludeFromDocs]
            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                if (ShouldWriteAttributeValue(placeholderText_UxmlAttributeFlags))
                    ((SearchFieldBase<TextInputType, T>)obj).placeholderText = placeholderText;
            }

            protected UxmlSerializedData() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : VisualElement.UxmlTraits
        {
            public UxmlTraits() => focusable.defaultValue = true;
        }

        internal static readonly BindingId valueProperty = "value";
        internal static readonly BindingId placeholderTextProperty = "placeholderText";

        readonly Button m_SearchButton;
        readonly Button m_CancelButton;
        readonly TextInputType m_TextField;

        public static readonly string ussClassName = "unity-search-field-base";
        public static readonly string textUssClassName = ussClassName + "__text-field";
        public static readonly string textInputUssClassName = textUssClassName + "__input";
        public static readonly string searchButtonUssClassName = ussClassName + "__search-button";
        public static readonly string cancelButtonUssClassName = ussClassName + "__cancel-button";
        public static readonly string cancelButtonOffVariantUssClassName = cancelButtonUssClassName + "--off";
        public static readonly string popupVariantUssClassName = ussClassName + "--popup";

        protected internal TextInputType textInputField => m_TextField;

        protected Button searchButton => m_SearchButton;

        [CreateProperty]
        public T value
        {
            get => m_TextField.value;
            set
            {
                var previous = m_TextField.value;
                m_TextField.value = value;
                if (!EqualityComparer<T>.Default.Equals(m_TextField.value, previous)) NotifyPropertyChanged(in valueProperty);
            }
        }

        [CreateProperty]
        public string placeholderText
        {
            get => m_TextField.textEdition.placeholder;
            set
            {
                if (value == m_TextField.textEdition.placeholder) return;
                m_TextField.textEdition.placeholder = value;
                NotifyPropertyChanged(in placeholderTextProperty);
            }
        }

        /// <summary>A search button, the text field and a cancel button, which clears the field and
        /// is marked off while the field is empty; Escape in the field clears it too.</summary>
        protected SearchFieldBase()
        {
            isCompositeRoot = true;
            focusable = true;
            tabIndex = 0;
            excludeFromFocusRing = true;
            delegatesFocus = true;
            AddToClassList(ussClassName);

            m_SearchButton = new Button(() => { }) { name = "unity-search" };
            m_SearchButton.AddToClassList(searchButtonUssClassName);
            m_SearchButton.focusable = false;
            hierarchy.Add(m_SearchButton);

            m_TextField = new TextInputType();
            m_TextField.AddToClassList(textUssClassName);
            hierarchy.Add(m_TextField);
            m_TextField.RegisterValueChangedCallback(OnValueChanged);
            var input = m_TextField.Q(TextInputBaseField<string>.textInputUssName);
            input.RegisterCallback<KeyDownEvent>(OnTextFieldKeyDown, TrickleDown.TrickleDown);
            input.AddToClassList(textInputUssClassName);

            m_CancelButton = new Button(() => { }) { name = "unity-cancel" };
            m_CancelButton.AddToClassList(cancelButtonUssClassName);
            m_CancelButton.AddToClassList(cancelButtonOffVariantUssClassName);
            hierarchy.Add(m_CancelButton);

            RegisterCallback<AttachToPanelEvent>(_ => UpdateCancelButton());
            m_CancelButton.clickable.clicked += ClearTextField;
        }

        void OnValueChanged(ChangeEvent<T> change) => UpdateCancelButton();

        protected abstract void ClearTextField();

        void OnTextFieldKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape) ClearTextField();
        }

        public virtual void SetValueWithoutNotify(T newValue)
        {
            m_TextField.SetValueWithoutNotify(newValue);
            UpdateCancelButton();
        }

        protected abstract bool FieldIsEmpty(T fieldValue);

        void UpdateCancelButton() => m_CancelButton.EnableInClassList(cancelButtonOffVariantUssClassName, FieldIsEmpty(m_TextField.value));
    }

    public partial class ToolbarSearchField : SearchFieldBase<TextField, string>
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : SearchFieldBase<TextField, string>.UxmlSerializedData
        {
            [Conditional("UNITY_EDITOR")]
            public new static void Register() => SearchFieldBase<TextField, string>.UxmlSerializedData.Register();
            public override object CreateInstance() => new ToolbarSearchField();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToolbarSearchField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : SearchFieldBase<TextField, string>.UxmlTraits
        {
            public UxmlTraits() { }
        }

        public new static readonly string textUssClassName = SearchFieldBase<TextField, string>.textUssClassName;
        public new static readonly string searchButtonUssClassName = SearchFieldBase<TextField, string>.searchButtonUssClassName;
        public new static readonly string cancelButtonUssClassName = SearchFieldBase<TextField, string>.cancelButtonUssClassName;
        public new static readonly string cancelButtonOffVariantUssClassName = SearchFieldBase<TextField, string>.cancelButtonOffVariantUssClassName;
        public new static readonly string popupVariantUssClassName = SearchFieldBase<TextField, string>.popupVariantUssClassName;
        public new static readonly string ussClassName = "unity-toolbar-search-field";

        protected new Button searchButton => base.searchButton;

        public ToolbarSearchField() => AddToClassList(ussClassName);

        protected override void ClearTextField() => value = string.Empty;

        protected override bool FieldIsEmpty(string fieldValue) => string.IsNullOrEmpty(fieldValue);
    }

    public partial class ToolbarPopupSearchField : ToolbarSearchField, IToolbarMenuElement
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : ToolbarSearchField.UxmlSerializedData
        {
            public override object CreateInstance() => new ToolbarPopupSearchField();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToolbarPopupSearchField, UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlTraits : ToolbarSearchField.UxmlTraits
        {
            public UxmlTraits() { }
        }

        internal static readonly BindingId menuProperty = "menu";

        [CreateProperty(ReadOnly = true)]
        public DropdownMenu menu { get; }

        public ToolbarPopupSearchField()
        {
            AddToClassList(popupVariantUssClassName);
            menu = new DropdownMenu();
            searchButton.clickable.clicked += this.ShowMenu;
        }
    }

    public partial class ToolbarBreadcrumbs : VisualElement
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
            public override object CreateInstance() => new ToolbarBreadcrumbs();
            public UxmlSerializedData() { }
        }

        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.", false)]
        public new partial class UxmlFactory : UxmlFactory<ToolbarBreadcrumbs>
        {
            public UxmlFactory() { }
        }

        // An item: a toolbar button with the breadcrumbs' item class instead of the button's.
        sealed class BreadcrumbItem : ToolbarButton
        {
            public BreadcrumbItem(Action clickEvent) : base(clickEvent)
            {
                Toolbar.SetToolbarStyleSheet(this);
                RemoveFromClassList(ToolbarButton.ussClassName);
                AddToClassList(itemClassName);
            }
        }

        public static readonly string ussClassName = "unity-toolbar-breadcrumbs";
        public static readonly string itemClassName = ussClassName + "__item";
        public static readonly string firstItemClassName = ussClassName + "__first-item";

        public ToolbarBreadcrumbs()
        {
            Toolbar.SetToolbarStyleSheet(this);
            AddToClassList(ussClassName);
        }

        public void PushItem(string label, Action clickedEvent = null)
        {
            var item = new BreadcrumbItem(clickedEvent) { text = label };
            item.EnableInClassList(firstItemClassName, childCount == 0);
            Add(item);
        }

        public void PopItem()
        {
            if (childCount > 0 && this[childCount - 1] is BreadcrumbItem)
            {
                RemoveAt(childCount - 1);
                return;
            }
            throw new InvalidOperationException("Last child isn't a BreadcrumbItem");
        }
    }
}
