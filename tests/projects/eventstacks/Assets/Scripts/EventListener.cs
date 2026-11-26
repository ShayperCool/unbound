using System;
using UnityEngine;

/// <summary>The listeners: methods that throw, directly or in a method they call, and one that
/// counts its calls.</summary>
public class EventListener : MonoBehaviour
{
    public static int Ran;
    public int count;

    public void Throw() => throw new InvalidOperationException("From a listener.");

    public void ThrowInt(int value) => throw new ArgumentOutOfRangeException("value", value, "From a listener.");

    public void ThrowDeep() => Helper(2);

    static void Helper(int depth)
    {
        if (depth == 0) throw new NullReferenceException("Deep.");
        Helper(depth - 1);
    }

    public void Count()
    {
        count++;
        Ran++;
    }

    public void Log() => Debug.Log("From a listener.");

    public void LogInt(int value) => Debug.Log("From a listener: " + value);

    /// <summary>StackTraceUtility.ExtractStackTrace's text, asked by the last Extract listener.</summary>
    public static string LastStack;

    public void Extract() => LastStack = StackTraceUtility.ExtractStackTrace();

    public void ExtractInt(int value) => LastStack = StackTraceUtility.ExtractStackTrace();

    public void LogCaught()
    {
        try { throw new InvalidOperationException("Caught in a listener."); }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// <summary>The calls of the Mark listeners, in order; Mark1 runs <see cref="OnMark1"/> after
    /// it notes itself.</summary>
    public static readonly System.Collections.Generic.List<string> Marks = new System.Collections.Generic.List<string>();
    public static Action OnMark1;

    public void Mark1()
    {
        Marks.Add("M1");
        OnMark1?.Invoke();
    }

    public void Mark2() => Marks.Add("M2");

    public void Mark3() => Marks.Add("M3");
}
