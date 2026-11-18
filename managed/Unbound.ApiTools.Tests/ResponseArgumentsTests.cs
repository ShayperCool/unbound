using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Unbound.Compiler;
using Xunit;

namespace Unbound.ApiTools.Tests;

/// <summary>An assembly compiles with its response file's arguments after the request's own: its
/// defines, -unsafe, a later -langversion and -nullable, -nowarn and -warnaserror take effect; an
/// option the compiler does not know fails the assembly; the arguments are part of the cache key
/// (Unity 6000.0.71f1, tests/projects/gameinterop rsp.*).</summary>
public class ResponseArgumentsTests
{
    const string Source = @"
public static class Probe
{
#if RSP_A && RSP_B
    public const string Defines = ""A,B"";
#else
    public const string Defines = ""none"";
#endif
}
";

    static CompileRequest Request(string root, string file, params string[] response)
    {
        var request = new CompileRequest { CacheDir = Path.Combine(root, "cache"), LanguageVersion = "9.0" };
        request.CompilerOptions.Add("/nowarn:0169");
        request.BclReferences.AddRange(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator));
        var spec = new AssemblySpec { Name = "Game", Sources = { file } };
        spec.ResponseArguments.AddRange(response);
        request.Assemblies.Add(spec);
        return request;
    }

    static string Constant(AssemblyResult result, string name)
    {
        var assembly = Assembly.Load(File.ReadAllBytes(result.Dll!));
        return (string)assembly.GetType("Probe")!.GetField(name)!.GetValue(null)!;
    }

    [Fact]
    public void ResponseArgumentsFollowTheRequestOptions()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var file = Path.Combine(root, "Probe.cs");
            File.WriteAllText(file, Source + @"
public static class Extra
{
    const string Part = ""ten"";
    public const string Interpolated = $""csharp-{Part}"";
    public static unsafe int Pointer() { int x = 3; int* p = &x; return *p; }
    public static string Echo(string? text) => text ?? """";
    static void Unused() { int unused = 1; }
}
");
            var service = new CompilerService();
            // Without the response file's options the C# 10 constant and the unsafe code fail.
            var plain = Assert.Single(service.Compile(Request(root, file)).Assemblies);
            Assert.Equal("failed", plain.Status);
            Assert.Contains(plain.Errors, e => e.Id == "CS8773");
            Assert.Contains(plain.Errors, e => e.Id == "CS0227");

            var request = Request(root, file, "-define:RSP_A;RSP_B", "-unsafe", "-langversion:10", "-nullable:enable", "-nowarn:CS0219");
            var built = Assert.Single(service.Compile(request).Assemblies);
            Assert.Equal("compiled", built.Status);
            Assert.Equal("A,B", Constant(built, "Defines"));
            Assert.DoesNotContain(built.WarningList, w => w.Id == "CS0219");
            Assert.DoesNotContain(built.WarningList, w => w.Id == "CS8632");

            // A later option wins: -warnaserror turns the unused variable into an error.
            var strict = Assert.Single(service.Compile(Request(root, file, "-define:RSP_A;RSP_B", "-unsafe", "-langversion:10", "-warnaserror+:CS0219")).Assemblies);
            Assert.Equal("failed", strict.Status);
            Assert.Contains(strict.Errors, e => e.Id == "CS0219");

            // The same arguments again: the cached output.
            Assert.Equal("cached", Assert.Single(new CompilerService().Compile(request).Assemblies).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void AnUnknownOptionFailsTheAssemblyAndIgnoredOnesWarn()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var file = Path.Combine(root, "Probe.cs");
            File.WriteAllText(file, Source);
            var unknown = Assert.Single(new CompilerService().Compile(Request(root, file, "-nosuchoption")).Assemblies);
            Assert.Equal("failed", unknown.Status);
            Assert.Contains(unknown.Errors, e => e.Id == "CS2007");

            var ignored = Assert.Single(new CompilerService().Compile(Request(root, file, "-additionalfile:notes.txt")).Assemblies);
            Assert.Equal("compiled", ignored.Status);
            Assert.Contains(ignored.WarningList, w => w.Id == "UNBOUND0003" && w.Message.Contains("-additionalfile:notes.txt"));
            Assert.Equal("none", Constant(ignored, "Defines"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
