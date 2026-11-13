using System;
using UnityEditor;
using UnityEngine;

public sealed class DefaultStateWindow : EditorWindow
{
    [NonSerialized] public object[] constructorValues;
    [NonSerialized] public object[] awakeValues;
    public DefaultStateWindow() { constructorValues = Values(false); }
    void Awake() { awakeValues = Values(true); }
    object[] Values(bool includeFlags)
    {
        return new object[] { minSize, position, depthBufferBits,
            typeof(EditorWindow).GetProperty("antiAlias").GetValue(this, null), titleContent.text,
            saveChangesMessage, includeFlags ? (object)hideFlags : "constructor" };
    }
    public void ChangeDefaults()
    {
        minSize = new Vector2(70, 90); position = new Rect(30, 40, 100, 120);
        depthBufferBits = 16; typeof(EditorWindow).GetProperty("antiAlias").SetValue(this, 4, null);
        titleContent = new GUIContent("changed", "hint"); saveChangesMessage = "changed-save";
    }
}
