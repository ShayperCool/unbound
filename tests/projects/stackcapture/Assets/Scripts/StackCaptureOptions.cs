using System;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class StackCaptureOptions
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Measure(Action<string> phase, Func<Exception> capture)
    {
        var previous = Application.GetStackTraceLogType(LogType.Exception);
        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.None);
        try
        {
            phase("noneCaptured");
            Debug.LogException(capture());
            phase("noneEmpty");
            Debug.LogException(new Exception(""));
        }
        finally { Application.SetStackTraceLogType(LogType.Exception, previous); }
    }
}
