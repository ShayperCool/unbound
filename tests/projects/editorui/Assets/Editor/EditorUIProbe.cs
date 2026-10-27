using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

[CustomPropertyDrawer(typeof(ProbeRangeAttribute))]
public class ProbeRangeDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => 42f;
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) => EditorGUI.PropertyField(position, property, label);
}

[CustomEditor(typeof(ProbeData))]
public class ProbeDataEditor : Editor
{
    public static int Enabled, Disabled;
    void OnEnable() => Enabled++;
    void OnDisable() => Disabled++;
    public override void OnInspectorGUI() => DrawDefaultInspector();
}

/// <summary>A tree of five items: 1 (2, 3 (4)), 5; display names a..e.</summary>
public class ProbeTree : TreeView
{
    public ProbeTree(TreeViewState state) : base(state) { }
    public ProbeTree(TreeViewState state, MultiColumnHeader header) : base(state, header) { }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
        var items = new List<TreeViewItem>
        {
            new TreeViewItem { id = 1, depth = 0, displayName = "a" },
            new TreeViewItem { id = 2, depth = 1, displayName = "b" },
            new TreeViewItem { id = 3, depth = 1, displayName = "c" },
            new TreeViewItem { id = 4, depth = 2, displayName = "d" },
            new TreeViewItem { id = 5, depth = 0, displayName = "e" },
        };
        SetupParentsAndChildrenFromDepths(root, items);
        return root;
    }

    public TreeViewItem Find(int id) => FindItem(id, rootItem);
    public bool Initialized => isInitialized;
    public object[] Metrics => new object[] { rowHeight, totalHeight, showBorder, showAlternatingRowBackgrounds, depthIndentWidth, baseIndent, extraSpaceBeforeIconAndLabel, foldoutWidth };
    public IList<int> Ancestors(int id) => GetAncestors(id);
    public IList<int> WithChildren(int id) => GetDescendantsThatHaveChildren(id);
    public static void Depths(TreeViewItem root) => SetupDepthsFromParentsAndChildren(root);
}

/// <summary>
/// The Editor UI API outside any inspector or window, in Play mode, as the batch-mode Editor answers
/// it: IMGUI editor state, metrics and styles, editor field functions called outside OnGUI, editors
/// and property drawers, generic menus, tree views and their headers, drag and drop, handles, scene
/// views, curve bindings. Each call records its result, or its exception, and what it logged.
/// Writes <c>editorui.jsonl</c> into <c>EDITORUI_PROBE_OUT</c>, then quits.
/// </summary>
[InitializeOnLoad]
public static class EditorUIProbe
{
    static EditorUIProbe()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Environment.GetEnvironmentVariable("EDITORUI_PROBE_OUT") != null) Run();
        };
    }

    static readonly StringBuilder s_Out = new StringBuilder();

    static string J(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string Str(object v)
    {
        switch (v)
        {
            case null: return "null";
            case bool b: return b ? "true" : "false";
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case int or long or uint or short: return Convert.ToString(v, CultureInfo.InvariantCulture);
            case string s: return J(s);
            case Type t: return J(t.FullName);
            case Object o: return o == null ? "\"destroyed\"" : J(o.GetType().FullName + ":" + o.name);
            case GUIContent c: return "{\"text\":" + J(c.text) + ",\"tooltip\":" + J(c.tooltip) + ",\"image\":" + (c.image != null ? "true" : "false") + "}";
            case RectOffset r: return J($"{r.left},{r.right},{r.top},{r.bottom}");
            case GUIStyle g:
                return "{\"name\":" + J(g.name) + ",\"fontSize\":" + g.fontSize + ",\"fontStyle\":" + J(g.fontStyle.ToString()) + ",\"wordWrap\":" + Str(g.wordWrap) +
                       ",\"alignment\":" + J(g.alignment.ToString()) + ",\"fixedHeight\":" + Str(g.fixedHeight) + ",\"fixedWidth\":" + Str(g.fixedWidth) +
                       ",\"padding\":" + Str(g.padding) + ",\"margin\":" + Str(g.margin) + ",\"border\":" + Str(g.border) + ",\"overflow\":" + Str(g.overflow) +
                       ",\"textColor\":" + J(g.normal.textColor.ToString("F3")) + ",\"richText\":" + Str(g.richText) + ",\"clipping\":" + J(g.clipping.ToString()) +
                       ",\"stretchWidth\":" + Str(g.stretchWidth) + ",\"stretchHeight\":" + Str(g.stretchHeight) + ",\"imagePosition\":" + J(g.imagePosition.ToString()) +
                       ",\"contentOffset\":" + J(g.contentOffset.ToString("F1")) + ",\"font\":" + J(g.font == null ? null : g.font.name) + ",\"background\":" + Str(g.normal.background != null) + "}";
            case Color c: return J(c.ToString("F3"));
            case Rect r: return J(r.ToString("F1"));
            case Vector2 v2: return J(v2.ToString("F2"));
            case Vector3 v3: return J(v3.ToString("F2"));
            case Matrix4x4 m: return Str(m.isIdentity);
            case Enum e: return J(e.ToString());
            case IEnumerable e: return "[" + string.Join(",", e.Cast<object>().Select(Str)) + "]";
            default: return J(v.ToString());
        }
    }

    static void Call(string name, Func<object> f)
    {
        var logs = new List<string>();
        Application.LogCallback handler = (message, trace, type) => logs.Add(type + ": " + message.Split('\n')[0]);
        Application.logMessageReceived += handler;
        string result = "null", error = "null";
        try { result = Str(f()); }
        catch (Exception e) { error = J(e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
        finally { Application.logMessageReceived -= handler; }
        s_Out.Append("{\"case\":\"call\",\"name\":").Append(J(name)).Append(",\"result\":").Append(result).Append(",\"error\":").Append(error)
            .Append(",\"logs\":[").Append(string.Join(",", logs.Select(J))).Append("]}\n");
    }

    static void Run()
    {
        try { Probe(); }
        catch (Exception e) { s_Out.Append("{\"case\":\"error\",\"message\":").Append(J(e.ToString())).Append("}\n"); }
        var dir = Environment.GetEnvironmentVariable("EDITORUI_PROBE_OUT");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "editorui.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Probe()
    {
        var data = ScriptableObject.CreateInstance<ProbeData>();
        data.name = "Data";
        var so = new SerializedObject(data);

        // IMGUI editor state.
        Call("EditorGUI.indentLevel", () => EditorGUI.indentLevel);
        Call("EditorGUI.indentLevel=3", () => { EditorGUI.indentLevel = 3; var v = EditorGUI.indentLevel; EditorGUI.indentLevel = 0; return v; });
        Call("GUI.changed", () => GUI.changed);
        Call("EditorGUI.ChangeCheck", () => { EditorGUI.BeginChangeCheck(); return EditorGUI.EndChangeCheck(); });
        Call("EditorGUI.ChangeCheck.changed", () => { EditorGUI.BeginChangeCheck(); GUI.changed = true; var r = EditorGUI.EndChangeCheck(); var after = GUI.changed; GUI.changed = false; return new object[] { r, after }; });
        Call("EditorGUI.ChangeCheck.nested", () =>
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.BeginChangeCheck();
            GUI.changed = true;
            var inner = EditorGUI.EndChangeCheck();
            var outer = EditorGUI.EndChangeCheck();
            GUI.changed = false;
            return new object[] { inner, outer };
        });
        Call("EditorGUI.EndChangeCheck.unbalanced", () => EditorGUI.EndChangeCheck());
        Call("EditorGUI.showMixedValue", () => EditorGUI.showMixedValue);
        Call("EditorGUI.actionKey", () => EditorGUI.actionKey);
        Call("EditorGUI.DisabledScope", () => { bool inside; using (new EditorGUI.DisabledScope(true)) inside = GUI.enabled; return new object[] { inside, GUI.enabled }; });
        Call("EditorGUI.DisabledGroup", () => { EditorGUI.BeginDisabledGroup(true); var inside = GUI.enabled; EditorGUI.EndDisabledGroup(); return new object[] { inside, GUI.enabled }; });
        Call("EditorGUI.DisabledScope.nested", () =>
        {
            bool a, b;
            using (new EditorGUI.DisabledScope(true)) { using (new EditorGUI.DisabledScope(false)) a = GUI.enabled; b = GUI.enabled; }
            return new object[] { a, b, GUI.enabled };
        });
        Call("EditorGUI.IndentLevelScope", () => { int inside; using (new EditorGUI.IndentLevelScope(2)) inside = EditorGUI.indentLevel; return new object[] { inside, EditorGUI.indentLevel }; });
        Call("EditorGUI.MixedValueScope", () => { bool inside; using (new EditorGUI.MixedValueScope(true)) inside = EditorGUI.showMixedValue; return new object[] { inside, EditorGUI.showMixedValue }; });
        Call("EditorGUI.indent", () => EditorGUI.IndentedRect(new Rect(0, 0, 100, 18)));
        Call("EditorGUI.IndentedRect.2", () => { EditorGUI.indentLevel = 2; var r = EditorGUI.IndentedRect(new Rect(0, 0, 100, 18)); EditorGUI.indentLevel = 0; return r; });

        // Metrics.
        Call("EditorGUIUtility.singleLineHeight", () => EditorGUIUtility.singleLineHeight);
        Call("EditorGUIUtility.standardVerticalSpacing", () => EditorGUIUtility.standardVerticalSpacing);
        Call("EditorGUIUtility.labelWidth", () => EditorGUIUtility.labelWidth);
        Call("EditorGUIUtility.labelWidth=200", () => { EditorGUIUtility.labelWidth = 200; var v = EditorGUIUtility.labelWidth; EditorGUIUtility.labelWidth = 0; return new object[] { v, EditorGUIUtility.labelWidth }; });
        Call("EditorGUIUtility.fieldWidth", () => EditorGUIUtility.fieldWidth);
        Call("EditorGUIUtility.currentViewWidth", () => EditorGUIUtility.currentViewWidth);
        Call("EditorGUIUtility.isProSkin", () => EditorGUIUtility.isProSkin);
        Call("EditorGUIUtility.pixelsPerPoint", () => EditorGUIUtility.pixelsPerPoint);
        Call("EditorGUIUtility.wideMode", () => EditorGUIUtility.wideMode);
        Call("EditorGUIUtility.hierarchyMode", () => EditorGUIUtility.hierarchyMode);
        Call("EditorGUIUtility.editingTextField", () => EditorGUIUtility.editingTextField);
        Call("EditorGUIUtility.IconContent", () => EditorGUIUtility.IconContent("console.warnicon"));
        Call("EditorGUIUtility.IconContent.tooltip", () => EditorGUIUtility.IconContent("console.warnicon", "Tip"));
        Call("EditorGUIUtility.IconContent.missing", () => EditorGUIUtility.IconContent("no such icon here"));
        Call("EditorGUIUtility.TrTextContent", () => EditorGUIUtility.TrTextContent("Hello", "Tip"));
        Call("EditorGUIUtility.TrTempContent", () => EditorGUIUtility.TrTempContent("Hi"));
        Call("EditorGUIUtility.ObjectContent", () => EditorGUIUtility.ObjectContent(null, typeof(Material)));
        Call("EditorGUIUtility.ObjectContent.data", () => EditorGUIUtility.ObjectContent(data, typeof(ProbeData)));
        Call("EditorGUIUtility.FindTexture", () => EditorGUIUtility.FindTexture("Folder Icon") != null);
        Call("EditorGUIUtility.GetIconSize", () => EditorGUIUtility.GetIconSize());
        Call("EditorGUIUtility.whiteTexture", () => EditorGUIUtility.whiteTexture != null);
        Call("EditorGUIUtility.systemCopyBuffer", () => EditorGUIUtility.systemCopyBuffer);

        // Styles.
        foreach (var (name, style) in new (string, Func<GUIStyle>)[]
                 {
                     ("label", () => EditorStyles.label), ("boldLabel", () => EditorStyles.boldLabel), ("miniLabel", () => EditorStyles.miniLabel),
                     ("largeLabel", () => EditorStyles.largeLabel), ("miniBoldLabel", () => EditorStyles.miniBoldLabel), ("foldout", () => EditorStyles.foldout),
                     ("foldoutHeader", () => EditorStyles.foldoutHeader), ("helpBox", () => EditorStyles.helpBox), ("toolbar", () => EditorStyles.toolbar),
                     ("toolbarButton", () => EditorStyles.toolbarButton), ("toolbarSearchField", () => EditorStyles.toolbarSearchField), ("textField", () => EditorStyles.textField),
                     ("textArea", () => EditorStyles.textArea), ("numberField", () => EditorStyles.numberField), ("popup", () => EditorStyles.popup),
                     ("objectField", () => EditorStyles.objectField), ("miniButton", () => EditorStyles.miniButton), ("miniButtonLeft", () => EditorStyles.miniButtonLeft),
                     ("wordWrappedLabel", () => EditorStyles.wordWrappedLabel), ("wordWrappedMiniLabel", () => EditorStyles.wordWrappedMiniLabel),
                     ("centeredGreyMiniLabel", () => EditorStyles.centeredGreyMiniLabel), ("inspectorDefaultMargins", () => EditorStyles.inspectorDefaultMargins),
                     ("inspectorFullWidthMargins", () => EditorStyles.inspectorFullWidthMargins), ("linkLabel", () => EditorStyles.linkLabel),
                     ("toggle", () => EditorStyles.toggle), ("radioButton", () => EditorStyles.radioButton), ("selectionRect", () => EditorStyles.selectionRect),
                     ("colorField", () => EditorStyles.colorField), ("layerMaskField", () => EditorStyles.layerMaskField), ("toolbarDropDown", () => EditorStyles.toolbarDropDown),
                     ("whiteLabel", () => EditorStyles.whiteLabel),
                 })
            Call("EditorStyles." + name, () => style());
        Call("EditorStyles.standardFont", () => EditorStyles.standardFont);

        // Field functions outside OnGUI.
        Call("EditorGUILayout.IntField", () => EditorGUILayout.IntField(5));
        Call("EditorGUILayout.IntField.label", () => EditorGUILayout.IntField("Label", 5));
        Call("EditorGUILayout.TextField", () => EditorGUILayout.TextField("a"));
        Call("EditorGUILayout.Toggle", () => EditorGUILayout.Toggle(true));
        Call("EditorGUILayout.FloatField", () => EditorGUILayout.FloatField(1.5f));
        Call("EditorGUILayout.Popup", () => EditorGUILayout.Popup(1, new[] { "a", "b" }));
        Call("EditorGUILayout.EnumPopup", () => EditorGUILayout.EnumPopup(KeyCode.A));
        Call("EditorGUILayout.LabelField", () => { EditorGUILayout.LabelField("x"); return null; });
        Call("EditorGUILayout.HelpBox", () => { EditorGUILayout.HelpBox("x", MessageType.Info); return null; });
        Call("EditorGUILayout.Space", () => { EditorGUILayout.Space(); return null; });
        Call("EditorGUILayout.BeginHorizontal", () => { EditorGUILayout.BeginHorizontal(); EditorGUILayout.EndHorizontal(); return null; });
        Call("EditorGUILayout.Foldout", () => EditorGUILayout.Foldout(true, "x"));
        Call("EditorGUILayout.PropertyField", () => EditorGUILayout.PropertyField(so.FindProperty("number")));
        Call("EditorGUILayout.ObjectField", () => EditorGUILayout.ObjectField(null, typeof(Object), false));
        Call("EditorGUILayout.Slider", () => EditorGUILayout.Slider(0.5f, 0, 1));
        Call("EditorGUILayout.GetControlRect", () => EditorGUILayout.GetControlRect());
        Call("EditorGUI.IntField", () => EditorGUI.IntField(new Rect(0, 0, 100, 18), 5));
        Call("EditorGUI.PropertyField", () => EditorGUI.PropertyField(new Rect(0, 0, 100, 18), so.FindProperty("number")));
        Call("EditorGUI.LabelField", () => { EditorGUI.LabelField(new Rect(0, 0, 100, 18), "x"); return null; });
        Call("EditorGUI.Foldout", () => EditorGUI.Foldout(new Rect(0, 0, 100, 18), true, "x"));
        Call("EditorGUI.TextField", () => EditorGUI.TextField(new Rect(0, 0, 100, 18), "t"));
        Call("EditorGUI.Toggle", () => EditorGUI.Toggle(new Rect(0, 0, 100, 18), true));
        Call("EditorGUI.BeginProperty", () => { var c = EditorGUI.BeginProperty(new Rect(0, 0, 100, 18), new GUIContent("L"), so.FindProperty("number")); EditorGUI.EndProperty(); return c; });
        Call("EditorGUI.PrefixLabel", () => EditorGUI.PrefixLabel(new Rect(0, 0, 300, 18), new GUIContent("L")));
        Call("EditorGUI.DrawRect", () => { EditorGUI.DrawRect(new Rect(0, 0, 10, 10), Color.red); return null; });

        // Property heights.
        foreach (var p in new[] { "number", "text", "position", "pair", "list", "ranged", "flag", "color", "key", "rect", "bounds", "curve", "notes", "area", "slider", "afterHeader", "afterSpace", "empty", "one", "three", "pairs", "v2", "v4", "rotation", "v3i", "five", "min" })
        {
            Call("EditorGUI.GetPropertyHeight." + p, () => EditorGUI.GetPropertyHeight(so.FindProperty(p)));
            Call("EditorGUI.GetPropertyHeight.noChildren." + p, () => EditorGUI.GetPropertyHeight(so.FindProperty(p), false));
        }
        Call("EditorGUI.GetPropertyHeight.pair.expanded", () => { var pr = so.FindProperty("pair"); pr.isExpanded = true; var h = EditorGUI.GetPropertyHeight(pr); pr.isExpanded = false; return h; });
        foreach (var p in new[] { "list", "empty", "one", "three", "pairs" })
            Call("EditorGUI.GetPropertyHeight." + p + ".expanded", () => { var pr = so.FindProperty(p); pr.isExpanded = true; var h = EditorGUI.GetPropertyHeight(pr); var noChildren = EditorGUI.GetPropertyHeight(pr, false); pr.isExpanded = false; return new object[] { h, noChildren }; });
        Call("EditorGUI.GetPropertyHeight.pairs.expandedElements", () =>
        {
            var pr = so.FindProperty("pairs");
            pr.isExpanded = true;
            pr.GetArrayElementAtIndex(0).isExpanded = true;
            var h = EditorGUI.GetPropertyHeight(pr);
            pr.GetArrayElementAtIndex(0).isExpanded = false;
            pr.isExpanded = false;
            return h;
        });
        Call("EditorGUI.GetPropertyHeight.label", () => new object[] { EditorGUI.GetPropertyHeight(so.FindProperty("position"), GUIContent.none), EditorGUI.GetPropertyHeight(so.FindProperty("position"), new GUIContent("L")), EditorGUI.GetPropertyHeight(so.FindProperty("rect"), GUIContent.none) });
        Call("EditorGUI.GetPropertyHeight.wideMode", () => { EditorGUIUtility.wideMode = true; var h = new object[] { EditorGUI.GetPropertyHeight(so.FindProperty("position")), EditorGUI.GetPropertyHeight(so.FindProperty("rect")), EditorGUI.GetPropertyHeight(so.FindProperty("notes")) }; EditorGUIUtility.wideMode = false; return h; });
        foreach (var t in new[] { SerializedPropertyType.Integer, SerializedPropertyType.Vector3, SerializedPropertyType.Rect, SerializedPropertyType.Bounds, SerializedPropertyType.Vector4, SerializedPropertyType.Generic })
            Call("EditorGUI.GetPropertyHeight.type." + t, () => EditorGUI.GetPropertyHeight(t, GUIContent.none));

        // Editors.
        Editor editor = null;
        Call("Editor.CreateEditor", () => { editor = Editor.CreateEditor(data); return editor.GetType(); });
        Call("Editor.enabled", () => ProbeDataEditor.Enabled);
        Call("Editor.target", () => editor.target == data);
        Call("Editor.targets", () => editor.targets.Length);
        Call("Editor.serializedObject", () => editor.serializedObject.targetObject == data);
        Call("Editor.name", () => editor.name);
        Call("Editor.hideFlags", () => editor.hideFlags);
        Call("Editor.OnInspectorGUI", () => { editor.OnInspectorGUI(); return null; });
        Call("Editor.DestroyImmediate", () => { Object.DestroyImmediate(editor); return ProbeDataEditor.Disabled; });
        var go = new GameObject("Holder");
        Call("Editor.CreateEditor.transform", () => { var e = Editor.CreateEditor(go.transform); var t = e.GetType(); Object.DestroyImmediate(e); return t; });
        Call("Editor.CreateEditor.gameObject", () => { var e = Editor.CreateEditor(go); var t = e.GetType(); Object.DestroyImmediate(e); return t; });
        Call("Editor.CreateEditor.material", () => { var e = Editor.CreateEditor(new Material(Shader.Find("Standard"))); var t = e.GetType(); Object.DestroyImmediate(e); return t; });
        Call("Editor.CreateEditor.null", () => Editor.CreateEditor((Object)null));
        Call("Editor.CreateEditor.type", () => { var e = Editor.CreateEditor(data, typeof(ProbeDataEditor)); var t = e.GetType(); Object.DestroyImmediate(e); return t; });
        Call("Editor.CreateCachedEditor", () =>
        {
            Editor cached = null;
            Editor.CreateCachedEditor(data, null, ref cached);
            var first = cached;
            Editor.CreateCachedEditor(data, null, ref cached);
            var same = ReferenceEquals(first, cached);
            Object.DestroyImmediate(cached);
            return same;
        });
        Call("ActiveEditorTracker.activeEditors", () => ActiveEditorTracker.sharedTracker.activeEditors.Length);

        // Generic menus.
        Call("GenericMenu", () =>
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("A"), true, () => { });
            menu.AddItem(new GUIContent("Sub/B"), false, _ => { }, 3);
            menu.AddSeparator("");
            menu.AddSeparator("Sub/");
            menu.AddDisabledItem(new GUIContent("C"));
            menu.AddDisabledItem(new GUIContent("D"), true);
            return new object[] { menu.GetItemCount(), menu.allowDuplicateNames };
        });
        Call("GenericMenu.ShowAsContext", () => { var m = new GenericMenu(); m.AddItem(new GUIContent("A"), false, () => { }); m.ShowAsContext(); return null; });
        Call("GenericMenu.DropDown", () => { var m = new GenericMenu(); m.AddItem(new GUIContent("A"), false, () => { }); m.DropDown(new Rect(0, 0, 10, 10)); return null; });
        Call("EditorUtility.DisplayDialog", () => EditorUtility.DisplayDialog("t", "m", "ok"));
        Call("EditorUtility.DisplayDialog.cancel", () => EditorUtility.DisplayDialog("t", "m", "ok", "cancel"));
        Call("EditorUtility.DisplayDialogComplex", () => EditorUtility.DisplayDialogComplex("t", "m", "a", "b", "c"));

        // Tree views.
        var state = new TreeViewState();
        ProbeTree tree = null;
        Call("TreeView.new", () => { tree = new ProbeTree(state); return tree.Initialized; });
        Call("TreeView.Reload", () => { tree.Reload(); return tree.Initialized; });
        Call("TreeView.rows", () => tree.GetRows().Select(r => r.id));
        Call("TreeView.root", () => new object[] { tree.Find(0)?.displayName, tree.Find(3)?.displayName, tree.Find(3)?.depth, tree.Find(3)?.parent?.id, tree.Find(1)?.hasChildren, tree.Find(2)?.hasChildren, tree.Find(1)?.children?.Count, tree.Find(9) == null });
        Call("TreeView.SetExpanded", () => { var changed = tree.SetExpanded(1, true); return new object[] { changed, tree.GetRows().Select(r => r.id), tree.GetExpanded(), state.expandedIDs }; });
        Call("TreeView.SetExpanded.again", () => tree.SetExpanded(1, true));
        Call("TreeView.IsExpanded", () => new object[] { tree.IsExpanded(1), tree.IsExpanded(3) });
        Call("TreeView.ExpandAll", () => { tree.ExpandAll(); return new object[] { tree.GetRows().Select(r => r.id), state.expandedIDs }; });
        Call("TreeView.CollapseAll", () => { tree.CollapseAll(); return new object[] { tree.GetRows().Select(r => r.id), state.expandedIDs }; });
        Call("TreeView.SetExpandedRecursive", () => { tree.SetExpandedRecursive(1, true); return new object[] { tree.GetRows().Select(r => r.id), state.expandedIDs }; });
        Call("TreeView.SetExpanded.list", () => { tree.SetExpanded(new List<int> { 5 }); return new object[] { tree.GetRows().Select(r => r.id), state.expandedIDs }; });
        Call("TreeView.search", () => { tree.searchString = "c"; var rows = tree.GetRows().Select(r => r.id).ToList(); var has = tree.hasSearch; tree.searchString = ""; return new object[] { has, rows, tree.GetRows().Select(r => r.id) }; });
        Call("TreeView.SetSelection", () => { tree.SetSelection(new List<int> { 2, 4 }); return new object[] { tree.GetSelection(), state.selectedIDs, state.lastClickedID, tree.HasSelection(), tree.IsSelected(2) }; });
        Call("TreeView.SetSelection.reveal", () => { tree.CollapseAll(); tree.SetSelection(new List<int> { 4 }, TreeViewSelectionOptions.RevealAndFrame); return new object[] { tree.GetRows().Select(r => r.id), state.expandedIDs, state.lastClickedID }; });
        Call("TreeView.metrics", () => tree.Metrics);
        Call("TreeView.GetAncestors", () => tree.Ancestors(4));
        Call("TreeView.GetDescendantsThatHaveChildren", () => tree.WithChildren(0));
        Call("TreeView.SetupDepthsFromParentsAndChildren", () =>
        {
            var root = new TreeViewItem { id = 10, depth = 5 };
            var child = new TreeViewItem { id = 11 };
            var grand = new TreeViewItem { id = 12 };
            root.AddChild(child);
            child.AddChild(grand);
            ProbeTree.Depths(root);
            return new object[] { root.depth, child.depth, grand.depth, grand.parent.id };
        });
        Call("TreeViewItem.compare", () => { var a = new TreeViewItem(1, 0, "a"); var b = new TreeViewItem(2, 0, "b"); return new object[] { a.CompareTo(b), a.id, a.depth, a.displayName, a.icon == null, a.hasChildren, a.children == null }; });
        Call("TreeView.Reload.noRoot", () =>
        {
            var empty = new EmptyTree(new TreeViewState());
            try { empty.Reload(); return "reloaded"; }
            finally { }
        });
        Call("MultiColumnHeader", () =>
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column { headerContent = new GUIContent("Name"), width = 150, minWidth = 60, autoResize = true, canSort = true },
                new MultiColumnHeaderState.Column { headerContent = new GUIContent("Value"), width = 80 },
            };
            var hs = new MultiColumnHeaderState(columns);
            var header = new MultiColumnHeader(hs);
            var t = new ProbeTree(new TreeViewState(), header);
            return new object[] { hs.visibleColumns, hs.sortedColumnIndex, hs.widthOfAllVisibleColumns, header.height, header.canSort, header.sortedColumnIndex, columns[1].minWidth, columns[1].maxWidth, columns[1].canSort, columns[1].allowToggleVisibility, columns[0].sortedAscending, columns[1].headerTextAlignment, t.multiColumnHeader == header };
        });
        Call("MultiColumnHeader.sort", () =>
        {
            var hs = new MultiColumnHeaderState(new[] { new MultiColumnHeaderState.Column(), new MultiColumnHeaderState.Column() });
            var header = new MultiColumnHeader(hs);
            int changes = 0;
            header.sortingChanged += _ => changes++;
            header.sortedColumnIndex = 1;
            header.SetSorting(0, false);
            return new object[] { hs.sortedColumnIndex, header.IsSortedAscending(0), changes, hs.sortedColumns };
        });

        // Drag and drop.
        Call("DragAndDrop", () => new object[] { DragAndDrop.objectReferences.Length, DragAndDrop.paths.Length, DragAndDrop.visualMode, DragAndDrop.activeControlID });
        Call("DragAndDrop.prepare", () =>
        {
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = new Object[] { data };
            DragAndDrop.paths = new[] { "Assets/x" };
            DragAndDrop.SetGenericData("k", 5);
            var r = new object[] { DragAndDrop.objectReferences.Length, DragAndDrop.paths.Length, DragAndDrop.GetGenericData("k"), DragAndDrop.GetGenericData("none") };
            DragAndDrop.PrepareStartDrag();
            return r;
        });
        Call("DragAndDrop.afterPrepare", () => new object[] { DragAndDrop.objectReferences.Length, DragAndDrop.paths.Length, DragAndDrop.GetGenericData("k") });
        Call("DragAndDrop.StartDrag", () => { DragAndDrop.StartDrag("x"); return null; });
        Call("DragAndDrop.AcceptDrag", () => { DragAndDrop.AcceptDrag(); return null; });

        // Handles and scene views.
        Call("Handles.color", () => Handles.color);
        Call("Handles.matrix", () => Handles.matrix);
        Call("Handles.DrawLine", () => { Handles.DrawLine(Vector3.zero, Vector3.one); return null; });
        Call("Handles.Label", () => { Handles.Label(Vector3.zero, "x"); return null; });
        Call("Handles.PositionHandle", () => Handles.PositionHandle(Vector3.one, Quaternion.identity));
        Call("Handles.Button", () => Handles.Button(Vector3.zero, Quaternion.identity, 1, 1, Handles.CubeHandleCap));
        Call("HandleUtility.GetHandleSize", () => HandleUtility.GetHandleSize(Vector3.zero));
        Call("HandleUtility.WorldToGUIPoint", () => HandleUtility.WorldToGUIPoint(Vector3.zero));
        Call("Handles.DrawingScope", () => { Color inside; using (new Handles.DrawingScope(Color.green)) inside = Handles.color; return new object[] { inside, Handles.color }; });
        Call("SceneView", () => new object[] { SceneView.lastActiveSceneView == null, SceneView.sceneViews.Count, SceneView.currentDrawingSceneView == null });
        Call("SceneView.RepaintAll", () => { SceneView.RepaintAll(); return null; });

        // Curve bindings.
        Call("EditorCurveBinding.FloatCurve", () => { var b = EditorCurveBinding.FloatCurve("a/b", typeof(Transform), "m_LocalPosition.x"); return new object[] { b.path, b.type, b.propertyName, b.isPPtrCurve, b.isDiscreteCurve, b.isSerializeReferenceCurve }; });
        Call("EditorCurveBinding.PPtrCurve", () => { var b = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"); return new object[] { b.path, b.type, b.propertyName, b.isPPtrCurve, b.isDiscreteCurve }; });
        Call("EditorCurveBinding.DiscreteCurve", () => { var b = EditorCurveBinding.DiscreteCurve("", typeof(GameObject), "m_IsActive"); return new object[] { b.isPPtrCurve, b.isDiscreteCurve }; });
        Call("EditorCurveBinding.equals", () => EditorCurveBinding.FloatCurve("a", typeof(Transform), "x") == EditorCurveBinding.FloatCurve("a", typeof(Transform), "x"));
        var clip = new AnimationClip { name = "Clip" };
        clip.SetCurve("Child", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0, 0, 1, 2));
        clip.SetCurve("", typeof(SpriteRenderer), "m_Color.r", AnimationCurve.Constant(0, 1, 0.5f));
        clip.SetCurve("", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0, 1, 1));
        Call("AnimationUtility.GetCurveBindings", () => AnimationUtility.GetCurveBindings(clip).Select(b => b.path + "|" + b.type.Name + "|" + b.propertyName + "|" + b.isDiscreteCurve));
        Call("AnimationUtility.GetObjectReferenceCurveBindings", () => AnimationUtility.GetObjectReferenceCurveBindings(clip).Length);
        Call("AnimationUtility.GetEditorCurve", () => { var c = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x")); return c == null ? null : new object[] { c.length, c.Evaluate(0.5f) }; });
        Call("AnimationUtility.GetEditorCurve.localPosition", () => AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "localPosition.x")) != null);
        Call("AnimationUtility.SetEditorCurve", () =>
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"), AnimationCurve.Linear(0, 1, 2, 3));
            return new object[] { AnimationUtility.GetCurveBindings(clip).Length, clip.length };
        });
        Call("AnimationUtility.SetEditorCurve.remove", () =>
        {
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"), null);
            return new object[] { AnimationUtility.GetCurveBindings(clip).Length, clip.length };
        });
        var child = new GameObject("Child");
        child.transform.SetParent(go.transform);
        var grandChild = new GameObject("Grand");
        grandChild.transform.SetParent(child.transform);
        Call("AnimationUtility.CalculateTransformPath", () => new object[] { AnimationUtility.CalculateTransformPath(grandChild.transform, go.transform), AnimationUtility.CalculateTransformPath(go.transform, go.transform) });
        Call("AnimationUtility.GetAnimatedObject", () => AnimationUtility.GetAnimatedObject(go, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x")));
        Call("AnimationUtility.GetFloatValue", () => { child.transform.localPosition = new Vector3(4, 5, 6); return AnimationUtility.GetFloatValue(go, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.y"), out var v) ? (object)v : "no"; });
        Call("AnimationUtility.GetAnimatableBindings", () => AnimationUtility.GetAnimatableBindings(child, go).Length > 0);
        Call("AnimationUtility.GetEditorCurveValueType", () => AnimationUtility.GetEditorCurveValueType(go, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.x")));
        Call("AnimationUtility.GetAnimationClipSettings", () => { var s = AnimationUtility.GetAnimationClipSettings(clip); return new object[] { s.loopTime, s.startTime, s.stopTime, s.cycleOffset, s.mirror }; });
        Call("AnimationUtility.GetAnimationEvents", () => AnimationUtility.GetAnimationEvents(clip).Length);
        Call("AnimationUtility.SetAnimationEvents", () => { AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = 0.5f, functionName = "F" } }); return new object[] { AnimationUtility.GetAnimationEvents(clip).Length, clip.events.Length }; });

        // Prefab utility events.
        Call("PrefabUtility.prefabInstanceUpdated", () => { PrefabUtility.prefabInstanceUpdated += _ => { }; return true; });
    }

    class EmptyTree : TreeView
    {
        public EmptyTree(TreeViewState s) : base(s) { }
        protected override TreeViewItem BuildRoot() => new TreeViewItem { id = 0, depth = -1 };
    }
}
