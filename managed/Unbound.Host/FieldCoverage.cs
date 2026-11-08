using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unbound.Compiler;
using Unbound.Runtime;
using UnityEngine;

namespace Unbound.Host;

internal sealed class FieldCoverageSetup
{
    public CompileRequest Compilation { get; set; } = new();
    public string Release { get; set; } = "";
    public string FacadeDir { get; set; } = "";
}

internal static class FieldCoverageHost
{
    // Load metadata for SerializationRules.Members. No script instances, constructors,
    // deserialization callbacks, lifecycle calls or runtime initialization methods are invoked.
    internal static void Prepare(FieldCoverageSetup setup)
    {
        var compiled = Entry.Compiler.Compile(setup.Compilation);
        ProjectInfo.ProjectPath = setup.Compilation.ProjectPath;
        ProjectInfo.UnityVersion = setup.Release;
        GameDomain.FrameworkDir = setup.Compilation.BclReferences.Select(Path.GetDirectoryName).FirstOrDefault();
        var assemblies = new List<GameAssembly>();
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in compiled.Assemblies)
        {
            if (result.Dll == null) continue;
            var spec = setup.Compilation.Assemblies.First(a => a.Name == result.Name);
            var assembly = new GameAssembly { Name = result.Name, Dll = result.Dll, Pdb = result.Pdb };
            foreach (var source in spec.Sources)
            {
                var guid = GuidOf(source + ".meta");
                if (guid != null) assembly.Scripts.Add((Path.GetFileNameWithoutExtension(source), guid, null));
            }
            assemblies.Add(assembly);
            added.Add(assembly.Name);
            foreach (var plugin in spec.Plugins)
            {
                string name = Path.GetFileNameWithoutExtension(plugin);
                if (added.Add(name)) assemblies.Add(new GameAssembly { Name = name, Dll = plugin, PluginGuid = GuidOf(plugin + ".meta") });
            }
        }
        GameDomain.Load(assemblies, setup.FacadeDir);
    }

    static string GuidOf(string path)
    {
        if (!File.Exists(path)) return null;
        foreach (var line in File.ReadLines(path))
            if (line.StartsWith("guid: ", StringComparison.Ordinal)) return line.Substring(6).Trim();
        return null;
    }
}
