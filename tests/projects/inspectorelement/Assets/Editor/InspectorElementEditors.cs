using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Counts the editors' lifetimes: how many were enabled and disabled.
public static class EditorCounts
{
    public static int enabled;
    public static int disabled;
    public static int guis;
}

[CustomEditor(typeof(InspectorUieTarget))]
public class InspectorUieEditor : Editor
{
    void OnEnable() => EditorCounts.enabled++;
    void OnDisable() => EditorCounts.disabled++;

    public override VisualElement CreateInspectorGUI()
    {
        EditorCounts.guis++;
        var root = new VisualElement { name = "custom-root" };
        root.Add(new Label("custom:" + serializedObject.targetObject.GetType().Name) { name = "custom-label" });
        root.Add(new UnityEditor.UIElements.PropertyField(serializedObject.FindProperty("a")));
        return root;
    }
}

[CustomEditor(typeof(InspectorImguiTarget))]
public class InspectorImguiEditor : Editor
{
    public override void OnInspectorGUI() => EditorGUILayout.LabelField("imgui");
}

// Builds no UI Toolkit GUI: the inspector falls back to its IMGUI.
[CustomEditor(typeof(InspectorNullGuiTarget))]
public class InspectorNullGuiEditor : Editor
{
    public override VisualElement CreateInspectorGUI() => null;
}

[CustomEditor(typeof(InspectorNoMarginsTarget))]
public class InspectorNoMarginsEditor : Editor
{
    public override bool UseDefaultMargins() => false;
    public override VisualElement CreateInspectorGUI() => new Label("no margins") { name = "no-margins" };
}
