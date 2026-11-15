using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Binding notifications of multi-column view descriptions: which property changes Column, Columns
// and SortColumnDescription report through INotifyBindablePropertyChanged, with which sender and
// name, and how Columns forwards its columns' changes.
public class ColumnNotifyProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("ColumnNotifyProbe").AddComponent<ColumnNotifyProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    readonly List<string> events = new List<string>();
    readonly Dictionary<object, string> names = new Dictionary<object, string>();

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("COLUMN_NOTIFY_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    void Listen(object source, string name)
    {
        names[source] = name;
        if (source is INotifyBindablePropertyChanged notify)
            notify.propertyChanged += (sender, args) => events.Add((names.TryGetValue(sender, out var n) ? n : sender?.GetType().Name ?? "null") + ":" + args.propertyName);
    }

    // Runs a change and records the notifications it raised, in order.
    void Step(string key, Action change)
    {
        events.Clear();
        try { change(); }
        catch (Exception e) { events.Add("threw " + e.GetType().Name); }
        Put(key, string.Join(" ", events));
    }

    void Record()
    {
        Put("column.notifies", typeof(INotifyBindablePropertyChanged).IsAssignableFrom(typeof(Column)));
        Put("columns.notifies", typeof(INotifyBindablePropertyChanged).IsAssignableFrom(typeof(Columns)));
        Put("sort.notifies", typeof(INotifyBindablePropertyChanged).IsAssignableFrom(typeof(SortColumnDescription)));

        var columns = new Columns();
        var first = new Column();
        var second = new Column();
        var third = new Column();
        Listen(columns, "columns");
        Listen(first, "first");
        Listen(second, "second");
        Listen(third, "third");
        Step("add.first", () => columns.Add(first));
        Step("add.second", () => columns.Add(second));
        Step("add.third", () => columns.Add(third));

        // Every bindable property of a column, each changed once and then set to the same value.
        var asset = ScriptableObject.CreateInstance<VisualTreeAsset>();
        var icon = new Background();
        Step("second.name", () => second.name = "B");
        Step("second.name.same", () => second.name = "B");
        Step("second.title", () => second.title = "Bravo");
        Step("second.title.same", () => second.title = "Bravo");
        Step("second.icon", () => second.icon = Background.FromTexture2D(Texture2D.whiteTexture));
        Step("second.icon.same", () => second.icon = Background.FromTexture2D(Texture2D.whiteTexture));
        Step("second.visible", () => second.visible = false);
        Step("second.visible.same", () => second.visible = false);
        Step("second.width", () => second.width = 120);
        Step("second.width.same", () => second.width = 120);
        Step("second.width.percent", () => second.width = Length.Percent(30));
        Step("second.minWidth", () => second.minWidth = 40);
        Step("second.maxWidth", () => second.maxWidth = 400);
        Step("second.sortable", () => second.sortable = false);
        Step("second.stretchable", () => second.stretchable = true);
        Step("second.optional", () => second.optional = false);
        Step("second.resizable", () => second.resizable = false);
        Step("second.headerTemplate", () => second.headerTemplate = asset);
        Step("second.cellTemplate", () => second.cellTemplate = asset);
        Step("second.cellTemplate.same", () => second.cellTemplate = asset);
        // Members without a bindable property.
        Step("second.makeCell", () => second.makeCell = () => new Label());
        Step("second.bindCell", () => second.bindCell = (e, i) => { });
        Step("second.makeHeader", () => second.makeHeader = () => new Label());
        Step("second.comparison", () => second.comparison = (a, b) => a - b);
        Step("second.bindingPath", () => second.bindingPath = "path");

        // Forwarding depends on the column's index in the collection.
        Step("first.title", () => first.title = "Alpha");
        Step("third.title", () => third.title = "Charlie");
        Step("columns.insertAtZero", () => columns.Insert(0, new Column()));
        Step("first.title.atOne", () => first.title = "Alpha2");
        Step("columns.removeThird", () => columns.Remove(third));
        Step("third.title.removed", () => third.title = "Charlie2");
        Step("columns.reinsertThird", () => columns.Insert(1, third));
        Step("third.title.reinserted", () => third.title = "Charlie3");

        // The collection's own properties.
        Step("columns.primaryColumnName", () => columns.primaryColumnName = "B");
        Step("columns.primaryColumnName.same", () => columns.primaryColumnName = "B");
        Step("columns.reorderable", () => columns.reorderable = false);
        Step("columns.resizable", () => columns.resizable = false);
        Step("columns.resizePreview", () => columns.resizePreview = true);
        Step("columns.stretchMode", () => columns.stretchMode = Columns.StretchMode.Grow);
        Step("columns.stretchMode.same", () => columns.stretchMode = Columns.StretchMode.Grow);

        // A column moved to another collection forwards there only.
        var others = new Columns();
        Listen(others, "others");
        Step("others.addSecond", () => others.Add(new Column()));
        Step("others.moveThird", () => others.Add(third));
        Step("third.title.moved", () => third.title = "Charlie4");

        // Sort descriptions.
        var sort = new SortColumnDescription();
        Listen(sort, "sort");
        Step("sort.columnName", () => sort.columnName = "B");
        Step("sort.columnName.same", () => sort.columnName = "B");
        Step("sort.columnIndex", () => sort.columnIndex = 2);
        Step("sort.columnIndex.same", () => sort.columnIndex = 2);
        Step("sort.direction", () => sort.direction = SortDirection.Descending);
        Step("sort.direction.same", () => sort.direction = SortDirection.Descending);
        var descriptions = new SortColumnDescriptions();
        Step("sort.addToDescriptions", () => descriptions.Add(sort));
        Step("sort.direction.inDescriptions", () => sort.direction = SortDirection.Ascending);
        var constructed = new SortColumnDescription(3, SortDirection.Descending);
        Listen(constructed, "constructed");
        Step("constructed.columnIndex", () => constructed.columnIndex = 4);
        Put("constructed.state", constructed.columnIndex + "/" + constructed.direction + "/" + (constructed.columnName ?? "<null>"));
    }
}
