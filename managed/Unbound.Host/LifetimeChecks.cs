using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unbound.Runtime;

namespace Unbound.Host;

/// <summary>
/// Game code's calls to engine members whose Unity binding refuses a dead receiver check it
/// first, as the binding does: such a member of a destroyed object, or of a placeholder the Editor
/// makes for an unassigned reference or a missing component, throws its exception
/// (<see cref="Unbound.Runtime.Lifetime"/>). Unbound's own members implement only some of these
/// checks, and its engine code reads members of dead objects in its bookkeeping, so the check
/// sits at the game's call: each call or callvirt of a listed member goes through a small static
/// method of the game assembly (type <see cref="Unbound.Runtime.Lifetime.CallsType"/>) that checks
/// the receiver and makes the original call. The list is read from the release's managed
/// assemblies with the facades (Unbound.ApiTools.CheckedMembers). Calls through a constrained
/// prefix, delegates and members of generic engine types stay as they are.
/// </summary>
static class LifetimeChecks
{
    const string ListFile = "checked-members.txt";

    static HashSet<string> s_Checked = new(StringComparer.Ordinal);

    /// <summary>The loaded list's identity, part of the adapted images' cache key.</summary>
    internal static string Identity { get; private set; } = "none";

    /// <summary>Reads the list generated with the release's facades; without one nothing is checked.</summary>
    internal static void Load(string facadeDir)
    {
        var path = string.IsNullOrEmpty(facadeDir) ? null : Path.Combine(facadeDir, ListFile);
        if (path == null || !File.Exists(path))
        {
            s_Checked = new HashSet<string>(StringComparer.Ordinal);
            Identity = "none";
            return;
        }
        var text = File.ReadAllText(path);
        s_Checked = new HashSet<string>(text.Split('\n', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        Identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8);
    }

    /// <summary>For tests: the members to check.</summary>
    internal static void Set(IEnumerable<string> members)
    {
        s_Checked = new HashSet<string>(members, StringComparer.Ordinal);
        Identity = "set";
    }

    /// <summary><see cref="Instrument"/>, or the image as it is (reported) when it cannot be
    /// rewritten: the assembly still loads, without the receiver checks.</summary>
    internal static byte[] TryInstrument(byte[] image)
    {
        if (s_Checked.Count == 0) return image;
        try
        {
            return Instrument(image);
        }
        catch (Exception e)
        {
            string name;
            try { name = ModuleDefinition.ReadModule(new MemoryStream(image)).Assembly.Name.Name; }
            catch { name = "an assembly"; }
            Native.Log((int)UnityEngine.LogType.Warning,
                $"[Unbound] {name}: cannot check its calls' dead receivers as Unity's bindings do ({e.GetType().Name}: {e.Message})");
            return image;
        }
    }

    internal static byte[] Instrument(byte[] image)
    {
        using var input = new MemoryStream(image);
        using var assembly = AccessChecks.Read(input);
        var module = assembly.MainModule;
        if (module.GetType(Unbound.Runtime.Lifetime.CallsType) != null) return image;
        var calls = new Calls(module);
        foreach (var type in module.GetTypes().ToList())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) || instruction.Operand is not MethodReference target)
                        continue;
                    if (!target.HasThis || instruction.Previous?.OpCode == OpCodes.Constrained || instruction.Previous?.OpCode == OpCodes.Tail)
                        continue;
                    var element = target is GenericInstanceMethod generic ? generic.ElementMethod : target;
                    if (element.DeclaringType is GenericInstanceType || element.DeclaringType.IsArray) continue;
                    if (!s_Checked.Contains(Key(element))) continue;
                    instruction.Operand = calls.For(target, instruction.OpCode);
                    instruction.OpCode = OpCodes.Call;
                }
            }
        }
        if (!calls.Any) return image;
        using var output = new MemoryStream();
        AccessChecks.Write(assembly, output);
        return output.ToArray();
    }

    /// <summary>The key Unbound.ApiTools.CheckedMembers writes for the member.</summary>
    internal static string Key(MethodReference method)
    {
        var declaring = method.DeclaringType is GenericInstanceType instance ? instance.ElementType : method.DeclaringType;
        var text = new StringBuilder(Name(declaring)).Append("::").Append(method.Name);
        if (method.GenericParameters.Count > 0) text.Append('`').Append(method.GenericParameters.Count);
        text.Append('(');
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            if (i > 0) text.Append(',');
            text.Append(Name(method.Parameters[i].ParameterType));
        }
        return text.Append(')').ToString();
    }

    /// <summary>A type as Unbound.ApiTools.CheckedMembers.TypeNames writes it.</summary>
    static string Name(TypeReference type) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        ArrayType array => Name(array.ElementType) + "[" + new string(',', Math.Max(0, array.Rank - 1)) + "]",
        ByReferenceType reference => Name(reference.ElementType) + "&",
        PointerType pointer => Name(pointer.ElementType) + "*",
        PinnedType pinned => Name(pinned.ElementType),
        RequiredModifierType required => Name(required.ElementType),
        OptionalModifierType optional => Name(optional.ElementType),
        GenericInstanceType instance => Name(instance.ElementType) + "<" + string.Join(",", instance.GenericArguments.Select(Name)) + ">",
        FunctionPointerType => "method",
        _ when type.IsNested => Name(type.DeclaringType) + "/" + type.Name,
        _ => string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name,
    };

    /// <summary>The checked calls of one module: one static method per member and call kind.</summary>
    sealed class Calls
    {
        readonly ModuleDefinition m_Module;
        readonly Dictionary<(string, bool), MethodDefinition> m_Made = new();
        TypeDefinition? m_Holder;
        MethodReference? m_Check;
        MethodReference? m_Hidden;

        internal Calls(ModuleDefinition module) => m_Module = module;

        internal bool Any => m_Holder != null;

        internal MethodReference For(MethodReference target, OpCode call)
        {
            var element = target is GenericInstanceMethod instance ? instance.ElementMethod : target;
            bool virtualCall = call == OpCodes.Callvirt;
            var key = (element.FullName, virtualCall);
            if (!m_Made.TryGetValue(key, out var wrapper)) m_Made[key] = wrapper = Make(element, virtualCall);
            if (target is not GenericInstanceMethod generic) return wrapper;
            var instantiated = new GenericInstanceMethod(wrapper);
            foreach (var argument in generic.GenericArguments) instantiated.GenericArguments.Add(argument);
            return instantiated;
        }

        MethodDefinition Make(MethodReference element, bool virtualCall)
        {
            if (m_Holder == null)
            {
                m_Holder = new TypeDefinition("", Unbound.Runtime.Lifetime.CallsType,
                    TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    m_Module.TypeSystem.Object);
                m_Module.Types.Add(m_Holder);
                m_Check = m_Module.ImportReference(typeof(Unbound.Runtime.Lifetime).GetMethod(nameof(Unbound.Runtime.Lifetime.Check))!);
                m_Hidden = m_Module.ImportReference(typeof(System.Diagnostics.StackTraceHiddenAttribute).GetConstructor(Type.EmptyTypes)!);
            }
            var wrapper = new MethodDefinition(element.Name + "_" + m_Made.Count,
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, m_Module.TypeSystem.Void);
            m_Holder.Methods.Add(wrapper);
            // Its frame stays out of stack traces: the trace reads as the game's own call.
            wrapper.CustomAttributes.Add(new CustomAttribute(m_Hidden!));
            // The wrapper's own type parameters stand for the member's, with its constraints.
            var resolved = element.GenericParameters.Count > 0 ? element.Resolve() : null;
            for (int i = 0; i < element.GenericParameters.Count; i++)
                wrapper.GenericParameters.Add(new GenericParameter(element.GenericParameters[i].Name, wrapper));
            for (int i = 0; i < element.GenericParameters.Count; i++)
            {
                var source = resolved?.GenericParameters[i];
                if (source == null) continue;
                wrapper.GenericParameters[i].Attributes = source.Attributes;
                foreach (var constraint in source.Constraints)
                    wrapper.GenericParameters[i].Constraints.Add(new GenericParameterConstraint(Substitute(m_Module.ImportReference(constraint.ConstraintType), wrapper)));
            }
            wrapper.ReturnType = Substitute(element.ReturnType, wrapper);
            wrapper.Parameters.Add(new ParameterDefinition("receiver", ParameterAttributes.None, element.DeclaringType));
            foreach (var parameter in element.Parameters)
                wrapper.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None, Substitute(parameter.ParameterType, wrapper)));
            MethodReference inner = element;
            if (element.GenericParameters.Count > 0)
            {
                var instance = new GenericInstanceMethod(element);
                foreach (var parameter in wrapper.GenericParameters) instance.GenericArguments.Add(parameter);
                inner = instance;
            }
            var il = wrapper.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, m_Check!);
            for (int i = 0; i < wrapper.Parameters.Count; i++) il.Emit(OpCodes.Ldarg, wrapper.Parameters[i]);
            il.Emit(virtualCall ? OpCodes.Callvirt : OpCodes.Call, inner);
            il.Emit(OpCodes.Ret);
            return wrapper;
        }

        /// <summary>The type with the member's method type parameters replaced by the wrapper's.</summary>
        static TypeReference Substitute(TypeReference type, MethodDefinition wrapper) => type switch
        {
            GenericParameter { Type: GenericParameterType.Method } parameter => wrapper.GenericParameters[parameter.Position],
            ArrayType array => new ArrayType(Substitute(array.ElementType, wrapper), array.Rank),
            ByReferenceType reference => new ByReferenceType(Substitute(reference.ElementType, wrapper)),
            PointerType pointer => new PointerType(Substitute(pointer.ElementType, wrapper)),
            RequiredModifierType required => new RequiredModifierType(required.ModifierType, Substitute(required.ElementType, wrapper)),
            OptionalModifierType optional => new OptionalModifierType(optional.ModifierType, Substitute(optional.ElementType, wrapper)),
            GenericInstanceType instance => SubstituteInstance(instance, wrapper),
            _ => type,
        };

        static TypeReference SubstituteInstance(GenericInstanceType instance, MethodDefinition wrapper)
        {
            var copy = new GenericInstanceType(instance.ElementType);
            foreach (var argument in instance.GenericArguments) copy.GenericArguments.Add(Substitute(argument, wrapper));
            return copy;
        }
    }
}
