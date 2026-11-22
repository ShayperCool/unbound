using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode (no Project window is open), what ProjectWindowUtil does: how
/// StartNameEditingIfProjectWindowExists runs its end action (instance ID, path, resource file),
/// cleans it up and sets the selection, and what its path helpers answer.
/// </summary>
static class ProjectWindowUtilProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    const BindingFlags Internal = BindingFlags.Static | BindingFlags.NonPublic;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("PROJECT_WINDOW_UTIL_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);

        var marker = new GameObject("Marker");
        var target = new GameObject("Target");
        Check("action.created", () => "hideFlags=" + ScriptableObject.CreateInstance<RecordingNameEditAction>().hideFlags);
        Edit("edit.zeroId", 0, "New Thing.asset", "resource.txt", null, marker);
        Edit("edit.lowercasePrefix", 0, "assets/Lower.asset", null, null, marker);
        Edit("edit.existingPath", 0, "Assets/Data/Existing.txt", null, null, marker);
        Edit("edit.nested", 0, "Data/Inner.asset", null, null, marker);
        Edit("edit.liveObject", target.GetInstanceID(), "Assets/Made.asset", null, null, marker, liveName: "Target");
        Edit("edit.noSelection", target.GetInstanceID(), "Assets/Made.asset", null, false, marker, liveName: "Target");
        Check("edit.nullAction", () =>
        {
            Selection.activeObject = marker;
            var logs = Capture(() => ProjectWindowUtil.StartNameEditingIfProjectWindowExists(target.GetInstanceID(), null, "Assets/None.asset", null, null), out var thrown);
            return "throws=" + (thrown == null ? "<none>" : thrown.GetType().Name) + "|logs=" + logs + "|selection=" + Name(Selection.activeObject);
        });

        Check("folder.active", () => Text(typeof(ProjectWindowUtil).GetMethod("GetActiveFolderPath", Internal).Invoke(null, null)));
        Check("folder.tryActive", () =>
        {
            var args = new object[] { null };
            var found = typeof(ProjectWindowUtil).GetMethod("TryGetActiveFolderPath", Internal).Invoke(null, args);
            return found + "|" + Text(args[0]);
        });
        Check("folder.containing", () => string.Join("|", new[] { "Assets/A/b.png", "Assets/A/", "/Assets/A/b/", "Assets", "", null, "a//b", "Assets\\A\\b.png" }
            .Select(p => Text(p) + "=>" + Text(ProjectWindowUtil.GetContainingFolder(p)))));
        Check("folder.base", () => string.Join("|", new[]
        {
            new[] { "Assets/B", "Assets/A", "Assets/A/Sub", "Assets/AB" },
            new[] { "/Assets/X/", "Assets/X/Y/" },
            new[] { "Only/" },
            new string[0],
        }.Select(set => "[" + string.Join(",", ProjectWindowUtil.GetBaseFolders(set).Select(Text)) + "]")));
        Check("folder.isFolder", () =>
        {
            var folder = AssetDatabase.LoadMainAssetAtPath("Assets/Data");
            var file = AssetDatabase.LoadMainAssetAtPath("Assets/Data/Existing.txt");
            return "folder=" + ProjectWindowUtil.IsFolder(folder.GetInstanceID()) + "|file=" + ProjectWindowUtil.IsFolder(file.GetInstanceID())
                + "|scene=" + ProjectWindowUtil.IsFolder(marker.GetInstanceID()) + "|none=" + ProjectWindowUtil.IsFolder(0);
        });
        Check("show.created", () =>
        {
            Selection.activeObject = marker;
            ProjectWindowUtil.ShowCreatedAsset(target);
            var shown = Name(Selection.activeObject);
            ProjectWindowUtil.ShowCreatedAsset(null);
            return "object=" + shown + "|null=" + Name(Selection.activeObject);
        });

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static void Edit(string key, int instanceId, string path, string resource, bool? select, Object before, string liveName = null)
    {
        Check(key, () =>
        {
            RecordingNameEditAction.Calls.Clear();
            Selection.activeObject = before;
            var action = ScriptableObject.CreateInstance<RecordingNameEditAction>();
            var logs = Capture(() =>
            {
                if (select.HasValue) ProjectWindowUtil.StartNameEditingIfProjectWindowExists(instanceId, action, path, null, resource, select.Value);
                else ProjectWindowUtil.StartNameEditingIfProjectWindowExists(instanceId, action, path, null, resource);
            }, out var thrown);
            return string.Join("|", new[]
            {
                "throws=" + (thrown == null ? "<none>" : thrown.GetType().Name), "logs=" + logs,
                // A live object's instance ID is the session's own: only which object it was is kept.
                "calls=" + string.Join(",", RecordingNameEditAction.Calls).Replace("(" + instanceId + ",", instanceId == 0 ? "(" + instanceId + "," : "(<" + liveName + ">,"),
                "actionAlive=" + (action != null), "selection=" + Name(Selection.activeObject),
            });
        });
    }

    static string Name(Object o) => o == null ? "<null>" : o.name;

    static string Text(object value) => value == null ? "<null>" : "\"" + value + "\"";

    static string Capture(Action action, out Exception thrown)
    {
        var logs = new System.Collections.Generic.List<string>();
        Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition.Replace("\n", "\\n"));
        Application.logMessageReceived += handler;
        thrown = null;
        try { action(); }
        catch (Exception e) { thrown = e; }
        finally { Application.logMessageReceived -= handler; }
        return logs.Count == 0 ? "<none>" : string.Join("||", logs);
    }

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
