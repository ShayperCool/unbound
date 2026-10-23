using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Unbound.Compiler;

/// <summary>
/// The Roslyn source generators the Editor runs on every script assembly (UI Toolkit's
/// UxmlElement code, Unity.Properties, ...), with the additional file the Editor gives them: a
/// <c>&lt;assembly&gt;.UnityAdditionalFile.txt</c> holding the project directory.
/// </summary>
static class SourceGenerators
{
    static readonly ConcurrentDictionary<string, ImmutableArray<ISourceGenerator>> s_ByPath = new();
    static readonly AnalyzerLoader s_Loader = new();

    internal static ImmutableArray<ISourceGenerator> Load(IEnumerable<string> paths)
    {
        var all = ImmutableArray.CreateBuilder<ISourceGenerator>();
        foreach (var path in paths)
            all.AddRange(s_ByPath.GetOrAdd(path, LoadGenerators));
        return all.ToImmutable();
    }

    static readonly object s_EntryLock = new();

    /// <summary>
    /// Unity's generators run only when <c>Assembly.GetEntryAssembly()</c> is set (they stay off
    /// inside IDEs), and read it once, in their static initializers. A natively hosted runtime has
    /// no entry assembly, and game code expects none (as in the Editor), so one is set only while
    /// the generators' types initialize.
    /// </summary>
    static ImmutableArray<ISourceGenerator> LoadGenerators(string path)
    {
        lock (s_EntryLock)
        {
            var previous = Assembly.GetEntryAssembly();
            Assembly.SetEntryAssembly(typeof(SourceGenerators).Assembly);
            try
            {
                var generators = new AnalyzerFileReference(path, s_Loader).GetGenerators(LanguageNames.CSharp).ToImmutableArray();
                foreach (var generator in generators)
                {
                    var type = generator.GetGeneratorType();
                    for (var t = type; t != null && t != typeof(object); t = t.BaseType)
                        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle);
                }
                return generators;
            }
            finally
            {
                Assembly.SetEntryAssembly(previous);
            }
        }
    }

    internal static AdditionalText AdditionalFile(string assemblyName, string projectPath) =>
        new InMemoryAdditionalText(assemblyName + ".UnityAdditionalFile.txt", projectPath);

    /// <summary>A driver for one assembly: reused across compiles, it reruns only what changed.</summary>
    internal static GeneratorDriver Driver(IEnumerable<string> paths, string assemblyName, string projectPath, CSharpParseOptions options) =>
        CSharpGeneratorDriver.Create(
            Load(paths),
            additionalTexts: new[] { AdditionalFile(assemblyName, projectPath) },
            parseOptions: options);

    /// <summary>The compilation with the generators' sources added.</summary>
    internal static CSharpCompilation Run(ref GeneratorDriver driver, CSharpCompilation compilation, List<Diagnostic> diagnostics)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var generatorDiagnostics, CancellationToken.None);
        diagnostics.AddRange(generatorDiagnostics);
        return (CSharpCompilation)updated;
    }

    sealed class InMemoryAdditionalText : AdditionalText
    {
        readonly SourceText m_Text;

        internal InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            m_Text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => m_Text;
    }

    /// <summary>Loads generator assemblies into the default context, where this compiler's own
    /// Roslyn is: they bind to it whatever Roslyn version they were built against.</summary>
    sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
    {
        readonly ConcurrentDictionary<string, Assembly> m_Loaded = new(StringComparer.Ordinal);

        public void AddDependencyLocation(string fullPath) { }

        public Assembly LoadFromPath(string fullPath) =>
            m_Loaded.GetOrAdd(fullPath, p => AssemblyLoadContext.Default.LoadFromAssemblyPath(p));
    }
}
