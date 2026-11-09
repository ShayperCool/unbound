using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

// Collection controls on a runtime panel: ListView (fixed and dynamic height, reordering, header and
// footer), TreeView, MultiColumnListView, MultiColumnTreeView and TabView. Each step records the
// item callbacks it caused (makeItem/bindItem/unbindItem/destroyItem and the column cell callbacks,
// with the element they were given), the events the controls raised and the resulting state (active
// rows with their layout, scroll offset, selection, items order). The panel is rendered after
// building and at the end.
public static class CollectionsProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string Output => Environment.GetEnvironmentVariable("UITK_COLLECTIONS_OUT");

    static readonly List<string> Log = new List<string>();
    static readonly StringBuilder Out = new StringBuilder();
    static VisualElement s_PanelRoot;
    static RenderTexture s_Target;

    static string F(float v) => v.ToString("R", Inv);
    static string Str(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    static string Ints(IEnumerable<int> values)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var v in values) { if (!first) sb.Append(','); sb.Append(v); first = false; }
        return sb.Append(']').ToString();
    }
    static string Objs(IEnumerable<object> values)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        foreach (var v in values) { if (!first) sb.Append(','); sb.Append(Str(v?.ToString())); first = false; }
        return sb.Append(']').ToString();
    }
    static string Rect(UnityEngine.Rect r) => "[" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height) + "]";
    static string V2(Vector2 v) => "[" + F(v.x) + "," + F(v.y) + "]";

    // ---- panel driving ----

    static Type Runtime => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    static void Update()
    {
        var m = Runtime.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        m.Invoke(null, null);
        m.Invoke(null, null);
    }

    static void Render(string name)
    {
        Runtime.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { false });
        Runtime.GetMethod("RenderOffscreenPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, null);
        var old = RenderTexture.active;
        RenderTexture.active = s_Target;
        var tex = new Texture2D(s_Target.width, s_Target.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new UnityEngine.Rect(0, 0, s_Target.width, s_Target.height), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(Output, name), tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        RenderTexture.active = old;
    }

    static void Pointer(EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None, int clickCount = 1)
    {
        var imgui = new Event { type = type, mousePosition = position, button = 0, modifiers = modifiers, clickCount = type == EventType.MouseMove ? 0 : clickCount };
        EventBase evt = type == EventType.MouseDown ? PointerDownEvent.GetPooled(imgui)
            : type == EventType.MouseUp ? PointerUpEvent.GetPooled(imgui)
            : (EventBase)PointerMoveEvent.GetPooled(imgui);
        using (evt) s_PanelRoot.SendEvent(evt);
    }

    static void Click(Vector2 position, EventModifiers modifiers = EventModifiers.None, int clickCount = 1)
    {
        Pointer(EventType.MouseMove, position, modifiers);
        Pointer(EventType.MouseDown, position, modifiers, clickCount);
        Pointer(EventType.MouseUp, position, modifiers, clickCount);
    }

    static void Navigate(NavigationMoveEvent.Direction direction, EventModifiers modifiers = EventModifiers.None)
    {
        using (var e = NavigationMoveEvent.GetPooled(direction, modifiers)) s_PanelRoot.SendEvent(e);
    }

    static void Key(KeyCode key, EventModifiers modifiers = EventModifiers.None)
    {
        using (var down = KeyDownEvent.GetPooled('\0', key, modifiers)) s_PanelRoot.SendEvent(down);
        using (var up = KeyUpEvent.GetPooled('\0', key, modifiers)) s_PanelRoot.SendEvent(up);
    }

    static Vector2 RowCenter(BaseVerticalCollectionView view, int index)
    {
        var e = view.GetRootElementForIndex(index);
        if (e == null) throw new InvalidOperationException("row " + index + " is not visible");
        return e.worldBound.center;
    }

    // ---- recording ----

    // Item elements made by the probe carry a sequential id in userData, so callbacks and rows can
    // name the element they were given.
    // Numbered per tag (one sequence per control or column), so one control's counts do not shift
    // another's ids.
    static readonly Dictionary<string, int> s_NextId = new Dictionary<string, int>();
    static string Id(VisualElement e) => e == null ? "null" : e.userData is string n ? n : "?" + e.GetType().Name;

    static Label MakeLabel(string tag)
    {
        s_NextId.TryGetValue(tag, out int id);
        s_NextId[tag] = ++id;
        Log.Add(tag + "make e" + id);
        return new Label { userData = tag + "e" + id };
    }

    static void Step(string section, string name, Action act, Func<string> state)
    {
        Log.Clear();
        string error = null;
        try { act(); Update(); }
        catch (Exception e) { error = e.GetType().Name + ": " + e.Message; Update(); }
        string s;
        try { s = state(); }
        catch (Exception e) { s = Str("state threw " + e.GetType().Name + ": " + e.Message); }
        Out.Append("{\"k\":\"step\",\"section\":").Append(Str(section)).Append(",\"step\":").Append(Str(name));
        if (error != null) Out.Append(",\"error\":").Append(Str(error));
        Out.Append(",\"log\":[");
        for (int i = 0; i < Log.Count; i++) { if (i > 0) Out.Append(','); Out.Append(Str(Log[i])); }
        Out.Append("],\"state\":").Append(s).Append("}\n");
    }

    // The visible rows of a collection view in content order: the element id and its text, its
    // layout in the content container, and whether it is shown as selected.
    static string Rows(BaseVerticalCollectionView view) => Rows(view, true);

    // Without layout for views whose rows animate in real time (animated reordering).
    static string Rows(BaseVerticalCollectionView view, bool layout)
    {
        var scroll = view.Q<ScrollView>();
        var content = scroll.contentContainer;
        var sb = new StringBuilder("{\"offset\":").Append(V2(scroll.scrollOffset))
            .Append(",\"content\":").Append(Rect(content.layout))
            .Append(",\"paddingTop\":").Append(F(content.resolvedStyle.paddingTop))
            .Append(",\"high\":").Append(F(scroll.verticalScroller.highValue))
            .Append(",\"selected\":").Append(Ints(view.selectedIndices))
            .Append(",\"rows\":[");
        bool first = true;
        foreach (var child in content.Children())
        {
            if (!first) sb.Append(',');
            first = false;
            Label tagged = null;
            child.Query<Label>().ForEach(l => { if (tagged == null && l.userData is string) tagged = l; });
            sb.Append("[").Append(Str(Id(tagged))).Append(',').Append(Str(tagged?.text)).Append(',').Append(layout ? Rect(child.layout) : "null")
              .Append(',').Append(child.ClassListContains(BaseVerticalCollectionView.itemSelectedVariantUssClassName) ? "true" : "false")
              .Append(',').Append(child.resolvedStyle.display == DisplayStyle.None ? "\"hidden\"" : "\"shown\"").Append(']');
        }
        return sb.Append("]}").ToString();
    }

    // A control's subtree: type, name, classes, layout and text, one entry per element.
    static string Tree(VisualElement top)
    {
        var sb = new StringBuilder("[");
        bool first = true;
        void Visit(VisualElement e, int depth)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append('[').Append(depth).Append(',').Append(Str(e.GetType().Name)).Append(',').Append(Str(e.name)).Append(",[");
            bool firstClass = true;
            foreach (var c in e.GetClasses()) { if (!firstClass) sb.Append(','); sb.Append(Str(c)); firstClass = false; }
            sb.Append("],").Append(Rect(e.layout)).Append(',').Append(Str((e as TextElement)?.text))
              .Append(',').Append(e.resolvedStyle.display == DisplayStyle.None ? "\"none\"" : "\"flex\"")
              .Append(',').Append(e.resolvedStyle.visibility == Visibility.Hidden ? "\"hidden\"" : "\"visible\"").Append(']');
            for (int i = 0; i < e.hierarchy.childCount; i++) Visit(e.hierarchy[i], depth + 1);
        }
        Visit(top, 0);
        return sb.Append(']').ToString();
    }

    static void Place(VisualElement e, float left, float top, float width, float height)
    {
        e.style.position = Position.Absolute;
        e.style.left = left; e.style.top = top; e.style.width = width; e.style.height = height;
    }

    static void HookEvents(BaseVerticalCollectionView view, string tag)
    {
        view.selectionChanged += items => Log.Add(tag + "selectionChanged " + Objs(items));
        view.selectedIndicesChanged += indices => Log.Add(tag + "selectedIndicesChanged " + Ints(indices));
        view.itemsChosen += items => Log.Add(tag + "itemsChosen " + Objs(items));
        view.itemIndexChanged += (a, b) => Log.Add(tag + "itemIndexChanged " + a + " " + b);
        view.itemsSourceChanged += () => Log.Add(tag + "itemsSourceChanged");
        if (view is BaseListView list)
        {
            list.itemsAdded += indices => Log.Add(tag + "itemsAdded " + Ints(indices));
            list.itemsRemoved += indices => Log.Add(tag + "itemsRemoved " + Ints(indices));
        }
    }

    static void Section(string name, Action body)
    {
        try { body(); }
        catch (Exception e)
        {
            Out.Append("{\"k\":\"section-error\",\"section\":").Append(Str(name)).Append(",\"error\":").Append(Str(e.GetType().Name + ": " + e.Message)).Append("}\n");
        }
    }

    public static void Run(UIDocument document, RenderTexture target)
    {
        Directory.CreateDirectory(Output);
        s_Target = target;
        var root = document.rootVisualElement;
        s_PanelRoot = root.panel.visualTree;
        root.Clear();
        root.style.backgroundColor = (Color)new Color32(240, 240, 236, 255);

        ListView fixedList = null, reorder = null, animated = null, footer = null, dynamic = null;
        TreeView tree = null;
        MultiColumnListView columns = null;
        MultiColumnTreeView columnTree = null;
        TabView tabs = null;

        Section("fixed", () => fixedList = FixedList(root));
        Section("reorder", () => reorder = Reorder(root, "reorder", ListViewReorderMode.Simple, 200));
        Section("animated", () => animated = Reorder(root, "animated", ListViewReorderMode.Animated, 392));
        Section("footer", () => footer = Footer(root));
        Section("dynamic", () => dynamic = Dynamic(root));
        Section("tree", () => tree = MakeTree(root));
        Section("columns", () => columns = Columns(root));
        Section("column-tree", () => columnTree = ColumnTree(root));
        Section("tabs", () => tabs = Tabs(root));

        Render("collections-built.png");

        Section("fixed-steps", () => FixedSteps(fixedList));
        Section("reorder-steps", () => ReorderSteps(reorder, "reorder"));
        Section("animated-steps", () => ReorderSteps(animated, "animated"));
        Section("footer-steps", () => FooterSteps(footer));
        Section("dynamic-steps", () => DynamicSteps(dynamic));
        Section("tree-steps", () => TreeSteps(tree));
        Section("columns-steps", () => ColumnsSteps(columns));
        Section("column-tree-steps", () => ColumnTreeSteps(columnTree));
        Section("tabs-steps", () => TabsSteps(tabs));
        Section("absolute", () => AbsoluteChildren(root));
        Section("animations", () => Animations(root));
        Section("binding", () => Bindings(root));

        // The animated list's rows move in real time; hidden for the final image.
        if (animated != null) { animated.style.visibility = Visibility.Hidden; Update(); }
        Render("collections-final.png");
        foreach (var (name, e) in new (string, VisualElement)[] { ("fixed", fixedList), ("reorder", reorder), ("animated", animated), ("footer", footer),
                     ("dynamic", dynamic), ("tree", tree), ("columns", columns), ("column-tree", columnTree), ("tabs", tabs) })
        {
            if (e == null) continue;
            if (name == "animated") continue; // rows animate in real time
            Out.Append("{\"k\":\"tree\",\"section\":").Append(Str(name)).Append(",\"tree\":").Append(Tree(e)).Append("}\n");
        }
        File.WriteAllText(Path.Combine(Output, "collections.jsonl"), Out.ToString());
    }

    // ---- ListView, fixed height ----

    static List<string> s_FixedItems;

    static ListView FixedList(VisualElement root)
    {
        s_FixedItems = new List<string>();
        for (int i = 0; i < 100; i++) s_FixedItems.Add("Row " + i);
        var list = new ListView { name = "fixed", fixedItemHeight = 20 };
        Place(list, 8, 8, 180, 200);
        list.makeItem = () => MakeLabel("");
        list.bindItem = (e, i) => { Log.Add("bind " + Id(e) + " " + i); ((Label)e).text = (string)list.itemsSource[i]; };
        list.unbindItem = (e, i) => Log.Add("unbind " + Id(e) + " " + i);
        list.destroyItem = e => Log.Add("destroy " + Id(e));
        HookEvents(list, "");
        Step("fixed", "attach", () => { list.itemsSource = s_FixedItems; root.Add(list); }, () => Rows(list));
        return list;
    }

    static void FixedSteps(ListView list)
    {
        var scroll = list.Q<ScrollView>();
        string Rows() => CollectionsProbe.Rows(list);
        Step("fixed", "scroll-30", () => scroll.scrollOffset = new Vector2(0, 30), Rows);
        Step("fixed", "scroll-200", () => scroll.scrollOffset = new Vector2(0, 200), Rows);
        Step("fixed", "scroll-past-end", () => scroll.scrollOffset = new Vector2(0, 5000), Rows);
        Step("fixed", "scroll-0", () => scroll.scrollOffset = Vector2.zero, Rows);
        Step("fixed", "scrolltoitem-50", () => list.ScrollToItem(50), Rows);
        Step("fixed", "scrolltoitem-45", () => list.ScrollToItem(45), Rows);
        Step("fixed", "scrolltoitem-last", () => list.ScrollToItem(-1), Rows);
        Step("fixed", "scrolltoitem-0", () => list.ScrollToItem(0), Rows);
        Step("fixed", "refreshitem-3", () => { s_FixedItems[3] = "Row 3 changed"; list.RefreshItem(3); }, Rows);
        Step("fixed", "refreshitem-offscreen", () => list.RefreshItem(60), Rows);
        Step("fixed", "refreshitems", () => list.RefreshItems(), Rows);
        Step("fixed", "rebuild", () => list.Rebuild(), Rows);
        Step("fixed", "append-refresh", () => { s_FixedItems.Add("Row 100"); list.RefreshItems(); }, Rows);
        Step("fixed", "scrolled-shrink-refresh", () => { scroll.scrollOffset = new Vector2(0, 1800); Update(); s_FixedItems.RemoveRange(20, s_FixedItems.Count - 20); list.RefreshItems(); }, Rows);
        Step("fixed", "itemheight-30", () => list.fixedItemHeight = 30, Rows);
        Step("fixed", "resize-120", () => list.style.height = 120, Rows);
        Step("fixed", "resize-200", () => list.style.height = 200, Rows);
        Step("fixed", "itemheight-20", () => list.fixedItemHeight = 20, Rows);
        Step("fixed", "scroll-top", () => scroll.scrollOffset = Vector2.zero, Rows);
        Step("fixed", "click-2", () => Click(RowCenter(list, 2)), Rows);
        Step("fixed", "click-2-again", () => Click(RowCenter(list, 2)), Rows);
        Step("fixed", "double-click-3", () => { Click(RowCenter(list, 3)); Click(RowCenter(list, 3), EventModifiers.None, 2); }, Rows);
        Step("fixed", "ctrl-click-single", () => Click(RowCenter(list, 5), EventModifiers.Control), Rows);
        Step("fixed", "multiple", () => list.selectionType = SelectionType.Multiple, Rows);
        Step("fixed", "ctrl-click-5", () => Click(RowCenter(list, 5), EventModifiers.Control), Rows);
        Step("fixed", "shift-click-8", () => Click(RowCenter(list, 8), EventModifiers.Shift), Rows);
        Step("fixed", "ctrl-click-6", () => Click(RowCenter(list, 6), EventModifiers.Control), Rows);
        Step("fixed", "click-7-in-selection", () => Click(RowCenter(list, 7)), Rows);
        Step("fixed", "nav-down", () => Navigate(NavigationMoveEvent.Direction.Down), Rows);
        Step("fixed", "nav-shift-down", () => Navigate(NavigationMoveEvent.Direction.Down, EventModifiers.Shift), Rows);
        Step("fixed", "nav-up", () => Navigate(NavigationMoveEvent.Direction.Up), Rows);
        Step("fixed", "key-end", () => Key(KeyCode.End), Rows);
        Step("fixed", "key-pageup", () => Key(KeyCode.PageUp), Rows);
        Step("fixed", "key-home", () => Key(KeyCode.Home), Rows);
        Step("fixed", "key-return", () => Key(KeyCode.Return), Rows);
        Step("fixed", "key-ctrl-a", () => Key(KeyCode.A, EventModifiers.Control), Rows);
        Step("fixed", "set-selection-7", () => list.SetSelection(7), Rows);
        Step("fixed", "set-selection-same", () => list.SetSelection(7), Rows);
        Step("fixed", "add-to-selection-9", () => list.AddToSelection(9), Rows);
        Step("fixed", "remove-from-selection-7", () => list.RemoveFromSelection(7), Rows);
        Step("fixed", "selection-without-notify", () => list.SetSelectionWithoutNotify(new[] { 1, 2 }), Rows);
        Step("fixed", "selected-index-4", () => list.selectedIndex = 4, Rows);
        Step("fixed", "selection-offscreen", () => list.SetSelection(15), Rows);
        Step("fixed", "clear-selection", () => list.ClearSelection(), Rows);
        Step("fixed", "selection-none", () => { list.SetSelection(3); Update(); list.selectionType = SelectionType.None; }, Rows);
        Step("fixed", "click-none", () => Click(RowCenter(list, 4)), Rows);
        Step("fixed", "single-again", () => { list.selectionType = SelectionType.Single; list.SetSelection(2); }, Rows);
        Step("fixed", "itemssource-5", () => list.itemsSource = new List<string> { "A", "B", "C", "D", "E" }, Rows);
        Step("fixed", "new-makeitem", () => list.makeItem = () => MakeLabel("new-"), Rows);
        Step("fixed", "itemssource-null", () => list.itemsSource = null, Rows);
        Step("fixed", "itemssource-back", () => list.itemsSource = s_FixedItems, Rows);
        var host = list.parent;
        Step("fixed", "detach", () => list.RemoveFromHierarchy(), () => "{\"children\":" + list.Q<ScrollView>().contentContainer.childCount + "}");
        Step("fixed", "attach-again", () => host.Add(list), Rows);
    }

    // ---- ListView reordering ----

    static ListView Reorder(VisualElement root, string tag, ListViewReorderMode mode, float left)
    {
        var items = new List<string>();
        for (int i = 0; i < 12; i++) items.Add(tag + " " + i);
        var list = new ListView { name = tag, fixedItemHeight = 20, reorderable = true, reorderMode = mode, selectionType = SelectionType.Multiple };
        Place(list, left, 8, 180, 200);
        list.makeItem = () => MakeLabel(tag + ":");
        list.bindItem = (e, i) => { Log.Add(tag + ":bind " + Id(e) + " " + i); ((Label)e).text = items[i]; };
        list.unbindItem = (e, i) => Log.Add(tag + ":unbind " + Id(e) + " " + i);
        list.destroyItem = e => Log.Add(tag + ":destroy " + Id(e));
        HookEvents(list, tag + ":");
        Step(tag, "attach", () => { list.itemsSource = items; root.Add(list); }, () => ReorderState(list));
        return list;
    }

    static string ReorderState(ListView list)
    {
        var rows = Rows(list, list.reorderMode != ListViewReorderMode.Animated);
        return rows.Substring(0, rows.Length - 1) + ",\"items\":" + Objs(Cast(list.itemsSource)) + "}";
    }

    static IEnumerable<object> Cast(IList list) { if (list == null) yield break; foreach (var o in list) yield return o; }

    static void Drag(Vector2 from, Vector2 to, int steps)
    {
        Pointer(EventType.MouseMove, from);
        Pointer(EventType.MouseDown, from);
        for (int s = 1; s <= steps; s++)
        {
            Pointer(EventType.MouseMove, Vector2.Lerp(from, to, s / (float)steps));
            Update();
        }
        Pointer(EventType.MouseUp, to);
    }

    static void ReorderSteps(ListView list, string tag)
    {
        string State() => ReorderState(list);
        // Moves by the handle in animated mode (the row's drag handle), by the row itself otherwise.
        Vector2 Grip(int index)
        {
            var row = list.GetRootElementForIndex(index);
            var handle = row?.Q(className: BaseListView.reorderableItemHandleUssClassName);
            return handle != null && handle.resolvedStyle.display != DisplayStyle.None ? handle.worldBound.center : RowCenter(list, index);
        }
        // Animated rows open and close their gaps over 500 ms: each drag starts once they have
        // settled, so where the pointer lands does not depend on the machine's speed.
        void Settle()
        {
            if (list.reorderMode != ListViewReorderMode.Animated) return;
            System.Threading.Thread.Sleep(600);
            Update();
        }
        Step(tag, "drag-1-to-5", () => { Settle(); var a = Grip(1); var b = RowCenter(list, 5) + new Vector2(0, 6); Drag(a, b, 6); }, State);
        Step(tag, "drag-6-to-0", () => { Settle(); var a = Grip(6); var b = RowCenter(list, 0) + new Vector2(0, -6); Drag(a, b, 6); }, State);
        Step(tag, "drag-2-in-place", () => { Settle(); var a = Grip(2); Drag(a, a + new Vector2(0, 12), 3); }, State);
        Step(tag, "small-move-no-drag", () => { Settle(); var a = Grip(3); Drag(a, a + new Vector2(0, 2), 2); }, State);
        Step(tag, "select-3-4", () => list.SetSelection(new[] { 3, 4 }), State);
        Step(tag, "drag-selection-to-9", () => { Settle(); var a = Grip(3); var b = RowCenter(list, 9) + new Vector2(0, 6); Drag(a, b, 8); }, State);
        Step(tag, "drag-to-end", () => { Settle(); int first = 0; while (list.GetRootElementForIndex(first) == null) first++; var a = Grip(first); var bottom = list.Q<ScrollView>().contentViewport.worldBound; Drag(a, new Vector2(a.x, bottom.yMax - 2), 10); }, State);
    }

    // ---- ListView header and footer ----

    static ListView Footer(VisualElement root)
    {
        var items = new List<string> { "One", "Two", "Three" };
        var list = new ListView { name = "footer", fixedItemHeight = 20, showAddRemoveFooter = true, showFoldoutHeader = true, headerTitle = "Things",
            showBoundCollectionSize = true, showBorder = true, showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly };
        Place(list, 584, 8, 180, 220);
        list.makeItem = () => MakeLabel("footer:");
        list.bindItem = (e, i) => { Log.Add("footer:bind " + Id(e) + " " + i); ((Label)e).text = items[i] ?? "(null)"; };
        list.unbindItem = (e, i) => Log.Add("footer:unbind " + Id(e) + " " + i);
        list.destroyItem = e => Log.Add("footer:destroy " + Id(e));
        HookEvents(list, "footer:");
        Step("footer", "attach", () => { list.itemsSource = items; root.Add(list); }, () => FooterState(list));
        return list;
    }

    static string FooterState(ListView list)
    {
        var rows = Rows(list);
        return rows.Substring(0, rows.Length - 1) + ",\"items\":" + Objs(Cast(list.itemsSource)) + ",\"tree\":" + Tree(list) + "}";
    }

    static void FooterSteps(ListView list)
    {
        string State() => FooterState(list);
        Button Add() => list.Q<Button>(BaseListView.footerAddButtonName);
        Button Remove() => list.Q<Button>(BaseListView.footerRemoveButtonName);
        Step("footer", "click-add", () => Click(Add().worldBound.center), State);
        Step("footer", "select-0-click-remove", () => { list.SetSelection(0); Update(); Click(Remove().worldBound.center); }, State);
        Step("footer", "click-remove-no-selection", () => { list.ClearSelection(); Update(); Click(Remove().worldBound.center); }, State);
        Step("footer", "size-field-5", () => { var f = list.Q<TextField>(); if (f != null) { f.value = "5"; using (var e = KeyDownEvent.GetPooled('\n', KeyCode.Return, EventModifiers.None)) f.SendEvent(e); } }, State);
        Step("footer", "collapse-header", () => { var fold = list.Q<Foldout>(); fold.value = false; }, State);
        Step("footer", "expand-header", () => { var fold = list.Q<Foldout>(); fold.value = true; }, State);
        Step("footer", "onadd-override", () => { list.onAdd = l => Log.Add("footer:onAdd"); Update(); Click(Add().worldBound.center); }, State);
        Step("footer", "onremove-override", () => { list.onRemove = l => Log.Add("footer:onRemove"); list.SetSelection(1); Update(); Click(Remove().worldBound.center); }, State);
        Step("footer", "no-footer", () => list.showAddRemoveFooter = false, State);
        Step("footer", "no-header", () => list.showFoldoutHeader = false, State);
        Step("footer", "empty", () => { list.itemsSource = new List<string>(); }, State);
    }

    // ---- ListView, dynamic height ----

    static float DynamicHeight(int i) => 16 + (i % 5) * 8;

    static List<int> s_DynamicItems;

    static ListView Dynamic(VisualElement root)
    {
        s_DynamicItems = new List<int>();
        for (int i = 0; i < 60; i++) s_DynamicItems.Add(i);
        var list = new ListView { name = "dynamic", virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight };
        Place(list, 584, 240, 180, 150);
        list.makeItem = () => MakeLabel("dyn:");
        list.bindItem = (e, i) => { Log.Add("dyn:bind " + Id(e) + " " + i); var label = (Label)e; label.text = "Dyn " + s_DynamicItems[i]; label.style.height = DynamicHeight(s_DynamicItems[i]); };
        list.unbindItem = (e, i) => Log.Add("dyn:unbind " + Id(e) + " " + i);
        list.destroyItem = e => Log.Add("dyn:destroy " + Id(e));
        HookEvents(list, "dyn:");
        Step("dynamic", "attach", () => { list.itemsSource = s_DynamicItems; root.Add(list); }, () => Rows(list));
        return list;
    }

    static void DynamicSteps(ListView list)
    {
        var scroll = list.Q<ScrollView>();
        string Rows() => CollectionsProbe.Rows(list);
        Step("dynamic", "update", () => { }, Rows);
        Step("dynamic", "scroll-50", () => scroll.scrollOffset = new Vector2(0, 50), Rows);
        Step("dynamic", "scroll-50-update", () => { }, Rows);
        Step("dynamic", "scroll-400", () => scroll.scrollOffset = new Vector2(0, 400), Rows);
        Step("dynamic", "scroll-400-update", () => { }, Rows);
        Step("dynamic", "scroll-end", () => scroll.scrollOffset = new Vector2(0, 100000), Rows);
        Step("dynamic", "scroll-end-update", () => { }, Rows);
        Step("dynamic", "scroll-back-20", () => scroll.scrollOffset = new Vector2(0, 20), Rows);
        Step("dynamic", "scroll-back-20-update", () => { }, Rows);
        Step("dynamic", "scrolltoitem-30", () => list.ScrollToItem(30), Rows);
        Step("dynamic", "scrolltoitem-30-update", () => { }, Rows);
        Step("dynamic", "scrolltoitem-2", () => list.ScrollToItem(2), Rows);
        Step("dynamic", "scrolltoitem-2-update", () => { }, Rows);
        Step("dynamic", "click-row-3", () => Click(RowCenter(list, 3)), Rows);
        Step("dynamic", "refreshitems", () => list.RefreshItems(), Rows);
        Step("dynamic", "rebuild", () => list.Rebuild(), Rows);
        Step("dynamic", "rebuild-update", () => { }, Rows);
    }

    // ---- TreeView ----

    static List<TreeViewItemData<string>> TreeItems(int roots, int children, int grandchildren)
    {
        var result = new List<TreeViewItemData<string>>();
        int id = 0;
        for (int r = 0; r < roots; r++)
        {
            int rootId = id++;
            var kids = new List<TreeViewItemData<string>>();
            for (int c = 0; c < children; c++)
            {
                int childId = id++;
                var grand = new List<TreeViewItemData<string>>();
                for (int g = 0; g < grandchildren; g++) { int gid = id++; grand.Add(new TreeViewItemData<string>(gid, "Leaf " + gid)); }
                kids.Add(new TreeViewItemData<string>(childId, "Child " + childId, grand));
            }
            result.Add(new TreeViewItemData<string>(rootId, "Root " + rootId, kids));
        }
        return result;
    }

    static string TreeState(BaseTreeView tree)
    {
        var rows = Rows(tree);
        var ids = new List<int>();
        for (int i = 0; i < tree.viewController.itemsSource.Count; i++) ids.Add(tree.GetIdForIndex(i));
        var expanded = new List<int>();
        foreach (var id in ids) if (tree.IsExpanded(id)) expanded.Add(id);
        return rows.Substring(0, rows.Length - 1) + ",\"ids\":" + Ints(ids) + ",\"expanded\":" + Ints(expanded) + ",\"selectedIds\":" + Ints(tree.selectedIds) + "}";
    }

    static TreeView MakeTree(VisualElement root)
    {
        var tree = new TreeView { name = "tree", fixedItemHeight = 20 };
        Place(tree, 8, 240, 180, 200);
        tree.SetRootItems(TreeItems(3, 3, 2));
        tree.makeItem = () => MakeLabel("tree:");
        tree.bindItem = (e, i) => { Log.Add("tree:bind " + Id(e) + " " + i); ((Label)e).text = tree.GetItemDataForIndex<string>(i); };
        tree.unbindItem = (e, i) => Log.Add("tree:unbind " + Id(e) + " " + i);
        tree.destroyItem = e => Log.Add("tree:destroy " + Id(e));
        HookEvents(tree, "tree:");
        tree.itemExpandedChanged += a => Log.Add("tree:itemExpandedChanged " + a.id + " " + (a.isExpanded ? "expanded" : "collapsed") + (a.isAppliedToAllChildren ? " all" : ""));
        Step("tree", "attach", () => root.Add(tree), () => TreeState(tree));
        return tree;
    }

    static void TreeSteps(TreeView tree)
    {
        string State() => TreeState(tree);
        Toggle ToggleOf(int index) => tree.GetRootElementForIndex(index)?.parent?.parent?.Q<Toggle>() ?? tree.GetRootElementForIndex(index)?.parent?.Q<Toggle>();
        Step("tree", "expand-0", () => tree.ExpandItem(0), State);
        Step("tree", "expand-1-children", () => tree.ExpandItem(1, true), State);
        Step("tree", "click-toggle-row-0", () => Click(ToggleOf(0).worldBound.center), State);
        Step("tree", "click-toggle-row-1", () => Click(ToggleOf(1).worldBound.center), State);
        Step("tree", "alt-click-toggle-row-1", () => Click(ToggleOf(1).worldBound.center, EventModifiers.Alt), State);
        Step("tree", "expand-all", () => tree.ExpandAll(), State);
        Step("tree", "scroll-100", () => tree.Q<ScrollView>().scrollOffset = new Vector2(0, 100), State);
        Step("tree", "collapse-all", () => tree.CollapseAll(), State);
        Step("tree", "select-id-25", () => tree.SetSelectionById(25), State);
        Step("tree", "collapse-10", () => tree.CollapseItem(10), State);
        Step("tree", "click-row-2", () => { tree.Q<ScrollView>().scrollOffset = Vector2.zero; Update(); Click(RowCenter(tree, 2)); }, State);
        Step("tree", "nav-right", () => Navigate(NavigationMoveEvent.Direction.Right), State);
        Step("tree", "nav-right-2", () => Navigate(NavigationMoveEvent.Direction.Right), State);
        Step("tree", "nav-left", () => Navigate(NavigationMoveEvent.Direction.Left), State);
        Step("tree", "nav-left-2", () => Navigate(NavigationMoveEvent.Direction.Left), State);
        // Selection by id of items with children (collapsed, under a collapsed parent), and adding to it.
        Step("tree", "collapse-all-again", () => tree.CollapseAll(), State);
        Step("tree", "select-id-parent-10", () => tree.SetSelectionById(10), State);
        Step("tree", "select-id-child-14", () => tree.SetSelectionById(14), State);
        Step("tree", "add-to-selection-id-17", () => { tree.selectionType = SelectionType.Multiple; tree.AddToSelectionById(17); }, State);
        Step("tree", "select-without-notify-id-21", () => tree.SetSelectionByIdWithoutNotify(new[] { 21 }), State);
        Step("tree", "expand-leaf-2", () => tree.ExpandItem(2), State);
        // New root items that keep an expanded id (0): is its expansion kept?
        Step("tree", "expand-0-before-new-roots", () => tree.ExpandItem(0), State);
        Step("tree", "set-root-items", () => { tree.SetRootItems(TreeItems(2, 2, 0)); tree.Rebuild(); }, State);
    }

    // ---- MultiColumnListView ----

    static Column MakeColumn(string tag, string name, string title, float width, bool stretch, Func<int, string> text)
    {
        var column = new Column { name = name, title = title, width = width, stretchable = stretch };
        column.makeCell = () => MakeLabel(tag + name + ":");
        column.bindCell = (e, i) => { Log.Add(tag + name + ":bind " + Id(e) + " " + i); ((Label)e).text = text(i); };
        column.unbindCell = (e, i) => Log.Add(tag + name + ":unbind " + Id(e) + " " + i);
        column.destroyCell = e => Log.Add(tag + name + ":destroy " + Id(e));
        return column;
    }

    static string ColumnsState(BaseVerticalCollectionView view)
    {
        var rows = Rows(view);
        var header = view.Q(className: "unity-multi-column-header");
        return rows.Substring(0, rows.Length - 1) + ",\"header\":" + (header == null ? "null" : Tree(header)) + ",\"firstRow\":" +
            (view.GetRootElementForIndex(0) is VisualElement r ? Tree(r) : "null") + "}";
    }

    static MultiColumnListView Columns(VisualElement root)
    {
        var items = new List<string>();
        for (int i = 0; i < 20; i++) items.Add("Item " + i);
        var view = new MultiColumnListView { name = "columns", fixedItemHeight = 20, showAlternatingRowBackgrounds = AlternatingRowBackground.All };
        Place(view, 200, 240, 280, 160);
        view.columns.Add(MakeColumn("cols:", "name", "Name", 80, true, i => items[i]));
        view.columns.Add(MakeColumn("cols:", "value", "Value", 60, false, i => (i * 3).ToString(Inv)));
        view.columns.Add(MakeColumn("cols:", "flag", "Flag", 40, false, i => i % 2 == 0 ? "yes" : "no"));
        HookEvents(view, "cols:");
        Step("columns", "attach", () => { view.itemsSource = items; root.Add(view); }, () => ColumnsState(view));
        return view;
    }

    static void ColumnsSteps(MultiColumnListView view)
    {
        string State() => ColumnsState(view);
        Step("columns", "scroll-60", () => view.Q<ScrollView>().scrollOffset = new Vector2(0, 60), State);
        Step("columns", "hide-value", () => view.columns["value"].visible = false, State);
        Step("columns", "show-value", () => view.columns["value"].visible = true, State);
        Step("columns", "width-flag-70", () => view.columns["flag"].width = 70, State);
        Step("columns", "click-row-4", () => Click(RowCenter(view, 4)), State);
        Step("columns", "add-column", () => view.columns.Add(MakeColumn("cols:", "extra", "Extra", 50, false, i => "x" + i)), State);
        Step("columns", "remove-column", () => view.columns.Remove(view.columns["extra"]), State);
        Step("columns", "refreshitems", () => view.RefreshItems(), State);
        // Sorting: rows ordered through the columns' comparisons (sortingMode Default), header clicks
        // (again: the other direction; Ctrl adds a column, Shift removes one), then turned off.
        Step("columns", "sort-value-desc", () =>
        {
            view.columnSortingChanged += () => Log.Add("cols:columnSortingChanged");
            view.columns["name"].comparison = (a, b) => string.CompareOrdinal((string)view.itemsSource[a], (string)view.itemsSource[b]);
            view.columns["value"].comparison = (a, b) => a.CompareTo(b);
            view.columns["flag"].comparison = (a, b) => (a % 2).CompareTo(b % 2);
            view.sortingMode = ColumnSortingMode.Default;
            view.sortColumnDescriptions.Add(new SortColumnDescription("value", SortDirection.Descending));
        }, State);
        Step("columns", "click-header-name", () => Click(HeaderCenter(view, "name")), State);
        Step("columns", "click-header-name-again", () => Click(HeaderCenter(view, "name")), State);
        Step("columns", "ctrl-click-header-flag", () => Click(HeaderCenter(view, "flag"), EventModifiers.Control), State);
        Step("columns", "shift-click-header-name", () => Click(HeaderCenter(view, "name"), EventModifiers.Shift), State);
        Step("columns", "sort-off", () => { view.sortColumnDescriptions.Clear(); view.sortingMode = ColumnSortingMode.None; }, State);
        // Resizing: the handle after the value column dragged 20 points right, then widths restored.
        Step("columns", "drag-resize-value", () =>
        {
            var from = HandleCenter(view, 1);
            Drag(from, from + new Vector2(20, 0), 4);
        }, State);
        Step("columns", "restore-widths", () => { view.columns["value"].width = 60; view.columns["flag"].width = 70; }, State);
        // Changing only the sorting mode after a sort, then Custom sorting (the game orders the rows).
        Step("columns", "sort-again", () =>
        {
            view.sortingMode = ColumnSortingMode.Default;
            view.sortColumnDescriptions.Add(new SortColumnDescription("value", SortDirection.Descending));
        }, State);
        Step("columns", "mode-none-only", () => view.sortingMode = ColumnSortingMode.None, State);
        Step("columns", "mode-none-refresh", () => view.RefreshItems(), State);
        Step("columns", "mode-default-again", () => view.sortingMode = ColumnSortingMode.Default, State);
        Step("columns", "mode-custom", () => view.sortingMode = ColumnSortingMode.Custom, State);
        Step("columns", "mode-custom-refresh", () => view.RefreshItems(), State);
        Step("columns", "sort-cleanup", () =>
        {
            view.sortingMode = ColumnSortingMode.Default;
            view.sortColumnDescriptions.Clear();
            view.sortingMode = ColumnSortingMode.None;
            view.RefreshItems();
        }, State);
        // Grow mode on a separate view: columns keep their widths, then grow with the header.
        MultiColumnListView grow = null;
        Step("columns", "grow-attach", () =>
        {
            var items = new List<string> { "a", "b", "c" };
            var growColumns = new UnityEngine.UIElements.Columns { stretchMode = UnityEngine.UIElements.Columns.StretchMode.Grow };
            growColumns.Add(MakeColumn("grow:", "name", "Name", 80, true, i => items[i]));
            growColumns.Add(MakeColumn("grow:", "value", "Value", 60, false, i => i.ToString(Inv)));
            growColumns.Add(MakeColumn("grow:", "flag", "Flag", 40, false, i => "f"));
            grow = new MultiColumnListView(growColumns) { name = "grow", fixedItemHeight = 20, itemsSource = items };
            Place(grow, 500, 300, 280, 100);
            view.parent.Add(grow);
        }, () => ColumnsState(grow));
        Step("columns", "grow-wider", () => grow.style.width = 340, () => ColumnsState(grow));
        Step("columns", "grow-narrower", () => grow.style.width = 200, () => ColumnsState(grow));
        Step("columns", "grow-wider-again", () => grow.style.width = 300, () => ColumnsState(grow));
        Step("columns", "grow-remove", () => grow.RemoveFromHierarchy(), () => "null");
    }

    static Vector2 HeaderCenter(BaseVerticalCollectionView view, string column) =>
        view.Q(column, "unity-multi-column-header__column").worldBound.center;

    static Vector2 HandleCenter(BaseVerticalCollectionView view, int index) =>
        view.Query(className: "unity-multi-column-header__column-resize-handle__drag-area").AtIndex(index).worldBound.center;

    // ---- MultiColumnTreeView ----

    static MultiColumnTreeView ColumnTree(VisualElement root)
    {
        var view = new MultiColumnTreeView { name = "column-tree", fixedItemHeight = 20 };
        Place(view, 200, 410, 280, 140);
        view.SetRootItems(TreeItems(3, 2, 1));
        view.columns.Add(MakeColumn("ctree:", "name", "Name", 140, false, i => view.GetItemDataForIndex<string>(i)));
        view.columns.Add(MakeColumn("ctree:", "id", "Id", 60, true, i => view.GetIdForIndex(i).ToString(Inv)));
        HookEvents(view, "ctree:");
        view.itemExpandedChanged += a => Log.Add("ctree:itemExpandedChanged " + a.id + " " + (a.isExpanded ? "expanded" : "collapsed"));
        Step("column-tree", "attach", () => root.Add(view), () => ColumnsState(view));
        return view;
    }

    static void ColumnTreeSteps(MultiColumnTreeView view)
    {
        string State() { var c = ColumnsState(view); var t = TreeState(view); return "{\"columns\":" + c + ",\"tree\":" + t + "}"; }
        Step("column-tree", "expand-0", () => view.ExpandItem(0), State);
        Step("column-tree", "expand-all", () => view.ExpandAll(), State);
        Step("column-tree", "click-row-1", () => Click(RowCenter(view, 1)), State);
        Step("column-tree", "collapse-all", () => view.CollapseAll(), State);
        // Sorting a tree orders items among their siblings.
        Step("column-tree", "sort-id-desc", () =>
        {
            view.ExpandAll();
            view.columns["id"].comparison = (a, b) => view.GetIdForIndex(a).CompareTo(view.GetIdForIndex(b));
            view.sortingMode = ColumnSortingMode.Default;
            view.sortColumnDescriptions.Add(new SortColumnDescription("id", SortDirection.Descending));
        }, State);
        Step("column-tree", "sort-off", () => { view.sortColumnDescriptions.Clear(); view.sortingMode = ColumnSortingMode.None; view.CollapseAll(); }, State);
        // A sorted tree of uneven shape (one child collapsed): which row shows which indent and toggle.
        string Shape() => "{\"columns\":" + ColumnsState(view) + ",\"tree\":" + TreeState(view) + ",\"shape\":" + TreeRowShape(view) + "}";
        Step("column-tree", "uneven-expand", () => { view.ExpandAll(); view.CollapseItem(1); }, Shape);
        Step("column-tree", "uneven-sort", () =>
        {
            view.sortingMode = ColumnSortingMode.Default;
            view.sortColumnDescriptions.Add(new SortColumnDescription("id", SortDirection.Descending));
        }, Shape);
        Step("column-tree", "uneven-sorted-expand", () => view.ExpandItem(1), Shape);
        Step("column-tree", "uneven-off", () => { view.sortColumnDescriptions.Clear(); view.sortingMode = ColumnSortingMode.None; view.CollapseAll(); }, Shape);
        // The primary column (the one holding the toggle) named, then back to the first visible one.
        Step("column-tree", "primary-id", () => view.columns.primaryColumnName = "id", Shape);
        Step("column-tree", "primary-default", () => view.columns.primaryColumnName = null, Shape);
    }

    static string FirstClass(VisualElement e)
    {
        foreach (var c in e.GetClasses()) return c;
        return null;
    }

    // Per visible row of a tree: its first cell text, indent width, and toggle visible/value.
    static string TreeRowShape(BaseTreeView view)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < view.viewController.itemsSource.Count; i++)
        {
            var root = view.GetRootElementForIndex(i);
            if (root == null) continue;
            if (sb.Length > 1) sb.Append(',');
            var indent = root.Q("unity-tree-view__item-indent");
            var toggle = root.Q<Toggle>();
            var label = root.Q<Label>();
            sb.Append('[').Append(i).Append(',').Append(Str(label?.text)).Append(',').Append(F(indent == null ? -1f : indent.layout.width))
              .Append(',').Append(toggle == null ? "null" : (toggle.visible ? "true" : "false")).Append(',').Append(toggle == null ? "null" : (toggle.value ? "true" : "false"))
              .Append(',').Append(toggle == null || toggle.parent == null ? "null" : Str(FirstClass(toggle.parent))).Append(']');
        }
        return sb.Append(']').ToString();
    }


    // ---- runtime data binding ----

    public class BindSource
    {
        public string message = "hi";
        public int count = 3;
        public float ratio = 0.25f;
        public BindInner inner = new BindInner();
    }

    public class BindInner
    {
        public string value = "inner";
    }

    public class BindNotifying : INotifyBindablePropertyChanged
    {
        public string message = "start";
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;
        public void Notify(string name) => propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(name));
    }

    public class BindHashed : IDataSourceViewHashProvider
    {
        public string message = "h0";
        public long hash;
        public long GetViewHashCode() => hash;
    }

    public struct BindTag
    {
        public int id;
    }

    public class BindTagged
    {
        public BindTag tag = new BindTag { id = 7 };
    }

    class LoggingBinding : CustomBinding
    {
        readonly string m_Name;
        public LoggingBinding(string name) => m_Name = name;
        protected override BindingResult Update(in BindingContext context)
        {
            Log.Add("custom " + m_Name + " update " + context.bindingId + " path=" + context.dataSourcePath + " source=" + (context.dataSource == null ? "null" : context.dataSource.GetType().Name));
            return new BindingResult(BindingStatus.Success);
        }
        protected override void OnActivated(in BindingActivationContext context) => Log.Add("custom " + m_Name + " activated " + context.targetElement.name + " " + context.bindingId);
        protected override void OnDeactivated(in BindingActivationContext context) => Log.Add("custom " + m_Name + " deactivated " + context.targetElement.name + " " + context.bindingId);
        protected override void OnDataSourceChanged(in DataSourceContextChanged context) =>
            Log.Add("custom " + m_Name + " source " + (context.previousContext.dataSource?.GetType().Name ?? "null") + "/" + context.previousContext.dataSourcePath
                + " -> " + (context.newContext.dataSource?.GetType().Name ?? "null") + "/" + context.newContext.dataSourcePath);
    }

    // Bindings on labels and fields in a box: each step records the texts and values, the last
    // results to the UI and to the source, and the custom bindings' calls (two panel updates per step).
    static void Bindings(VisualElement root)
    {
        // Data bindings update when panels repaint: each step runs two frames (update, repaint).
        void Frames()
        {
            for (int i = 0; i < 2; i++)
            {
                Update();
                Runtime.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { false });
            }
        }
        Func<string> BindState = null;
        void Bind(string name, Action act) => Step("binding", name, () => { act(); Frames(); }, BindState);
        var box = new VisualElement { name = "bind-box" };
        Place(box, 300, 560, 200, 30);
        root.Add(box);
        var plain = new BindSource();
        var notifying = new BindNotifying();
        var hashed = new BindHashed();
        var labels = new List<Label>();
        Label NewLabel(string name) { var l = new Label { name = name }; labels.Add(l); return l; }
        var field = new TextField { name = "bind-field" };
        var slider = new Slider(0, 10) { name = "bind-slider" };
        string Result(VisualElement e, string id)
        {
            string ui = e.TryGetLastBindingToUIResult(id, out var r) ? r.status.ToString() : "none";
            string src = e.TryGetLastBindingToSourceResult(id, out var r2) ? r2.status.ToString() : "none";
            return ui + "/" + src;
        }
        BindState = () =>
        {
            var sb = new StringBuilder("{");
            foreach (var l in labels) sb.Append(Str(l.name)).Append(":[").Append(Str(l.text)).Append(',').Append(Str(Result(l, "text"))).Append("],");
            sb.Append("\"field\":[").Append(Str(field.value)).Append(',').Append(Str(Result(field, "value"))).Append("],");
            sb.Append("\"slider\":[").Append(F(slider.value)).Append(',').Append(Str(Result(slider, "value"))).Append("],");
            sb.Append("\"plain\":[").Append(Str(plain.message)).Append(',').Append(plain.count).Append(',').Append(F(plain.ratio)).Append("]}");
            return sb.ToString();
        };
        Binding.SetPanelLogLevel(root.panel, BindingLogLevel.None);
        var a = NewLabel("bind-plain");
        var parent = new VisualElement { name = "bind-parent" };
        var b = NewLabel("bind-hier");
        var c = NewLabel("bind-notify");
        var d = NewLabel("bind-once");
        var e2 = NewLabel("bind-conv");
        var f = NewLabel("bind-hash");
        var g = NewLabel("bind-dirty");
        var h = NewLabel("bind-nosource");
        var k = NewLabel("bind-custom");
        var m = NewLabel("bind-global");
        DataBinding once = null, dirty = null;
        Bind("attach", () =>
        {
            a.dataSource = plain;
            a.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("message"), bindingMode = BindingMode.ToTarget });
            parent.dataSource = plain;
            parent.dataSourcePath = new PropertyPath("inner");
            b.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("value") });
            parent.Add(b);
            c.dataSource = notifying;
            c.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("message") });
            d.dataSource = plain;
            once = new DataBinding { dataSourcePath = new PropertyPath("message"), bindingMode = BindingMode.ToTargetOnce };
            d.SetBinding("text", once);
            e2.dataSource = plain;
            var conv = new DataBinding { dataSourcePath = new PropertyPath("count"), bindingMode = BindingMode.ToTarget };
            conv.sourceToUiConverters.AddConverter((ref int v) => "n=" + v);
            e2.SetBinding("text", conv);
            f.dataSource = hashed;
            f.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("message") });
            g.dataSource = plain;
            dirty = new DataBinding { dataSourcePath = new PropertyPath("message"), updateTrigger = BindingUpdateTrigger.WhenDirty };
            g.SetBinding("text", dirty);
            h.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("message") });
            k.dataSource = plain;
            k.SetBinding("text", new LoggingBinding("k"));
            ConverterGroups.RegisterGlobalConverter((ref BindTag t) => "tag " + t.id);
            m.dataSource = new BindTagged();
            m.SetBinding("text", new DataBinding { dataSourcePath = new PropertyPath("tag"), bindingMode = BindingMode.ToTarget });
            field.dataSource = plain;
            field.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("message") });
            slider.dataSource = plain;
            slider.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("count") });
            box.Add(a); box.Add(parent); box.Add(c); box.Add(d); box.Add(e2); box.Add(f); box.Add(g); box.Add(h); box.Add(k); box.Add(m);
            box.Add(field); box.Add(slider);
        });
        Bind("source-changes", () =>
        {
            plain.message = "changed";
            plain.count = 5;
            plain.inner.value = "inner2";
            notifying.message = "silent";
            hashed.message = "h1";
        });
        Bind("notify-other", () => notifying.Notify("other"));
        Bind("notify-path", () => notifying.Notify("message"));
        Bind("hash-change", () => hashed.hash = 1);
        Bind("mark-dirty", () => { once.MarkDirty(); dirty.MarkDirty(); });
        Bind("ui-to-source", () => { field.value = "typed"; slider.value = 7.6f; });
        Bind("source-to-ui-again", () => plain.message = "from source");
        Bind("to-target-field", () =>
        {
            field.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("message"), bindingMode = BindingMode.ToTarget });
        });
        Bind("to-target-field-typed", () => field.value = "ignored");
        Bind("to-source-field", () =>
        {
            field.SetBinding("value", new DataBinding { dataSourcePath = new PropertyPath("message"), bindingMode = BindingMode.ToSource });
            plain.message = "kept";
        });
        Bind("to-source-field-typed", () => field.value = "written");
        Bind("give-source", () => h.dataSource = plain);
        Bind("context", () =>
        {
            var ctx = b.GetDataSourceContext("text");
            Log.Add("context " + (ctx.dataSource?.GetType().Name ?? "null") + " " + ctx.dataSourcePath);
            Log.Add("hierarchical " + b.GetHierarchicalDataSourceContext().dataSourcePath);
            Log.Add("infos " + string.Join(",", System.Linq.Enumerable.Select(field.GetBindingInfos(), i => i.bindingId.ToString())));
        });
        Bind("change-hierarchy-source", () => parent.dataSourcePath = new PropertyPath("message"));
        Bind("clear", () => { a.ClearBinding("text"); plain.message = "after clear"; Log.Add("has " + a.HasBinding("text")); });
        Bind("remove-custom", () => k.RemoveFromHierarchy());
        Bind("readd-custom", () => box.Add(k));
        // A list whose rows get their data sources automatically: each made row binds its text to its
        // item; a tree with makeItem and no bindItem shows no rows.
        var autoItems = new List<string> { "one", "two", "three" };
        ListView autoList = null;
        TreeView templateTree = null;
        Func<string> collectionState = () => "{\"list\":" + Rows(autoList) + ",\"tree\":" + Str(templateTree.GetRootElementForIndex(0) == null ? "no rows" : "rows") + "}";
        Step("binding", "autoassign-attach", () =>
        {
            autoList = new ListView { name = "bind-autolist", fixedItemHeight = 20, bindingSourceSelectionMode = BindingSourceSelectionMode.AutoAssign };
            autoList.makeItem = () =>
            {
                var l = MakeLabel("auto:");
                l.SetBinding("text", new DataBinding { bindingMode = BindingMode.ToTarget });
                return l;
            };
            autoList.itemsSource = autoItems;
            Place(autoList, 520, 560, 120, 70);
            root.Add(autoList);
            templateTree = new TreeView { name = "bind-templatetree", fixedItemHeight = 20 };
            templateTree.makeItem = () => new Label();
            templateTree.SetRootItems(new List<TreeViewItemData<string>> { new TreeViewItemData<string>(1, "a") });
            Place(templateTree, 650, 560, 100, 40);
            root.Add(templateTree);
            Frames();
        }, collectionState);
        Step("binding", "autoassign-change", () => { autoItems[1] = "TWO"; Frames(); }, collectionState);
        Step("binding", "autoassign-cleanup", () => { autoList.RemoveFromHierarchy(); templateTree.RemoveFromHierarchy(); }, () => "null");
        Step("binding", "cleanup", () => { Binding.ResetPanelLogLevel(root.panel); box.RemoveFromHierarchy(); }, () => "null");
    }

    // ---- transition animations (experimental.animation) ----

    // Animations of 1 ms with a pause before the panel update, so each completes on its first tick:
    // values, completion callbacks, recycling, KeepAlive, StyleValues read from the element, and
    // animations started on an element outside a panel.
    static void Animations(VisualElement root)
    {
        var box = new VisualElement { name = "anim-box" };
        Place(box, 8, 560, 40, 20);
        root.Add(box);
        void Wait() => System.Threading.Thread.Sleep(5);
        string Value(string tag, object v) => tag + " " + (v is float f ? F(f) : v.ToString());
        ValueAnimation<float> plain = null;
        Step("animations", "float-1ms", () =>
        {
            plain = box.experimental.animation.Start(0f, 100f, 1, (e, v) => Log.Add(Value("float", v)));
            plain.OnCompleted(() => Log.Add("float completed"));
            Log.Add("started running=" + plain.isRunning + " duration=" + plain.durationMs + " auto=" + plain.autoRecycle);
            Wait();
        }, () => Str("running=" + plain.isRunning));
        Step("animations", "recycled-start", () =>
        {
            try { plain.Start(); Log.Add("restarted"); }
            catch (Exception e) { Log.Add(e.GetType().Name + ": " + e.Message); }
        }, () => "null");
        ValueAnimation<float> kept = null;
        Step("animations", "keepalive", () =>
        {
            kept = box.experimental.animation.Start(10f, 20f, 0, (e, v) => Log.Add(Value("kept", v))).KeepAlive();
            kept.OnCompleted(() => Log.Add("kept completed"));
            Log.Add("duration=" + kept.durationMs + " auto=" + kept.autoRecycle);
            Wait();
        }, () => Str("running=" + kept.isRunning));
        Step("animations", "keepalive-restart", () => { kept.Start(); Wait(); }, () => Str("running=" + kept.isRunning));
        Step("animations", "keepalive-recycle", () => { kept.Recycle(); try { kept.Stop(); } catch (Exception e) { Log.Add(e.GetType().Name); } }, () => "null");
        Step("animations", "style-values", () =>
        {
            box.style.paddingTop = 4;
            Update();
            var to = new StyleValues { paddingTop = 20, opacity = 0.5f };
            var anim = box.experimental.animation.Start(to, 1);
            anim.OnCompleted(() => Log.Add("style completed"));
            Wait();
        }, () => Str("paddingTop=" + box.style.paddingTop + " opacity=" + box.style.opacity + " resolved=" + F(box.resolvedStyle.paddingTop)));
        Step("animations", "flexshrink-sets-grow", () =>
        {
            var values = new StyleValues { flexShrink = 3 };
            Log.Add("grow=" + F(values.flexGrow) + " shrink=" + F(values.flexShrink));
        }, () => "null");
        var detached = new VisualElement { name = "anim-detached" };
        ValueAnimation<float> outside = null;
        Step("animations", "outside-panel", () =>
        {
            outside = detached.experimental.animation.Start(1f, 2f, 1, (e, v) => Log.Add(Value("outside", v)));
            outside.OnCompleted(() => Log.Add("outside completed"));
            Wait();
        }, () => Str("running=" + outside.isRunning));
        Step("animations", "outside-added", () => { box.Add(detached); Wait(); }, () => Str("running=" + outside.isRunning));
        // The easing curves over a grid of progress values, and an animation's defaults: OutQuad
        // easing, unclamped interpolation, the duration given.
        Step("animations", "easing-curves", () =>
        {
            var curves = new (string, Func<float, float>)[] {
                ("Step", Easing.Step), ("Linear", Easing.Linear), ("InSine", Easing.InSine), ("OutSine", Easing.OutSine), ("InOutSine", Easing.InOutSine),
                ("InQuad", Easing.InQuad), ("OutQuad", Easing.OutQuad), ("InOutQuad", Easing.InOutQuad), ("InCubic", Easing.InCubic), ("OutCubic", Easing.OutCubic),
                ("InOutCubic", Easing.InOutCubic), ("InBounce", Easing.InBounce), ("OutBounce", Easing.OutBounce), ("InOutBounce", Easing.InOutBounce),
                ("InElastic", Easing.InElastic), ("OutElastic", Easing.OutElastic), ("InOutElastic", Easing.InOutElastic), ("InBack", Easing.InBack),
                ("OutBack", Easing.OutBack), ("InOutBack", Easing.InOutBack), ("InCirc", Easing.InCirc), ("OutCirc", Easing.OutCirc), ("InOutCirc", Easing.InOutCirc),
                ("InPower4", t => Easing.InPower(t, 4)), ("OutPower4", t => Easing.OutPower(t, 4)), ("InOutPower4", t => Easing.InOutPower(t, 4)),
                ("InBack3", t => Easing.InBack(t, 3f)), ("OutBack3", t => Easing.OutBack(t, 3f)), ("InOutBack3", t => Easing.InOutBack(t, 3f)) };
            float[] grid = { 0f, 0.1f, 0.25f, 0.4f, 0.5f, 0.6f, 0.75f, 0.9f, 1f };
            foreach (var (name, curve) in curves)
            {
                var line = new StringBuilder(name);
                foreach (float t in grid) line.Append(' ').Append(F(curve(t)));
                Log.Add(line.ToString());
            }
        }, () => "null");
        Step("animations", "defaults", () =>
        {
            var anim = box.experimental.animation.Start(0f, 10f, 1000, (e, v) => { });
            Log.Add("duration=" + anim.durationMs + " ease(0.5)=" + F(anim.easingCurve(0.5f)) + " lerp(1.5)=" + F(((ValueAnimation<float>)anim).interpolator(0f, 10f, 1.5f)) + " auto=" + anim.autoRecycle);
            anim.Stop();
        }, () => "null");
        Step("animations", "cleanup", () => box.RemoveFromHierarchy(), () => "null");
    }

    // ---- absolute children in a padded, bordered parent ----

    // One absolutely positioned child per case (a tab header's underline is one), in a parent with
    // borders and padding: its layout rect for insets, percentage sizes, offsets, margins, min/max,
    // no offsets under each justify/align/direction. Each parent is removed after its line.
    static void AbsoluteChildren(VisualElement root)
    {
        var cases = new (string name, Action<VisualElement, VisualElement> setup)[] {
            ("insets", (h, c) => { c.style.left = 0; c.style.right = 0; }),
            ("width-100p", (h, c) => c.style.width = Length.Percent(100)),
            ("insets-width-100p", (h, c) => { c.style.left = 0; c.style.right = 0; c.style.width = Length.Percent(100); }),
            ("static", (h, c) => c.style.width = 10),
            ("justify-center", (h, c) => { c.style.width = 10; h.style.justifyContent = Justify.Center; }),
            ("justify-end", (h, c) => { c.style.width = 10; h.style.justifyContent = Justify.FlexEnd; }),
            ("justify-space-between", (h, c) => { c.style.width = 10; h.style.justifyContent = Justify.SpaceBetween; }),
            ("justify-space-around", (h, c) => { c.style.width = 10; h.style.justifyContent = Justify.SpaceAround; }),
            ("justify-space-evenly", (h, c) => { c.style.width = 10; h.style.justifyContent = Justify.SpaceEvenly; }),
            ("align-center", (h, c) => { c.style.width = 10; h.style.alignItems = Align.Center; }),
            ("align-end", (h, c) => { c.style.width = 10; h.style.alignItems = Align.FlexEnd; }),
            ("left-10p", (h, c) => { c.style.width = 10; c.style.left = Length.Percent(10); }),
            ("top-10p", (h, c) => { c.style.width = 10; c.style.top = Length.Percent(10); }),
            ("top-50p", (h, c) => { c.style.width = 10; c.style.top = Length.Percent(50); }),
            ("right-10p", (h, c) => { c.style.width = 10; c.style.right = Length.Percent(10); }),
            ("margin-left-10p", (h, c) => { c.style.width = 10; c.style.marginLeft = Length.Percent(10); }),
            ("margin-top-10p", (h, c) => { c.style.width = 10; c.style.marginTop = Length.Percent(10); }),
            ("height-50p", (h, c) => { c.style.width = 10; c.style.height = Length.Percent(50); }),
            ("min-width-50p", (h, c) => { c.style.width = 10; c.style.minWidth = Length.Percent(50); }),
            ("max-width-10p", (h, c) => { c.style.width = 40; c.style.maxWidth = Length.Percent(10); }),
            ("row-static", (h, c) => { c.style.width = 10; h.style.flexDirection = FlexDirection.Row; }),
            ("row-justify-center", (h, c) => { c.style.width = 10; h.style.flexDirection = FlexDirection.Row; h.style.justifyContent = Justify.Center; }),
            ("row-align-end", (h, c) => { c.style.width = 10; h.style.flexDirection = FlexDirection.Row; h.style.alignItems = Align.FlexEnd; }),
            ("column-reverse", (h, c) => { c.style.width = 10; h.style.flexDirection = FlexDirection.ColumnReverse; }),
            ("row-reverse", (h, c) => { c.style.width = 10; h.style.flexDirection = FlexDirection.RowReverse; }),
        };
        foreach (var (name, setup) in cases)
        {
            Step("absolute", name, () =>
            {
                var host = new VisualElement { name = "absolute-host" };
                Place(host, 10, 300, 79, 40);
                host.style.paddingLeft = 20; host.style.paddingRight = 20; host.style.paddingTop = 4; host.style.paddingBottom = 6;
                host.style.borderLeftWidth = 3; host.style.borderTopWidth = 2;
                var child = new VisualElement { name = "absolute-child" };
                child.style.position = Position.Absolute;
                child.style.height = 2;
                setup(host, child);
                host.Add(child);
                root.Add(host);
            }, () =>
            {
                var host = root.Q("absolute-host");
                var rect = Rect(host.Q("absolute-child").layout);
                host.RemoveFromHierarchy();
                return rect;
            });
        }
    }

    // ---- TabView ----

    static string TabsState(TabView view)
    {
        return "{\"selected\":" + view.selectedTabIndex + ",\"active\":" + Str(view.activeTab?.label) + ",\"tree\":" + Tree(view) + "}";
    }

    static TabView Tabs(VisualElement root)
    {
        var view = new TabView { name = "tabs" };
        Place(view, 492, 410, 280, 140);
        for (int i = 0; i < 3; i++)
        {
            var tab = new Tab("Tab " + i) { name = "tab" + i };
            tab.Add(new Label("Content of tab " + i));
            int index = i;
            tab.selected += t => Log.Add("tabs:selected " + t.label);
            tab.closing += () => { Log.Add("tabs:closing " + index); return true; };
            tab.closed += t => Log.Add("tabs:closed " + t.label);
            if (i == 2) tab.closeable = true;
            view.Add(tab);
        }
        view.activeTabChanged += (a, b) => Log.Add("tabs:activeTabChanged " + (a?.label ?? "null") + " " + (b?.label ?? "null"));
        view.tabClosed += (t, i) => Log.Add("tabs:tabClosed " + t.label + " " + i);
        Step("tabs", "attach", () => root.Add(view), () => TabsState(view));
        return view;
    }

    static void TabsSteps(TabView view)
    {
        string State() => TabsState(view);
        VisualElement Header(int i) => view.Q<Tab>("tab" + i)?.tabHeader;
        Step("tabs", "click-tab-1", () => Click(Header(1).worldBound.center), State);
        Step("tabs", "click-tab-1-again", () => Click(Header(1).worldBound.center), State);
        Step("tabs", "selected-index-2", () => view.selectedTabIndex = 2, State);
        Step("tabs", "close-tab-2", () => Click(Header(2).Q(className: Tab.closeButtonUssClassName).worldBound.center), State);
        Step("tabs", "add-tab", () => { var t = new Tab("Late") { name = "tab3" }; t.Add(new Label("Late content")); view.Add(t); }, State);
        Step("tabs", "activate-late", () => view.activeTab = view.Q<Tab>("tab3"), State);
        Step("tabs", "remove-active", () => view.Q<Tab>("tab3").RemoveFromHierarchy(), State);
    }
}
