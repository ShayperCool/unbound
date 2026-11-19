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

    /// <summary>The release's compiler is older than Unbound's: a symbolic -langversion means its
    /// newest version, and a newer explicit one is refused. A line's arguments split as the
    /// compiler splits a response file's lines.</summary>
    [Fact]
    public void LanguageVersionsStopAtTheReleasesCompiler()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var file = Path.Combine(root, "Probe.cs");
            // A raw string literal: C# 11.
            File.WriteAllText(file, Source + "public static class Raw { public const string Text = \"\"\"raw\"\"\"; }\n");
            CompileRequest Limited(params string[] response)
            {
                var request = Request(root, file, response);
                request.LanguageVersionMax = "10.0";
                return request;
            }
            var latest = Assert.Single(new CompilerService().Compile(Limited("-langversion:latest")).Assemblies);
            Assert.Equal("failed", latest.Status);
            Assert.Contains(latest.Errors, e => e.Id == "CS8936" || e.Id == "CS9058" || e.Message.Contains("raw string"));
            var newer = Assert.Single(new CompilerService().Compile(Limited("-langversion:11")).Assemblies);
            Assert.Equal("failed", newer.Status);
            Assert.Contains(newer.Errors, e => e.Id == "CS1617");
            // Control: without a known limit, latest is Unbound's compiler's.
            Assert.Equal("compiled", Assert.Single(new CompilerService().Compile(Request(root, file, "-langversion:latest")).Assemblies).Status);

            File.WriteAllText(file, Source);
            var split = Assert.Single(new CompilerService().Compile(Request(root, file, "-define:RSP_A\t-define:RSP_B")).Assemblies);
            Assert.Equal("compiled", split.Status);
            Assert.Equal("A,B", Constant(split, "Defines"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>A quoted value with spaces stays one argument; a reference under an alias is
    /// reachable through extern alias; an edited rule set compiles again.</summary>
    [Fact]
    public void QuotedValuesAliasesAndRuleSetEdits()
    {
        var root = TestAssemblies.TempDir();
        try
        {
            var lib = Path.Combine(root, "Lib.cs");
            File.WriteAllText(lib, "public static class Lib { public const int V = 5; }\n");
            var libRequest = Request(root, lib);
            libRequest.Assemblies[0].Name = "AliasLib";
            var built = Assert.Single(new CompilerService().Compile(libRequest).Assemblies);
            Assert.Equal("compiled", built.Status);

            var file = Path.Combine(root, "Probe.cs");
            File.WriteAllText(file, "extern alias Old;\npublic static class Probe { public const int V = Old::Lib.V; static void M() { int unused = 1; } }\n");
            var rules = Path.Combine(root, "Code Rules", "a.ruleset");
            Directory.CreateDirectory(Path.GetDirectoryName(rules)!);
            void Rules(string action) => File.WriteAllText(rules,
                "<RuleSet Name=\"r\" ToolsVersion=\"16.0\"><Rules AnalyzerId=\"Microsoft.CodeAnalysis.CSharp\" RuleNamespace=\"Microsoft.CodeAnalysis.CSharp\">"
                + "<Rule Id=\"CS0219\" Action=\"" + action + "\" /></Rules></RuleSet>");
            CompileRequest WithRules()
            {
                var request = Request(root, file, "-ruleset:\"" + rules + "\"", "-nowarn:\"CS0168, CS0414\"");
                request.Assemblies[0].AliasedReferences.Add(new AliasedReference { Alias = "Old", Path = built.Dll! });
                return request;
            }
            Rules("Error");
            var service = new CompilerService();
            var strict = Assert.Single(service.Compile(WithRules()).Assemblies);
            Assert.Equal("failed", strict.Status);
            Assert.Contains(strict.Errors, e => e.Id == "CS0219");
            Rules("None");
            var relaxed = Assert.Single(service.Compile(WithRules()).Assemblies);
            Assert.Equal("compiled", relaxed.Status);
            Assert.DoesNotContain(relaxed.WarningList, w => w.Id == "CS0219");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
