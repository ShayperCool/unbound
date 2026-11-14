using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;

public sealed class ComponentLogsProbe : MonoBehaviour
{
    sealed class Observations
    {
        public string Key, TypeName;
        public readonly int[] Main = new int[5], Threaded = new int[5], Handler = new int[5];
        public readonly int[] PublicMain = new int[5], PublicThreaded = new int[5], PublicHandler = new int[5];
        public bool MainOnMainThread = true;
        public int MainThread;

        public void Add(string message, LogType type, int receiver)
        {
            bool control = message.StartsWith(Key + " public ", StringComparison.Ordinal);
            if (!control && !message.Contains(TypeName)) return;
            int[] counts = control
                ? receiver == 0 ? PublicMain : receiver == 1 ? PublicThreaded : PublicHandler
                : receiver == 0 ? Main : receiver == 1 ? Threaded : Handler;
            counts[(int)type]++;
            if (receiver == 0 && Thread.CurrentThread.ManagedThreadId != MainThread) MainOnMainThread = false;
        }
    }

    sealed class Handler : ILogHandler
    {
        readonly Observations observations;
        readonly ILogHandler next;
        public Handler(Observations observations, ILogHandler next) { this.observations = observations; this.next = next; }
        public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
        {
            observations.Add(args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args), type, 2);
            next.LogFormat(type, context, format, args);
        }
        public void LogException(Exception error, UnityEngine.Object context) { next.LogException(error, context); }
    }

    StreamWriter writer;
    void Row(string key, string value) => writer.WriteLine("{\"k\":\"" + key + "\",\"v\":" + value + "}");
    static string Array(int[] values) => "[" + string.Join(",", values) + "]";
    static string Bool(bool value) => value ? "true" : "false";

    void Snapshot(Observations observations, string stage)
    {
        string key = observations.Key + "." + stage;
        Row(key + ".main", Array(observations.Main));
        Row(key + ".threaded", Array(observations.Threaded));
        Row(key + ".handler", Array(observations.Handler));
        Row(key + ".public_main", Array(observations.PublicMain));
        Row(key + ".public_threaded", Array(observations.PublicThreaded));
        Row(key + ".public_handler", Array(observations.PublicHandler));
        Row(key + ".main_on_main_thread", Bool(observations.MainOnMainThread));
        writer.Flush();
    }

    IEnumerator Start()
    {
        string output = Environment.GetEnvironmentVariable("COMPONENT_LOGS_OUT");
        if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("COMPONENT_LOGS_OUT is required");
        Directory.CreateDirectory(output);
        writer = new StreamWriter(Path.Combine(output, "values.jsonl"));
        Row("release", "\"" + Application.unityVersion + "\"");
        var kinds = new[] { typeof(AudioLowPassFilter), typeof(AudioHighPassFilter), typeof(AudioDistortionFilter),
            typeof(AudioEchoFilter), typeof(AudioChorusFilter), typeof(AudioReverbFilter), typeof(UniqueLogComponent) };
        var quietClip = AudioClip.Create("Component log control", 1, 1, 48000, false);
        var logger = Debug.unityLogger;
        var originalHandler = logger.logHandler;
        bool originalEnabled = logger.logEnabled;
        foreach (Type kind in kinds)
        foreach (bool active in new[] { true, false })
        foreach (bool custom in new[] { false, true })
        foreach (bool logging in new[] { true, false })
        {
            string key = kind.Name + "." + (active ? "active" : "disabled")
                + "." + (custom ? "custom" : "default") + "." + (logging ? "logging" : "muted");
            var owner = new GameObject("Component log " + key);
            var source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.clip = quietClip;
            var first = (Behaviour)owner.AddComponent(kind);
            if (!first) throw new InvalidOperationException("First component was rejected: " + kind.Name);
            first.enabled = active;
            var observations = new Observations { Key = key, TypeName = kind.Name,
                MainThread = Thread.CurrentThread.ManagedThreadId };
            Application.LogCallback main = (message, trace, severity) => observations.Add(message, severity, 0);
            Application.LogCallback threaded = (message, trace, severity) => observations.Add(message, severity, 1);
            Application.logMessageReceived += main;
            Application.logMessageReceivedThreaded += threaded;
            logger.logHandler = custom ? new Handler(observations, originalHandler) : originalHandler;
            logger.logEnabled = logging;
            try
            {
                Debug.Log(key + " public Log");
                Debug.LogWarning(key + " public Warning");
                Debug.LogError(key + " public Error");
                var second = owner.AddComponent(kind);
                Row(key + ".second_null", Bool(second == null));
                Row(key + ".count", owner.GetComponents(kind).Length.ToString(CultureInfo.InvariantCulture));
                Row(key + ".first_retained", Bool(ReferenceEquals(owner.GetComponent(kind), first)));
                Snapshot(observations, "immediate");
                yield return null;
                Snapshot(observations, "next_frame");
                yield return null;
                Snapshot(observations, "second_frame");
            }
            finally
            {
                logger.logEnabled = originalEnabled;
                logger.logHandler = originalHandler;
                Application.logMessageReceived -= main;
                Application.logMessageReceivedThreaded -= threaded;
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
        UnityEngine.Object.DestroyImmediate(quietClip);
        Row("complete", "true");
        writer.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
