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
public delegate void Plain(int x);
public delegate void Marshalled<T>([MarshalAs(UnmanagedType.I1)] bool ok, T data);
[UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
public delegate void Wide<T>(string text, T data);
public struct Flags { public bool On; public int Count; }
public delegate void Touch<T>([In] ref Flags flags, T data);
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
    public static int Seen;
    static void Store(int x) => Seen = x;
    // A temporary delegate of a non-generic type: .NET's own marshalling, kept alive as under Mono.
    public static IntPtr TemporaryPlain() => Marshal.GetFunctionPointerForDelegate(new Plain(Store));
    static void Ignore(bool ok, int data) { }
    public static string WideText;
    static void ReadWide(string text, int data) => WideText = text + ""/"" + data;
    static readonly Wide<int> s_Wide = ReadWide;
    public static IntPtr WideCallback() => Marshal.GetFunctionPointerForDelegate(s_Wide);
    // Flags is not blittable (bool): an [In] parameter is marshalled in only, so the change does not
    // reach the caller's memory.
    public static string Touched;
    static void Change(ref Flags flags, int data)
    {
        Touched = flags.On + ""/"" + flags.Count + ""/"" + data;
        flags.Count = data;
    }
    static readonly Touch<int> s_Touch = Change;
    public static IntPtr TouchCallback() => Marshal.GetFunctionPointerForDelegate(s_Touch);
    public static IntPtr WithMarshalling() => Marshal.GetFunctionPointerForDelegate(new Marshalled<int>(Ignore));
}";

        static Assembly Compiled(bool redirected)
        {
            var image = WideFloatsTests.Emit(WideFloatsTests.Compilation(Source, OptimizationLevel.Release, "GenericCallbackProbe")).Image;
            // Not collectible, as the game's load context: the emitted callback types reference its types.
            return new System.Runtime.Loader.AssemblyLoadContext("generic callbacks " + Guid.NewGuid())
                .LoadFromStream(new System.IO.MemoryStream(redirected ? FrameworkShims.Redirect(image) : image));
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
        public unsafe void ATemporaryNonGenericDelegateStaysCallable()
        {
            var probe = Compiled(redirected: true);
            var pointer = (IntPtr)Call(probe, "TemporaryPlain");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            ((delegate* unmanaged<int, void>)pointer)(5);
            Assert.Equal(5, probe.GetType("Probe")!.GetField("Seen")!.GetValue(null));
        }

        [Fact]
        public unsafe void TheGenericTypesMarshallingAndDirectionsAreKept()
        {
            var probe = Compiled(redirected: true);
            // CharSet.Unicode of [UnmanagedFunctionPointer]: the string arrives as UTF-16.
            var wide = (IntPtr)Call(probe, "WideCallback");
            var text = Marshal.StringToHGlobalUni("h\u00e9llo");
            try { ((delegate* unmanaged[Cdecl]<IntPtr, int, void>)wide)(text, 4); }
            finally { Marshal.FreeHGlobal(text); }
            Assert.Equal("h\u00e9llo/4", probe.GetType("Probe")!.GetField("WideText")!.GetValue(null));
            // An [In] parameter is not written back (its direction is the generic Invoke's).
            var touch = (IntPtr)Call(probe, "TouchCallback");
            var flags = stackalloc int[2] { 1, 1 }; // a 4-byte BOOL and the count, as marshalled
            ((delegate* unmanaged<int*, int, void>)touch)(flags, 99);
            Assert.Equal("True/1/99", probe.GetType("Probe")!.GetField("Touched")!.GetValue(null));
            Assert.Equal(1, flags[1]);
        }

        [Fact]
        public void PerParameterMarshallingFailsLoudly()
        {
            var error = Assert.Throws<NotSupportedException>(() => Call(Compiled(redirected: true), "WithMarshalling"));
            Assert.Contains("[MarshalAs]", error.Message);
        }

        [Fact]
        public void WithoutTheRedirectNetRefusesIt() =>
            Assert.Throws<ArgumentException>(() => Call(Compiled(redirected: false), "Generic"));
    }
}
