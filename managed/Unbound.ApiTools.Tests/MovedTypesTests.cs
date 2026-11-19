using System;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Unbound.Compiler;
using Xunit;

namespace Unbound.ApiTools.Tests;

/// <summary>A package in the Editor's package cache that names types by where they moved from
/// compiles against them where they are now, as the Editor's script updater rewrites it
/// (Unity 6000.0.71f1, tests/projects/compilesetup <c>call.converter</c>); other assemblies, types
/// that do not update automatically and assemblies outside "Unity*" are left failing.</summary>
public class MovedTypesTests
{
    const string Attribute = @"
namespace UnityEngine.Scripting.APIUpdating
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class MovedFromAttribute : System.Attribute
    {
        public MovedFromAttribute(bool autoUpdateAPI, string sourceNamespace = null, string sourceAssembly = null, string sourceClassName = null) { }
        public MovedFromAttribute(string sourceNamespace) { }
    }
}
";
    const string Target = @"
using UnityEngine.Scripting.APIUpdating;
namespace New.Space
{
    [MovedFrom(true, ""Old.Space"")] public class Moved { public static string Where() => typeof(Moved).FullName; }
    [MovedFrom(true, ""Old.Space"", null, ""OldName"")] public class Renamed { }
    [MovedFrom(true, ""Old.Space"")] public class Box<T> { }
    [MovedFrom(false, ""Old.Space"")] public class Manual { }
    [MovedFrom(""Old.Space"")] public class Legacy { }
    // From a namespace that no longer has any type.
    [MovedFrom(true, ""Gone.Space"")] public class Vanished { public static string Where() => typeof(Vanished).FullName; }
    [MovedFrom(true, ""Old.Deeper.Space"")] public class DeepMoved { }
}
namespace Old.Space { public class Stays { } }
namespace Old.Deeper.Space { public class Stays { } }
";
    const string Consumer = @"
using O = Old.Space;
using Old.Space;
public static class Consumer
{
    public static string Aliased() => typeof(O.Moved).FullName;
    public static string Called() => Old.Space.Moved.Where();
    public static string Renamed() => typeof(OldName).FullName;
    public static string Generic() => typeof(Box<int>).Name;
    public static string Legacy() => typeof(O.Legacy).FullName;
    public static string Vanished() => typeof(Gone.Space.Vanished).FullName;
    public static string Rooted() => typeof(global::Old.Space.Moved).FullName;
    public static string VanishedCall() => Gone.Space.Vanished.Where();
}
namespace Old.Inner
{
    // `Space` relative to the enclosing namespace's parent.
    public static class Relative { public static string Name() => typeof(Space.Moved).FullName; }
}
namespace Old
{
    namespace Deeper
    {
        // `Space` is Old.Deeper.Space here: the nested declarations' names joined.
        public static class Nested { public static string Name() => typeof(Space.DeepMoved).FullName; }
    }
}
";

    static CompileRequest Setup(string root, string targetName, string consumer, bool updates)
    {
        File.WriteAllText(Path.Combine(root, "Attribute.cs"), Attribute);
        File.WriteAllText(Path.Combine(root, "Target.cs"), Target);
        File.WriteAllText(Path.Combine(root, "Consumer.cs"), consumer);
        var request = new CompileRequest { CacheDir = Path.Combine(root, "cache"), LanguageVersion = "latest" };
        request.BclReferences.AddRange(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator));
        request.Assemblies.Add(new AssemblySpec { Name = targetName, Sources = { Path.Combine(root, "Attribute.cs"), Path.Combine(root, "Target.cs") } });
        request.Assemblies.Add(new AssemblySpec
        {
            Name = "Package",
            Sources = { Path.Combine(root, "Consumer.cs") },
            References = { targetName },
            UpdatesMovedTypes = updates,
        });
        return request;
    }

    [Fact]
    public void APackageNamingMovedTypesByTheirOldPlaceCompilesAgainstTheirNewOne()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var results = new CompilerService().Compile(Setup(root, "Unity.Moved.Target", Consumer, updates: true)).Assemblies;
            var package = results.Single(a => a.Name == "Package");
            Assert.All(results, r => Assert.True(r.Status != "failed" || r.Name == "Package", r.Name + ": " + string.Join("\n", r.Errors.Select(e => e.Message))));
            Assert.True(package.Status == "compiled", string.Join("\n", package.Errors.Select(e => e.Line + ":" + e.Column + " " + e.Message)));
            var context = new AssemblyLoadContext("moved-types", isCollectible: true);
            try
            {
                context.LoadFromAssemblyPath(results.Single(a => a.Name == "Unity.Moved.Target").Dll!);
                var consumer = context.LoadFromAssemblyPath(package.Dll!).GetType("Consumer")!;
                string Call(string method) => (string)consumer.GetMethod(method)!.Invoke(null, null)!;
                Assert.Equal("New.Space.Moved", Call("Aliased"));
                Assert.Equal("New.Space.Moved", Call("Called"));
                Assert.Equal("New.Space.Renamed", Call("Renamed"));
                Assert.Equal("Box`1", Call("Generic"));
                // The release's MovedFrom(string sourceNamespace) declares autoUpdateAPI true.
                Assert.Equal("New.Space.Legacy", Call("Legacy"));
                Assert.Equal("New.Space.Vanished", Call("Vanished"));
                Assert.Equal("New.Space.Vanished", Call("VanishedCall"));
                Assert.Equal("New.Space.Moved", Call("Rooted"));
                var assembly = consumer.Assembly;
                string Named(string type) => (string)assembly.GetType(type)!.GetMethod("Name")!.Invoke(null, null)!;
                Assert.Equal("New.Space.Moved", Named("Old.Inner.Relative"));
                Assert.Equal("New.Space.DeepMoved", Named("Old.Deeper.Nested"));
            }
            finally { context.Unload(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    // An assembly outside the package cache: the Editor asks before it changes such files.
    [InlineData("Unity.Moved.Target", false, "public static class C { static object M() => typeof(Old.Space.Moved); }")]
    // A type that does not update automatically.
    [InlineData("Unity.Moved.Target", true, "public static class C { static object M() => typeof(Old.Space.Manual); }")]
    // Moved types of assemblies whose name does not start with "Unity" are not considered.
    [InlineData("Moved.Target", true, "public static class C { static object M() => typeof(Old.Space.Moved); }")]
    // `global::Moved` names the global namespace only, not the usings' namespaces (CS0400).
    [InlineData("Unity.Moved.Target", true, "using Old.Space; public static class C { static object M() => typeof(global::Moved); }")]
    public void OtherwiseTheNameStaysMissing(string targetName, bool updates, string consumer)
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var package = new CompilerService().Compile(Setup(root, targetName, consumer, updates)).Assemblies.Single(a => a.Name == "Package");
            Assert.Equal("failed", package.Status);
            Assert.Contains(package.Errors, e => e.Id is "CS0234" or "CS0400");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
