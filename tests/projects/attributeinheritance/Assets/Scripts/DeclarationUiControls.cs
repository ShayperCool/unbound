using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

public static class DeclarationUiControls
{
    [Serializable] public class PlainData { public string name = "before"; public int index = 3; }
    sealed class InterestedElement : VisualElement
    {
        public int geometryEvents;
        [EventInterest(typeof(GeometryChangedEvent))]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (evt is GeometryChangedEvent) geometryEvents++;
        }
    }
    static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    static Type TypeOf(string name) { return typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements." + name, true); }
    static void Setter(Action<string, object> row, Type type, string name)
    {
        var property = type.GetProperty(name, All);
        row("ui.shape." + type.FullName + "." + name + ".setter", property.GetSetMethod(true) == null ? "absent" : property.GetSetMethod(true).IsPrivate ? "private" : "other");
    }
    public static void Record(Action<string, object> row)
    {
        var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        var owner = new GameObject("DeclarationEventInterest");
        try
        {
            var document = owner.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var target = new InterestedElement();
            document.rootVisualElement.Add(target);
            using (var evt = GeometryChangedEvent.GetPooled(new Rect(0, 0, 1, 1), new Rect(0, 0, 7, 9)))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
            row("ui.attribute.interest.specified-event-delivered", target.geometryEvents);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(panelSettings);
        }
        var eventTypes = new[] { typeof(KeyDownEvent), typeof(PointerDownEvent) };
        var interest = new EventInterestAttribute(eventTypes);
        row("ui.attribute.interest.array-identity", ReferenceEquals(eventTypes, typeof(EventInterestAttribute).GetField("eventTypes", All).GetValue(interest)));
        row("ui.attribute.interest.array-flags", Convert.ToInt32(typeof(EventInterestAttribute).GetField("categoryFlags", All).GetValue(interest)));
        interest = new EventInterestAttribute((Type[])null);
        row("ui.attribute.interest.null", typeof(EventInterestAttribute).GetField("eventTypes", All).GetValue(interest) == null);
        foreach (var option in new[] { EventInterestOptions.Inherit, EventInterestOptions.AllEventTypes, (EventInterestOptions)17 })
        {
            interest = new EventInterestAttribute(option);
            row("ui.attribute.interest.flags." + (int)option, Convert.ToInt32(typeof(EventInterestAttribute).GetField("categoryFlags", All).GetValue(interest)));
            row("ui.attribute.interest.types." + (int)option, typeof(EventInterestAttribute).GetField("eventTypes", All).GetValue(interest) == null);
        }
        foreach (var type in new[] { typeof(EventInterestAttribute), typeof(UxmlObjectAttribute) })
        {
            var usage = (AttributeUsageAttribute)Attribute.GetCustomAttribute(type, typeof(AttributeUsageAttribute), true);
            row("ui.attribute." + type.Name + ".targets", (int)usage.ValidOn);
            row("ui.attribute." + type.Name + ".multiple", usage.AllowMultiple);
            row("ui.attribute." + type.Name + ".inherited", usage.Inherited);
        }
        var sort = new SortColumnDescription("before", SortDirection.Ascending);
        row("ui.serialize.sort.before-name", sort.columnName);
        JsonUtility.FromJsonOverwrite("{\"m_ColumnIndex\":17,\"m_ColumnName\":\"after\",\"m_SortDirection\":1}", sort);
        row("ui.serialize.sort.after-name", sort.columnName);
        row("ui.serialize.sort.after-index", sort.columnIndex);
        row("ui.serialize.sort.after-direction", (int)sort.direction);
        var control = new PlainData();
        JsonUtility.FromJsonOverwrite("{\"name\":\"after\",\"index\":17}", control);
        row("ui.serialize.control.name", control.name);
        row("ui.serialize.control.index", control.index);
        JsonUtility.FromJsonOverwrite("{\"index\":31}", control);
        row("ui.serialize.control.omitted-name", control.name);
        row("ui.serialize.control.changed-index", control.index);
        row("ui.serialize.angle.json", JsonUtility.ToJson(new Angle(33, AngleUnit.Degree)));
        Angle angle = JsonUtility.FromJson<Angle>("{\"m_Value\":87,\"m_Unit\":1}");
        row("ui.serialize.angle.value", angle.value);
        row("ui.serialize.angle.unit", (int)angle.unit);
        foreach (var type in new[] { typeof(SortColumnDescription), typeof(ThemeStyleSheet), typeof(VisualTreeAsset) })
            row("ui.shape." + type.FullName + ".serializable", type.IsSerializable);
        foreach (string name in new[] { "target", "visualTreeAsset", "slotInsertionPoints" }) Setter(row, typeof(CreationContext), name);
        foreach (string name in new[] { "dragElement", "dragMinThumb", "dragMaxThumb", "clampedDragger" }) Setter(row, typeof(MinMaxSlider), name);
        Setter(row, typeof(MeshGenerationContext), "visualElement");
        Setter(row, typeof(UIDocument), "parentUI");
        Setter(row, typeof(VisualElement), "hierarchy");
        row("ui.shape.BaseTreeView.indent-readonly", typeof(BaseTreeView).GetField("s_TreeViewIndentProperty", All).IsInitOnly);
        foreach (string name in new[] { "DefaultEventSystem", "RuntimePanel" }) row("ui.shape." + name + ".sealed", TypeOf(name).IsSealed);
        row("ui.shape.BaseVisualElementPanel.UpdateAnimations.abstract", TypeOf("BaseVisualElementPanel").GetMethod("UpdateAnimations", All).IsAbstract);
        foreach (string name in new[] { "visualTree", "dispatcher", "focusController" })
        {
            var property = TypeOf("Panel").GetProperty(name, All);
            row("ui.shape.Panel." + name + ".getter-final", property.GetGetMethod(true).IsFinal);
            if (property.GetSetMethod(true) != null) row("ui.shape.Panel." + name + ".setter-final", property.GetSetMethod(true).IsFinal);
        }
        var constructor = TypeOf("BaseRuntimePanel").GetConstructors(All).Single(c => c.GetParameters().Length == 2);
        var parameter = constructor.GetParameters()[1];
        row("ui.shape.BaseRuntimePanel.dispatcher.optional", parameter.IsOptional);
        row("ui.shape.BaseRuntimePanel.dispatcher.default-null", parameter.HasDefaultValue && parameter.DefaultValue == null);
        foreach (var pair in new[] {
            new[] { "Angle", "m_Value" }, new[] { "Angle", "m_Unit" },
            new[] { "BaseField`1", "m_Value" }, new[] { "BaseSlider`1", "m_LowValue" }, new[] { "BaseSlider`1", "m_HighValue" },
            new[] { "BaseVerticalCollectionView", "m_SelectedIds" }, new[] { "BaseVerticalCollectionView", "m_ShowAlternatingRowBackgrounds" },
            new[] { "Foldout", "m_Value" }, new[] { "PanelSettings", "m_Match" }, new[] { "ScrollView", "m_ScrollOffset" },
            new[] { "SortColumnDescription", "m_ColumnIndex" }, new[] { "SortColumnDescription", "m_ColumnName" }, new[] { "SortColumnDescription", "m_SortDirection" },
            new[] { "SortColumnDescriptions", "m_Descriptions" }, new[] { "VisualTreeAsset", "m_ContentHash" },
            new[] { "VisualTreeAsset", "m_ImportedWithErrors" }, new[] { "VisualTreeAsset", "m_ImportedWithWarnings" } })
        {
            var field = TypeOf(pair[0]).GetField(pair[1], All);
            row("ui.field." + pair[0] + "." + pair[1], string.Join(",", field.CustomAttributes.Select(a => a.AttributeType.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray()));
        }
    }
}
