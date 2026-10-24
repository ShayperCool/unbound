using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Runs the probe when <c>JOBS_PROBE_OUT</c> names the output file: the synchronous scenarios
/// first, then the ones that span frames (on a driver object's coroutine); writes the JSON and
/// exits.
/// </summary>
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("JOBS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var driver = new GameObject("JobsProbe").AddComponent<ProbeDriver>();
        driver.Output = output;
    }
}

public sealed class ProbeDriver : MonoBehaviour
{
    public string Output;

    IEnumerator Start()
    {
        Run(JobsProbe.RunAll);
        Run(SafetyProbe.RunAll);
        Run(CollectionsProbe.RunAll);
        Run(BurstProbe.RunAll);
        Run(ProfilingProbe.RunAll);
        Run(BaselibProbe.RunAll);
        Run(SplinesProbe.RunAll);
        yield return StartCoroutine(SafetyProbe.AcrossFrames());
        yield return StartCoroutine(ProfilingProbe.AcrossFrames());
        yield return StartCoroutine(TransportProbe.Run());
        yield return StartCoroutine(LoggingProbe.Run());
        File.WriteAllText(Output, ProbeLog.Json());
        Debug.Log("jobs probe written to " + Output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Run(Action scenarios)
    {
        try
        {
            scenarios();
        }
        catch (Exception e)
        {
            ProbeLog.Begin("crash." + scenarios.Method.DeclaringType.Name);
            ProbeLog.Error(e);
            Debug.LogException(e);
        }
    }
}

/// <summary>The messages logged while it is active, from any thread (jobs log their exceptions).</summary>
public sealed class LogCapture : IDisposable
{
    readonly List<string> m_Messages = new List<string>();

    public LogCapture()
    {
        Application.logMessageReceivedThreaded += OnLog;
    }

    void OnLog(string condition, string stackTrace, LogType type)
    {
        lock (m_Messages) m_Messages.Add(type + ": " + FirstLine(condition));
    }

    static string FirstLine(string text)
    {
        int newline = text.IndexOf('\n');
        return newline < 0 ? text : text.Substring(0, newline);
    }

    public List<string> Messages
    {
        get { lock (m_Messages) return new List<string>(m_Messages); }
    }

    /// <summary>Records the count and each message under <paramref name="key"/>.</summary>
    public void Record(string key)
    {
        var messages = Messages;
        ProbeLog.Value(key + ".count", messages.Count);
        for (int i = 0; i < messages.Count; i++) ProbeLog.Value(key + "." + i, messages[i]);
    }

    public void Dispose()
    {
        Application.logMessageReceivedThreaded -= OnLog;
    }
}
