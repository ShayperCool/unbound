using System.Collections.Generic;
using System.Threading;

/// <summary>Every construction of the counted scripts, in order: the object and whether it ran on
/// the thread the probe's Start runs on (tests/projects/gameinterop ctor.twice.*).</summary>
public static class ConstructionLog
{
    public static readonly List<(object Made, int Thread)> Made = new List<(object, int)>();

    public static void Note(object made)
    {
        lock (Made) Made.Add((made, Thread.CurrentThread.ManagedThreadId));
    }
}
