#define ENABLE_PROFILER
using System;
using UnityEngine.Profiling;

public static class ConditionalEnabledControl
{
    static int effects;
    static ProfilerArea Argument() { effects++; return ProfilerArea.CPU; }
    public static void Record(Action<string, object> row)
    {
        effects = 0;
        bool previous = Profiler.GetAreaEnabled(ProfilerArea.CPU);
        Profiler.SetAreaEnabled(Argument(), previous);
        row("conditional.defined.argument-effects", effects);
    }
}
