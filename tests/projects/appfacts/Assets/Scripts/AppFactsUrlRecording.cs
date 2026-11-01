using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

// Fixture synchronization only. The subprocess completes a record by appending its newline.
// Stopwatch is real elapsed time on both Mono and CoreCLR, independent of headless frame time.
internal static class AppFactsUrlRecording
{
    internal static string Read(string log) => File.Exists(log) ? File.ReadAllText(log) : "";

    internal static string[] Wait(string log, int offset, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            string text = Read(log);
            if (text.Length > offset && text.EndsWith("\n", StringComparison.Ordinal))
                return text.Substring(offset).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Thread.Sleep(10);
        }
        throw new TimeoutException("OpenURL recording stub did not finish a record before the real-time deadline");
    }
}
