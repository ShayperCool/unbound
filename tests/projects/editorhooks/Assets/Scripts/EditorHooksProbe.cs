using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, the Editor hooks package editor code (Burst's loader) calls through
/// internal access: when EditorApplication.CallDelayed runs its callbacks against the tick event,
/// delayCall, update and the player loop, and with which delays, cancellation and threads; what
/// FileUtil.PathToAbsolutePath makes of project, package and other paths; and the folders
/// AssemblyHelper.GetDefaultAssemblySearchPaths lists. Paths are recorded with the project, the
/// Editor's contents and each package's folder replaced by a name.
/// </summary>
public static class EditorHooksProbe
{
    const string Variable = "EDITOR_HOOKS_OUT";
    const BindingFlags Internal = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    /// <summary>The frames whose events are recorded one by one, counted from the probe's start.</summary>
    const int RecordedFrames = 4;
    /// <summary>The delay of the delayed call that waits, in seconds.</summary>
    const double LongDelay = 0.2;
    /// <summary>Seconds after which the probe stops waiting for the long delay.</summary>
    const double Timeout = 10;

    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly List<string>[] s_Frames = Enumerable.Range(0, RecordedFrames).Select(_ => new List<string>()).ToArray();
    static int s_Start, s_MainThread, s_Ticks, s_TickFrames, s_LastTickFrame = -1;
    // CallDelayed waits on the wall clock, so the probe times it with one.
    static readonly System.Diagnostics.Stopwatch s_LongClock = new System.Diagnostics.Stopwatch();
    static double s_LongElapsed = -1;
    static string s_Thread = "<not run>";
    static bool s_Cancelled, s_Finished, s_FromUpdateRegistered;
    static Action s_RemoveTick;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        var directory = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        s_MainThread = Thread.CurrentThread.ManagedThreadId;
        s_Start = Time.frameCount;

        RecordPaths();

        new GameObject("Hooks").AddComponent<HooksFrameLogger>();
        EditorApplication.update += OnUpdate;
        s_RemoveTick = AddTick(OnTick);
        EditorApplication.delayCall += () => Log("delayCall");
        CallDelayed(() => Log("delayed0"), 0);
        CallDelayed(() => Log("delayed0b"), 0);
        var cancel = CallDelayed(() => s_Cancelled = true, 0);
        cancel();
        s_LongClock.Start();
        CallDelayed(() => s_LongElapsed = s_LongClock.Elapsed.TotalSeconds, LongDelay);
        Task.Run(() => CallDelayed(() => s_Thread = Thread.CurrentThread.ManagedThreadId == s_MainThread ? "main" : "other", 0)).Wait();
        Log("registered");
    }

    /// <summary>Adds an event to the frame it happens in (counted from the probe's start).</summary>
    public static void Log(string name)
    {
        int frame = Time.frameCount - s_Start;
        if (frame >= 0 && frame < RecordedFrames) s_Frames[frame].Add(name);
    }

    static void OnTick()
    {
        s_Ticks++;
        if (Time.frameCount != s_LastTickFrame)
        {
            s_LastTickFrame = Time.frameCount;
            s_TickFrames++;
        }
        Log("tick");
    }

    static void OnUpdate()
    {
        Log("update");
        if (!s_FromUpdateRegistered)
        {
            // Registered while the Editor's update runs: when do these run?
            s_FromUpdateRegistered = true;
            CallDelayed(() => Log("delayedFromUpdate"), 0);
            EditorApplication.delayCall += () => Log("delayCallFromUpdate");
        }
        int frame = Time.frameCount - s_Start;
        if (s_Finished || frame < RecordedFrames + 1) return;
        if (s_LongElapsed < 0 && s_LongClock.Elapsed.TotalSeconds < Timeout) return;
        s_Finished = true;
        Finish();
    }

    static Action CallDelayed(Action action, double seconds)
    {
        var method = typeof(EditorApplication).GetMethod("CallDelayed", Internal);
        var callback = new EditorApplication.CallbackFunction(action);
        return (Action)method.Invoke(null, new object[] { callback, seconds });
    }

    static Action AddTick(Action handler)
    {
        var tick = typeof(EditorApplication).GetEvent("tick", Internal);
        var callback = new EditorApplication.CallbackFunction(handler);
        tick.GetAddMethod(true).Invoke(null, new object[] { callback });
        return () => tick.GetRemoveMethod(true).Invoke(null, new object[] { callback });
    }

    static void Finish()
    {
        for (int i = 0; i < RecordedFrames; i++) Put("frame" + i, string.Join(",", s_Frames[i]));
        int frames = Time.frameCount - s_Start;
        Put("tick.everyFrame", (s_TickFrames >= frames).ToString());
        Put("tick.oncePerFrame", (s_Ticks == s_TickFrames).ToString());
        Put("delayed.long", s_LongElapsed < 0 ? "not run" : "ran after the delay=" + (s_LongElapsed >= LongDelay));
        Put("delayed.cancelled", s_Cancelled ? "ran" : "not run");
        Put("delayed.fromThread", s_Thread);
        s_RemoveTick();
        EditorApplication.update -= OnUpdate;

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static void RecordPaths()
    {
        var toAbsolute = typeof(FileUtil).GetMethod("PathToAbsolutePath", Internal);
        foreach (var path in new[]
        {
            "Assets", "Assets/", "Assets/Scripts/EditorHooksProbe.cs", "assets/scripts", "./Assets", "Assets\\Scripts",
            "Packages", "Packages/com.probe.hooks", "Packages/com.probe.hooks/Runtime/Hooks.txt", "Packages/com.unity.modules.audio",
            "Packages/com.missing.package/File.txt", "Packages/com.probe.hooks/../com.probe.hooks/Runtime",
            "Library/ScriptAssemblies", "ProjectSettings/ProjectSettings.asset", "Temp", "Other/File.txt", "../Outside",
            "/tmp/Absolute/File.txt", "",
        })
        {
            // A null path is left out: the Editor crashes on it.
            Check("absolute." + (path.Length == 0 ? "<empty>" : path), () => Mask(Invoke(toAbsolute, path)));
        }

        // The search paths end with the Unity assemblies' folder and the script assemblies; the
        // folders before them are those of the Editor's precompiled assemblies, sorted by their
        // full paths: UnityEditor.Graphs's (Managed) and each installed platform's that holds its
        // Editor extensions. Recorded as the two parts and whether the first follows that rule, so
        // the row holds on every OS and installation.
        Check("search.tail", () => string.Join("|", SearchPaths().Skip(Math.Max(0, SearchPaths().Length - 2)).Select(Mask)));
        Check("search.precompiledFolders", () =>
        {
            var paths = SearchPaths();
            var head = paths.Take(Math.Max(0, paths.Length - 2)).ToArray();
            var contents = EditorApplication.applicationContentsPath;
            var expected = new List<string>();
            if (File.Exists(Path.Combine(contents, "Managed", "UnityEditor.Graphs.dll"))) expected.Add(Path.Combine(contents, "Managed"));
            foreach (var root in new[] { Path.Combine(contents, "PlaybackEngines"), Path.GetFullPath(Path.Combine(contents, "..", "..", "PlaybackEngines")) }.Distinct())
                if (Directory.Exists(root))
                    foreach (var engine in Directory.GetDirectories(root))
                        if (Directory.GetFiles(engine, "UnityEditor.*.Extensions.dll").Length > 0) expected.Add(engine.Replace('\\', '/'));
            expected.Sort(StringComparer.Ordinal);
            return "rule=" + head.SequenceEqual(expected) + "|android=" + head.Select(Mask).Contains("\"<engine:Android>\"");
        });
    }

    static string[] SearchPaths() =>
        (string[])typeof(EditorApplication).Assembly.GetType("UnityEditor.AssemblyHelper").GetMethod("GetDefaultAssemblySearchPaths", Internal).Invoke(null, null);

    /// <summary>The standalone target of the OS the Editor runs on.</summary>
    static BuildTarget HostStandalone => Application.platform == RuntimePlatform.OSXEditor ? BuildTarget.StandaloneOSX
        : Application.platform == RuntimePlatform.WindowsEditor ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneLinux64;

    static object Invoke(MethodInfo method, string argument)
    {
        try { return method.Invoke(null, new object[] { argument }); }
        catch (TargetInvocationException e) { return "throws " + e.InnerException.GetType().Name; }
    }

    /// <summary>The path in quotes, with the package folders, the Editor's contents and the project
    /// replaced by names (longest first).</summary>
    static string Mask(object value)
    {
        if (value == null) return "<null>";
        var text = value.ToString();
        var names = new List<KeyValuePair<string, string>>();
        foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            if (!string.IsNullOrEmpty(package.resolvedPath)) names.Add(new KeyValuePair<string, string>(package.resolvedPath, "<package:" + package.name + ">"));
        names.Add(new KeyValuePair<string, string>(BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None, false), "<engine:Android>"));
        names.Add(new KeyValuePair<string, string>(BuildPipeline.GetPlaybackEngineDirectory(HostStandalone, BuildOptions.None, false), "<engine:Standalone>"));
        names.Add(new KeyValuePair<string, string>(EditorApplication.applicationContentsPath, "<EditorContents>"));
        names.Add(new KeyValuePair<string, string>(Path.GetFullPath("."), "<Project>"));
        foreach (var name in names.Where(n => !string.IsNullOrEmpty(n.Key)).OrderByDescending(n => n.Key.Length))
        {
            text = text.Replace(name.Key, name.Value);
            text = text.Replace(name.Key.Replace('\\', '/'), name.Value);
        }
        return "\"" + text + "\"";
    }

    static void Put(string key, string value) => s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        Put(key, value);
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
