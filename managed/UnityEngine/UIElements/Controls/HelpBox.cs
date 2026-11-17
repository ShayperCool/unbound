using Unity.Properties;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// A message with an icon for its kind (Unity 6000.0 Manual "HelpBox": "a help box with a
    /// message and an icon"). The label is the help box's first child; the icon, when the message
    /// type has one (Info, Warning, Error), is inserted before it with the type's USS class, and
    /// leaves the hierarchy for None. Matches the Unity 6000.0.71f1 Editor
    /// (tests/projects/uitkcontrols).
    /// </summary>
    public partial class HelpBox : VisualElement
    {
        internal static readonly BindingId textProperty = nameof(text);
        internal static readonly BindingId messageTypeProperty = nameof(messageType);

        public static readonly string ussClassName = "unity-help-box";
        public static readonly string labelUssClassName = ussClassName + "__label";
        public static readonly string iconUssClassName = ussClassName + "__icon";
        public static readonly string iconInfoUssClassName = iconUssClassName + "--info";
        public static readonly string iconwarningUssClassName = iconUssClassName + "--warning";
        public static readonly string iconErrorUssClassName = iconUssClassName + "--error";

        HelpBoxMessageType m_HelpBoxMessageType;
        readonly VisualElement m_Icon;
        string m_IconClass;
        readonly Label m_Label;

        [CreateProperty]
        public string text
        {
            get => m_Label.text;
            set
            {
                var previous = text;
                m_Label.text = value;
                if (string.CompareOrdinal(previous, text) != 0) NotifyPropertyChanged(textProperty);
            }
        }

        [CreateProperty]
        public HelpBoxMessageType messageType
        {
            get => m_HelpBoxMessageType;
            set
            {
                if (value == m_HelpBoxMessageType) return;
                m_HelpBoxMessageType = value;
                UpdateIcon(value);
                NotifyPropertyChanged(messageTypeProperty);
            }
        }

        public HelpBox() : this(string.Empty, HelpBoxMessageType.None) { }

        public HelpBox(string text, HelpBoxMessageType messageType)
        {
            AddToClassList(ussClassName);
            m_HelpBoxMessageType = messageType;
            m_Label = new Label(text);
            m_Label.AddToClassList(labelUssClassName);
            Add(m_Label);
            m_Icon = new VisualElement();
            m_Icon.AddToClassList(iconUssClassName);
            UpdateIcon(messageType);
        }

        static string GetIconClass(HelpBoxMessageType messageType) => messageType switch
        {
            HelpBoxMessageType.Info => iconInfoUssClassName,
            HelpBoxMessageType.Warning => iconwarningUssClassName,
            HelpBoxMessageType.Error => iconErrorUssClassName,
            _ => null,
        };

        void UpdateIcon(HelpBoxMessageType messageType)
        {
            if (!string.IsNullOrEmpty(m_IconClass)) m_Icon.RemoveFromClassList(m_IconClass);
            m_IconClass = GetIconClass(messageType);
            if (m_IconClass == null)
            {
                m_Icon.RemoveFromHierarchy();
                return;
            }
            m_Icon.AddToClassList(m_IconClass);
            if (m_Icon.parent == null) Insert(0, m_Icon);
        }
    }

    /// <summary>A text element with a title and a content container for its children (Unity 6000.0
    /// Scripting API PopupWindow; matches the Unity 6000.0.71f1 Editor, tests/projects/uitkcontrols).</summary>
    public partial class PopupWindow : TextElement
    {
        public new static readonly string ussClassName = "unity-popup-window";
        public static readonly string contentUssClassName = ussClassName + "__content-container";

        readonly VisualElement m_ContentContainer;

        public PopupWindow()
        {
            AddToClassList(ussClassName);
            m_ContentContainer = new VisualElement { name = "unity-content-container" };
            m_ContentContainer.AddToClassList(contentUssClassName);
            hierarchy.Add(m_ContentContainer);
        }

        public override VisualElement contentContainer => m_ContentContainer;
    }
}
