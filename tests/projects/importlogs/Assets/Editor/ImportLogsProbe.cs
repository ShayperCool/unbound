using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// When the messages an import logs reach the log: a USS warning (Assets/Styles/Bad.uss) and an
/// AssetPostprocessor's warning (LoggingPostprocessor, for Assets/Textures/Logged.png). A listener
/// registered when scripts load records the matching messages before Play mode, while Play mode
/// loads the two assets, and while Play mode imports new copies of them. Recorded in Play mode
/// from a project imported fresh.
/// </summary>
[InitializeOnLoad]
public static class ImportLogsProbe
{
    const string Variable = "IMPORT_LOGS_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Seen = new List<string>();
    static readonly List<string> s_BeforePlay = new List<string>();

    static ImportLogsProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (message.Contains("found 'none'") || message.Contains("importlogs-postprocess"))
                s_Seen.Add(type + ": " + message.Split('\n')[0]);
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.delayCall += Run;
        };
    }

    public static void Record()
    {
        s_BeforePlay.AddRange(s_Seen);
        s_Seen.Clear();
        EditorApplication.EnterPlaymode();
    }

    static string Taken()
    {
        var text = string.Join(" | ", s_Seen);
        s_Seen.Clear();
        return text;
    }

    static void Run()
    {
        try
        {
            // What reached the listener before Play mode (the project's first import, if it ran
            // after scripts loaded).
            Put("beforePlay", string.Join(" | ", s_BeforePlay));
            Put("enterPlay", Taken());
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Styles/Bad.uss");
            Put("load.uss.loaded", sheet != null);
            Put("load.uss", Taken());
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Logged.png");
            Put("load.texture.loaded", texture != null);
            Put("load.texture", Taken());
            // Imports in Play mode: new copies of both.
            File.Copy("Assets/Styles/Bad.uss", "Assets/Styles/BadNew.uss", true);
            AssetDatabase.ImportAsset("Assets/Styles/BadNew.uss");
            Put("import.uss", Taken());
            var copy = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Styles/BadNew.uss");
            Put("import.uss.loaded", copy != null);
            Put("import.uss.afterLoad", Taken());
            // A forced import in Play mode of one of the project's assets not loaded yet.
            AssetDatabase.ImportAsset("Assets/Styles/Forced.uss", ImportAssetOptions.ForceUpdate);
            Put("forced.uss", Taken());
            Put("forced.uss.loaded", AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Styles/Forced.uss") != null);
            Put("forced.uss.afterLoad", Taken());
            // A file a refresh in Play mode finds.
            File.Copy("Assets/Styles/Bad.uss", "Assets/Styles/Refreshed.uss", true);
            AssetDatabase.Refresh();
            Put("refresh.uss", Taken());
            Put("refresh.uss.loaded", AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Styles/Refreshed.uss") != null);
            Put("refresh.uss.afterLoad", Taken());
            File.Copy("Assets/Textures/Logged.png", "Assets/Textures/LoggedNew.png", true);
            AssetDatabase.ImportAsset("Assets/Textures/LoggedNew.png");
            Put("import.texture", Taken());
            var copiedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/LoggedNew.png");
            Put("import.texture.loaded", copiedTexture != null);
            Put("import.texture.afterLoad", Taken());
        }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); }
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
