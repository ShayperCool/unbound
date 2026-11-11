using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// The unit of <c>System.Diagnostics.Stopwatch</c>'s ticks as game code sees it: <c>Frequency</c>,
/// <c>IsHighResolution</c>, and over a 200 ms sleep how <c>ElapsedTicks</c> and the
/// <c>GetTimestamp</c> difference compare with <c>Elapsed.Ticks</c> (100 ns) and with
/// <c>Frequency</c>, rounded so that timing noise does not show, and the sleep in seconds from
/// each measure, also the way game code converts ticks (divided by 10 000 000).
/// </summary>
public class StopwatchProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder s_Output = new StringBuilder();

    static void Record(string key, string value) =>
        s_Output.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    static string B(bool b) => b ? "true" : "false";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("STOPWATCH_UNITS_OUT") == null) return;
        Application.runInBackground = true;
        DontDestroyOnLoad(new GameObject("StopwatchProbe").AddComponent<StopwatchProbe>().gameObject);
    }

    static long Ticks(Stopwatch watch) => watch.ElapsedTicks;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ref readonly long FrequencyReference() => ref Stopwatch.Frequency;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long TouchStack()
    {
        // Reuse stack space after FrequencyReference returns, before its reference is read.
        const int StackControlLength = 128;
        Span<long> values = stackalloc long[StackControlLength];
        long sum = 0;
        for (int i = 0; i < values.Length; i++) values[i] = i + 1;
        for (int i = 0; i < values.Length; i++) sum += values[i];
        return sum;
    }

    void Start()
    {
        Record("frequency", Stopwatch.Frequency.ToString());
        Record("isHighResolution", B(Stopwatch.IsHighResolution));
        ref readonly long frequency = ref FrequencyReference();
        Record("stackControlSum", TouchStack().ToString());
        Record("frequencyFromEscapedReference", frequency.ToString());

        long start = Stopwatch.GetTimestamp();
        var watch = Stopwatch.StartNew();
        Thread.Sleep(200);
        watch.Stop();
        long stamps = Stopwatch.GetTimestamp() - start;
        long elapsed = watch.Elapsed.Ticks;

        Record("elapsedTicksEqualsElapsed", B(watch.ElapsedTicks == elapsed));
        Record("elapsedTicksEqualsHelper", B(Ticks(watch) == watch.ElapsedTicks));
        // Ratios to Elapsed.Ticks, to one decimal: 1 when the unit is 100 ns.
        Record("elapsedTicksPerTimeSpanTick", Math.Round((double)watch.ElapsedTicks / elapsed, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Record("timestampPerTimeSpanTick", Math.Round((double)stamps / elapsed, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        // The sleep in seconds from each measure, to a tenth: 0.2 when units and Frequency agree.
        Record("secondsFromElapsedTicksAndFrequency", Math.Round((double)watch.ElapsedTicks / Stopwatch.Frequency, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Record("secondsFromTimestampsAndFrequency", Math.Round((double)stamps / Stopwatch.Frequency, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Record("secondsFromElapsedTicksAsTimeSpan", Math.Round(watch.ElapsedTicks / 10000000.0, 1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Record("complete", "true");
        File.WriteAllText(Environment.GetEnvironmentVariable("STOPWATCH_UNITS_OUT"), s_Output.ToString());
        Finished = true;
    }
}
