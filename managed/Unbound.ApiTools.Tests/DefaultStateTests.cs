using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unbound.ApiTools.Tests;

public sealed class DefaultStateTests
{
    [Fact]
    public void InventoryIncludesFactoriesAndExplainsUnconstructibleTypes()
    {
        const string source = """
            namespace UnityEngine {
                public class Component {}
                public class ScriptableObject {}
                internal class InternalComponent : Component {}
                public abstract class AbstractAsset : ScriptableObject {}
                public class PublicClass {}
                public class NeedsArgument { public NeedsArgument(int value) {} }
                public struct ZeroStruct { public int value; }
                public class Generic<T> { public class Nested {} }
                public static class Utilities {}
                internal class Hidden {}
            }
            """;
        var root = TestAssemblies.TempDir();
        try
        {
            var modules = Path.Combine(root, "Managed", "UnityEngine");
            TestAssemblies.Compile(modules, "UnityEngine.CoreModule", source);
            var impl = TestAssemblies.Compile(Path.Combine(root, "impl"), "UnityEngine.CoreModule", source);
            var rows = DefaultState.Inspect(modules, impl).Types;
            DefaultStateType Row(string type) => Assert.Single(rows, row => row.Type == "UnityEngine." + type);
            Assert.Equal("component", Row("InternalComponent").Category);
            Assert.Equal("candidate", Row("InternalComponent").Status);
            Assert.Equal("abstract", Row("AbstractAsset").Status);
            Assert.Equal("candidate", Row("ZeroStruct").Status);
            Assert.Equal("candidate", Row("PublicClass").Status);
            Assert.Equal("no-public-parameterless-constructor", Row("NeedsArgument").Status);
            Assert.Equal("open-generic", Row("Generic`1+Nested").Status);
            Assert.Equal("static", Row("Utilities").Status);
            Assert.DoesNotContain(rows, row => row.Type == "UnityEngine.Hidden");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LiteralStoresDistinguishUnknownMissingAndRetypedStorage()
    {
        const string reference = """
            public class Values {
                int equal = 7, changed = -1, later = 1, missing = 3;
                string text = "reference";
                int retyped = 4;
                public Values() { later = Get(); }
                static int Get() => 8;
            }
            public class Branch { int value; public Branch(bool flag) { if (flag) value = 1; else value = 2; } }
            public class Generic<T> { int value = 12; }
            """;
        const string implementation = """
            public class Values {
                int equal = 7, changed = 0, later = 2;
                string text = "implementation";
                byte retyped = 1;
                public Values() { later = Get(); }
                static int Get() => 9;
            }
            public class Branch { int value; public Branch(bool flag) { if (flag) value = 8; else value = 9; } }
            public class Generic<T> { int value = 13; }
            """;
        var root = TestAssemblies.TempDir();
        try
        {
            var expected = TestAssemblies.Compile(Path.Combine(root, "reference"), "Api", reference);
            var actual = TestAssemblies.Compile(Path.Combine(root, "actual"), "Api", implementation);
            var report = ConstructorStores.Compare(new[] { expected }, actual);
            Assert.Equal(1, report.Equal);
            Assert.Equal(1, report.Unknown);
            Assert.Equal(3, report.UnmatchedFields);
            Assert.Equal(2, report.BranchingConstructors);
            Assert.Equal(3, report.Differences.Count);
            Assert.Contains(report.Differences, row => row.Constructor.StartsWith("Generic`1/") && row.Expected == "i4:12" && row.Actual == "i4:13");
            Assert.Contains(report.Differences, row => row.Field.StartsWith("changed:") && row.Expected == "i4:-1" && row.Actual == "i4:0");
            Assert.Contains(report.Differences, row => row.Field.StartsWith("text:") && row.Expected == "string:\"reference\"");
            Assert.DoesNotContain(report.Differences, row => row.Field.StartsWith("retyped:") || row.Field.StartsWith("later:"));
        }
        finally { Directory.Delete(root, true); }
    }
}
