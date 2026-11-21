using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what UnityEditor.Events.UnityEventTools does to an event's persistent
/// listeners: the stored target, method, mode, argument and state of each, what invoking the event
/// then calls, and what each call throws or logs.
/// </summary>
static class UnityEventToolsProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static EventReceiver s_Receiver, s_Other;
    static int s_Hosts;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("UNITY_EVENT_TOOLS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        s_Receiver = new GameObject("Receiver").AddComponent<EventReceiver>();
        s_Other = new GameObject("Other").AddComponent<EventReceiver>();

        Case("add.empty", h => UnityEventTools.AddPersistentListener(h.plain), h => h.plain, h => h.plain.Invoke());
        Case("add.void", h => UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping), h => h.plain, h => h.plain.Invoke());
        Case("add.int", h => UnityEventTools.AddPersistentListener(h.number, s_Receiver.TakeInt), h => h.number, h => h.number.Invoke(7));
        Case("add.pair", h => UnityEventTools.AddPersistentListener(h.pair, s_Receiver.Pair), h => h.pair, h => h.pair.Invoke(3, "x"));
        Case("add.static", h => UnityEventTools.AddPersistentListener(h.plain, EventReceiver.StaticPing), h => h.plain, h => h.plain.Invoke());
        Case("add.lambda", h => UnityEventTools.AddPersistentListener(h.plain, () => EventReceiver.Calls.Add("lambda")), h => h.plain, h => h.plain.Invoke());
        Case("add.closure", h => { int n = 1; UnityEventTools.AddPersistentListener(h.plain, () => EventReceiver.Calls.Add("closure" + n)); }, h => h.plain, h => h.plain.Invoke());
        Case("add.null", h => UnityEventTools.AddPersistentListener(h.plain, null), h => h.plain, h => h.plain.Invoke());
        Case("add.destroyedTarget", h =>
        {
            var gone = new GameObject("Gone").AddComponent<EventReceiver>();
            Object.DestroyImmediate(gone.gameObject);
            UnityEventTools.AddPersistentListener(h.plain, gone.Ping);
        }, h => h.plain, h => h.plain.Invoke());
        Case("add.voidOnInt", h => UnityEventTools.AddVoidPersistentListener(h.number, s_Receiver.Ping), h => h.number, h => h.number.Invoke(5));
        Case("add.intArgument", h => UnityEventTools.AddIntPersistentListener(h.plain, s_Receiver.TakeInt, 42), h => h.plain, h => h.plain.Invoke());
        Case("add.floatArgument", h => UnityEventTools.AddFloatPersistentListener(h.number, s_Receiver.TakeFloat, 1.5f), h => h.number, h => h.number.Invoke(9));
        Case("add.boolArgument", h => UnityEventTools.AddBoolPersistentListener(h.plain, s_Receiver.TakeBool, true), h => h.plain, h => h.plain.Invoke());
        Case("add.stringArgument", h => UnityEventTools.AddStringPersistentListener(h.plain, s_Receiver.TakeString, "hello"), h => h.plain, h => h.plain.Invoke());
        Case("add.stringNull", h => UnityEventTools.AddStringPersistentListener(h.plain, s_Receiver.TakeString, null), h => h.plain, h => h.plain.Invoke());
        Case("add.objectArgument", h => UnityEventTools.AddObjectPersistentListener<Object>(h.plain, s_Receiver.TakeObject, s_Other), h => h.plain, h => h.plain.Invoke());
        Case("add.objectTyped", h => UnityEventTools.AddObjectPersistentListener<Transform>(h.plain, s_Receiver.TakeTransform, s_Other.transform), h => h.plain, h => h.plain.Invoke());
        Case("add.objectNull", h => UnityEventTools.AddObjectPersistentListener<Object>(h.plain, s_Receiver.TakeObject, null), h => h.plain, h => h.plain.Invoke());
        Case("add.objectTypedNull", h => UnityEventTools.AddObjectPersistentListener<Transform>(h.plain, s_Receiver.TakeTransform, null), h => h.plain, h => h.plain.Invoke());
        Case("add.objectNullCall", h => UnityEventTools.AddObjectPersistentListener<Object>(h.plain, null, s_Other), h => h.plain, h => h.plain.Invoke());
        Case("add.voidNullCall", h => UnityEventTools.AddVoidPersistentListener(h.plain, null), h => h.plain, h => h.plain.Invoke());
        Case("add.voidLambda", h => UnityEventTools.AddVoidPersistentListener(h.number, () => EventReceiver.Calls.Add("voidLambda")), h => h.number, h => h.number.Invoke(1));
        Case("add.intLambda", h => UnityEventTools.AddIntPersistentListener(h.plain, x => EventReceiver.Calls.Add("intLambda" + x), 1), h => h.plain, h => h.plain.Invoke());
        Case("add.two", h =>
        {
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            UnityEventTools.AddIntPersistentListener(h.plain, s_Other.TakeInt, 2);
        }, h => h.plain, h => h.plain.Invoke());
        Case("register.replace", h =>
        {
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            UnityEventTools.RegisterPersistentListener(h.plain, 0, s_Other.Ping);
        }, h => h.plain, h => h.plain.Invoke());
        Case("register.intOverVoid", h =>
        {
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            UnityEventTools.RegisterIntPersistentListener(h.plain, 0, s_Other.TakeInt, 11);
        }, h => h.plain, h => h.plain.Invoke());
        Case("register.outOfRange", h => UnityEventTools.RegisterPersistentListener(h.plain, 3, s_Receiver.Ping), h => h.plain, h => h.plain.Invoke());
        Case("register.negative", h => UnityEventTools.RegisterVoidPersistentListener(h.plain, -1, s_Receiver.Ping), h => h.plain, h => h.plain.Invoke());
        Case("remove.index", h =>
        {
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            UnityEventTools.AddPersistentListener(h.plain, s_Other.Ping);
            UnityEventTools.RemovePersistentListener(h.plain, 0);
        }, h => h.plain, h => h.plain.Invoke());
        Case("remove.indexOutOfRange", h => UnityEventTools.RemovePersistentListener(h.plain, 2), h => h.plain, h => h.plain.Invoke());
        Case("remove.call", h =>
        {
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            UnityEventTools.AddPersistentListener(h.plain, s_Other.Ping);
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            UnityEventTools.RemovePersistentListener(h.plain, s_Receiver.Ping);
        }, h => h.plain, h => h.plain.Invoke());
        Case("remove.callWithArgument", h =>
        {
            UnityEventTools.AddIntPersistentListener(h.plain, s_Receiver.TakeInt, 1);
            UnityEventTools.RemovePersistentListener<int>(h.plain, s_Receiver.TakeInt);
        }, h => h.plain, h => h.plain.Invoke());
        Case("remove.callAbsent", h =>
        {
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            UnityEventTools.RemovePersistentListener(h.plain, s_Other.Ping);
        }, h => h.plain, h => h.plain.Invoke());
        Case("unregister", h =>
        {
            UnityEventTools.AddIntPersistentListener(h.plain, s_Receiver.TakeInt, 5);
            UnityEventTools.UnregisterPersistentListener(h.plain, 0);
        }, h => h.plain, h => h.plain.Invoke());
        Case("unregister.outOfRange", h => UnityEventTools.UnregisterPersistentListener(h.plain, 0), h => h.plain, h => h.plain.Invoke());
        Case("state.afterAdd", h =>
        {
            UnityEventTools.AddPersistentListener(h.plain, s_Receiver.Ping);
            h.plain.SetPersistentListenerState(0, UnityEventCallState.Off);
        }, h => h.plain, h => h.plain.Invoke());

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        UnityEditor.EditorApplication.Exit(0);
    }

    /// <summary>One case on a fresh host: what the edit throws and logs, the event's persistent
    /// listeners afterwards, and what invoking it calls and logs.</summary>
    static void Case(string key, Action<EventHost> edit, Func<EventHost, UnityEventBase> pick, Action<EventHost> invoke)
    {
        var host = new GameObject("Host" + (++s_Hosts)).AddComponent<EventHost>();
        var editLogs = Capture(() => edit(host), out var thrown);
        EventReceiver.Calls.Clear();
        var invokeLogs = Capture(() => invoke(host), out var invokeThrown);
        var value = string.Join("|", new[]
        {
            "throws=" + Describe(thrown),
            "logs=" + editLogs,
            "listeners=" + Listeners(pick(host)),
            "calls=" + (EventReceiver.Calls.Count == 0 ? "<none>" : string.Join(",", EventReceiver.Calls)),
            "invokeThrows=" + Describe(invokeThrown),
            "invokeLogs=" + invokeLogs,
        });
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
        Object.DestroyImmediate(host.gameObject);
    }

    static string Describe(Exception e)
    {
        if (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        if (e == null) return "<none>";
        // A lambda's method name is the compiler's choice: only that it is one is recorded.
        var message = System.Text.RegularExpressions.Regex.Replace(e.Message, @"<\w+>b__[0-9_]+", "<lambda>");
        return e.GetType().Name + ":" + message.Replace("\r", "").Replace("\n", "\\n");
    }

    static string Capture(Action action, out Exception thrown)
    {
        var logs = new List<string>();
        Application.LogCallback handler = (condition, stackTrace, type) =>
            logs.Add(type + ":" + condition.Replace("\r", "").Replace("\n", "\\n"));
        Application.logMessageReceived += handler;
        thrown = null;
        try { action(); }
        catch (Exception e) { thrown = e; }
        finally { Application.logMessageReceived -= handler; }
        return logs.Count == 0 ? "<none>" : string.Join("||", logs);
    }

    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    static object Field(object owner, string name)
    {
        for (var type = owner?.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Fields);
            if (field != null) return field.GetValue(owner);
        }
        return null;
    }

    /// <summary>Each stored persistent call: target, target type, method, mode, state and argument.</summary>
    static string Listeners(UnityEventBase unityEvent)
    {
        var calls = Field(Field(unityEvent, "m_PersistentCalls"), "m_Calls") as IList;
        if (calls == null) return "<unreadable>";
        var parts = new List<string> { "count=" + unityEvent.GetPersistentEventCount() };
        foreach (var call in calls)
        {
            var target = Field(call, "m_Target") as Object;
            var arguments = Field(call, "m_Arguments");
            var objectArgument = Field(arguments, "m_ObjectArgument") as Object;
            parts.Add(string.Join(";", new[]
            {
                "target=" + (target != null ? target.name : ReferenceEquals(Field(call, "m_Target"), null) ? "<null>" : "<destroyed>"),
                "type=" + (Field(call, "m_TargetAssemblyTypeName") ?? "<null>"),
                "method=" + (Field(call, "m_MethodName") ?? "<null>"),
                "mode=" + Field(call, "m_Mode"),
                "state=" + Field(call, "m_CallState"),
                "int=" + Field(arguments, "m_IntArgument"),
                "float=" + ((float)Field(arguments, "m_FloatArgument")).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                "string=" + (Field(arguments, "m_StringArgument") ?? "<null>"),
                "bool=" + Field(arguments, "m_BoolArgument"),
                "object=" + (objectArgument != null ? objectArgument.name : "<null>"),
                "objectType=" + (Field(arguments, "m_ObjectArgumentAssemblyTypeName") ?? "<null>"),
            }));
        }
        return string.Join("/", parts);
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
