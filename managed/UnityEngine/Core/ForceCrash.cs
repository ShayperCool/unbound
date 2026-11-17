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
    /// PureVirtualFunction and MonoAbort of SIGABRT. Matches the Unity 6000.0.71f1 Editor on Linux
    /// (tests/projects/forcecrash); macOS prints the same lines (the macOS Editor's are unmeasured).
    /// FatalError logs the Editor's error, but its end is not matched: the Editor starts shutting
    /// down and never exits by itself, where Unbound aborts after the error without the shutdown.
    /// </summary>
    public static partial class Utils
    {
        const int SIGABRT = 6;
        const int SIGSEGV = 11;

        public static void ForceCrash(ForcedCrashCategory crashCategory)
        {
            switch (crashCategory)
            {
                case ForcedCrashCategory.AccessViolation:
                    Print("Forcing a crash -- Intentionally Dereferencing NULL pointer");
                    Die(SIGSEGV);
                    break;
                case ForcedCrashCategory.FatalError:
                    Debug.LogError("Intentionally caused fatal error");
                    Die(SIGABRT);
                    break;
                case ForcedCrashCategory.Abort:
                    Die(SIGABRT);
                    break;
                case ForcedCrashCategory.PureVirtualFunction:
                    Print("pure virtual method called");
                    Print("terminate called without an active exception");
                    Die(SIGABRT);
                    break;
                case ForcedCrashCategory.MonoAbort:
                    Print("* Assertion at unity-utils.c:183, condition `klass' not met");
                    Die(SIGABRT);
                    break;
            }
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
