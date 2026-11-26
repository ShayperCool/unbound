using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public sealed class ReorderableInputData : ScriptableObject
{
    public int[] values = { 10, 20, 30, 40 };
}

/// <summary>
/// Records, in a game's OnGUI in Play mode, how a ReorderableList answers mouse and keyboard
/// events: one scripted event per frame in place of the frame's Repaint, given to lists drawn with
/// DoList at fixed rects. After each event: the callbacks called (in order, with the list's index
/// and count at the call), the elements, index, selection, hot and keyboard control.
/// Lists: an IList of strings with every callback, a plain IList of strings (default add and
/// remove), one whose add and remove are refused, a multi-select one, a non-draggable one, and a
/// serialized int array.
/// </summary>
public sealed class ReorderableInputProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    int m_Step = -1;
    int m_Frames;
    bool m_Done;
    readonly List<string> m_Calls = new List<string>();

    ReorderableList m_Full, m_Plain, m_Refusing, m_Multi, m_Fixed, m_Serialized;
    List<string> m_FullItems, m_PlainItems, m_RefusingItems, m_MultiItems, m_FixedItems;
    SerializedObject m_Object;
    bool m_UseAddCallback = true, m_UseRemoveCallback = true, m_UseDropdown;

    // Each list's rect: 300 wide, side by side vertically far apart (rows are 23 high).
    static readonly Rect FullRect = new Rect(10, 10, 300, 200);
    static readonly Rect PlainRect = new Rect(10, 250, 300, 200);
    static readonly Rect RefusingRect = new Rect(10, 490, 300, 200);
    static readonly Rect MultiRect = new Rect(10, 730, 300, 200);
    static readonly Rect FixedRect = new Rect(10, 970, 300, 200);
    static readonly Rect SerializedRect = new Rect(10, 1210, 300, 200);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("REORDERABLE_INPUT_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("ReorderableInputProbe").AddComponent<ReorderableInputProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.Setup();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    void Log(string what, ReorderableList list) => m_Calls.Add(what + " " + list.index + "/" + list.count);

    void Setup()
    {
        m_FullItems = new List<string> { "a", "b", "c", "d" };
        m_Full = new ReorderableList(m_FullItems, typeof(string));
        m_Full.onSelectCallback = l => Log("select", l);
        m_Full.onMouseUpCallback = l => Log("mouseUp", l);
        m_Full.onMouseDragCallback = l => Log("mouseDrag", l);
        m_Full.onReorderCallbackWithDetails = (l, from, to) => m_Calls.Add("reorderDetails " + from + ">" + to + " " + l.index);
        m_Full.onReorderCallback = l => Log("reorder", l);
        m_Full.onChangedCallback = l => Log("changed", l);
        m_Full.onCanAddCallback = l => { Log("canAdd", l); return true; };
        m_Full.onCanRemoveCallback = l => { Log("canRemove", l); return true; };
        m_Full.onAddCallback = l => { Log("add", l); m_FullItems.Add("n" + m_FullItems.Count); };
        m_Full.onRemoveCallback = l => { Log("remove", l); m_FullItems.RemoveAt(l.index); };
        m_Full.onAddDropdownCallback = null;

        m_PlainItems = new List<string> { "a", "b", "c" };
        m_Plain = new ReorderableList(m_PlainItems, typeof(string));
        m_Plain.onChangedCallback = l => Log("changed", l);

        m_RefusingItems = new List<string> { "a", "b" };
        m_Refusing = new ReorderableList(m_RefusingItems, typeof(string));
        m_Refusing.onCanAddCallback = l => { Log("canAdd", l); return false; };
        m_Refusing.onCanRemoveCallback = l => { Log("canRemove", l); return false; };
        m_Refusing.onAddCallback = l => Log("add", l);
        m_Refusing.onRemoveCallback = l => Log("remove", l);

        m_MultiItems = new List<string> { "a", "b", "c", "d", "e" };
        m_Multi = new ReorderableList(m_MultiItems, typeof(string)) { multiSelect = true };
        m_Multi.onSelectCallback = l => Log("select", l);

        m_FixedItems = new List<string> { "a", "b", "c" };
        m_Fixed = new ReorderableList(m_FixedItems, typeof(string), false, true, true, true);
        m_Fixed.onMouseUpCallback = l => Log("mouseUp", l);
        m_Fixed.onSelectCallback = l => Log("select", l);

        var data = ScriptableObject.CreateInstance<ReorderableInputData>();
        m_Object = new SerializedObject(data);
        m_Serialized = new ReorderableList(m_Object, m_Object.FindProperty("values"));
        m_Serialized.onChangedCallback = l => Log("changed", l);

        Put("heights", m_Full.GetHeight(), m_Plain.GetHeight(), m_Serialized.GetHeight());
    }

    void DrawAll()
    {
        m_Full.onAddCallback = m_UseAddCallback ? l => { Log("add", l); m_FullItems.Add("n" + m_FullItems.Count); } : null;
        m_Full.onRemoveCallback = m_UseRemoveCallback ? l => { Log("remove", l); m_FullItems.RemoveAt(l.index); } : null;
        m_Full.onAddDropdownCallback = m_UseDropdown ? (r, l) => m_Calls.Add("addDropdown " + r.x + "," + r.y + "," + r.width + "," + r.height + " " + l.index) : null;
        m_Full.DoList(FullRect);
        m_Plain.DoList(PlainRect);
        m_Refusing.DoList(RefusingRect);
        m_Multi.DoList(MultiRect);
        m_Fixed.DoList(FixedRect);
        m_Object.Update();
        m_Serialized.DoList(SerializedRect);
        m_Object.ApplyModifiedProperties();
    }

    // A row's middle: header 20, top padding 4, rows of 23 (21 and 2 of padding).
    static Vector2 Row(Rect list, float row) => new Vector2(list.x + 150, list.y + 20 + 4 + 23 * row + 11.5f);

    // The footer's buttons: the footer starts after the rows and 4 of padding.
    static Vector2 Add(Rect list, int rows) => new Vector2(list.xMax - 10 - 8 - 50 + 4 + 12, list.y + 20 + 4 + 23 * rows + 4 + 8);
    static Vector2 Remove(Rect list, int rows) => new Vector2(list.xMax - 10 - 29 + 12, list.y + 20 + 4 + 23 * rows + 4 + 8);

    static Event Mouse(EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None) =>
        new Event { type = type, mousePosition = position, button = 0, clickCount = 1, modifiers = modifiers };
    static Event Key(string key) => Event.KeyboardEvent(key);

    (string Name, Func<Event> Make, Action Before)[] Script => new (string, Func<Event>, Action)[]
    {
        // Click a row: down selects (and takes the hot control when draggable), up on it.
        ("full.click.down", () => Mouse(EventType.MouseDown, Row(FullRect, 1)), null),
        ("full.click.up", () => Mouse(EventType.MouseUp, Row(FullRect, 1)), null),
        // Arrow keys on the focused list, wrapping at both ends.
        ("full.key.down", () => Key("down"), null),
        ("full.key.down2", () => Key("down"), null),
        ("full.key.downWrap", () => Key("down"), null),
        ("full.key.upWrap", () => Key("up"), null),
        // Drag row 0 below row 2 and drop it.
        ("full.drag.down", () => Mouse(EventType.MouseDown, Row(FullRect, 0)), null),
        ("full.drag.move1", () => Mouse(EventType.MouseDrag, Row(FullRect, 1.2f)), null),
        ("full.drag.move2", () => Mouse(EventType.MouseDrag, Row(FullRect, 2.6f)), null),
        ("full.drag.up", () => Mouse(EventType.MouseUp, Row(FullRect, 2.6f)), null),
        // A drag cancelled with Escape: no reorder.
        ("full.escape.down", () => Mouse(EventType.MouseDown, Row(FullRect, 3)), null),
        ("full.escape.move", () => Mouse(EventType.MouseDrag, Row(FullRect, 0.5f)), null),
        ("full.escape.key", () => Key("escape"), null),
        ("full.escape.up", () => Mouse(EventType.MouseUp, Row(FullRect, 0.5f)), null),
        // The footer: add and remove with callbacks, then the add drop-down.
        ("full.add.down", () => Mouse(EventType.MouseDown, Add(FullRect, 4)), null),
        ("full.add.up", () => Mouse(EventType.MouseUp, Add(FullRect, 4)), null),
        ("full.remove.down", () => Mouse(EventType.MouseDown, Remove(FullRect, 5)), null),
        ("full.remove.up", () => Mouse(EventType.MouseUp, Remove(FullRect, 5)), null),
        ("full.dropdown.down", () => Mouse(EventType.MouseDown, Add(FullRect, 4)), () => m_UseDropdown = true),
        ("full.dropdown.up", () => Mouse(EventType.MouseUp, Add(FullRect, 4)), null),
        // Default add (an empty string) and remove (the selected row).
        ("plain.add.down", () => Mouse(EventType.MouseDown, Add(PlainRect, 3)), null),
        ("plain.add.up", () => Mouse(EventType.MouseUp, Add(PlainRect, 3)), null),
        ("plain.select.down", () => Mouse(EventType.MouseDown, Row(PlainRect, 1)), null),
        ("plain.select.up", () => Mouse(EventType.MouseUp, Row(PlainRect, 1)), null),
        ("plain.remove.down", () => Mouse(EventType.MouseDown, Remove(PlainRect, 4)), null),
        ("plain.remove.up", () => Mouse(EventType.MouseUp, Remove(PlainRect, 4)), null),
        // Delete on the focused list removes the selected row.
        ("plain.delete.select", () => Mouse(EventType.MouseDown, Row(PlainRect, 0)), null),
        ("plain.delete.selectUp", () => Mouse(EventType.MouseUp, Row(PlainRect, 0)), null),
        ("plain.delete.key", () => Key("delete"), null),
        // Refused add and remove: the buttons are disabled.
        ("refusing.select", () => Mouse(EventType.MouseDown, Row(RefusingRect, 0)), null),
        ("refusing.selectUp", () => Mouse(EventType.MouseUp, Row(RefusingRect, 0)), null),
        ("refusing.add.down", () => Mouse(EventType.MouseDown, Add(RefusingRect, 2)), null),
        ("refusing.add.up", () => Mouse(EventType.MouseUp, Add(RefusingRect, 2)), null),
        ("refusing.remove.down", () => Mouse(EventType.MouseDown, Remove(RefusingRect, 2)), null),
        ("refusing.remove.up", () => Mouse(EventType.MouseUp, Remove(RefusingRect, 2)), null),
        // Multi-select: a click, an action-key click toggling, a shift click for a range.
        ("multi.click", () => Mouse(EventType.MouseDown, Row(MultiRect, 1)), null),
        ("multi.clickUp", () => Mouse(EventType.MouseUp, Row(MultiRect, 1)), null),
        ("multi.toggle", () => Mouse(EventType.MouseDown, Row(MultiRect, 3), EventModifiers.Control), null),
        ("multi.toggleUp", () => Mouse(EventType.MouseUp, Row(MultiRect, 3), EventModifiers.Control), null),
        ("multi.untoggle", () => Mouse(EventType.MouseDown, Row(MultiRect, 1), EventModifiers.Control), null),
        ("multi.untoggleUp", () => Mouse(EventType.MouseUp, Row(MultiRect, 1), EventModifiers.Control), null),
        ("multi.range", () => Mouse(EventType.MouseDown, Row(MultiRect, 0), EventModifiers.Shift), null),
        ("multi.rangeUp", () => Mouse(EventType.MouseUp, Row(MultiRect, 0), EventModifiers.Shift), null),
        // A non-draggable list: mouse up on the selected row.
        ("fixed.down", () => Mouse(EventType.MouseDown, Row(FixedRect, 2)), null),
        ("fixed.up", () => Mouse(EventType.MouseUp, Row(FixedRect, 2)), null),
        ("fixed.upElsewhere", () => Mouse(EventType.MouseUp, Row(FixedRect, 0)), null),
        // A serialized array: default add, reorder by dragging, remove.
        ("serialized.add.down", () => Mouse(EventType.MouseDown, Add(SerializedRect, 4)), null),
        ("serialized.add.up", () => Mouse(EventType.MouseUp, Add(SerializedRect, 4)), null),
        ("serialized.drag.down", () => Mouse(EventType.MouseDown, Row(SerializedRect, 0)), null),
        ("serialized.drag.move", () => Mouse(EventType.MouseDrag, Row(SerializedRect, 2.6f)), null),
        ("serialized.drag.up", () => Mouse(EventType.MouseUp, Row(SerializedRect, 2.6f)), null),
        ("serialized.remove.down", () => Mouse(EventType.MouseDown, Remove(SerializedRect, 5)), null),
        ("serialized.remove.up", () => Mouse(EventType.MouseUp, Remove(SerializedRect, 5)), null),
    };

    object[] State()
    {
        var serialized = new List<object>();
        var values = m_Object.FindProperty("values");
        for (int i = 0; i < values.arraySize; i++) serialized.Add(values.GetArrayElementAtIndex(i).intValue);
        return new object[]
        {
            string.Join(",", m_FullItems), m_Full.index, m_Full.selectedIndices.ToArray(),
            string.Join(",", m_PlainItems), m_Plain.index,
            string.Join(",", m_RefusingItems), m_Refusing.index,
            m_Multi.index, m_Multi.selectedIndices.ToArray(),
            m_Fixed.index,
            serialized.ToArray(), m_Serialized.index,
            GUIUtility.hotControl != 0, GUIUtility.keyboardControl != 0,
            new object[] { m_Full.HasKeyboardControl(), m_Plain.HasKeyboardControl(), m_Multi.HasKeyboardControl(), m_Fixed.HasKeyboardControl() },
        };
    }

    void OnGUI()
    {
        if (m_Done) return;
        var type = Event.current.type;
        if (type != EventType.Layout && type != EventType.Repaint) return;
        if (m_Step < 0)
        {
            DrawAll();
            if (type == EventType.Repaint) { m_Step = 0; Put("start", State()); }
            return;
        }
        if (type != EventType.Repaint || ++m_Frames % 2 != 0) { DrawAll(); return; }
        var script = Script;
        if (m_Step == script.Length)
        {
            m_Done = true;
            m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
            EditorApplication.Exit(0);
            return;
        }
        var step = script[m_Step];
        step.Before?.Invoke();
        var real = Event.current;
        var scripted = step.Make();
        Event.current = scripted;
        m_Calls.Clear();
        bool changedBefore = GUI.changed;
        GUI.changed = false;
        object[] state;
        try
        {
            DrawAll();
            state = State();
        }
        finally { Event.current = real; }
        bool changed = GUI.changed;
        GUI.changed = changedBefore;
        Put("script/" + step.Name, m_Calls.ToArray(), state, scripted.type == EventType.Used, changed);
        m_Step++;
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
