using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

/// <summary>The window whose root visual element and CreateGUI the probe records.</summary>
public sealed class EditorWindowGuiProbeWindow : EditorWindow
{
    public readonly List<string> Events = new List<string>();
    public bool AttachInCreateGui;
    public VisualElement CreatedChild;

    void OnEnable() => Events.Add("OnEnable");
    void OnFocus() => Events.Add("OnFocus");
    void OnLostFocus() => Events.Add("OnLostFocus");
    void OnDestroy() => Events.Add("OnDestroy");

    public void CreateGUI()
    {
        Events.Add("CreateGUI");
        if (AttachInCreateGui)
        {
            CreatedChild = new VisualElement { name = "from-create-gui" };
            rootVisualElement.Add(CreatedChild);
        }
    }
}
