using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Records, in a game's OnGUI in Play mode, what an IMGUI TreeView does: its rows, row rects,
/// indents and the RowGUI arguments in Layout and Repaint; its callbacks (selection, single, double
/// and context clicks, expansion, key events, search); and scripted clicks on rows and foldouts,
/// arrow keys, Shift and Control clicks and a search, one event per frame. A second tree with a
/// two-column header records the header's height and the cells' rects.
/// </summary>
public sealed class ImguiTreeViewProbe : MonoBehaviour
{
    sealed class ProbeTree : TreeView
    {
        internal readonly List<string> Calls = new List<string>();
        internal readonly List<object> RowArgs = new List<object>();
        internal bool RecordRows;
        // The event the first row's GUI saw last (its type and mouse position in the tree's clip).
        internal object SeenEvent;

        public ProbeTree(TreeViewState state) : base(state) { }
        public ProbeTree(TreeViewState state, MultiColumnHeader header) : base(state, header) { }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem(0, -1, "Root");
            var a = new TreeViewItem(1, 0, "Alpha");
            a.AddChild(new TreeViewItem(2, 1, "Alpha One"));
            var a2 = new TreeViewItem(3, 1, "Alpha Two");
            a2.AddChild(new TreeViewItem(4, 2, "Alpha Two A"));
            a.AddChild(a2);
            root.AddChild(a);
            root.AddChild(new TreeViewItem(5, 0, "Beta"));
            var c = new TreeViewItem(6, 0, "Gamma");
            c.AddChild(new TreeViewItem(7, 1, "Gamma One"));
            root.AddChild(c);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.row == 0 && SeenEvent == null)
                SeenEvent = new object[] { Event.current.type.ToString(), Event.current.mousePosition.x > 1000 || Event.current.mousePosition.y > 1000 };
            if (RecordRows)
            {
                var cells = new List<object>();
                if (multiColumnHeader != null)
                    for (int i = 0; i < args.GetNumVisibleColumns(); i++) cells.Add(R(args.GetCellRect(i)));
                else
                    cells.Add(Try(() => args.GetNumVisibleColumns()));
                RowArgs.Add(new object[] { args.row, args.item.id, args.label, R(args.rowRect), args.selected, args.focused, args.isRenaming, cells.ToArray(), GetFoldoutIndent(args.item), GetContentIndent(args.item) });
            }
            base.RowGUI(args);
        }

        protected override void SelectionChanged(IList<int> selectedIds) => Calls.Add("SelectionChanged " + string.Join(",", selectedIds));
        protected override void SingleClickedItem(int id) => Calls.Add("SingleClickedItem " + id);
        protected override void DoubleClickedItem(int id) => Calls.Add("DoubleClickedItem " + id);
        protected override void ContextClickedItem(int id) => Calls.Add("ContextClickedItem " + id);
        protected override void ContextClicked() => Calls.Add("ContextClicked");
        protected override void ExpandedStateChanged() => Calls.Add("ExpandedStateChanged");
        protected override void SearchChanged(string newSearch) => Calls.Add("SearchChanged " + newSearch);
        protected override void KeyEvent() => Calls.Add("KeyEvent " + Event.current.type + " " + Event.current.keyCode);
        protected override bool CanMultiSelect(TreeViewItem item) => true;

        internal bool Initialized => isInitialized;
        internal object Indents() => GetRows().Select(r => (object)new object[] { r.id, GetFoldoutIndent(r), GetContentIndent(r) }).ToArray();
        internal Rect FirstRowRect() => GetRowRect(0);
        internal object RowRects() => Enumerable.Range(0, GetRows().Count).Select(i => (object)R(GetRowRect(i))).ToArray();
        internal object Props() => new object[] { rowHeight, totalHeight, baseIndent, foldoutWidth, depthIndentWidth, extraSpaceBeforeIconAndLabel, customFoldoutYOffset, cellMargin, useScrollView, showBorder, showAlternatingRowBackgrounds, isInitialized };
    }

    StringBuilder m_Output;
    string m_Directory;
    bool m_Done, m_RecordedLayout, m_RecordedRepaint;
    int m_Step = -1, m_Frames;
    ProbeTree m_Tree, m_Columns;
    // The trees span y 10-210, or 260-460 when the display's resting cursor is in the upper band:
    // a mouse event inside a clip takes the pass's real mouse position (script/*), so a tree under
    // the resting cursor would take the scripted clicks there. Nothing recorded depends on the band.
    Rect TreeRect = new Rect(10, 10, 300, 200);
    Rect ColumnsRect = new Rect(330, 10, 300, 200);
    bool m_Placed;
    // After each step the trees are not drawn for 0.25 s (from a Layout event on): a collapse or
    // expansion animates for at most 0.07 s of real time, so it ends in the first pass after the
    // hold whatever the frame rate, and the callbacks keep their order.
    bool m_Holding;
    float m_HoldUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("IMGUI_TREE_VIEW_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var go = new GameObject("ImguiTreeViewProbe");
        var probe = go.AddComponent<ImguiTreeViewProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
        probe.Setup();
    }

    void Setup()
    {
        m_Tree = new ProbeTree(new TreeViewState());
        Put("init/beforeReload", Try(() => m_Tree.Initialized), Try(() => m_Tree.GetRows()?.Count), Try(() => m_Tree.totalHeight));
        m_Tree.Reload();
        Put("init/rows", Ids(m_Tree), m_Tree.totalHeight, Try(() => m_Tree.GetExpanded().Count));
        m_Tree.SetExpanded(1, true);
        Put("init/expandA", Ids(m_Tree), m_Tree.totalHeight, m_Tree.GetExpanded().ToArray(), m_Tree.Calls.ToArray());
        m_Tree.ExpandAll();
        Put("init/expandAll", Ids(m_Tree), m_Tree.GetExpanded().ToArray());
        m_Tree.CollapseAll();
        m_Tree.SetExpanded(1, true);
        Put("init/props", m_Tree.Props());
        Put("outside/onGUI", Try(() => { m_Tree.OnGUI(TreeRect); return "ok"; }), m_Tree.Calls.ToArray());
        m_Tree.Calls.Clear();

        var columns = new[]
        {
            new MultiColumnHeaderState.Column { headerContent = new GUIContent("Name"), width = 150, minWidth = 60 },
            new MultiColumnHeaderState.Column { headerContent = new GUIContent("Value"), width = 100, minWidth = 40 },
        };
        var header = new MultiColumnHeader(new MultiColumnHeaderState(columns));
        Put("header/init", header.height, header.state.widthOfAllVisibleColumns, header.state.visibleColumns.ToArray(), header.canSort, header.sortedColumnIndex);
        m_Columns = new ProbeTree(new TreeViewState(), header);
        m_Columns.Reload();
        m_Columns.ExpandAll();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static object R(Rect r) => new object[] { r.x, r.y, r.width, r.height };
    static object Ids(TreeView tree) => tree.GetRows().Select(r => (object)r.id).ToArray();

    static object Try(Func<object> read)
    {
        try { return read(); }
        catch (Exception ex) { return "throws " + ex.GetType().Name; }
    }

    object Draw(ProbeTree tree, Rect rect, bool rows)
    {
        tree.RowArgs.Clear();
        tree.RecordRows = rows;
        bool changed = GUI.changed;
        GUI.changed = false;
        int before = GUIUtility.GetControlID(FocusType.Passive);
        object error = Try(() => { tree.OnGUI(rect); return "ok"; });
        int after = GUIUtility.GetControlID(FocusType.Passive);
        bool reported = GUI.changed;
        GUI.changed = changed;
        tree.RecordRows = false;
        return new object[] { error, after - before - 1, reported, tree.RowArgs.ToArray() };
    }

    // The game OnGUI's root clip, and groups that leave the 640-point screen in part and in
    // whole: the clip's visible rect (GUIClip is internal; a tree reads it for its row width) and
    // a tree's row rect read inside each.
    object Clips()
    {
        var clip = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
        object Read(string name) => Try(() => R((Rect)clip.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null)));
        object Here() => new object[] { Read("visibleRect"), Read("topmostRect"), R(m_Tree.FirstRowRect()) };
        var root = Here();
        GUI.BeginGroup(new Rect(600, 300, 100, 50));
        var part = Here();
        GUI.BeginGroup(new Rect(10, 60, 20, 20));
        var below = Here();
        GUI.EndGroup();
        GUI.BeginGroup(new Rect(100, 10, 20, 20));
        var edge = Here();
        GUI.EndGroup();
        GUI.BeginGroup(new Rect(-5, -5, 20, 20));
        var corner = Here();
        GUI.EndGroup();
        GUI.EndGroup();
        GUI.BeginScrollView(new Rect(10, 300, 100, 50), new Vector2(5, 7), new Rect(0, 0, 300, 300));
        var scrolled = Here();
        GUI.EndScrollView();
        GUI.BeginGroup(new Rect(700, 300, 100, 50));
        var outside = Here();
        GUI.BeginGroup(new Rect(-750, 0, 50, 20));
        var back = Here();
        GUI.EndGroup();
        GUI.EndGroup();
        return new object[] { root, part, below, edge, corner, outside, back, scrolled };
    }

    object State(ProbeTree tree)
    {
        var answer = new object[]
        {
            Ids(tree), tree.GetSelection().ToArray(), tree.GetExpanded().ToArray(), tree.HasFocus(), tree.state.lastClickedID,
            tree.searchString ?? "null", tree.Calls.ToArray(), GUIUtility.keyboardControl != 0, GUIUtility.hotControl != 0,
            tree.SeenEvent,
        };
        tree.Calls.Clear();
        return answer;
    }

    static Event Mouse(EventType type, Vector2 position, int button = 0, int clicks = 1, EventModifiers modifiers = EventModifiers.None) =>
        new Event { type = type, mousePosition = position, button = button, clickCount = clicks, modifiers = modifiers };
    static Event Key(string key) => Event.KeyboardEvent(key);

    // Rows are 16 points high from the tree rect's top; row 0 is Alpha (expanded: Alpha One,
    // Alpha Two, then Beta, Gamma).
    static Vector2 Row(int row, float x = 150) => new Vector2(10 + x, 10 + row * 16 + 8);

    // Far outside both trees: a position no real cursor has. Inside a tree's clip the Editor gives
    // the event the pass's own (real) mouse position, so a scripted mouse event cannot reach the
    // rows; the probe drives the trees by keyboard and records whether the scripted position
    // survived the clip.
    static readonly Vector2 Far = new Vector2(5000, 5000);

    (string Name, Func<Event> Make, Action Before)[] Script() => new (string, Func<Event>, Action)[]
    {
        ("mouse.far.down", () => Mouse(EventType.MouseDown, Far), null),
        ("mouse.far.up", () => Mouse(EventType.MouseUp, Far), null),
        ("focus", () => Mouse(EventType.MouseMove, Far), () => m_Tree.SetFocus()),
        ("key.down", () => Key("down"), null),
        ("key.down2", () => Key("down"), null),
        ("key.right", () => Key("right"), null),
        ("key.left", () => Key("left"), null),
        ("key.left2", () => Key("left"), null),
        ("key.up", () => Key("up"), null),
        ("key.home", () => Key("home"), null),
        ("key.end", () => Key("end"), null),
        ("key.shiftUp", () => Key("#up"), null),
        ("key.shiftUp2", () => Key("#up"), null),
        ("key.shiftDown", () => Key("#down"), null),
        ("key.ctrlDown", () => Key("^down"), null),
        ("key.pageUp", () => Key("page up"), null),
        ("key.return", () => Key("return"), null),
        ("search.set", () => Mouse(EventType.MouseMove, Far), () => m_Tree.searchString = "One"),
        ("search.down", () => Key("down"), null),
        ("search.clear", () => Mouse(EventType.MouseMove, Far), () => m_Tree.searchString = ""),
        ("selectAll", () => Key("^a"), null),
        ("columns.focus", () => Mouse(EventType.MouseMove, Far), () => m_Columns.SetFocus()),
        ("columns.down", () => Key("down"), null),
        ("columns.down2", () => Key("down"), null),
    };

    (string Name, Func<Event> Make, Action Before)[] m_Script;

    void OnGUI()
    {
        if (m_Done) return;
        var type = Event.current.type;
        if (type != EventType.Layout && type != EventType.Repaint) return;
        if (!m_Placed)
        {
            m_Placed = true;
            if (Event.current.mousePosition.y < 235)
            {
                TreeRect.y = 260;
                ColumnsRect.y = 260;
            }
        }
        if (m_Holding)
        {
            if (type != EventType.Layout || Time.realtimeSinceStartup < m_HoldUntil) return;
            m_Holding = false;
        }
        if (m_Step < 0)
        {
            string phase = type == EventType.Layout ? "layout" : "repaint";
            if (type == EventType.Layout && m_RecordedLayout) return;
            if (type == EventType.Repaint && (!m_RecordedLayout || m_RecordedRepaint)) return;
            Put($"clip/{phase}", Clips());
            Put($"draw/{phase}/tree", Draw(m_Tree, TreeRect, true));
            Put($"draw/{phase}/columns", Draw(m_Columns, ColumnsRect, true), m_Columns.multiColumnHeader.height);
            Put($"draw/{phase}/rects", m_Tree.RowRects(), m_Tree.Indents(), m_Tree.Props(), m_Tree.treeViewControlID != 0, m_Tree.HasFocus());
            Put($"draw/{phase}/state", State(m_Tree));
            if (type == EventType.Layout) m_RecordedLayout = true;
            else
            {
                m_RecordedRepaint = true;
                m_Script = Script();
                m_Step = 0;
                Hold();
            }
            return;
        }
        if (type != EventType.Repaint) { Draw(m_Tree, TreeRect, false); Draw(m_Columns, ColumnsRect, false); return; }
        if (++m_Frames % 2 != 0) { Draw(m_Tree, TreeRect, false); Draw(m_Columns, ColumnsRect, false); return; }
        var real = Event.current;
        var step = m_Script[m_Step];
        step.Before?.Invoke();
        var scripted = step.Make();
        m_Tree.SeenEvent = null;
        m_Columns.SeenEvent = null;
        Event.current = scripted;
        object a, b;
        try
        {
            a = Draw(m_Tree, TreeRect, false);
            b = Draw(m_Columns, ColumnsRect, false);
        }
        finally { Event.current = real; }
        m_Step++;
        Hold();
        Put("script/" + step.Name, ((object[])a)[0], ((object[])a)[2], State(m_Tree), ((object[])b)[0], State(m_Columns), scripted.type.ToString());
        if (m_Step == m_Script.Length)
        {
            m_Done = true;
            m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
            EditorApplication.Exit(0);
        }
    }

    void Hold()
    {
        m_Holding = true;
        m_HoldUntil = Time.realtimeSinceStartup + 0.25f;
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
