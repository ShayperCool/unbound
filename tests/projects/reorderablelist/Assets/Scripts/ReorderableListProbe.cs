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

public sealed class ReorderableListProbeData : ScriptableObject
{
    public int[] values = { 10, 20, 30 };
    public List<int> alternate = new List<int> { 1 };
}

static class ReorderableListProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("REORDERABLE_LIST_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, params object[] values) => output.Append("{\"k\":").Append(Json(key))
            .Append(",\"v\":").Append(Json(values)).Append("}\n");
        void State(string key, ReorderableList list) => Put(key, list.count, list.index, list.selectedIndices);

        var elements = new List<int> { 10, 20, 30, 40 };
        var list = new ReorderableList(elements, typeof(int));
        Put("list/defaults", ReferenceEquals(list.list, elements), list.serializedProperty == null,
            list.draggable, list.displayAdd, list.displayRemove, list.multiSelect,
            list.elementHeight, list.headerHeight, list.footerHeight, list.showDefaultBackground);
        State("list/initial", list);
        var selection = list.selectedIndices;
        int callbacks = 0;
        list.onSelectCallback += _ => callbacks++;
        list.index = 2;
        list.Select(0, append: true);
        list.Select(0, append: true);
        State("selection/appendWhileSingle", list);
        Put("selection/liveView", selection, list.IsSelected(0), list.IsSelected(1));
        list.index = 2;
        State("selection/replaceWithExisting", list);
        Put("selection/rangeWhileSingle", ExceptionFrom(() => list.SelectRange(0, 2)));
        list.multiSelect = true;
        list.SelectRange(3, 1);
        State("selection/reversedRange", list);
        list.multiSelect = false;
        list.Deselect(2);
        list.Deselect(99);
        State("selection/deselectWhileSingle", list);
        list.Select(-2, append: true);
        list.Select(30, append: true);
        State("selection/outOfRange", list);
        elements.RemoveAt(3);
        State("list/mutate", list);
        list.list = new List<int>();
        State("list/replace", list);
        list.ClearSelection();
        State("selection/clear", list);
        Put("selection/callbacks", callbacks, selection);
        list.list = null;
        State("list/noBacking", list);
        var configured = new ReorderableList((IList)null, typeof(int), false, false, false, true);
        Put("list/configured", configured.count, configured.index, configured.draggable, configured.displayAdd, configured.displayRemove);
        configured.draggable = true;
        Put("list/draggableChanged", configured.draggable);

        var first = ScriptableObject.CreateInstance<ReorderableListProbeData>();
        var second = ScriptableObject.CreateInstance<ReorderableListProbeData>();
        second.values = new[] { 1, 2 };
        using var firstObject = new SerializedObject(first);
        using var secondObject = new SerializedObject(second);
        using var multi = new SerializedObject(new UnityEngine.Object[] { first, second });
        var firstProperty = firstObject.FindProperty(nameof(first.values));
        var serialized = new ReorderableList(firstObject, firstProperty);
        Put("serialized/defaults", ReferenceEquals(serialized.serializedProperty, firstProperty), serialized.list == null,
            serialized.draggable, serialized.displayAdd, serialized.displayRemove, serialized.count, serialized.index);
        serialized.index = 2;
        firstProperty.arraySize = 4;
        State("serialized/resize", serialized);
        serialized.list = elements;
        Put("serialized/withList", serialized.count, ReferenceEquals(serialized.list, elements));
        serialized.serializedProperty = secondObject.FindProperty(nameof(second.alternate));
        State("serialized/rebind", serialized);
        Put("serialized/nullSetter", ExceptionFrom(() => serialized.serializedProperty = null), serialized.serializedProperty == null);
        State("serialized/fallback", serialized);
        var both = new ReorderableList(multi, multi.FindProperty(nameof(first.values)), true, false, true, false);
        multi.maxArraySizeForMultiEditing = 2;
        Put("serialized/multiAtLimit", both.count, both.index, both.draggable, both.displayAdd, both.displayRemove);
        multi.maxArraySizeForMultiEditing = 1;
        Put("serialized/multiOverLimit", both.count, both.index);
        firstObject.maxArraySizeForMultiEditing = 1;
        Put("serialized/singleOverLimit", new ReorderableList(firstObject, firstProperty).count);

        second.hideFlags = HideFlags.NotEditable;
        using var lockedObject = new SerializedObject(second);
        var lockedProperty = lockedObject.FindProperty(nameof(second.values));
        var locked = new ReorderableList(lockedObject, lockedProperty);
        Put("serialized/readOnly", lockedProperty.editable, locked.draggable, locked.count);
        var logs = new List<string>();
        Application.LogCallback receiver = (message, _, type) => logs.Add(type + ": " + message);
        Application.logMessageReceived += receiver;
        try
        {
            var value = firstProperty.GetArrayElementAtIndex(0);
            var scalar = new ReorderableList(firstObject, value);
            Put("serialized/scalar", scalar.count, scalar.index, logs);
        }
        finally { Application.logMessageReceived -= receiver; }

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static object ExceptionFrom(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error.GetType().Name + ": " + error.Message; }
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
