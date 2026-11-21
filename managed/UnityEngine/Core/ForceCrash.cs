using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Diagnostics
{
    public enum ForcedCrashCategory
    {
        AccessViolation = 0,
        FatalError = 1,
        Abort = 2,
        PureVirtualFunction = 3,
        MonoAbort = 4,
    }

    /// <summary>
    /// Crashes the process as the Editor does when a script asks for it: the lines the Editor prints
    /// for the category, then the signal it dies of. AccessViolation dies of SIGSEGV; Abort,
    /// PureVirtualFunction, MonoAbort and any value outside ForcedCrashCategory die of SIGABRT
    /// (values outside 0..3 follow MonoAbort). Matches the Unity 6000.0.71f1 Editor on Linux
    /// (tests/projects/forcecrash). On macOS Unbound prints the same Linux lines without a measurement
    /// of the macOS Editor's.
    /// FatalError logs the Editor's error, but its end is not matched: the Editor starts shutting
    /// down and never exits by itself, where Unbound aborts after the error without the shutdown.
    /// </summary>
    public static partial class Utils
    {
        internal const int SIGABRT = 6;
        internal const int SIGSEGV = 11;

        internal static (string[] Lines, string Error, int Signal) Plan(ForcedCrashCategory crashCategory) => crashCategory switch
        {
            ForcedCrashCategory.AccessViolation => (new[] { "Forcing a crash -- Intentionally Dereferencing NULL pointer" }, null, SIGSEGV),
            ForcedCrashCategory.FatalError => (Array.Empty<string>(), "Intentionally caused fatal error", SIGABRT),
            ForcedCrashCategory.Abort => (Array.Empty<string>(), null, SIGABRT),
            ForcedCrashCategory.PureVirtualFunction => (new[] { "pure virtual method called", "terminate called without an active exception" }, null, SIGABRT),
            // MonoAbort and any value outside 0..3 (measured for 7 and -1, tests/projects/forcecrash).
            _ => (new[] { "* Assertion at unity-utils.c:183, condition `klass' not met" }, null, SIGABRT),
        };

        public static void ForceCrash(ForcedCrashCategory crashCategory)
        {
            var (lines, error, signal) = Plan(crashCategory);
            if (error != null) Debug.LogError(error);
            foreach (var line in lines) Print(line);
            Die(signal);
        }

        /// <summary>A line the Editor prints to its log and console, not through the game's log.</summary>
        static void Print(string line)
        {
            Unbound.Runtime.ConsoleLog.Append(line);
            Unbound.Runtime.Native.Log((int)LogType.Log, line);
        }

        [DllImport("libc", EntryPoint = "signal")]
        static extern IntPtr SetSignalHandler(int signal, IntPtr handler);

        [DllImport("libc", EntryPoint = "raise")]
        static extern int Raise(int signal);

        /// <summary>Dies of the signal with its default action (the runtimes' own handlers removed),
        /// so the process ends as the Editor's does.</summary>
        static void Die(int signal)
        {
            Console.Out.Flush();
            Console.Error.Flush();
            SetSignalHandler(signal, IntPtr.Zero);
            Raise(signal);
            // Not reached: the default action of both signals ends the process.
            Environment.FailFast("ForceCrash: the signal did not end the process");
        }
    }
}

namespace UnityEngine
{
    public partial class Application
    {
        [Obsolete("Use UnityEngine.Diagnostics.Utils.ForceCrash")]
        public static void ForceCrash(int mode) => Diagnostics.Utils.ForceCrash((Diagnostics.ForcedCrashCategory)mode);
    }
}
