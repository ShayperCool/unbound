using UnityEditor;
using UnityEngine;

// A drawer with only IMGUI: its height, and what it is drawn with.
[CustomPropertyDrawer(typeof(ImguiDrawn))]
public class ImguiDrawnDrawer : PropertyDrawer
{
    public static System.Collections.Generic.List<string> calls = new System.Collections.Generic.List<string>();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        calls.Add("height");
        return 37f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        calls.Add($"gui:{Event.current.type}:{position.x},{position.y},{position.width},{position.height}:{label.text}");
        EditorGUI.LabelField(position, label, new GUIContent("imgui"));
    }
}

// Drawers that measure and draw their own property again: the type's default GUI, and the
// field's default GUI under an attribute's drawer.
[CustomPropertyDrawer(typeof(ImguiNested))]
public class ImguiNestedDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float own = EditorGUI.GetPropertyHeight(property, label, true);
        ImguiDrawnDrawer.calls.Add($"nested.height:{own}");
        return own + 10f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        ImguiDrawnDrawer.calls.Add($"nested.gui:{Event.current.type}:{position.x},{position.y},{position.width},{position.height}:{label.text}");
        EditorGUI.PropertyField(position, property, label, true);
    }
}

[CustomPropertyDrawer(typeof(ImguiTaggedAttribute))]
public class ImguiTaggedDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float own = EditorGUI.GetPropertyHeight(property);
        ImguiDrawnDrawer.calls.Add($"tagged.height:{own}");
        return own + 3f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        ImguiDrawnDrawer.calls.Add($"tagged.gui:{Event.current.type}:{position.x},{position.y},{position.width},{position.height}:{label.text}");
        EditorGUILayout.PropertyField(property);
        EditorGUI.PropertyField(position, property, label);
    }
}
