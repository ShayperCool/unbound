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
            File.WriteAllText(Path.Combine(outDir, "clone.txt"), string.Join("\n", args) + "\nproject " + Application.dataPath);
            EditorApplication.Exit(0);
            return;
        }
        var log = new System.Text.StringBuilder();
        log.AppendLine("applicationPath " + EditorApplication.applicationPath);
        try { var p = Process.Start(new ProcessStartInfo(EditorApplication.applicationPath, "-batchmode -projectPath /tmp/x") { UseShellExecute = false, RedirectStandardError = true }); log.AppendLine("plain launch started pid " + p.Id + " stderr " + p.StandardError.ReadToEnd()); }
        catch (Exception e) { log.AppendLine("plain launch: " + e.GetType().Name + ": " + e.Message); }
        var clone = Environment.GetEnvironmentVariable("CLONELAUNCH_CLONE");
        var info = new ProcessStartInfo(EditorApplication.applicationPath,
            $"-projectPath \"{clone}\" --virtual-project-clone -forgetProjectPath -logFile \"{Path.Combine(outDir, "clone-log.txt")}\" -vp-channel-name=vp-channel -ump-channel-service-port 1234 -vpId=abc")
        { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        try
        {
            using var p = Process.Start(info);
            log.AppendLine("clone started pid " + p.Id);
            var marker = Path.Combine(outDir, "clone.txt");
            var start = DateTime.UtcNow;
            while (!File.Exists(marker) && (DateTime.UtcNow - start).TotalSeconds < 240) Thread.Sleep(500);
            log.AppendLine("clone marker " + File.Exists(marker));
            p.WaitForExit(30000);
            log.AppendLine("clone exited " + p.HasExited + (p.HasExited ? " code " + p.ExitCode : ""));
        }
        catch (Exception e) { log.AppendLine("clone launch: " + e.GetType().Name + ": " + e.Message); }
        File.WriteAllText(Path.Combine(outDir, "main.txt"), log.ToString());
        EditorApplication.Exit(0);
    }
}
