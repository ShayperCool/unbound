using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEditor.EditorTools;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $EDITORLOAD_OUT (tests/value_probe.py rows) when Play mode starts: the
/// Editor API that packages' editor code calls while it loads, with no editor window involved:
/// drag and drop handlers, the scene tools and their events, the product GUID, the case-insensitive
/// class lookup, and which of the events subscribed here fire during a few frames of Play mode.
/// </summary>
[InitializeOnLoad]
public static class EditorLoadProbe
{
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();
    static readonly List<string> events = new List<string>();
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    static EditorLoadProbe()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && Environment.GetEnvironmentVariable("EDITORLOAD_OUT") != null) Run();
        };
    }

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<object> action)
    {
        logs.Clear();
        events.Clear();
        string result;
        try
        {
            var value = action();
            result = ReferenceEquals(value, null) ? "returns null" : "returns " + value;
        }
        catch (Exception e)
        {
            if (e is TargetInvocationException t && t.InnerException != null) e = t.InnerException;
            result = "throws " + e.GetType().FullName + ": " + e.Message;
        }
        Row(key, result);
        if (events.Count > 0) Row(key + ".events", string.Join(",", events));
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static void Do(string key, Action action) => Try(key, () => { action(); return "done"; });

    static void Run()
    {
        var dir = Environment.GetEnvironmentVariable("EDITORLOAD_OUT");
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try
        {
            Subscribe();
            DropHandlers();
            ToolState();
            Lookups();
        }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        int frames = 0;
        EditorApplication.update += Wait;

        void Wait()
        {
            if (++frames < 30) return;
            EditorApplication.update -= Wait;
            Row("later.events", string.Join(",", s_Fired));
            output.Write("{\"k\":\"complete\",\"v\":true}\n");
            output.Dispose();
            EditorApplication.Exit(0);
        }
    }

    // Events subscribed when Play mode starts: what fires in the next frames without any call here.
    static readonly List<string> s_Fired = new List<string>();

    static void Subscribe()
    {
        Do("subscribe.header", () => Editor.finishedDefaultHeaderGUI += e => s_Fired.Add("finishedDefaultHeaderGUI"));
        Do("subscribe.import_completed", () => AssetDatabase.importPackageCompleted += p => s_Fired.Add("importPackageCompleted"));
        Do("subscribe.import_cancelled", () => AssetDatabase.importPackageCancelled += p => s_Fired.Add("importPackageCancelled"));
        Do("subscribe.import_failed", () => AssetDatabase.importPackageFailed += (p, m) => s_Fired.Add("importPackageFailed"));
        Do("subscribe.compilation_started", () => CompilationPipeline.compilationStarted += o => s_Fired.Add("compilationStarted"));
        Do("subscribe.assembly_compiled", () => CompilationPipeline.assemblyCompilationFinished += (p, m) => s_Fired.Add("assemblyCompilationFinished"));
        Do("subscribe.prefab_reverted", () => PrefabUtility.prefabInstanceReverted += g => s_Fired.Add("prefabInstanceReverted"));
        Do("subscribe.require_in_build", () => UnityEditor.Analytics.AnalyticsSettings.OnRequireInBuildHandler += () => { s_Fired.Add("OnRequireInBuildHandler"); return false; });
        Do("subscribe.active_context", () => ToolManager.activeContextChanged += () => { s_Fired.Add("activeContextChanged"); events.Add("activeContextChanged"); });
        Do("subscribe.tool_events", () =>
        {
            ToolManager.activeToolChanging += () => events.Add("activeToolChanging");
            ToolManager.activeToolChanged += () => events.Add("activeToolChanged");
            Tools.pivotModeChanged += () => events.Add("pivotModeChanged");
            Tools.pivotRotationChanged += () => events.Add("pivotRotationChanged");
        });
        Do("register.build_player_handler", () => BuildPlayerWindow.RegisterBuildPlayerHandler(o => s_Fired.Add("buildPlayerHandler")));
        Do("register.build_player_handler_null", () => BuildPlayerWindow.RegisterBuildPlayerHandler(null));
    }

    static DragAndDropVisualMode Project(int id, string path, bool perform) => DragAndDropVisualMode.None;
    static DragAndDropVisualMode Scene(UnityEngine.Object o, Vector3 w, Vector2 v, Transform t, bool perform) => DragAndDropVisualMode.None;
    static DragAndDropVisualMode Hierarchy(int id, HierarchyDropFlags flags, Transform t, bool perform) => DragAndDropVisualMode.None;
    static DragAndDropVisualMode Inspector(UnityEngine.Object[] targets, bool perform) => DragAndDropVisualMode.None;

    static string Handlers()
    {
        var property = typeof(DragAndDrop).GetProperty("dropHandlers", All);
        var map = (IDictionary)property.GetValue(null);
        var parts = new List<string>();
        foreach (DictionaryEntry entry in map)
            parts.Add(entry.Key + "=" + ((ICollection)entry.Value).Count);
        parts.Sort(StringComparer.Ordinal);
        return map.Count + ":" + string.Join(",", parts);
    }

    static void DropHandlers()
    {
        Try("drop.targets", () => DragAndDropWindowTarget.projectBrowser + "," + DragAndDropWindowTarget.sceneView + "," +
                                  DragAndDropWindowTarget.hierarchy + "," + DragAndDropWindowTarget.inspector);
        Try("drop.handlers_at_start", Handlers);
        DragAndDrop.HierarchyDropHandler hierarchy = Hierarchy;
        DragAndDrop.ProjectBrowserDropHandler project = Project;
        DragAndDrop.SceneDropHandler scene = Scene;
        DragAndDrop.InspectorDropHandler inspector = Inspector;
        Try("drop.has_before", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.hierarchy, hierarchy));
        Do("drop.add_hierarchy", () => DragAndDrop.AddDropHandler(hierarchy));
        Try("drop.has_after", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.hierarchy, hierarchy));
        Try("drop.has_other_target", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.inspector, hierarchy));
        Try("drop.handlers_after_hierarchy", Handlers);
        Do("drop.add_hierarchy_again", () => DragAndDrop.AddDropHandler(hierarchy));
        Try("drop.handlers_after_again", Handlers);
        Do("drop.add_project", () => DragAndDrop.AddDropHandler(project));
        Do("drop.add_scene", () => DragAndDrop.AddDropHandler(scene));
        Do("drop.add_inspector", () => DragAndDrop.AddDropHandler(inspector));
        Try("drop.handlers_after_all", Handlers);
        Try("drop.has_project", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.projectBrowser, project));
        Try("drop.has_scene", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.sceneView, scene));
        Try("drop.has_inspector", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.inspector, inspector));
        Do("drop.remove_hierarchy", () => DragAndDrop.RemoveDropHandler(hierarchy));
        Try("drop.has_removed", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.hierarchy, hierarchy));
        Try("drop.handlers_after_remove", Handlers);
        Do("drop.remove_hierarchy_again", () => DragAndDrop.RemoveDropHandler(hierarchy));
        Do("drop.add_null", () => DragAndDrop.AddDropHandler((DragAndDrop.HierarchyDropHandler)null));
        Try("drop.handlers_after_null", Handlers);
        Try("drop.has_null", () => DragAndDrop.HasHandler(DragAndDropWindowTarget.hierarchy, null));
        Try("drop.has_unknown_target", () => DragAndDrop.HasHandler(12345, inspector));
        Do("drop.cleanup", () =>
        {
            DragAndDrop.RemoveDropHandler(project);
            DragAndDrop.RemoveDropHandler(scene);
            DragAndDrop.RemoveDropHandler(inspector);
        });
        Try("drop.handlers_end", Handlers);
    }

    // The pivot settings start from the Editor preferences of the machine: only their changes are recorded.
    static string ToolsNow() => Tools.current + "|" + (ToolManager.activeToolType?.FullName ?? "null") + "|" + (ToolManager.activeContextType?.FullName ?? "null");

    static void ToolState()
    {
        // The tool and context types read before Tools.current.
        Try("tools.types_first", () => (ToolManager.activeToolType?.FullName ?? "null") + "|" + (ToolManager.activeContextType?.FullName ?? "null"));
        Try("tools.start", ToolsNow);
        var mode = Tools.pivotMode;
        var rotation = Tools.pivotRotation;
        var tool = Tools.current;
        Do("tools.pivot_mode_other", () => Tools.pivotMode = mode == PivotMode.Center ? PivotMode.Pivot : PivotMode.Center);
        Try("tools.pivot_mode_other.state", () => (Tools.pivotMode != mode) + "|" + (Tools.pivotRotation == rotation));
        Do("tools.pivot_mode_same", () => Tools.pivotMode = Tools.pivotMode);
        Do("tools.pivot_mode_back", () => Tools.pivotMode = mode);
        Do("tools.pivot_rotation_other", () => Tools.pivotRotation = rotation == PivotRotation.Local ? PivotRotation.Global : PivotRotation.Local);
        Try("tools.pivot_rotation_other.state", () => (Tools.pivotMode == mode) + "|" + (Tools.pivotRotation != rotation));
        Do("tools.pivot_rotation_same", () => Tools.pivotRotation = Tools.pivotRotation);
        Do("tools.pivot_rotation_back", () => Tools.pivotRotation = rotation);
        Do("tools.current_rotate", () => Tools.current = Tool.Rotate);
        Try("tools.current_rotate.state", ToolsNow);
        Do("tools.current_same", () => Tools.current = Tool.Rotate);
        Do("tools.current_view", () => Tools.current = Tool.View);
        Try("tools.current_view.state", ToolsNow);
        Do("tools.current_none", () => Tools.current = Tool.None);
        Try("tools.current_none.state", ToolsNow);
        foreach (var other in new[] { Tool.Scale, Tool.Rect, Tool.Transform, Tool.Custom })
        {
            Do("tools.current_" + other, () => Tools.current = other);
            Try("tools.current_" + other + ".state", ToolsNow);
        }
        Do("tools.current_back", () => Tools.current = tool);
        Try("tools.end", ToolsNow);
    }

    static void Lookups()
    {
        Try("player.product_guid", () => PlayerSettings.productGUID.ToString());
        var unityType = typeof(Editor).Assembly.GetType("UnityEditor.UnityType");
        var find = unityType.GetMethod("FindTypeByNameCaseInsensitive", All);
        var name = unityType.GetProperty("name", All);
        var id = unityType.GetProperty("persistentTypeID", All);
        foreach (var query in new[] { "GameObject", "gameobject", "TRANSFORM", "monobehaviour", "NoSuchType", "" })
        {
            Try("unity_type.ci_" + (query.Length == 0 ? "empty" : query), () =>
            {
                var found = find.Invoke(null, new object[] { query });
                return found == null ? "null" : name.GetValue(found) + "/" + id.GetValue(found);
            });
        }
        Try("unity_type.ci_null", () => find.Invoke(null, new object[] { null }));
    }
}
