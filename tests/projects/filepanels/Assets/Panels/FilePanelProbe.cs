using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in a batch-mode Editor's Play mode, what EditorUtility's file and folder panels
/// answer and log where nobody can answer a panel.
/// </summary>
static class FilePanelProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("FILE_PANELS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        Application.logMessageReceived += (condition, stackTrace, type) => s_Logs.Add(type + ":" + condition.Replace("\n", "\\n"));

        Panel("saveInProject", () => EditorUtility.SaveFilePanelInProject("Save thing", "Thing", "asset", "Where to save it"));
        Panel("saveInProject.path", () => EditorUtility.SaveFilePanelInProject("Save thing", "Thing", "asset", "Where to save it", "Assets/Panels"));
        Panel("saveInProject.nulls", () => EditorUtility.SaveFilePanelInProject(null, null, null, null));
        Panel("saveInProject.nullPath", () => EditorUtility.SaveFilePanelInProject("Save thing", "Thing", "asset", "Where", null));
        Panel("saveInProject.outside", () => EditorUtility.SaveFilePanelInProject("Save thing", "Thing", "asset", "Where", "/tmp"));
        Panel("save", () => EditorUtility.SaveFilePanel("Save file", "Assets", "File", "txt"));
        Panel("save.nulls", () => EditorUtility.SaveFilePanel(null, null, null, null));
        Panel("open", () => EditorUtility.OpenFilePanel("Open file", "Assets", "txt"));
        Panel("open.nulls", () => EditorUtility.OpenFilePanel(null, null, null));
        Panel("openFilters", () => EditorUtility.OpenFilePanelWithFilters("Open file", "Assets", new[] { "Text", "txt", "All", "*" }));
        Panel("openFolder", () => EditorUtility.OpenFolderPanel("Open folder", "Assets", "Folder"));
        Panel("saveFolder", () => EditorUtility.SaveFolderPanel("Save folder", "Assets", "Folder"));
        Panel("openFilters.nulls", () => EditorUtility.OpenFilePanelWithFilters(null, null, null));
        Panel("openFolder.nulls", () => EditorUtility.OpenFolderPanel(null, null, null));
        Panel("saveFolder.nulls", () => EditorUtility.SaveFolderPanel(null, null, null));
        Panel("saveInProject.messageOnly", () => EditorUtility.SaveFilePanelInProject(null, "Thing", "asset", "Only a message"));
        Panel("dialog", () => EditorUtility.DisplayDialog("Title", "Message", "OK", "Cancel").ToString());
        Panel("dialog.ok", () => EditorUtility.DisplayDialog("Title", "Message", "OK").ToString());
        Panel("dialog.nulls", () => EditorUtility.DisplayDialog(null, null, null, null).ToString());
        Panel("dialog.noButtons", () => EditorUtility.DisplayDialog("Title", "Message", "", null).ToString());
        Panel("dialog.noText", () => EditorUtility.DisplayDialog("", null, "OK", "Cancel").ToString());
        Panel("dialog.titleOnly", () => EditorUtility.DisplayDialog("Title", null, null, "Cancel").ToString());
        Panel("dialog.messageOnly", () => EditorUtility.DisplayDialog(null, "Message", "OK").ToString());
        Panel("openFilters.odd", () => EditorUtility.OpenFilePanelWithFilters("Open file", "Assets", new[] { "Text", "txt", "All" }));
        Panel("openFilters.empty", () => EditorUtility.OpenFilePanelWithFilters("Open file", "Assets", new string[0]));

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static void Panel(string key, Func<string> call)
    {
        s_Logs.Clear();
        string result;
        try
        {
            var answer = call();
            result = answer == null ? "<null>" : "\"" + answer + "\"";
        }
        catch (Exception e) { result = "throws " + e.GetType().Name + ":" + e.Message.Replace("\n", "\\n"); }
        var value = "result=" + result + "|logs=" + (s_Logs.Count == 0 ? "<none>" : string.Join("||", s_Logs));
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
