using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, MenuCommand's constructors and what menu commands and validators that
/// take a MenuCommand receive when the Editor runs them by path (EditorApplication.ExecuteMenuItem,
/// Menu.GetEnabled): whether they run, the command object, its context and user data, and the
/// result, with controls for a parameterless command, a validator that answers false, a validator
/// that throws, and an item of an object's context menu.
/// </summary>
static class MenuCommandProbe
{
    static readonly List<string> s_Calls = new List<string>();
    static bool s_Allow = true;

    static string Describe(MenuCommand c) =>
        c == null ? "<null>" : $"context={(c.context == null ? "<null>" : c.context.GetType().Name + ":" + c.context.name)};userData={c.userData}";

    [MenuItem("UnboundProbe/Plain")]
    static void Plain() => s_Calls.Add("Plain");

    [MenuItem("UnboundProbe/WithCommand")]
    static void WithCommand(MenuCommand command) => s_Calls.Add("WithCommand " + Describe(command));

    [MenuItem("UnboundProbe/Validated")]
    static void Validated(MenuCommand command) => s_Calls.Add("Validated " + Describe(command));

    [MenuItem("UnboundProbe/Validated", true)]
    static bool ValidateValidated(MenuCommand command)
    {
        s_Calls.Add("ValidateValidated " + Describe(command));
        return s_Allow;
    }

    [MenuItem("UnboundProbe/Throwing")]
    static void Throwing(MenuCommand command) => s_Calls.Add("Throwing " + Describe(command));

    [MenuItem("UnboundProbe/Throwing", true)]
    static bool ValidateThrowing(MenuCommand command)
    {
        s_Calls.Add("ValidateThrowing " + Describe(command));
        throw new InvalidOperationException("validator-control");
    }

    [MenuItem("CONTEXT/Transform/UnboundProbe")]
    static void TransformContext(MenuCommand command) => s_Calls.Add("TransformContext " + Describe(command));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("MENU_COMMAND_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (Exception e) { Put(key, "throws " + e.GetType().Name + ": " + e.Message); }
        }
        void Calls(string key)
        {
            Put(key + ".calls", string.Join("|", s_Calls));
            s_Calls.Clear();
        }

        var target = new GameObject("Target");
        Check("ctor.contextAndData", () => Describe(new MenuCommand(target, 7)));
        Check("ctor.context", () => Describe(new MenuCommand(target)));
        Check("ctor.null", () => Describe(new MenuCommand(null)));
        Check("ctor.negative", () => Describe(new MenuCommand(target.transform, -3)));
        Check("fields.writable", () =>
        {
            var command = new MenuCommand(null, 1) { context = target, userData = 2 };
            return Describe(command);
        });

        Check("execute.plain", () => EditorApplication.ExecuteMenuItem("UnboundProbe/Plain").ToString());
        Calls("execute.plain");
        Check("execute.withCommand", () => EditorApplication.ExecuteMenuItem("UnboundProbe/WithCommand").ToString());
        Calls("execute.withCommand");
        Check("enabled.withCommand", () => Menu.GetEnabled("UnboundProbe/WithCommand").ToString());
        Calls("enabled.withCommand");

        s_Allow = true;
        Check("enabled.validated.true", () => Menu.GetEnabled("UnboundProbe/Validated").ToString());
        Calls("enabled.validated.true");
        Check("execute.validated.true", () => EditorApplication.ExecuteMenuItem("UnboundProbe/Validated").ToString());
        Calls("execute.validated.true");
        s_Allow = false;
        Check("enabled.validated.false", () => Menu.GetEnabled("UnboundProbe/Validated").ToString());
        Calls("enabled.validated.false");
        Check("execute.validated.false", () => EditorApplication.ExecuteMenuItem("UnboundProbe/Validated").ToString());
        Calls("execute.validated.false");
        s_Allow = true;

        Check("enabled.throwing", () => Menu.GetEnabled("UnboundProbe/Throwing").ToString());
        Calls("enabled.throwing");
        Check("execute.throwing", () => EditorApplication.ExecuteMenuItem("UnboundProbe/Throwing").ToString());
        Calls("execute.throwing");

        Selection.activeGameObject = target;
        Check("execute.context", () => EditorApplication.ExecuteMenuItem("CONTEXT/Transform/UnboundProbe").ToString());
        Calls("execute.context");
        Check("enabled.context", () => Menu.GetEnabled("CONTEXT/Transform/UnboundProbe").ToString());
        Calls("enabled.context");
        Check("execute.withCommand.selected", () => EditorApplication.ExecuteMenuItem("UnboundProbe/WithCommand").ToString());
        Calls("execute.withCommand.selected");

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s ?? "<null>")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
