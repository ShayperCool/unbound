using System;

// The UXML serialized data classes of Button, BaseBoolField and Toggle, which the editor toolbar
// controls' data derive from (UnityEditor.UIElements ToolbarButton, ToolbarToggle), and of Label,
// TabView and TwoPaneSplitView, which packages' controls derive from. Creating an instance needs
// no other member; their attributes and Deserialize are not implemented yet.

namespace UnityEngine.UIElements
{
    public partial class Button
    {
        [Serializable]
        public new partial class UxmlSerializedData : TextElement.UxmlSerializedData
        {
            public override object CreateInstance() => new Button();
            public UxmlSerializedData() { }
        }
    }

    public abstract partial class BaseBoolField
    {
        [Serializable]
        public new abstract partial class UxmlSerializedData : BaseField<bool>.UxmlSerializedData
        {
            protected UxmlSerializedData() { }
        }
    }

    public partial class Toggle
    {
        [Serializable]
        public new partial class UxmlSerializedData : BaseBoolField.UxmlSerializedData
        {
            public override object CreateInstance() => new Toggle();
            public UxmlSerializedData() { }
        }
    }

    public partial class Label
    {
        [Serializable]
        public new partial class UxmlSerializedData : TextElement.UxmlSerializedData
        {
            public override object CreateInstance() => new Label();
            public UxmlSerializedData() { }
        }
    }

    public partial class TabView
    {
        [Serializable]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
            public override object CreateInstance() => new TabView();
            public UxmlSerializedData() { }
        }
    }

    public partial class TwoPaneSplitView
    {
        [Serializable]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
            public override object CreateInstance() => new TwoPaneSplitView();
            public UxmlSerializedData() { }
        }
    }
}
