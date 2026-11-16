using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// A collection view's serialized selection through JsonUtility (BaseVerticalCollectionView is an
// ISerializationCallbackReceiver): what ToJson writes for the selection, and the selection after
// FromJsonOverwrite replaces the selected ids (ids missing from the source, an empty list, no items
// source, outside a panel, a TreeView), the selection events it causes and the rows marked
// selected; then selection calls made after it. Controls: the callback called directly without a
// serialized change, and ToJson leaving the selection as it was.
public class CollectionJsonProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("CollectionJsonProbe").AddComponent<CollectionJsonProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    VisualElement root;
    int selectionChanged, indicesChanged;

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("COLLECTION_JSON_OUT"); if (string.IsNullOrEmpty(path)) yield break;
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

    static IEnumerator Frames() { yield return null; yield return null; }

    ListView List(bool withSource = true)
    {
        var source = new List<string>();
        for (int i = 0; i < 10; i++) source.Add("i" + i);
        var list = new ListView
        {
            fixedItemHeight = 20,
            selectionType = SelectionType.Multiple,
            makeItem = () => new Label(),
            bindItem = (e, i) => ((Label)e).text = "i" + i,
            itemsSource = withSource ? source : null,
        };
        list.style.height = 400;
        list.selectionChanged += _ => selectionChanged++;
        list.selectedIndicesChanged += _ => indicesChanged++;
        return list;
    }

    static string Ints(IEnumerable<int> values) => "[" + string.Join(",", values) + "]";

    // The selection API's answers and the selection events since the last call.
    string State(BaseVerticalCollectionView view)
    {
        var item = view.selectedItem;
        var result = "ids " + Ints(view.selectedIds) + " indices " + Ints(view.selectedIndices) + " index " + view.selectedIndex
            + " item " + (item ?? "<null>") + " items " + view.selectedItems.Count() + " events " + selectionChanged + "/" + indicesChanged;
        selectionChanged = indicesChanged = 0;
        return result;
    }

    // The bound rows marked selected, in index order.
    static string Marked(BaseVerticalCollectionView view)
    {
        var rows = view.Query(className: BaseVerticalCollectionView.itemUssClassName).ToList();
        var marked = rows.Where(e => e.ClassListContains(BaseVerticalCollectionView.itemSelectedVariantUssClassName))
            .Select(e => e.Q<Label>()?.text ?? "?").OrderBy(t => t, StringComparer.Ordinal);
        return "rows " + rows.Count + " marked [" + string.Join(",", marked) + "]";
    }

    static string SelectedIdsJson(object view)
    {
        var match = Regex.Match(JsonUtility.ToJson(view), "\"m_SelectedIds\":\\[[^\\]]*\\]");
        return match.Success ? match.Value : "<absent>";
    }

    static void Overwrite(object view, string ids) => JsonUtility.FromJsonOverwrite("{\"m_SelectedIds\":" + ids + "}", view);

    IEnumerator Record()
    {
        var list = List();
        root.Add(list);
        yield return Frames();
        list.SetSelection(new[] { 2, 5 });
        Put("toJson.selection", SelectedIdsJson(list));
        Put("toJson.state", State(list));

        // The callback alone: nothing serialized changed.
        ((ISerializationCallbackReceiver)list).OnAfterDeserialize();
        Put("direct.state", State(list));
        ((ISerializationCallbackReceiver)list).OnBeforeSerialize();
        Put("direct.before", State(list));

        Overwrite(list, "[7,3]");
        Put("overwrite.state", State(list));
        yield return Frames();
        Put("overwrite.marked", Marked(list) + " " + State(list));
        Put("overwrite.toJson", SelectedIdsJson(list));
        // Selection calls after the overwrite.
        list.AddToSelection(7);
        Put("overwrite.addSelected", State(list));
        list.AddToSelection(2);
        Put("overwrite.addPrevious", State(list));
        list.RemoveFromSelection(3);
        Put("overwrite.remove", State(list));
        list.ClearSelection();
        Put("overwrite.clear", State(list));

        list.SetSelection(new[] { 1, 4 });
        State(list);
        Overwrite(list, "[42,4]");
        Put("missing.state", State(list));
        Overwrite(list, "[]");
        Put("empty.state", State(list));
        yield return Frames();
        Put("empty.marked", Marked(list) + " " + State(list));

        // Without an items source, and outside a panel.
        var noSource = List(false);
        root.Add(noSource);
        yield return Frames();
        Overwrite(noSource, "[1]");
        Put("noSource.state", State(noSource) + " json " + SelectedIdsJson(noSource));
        noSource.itemsSource = Enumerable.Range(0, 5).Select(i => "n" + i).ToList();
        Put("noSource.sourceSet", State(noSource));
        root.Remove(list);
        root.Remove(noSource);

        var detached = List();
        Overwrite(detached, "[6]");
        Put("detached.state", State(detached));
        root.Add(detached);
        yield return Frames();
        Put("detached.attached", Marked(detached) + " " + State(detached));
        yield return Frames();
        Put("detached.later", Marked(detached) + " " + State(detached));
        detached.RemoveFromHierarchy();
        // Control: selected outside a panel through the selection API.
        var control = List();
        control.SetSelection(6);
        Put("detachedControl.state", State(control));
        root.Add(control);
        yield return Frames();
        Put("detachedControl.attached", Marked(control) + " " + State(control));
        yield return Frames();
        Put("detachedControl.later", Marked(control) + " " + State(control));
        control.RemoveFromHierarchy();

        // A TreeView: ids are the tree's item ids.
        var tree = new TreeView { fixedItemHeight = 20, selectionType = SelectionType.Multiple, makeItem = () => new Label() };
        tree.style.height = 400;
        tree.bindItem = (e, i) => ((Label)e).text = "t" + tree.GetIdForIndex(i);
        var roots = new List<TreeViewItemData<string>>
        {
            new TreeViewItemData<string>(10, "a", new List<TreeViewItemData<string>> { new TreeViewItemData<string>(11, "a1"), new TreeViewItemData<string>(12, "a2") }),
            new TreeViewItemData<string>(20, "b"),
        };
        tree.SetRootItems(roots);
        tree.selectionChanged += _ => selectionChanged++;
        tree.selectedIndicesChanged += _ => indicesChanged++;
        root.Add(tree);
        yield return Frames();
        Overwrite(tree, "[20,11]");
        Put("tree.state", State(tree));
        yield return Frames();
        Put("tree.marked", Marked(tree) + " " + State(tree));
        tree.ExpandItem(10);
        yield return Frames();
        Put("tree.expanded", Marked(tree) + " " + State(tree));
    }
}
