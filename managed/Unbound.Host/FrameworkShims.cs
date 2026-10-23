using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unbound.Host;

/// <summary>
/// Redirects calls to .NET Framework / Mono members that .NET lacks (Odin emits its formatters
/// with <c>AppDomain.DefineDynamicAssembly</c> and <c>DefineDynamicModule(string, bool)</c>) to
/// <see cref="Unbound.FrameworkCompat"/>, which takes the instance as its first parameter.
/// </summary>
static class FrameworkShims
{
    /// <summary>Member names worth a closer look: an assembly referencing none is left alone.</summary>
    static readonly HashSet<string> s_Names;
    static readonly System.Reflection.MethodInfo[] s_Shims;

    static FrameworkShims()
    {
        s_Shims = typeof(Unbound.FrameworkCompat).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        s_Names = new HashSet<string>(s_Shims.Select(m => m.Name), StringComparer.Ordinal);
    }

    internal static byte[] Redirect(byte[] image)
    {
        if (!ReferencesShimmedNames(image)) return image;
        using var input = new MemoryStream(image);
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = AccessChecks.Resolver });
        var module = assembly.MainModule;
        bool changed = false;
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) || instruction.Operand is not MethodReference target)
                        continue;
                    if (!s_Names.Contains(target.Name) || Exists(target)) continue;
                    var shim = FindShim(target);
                    if (shim == null) continue;
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = module.ImportReference(shim);
                    changed = true;
                }
            }
        }
        if (!changed) return image;
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }

    static bool ReferencesShimmedNames(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        if (!pe.HasMetadata) return false;
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.MemberReferences)
            if (s_Names.Contains(reader.GetString(reader.GetMemberReference(handle).Name))) return true;
        return false;
    }

    /// <summary>Whether .NET itself has the member (then the call stays as it is).</summary>
    static bool Exists(MethodReference target)
    {
        var type = Type.GetType(target.DeclaringType.FullName + ", " + RuntimeAssemblyOf(target.DeclaringType.FullName), false);
        if (type == null) return true; // not a framework type we know: leave it
        var parameters = target.Parameters.Select(p => p.ParameterType.FullName).ToArray();
        return type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Any(m => m.Name == target.Name && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameters));
    }

    static string RuntimeAssemblyOf(string typeName) =>
        typeName.StartsWith("System.Reflection.Emit.", StringComparison.Ordinal) ? "System.Private.CoreLib" : "System.Private.CoreLib";

    static System.Reflection.MethodInfo? FindShim(MethodReference target)
    {
        var wanted = new List<string>();
        if (target.HasThis) wanted.Add(target.DeclaringType.FullName);
        wanted.AddRange(target.Parameters.Select(p => p.ParameterType.FullName));
        return s_Shims.FirstOrDefault(m => m.Name == target.Name
            && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(wanted));
    }
}
