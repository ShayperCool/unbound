using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Mono.Cecil;
using AssemblyDefinition = Mono.Cecil.AssemblyDefinition;
using CustomAttribute = Mono.Cecil.CustomAttribute;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using TypeDefinition = Mono.Cecil.TypeDefinition;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Unbound.Host;

/// <summary>
/// Mono lets an assembly that asks to skip verification (<c>SecurityPermission(SkipVerification)</c>)
/// use the engine's internal types, and plugins such as Odin rely on it. .NET enforces type and
/// member access unless the accessing assembly carries <c>IgnoresAccessChecksTo</c>, so such
/// assemblies get one for every assembly they reference, the engine included.
/// </summary>
static class AccessChecks
{
    const string AttributeNamespace = "System.Runtime.CompilerServices";
    const string AttributeName = "IgnoresAccessChecksToAttribute";

    internal static DefaultAssemblyResolver Resolver => s_Resolver;

    static DefaultAssemblyResolver s_Resolver = new DefaultAssemblyResolver();

    /// <summary>Where the rewrite finds referenced assemblies (writing attributes may need their types).</summary>
    internal static void SetSearchDirectories(IEnumerable<string> directories)
    {
        s_Resolver.Dispose();
        s_Resolver = new DefaultAssemblyResolver();
        foreach (var d in s_Resolver.GetSearchDirectories()) s_Resolver.RemoveSearchDirectory(d);
        foreach (var d in directories)
            if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) s_Resolver.AddSearchDirectory(d);
    }

    internal static byte[] Relax(byte[] image)
    {
        if (!SkipsVerification(image)) return image;
        using var input = new MemoryStream(image);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = s_Resolver });
        var module = assembly.MainModule;
        var attribute = DefineAttribute(module);
        var targets = new SortedSet<string>(StringComparer.Ordinal) { typeof(UnityEngine.Object).Assembly.GetName().Name! };
        foreach (var reference in module.AssemblyReferences) targets.Add(reference.Name);
        foreach (var target in targets)
        {
            var custom = new CustomAttribute(attribute);
            custom.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, target));
            assembly.CustomAttributes.Add(custom);
        }
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }

    /// <summary>Whether the assembly's permission requests include SkipVerification.</summary>
    static bool SkipsVerification(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        if (!pe.HasMetadata) return false;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) return false;
        foreach (var handle in reader.GetAssemblyDefinition().GetDeclarativeSecurityAttributes())
        {
            var blob = reader.GetBlobBytes(reader.GetDeclarativeSecurityAttribute(handle).PermissionSet);
            if (Encoding.ASCII.GetString(blob).Contains("SkipVerification", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The attribute's constructor: .NET recognises the attribute by name in any assembly.</summary>
    static MethodReference DefineAttribute(Mono.Cecil.ModuleDefinition module)
    {
        var baseType = module.ImportReference(typeof(Attribute));
        var type = new TypeDefinition(AttributeNamespace, AttributeName,
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit | TypeAttributes.Class, baseType);
        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition("assemblyName", ParameterAttributes.None, module.TypeSystem.String));
        var il = ctor.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, module.ImportReference(typeof(Attribute).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)!));
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        type.Methods.Add(ctor);
        module.Types.Add(type);
        return ctor;
    }
}
