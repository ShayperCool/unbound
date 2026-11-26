using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Launches of the Editor's executable from the game (tests/projects/clonelaunch): one with
/// ordinary Editor arguments, which Unbound refuses, and a Multiplayer Play Mode virtual player's
/// (the clone project of $CLONELAUNCH_CLONE), which Unbound runs as another Unbound; the clone
/// writes its command line to clone.txt. The launcher writes main.txt. Both in $CLONELAUNCH_OUT.
/// </summary>
public class LaunchProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLONELAUNCH_OUT"))) return;
        new GameObject("LaunchProbe").AddComponent<LaunchProbe>();
    }

    void Start()
    {
        var args = Environment.GetCommandLineArgs();
        var outDir = Environment.GetEnvironmentVariable("CLONELAUNCH_OUT");
        if (Array.IndexOf(args, "--virtual-project-clone") >= 0)
        {
            UnityEngine.Debug.Log("The virtual player logs this.");
            File.WriteAllText(Path.Combine(outDir, "clone.txt"), string.Join("\n", args) + "\nproject " + Application.dataPath);
            EditorApplication.Exit(0);
            return;
        }
        var log = new System.Text.StringBuilder();
        log.AppendLine("applicationPath " + EditorApplication.applicationPath);
        try { var p = Process.Start(new ProcessStartInfo(EditorApplication.applicationPath, "-batchmode -projectPath /tmp/x") { UseShellExecute = false, RedirectStandardError = true }); log.AppendLine("plain launch started pid " + p.Id + " stderr " + p.StandardError.ReadToEnd()); }
        catch (Exception e) { log.AppendLine("plain launch: " + e.GetType().Name + ": " + e.Message); }
        // As Multiplayer Play Mode starts a virtual player (ProcessSystem): its log in the clone's
        // Logs folder, which does not exist yet; the Process and its pipes disposed at once.
        var clone = Environment.GetEnvironmentVariable("CLONELAUNCH_CLONE");
        var editorLog = Path.Combine(clone, "Logs", "Editor.log");
        int pid = -1;
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo(EditorApplication.applicationPath,
                $"-projectPath \"{clone}\" --virtual-project-clone -forgetProjectPath -logFile \"{editorLog}\" -vp-channel-name=vp-channel -ump-channel-service-port 1234 -vpId=abc")
            { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            p.Start();
            pid = p.Id;
            log.AppendLine("clone started");
        }
        catch (Exception e) { log.AppendLine("clone launch: " + e.GetType().Name + ": " + e.Message); }
        var marker = Path.Combine(outDir, "clone.txt");
        var start = DateTime.UtcNow;
        while (!File.Exists(marker) && (DateTime.UtcNow - start).TotalSeconds < 240) Thread.Sleep(500);
        log.AppendLine("clone marker " + File.Exists(marker));
        bool exited = false;
        while (pid > 0 && (DateTime.UtcNow - start).TotalSeconds < 300)
        {
            try { using var running = Process.GetProcessById(pid); if (running.HasExited) { exited = true; break; } }
            catch (ArgumentException) { exited = true; break; }
            Thread.Sleep(500);
        }
        log.AppendLine("clone exited " + exited);
        File.WriteAllText(Path.Combine(outDir, "main.txt"), log.ToString());
        EditorApplication.Exit(0);
    }
}
