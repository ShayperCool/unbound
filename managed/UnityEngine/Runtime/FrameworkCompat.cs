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
