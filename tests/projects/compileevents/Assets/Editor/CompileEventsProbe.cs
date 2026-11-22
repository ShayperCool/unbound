using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// What the compilation pipeline raises when a script changes in Play mode (an assembly of its own,
/// Assets/Other, does not depend on the changed script): the probe rewrites
/// Assets/Scripts/Changing.cs three times (a new value, a compile error, the fix), asks for a refresh,
/// then asks for a compilation (both RequestScriptCompilation overloads) and sets the scripting
/// defines (the active target's, another target's, the active target's again unchanged),
/// and logs every compilation and assembly reload event to a file in $COMPILE_EVENTS_OUT (it
/// survives a domain reload); values.jsonl summarizes each round.
/// </summary>
[InitializeOnLoad]
public static class CompileEventsProbe
{
    const string Variable = "COMPILE_EVENTS_OUT";
    const int Settle = 30, Timeout = 3000;
    /// <summary>How long a request round waits for its compilation (they run asynchronously).</summary>
    const double RequestSeconds = 20;
    static string Out => Environment.GetEnvironmentVariable(Variable);
    static string EventLog => Path.Combine(Out, "events.log");
    static string StepFile => Path.Combine(Out, "step.txt");
    static readonly Dictionary<object, int> s_Contexts = new Dictionary<object, int>();
    static int s_Wait, s_Waited;
    static double s_RoundStart;

    static CompileEventsProbe()
    {
        if (string.IsNullOrEmpty(Out)) return;
        Directory.CreateDirectory(Out);
        Log("initializeOnLoad");
        CompilationPipeline.compilationStarted += c => Log("compilationStarted " + Context(c));
        CompilationPipeline.compilationFinished += c => Log("compilationFinished " + Context(c));
#pragma warning disable 618
        CompilationPipeline.assemblyCompilationStarted += a => Log("assemblyCompilationStarted " + a);
#pragma warning restore 618
        CompilationPipeline.assemblyCompilationFinished += (a, m) => Log("assemblyCompilationFinished " + a + " " + Messages(m));
        CompilationPipeline.assemblyCompilationNotRequired += a => Log("notRequired " + a);
        SubscribeInternalFinished();
        AssemblyReloadEvents.beforeAssemblyReload += () => Log("beforeAssemblyReload");
        AssemblyReloadEvents.afterAssemblyReload += () => Log("afterAssemblyReload");
        EditorApplication.playModeStateChanged += state => Log("playMode " + state);
        EditorApplication.update += Tick;
    }

    /// <summary>Subscribes to the Editor compilation's own assemblyCompilationFinished (internal;
    /// package editor code such as Burst's loader uses it for the assembly's compile errors and
    /// defines), after CompilationPipeline's events.</summary>
    static void SubscribeInternalFinished()
    {
        const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var compilation = typeof(CompilationPipeline).Assembly.GetType("UnityEditor.Scripting.ScriptCompilation.EditorCompilationInterface")
            .GetProperty("Instance", Any).GetValue(null);
        var finished = compilation.GetType().GetEvent("assemblyCompilationFinished", Any);
        var handler = Delegate.CreateDelegate(finished.EventHandlerType, typeof(CompileEventsProbe).GetMethod(nameof(InternalFinished), Any));
        finished.GetAddMethod(true).Invoke(compilation, new object[] { handler });
    }

    /// <summary>Logs the internal event: the assembly's output path, whether it has compile errors,
    /// whether its defines hold the probe's define, and how many messages came with it.</summary>
    static void InternalFinished(object assembly, CompilerMessage[] messages)
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object Read(string name) => assembly.GetType().GetProperty(name, Any).GetValue(assembly);
        var defines = (string[])Read("Defines") ?? new string[0];
        Log("internalFinished " + Read("FullPath") + " errors=" + Read("HasCompileErrors") + " probeDefine=" + defines.Contains("PROBE_DEFINE")
            + " messages=" + (messages == null ? "null" : messages.Length.ToString()));
    }

    [InitializeOnEnterPlayMode]
    static void Entering(EnterPlayModeOptions options)
    {
        if (!string.IsNullOrEmpty(Out)) Log("enterPlayMode " + options);
    }

    [UnityEditor.Callbacks.DidReloadScripts]
    static void Reloaded()
    {
        if (!string.IsNullOrEmpty(Out)) Log("didReloadScripts");
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    /// <summary>The context's type and which one it is in this domain (1 for the first seen).</summary>
    static string Context(object context)
    {
        if (context == null) return "null";
        if (!s_Contexts.TryGetValue(context, out int n)) s_Contexts[context] = n = s_Contexts.Count + 1;
        return context.GetType().FullName + "#" + n;
    }

    static string Messages(CompilerMessage[] messages) =>
        "[" + string.Join(" | ", messages.Select(m => m.type + " " + m.file + "(" + m.line + "," + m.column + ") " + m.message)) + "]";

    static void Log(string line) => File.AppendAllText(EventLog, line.Replace("\n", "\\n") + "\n");

    static int Step
    {
        get => File.Exists(StepFile) ? int.Parse(File.ReadAllText(StepFile)) : 0;
        set => File.WriteAllText(StepFile, value.ToString());
    }

    static readonly string[] s_Versions =
    {
        "public static class Changing { public const int Version = 1; }\n",
        "public static class Changing { public const int Version = ; }\n",
        "public static class Changing { public const int Version = 2; }\n",
    };

    /// <summary>Each round's change: the three rewrites, then the requests.</summary>
    static readonly Action[] s_Rounds =
    {
        () => Rewrite(0), () => Rewrite(1), () => Rewrite(2),
        () => CompilationPipeline.RequestScriptCompilation(),
        () => CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache),
        () => PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, "PROBE_DEFINE"),
        () => PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, "OTHER_DEFINE"),
        () => PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, "PROBE_DEFINE"),
    };

    static void Rewrite(int version)
    {
        File.WriteAllText("Assets/Scripts/Changing.cs", s_Versions[version]);
        AssetDatabase.Refresh();
    }

    /// <summary>What the round left: the scripts' define and the targets' scripting defines.</summary>
    static void LogState() =>
        Log("state defined=" + Defined.On + " standalone=" + PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone)
            + " android=" + PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android));

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        int step = Step;
        if (step >= s_Rounds.Length + 1) return;
        s_Waited++;
        if (step > 0)
        {
            // Wait for the round's compilationFinished, then let reloads settle.
            var lines = File.ReadAllLines(EventLog);
            int begin = Array.LastIndexOf(lines, "round " + step);
            bool finished = begin >= 0 && lines.Skip(begin).Any(l => l.StartsWith("compilationFinished", StringComparison.Ordinal));
            bool request = step > s_Versions.Length;
            if (!finished && (request ? EditorApplication.timeSinceStartup - s_RoundStart < RequestSeconds || EditorApplication.isCompiling : s_Waited < Timeout)) return;
            if (++s_Wait < Settle) return;
        }
        else if (++s_Wait < Settle) return;
        s_Wait = 0;
        s_Waited = 0;
        if (step > 0) LogState();
        if (step == s_Rounds.Length)
        {
            Step = step + 1;
            Finish();
            return;
        }
        Step = step + 1;
        Log("round " + (step + 1));
        s_RoundStart = EditorApplication.timeSinceStartup;
        s_Rounds[step]();
    }

    /// <summary>Play mode's own events: its state changes and [InitializeOnEnterPlayMode].</summary>
    static bool IsPlayModeEvent(string line) => line.StartsWith("playMode ", StringComparison.Ordinal) || line.StartsWith("enterPlayMode ", StringComparison.Ordinal);

    /// <summary>An event's name, with its argument for Play mode's events.</summary>
    static string Token(string line) => IsPlayModeEvent(line) ? line.Replace(' ', ':') : line.Split(' ')[0];

    /// <summary>The event logged before an internal event: its name, ":same" when it was about the
    /// same assembly.</summary>
    static string Before(string previous, string line)
    {
        var words = previous.Split(' ');
        return words[0] + (words.Length > 1 && words[1] == line.Split(' ')[1] ? ":same" : "");
    }

    static bool IsInternal(string line) => line.StartsWith("internalFinished ", StringComparison.Ordinal);

    static void Finish()
    {
        var all = File.ReadAllLines(EventLog);
        // The rows of CompilationPipeline's events leave the internal event out; it has its own row.
        var lines = all.Where(l => !IsInternal(l)).ToArray();
        var rows = new StringBuilder();
        void Put(string key, string value) =>
            rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
        // How the domain the rounds start in loaded: its events from the reload entering Play mode
        // on (the Editor loaded others before, in Edit mode).
        int first = Array.IndexOf(lines, "round 1");
        var before = (first < 0 ? lines : lines.Take(first)).Select(Token).ToArray();
        int reload = Array.LastIndexOf(before, "beforeAssemblyReload");
        Put("start.load", string.Join(",", before.Skip(Math.Max(0, reload >= 0 ? reload : Array.LastIndexOf(before, "initializeOnLoad")))));
        for (int round = 1; round <= s_Rounds.Length; round++)
        {
            int begin = Array.IndexOf(lines, "round " + round);
            int end = Array.IndexOf(lines, "round " + (round + 1));
            if (end < 0) end = lines.Length;
            var events = begin < 0 ? new string[0] : lines.Skip(begin + 1).Take(end - begin - 1).ToArray();
            string k = "round" + round + ".";
            // The order of the round's events, an assembly's (compiled or not required) being
            // "assembly": their order among themselves is not Unity's to keep.
            var order = new List<string>();
            Put(k + "state", events.LastOrDefault(e => e.StartsWith("state ", StringComparison.Ordinal)) ?? "none");
            events = events.Where(e => !e.StartsWith("state ", StringComparison.Ordinal)).ToArray();
            Put(k + "playMode", string.Join(",", events.Where(IsPlayModeEvent).Select(Token)));
            foreach (var e in events.Where(e => !IsPlayModeEvent(e)))
            {
                string name = e.Split(' ')[0];
                if ((name.StartsWith("assembly", StringComparison.Ordinal) && !name.EndsWith("Reload", StringComparison.Ordinal)) || name == "notRequired")
                    name = "assembly";
                if (order.Count == 0 || order[order.Count - 1] != name) order.Add(name);
            }
            Put(k + "order", string.Join(",", order));
            Put(k + "assemblies", string.Join(" || ", events.Where(e => e.StartsWith("assemblyCompilation", StringComparison.Ordinal)).OrderBy(e => e.Split(' ')[1], StringComparer.Ordinal).ThenBy(e => e, StringComparer.Ordinal)));
            Put(k + "notRequired", string.Join(",", events.Where(e => e.StartsWith("notRequired", StringComparison.Ordinal)).Select(e => e.Substring("notRequired ".Length)).OrderBy(e => e, StringComparer.Ordinal)));
            var contexts = events.Where(e => e.StartsWith("compilation", StringComparison.Ordinal)).Select(e => e.Split(' ')[1]).ToArray();
            Put(k + "contexts", string.Join(",", contexts.Select(c => c.Split('#')[0]).Distinct()));
            Put(k + "contextShared", (contexts.Length == 2 && contexts[0] == contexts[1]).ToString());
            // The internal event of each compiled assembly, with the event logged just before it.
            int allBegin = Array.IndexOf(all, "round " + round), allEnd = Array.IndexOf(all, "round " + (round + 1));
            if (allEnd < 0) allEnd = all.Length;
            var internals = new List<string>();
            for (int i = allBegin + 1; allBegin >= 0 && i < allEnd; i++)
                if (IsInternal(all[i])) internals.Add(all[i].Substring("internalFinished ".Length) + " after=" + Before(all[i - 1], all[i]));
            Put(k + "internal", string.Join(" || ", internals.OrderBy(e => e, StringComparer.Ordinal)));
        }
        rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Out, "values.jsonl"), rows.ToString());
        EditorApplication.Exit(0);
    }
}
