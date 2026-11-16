namespace UnityEngine.UIElements
{
    /// <summary>A value field that can show that it edits several different values at once.</summary>
    public partial interface IMixedValueSupport
    {
        bool showMixedValue { get; set; }
    }

    /// <summary>A field with a label in front of its input.</summary>
    internal partial interface IPrefixLabel
    {
        string label { get; }
        Label labelElement { get; }
    }

    /// <summary>An element that tells when an edit of its value starts and ends (focus, or a press
    /// and release, depending on the field).</summary>
    internal partial interface IEditableElement
    {
        internal System.Action editingStarted { get; set; }
        internal System.Action editingEnded { get; set; }
    }
}
