using System;
using System.IO;
using System.Linq;
using Unbound.Compiler;
using Xunit;

namespace Unbound.ApiTools.Tests;

/// <summary>An assembly's warnings come back with its compile, and with a later hit of the compile
/// cache for the same inputs (an edit undone), from the warm service and from a fresh one.</summary>
public class CompileWarningsTests
{
    [Fact]
    public void ACachedOutputReportsTheWarningsOfTheCompileThatMadeIt()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var source = Path.Combine(root, "Warn.cs");
            const string warned = "public static class Warn { static void M() { int unused = 1; } }\n";
            File.WriteAllText(source, warned);
            var request = new CompileRequest { CacheDir = Path.Combine(root, "cache"), LanguageVersion = "latest" };
            request.BclReferences.AddRange(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator));
            request.Assemblies.Add(new AssemblySpec { Name = "Game", Sources = { source } });
            var service = new CompilerService();

            var first = Assert.Single(service.Compile(request).Assemblies);
            Assert.Equal("compiled", first.Status);
            var warning = Assert.Single(first.WarningList, w => w.Id == "CS0219");
            Assert.Equal(source, warning.File);
            Assert.Equal(1, warning.Line);
            Assert.Equal(50, warning.Column);

            // A different version, then the original again: the cache's output and its warnings.
            File.WriteAllText(source, "public static class Warn { }\n");
            var changed = Assert.Single(service.Compile(request).Assemblies);
            Assert.Equal("compiled", changed.Status);
            Assert.DoesNotContain(changed.WarningList, w => w.Id == "CS0219");
            File.WriteAllText(source, warned);
            foreach (var compiler in new[] { service, new CompilerService() })
            {
                var undone = Assert.Single(compiler.Compile(request).Assemblies);
                Assert.Equal("cached", undone.Status);
                var again = Assert.Single(undone.WarningList, w => w.Id == "CS0219");
                Assert.Equal((warning.Message, warning.File, warning.Line, warning.Column), (again.Message, again.File, again.Line, again.Column));
                Assert.Equal(undone.WarningList.Count, undone.Warnings);
                File.WriteAllText(source, "public static class Warn { }\n");
                Assert.Equal("cached", Assert.Single(compiler.Compile(request).Assemblies).Status);
                File.WriteAllText(source, warned);
            }

            // Control: an unreadable warnings file is a cache miss, not a failure.
            foreach (var file in Directory.GetFiles(Path.Combine(root, "cache", "Game"), "*.warnings.json")) File.WriteAllText(file, "{");
            var fresh = Assert.Single(new CompilerService().Compile(request).Assemblies);
            Assert.Equal("compiled", fresh.Status);
            Assert.Contains(fresh.WarningList, w => w.Id == "CS0219");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
