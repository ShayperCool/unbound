using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

static class DrawerText
{
    public static Label Describe(string who, PropertyDrawer drawer, SerializedProperty property) =>
        new Label($"{who}:{property.propertyPath}:{drawer.preferredLabel}:{drawer.fieldInfo?.Name}:{drawer.attribute?.GetType().Name}") { name = who };
}

[CustomPropertyDrawer(typeof(CustomShape))]
public class CustomShapeDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property) => DrawerText.Describe("shape", this, property);
}

[CustomPropertyDrawer(typeof(BaseNo), false)]
public class BaseNoDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property) => DrawerText.Describe("baseNo", this, property);
}

[CustomPropertyDrawer(typeof(BaseYes), true)]
public class BaseYesDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property) => DrawerText.Describe("baseYes", this, property);
}

[CustomPropertyDrawer(typeof(IMarker), true)]
public class MarkerDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property) => DrawerText.Describe("marker", this, property);
}

[CustomPropertyDrawer(typeof(Wrapper<>))]
public class WrapperDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property) => DrawerText.Describe("wrapper", this, property);
}

[CustomPropertyDrawer(typeof(TaggedAttribute))]
public class TaggedDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var label = DrawerText.Describe("tagged", this, property);
        label.text += ":" + ((TaggedAttribute)attribute).tag;
        return label;
    }
}

// Each draws its name and, below it, the property again: the next drawer, or the default field.
[CustomPropertyDrawer(typeof(OuterAttribute))]
public class OuterDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var box = new VisualElement { name = "outer" };
        box.Add(DrawerText.Describe("outer", this, property));
        box.Add(new PropertyField(property));
        return box;
    }
}

[CustomPropertyDrawer(typeof(InnerAttribute))]
public class InnerDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var box = new VisualElement { name = "inner" };
        box.Add(DrawerText.Describe("inner", this, property));
        box.Add(new PropertyField(property));
        return box;
    }
}

[CustomPropertyDrawer(typeof(ImguiOnly))]
public class ImguiOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) => EditorGUI.LabelField(position, "imgui");
}

[CustomPropertyDrawer(typeof(WholeListAttribute))]
public class WholeListDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property) => DrawerText.Describe("wholeList", this, property);
}

[CustomPropertyDrawer(typeof(BannerAttribute))]
public class BannerDrawer : DecoratorDrawer
{
    public override VisualElement CreatePropertyGUI() => new Label("banner:" + ((BannerAttribute)attribute).text) { name = "banner" };
}

[CustomPropertyDrawer(typeof(ImguiBannerAttribute))]
public class ImguiBannerDrawer : DecoratorDrawer
{
    public override float GetHeight() => 21f;
    public override void OnGUI(Rect position) => EditorGUI.LabelField(position, "imgui banner");
}

[CustomPropertyDrawer(typeof(Counted))]
public class CountedDrawer : PropertyDrawer
{
    public static int created;
    public static int guis;
    public CountedDrawer() => created++;
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        guis++;
        return new Label("counted") { name = "counted" };
    }
}

// Builds the children's own PropertyFields.
[CustomPropertyDrawer(typeof(Composite))]
public class CompositeDrawer : PropertyDrawer
{
    public override VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        var box = new VisualElement { name = "composite" };
        box.Add(new PropertyField(property.FindPropertyRelative("first")));
        box.Add(new PropertyField(property.FindPropertyRelative("second"), "Second!"));
        return box;
    }
}
