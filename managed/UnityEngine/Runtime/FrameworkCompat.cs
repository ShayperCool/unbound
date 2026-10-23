using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Unbound
{
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
