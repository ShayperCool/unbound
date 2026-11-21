using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unbound.Runtime.Jobs;

namespace UnityEngine.Tests
{
    /// <summary>The tests run the engine's job system: the shared library of crates/unbound-jobs
    /// (copied next to the tests, see UnityEngine.Tests.csproj), whose table the engine gets
    /// through its native API.</summary>
    static unsafe class TestJobSystem
    {
        [ModuleInitializer]
        internal static void Install()
        {
            string file = OperatingSystem.IsMacOS() ? "libunbound_jobs.dylib" : OperatingSystem.IsWindows() ? "unbound_jobs.dll" : "libunbound_jobs.so";
            var library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, file));
            var table = (delegate* unmanaged<void*>)NativeLibrary.GetExport(library, "unbound_jobs_api");
            JobSystem.Install(table());
        }
    }
}
