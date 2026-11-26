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
        yield return Logs(listener);
        yield return CoroutineThrow();
        Changes();
        Removals(listener);
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
        yield return ("persistent_void_on_int", () =>
        {
            var e = new UnityEvent<int>();
            UnityEventTools.AddVoidPersistentListener(e, listener.Throw);
            return () => e.Invoke(3);
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

    /// <summary>The message and stack text of Debug.Log called by listeners.</summary>
    static IEnumerator Logs(EventListener listener)
    {
        void Logged(string key, Action invoke)
        {
            logs.Clear();
            invoke();
            Row(key, Paths(string.Join("\n---\n", logs)));
        }
        var plain = new UnityEvent();
        UnityEventTools.AddVoidPersistentListener(plain, listener.Log);
        Logged("log.persistent_void", plain.Invoke);
        var dynamic = new UnityEvent<int>();
        UnityEventTools.AddPersistentListener(dynamic, listener.LogInt);
        Logged("log.persistent_dynamic", () => dynamic.Invoke(4));
        var voidOnInt = new UnityEvent<int>();
        UnityEventTools.AddVoidPersistentListener(voidOnInt, listener.Log);
        Logged("log.persistent_void_on_int", () => voidOnInt.Invoke(4));
        var fixedArgument = new UnityEvent();
        UnityEventTools.AddIntPersistentListener(fixedArgument, listener.LogInt, 6);
        Logged("log.persistent_int", fixedArgument.Invoke);
        var runtime = new UnityEvent<int>();
        runtime.AddListener(value => Debug.Log("From a lambda: " + value));
        Logged("log.runtime_lambda_on_int", () => runtime.Invoke(5));

        // StackTraceUtility.ExtractStackTrace asked by a listener.
        void Extracted(string key, Action invoke)
        {
            EventListener.LastStack = null;
            invoke();
            Row(key, Paths(EventListener.LastStack ?? "null"));
        }
        var extract = new UnityEvent();
        UnityEventTools.AddVoidPersistentListener(extract, listener.Extract);
        Extracted("extract.persistent_void", extract.Invoke);
        var extractDynamic = new UnityEvent<int>();
        UnityEventTools.AddPersistentListener(extractDynamic, listener.ExtractInt);
        Extracted("extract.persistent_dynamic", () => extractDynamic.Invoke(2));
        var extractFixed = new UnityEvent();
        UnityEventTools.AddIntPersistentListener(extractFixed, listener.ExtractInt, 3);
        Extracted("extract.persistent_int", extractFixed.Invoke);
        var extractVoidOnInt = new UnityEvent<int>();
        UnityEventTools.AddVoidPersistentListener(extractVoidOnInt, listener.Extract);
        Extracted("extract.persistent_void_on_int", () => extractVoidOnInt.Invoke(2));

        // Debug.LogException of an exception a listener catches: its caller lines.
        var caught = new UnityEvent<int>();
        UnityEventTools.AddVoidPersistentListener(caught, listener.LogCaught);
        Logged("log_exception_inside.persistent_void_on_int", () => caught.Invoke(1));
        yield return null;
    }

    static IEnumerator Throwing()
    {
        yield return null;
        throw new InvalidOperationException("From a coroutine.");
    }

    /// <summary>The log of an exception a coroutine throws after its first step.</summary>
    IEnumerator CoroutineThrow()
    {
        logs.Clear();
        StartCoroutine(Throwing());
        yield return null;
        yield return null;
        yield return null;
        Row("coroutine.unhandled", Paths(string.Join("\n---\n", logs)));
    }

    /// <summary>Listeners removed, cleared or turned off before an Invoke of the same event from a
    /// listener: the calls of the outer and inner Invokes (Mark listeners: persistent ones).</summary>
    static void Removals(EventListener listener)
    {
        var trace = new List<string>();
        int depth = 0;

        var removed = new UnityEvent();
        UnityAction b = () => trace.Add("B" + depth);
        removed.AddListener(() =>
        {
            trace.Add("A" + depth);
            if (depth++ == 0) { removed.RemoveListener(b); removed.Invoke(); }
        });
        removed.AddListener(b);
        removed.AddListener(() => trace.Add("C" + depth));
        removed.AddListener(() => trace.Add("D" + depth));
        removed.Invoke();
        Row("changes.nested_invoke_after_remove", string.Join(",", trace));

        trace.Clear();
        depth = 0;
        var cleared = new UnityEvent();
        cleared.AddListener(() =>
        {
            trace.Add("A" + depth);
            if (depth++ == 0) { cleared.RemoveAllListeners(); cleared.Invoke(); }
        });
        cleared.AddListener(() => trace.Add("B" + depth));
        cleared.AddListener(() => trace.Add("C" + depth));
        cleared.Invoke();
        Row("changes.nested_invoke_after_remove_all", string.Join(",", trace));

        EventListener.Marks.Clear();
        depth = 0;
        var persistent = new UnityEvent();
        UnityEventTools.AddVoidPersistentListener(persistent, listener.Mark1);
        UnityEventTools.AddVoidPersistentListener(persistent, listener.Mark2);
        UnityEventTools.AddVoidPersistentListener(persistent, listener.Mark3);
        EventListener.OnMark1 = () =>
        {
            if (depth++ != 0) return;
            persistent.SetPersistentListenerState(1, UnityEventCallState.Off);
            persistent.Invoke();
        };
        persistent.Invoke();
        EventListener.OnMark1 = null;
        Row("changes.nested_invoke_after_persistent_off", string.Join(",", EventListener.Marks));
    }
}
