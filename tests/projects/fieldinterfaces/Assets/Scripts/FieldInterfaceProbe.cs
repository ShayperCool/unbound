using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// The interfaces of BaseField in a runtime panel: IMixedValueSupport (public) on each field kind,
// the internal IPrefixLabel answers, and the internal IEditableElement notifications (editingStarted
// and editingEnded) that each field kind raises on focus or on presses and releases, across
// detaching, re-attaching and replacing the handlers.
public class FieldInterfaceProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("FieldInterfaceProbe").AddComponent<FieldInterfaceProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    static readonly Type s_Editable = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.IEditableElement");
    static readonly Type s_Prefix = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.IPrefixLabel");
    readonly Dictionary<VisualElement, int[]> m_Counts = new Dictionary<VisualElement, int[]>();
    VisualElement root;

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("FIELD_INTERFACES_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        bool failed = false;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        if (Screen.dpi > 0) settings.scale = 96f / Screen.dpi;
        var document = new GameObject("Document").AddComponent<UIDocument>();
        document.panelSettings = settings;
        root = document.rootVisualElement;
        root.style.unityFontDefinition = FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        root.style.fontSize = 14;
        root.style.width = 400;
        yield return null;
        var steps = Record();
        while (true)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception ex) { Debug.LogException(ex); failed = true; break; }
            if (!more) break;
            yield return steps.Current;
        }
        if (!failed) Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static IEnumerator Settle() { yield return null; yield return null; }

    static PropertyInfo EditableProperty(string name) =>
        s_Editable.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    // Counts the field's editingStarted and editingEnded notifications.
    void Hook(VisualElement field)
    {
        var counts = new int[2];
        m_Counts[field] = counts;
        EditableProperty("editingStarted").SetValue(field, (Action)(() => counts[0]++));
        EditableProperty("editingEnded").SetValue(field, (Action)(() => counts[1]++));
    }

    string Counts(VisualElement field) => "started " + m_Counts[field][0] + " ended " + m_Counts[field][1];

    static void Pointer(VisualElement target, EventType type)
    {
        var center = target.worldBound.center;
        var imgui = new Event { type = type, mousePosition = center, button = 0, clickCount = 1 };
        using (EventBase e = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui) : (EventBase)PointerUpEvent.GetPooled(imgui))
        {
            e.target = target;
            target.SendEvent(e);
        }
    }

    static string Interfaces(Type type) => string.Join(",", type.GetInterfaces()
        .Where(i => i.Name == "IMixedValueSupport" || i.Name == "IPrefixLabel" || i.Name == "IEditableElement")
        .Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal));

    IEnumerator Record()
    {
        // Declarations.
        foreach (var type in new[] { typeof(TextField), typeof(Toggle), typeof(Slider), typeof(SliderInt), typeof(MinMaxSlider),
                     typeof(EnumField), typeof(RadioButtonGroup), typeof(DropdownField), typeof(Label), typeof(Foldout) })
            Put("interfaces." + type.Name, Interfaces(type));

        var text = new TextField("Name") { value = "abc" };
        var other = new TextField("Other") { value = "x" };
        var toggle = new Toggle("Flag");
        var slider = new Slider("Amount", 0f, 10f) { value = 2f };
        var range = new MinMaxSlider("Range", 2f, 8f, 0f, 10f);
        var plain = new TextField { value = "no label" };
        foreach (var e in new VisualElement[] { text, other, toggle, slider, range, plain }) root.Add(e);
        yield return Settle();

        // IMixedValueSupport through the interface.
        IMixedValueSupport mixed = text;
        mixed.showMixedValue = true;
        yield return Settle();
        Put("mixed.text", text.showMixedValue + " " + text.text + " " + mixed.showMixedValue);
        mixed.showMixedValue = false;
        yield return Settle();
        Put("mixed.text.off", text.showMixedValue + " " + text.text);
        IMixedValueSupport mixedToggle = toggle;
        mixedToggle.showMixedValue = true;
        Put("mixed.toggle", toggle.showMixedValue + " " + toggle.value);
        mixedToggle.showMixedValue = false;

        // IPrefixLabel.
        var label = s_Prefix.GetProperty("label");
        var labelElement = s_Prefix.GetProperty("labelElement");
        Put("prefix.text", (label.GetValue(text) ?? "<null>") + " " + ReferenceEquals(labelElement.GetValue(text), text.labelElement));
        text.label = "Renamed";
        Put("prefix.text.renamed", label.GetValue(text) + " " + ((Label)labelElement.GetValue(text)).text);
        Put("prefix.plain", (label.GetValue(plain) ?? "<null>") + " " + (labelElement.GetValue(plain) != null) + " " + ReferenceEquals(labelElement.GetValue(plain), plain.labelElement));

        // IEditableElement: handlers are null until set.
        Put("editable.initial", (EditableProperty("editingStarted").GetValue(text) == null) + " " + (EditableProperty("editingEnded").GetValue(text) == null));
        foreach (var e in new VisualElement[] { text, other, toggle, slider, range }) Hook(e);

        // A text field: focus starts, losing focus ends.
        text.Focus();
        yield return Settle();
        Put("text.focus", Counts(text));
        other.Focus();
        yield return Settle();
        Put("text.blur", Counts(text) + " other " + Counts(other));
        other.Blur();
        yield return Settle();
        Put("other.blur", Counts(other));

        // A toggle: focus does not start; a release on it starts, losing focus ends.
        toggle.Focus();
        yield return Settle();
        Put("toggle.focus", Counts(toggle));
        Pointer(toggle, EventType.MouseDown);
        Pointer(toggle, EventType.MouseUp);
        yield return Settle();
        Put("toggle.click", Counts(toggle) + " value " + toggle.value);
        toggle.Blur();
        yield return Settle();
        Put("toggle.blur", Counts(toggle));

        // A slider: presses on the label or the drag area start, a release on the drag area ends;
        // focus alone does neither.
        slider.Focus();
        yield return Settle();
        slider.Blur();
        yield return Settle();
        Put("slider.focus", Counts(slider));
        Pointer(slider.labelElement, EventType.MouseDown);
        Pointer(slider.labelElement, EventType.MouseUp);
        yield return Settle();
        Put("slider.label", Counts(slider));
        var drag = slider.Q(className: "unity-base-slider__drag-container");
        Pointer(drag, EventType.MouseDown);
        yield return Settle();
        Put("slider.dragDown", Counts(slider));
        Pointer(drag, EventType.MouseUp);
        yield return Settle();
        Put("slider.dragUp", Counts(slider) + " value " + slider.value.ToString("R", CultureInfo.InvariantCulture));

        // A min-max slider: a press on its input starts, a release ends.
        var rangeInput = range.Q(className: MinMaxSlider.inputUssClassName) ?? range;
        Pointer(rangeInput, EventType.MouseDown);
        yield return Settle();
        Put("range.down", Counts(range));
        Pointer(rangeInput, EventType.MouseUp);
        yield return Settle();
        Put("range.up", Counts(range));

        // Detached and attached again twice: one notification per focus, none while detached.
        text.RemoveFromHierarchy();
        yield return Settle();
        Put("detached", Counts(text) + " panel " + (text.panel != null));
        root.Add(text);
        yield return Settle();
        text.RemoveFromHierarchy();
        root.Add(text);
        yield return Settle();
        text.Focus();
        yield return Settle();
        Put("reattached.focus", Counts(text));
        other.Focus();
        yield return Settle();
        Put("reattached.blur", Counts(text));

        // Replaced handlers: only the new one is told.
        int replaced = 0;
        EditableProperty("editingStarted").SetValue(text, (Action)(() => replaced++));
        text.Focus();
        yield return Settle();
        Put("replaced", Counts(text) + " replaced " + replaced);
        EditableProperty("editingStarted").SetValue(text, null);
        other.Focus();
        yield return Settle();
        text.Focus();
        yield return Settle();
        Put("cleared", Counts(text) + " replaced " + replaced);

        // The focused field detached: whether the edit ends as it leaves the panel.
        var focusController = other.focusController;
        text.RemoveFromHierarchy();
        yield return Settle();
        var focused = focusController.focusedElement as VisualElement;
        Put("focusedDetach", Counts(text) + " focused " + (focused == null ? "<none>" : focused.GetType().Name));
    }
}
