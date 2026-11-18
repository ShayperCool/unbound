using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>values.jsonl rows (tests/value_probe.py) and the log messages written since the probe
/// started (before the scene loads, so that what loading the scene logs is kept too).</summary>
public static class ProbeLog
{
    public static bool Active;
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();
    static readonly List<string> events = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Begin()
    {
        var dir = Environment.GetEnvironmentVariable("GAMEINTEROP_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Active = true;
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
    }

    public static bool Open => output != null;

    public static void Event(string text) { if (Active) events.Add(text); }

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    public static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value ?? "null") + "\"}\n");

    public static void Row(string key, bool value) => Row(key, value ? "true" : "false");

    public static void Row(string key, int value) => Row(key, value.ToString());

    /// <summary>The logs and lifecycle events since the last flush, as rows of their own.</summary>
    public static void Flush(string key)
    {
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
        if (events.Count > 0) Row(key + ".events", string.Join("\n", events));
        logs.Clear();
        events.Clear();
    }

    public static string Thrown(Exception e) => "throws " + e.GetType().FullName + ": " + e.Message;

    /// <summary>What an action returns or throws, then its logs and events.</summary>
    public static void Try(string key, Func<object> action)
    {
        Flush(key + ".before");
        string result;
        try
        {
            var value = action();
            result = value is UnityEngine.Object o ? (o == null ? (ReferenceEquals(o, null) ? "returns null" : "returns Unity-null") : "returns " + o.GetType().Name + " '" + o.name + "'")
                : value == null ? "returns null" : "returns " + value;
        }
        catch (Exception e) { result = Thrown(e); }
        Row(key, result);
        Flush(key);
    }

    public static void Close()
    {
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
        output = null;
    }
}
