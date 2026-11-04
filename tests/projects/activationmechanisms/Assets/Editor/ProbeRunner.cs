using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records the probe in a batchmode Editor with graphics:
/// <c>-batchmode -executeMethod ProbeRunner.Record</c>, without <c>-quit</c> (Play mode runs
/// asynchronously) and without <c>-nographics</c>. Nothing here needs a Game view.
///
/// Record opens the probe scene and asks for Play mode from a later Editor update. In Play mode
/// <see cref="ActivationMechanismsProbe"/> writes its record and then sets
/// <see cref="ActivationMechanismsProbe.finishedCode"/>. Only <see cref="Tick"/>, an
/// <c>EditorApplication.update</c> handler, changes state: it notices the code, leaves Play mode,
/// waits <see cref="SettleTicks"/> Edit-mode updates and quits with the code. The probe's
/// coroutine and the play mode state callback only record; neither leaves Play mode nor quits.
/// Progress survives domain reloads in SessionState. Tick also clears the console's Error Pause
/// and quits with 4 when the Editor has not quit <see cref="WatchdogSeconds"/> after Record.
/// Every transition is appended to <c>runner-events.jsonl</c> next to the record. Before it
/// quits, the runner removes both handlers and closes its event output, so nothing it owns
/// writes a file or queries an Editor manager during shutdown. Unlike the shader probes' runner,
/// Record changes no Editor setting.
///
/// Exit codes: 0 complete, 3 the probe recorded a fatal error, 4 watchdog, 5 no code recorded,
/// 6 Record failed, 7 no output directory.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const string State = "ActivationMechanismsProbe.";
    const double WatchdogSeconds = 600;
    const int SettleTicks = 30;

    static bool armed;
    static bool closed;
    static int editTicks;

    static string Output => Environment.GetEnvironmentVariable(ActivationMechanismsProbe.OutputVariable);

    // Unbound runs the probe by itself; the Editor runner has nothing to drive there.
    static bool InUnbound => typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;

    static ProbeRunner()
    {
        if (InUnbound || Output == null || !SessionState.GetBool(State + "started", false)) return;
        if (SessionState.GetBool(State + "exit", false)) return;
        Event("domain", "playing=" + B(EditorApplication.isPlaying));
        Arm();
    }

    /// <summary>The entry point (<c>-executeMethod ProbeRunner.Record</c>).</summary>
    public static void Record()
    {
        if (InUnbound) return;
        if (Output == null)
        {
            EditorApplication.delayCall += () => EditorApplication.Exit(7);
            return;
        }
        if (SessionState.GetBool(State + "started", false))
        {
            Event("record-again", null);
            return;
        }
        SessionState.SetBool(State + "started", true);
        SessionState.SetFloat(State + "start", (float)EditorApplication.timeSinceStartup);
        Event("record", "batchmode=" + B(Application.isBatchMode) + " target=" + EditorUserBuildSettings.activeBuildTarget
              + " version=" + Application.unityVersion);
        Arm();
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        }
        catch (Exception e)
        {
            Event("record-failed", e.GetType().Name + ": " + e.Message);
            SessionState.SetInt(State + "code", 6);
            SessionState.SetBool(State + "finished", true);
            return;
        }
        EditorApplication.delayCall += () =>
        {
            Event("enter", null);
            EditorApplication.EnterPlaymode();
        };
    }

    static void Arm()
    {
        if (armed) return;
        armed = true;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnState;
    }

    static void OnState(PlayModeStateChange state)
    {
        if (closed) return;
        Event("state", state.ToString());
    }

    static void Tick()
    {
        if (closed || SessionState.GetBool(State + "exit", false)) return;
        if (EditorApplication.isPaused)
        {
            EditorApplication.isPaused = false;
            Event("unpause", null);
        }
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(State + "start", 0) > WatchdogSeconds)
        {
            File.WriteAllText(Path.Combine(Output, "watchdog.txt"), "the probe did not finish and quit in time\n");
            Quit(4, "watchdog");
            return;
        }
        if (!SessionState.GetBool(State + "finished", false))
        {
            if (!EditorApplication.isPlaying || ActivationMechanismsProbe.finishedCode == null) return;
            SessionState.SetInt(State + "code", ActivationMechanismsProbe.finishedCode.Value);
            SessionState.SetBool(State + "finished", true);
            Event("finished", ActivationMechanismsProbe.finishedCode.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (EditorApplication.isPlaying)
        {
            if (SessionState.GetBool(State + "leaving", false)) return;
            SessionState.SetBool(State + "leaving", true);
            Event("leave", null);
            EditorApplication.ExitPlaymode();
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode || ++editTicks < SettleTicks) return;
        Quit(SessionState.GetInt(State + "code", 5), "edit mode");
    }

    /// <summary>Records the exit, removes both handlers and closes the event output, then quits.</summary>
    static void Quit(int code, string reason)
    {
        SessionState.SetBool(State + "exit", true);
        Event("exit", code.ToString(CultureInfo.InvariantCulture) + " " + reason);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnState;
        closed = true;
        EditorApplication.Exit(code);
    }

    static string B(bool b) => b ? "true" : "false";

    static string Q(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    /// <summary>Appends one runner event: sequence number (across domain reloads), Editor time,
    /// name and detail. Diagnostics and guard input; not part of the compared record. Nothing is
    /// written once the runner has closed.</summary>
    static void Event(string name, string detail)
    {
        if (closed) return;
        int seq = SessionState.GetInt(State + "seq", 0);
        SessionState.SetInt(State + "seq", seq + 1);
        string line = "{\"seq\":" + seq.ToString(CultureInfo.InvariantCulture)
            + ",\"t\":" + EditorApplication.timeSinceStartup.ToString("F3", CultureInfo.InvariantCulture)
            + ",\"e\":" + Q(name) + ",\"detail\":" + Q(detail) + "}\n";
        File.AppendAllText(Path.Combine(Output, "runner-events.jsonl"), line);
    }
}
