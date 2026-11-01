using System;
using System.Linq;
using System.Text.Json;
using Mono.Cecil;
// Offline assembly metadata inspection. This never starts or loads the Editor.
class InspectLayout
{
    static void Main(string[] args)
    {
        using var module = ModuleDefinition.ReadModule(args[0]);
        var particle = module.Types.Single(t => t.Name == "ParticleSystem");
        var trails = particle.NestedTypes.Single(t => t.Name == "Trails");
        Console.WriteLine(JsonSerializer.Serialize(trails.Fields.Select(f => new {
            name = f.Name, type = f.FieldType.FullName, visibility = f.Attributes.ToString(),
            isPublic = f.IsPublic, isAssembly = f.IsAssembly
        })));
    }
}
