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
/// <see cref="Unbound.FrameworkCompat"/>, which takes the instance as its first parameter, and
/// members .NET has with other answers than the Editor's Mono (<see cref="Unbound.EditorProcess"/>,
/// <see cref="Unbound.MonoStopwatch"/>).
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
        foreach (var replaced in s_Replaced.Keys) s_Names.Add(replaced.Name);
        foreach (var replaced in s_ReplacedFields.Keys) s_Names.Add(replaced.Name);
        foreach (var replaced in s_ReplacedWithArguments.Keys) s_Names.Add(replaced.Name);
    }

    /// <summary>Members .NET has that must answer as in the Editor's process instead: the
    /// command line is Unity's (the Test Framework reads -runTests from it), Stopwatch counts
    /// Mono's 100 ns ticks, types and assemblies have Mono's names, and a game assembly names the file
    /// the game sees. Parameterless members only; an instance member's replacement takes
    /// the instance.</summary>
    static readonly Dictionary<(string Type, string Name), System.Reflection.MethodInfo> s_Replaced = new()
    {
        [("System.Environment", "GetCommandLineArgs")] = typeof(Unbound.EditorProcess).GetMethod(nameof(Unbound.EditorProcess.GetCommandLineArgs))!,
        [("System.Environment", "get_CommandLine")] = typeof(Unbound.EditorProcess).GetMethod(nameof(Unbound.EditorProcess.get_CommandLine))!,
        [("System.Diagnostics.Stopwatch", "GetTimestamp")] = typeof(Unbound.MonoStopwatch).GetMethod(nameof(Unbound.MonoStopwatch.GetTimestamp))!,
        [("System.Diagnostics.Stopwatch", "get_ElapsedTicks")] = typeof(Unbound.MonoStopwatch).GetMethod(nameof(Unbound.MonoStopwatch.get_ElapsedTicks))!,
        // Type and assembly names as the Editor's Mono gives them (tests/projects/typenames).
        [("System.Type", "get_AssemblyQualifiedName")] = typeof(Unbound.MonoTypeNames).GetMethod(nameof(Unbound.MonoTypeNames.get_AssemblyQualifiedName))!,
        [("System.Type", "get_FullName")] = typeof(Unbound.MonoTypeNames).GetMethod(nameof(Unbound.MonoTypeNames.get_FullName), new[] { typeof(Type) })!,
        [("System.Reflection.Assembly", "get_FullName")] = typeof(Unbound.MonoTypeNames).GetMethod(nameof(Unbound.MonoTypeNames.get_FullName), new[] { typeof(System.Reflection.Assembly) })!,
        [("System.Reflection.Assembly", "GetName")] = typeof(Unbound.MonoTypeNames).GetMethod(nameof(Unbound.MonoTypeNames.GetName))!,
        // The file a game assembly names as its own (tests/projects/compilesetup location.*).
        [("System.Reflection.Assembly", "get_Location")] = typeof(Unbound.AssemblyFiles).GetMethod(nameof(Unbound.AssemblyFiles.get_Location))!,
        [("System.Reflection.Assembly", "get_CodeBase")] = typeof(Unbound.AssemblyFiles).GetMethod(nameof(Unbound.AssemblyFiles.get_CodeBase))!,
    };

    /// <summary>Virtual members of <see cref="s_Replaced"/>: only their virtual calls are redirected; a
    /// non-virtual call (a subclass's <c>base.FullName</c>) and a delegate of the member stay.</summary>
    static readonly HashSet<(string Type, string Name)> s_VirtualOnly = new()
    {
        ("System.Type", "get_AssemblyQualifiedName"), ("System.Type", "get_FullName"),
        ("System.Reflection.Assembly", "get_FullName"), ("System.Reflection.Assembly", "GetName"),
        ("System.Reflection.Assembly", "get_Location"), ("System.Reflection.Assembly", "get_CodeBase"),
    };

    /// <summary>Members .NET has, with parameters, that must answer as the Editor's Mono does: the
    /// replacement takes the same parameters (a generic method's the same type arguments).</summary>
    static readonly Dictionary<(string Type, string Name), System.Reflection.MethodInfo[]> s_ReplacedWithArguments = new()
    {
        [("System.Runtime.InteropServices.Marshal", "GetFunctionPointerForDelegate")] =
            typeof(Unbound.MonoMarshal).GetMethods().Where(m => m.Name == nameof(Unbound.MonoMarshal.GetFunctionPointerForDelegate)).ToArray(),
    };

    /// <summary>Keep field loads and escaping readonly references on stable shared storage.</summary>
    static readonly Dictionary<(string Type, string Name), System.Reflection.FieldInfo> s_ReplacedFields = new()
    {
        [("System.Diagnostics.Stopwatch", "Frequency")] = typeof(Unbound.MonoStopwatch).GetField(nameof(Unbound.MonoStopwatch.Frequency))!,
    };

    internal static byte[] Redirect(byte[] image)
    {
        if (!ReferencesShimmedNames(image)) return image;
        using var input = new MemoryStream(image);
        using var assembly = AccessChecks.Read(input);
        var module = assembly.MainModule;
        bool changed = false;
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if ((instruction.OpCode == OpCodes.Ldsfld || instruction.OpCode == OpCodes.Ldsflda)
                        && instruction.Operand is FieldReference field
                        && s_ReplacedFields.TryGetValue((field.DeclaringType.FullName, field.Name), out var replacementField))
                    {
                        instruction.Operand = module.ImportReference(replacementField);
                        changed = true;
                        continue;
                    }
                    if (instruction.OpCode == OpCodes.Ldftn && instruction.Operand is MethodReference pointed
                        && pointed.Parameters.Count == 0 && !s_VirtualOnly.Contains((pointed.DeclaringType.FullName, pointed.Name))
                        && s_Replaced.TryGetValue((pointed.DeclaringType.FullName, pointed.Name), out var pointedReplacement))
                    {
                        // A delegate made of the member (`Func<string[]> f = Environment.GetCommandLineArgs`).
                        instruction.Operand = module.ImportReference(pointedReplacement);
                        changed = true;
                        continue;
                    }
                    if (instruction.OpCode == OpCodes.Ldftn && instruction.Operand is MethodReference group
                        && ReplacedWithArguments(module, group) is MethodReference groupReplacement)
                    {
                        // A delegate made of a member with parameters
                        // (`Func<Delegate, IntPtr> f = Marshal.GetFunctionPointerForDelegate`).
                        instruction.Operand = groupReplacement;
                        changed = true;
                        continue;
                    }
                    if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) || instruction.Operand is not MethodReference target)
                        continue;
                    if (!s_Names.Contains(target.Name)) continue;
                    if (target.Parameters.Count == 0 && s_Replaced.TryGetValue((target.DeclaringType.FullName, target.Name), out var replacement)
                        && !(instruction.OpCode == OpCodes.Call && s_VirtualOnly.Contains((target.DeclaringType.FullName, target.Name))))
                    {
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = module.ImportReference(replacement);
                        changed = true;
                        continue;
                    }
                    if (ReplacedWithArguments(module, target) is MethodReference withArguments)
                    {
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = withArguments;
                        changed = true;
                        continue;
                    }
                    var shim = FindShim(target);
                    if (shim == null || Exists(shim)) continue;
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = module.ImportReference(shim);
                    changed = true;
                }
            }
        }
        if (!changed) return image;
        using var output = new MemoryStream();
        AccessChecks.Write(assembly, output);
        return output.ToArray();
    }

    /// <summary>The replacement of a call to a member in <see cref="s_ReplacedWithArguments"/>: the
    /// shim of the same generic arity and parameter count, made generic with the call's type
    /// arguments; null for other members.</summary>
    static MethodReference? ReplacedWithArguments(Mono.Cecil.ModuleDefinition module, MethodReference target)
    {
        var element = target is GenericInstanceMethod instance ? instance.ElementMethod : target;
        if (!s_ReplacedWithArguments.TryGetValue((element.DeclaringType.FullName, element.Name), out var shims)) return null;
        int arity = target is GenericInstanceMethod generic ? generic.GenericArguments.Count : 0;
        var shim = shims.FirstOrDefault(m => m.GetGenericArguments().Length == arity && m.GetParameters().Length == target.Parameters.Count);
        if (shim == null) return null;
        var imported = module.ImportReference(shim);
        if (target is not GenericInstanceMethod call) return imported;
        var replacement = new GenericInstanceMethod(imported);
        foreach (var argument in call.GenericArguments) replacement.GenericArguments.Add(argument);
        return replacement;
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

    /// <summary>Check the known framework receiver from the shim's signature. Cecil's names
    /// for arbitrary declaring types cannot be passed to the reflection type-name parser.</summary>
    static bool Exists(System.Reflection.MethodInfo shim)
    {
        var parameters = shim.GetParameters();
        var receiver = parameters[0].ParameterType;
        return receiver.GetMethod(shim.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            parameters.Skip(1).Select(p => p.ParameterType).ToArray()) != null;
    }

    static System.Reflection.MethodInfo? FindShim(MethodReference target)
    {
        if (!target.HasThis) return null;
        var wanted = target.Parameters.Select(p => p.ParameterType.FullName).Prepend(target.DeclaringType.FullName).ToArray();
        return s_Shims.FirstOrDefault(m => m.Name == target.Name
            && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(wanted));
    }
}
