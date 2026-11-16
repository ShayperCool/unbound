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
    static string Widths(MultiColumnListView view)
    {
        var cols = view.Query(className: "unity-multi-column-header__column").ToList();
        return string.Join(" ", cols.Select(c => c.name + ":" + c.resolvedStyle.display + ":" + c.layout.width.ToString("R", CultureInfo.InvariantCulture)));
    }

    static MultiColumnListView View(VisualElement root, float width, float? maxWidth, float hiddenWidth)
    {
        var view = new MultiColumnListView();
        view.style.width = width; view.style.height = 120;
        view.columns.stretchMode = Columns.StretchMode.Grow;
        var a = new Column { name = "a", title = "A", width = 150, stretchable = true };
        var b = new Column { name = "b", title = "B", width = 150, stretchable = true };
        if (maxWidth.HasValue) { a.maxWidth = maxWidth.Value; b.maxWidth = maxWidth.Value; }
        view.columns.Add(a);
        view.columns.Add(b);
        view.columns.Add(new Column { name = "c", title = "C", width = hiddenWidth, visible = false });
        foreach (var c in view.columns) { c.makeCell = () => new Label(); c.bindCell = (e, i) => ((Label)e).text = i.ToString(); }
        view.itemsSource = new List<int> { 1, 2, 3 };
        root.Add(view);
        return view;
    }

    static void Replace(MultiColumnListView view)
    {
        view.SetViewController(new MultiColumnListViewController(view.columns, view.sortColumnDescriptions, new List<SortColumnDescription>()));
        view.Rebuild();
    }

    IEnumerator Frames() { for (int i = 0; i < 5; i++) yield return null; }

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("COLUMN_LAYOUT_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        if (Screen.dpi > 0) settings.scale = 96f / Screen.dpi;
        var doc = new GameObject("Doc").AddComponent<UIDocument>();
        doc.panelSettings = settings;

        // Shown and hidden before and after the controller is replaced, at one width.
        var view = View(doc.rootVisualElement, 400, null, 120);
        yield return Frames();
        Put("start", Widths(view));
        view.columns["c"].visible = true;
        yield return Frames();
        Put("show1", Widths(view));
        view.columns["c"].visible = false;
        yield return Frames();
        Put("hide1", Widths(view));
        Replace(view);
        yield return Frames();
        Put("replaced", Widths(view));
        view.columns["c"].visible = true;
        yield return Frames();
        Put("show2", Widths(view));
        view.columns["c"].visible = false;
        yield return Frames();
        Put("hide2", Widths(view));
        view.RemoveFromHierarchy();

        // The replaced controller's layout keeps the width it last laid out (400): after the view
        // is widened to 600, a column shown is made room for against 400 by a layout that still
        // follows the columns, though the current one has room for it.
        var stale = View(doc.rootVisualElement, 400, 150, 200);
        yield return Frames();
        Put("stale.start", Widths(stale));
        Replace(stale);
        yield return Frames();
        Put("stale.replaced", Widths(stale));
        stale.style.width = 600;
        yield return Frames();
        Put("stale.widened", Widths(stale));
        stale.columns["c"].visible = true;
        yield return Frames();
        Put("stale.show", Widths(stale));

        Put("complete", 1);
        output.Dispose();
        EditorApplication.Exit(0);
    }
}
