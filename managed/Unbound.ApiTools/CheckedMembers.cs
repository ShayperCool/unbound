using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Unbound.ApiTools;

/// <summary>
/// The engine members whose binding refuses a dead receiver, read from a release's managed
/// assemblies: an instance method that passes its own object to
/// <c>UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException</c> (the pattern of every
/// binding that marshals its object with <c>MarshalNotNull</c>), or calls such a method on its own
/// object. Game code's calls to these members check their receiver as the binding would
/// (Unbound.Host's lifetime checks read the list).
/// </summary>
public static class CheckedMembers
{
    public const string FileName = "checked-members.txt";
    // The list is published with the facades: a change of its output bumps FacadeGenerator.FormatVersion.

    const string ThrowHelper = "UnityEngine.Bindings.ThrowHelper";
    const string ThrowNullReference = "ThrowNullReferenceException";
    const string Marshaller = "UnityEngine.Object/MarshalledUnityObject";

    const string EngineObject = "UnityEngine.Object";

    /// <summary>The list's text: one <see cref="Key"/> per line, sorted. Only members of classes
    /// deriving from UnityEngine.Object: other classes with an engine pointer (SerializedObject,
    /// SerializedProperty, ...) use the same helper for their own pointer.</summary>
    public static string Compute(IEnumerable<byte[]> modules)
    {
        var direct = new HashSet<string>(StringComparer.Ordinal);
        var callers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var bases = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var bytes in modules)
            Scan(bytes, direct, callers, bases);
        // Members reaching a checking member through their own object check too.
        var result = new HashSet<string>(direct, StringComparer.Ordinal);
        var pending = new Queue<string>(direct);
        while (pending.Count > 0)
        {
            if (!callers.TryGetValue(pending.Dequeue(), out var list)) continue;
            foreach (var caller in list)
                if (result.Add(caller)) pending.Enqueue(caller);
        }
        bool IsEngineObject(string type)
        {
            for (int depth = 0; depth < 64 && type != null; depth++)
            {
                if (type == EngineObject) return true;
                if (!bases.TryGetValue(type, out var next) || next == null) return false;
                type = next;
            }
            return false;
        }
        var lines = result.Where(key => IsEngineObject(key.Substring(0, key.IndexOf("::", StringComparison.Ordinal)))).ToList();
        lines.Sort(StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (var line in lines) text.Append(line).Append('\n');
        return text.ToString();
    }

    /// <summary>A method's key: its declaring type (without type arguments), name, generic arity
    /// and parameter types, written as <see cref="TypeNames"/> writes them. Unbound.Host forms
    /// the same keys from game code's member references.</summary>
    public static string Key(string declaringType, string name, int genericArity, IEnumerable<string> parameters) =>
        declaringType + "::" + name + (genericArity > 0 ? "`" + genericArity : "") + "(" + string.Join(",", parameters) + ")";

    static void Scan(byte[] bytes, HashSet<string> direct, Dictionary<string, List<string>> callers, Dictionary<string, string?> bases)
    {
        using var pe = new PEReader(ImmutableArray.Create(bytes));
        if (!pe.HasMetadata) return;
        var md = pe.GetMetadataReader();
        var names = new TypeNames(md);
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            string typeName = names.Definition(typeHandle);
            bases[typeName] = type.BaseType.IsNil ? null : names.Parent(type.BaseType);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if ((method.Attributes & System.Reflection.MethodAttributes.Static) != 0 || method.RelativeVirtualAddress == 0) continue;
                var signature = method.DecodeSignature(names, null);
                string key = Key(typeName, md.GetString(method.Name), signature.GenericParameterCount, signature.ParameterTypes);
                try { Walk(md, names, pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader(), key, direct, callers); }
                catch (BadImageFormatException) { } // an unreadable body checks nothing
            }
        }
    }

    /// <summary>One pass over the body from its entry, following which stack slots hold the
    /// method's own object (<c>ldarg.0</c>, also duplicated). Only what happens first counts: the
    /// binding pattern (the object marshalled, then the throw helper on the object, branches on
    /// the marshalled pointer between them) or a call on the object itself before any branch,
    /// throw, object creation or other engine call (framework helpers such as <c>typeof</c> may
    /// come first). A member that decides something before it reaches its object (argument
    /// checks, its own messages) is left out, so a check at its call never comes before Unity's
    /// own earlier answer.</summary>
    static void Walk(MetadataReader md, TypeNames names, BlobReader il, string key,
        HashSet<string> direct, Dictionary<string, List<string>> callers)
    {
        var stack = new List<bool>();
        bool marshalled = false;
        while (il.RemainingBytes > 0)
        {
            byte first = il.ReadByte();
            OpCode? decoded = first == 0xFE ? (il.RemainingBytes > 0 ? OpCodeTable.TwoByte[il.ReadByte()] : null) : OpCodeTable.OneByte[first];
            if (decoded is not { } op) return;
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
            {
                if (il.RemainingBytes < 4) return;
                var handle = MetadataTokens.EntityHandle(il.ReadInt32());
                var callee = Callee(md, names, handle);
                if (callee == null) return;
                var (calleeKey, calleeType, calleeName, parameters, hasThis, returns) = callee.Value;
                int popped = parameters + (hasThis && op != OpCodes.Newobj ? 1 : 0);
                bool receiverIsThis = hasThis && op != OpCodes.Newobj && stack.Count >= popped && stack[stack.Count - popped];
                bool argumentIsThis = !hasThis && parameters == 1 && stack.Count >= 1 && stack[^1];
                if (calleeType == ThrowHelper && calleeName == ThrowNullReference && argumentIsThis)
                {
                    if (marshalled) direct.Add(key);
                    return;
                }
                if (calleeType == Marshaller && !hasThis) marshalled = true;
                else if (receiverIsThis && !marshalled)
                {
                    if (!callers.TryGetValue(calleeKey, out var list)) callers[calleeKey] = list = new List<string>();
                    list.Add(key);
                    return;
                }
                else if (!calleeType.StartsWith("System.", StringComparison.Ordinal)) return;
                Pop(stack, popped);
                if (op == OpCodes.Newobj || returns) stack.Add(false);
                continue;
            }
            if (!SkipOperand(ref il, op)) return;
            if (op == OpCodes.Ldarg_0) { stack.Add(true); continue; }
            if (op == OpCodes.Dup) { stack.Add(stack.Count > 0 && stack[^1]); continue; }
            if (op.FlowControl is FlowControl.Return or FlowControl.Throw) return;
            if (op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
            {
                // Only the binding's own test of its marshalled pointer branches before the check.
                if (!marshalled) return;
                Pop(stack, Math.Max(0, Pops(op.StackBehaviourPop)));
                continue;
            }
            int pops = Pops(op.StackBehaviourPop), pushes = Pushes(op.StackBehaviourPush);
            if (pops < 0 || pushes < 0) return;
            Pop(stack, pops);
            for (int i = 0; i < pushes; i++) stack.Add(false);
        }
    }

    static void Pop(List<bool> stack, int count)
    {
        if (count >= stack.Count) stack.Clear();
        else stack.RemoveRange(stack.Count - count, count);
    }

    /// <summary>The called method's key, declaring type and name, parameter count, whether it
    /// has an object and a result; null for a call through a signature (calli has its own opcode).</summary>
    static (string Key, string Type, string Name, int Parameters, bool HasThis, bool Returns)? Callee(MetadataReader md, TypeNames names, EntityHandle handle)
    {
        int arity = 0;
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
            handle = spec.Method;
        }
        string type, name;
        MethodSignature<string> signature;
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var definition = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                type = names.Definition(definition.GetDeclaringType());
                name = md.GetString(definition.Name);
                signature = definition.DecodeSignature(names, null);
                break;
            case HandleKind.MemberReference:
                var reference = md.GetMemberReference((MemberReferenceHandle)handle);
                if (reference.GetKind() != MemberReferenceKind.Method) return null;
                type = names.Parent(reference.Parent);
                if (type == null) return null;
                name = md.GetString(reference.Name);
                signature = reference.DecodeMethodSignature(names, null);
                break;
            default:
                return null;
        }
        arity = signature.GenericParameterCount;
        return (Key(type, name, arity, signature.ParameterTypes), type, name, signature.ParameterTypes.Length,
            signature.Header.IsInstance, signature.ReturnType != "System.Void");
    }

    static int Pops(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Pop0 => 0,
        StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
        StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
            or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
        StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
            or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref
            or StackBehaviour.Popref_popi_pop1 => 3,
        _ => -1,
    };

    static int Pushes(StackBehaviour behaviour) => behaviour switch
    {
        StackBehaviour.Push0 => 0,
        StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8
            or StackBehaviour.Pushref => 1,
        StackBehaviour.Push1_push1 => 2,
        _ => -1,
    };

    /// <summary>Moves past the operand; false when the body ends inside it.</summary>
    static bool SkipOperand(ref BlobReader il, OpCode op)
    {
        int size;
        switch (op.OperandType)
        {
            case OperandType.InlineNone: return true;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar: size = 1; break;
            case OperandType.InlineVar: size = 2; break;
            case OperandType.InlineI8:
            case OperandType.InlineR: size = 8; break;
            case OperandType.InlineSwitch:
                if (il.RemainingBytes < 4) return false;
                long targets = (long)il.ReadUInt32() * 4;
                if (targets > il.RemainingBytes) return false;
                size = (int)targets;
                break;
            default: size = 4; break;
        }
        if (size > il.RemainingBytes) return false;
        il.Offset += size;
        return true;
    }

    static class OpCodeTable
    {
        internal static readonly OpCode?[] OneByte = new OpCode?[256];
        internal static readonly OpCode?[] TwoByte = new OpCode?[256];

        static OpCodeTable()
        {
            foreach (var field in typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode op) continue;
                var value = (ushort)op.Value;
                if (op.Size == 1) OneByte[value] = op;
                else TwoByte[value & 0xFF] = op;
            }
        }
    }

    /// <summary>Type names as both sides write them: namespace and name ("/" before a nested
    /// type), type arguments in angle brackets, "[]", "[,]", "&amp;" and "*", "!n" and "!!n" for
    /// type and method parameters, modifiers dropped.</summary>
    public sealed class TypeNames : ISignatureTypeProvider<string, object?>
    {
        readonly MetadataReader m_Reader;
        public TypeNames(MetadataReader reader) => m_Reader = reader;

        public string Definition(TypeDefinitionHandle handle)
        {
            var type = m_Reader.GetTypeDefinition(handle);
            string name = m_Reader.GetString(type.Name);
            var outer = type.GetDeclaringType();
            if (!outer.IsNil) return Definition(outer) + "/" + name;
            string ns = m_Reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        string Reference(TypeReferenceHandle handle)
        {
            var type = m_Reader.GetTypeReference(handle);
            string name = m_Reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return Reference((TypeReferenceHandle)type.ResolutionScope) + "/" + name;
            string ns = m_Reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        /// <summary>A member reference's declaring type, without type arguments.</summary>
        public string? Parent(EntityHandle parent) => parent.Kind switch
        {
            HandleKind.TypeDefinition => Definition((TypeDefinitionHandle)parent),
            HandleKind.TypeReference => Reference((TypeReferenceHandle)parent),
            HandleKind.TypeSpecification => Strip(m_Reader.GetTypeSpecification((TypeSpecificationHandle)parent).DecodeSignature(this, null)),
            _ => null,
        };

        static string Strip(string name)
        {
            int open = name.IndexOf('<');
            return open < 0 ? name : name.Substring(0, open);
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode switch
        {
            PrimitiveTypeCode.IntPtr => "IntPtr",
            PrimitiveTypeCode.UIntPtr => "UIntPtr",
            PrimitiveTypeCode.TypedReference => "TypedReference",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Definition(handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Reference(handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "method";
    }
}
