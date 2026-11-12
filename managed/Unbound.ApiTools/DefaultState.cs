using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Unbound.ApiTools;

/// <summary>Inventory for the release Editor's default-state capture. No reference assembly is loaded for execution.</summary>
public static class DefaultState
{
    public static DefaultStateReport Inspect(string unityManagedEngineDir, string implAssembly,
        IEnumerable<string>? platformModules = null)
    {
        var platforms = platformModules?.ToArray();
        var builder = new StubBuilder(unityManagedEngineDir, platforms, implAssembly, new HandWrittenSources(null));
        return new DefaultStateReport
        {
            Types = builder.DefaultStateTypes(),
            ConstructorStores = ConstructorStores.Compare(UnityModules.Files(unityManagedEngineDir, platforms), implAssembly)
        };
    }
}

public sealed class DefaultStateReport
{
    public List<DefaultStateType> Types { get; set; } = new();
    public ConstructorStoreReport ConstructorStores { get; set; } = new();
}

public sealed class DefaultStateType
{
    public string Type { get; set; } = "";
    public string Module { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "";
}

sealed partial class StubBuilder
{
    public List<DefaultStateType> DefaultStateTypes()
    {
        IndexOwners();
        var result = new List<DefaultStateType>();
        foreach (var implementation in SelfAndNested(TopLevelTypes(_implAssembly.GlobalNamespace)))
        {
            var type = FindUnity(implementation);
            if (type == null || type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) continue;
            var category = Derives(type, "UnityEngine.Component") ? "component"
                : Derives(type, "UnityEngine.ScriptableObject") ? "scriptable"
                : type.TypeKind == TypeKind.Struct ? "struct" : "class";
            if (category is "class" or "struct" && !PubliclyVisible(type)) continue;
            // A nested type in an open generic parent also cannot be constructed.
            bool generic = false;
            for (var parent = type; parent != null; parent = parent.ContainingType) generic |= parent.IsGenericType;
            var status = generic ? "open-generic" : type.IsStatic ? "static" : type.IsAbstract ? "abstract"
                : category == "class" && !type.InstanceConstructors.Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0)
                    ? "no-public-parameterless-constructor" : "candidate";
            result.Add(new DefaultStateType { Type = MetadataName(type), Module = type.ContainingAssembly.Name,
                Category = category, Status = status });
        }
        return result.OrderBy(t => t.Type, StringComparer.Ordinal).ToList();
    }

    static bool Derives(INamedTypeSymbol type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (MetadataName(current) == name) return true;
        return false;
    }

    static bool PubliclyVisible(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
            if (current.DeclaredAccessibility != Accessibility.Public) return false;
        return true;
    }
}
