using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Unbound
{
    /// <summary>The Editor process as game and editor code see it: its command line is Unity's
    /// (<c>Unity -projectPath ...</c>, plus what <c>unbound test</c> passes). Calls to
    /// <c>Environment.GetCommandLineArgs</c> and <c>Environment.CommandLine</c> are redirected here
    /// when assemblies load (Unbound.Host.FrameworkShims).</summary>
    public static class EditorProcess
    {
        internal static string[] s_CommandLine;

        public static string[] GetCommandLineArgs()
        {
            if (s_CommandLine == null || s_CommandLine.Length == 0)
            {
                var data = UnityEditor.UnityInstallInfo.DataPath ?? "";
                var unity = OperatingSystem.IsMacOS()
                    ? System.IO.Path.Combine(data, "MacOS", "Unity")
                    : System.IO.Path.Combine(data, "..", "Unity");
                s_CommandLine = new[] { System.IO.Path.GetFullPath(unity), "-projectpath", UnityEngine.ProjectInfo.ProjectPath };
            }
            return (string[])s_CommandLine.Clone();
        }

        public static string get_CommandLine() => string.Join(" ", GetCommandLineArgs());
    }

    /// <summary>
    /// <c>System.Diagnostics.Stopwatch</c> in the Linux 6000.0.71f1 Editor: ticks are 100 ns,
    /// <c>Frequency</c> is 10 000 000 and stopped <c>ElapsedTicks</c> equals <c>Elapsed.Ticks</c>
    /// (tests/projects/stopwatchunits). The measured .NET host counts nanoseconds. Game code that divides raw ticks by
    /// <c>TimeSpan.TicksPerSecond</c> works in the Editor and would run 100 times too fast here.
    /// Game assemblies' uses of these members are redirected here when they load
    /// (Unbound.Host.FrameworkShims).
    /// </summary>
    public static class MonoStopwatch
    {
        public static readonly long Frequency = TimeSpan.TicksPerSecond;

        public static long GetTimestamp() => ToMonoTicks(System.Diagnostics.Stopwatch.GetTimestamp());

        public static long get_ElapsedTicks(System.Diagnostics.Stopwatch watch) => watch.Elapsed.Ticks;

        /// <summary>A .NET timestamp in 100 ns ticks, without overflow or lost precision.</summary>
        internal static long ToMonoTicks(long timestamp)
        {
            long frequency = System.Diagnostics.Stopwatch.Frequency;
            if (frequency == TimeSpan.TicksPerSecond) return timestamp;
            long seconds = Math.DivRem(timestamp, frequency, out long rest);
            return seconds * TimeSpan.TicksPerSecond + rest * TimeSpan.TicksPerSecond / frequency;
        }
    }

    /// <summary>
    /// .NET Framework / Mono members that .NET lacks, as static methods taking the instance first.
    /// Assemblies calling them are redirected here when they load (Unbound.Host.FrameworkShims).
    /// </summary>
    public static class FrameworkCompat
    {
        public static ModuleBuilder DefineDynamicModule(AssemblyBuilder assembly, string name, bool emitSymbolInfo) =>
            assembly.DefineDynamicModule(name);

        public static ModuleBuilder DefineDynamicModule(AssemblyBuilder assembly, string name, string fileName) =>
            assembly.DefineDynamicModule(name);

        public static ModuleBuilder DefineDynamicModule(AssemblyBuilder assembly, string name, string fileName, bool emitSymbolInfo) =>
            assembly.DefineDynamicModule(name);

        public static AssemblyBuilder DefineDynamicAssembly(AppDomain domain, AssemblyName name, AssemblyBuilderAccess access) =>
            AssemblyBuilder.DefineDynamicAssembly(name, RunAccess(access));

        public static AssemblyBuilder DefineDynamicAssembly(AppDomain domain, AssemblyName name, AssemblyBuilderAccess access, string dir) =>
            AssemblyBuilder.DefineDynamicAssembly(name, RunAccess(access));

        /// <summary>Saving to disk does not exist in .NET: such builders only run.</summary>
        static AssemblyBuilderAccess RunAccess(AssemblyBuilderAccess access) =>
            access == AssemblyBuilderAccess.RunAndCollect ? AssemblyBuilderAccess.RunAndCollect : AssemblyBuilderAccess.Run;
    }
}
