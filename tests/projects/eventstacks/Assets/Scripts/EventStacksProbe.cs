using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Writes values.jsonl in $EVENTSTACKS_OUT (tests/value_probe.py rows): the texts game code and the
/// log see of exceptions that UnityEvent listeners throw (persistent ones added with UnityEventTools,
/// with no, a fixed and a dynamic argument, one throwing in a method it calls; runtime ones, a method
/// and a lambda), and whether the listeners after a throwing one run. The project's folder reads
/// "&lt;project&gt;".
/// </summary>
public class EventStacksProbe : MonoBehaviour
{
    const string Variable = "EVENTSTACKS_OUT";
    static StreamWriter output;
    static string project;
    static readonly List<string> logs = new List<string>();
    static Action pendingThrow;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static string Paths(string text) => text.Replace(project, "<project>");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("EventStacksProbe").AddComponent<EventStacksProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        project = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message + "\n" + stack);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        var listener = new GameObject("Listener").AddComponent<EventListener>();
        foreach (var (key, make) in Cases(listener))
        {
            Caught(key, make());
            yield return Unhandled(key, make());
        }
        Changes();
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    /// <summary>Each case's event, made again for every use, invoked by the returned action.</summary>
    static IEnumerable<(string, Func<Action>)> Cases(EventListener listener)
    {
        yield return ("persistent_void", () =>
        {
            var e = new UnityEvent();
            UnityEventTools.AddVoidPersistentListener(e, listener.Throw);
            return e.Invoke;
        });
        yield return ("persistent_int", () =>
        {
            var e = new UnityEvent();
            UnityEventTools.AddIntPersistentListener(e, listener.ThrowInt, 5);
            return e.Invoke;
        });
        yield return ("persistent_dynamic", () =>
        {
            var e = new UnityEvent<int>();
            UnityEventTools.AddPersistentListener(e, listener.ThrowInt);
            return () => e.Invoke(3);
        });
        yield return ("persistent_deep", () =>
        {
            var e = new UnityEvent();
            UnityEventTools.AddVoidPersistentListener(e, listener.ThrowDeep);
            return e.Invoke;
        });
        yield return ("persistent_then_others", () =>
        {
            var e = new UnityEvent();
            listener.count = 0;
            UnityEventTools.AddVoidPersistentListener(e, listener.Throw);
            UnityEventTools.AddVoidPersistentListener(e, listener.Count);
            e.AddListener(listener.Count);
            return e.Invoke;
        });
        yield return ("runtime_method", () =>
        {
            var e = new UnityEvent();
            e.AddListener(listener.Throw);
            return e.Invoke;
        });
        yield return ("runtime_lambda", () =>
        {
            var e = new UnityEvent();
            e.AddListener(() => throw new InvalidOperationException("From a lambda."));
            return e.Invoke;
        });
    }

    /// <summary>What game code that catches the exception sees: ExtractStringFromException's text,
    /// Debug.LogException's message and stack, and the listeners that ran after the throwing one.</summary>
    static void Caught(string key, Action invoke)
    {
        Exception thrown = null;
        EventListener.Ran = 0;
        try { invoke(); }
        catch (Exception e) { thrown = e; }
        Row(key + ".ran_after", EventListener.Ran.ToString());
        if (thrown == null) { Row(key + ".extract", "returns"); return; }
        Row(key + ".extract", Paths(StackTraceUtility.ExtractStringFromException(thrown)));
        logs.Clear();
        Debug.LogException(thrown);
        Row(key + ".log_exception", Paths(string.Join("\n---\n", logs)));
    }

    /// <summary>The log of the exception when a callback (Update) does not catch it.</summary>
    static IEnumerator Unhandled(string key, Action invoke)
    {
        logs.Clear();
        pendingThrow = invoke;
        yield return null;
        yield return null;
        Row(key + ".unhandled", Paths(string.Join("\n---\n", logs)));
    }

    void Update()
    {
        var action = pendingThrow;
        if (action == null) return;
        pendingThrow = null;
        action();
    }

    /// <summary>Listeners added or removed while the event runs, and an Invoke from a listener of
    /// the same event (each row: the calls in order).</summary>
    static void Changes()
    {
        var trace = new List<string>();
        var added = new UnityEvent();
        UnityAction late = () => trace.Add("late");
        added.AddListener(() => { trace.Add("first"); added.AddListener(late); });
        added.AddListener(() => trace.Add("second"));
        added.Invoke();
        trace.Add("|");
        added.Invoke();
        Row("changes.add_during_invoke", string.Join(",", trace));

        trace.Clear();
        var removed = new UnityEvent();
        UnityAction third = () => trace.Add("third");
        removed.AddListener(() => { trace.Add("first"); removed.RemoveListener(third); });
        removed.AddListener(() => trace.Add("second"));
        removed.AddListener(third);
        removed.Invoke();
        trace.Add("|");
        removed.Invoke();
        Row("changes.remove_during_invoke", string.Join(",", trace));

        trace.Clear();
        var nested = new UnityEvent();
        int depth = 0;
        UnityAction extra = () => trace.Add("extra" + depth);
        nested.AddListener(() =>
        {
            trace.Add("a" + depth);
            if (depth++ == 0) { nested.AddListener(extra); nested.Invoke(); }
        });
        nested.AddListener(() => trace.Add("b" + depth));
        nested.Invoke();
        Row("changes.nested_invoke_after_add", string.Join(",", trace));

        trace.Clear();
        var cleared = new UnityEvent();
        cleared.AddListener(() => { trace.Add("first"); cleared.RemoveAllListeners(); });
        cleared.AddListener(() => trace.Add("second"));
        cleared.Invoke();
        trace.Add("|");
        cleared.Invoke();
        Row("changes.remove_all_during_invoke", string.Join(",", trace));
    }
}
