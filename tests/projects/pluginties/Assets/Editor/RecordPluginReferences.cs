using System;
using System.IO;
using System.Linq;
using UnityEditor.Compilation;

public static class RecordPluginReferences
{
    public static void Record()
    {
        using var output = File.CreateText(Environment.GetEnvironmentVariable("PLUGIN_TIES_OUT"));
        foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                     .OrderBy(a => a.name, StringComparer.Ordinal))
        {
            foreach (var path in assembly.compiledAssemblyReferences
                         .Where(p => Path.GetFileName(p).StartsWith("ProbeTie", StringComparison.Ordinal))
                         .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
                output.WriteLine($"{assembly.name}\t{path}");
        }
        output.WriteLine("complete");
    }
}
