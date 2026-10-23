using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unbound.ApiTools.Tests;

public class ApiCheckerTests
{
    const string Unity = """
        namespace UnityEngine
        {
            public static class SystemInfo { public static int supportsTextureWrapMirrorOnce => 0; }
            public class Camera { public enum MonoOrStereoscopicEye { Left, Right, Mono } }
            public enum Mode { A = 1, B = 2 }
            public struct Pair { public int a; public float b; }
            public class Thing
            {
                public int Count { get; set; }
                public void Do(int x) {}
                public Thing() {}
            }
        }
        namespace UnityEngine.Rendering { public struct ReflectionProbeBlendInfo { public float weight; } }
        """;

    const string HandWritten = """
        namespace UnityEngine
        {
            public static partial class SystemInfo { public static bool supportsTextureWrapMirrorOnce => false; }
            public partial class Camera { }
            public enum MonoOrStereoscopicEye { Left, Right, Mono }
            public enum Mode { A = 1, B = 3, C = 4 }
            public partial struct Pair { public float b; public int a; }
            internal partial class Thing
            {
                public int Count { get; }
                public void Do(ref int x) {}
                internal void Do(string helper) {}
            }
            public struct ReflectionProbeBlendInfo { public float weight; }
            public class Invented {}
        }
        namespace Unbound { public class OwnPublicHelper {} }
        """;

    [Fact]
    public void ReportsHandWrittenApiThatDiffersFromUnity()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var unity = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(unity, "UnityEngine.CoreModule", Unity);
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", HandWritten);

            var report = ApiChecker.Check(unity, impl);
            string Messages(string category, string type) => string.Join("\n",
                report.Errors.Where(e => e.Category == category && e.Type == type).Select(e => e.Message));

            Assert.Contains("static bool supportsTextureWrapMirrorOnce { get; } matches none of Unity's: static int supportsTextureWrapMirrorOnce { get; }",
                Messages("signature", "UnityEngine.SystemInfo"));
            Assert.Contains("Unity's is UnityEngine.Camera+MonoOrStereoscopicEye", Messages("type", "UnityEngine.MonoOrStereoscopicEye"));
            Assert.Contains("Unity's is UnityEngine.Rendering.ReflectionProbeBlendInfo", Messages("type", "UnityEngine.ReflectionProbeBlendInfo"));
            Assert.Contains("does not exist in Unity", Messages("type", "UnityEngine.Invented"));
            Assert.DoesNotContain(report.Errors, e => e.Type.StartsWith("Unbound", StringComparison.Ordinal));

            var mode = Messages("enum", "UnityEngine.Mode");
            Assert.Contains("B is 3, Unity's is 2", mode);
            Assert.Contains("has C = 4, which Unity's does not", mode);
            Assert.Contains("public fields are (float b, int a), Unity's are (int a, float b)", Messages("layout", "UnityEngine.Pair"));

            Assert.Contains("is internal, Unity's is public", Messages("access", "UnityEngine.Thing"));
            var thing = Messages("signature", "UnityEngine.Thing");
            Assert.Contains("int Count { get; } has no setter, Unity's has one", thing);
            Assert.Contains("void Do(ref int) matches none of Unity's: void Do(int)", thing);
            Assert.DoesNotContain("Do(string)", thing); // an internal helper overload is Unbound's own
            Assert.Equal(10, report.Errors.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
