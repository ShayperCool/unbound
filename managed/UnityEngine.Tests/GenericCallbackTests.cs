using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Unbound.Host;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Marshal.GetFunctionPointerForDelegate of a delegate of a generic type, which the
    /// Editor's Mono marshals and .NET refuses: game assemblies' calls are redirected to
    /// Unbound.MonoMarshal (tests/projects/pluginbundle `callback.generic`).</summary>
    public class GenericCallbackTests
    {
        const string Source = @"
using System;
using System.Runtime.InteropServices;
public static class Probe
{
    public static string Received = ""not called"";
    static readonly Action<string, int, string> s_Callback = (a, b, c) => Received = a + ""|"" + b + ""|"" + c;
    static readonly Func<int, int> s_Twice = x => 2 * x;
    public static IntPtr Generic() => Marshal.GetFunctionPointerForDelegate(s_Callback);
    static void Record(string a, int b, string c) => Received = a + ""/"" + b + ""/"" + c;
    // A temporary delegate of a static method, as plugins pass one.
    public static IntPtr Temporary() => Marshal.GetFunctionPointerForDelegate(new Action<string, int, string>(Record));
    public static IntPtr AsDelegate() => Marshal.GetFunctionPointerForDelegate((Delegate)s_Twice);
}";

        static Assembly Compiled(bool redirected)
        {
            var image = WideFloatsTests.Emit(WideFloatsTests.Compilation(Source, OptimizationLevel.Release, "GenericCallbackProbe")).Image;
            return WideFloatsTests.Load(redirected ? FrameworkShims.Redirect(image) : image);
        }

        static object Call(Assembly assembly, string method)
        {
            try { return assembly.GetType("Probe")!.GetMethod(method)!.Invoke(null, null)!; }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }

        [Fact]
        public unsafe void AGenericDelegateBecomesANativeCallback()
        {
            var probe = Compiled(redirected: true);
            var pointer = (IntPtr)Call(probe, "Generic");
            Assert.Equal(pointer, (IntPtr)Call(probe, "Generic")); // one callable delegate per delegate
            var first = Marshal.StringToHGlobalAnsi("native");
            var last = Marshal.StringToHGlobalAnsi("call");
            try { ((delegate* unmanaged<IntPtr, int, IntPtr, void>)pointer)(first, 17, last); }
            finally
            {
                Marshal.FreeHGlobal(first);
                Marshal.FreeHGlobal(last);
            }
            Assert.Equal("native|17|call", probe.GetType("Probe")!.GetField("Received")!.GetValue(null));
            // A temporary delegate stays callable after collections, as under Mono.
            var temporary = (IntPtr)Call(probe, "Temporary");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            first = Marshal.StringToHGlobalAnsi("later");
            try { ((delegate* unmanaged<IntPtr, int, IntPtr, void>)temporary)(first, 3, first); }
            finally { Marshal.FreeHGlobal(first); }
            Assert.Equal("later/3/later", probe.GetType("Probe")!.GetField("Received")!.GetValue(null));
            // Through the overload taking a Delegate too.
            var twice = (IntPtr)Call(probe, "AsDelegate");
            Assert.Equal(42, ((delegate* unmanaged<int, int>)twice)(21));
        }

        [Fact]
        public void WithoutTheRedirectNetRefusesIt() =>
            Assert.Throws<ArgumentException>(() => Call(Compiled(redirected: false), "Generic"));
    }
}
