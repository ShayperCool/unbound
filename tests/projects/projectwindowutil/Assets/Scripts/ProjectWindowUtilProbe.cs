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
            new[] { "Assets/A 1", "Assets/A", "Assets/A/Sub" },
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

        RecordCreation(marker);

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

    // The asset-creating helpers: each runs its end action at once (no Project window); recorded
    // are what was thrown and logged, the created file (its text for text files), the object
    // loaded from it, the selection afterwards and the paths OnWillCreateAsset was called with.
    static void RecordCreation(Object marker)
    {
        Check("create.lineEndings", () => EditorSettings.lineEndingsForNewScripts.ToString());
        Create("create.asset", marker, () => ProjectWindowUtil.CreateAsset(new AnimationClip(), "Data/Clip.anim"), "Assets/Data/Clip.anim");
        Create("create.assetAgain", marker, () => ProjectWindowUtil.CreateAsset(new AnimationClip(), "Data/Clip.anim"), "Assets/Data/Clip 1.anim");
        Create("create.assetNull", marker, () => ProjectWindowUtil.CreateAsset(null, "Data/Null.anim"), "Assets/Data/Null.anim");
        Create("create.folder", marker, () => ProjectWindowUtil.CreateFolder(), "Assets/New Folder");
        Create("create.folderAgain", marker, () => ProjectWindowUtil.CreateFolder(), "Assets/New Folder 1");
        Create("create.scene", marker, () => ProjectWindowUtil.CreateScene(), "Assets/New Scene.unity");
        Create("create.withContent", marker, () => ProjectWindowUtil.CreateAssetWithContent("Data/Text.txt", "one\r\ntwo\rthree\nfour"), "Assets/Data/Text.txt");
        Create("create.template", marker, () => ProjectWindowUtil.CreateScriptAssetFromTemplateFile("Assets/Data/ScriptTemplate.txt", "Data/My Note.txt"), "Assets/Data/My Note.txt");
        Create("create.templateLower", marker, () => ProjectWindowUtil.CreateScriptAssetFromTemplateFile("Assets/Data/ScriptTemplate.txt", "Data/note two.txt"), "Assets/Data/note two.txt");
        Create("create.templateNoName", marker, () => ProjectWindowUtil.CreateScriptAssetFromTemplateFile("Assets/Data/ScriptTemplate.txt", ""), "Assets/ScriptTemplate.txt");
        Create("create.templateNull", marker, () => ProjectWindowUtil.CreateScriptAssetFromTemplateFile(null, "Data/X.txt"), "Assets/Data/X.txt");
        Create("create.templateMissing", marker, () => ProjectWindowUtil.CreateScriptAssetFromTemplateFile("Assets/Data/Missing.txt", "Data/Y.txt"), "Assets/Data/Y.txt");
        Check("create.scriptWithContent", () =>
        {
            CreationRecorder.Paths.Clear();
            CreationRecorder.Recording = true;
            Object made = null;
            var logs = Capture(() => made = ProjectWindowUtil.CreateScriptAssetWithContent("Assets/Data/Direct.txt", "a\r\nb"), out var thrown);
            CreationRecorder.Recording = false;
            return "throws=" + (thrown == null ? "<none>" : thrown.GetType().Name) + "|logs=" + logs + "|returned=" + Describe(made)
                + "|" + Created("Assets/Data/Direct.txt") + "|willCreate=" + string.Join(",", CreationRecorder.Paths);
        });
        Check("ancestors", () =>
        {
            string Paths(int id) => "[" + string.Join(",", ProjectWindowUtil.GetAncestors(id).Select(a => Text(AssetDatabase.GetAssetPath(a))).OrderBy(p => p, StringComparer.Ordinal)) + "]";
            var file = AssetDatabase.LoadMainAssetAtPath("Assets/Data/Existing.txt");
            var folder = AssetDatabase.LoadMainAssetAtPath("Assets/Data");
            return "file=" + Paths(file.GetInstanceID()) + "|folder=" + Paths(folder.GetInstanceID()) + "|scene=" + Paths(marker.GetInstanceID())
                + "|zero=" + Paths(0) + "|max=" + Paths(int.MaxValue);
        });
    }

    static void Create(string key, Object marker, Action create, string path)
    {
        Check(key, () =>
        {
            Selection.activeObject = marker;
            CreationRecorder.Paths.Clear();
            CreationRecorder.Recording = true;
            var logs = Capture(create, out var thrown);
            CreationRecorder.Recording = false;
            return "throws=" + (thrown == null ? "<none>" : thrown.GetType().Name + ":" + thrown.Message) + "|logs=" + logs + "|" + Created(path)
                + "|selection=" + Describe(Selection.activeObject) + "|willCreate=" + string.Join(",", CreationRecorder.Paths);
        });
    }

    // The file or folder at an asset path: whether it exists, a text file's text (a scene's
    // GameObject names), and the main object loaded from it.
    static string Created(string path)
    {
        string what;
        if (Directory.Exists(path)) what = "folder";
        else if (!File.Exists(path)) what = "none";
        else if (path.EndsWith(".txt", StringComparison.Ordinal)) what = "text=" + File.ReadAllText(path).Replace("\r", "\\r").Replace("\n", "\\n");
        else if (path.EndsWith(".unity", StringComparison.Ordinal))
        {
            var lines = File.ReadAllLines(path);
            // Sorted: a new scene's objects get random file IDs, which order the file.
            var names = lines.Select((l, i) => (l, i)).Where(x => x.l == "GameObject:").Select(x => lines.Skip(x.i).First(l => l.StartsWith("  m_Name: ", StringComparison.Ordinal)).Substring(10))
                .OrderBy(n => n, StringComparer.Ordinal);
            what = "scene gameObjects=" + string.Join(",", names);
        }
        else what = "file";
        return "created=" + what + "|loaded=" + Describe(AssetDatabase.LoadMainAssetAtPath(path));
    }

    static string Describe(Object o) => o == null ? "<null>" : o.GetType().Name + ":" + o.name;

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
