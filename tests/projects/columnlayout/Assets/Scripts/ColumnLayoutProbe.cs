using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// A multi-column list view's header column widths in Grow mode while a column is hidden and shown,
// before and after its view controller is replaced (the replaced controller's header is disposed and
// its column layout stays subscribed to the columns, as in the release).
public class ColumnLayoutProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("ColumnLayoutProbe").AddComponent<ColumnLayoutProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    MultiColumnListView view;

    string Widths()
    {
        var cols = view.Query(className: "unity-multi-column-header__column").ToList();
        return string.Join(" ", cols.Select(c => c.name + ":" + c.resolvedStyle.display + ":" + c.layout.width.ToString("R", CultureInfo.InvariantCulture)));
    }

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("COLUMN_LAYOUT_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        if (Screen.dpi > 0) settings.scale = 96f / Screen.dpi;
        var doc = new GameObject("Doc").AddComponent<UIDocument>();
        doc.panelSettings = settings;
        view = new MultiColumnListView();
        view.style.width = 400; view.style.height = 200;
        view.columns.stretchMode = Columns.StretchMode.Grow;
        view.columns.Add(new Column { name = "a", title = "A", width = 150, stretchable = true });
        view.columns.Add(new Column { name = "b", title = "B", width = 150, stretchable = true });
        view.columns.Add(new Column { name = "c", title = "C", width = 120, visible = false });
        foreach (var c in view.columns) { c.makeCell = () => new Label(); c.bindCell = (e, i) => ((Label)e).text = i.ToString(); }
        view.itemsSource = new List<int> { 1, 2, 3 };
        doc.rootVisualElement.Add(view);
        for (int i = 0; i < 5; i++) yield return null;
        Put("start", Widths());
        view.columns["c"].visible = true;
        for (int i = 0; i < 5; i++) yield return null;
        Put("show1", Widths());
        view.columns["c"].visible = false;
        for (int i = 0; i < 5; i++) yield return null;
        Put("hide1", Widths());
        view.SetViewController(new MultiColumnListViewController(view.columns, view.sortColumnDescriptions, new List<SortColumnDescription>()));
        view.Rebuild();
        for (int i = 0; i < 5; i++) yield return null;
        Put("replaced", Widths());
        view.columns["a"].width = 150; view.columns["b"].width = 150;
        for (int i = 0; i < 5; i++) yield return null;
        Put("reset", Widths());
        view.columns["c"].visible = true;
        for (int i = 0; i < 5; i++) yield return null;
        Put("show2", Widths());
        view.columns["c"].visible = false;
        for (int i = 0; i < 5; i++) yield return null;
        Put("hide2", Widths());
        view.columns["a"].width = 150; view.columns["b"].width = 150;
        for (int i = 0; i < 5; i++) yield return null;
        Put("reset2", Widths());
        view.columns["c"].visible = true;
        for (int i = 0; i < 5; i++) yield return null;
        Put("show3", Widths());
        Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(0);
    }
}
