using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Which RadioButtons form a group in a runtime panel: buttons under no GroupBox (one group per
// panel), under GroupBoxes (nested too) and under a RadioButtonGroup; buttons not in a panel; a
// button moved to another group; the order of the change events when the selection moves;
// SetSelected and SetValueWithoutNotify; and GroupBox's title label.
public class RadioGroupProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("RadioGroupProbe").AddComponent<RadioGroupProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    VisualElement root;
    readonly List<string> events = new List<string>();
    readonly Dictionary<string, RadioButton> buttons = new Dictionary<string, RadioButton>();

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("RADIO_GROUPS_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        bool failed = false;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        var document = new GameObject("Document").AddComponent<UIDocument>();
        document.panelSettings = settings;
        root = document.rootVisualElement;
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

    RadioButton Button(string name)
    {
        var b = new RadioButton(name) { name = name };
        b.RegisterValueChangedCallback(e => events.Add(name + ":" + e.newValue));
        buttons[name] = b;
        return b;
    }

    // The checked buttons, then the change events since the last call, in order.
    string State()
    {
        var on = string.Join(",", buttons.Where(p => p.Value.value).Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal));
        var result = "on [" + on + "] events [" + string.Join(" ", events) + "]";
        events.Clear();
        return result;
    }

    static IEnumerator Frames() { yield return null; yield return null; }

    static string Classes(VisualElement e) => string.Join(" ", e.GetClasses().OrderBy(c => c, StringComparer.Ordinal));

    IEnumerator Record()
    {
        // Under no GroupBox: one group for the panel, whatever the parents.
        var plain = new VisualElement();
        root.Add(Button("a"));
        root.Add(Button("b"));
        plain.Add(Button("c"));
        root.Add(plain);
        yield return Frames();
        buttons["a"].value = true;
        Put("panel.a", State());
        buttons["c"].value = true;
        Put("panel.c", State());
        buttons["c"].value = false;
        Put("panel.cOff", State());
        buttons["b"].SetValueWithoutNotify(true);
        Put("panel.bWithoutNotify", State());
        buttons["a"].value = true;
        Put("panel.aAgain", State());

        // GroupBoxes, one nested in another.
        var g1 = new GroupBox("First");
        var g2 = new GroupBox();
        var inner = new GroupBox("Inner");
        g1.Add(Button("g1a"));
        g1.Add(Button("g1b"));
        g2.Add(Button("g2a"));
        g2.Add(Button("g2b"));
        inner.Add(Button("in1"));
        inner.Add(Button("in2"));
        g1.Add(inner);
        root.Add(g1);
        root.Add(g2);
        yield return Frames();
        Put("groupbox.tree", "g1 " + Classes(g1) + " children " + string.Join(",", g1.Children().Select(c => c.GetType().Name + (c is Label l ? "(" + l.text + "," + Classes(l) + ")" : "")))
            + " | g2 children " + g2.childCount + " text " + (g2.text ?? "<null>"));
        buttons["g1a"].value = true;
        buttons["g2b"].value = true;
        buttons["in1"].value = true;
        Put("groupbox.select", State());
        buttons["g1b"].value = true;
        Put("groupbox.g1b", State());
        buttons["in2"].SetSelected(true);
        Put("groupbox.in2SetSelected", State());
        buttons["in2"].SetSelected(false);
        Put("groupbox.in2Deselected", State());
        // Moved into another group.
        g2.Add(buttons["g1b"]);
        yield return Frames();
        Put("groupbox.moved", State());
        buttons["g2a"].value = true;
        Put("groupbox.movedSelect", State());
        // Title changes.
        g2.text = "Second";
        g1.text = null;
        Put("groupbox.titles", "g1 children " + string.Join(",", g1.Children().Select(c => c.GetType().Name)) + " text " + (g1.text ?? "<null>")
            + " | g2 first " + g2[0].GetType().Name + " text " + g2.text);

        // Not in a panel.
        var detached = new VisualElement();
        detached.Add(Button("d1"));
        detached.Add(Button("d2"));
        buttons["d1"].value = true;
        buttons["d2"].value = true;
        Put("detached.both", State());
        root.Add(detached);
        yield return Frames();
        Put("detached.attached", State());
        buttons["d1"].value = false;
        buttons["d1"].value = true;
        Put("detached.reselect", State());
        detached.RemoveFromHierarchy();
        yield return Frames();
        buttons["d2"].value = true;
        Put("detached.removed", State());

        // Registration order: the later buttons inserted before the earlier ones.
        var order = new GroupBox();
        order.Add(Button("o1"));
        root.Add(order);
        yield return Frames();
        order.Insert(0, Button("o2"));
        order.Insert(0, Button("o3"));
        yield return Frames();
        buttons["o2"].value = true;
        buttons["o3"].value = true;
        buttons["o1"].value = true;
        Put("order.events", State());

        // RadioButtonGroup.
        var group = new RadioButtonGroup("Group", new List<string> { "x", "y", "z" });
        int groupChanges = 0;
        string groupLast = "";
        group.RegisterValueChangedCallback(e => { groupChanges++; groupLast = e.previousValue + ">" + e.newValue; });
        root.Add(group);
        yield return Frames();
        string Group() => "value " + group.value + " radios " + string.Join("", group.Query<RadioButton>().ToList().Select(r => r.value ? "1" : "0")) + " changes " + groupChanges + " " + groupLast;
        Put("rbg.initial", Group());
        group.value = 1;
        Put("rbg.value1", Group());
        group.Query<RadioButton>().ToList()[2].value = true;
        Put("rbg.radio2", Group());
        var extra = Button("extra");
        group.Add(extra);
        yield return Frames();
        Put("rbg.extraAdded", Group());
        extra.value = true;
        Put("rbg.extraChecked", Group() + " " + State());
        group.value = 0;
        Put("rbg.value0", Group() + " " + State());
        group.value = -1;
        Put("rbg.none", Group());

        // A checked radio button added to a group that has a value.
        var second = new RadioButtonGroup("Second", new List<string> { "p", "q" });
        root.Add(second);
        yield return Frames();
        second.value = 0;
        var preChecked = new RadioButton("pre") { value = true };
        second.Add(preChecked);
        Put("rbg2.added", "value " + second.value + " radios " + string.Join("", second.Query<RadioButton>().ToList().Select(r => r.value ? "1" : "0")));
        yield return Frames();
        Put("rbg2.settled", "value " + second.value + " radios " + string.Join("", second.Query<RadioButton>().ToList().Select(r => r.value ? "1" : "0")));

        // SetValueWithoutNotify on a group, and a group with a value leaving its panel and coming back.
        var third = new RadioButtonGroup("Third", new List<string> { "r", "s", "t" });
        var thirdEvents = new List<string>();
        third.RegisterValueChangedCallback(e => thirdEvents.Add("group:" + e.previousValue + ">" + e.newValue));
        root.Add(third);
        yield return Frames();
        var thirdRadios = third.Query<RadioButton>().ToList();
        for (int i = 0; i < thirdRadios.Count; i++)
        {
            int index = i;
            thirdRadios[i].RegisterValueChangedCallback(e => { if (e.target == thirdRadios[index]) thirdEvents.Add(index + ":" + e.newValue); });
        }
        string Third()
        {
            var result = "value " + third.value + " radios " + string.Join("", thirdRadios.Select(r => r.value ? "1" : "0")) + " events [" + string.Join(" ", thirdEvents) + "]";
            thirdEvents.Clear();
            return result;
        }
        third.value = 2;
        Put("rbg3.value2", Third());
        third.SetValueWithoutNotify(0);
        Put("rbg3.withoutNotify0", Third());
        third.RemoveFromHierarchy();
        yield return Frames();
        Put("rbg3.detached", Third());
        root.Add(third);
        yield return Frames();
        Put("rbg3.reattached", Third());
    }
}
