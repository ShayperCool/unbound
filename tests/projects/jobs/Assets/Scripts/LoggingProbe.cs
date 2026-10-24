using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Unity.Logging;
using Unity.Logging.Sinks;
using UnityEngine;

/// <summary>com.unity.logging writing a text file (Baselib file IO, timers, thread-local storage).</summary>
public static class LoggingProbe
{
    public static IEnumerator Run()
    {
        ProbeLog.Begin("logging.file");
        var directory = Path.Combine(Path.GetTempPath(), "unbound-jobs-probe-" + System.Diagnostics.Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "probe.log");
        var logger = new Unity.Logging.Logger(new LoggerConfig()
            .MinimumLevel.Debug()
            .OutputTemplate("{Level} | {Message}")
            .WriteTo.File(path));
        Log.Logger = logger;
        Log.Info("Hello {0}", 42);
        Log.Warning("warn {0} {1}", 1.5f, "text");
        Log.Debug("debug");
        Log.Verbose("hidden");
        Log.Error("error {name}", "named");
        Log.FlushAll();
        yield return null;
        Log.FlushAll();
        logger.Dispose();
        var files = Directory.GetFiles(directory);
        ProbeLog.Value("files", files.Length);
        string text = "";
        foreach (var file in files) text += File.ReadAllText(file);
        text = Regex.Replace(text, "\r", "");
        ProbeLog.Value("text", text);
        Directory.Delete(directory, true);

        // The Editor's console sink (ConsoleWindow.AddMessage): what reaches the log callbacks.
        ProbeLog.Begin("logging.console");
        using (var capture = new LogCapture())
        {
            var console = new Unity.Logging.Logger(new LoggerConfig()
                .MinimumLevel.Debug()
                .OutputTemplate("{Level} | {Message}")
                .WriteTo.UnityEditorConsole(captureStackTrace: false));
            Log.Logger = console;
            Log.Info("console info {0}", 7);
            Log.Warning("console warning");
            Log.Error("console error");
            Log.FlushAll();
            yield return null;
            Log.FlushAll();
            console.Dispose();
            capture.Record("captured");
        }
    }
}
