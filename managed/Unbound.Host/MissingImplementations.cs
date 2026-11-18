using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unbound.Host;

/// <summary>
/// A precompiled plugin built against another version of a library can hold a class that lacks an
/// implementation of an abstract or interface member of the version the project has. The Editor's
/// Mono runtime lists and finds such a type and fails only when it is used: creating it or calling
/// its static members throws TypeLoadException (Unity 6000.0.71f1, tests/projects/gameinterop
/// plugins.extras.*). .NET refuses the type when it loads, so reflection over the whole plugin
/// (Assembly.GetTypes, GetType) would fail. Such a class gets an explicit implementation of each
/// missing member and its constructors and static methods throw first, so it loads and fails at
/// its first use as in the Editor.
///
/// The check is conservative: a member counts as implemented when the class or a base below the
/// member's declaration has a method of that name and parameter count, or an explicit
/// implementation of a member of that name (an interface's default implementation too); a type
/// whose bases or interfaces do not resolve is left as it is.
/// </summary>
static class MissingImplementations
{
    internal static byte[] Repair(byte[] image)
    {
        try
        {
            using var input = new MemoryStream(image);
            using var assembly = AccessChecks.Read(input);
            var module = assembly.MainModule;
            var repaired = 0;
            foreach (var type in module.GetTypes().ToList())
            {
                if (!type.IsClass || type.IsInterface || type.IsAbstract || type.IsValueType) continue;
                var missing = Missing(type);
                if (missing == null || missing.Count == 0) continue;
                Defer(module, type, missing);
                repaired++;
            }
            if (repaired == 0) return image;
            using var output = new MemoryStream();
            AccessChecks.Write(assembly, output);
            return output.ToArray();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Unbound] cannot check a plugin's classes for missing implementations ({e.GetType().Name}: {e.Message})");
            return image;
        }
    }

    /// <summary>The abstract and interface members the class leaves without an implementation, as
    /// references to them; null when one of its bases or interfaces does not resolve.</summary>
    static List<MethodReference> Missing(TypeDefinition type)
    {
        // The class and its bases, the class first, each with its reference (generic arguments).
        var chain = new List<(TypeDefinition Definition, TypeReference Reference)>();
        TypeReference reference = type;
        while (reference != null)
        {
            var definition = Resolve(reference);
            if (definition == null) return null;
            chain.Add((definition, reference));
            reference = definition.BaseType == null ? null : Substitute(definition.BaseType, reference);
        }
        // Default implementations that interfaces give for other interfaces' members (an
        // interface's explicit implementation of a member it inherits).
        var defaults = new List<MethodDefinition>();
        var faces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (definition, _) in chain)
            foreach (var implemented in definition.Interfaces)
                if (!CollectDefaults(implemented.InterfaceType, faces, defaults)) return null;
        bool Implemented(string name, int parameters, int below)
        {
            foreach (var d in defaults)
                if (d.Overrides.Any(o => o.Name == name && o.Parameters.Count == parameters)) return true;
            for (int i = 0; i < below; i++)
                foreach (var m in chain[i].Definition.Methods)
                {
                    if (m.IsStatic) continue;
                    if (m.Name == name && m.Parameters.Count == parameters && !m.IsAbstract) return true;
                    if (m.HasOverrides && m.Overrides.Any(o => o.Name == name && o.Parameters.Count == parameters)) return true;
                }
            return false;
        }
        var missing = new List<MethodReference>();
        for (int level = 1; level < chain.Count; level++)
        {
            var (definition, declaring) = chain[level];
            if (!definition.IsAbstract) continue;
            foreach (var m in definition.Methods)
                if (m.IsAbstract && !m.IsStatic && !Implemented(m.Name, m.Parameters.Count, level))
                    missing.Add(Member(m, declaring));
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (definition, declaring) in chain)
            foreach (var implemented in definition.Interfaces)
            {
                var face = Substitute(implemented.InterfaceType, declaring);
                if (!AddInterface(face, seen, missing, Implemented, chain.Count)) return null;
            }
        return missing;
    }

    static bool AddInterface(TypeReference face, HashSet<string> seen, List<MethodReference> missing,
        Func<string, int, int, bool> implemented, int below)
    {
        if (!seen.Add(face.FullName)) return true;
        var definition = Resolve(face);
        if (definition == null) return false;
        foreach (var m in definition.Methods)
            if (m.IsAbstract && !m.IsStatic && !implemented(m.Name, m.Parameters.Count, below))
                missing.Add(Member(m, face));
        foreach (var inherited in definition.Interfaces)
            if (!AddInterface(Substitute(inherited.InterfaceType, face), seen, missing, implemented, below)) return false;
        return true;
    }

    static TypeDefinition Resolve(TypeReference reference)
    {
        try { return reference.Resolve(); }
        catch (AssemblyResolutionException) { return null; }
    }

    /// <summary>The non-abstract methods with explicit implementations (default implementations)
    /// of an interface and the interfaces it inherits; false when one does not resolve.</summary>
    static bool CollectDefaults(TypeReference face, HashSet<string> seen, List<MethodDefinition> into)
    {
        if (!seen.Add(face.FullName)) return true;
        var definition = Resolve(face);
        if (definition == null) return false;
        foreach (var m in definition.Methods)
            if (!m.IsAbstract && m.HasOverrides) into.Add(m);
        foreach (var inherited in definition.Interfaces)
            if (!CollectDefaults(inherited.InterfaceType, seen, into)) return false;
        return true;
    }

    /// <summary>A base or interface reference with the generic parameters of the type that names
    /// it replaced by that type's arguments.</summary>
    static TypeReference Substitute(TypeReference named, TypeReference by)
    {
        if (named is not GenericInstanceType generic || by is not GenericInstanceType arguments) return named;
        var result = new GenericInstanceType(generic.ElementType);
        foreach (var argument in generic.GenericArguments)
            result.GenericArguments.Add(argument is GenericParameter p && p.Type == GenericParameterType.Type && p.Position < arguments.GenericArguments.Count
                ? arguments.GenericArguments[p.Position]
                : argument);
        return result;
    }

    /// <summary>A type in a member's signature with the declaring type's generic parameters
    /// replaced by its arguments: the signature an implementation in the class has.</summary>
    static TypeReference Bound(TypeReference type, TypeReference declaring)
    {
        var arguments = (declaring as GenericInstanceType)?.GenericArguments;
        switch (type)
        {
            case GenericParameter p when p.Type == GenericParameterType.Type && arguments != null && p.Position < arguments.Count:
                return arguments[p.Position];
            case ByReferenceType r:
                return new ByReferenceType(Bound(r.ElementType, declaring));
            case ArrayType a:
                return new ArrayType(Bound(a.ElementType, declaring), a.Rank);
            case GenericInstanceType g:
                var result = new GenericInstanceType(g.ElementType);
                foreach (var argument in g.GenericArguments) result.GenericArguments.Add(Bound(argument, declaring));
                return result;
            default:
                return type;
        }
    }

    /// <summary>A reference to a member of a declaring type reference (generic or not).</summary>
    static MethodReference Member(MethodDefinition method, TypeReference declaring)
    {
        var reference = new MethodReference(method.Name, method.ReturnType, declaring)
        {
            HasThis = method.HasThis,
            ExplicitThis = method.ExplicitThis,
            CallingConvention = method.CallingConvention,
        };
        foreach (var p in method.Parameters) reference.Parameters.Add(new ParameterDefinition(p.ParameterType));
        foreach (var g in method.GenericParameters) reference.GenericParameters.Add(new GenericParameter(g.Name, reference));
        return reference;
    }

    /// <summary>Adds a throwing implementation of each missing member and makes the constructors
    /// and static methods throw: the type loads, its first use fails.</summary>
    static void Defer(ModuleDefinition module, TypeDefinition type, List<MethodReference> missing)
    {
        var exception = module.ImportReference(typeof(TypeLoadException).GetConstructor(new[] { typeof(string) }));
        var assemblyName = module.Assembly.FullName;
        string Message(MethodReference m) => $"Method '{m.Name}' in type '{type.FullName}' from assembly '{assemblyName}' does not have an implementation.";
        void Throw(ILProcessor il, Instruction before, string message)
        {
            il.InsertBefore(before, il.Create(OpCodes.Ldstr, message));
            il.InsertBefore(before, il.Create(OpCodes.Newobj, exception));
            il.InsertBefore(before, il.Create(OpCodes.Throw));
        }
        var first = Message(missing[0]);
        foreach (var method in type.Methods.ToList())
        {
            if (!method.HasBody || method.IsConstructor && method.IsStatic) continue;
            if (!method.IsConstructor && !method.IsStatic) continue;
            var il = method.Body.GetILProcessor();
            Throw(il, method.Body.Instructions[0], first);
        }
        foreach (var target in missing)
        {
            var imported = module.ImportReference(target);
            var stub = new MethodDefinition("<Unbound>Missing." + target.DeclaringType.Name + "." + target.Name,
                MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                module.ImportReference(Bound(target.ReturnType, target.DeclaringType)));
            foreach (var p in target.Parameters) stub.Parameters.Add(new ParameterDefinition(module.ImportReference(Bound(p.ParameterType, target.DeclaringType))));
            foreach (var g in target.GenericParameters) stub.GenericParameters.Add(new GenericParameter(g.Name, stub));
            stub.Overrides.Add(imported);
            var il = stub.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldstr, Message(target)));
            il.Append(il.Create(OpCodes.Newobj, exception));
            il.Append(il.Create(OpCodes.Throw));
            type.Methods.Add(stub);
        }
    }
}
