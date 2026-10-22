using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Unbound.ApiTools.Tests;

/// <summary>Compiles small assemblies on the fly: stand-ins for Unity modules, Unbound's
/// implementation and game code.</summary>
static class TestAssemblies
{
    static readonly MetadataReference[] Framework =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll")
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToArray();

    /// <summary>Compiles <paramref name="source"/> into <c>&lt;dir&gt;/&lt;name&gt;.dll</c>.</summary>
    public static string Compile(string dir, string name, string source, params string[] references)
    {
        Directory.CreateDirectory(dir);
        var compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source) },
            Framework.Concat(references.Select(r => MetadataReference.CreateFromFile(r))),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Disable));
        var path = Path.Combine(dir, name + ".dll");
        using (var stream = File.Create(path))
        {
            var result = compilation.Emit(stream);
            if (!result.Success)
                throw new InvalidOperationException(
                    $"{name} did not compile:\n" + string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }
        return path;
    }

    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "unbound-apitools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
