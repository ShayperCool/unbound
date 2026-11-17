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
    /// <c>Marshal.GetFunctionPointerForDelegate</c> as the Editor's Mono answers it: a delegate of a
    /// generic type (<c>Action&lt;string, int, string&gt;</c>) gets a native-callable pointer too,
    /// while .NET refuses generic delegate types (tests/projects/genericcallback). Such a delegate is
    /// called through a delegate of a non-generic type with its signature (one type per generic
    /// type). As under Mono, where a native callback made from a delegate stays valid after the
    /// delegate is collected (plugins pass temporary ones), the delegate called is kept alive, once
    /// per equal delegate (Unbound.NativeCallbackRoots). Game assemblies' calls are redirected here
    /// when they load (Unbound.Host.FrameworkShims).
    /// </summary>
    public static class MonoMarshal
    {
        static readonly System.Collections.Generic.Dictionary<Delegate, Delegate> s_Callable = new();
        static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Type> s_Types = new();
        static ModuleBuilder s_Module;
        static int s_Defined;

        public static IntPtr GetFunctionPointerForDelegate(Delegate d)
        {
            if (d == null) return System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(d);
            if (!d.GetType().IsGenericType)
            {
                NativeCallbackRoots.Keep(d);
                return System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(d);
            }
            Delegate callable;
            lock (s_Callable)
                if (!s_Callable.TryGetValue(d, out callable)) s_Callable[d] = callable = Callable(d);
            return System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(callable);
        }

        public static IntPtr GetFunctionPointerForDelegate<TDelegate>(TDelegate d) where TDelegate : notnull =>
            GetFunctionPointerForDelegate((Delegate)(object)d);

        /// <summary>A delegate of a non-generic type with the signature of <paramref name="d"/>'s,
        /// invoking it.</summary>
        static Delegate Callable(Delegate d)
        {
            var invoke = d.GetType().GetMethod("Invoke");
            var type = s_Types.GetOrAdd(d.GetType(), Define);
            return Delegate.CreateDelegate(type, d, invoke);
        }

        static Type Define(Type generic)
        {
            var invoke = generic.GetMethod("Invoke");
            lock (s_Types)
            {
                s_Module ??= AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Unbound.MonoMarshal.Callbacks"), AssemblyBuilderAccess.Run)
                    .DefineDynamicModule("Callbacks");
                var builder = s_Module.DefineType("Callback" + s_Defined++ + "_" + generic.Name.Replace('`', '_'),
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.AutoClass, typeof(MulticastDelegate));
                // The generic type's own calling convention, when it names one.
                var convention = (System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute)Attribute.GetCustomAttribute(
                    generic, typeof(System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute));
                if (convention != null)
                    builder.SetCustomAttribute(new CustomAttributeBuilder(
                        typeof(System.Runtime.InteropServices.UnmanagedFunctionPointerAttribute).GetConstructor(new[] { typeof(System.Runtime.InteropServices.CallingConvention) }),
                        new object[] { convention.CallingConvention }));
                builder.DefineConstructor(MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
                        CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) })
                    .SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
                var parameters = Array.ConvertAll(invoke.GetParameters(), p => p.ParameterType);
                builder.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                        invoke.ReturnType, parameters)
                    .SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);
                return builder.CreateType();
            }
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
