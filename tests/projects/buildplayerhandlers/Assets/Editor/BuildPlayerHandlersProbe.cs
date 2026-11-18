using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// BuildPlayerWindow.RegisterBuildPlayerHandler and RegisterGetBuildPlayerOptionsHandler when Play
/// mode starts: a first handler, a second one replacing it, null, and one after null; for each
/// call, the logs it wrote and which handler the window then keeps (read by reflection).
/// </summary>
[InitializeOnLoad]
public static class BuildPlayerHandlersProbe
{
    const string Variable = "BUILD_PLAYER_HANDLERS_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly List<string> s_Logs = new List<string>();

    static BuildPlayerHandlersProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.delayCall += Run;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Put(string key, string value) =>
        s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\"}\n");

    static object Field(string name) =>
        typeof(BuildPlayerWindow).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);

    static void Step(string key, Action call, string field, Delegate expected)
    {
        s_Logs.Clear();
        try { call(); Put(key + ".returns", "done"); }
        catch (Exception e) { Put(key + ".returns", e.GetType().FullName + ": " + e.Message); }
        Put(key + ".logs", string.Join(" | ", s_Logs));
        var kept = Field(field);
        Put(key + ".kept", kept == null ? "null" : ReferenceEquals(kept, expected) ? "this" : "other");
    }

    static void Run()
    {
        Application.logMessageReceived += (condition, stack, type) => s_Logs.Add(type + ": " + condition);
        Action<BuildPlayerOptions> first = o => { }, second = o => { }, third = o => { };
        Step("build.first", () => BuildPlayerWindow.RegisterBuildPlayerHandler(first), "buildPlayerHandler", first);
        Step("build.second", () => BuildPlayerWindow.RegisterBuildPlayerHandler(second), "buildPlayerHandler", second);
        Step("build.same", () => BuildPlayerWindow.RegisterBuildPlayerHandler(second), "buildPlayerHandler", second);
        Step("build.null", () => BuildPlayerWindow.RegisterBuildPlayerHandler(null), "buildPlayerHandler", null);
        Step("build.afterNull", () => BuildPlayerWindow.RegisterBuildPlayerHandler(third), "buildPlayerHandler", third);
        Func<BuildPlayerOptions, BuildPlayerOptions> a = o => o, b = o => o, c = o => o;
        Step("options.first", () => BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(a), "getBuildPlayerOptionsHandler", a);
        Step("options.second", () => BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(b), "getBuildPlayerOptionsHandler", b);
        Step("options.null", () => BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(null), "getBuildPlayerOptionsHandler", null);
        Step("options.afterNull", () => BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(c), "getBuildPlayerOptionsHandler", c);
        BuildPlayerWindow.RegisterBuildPlayerHandler(null);
        BuildPlayerWindow.RegisterGetBuildPlayerOptionsHandler(null);
        s_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }
}
