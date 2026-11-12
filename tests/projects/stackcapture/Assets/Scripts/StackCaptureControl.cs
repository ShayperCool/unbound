using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

public sealed class StackCaptureControl : MonoBehaviour
{
    static readonly List<string> Values = new List<string>();
    static string output, phase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        output = Environment.GetEnvironmentVariable("STACK_CAPTURE_OUT");
        if (output != null) new GameObject("Captured stack").AddComponent<StackCaptureControl>();
    }

    static string Q(string value) => "\"" + (value ?? "").Replace("\\", "\\\\")
        .Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";

    static string Normalize(string value) => (value ?? "").Replace('\\', '/')
        .Replace(Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/", "");

    static void Put(string key, string value) => Values.Add("{\"k\":" + Q(key) + ",\"v\":" + Q(value) + "}");

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Leaf(int count, string value)
    {
        throw new InvalidOperationException(value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Exception Capture()
    {
        try { Leaf(7, "inner failure"); }
        catch (Exception e) { return e; }
        throw new Exception("unreachable");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static Exception Wrapped()
    {
        try
        {
            try { Leaf(8, "nested failure"); }
            catch (Exception e) { throw new ArgumentException("outer failure", e); }
        }
        catch (Exception e) { return e; }
        throw new Exception("unreachable");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Start()
    {
        Application.logMessageReceived += (message, trace, type) =>
        {
            if (type != LogType.Exception || phase == null) return;
            Put(phase + ".message", Normalize(message));
            Put(phase + ".trace", Normalize(trace));
        };
        phase = "direct";
        Debug.LogException(Capture());
        phase = "wrapped";
        Debug.LogException(Wrapped());
        phase = "unthrownOuter";
        Debug.LogException(new ArgumentException("unthrown outer", Capture()));
        phase = "empty";
        Debug.LogException(new Exception(""));
        phase = "generic";
        Debug.LogException(StackGenericControl<int>.Capture(19));
        StackCaptureOptions.Measure(value => phase = value, Capture); phase = null;
        Values.Sort(StringComparer.Ordinal);
        Put("complete", "1");
        File.WriteAllLines(output, Values);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}

public sealed class StackGenericControl<T>
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception Capture(T owner)
    {
        try { Leaf(owner, "generic failure"); }
        catch (Exception e) { return e; }
        throw new Exception("unreachable");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Leaf<TValue>(T owner, TValue value)
    {
        throw new InvalidOperationException(value.ToString());
    }
}
