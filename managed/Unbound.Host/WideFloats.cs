using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unbound.Runtime;
using GenericParameter = Mono.Cecil.GenericParameter;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;
using MemberReference = Mono.Cecil.MemberReference;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using TypeDefinition = Mono.Cecil.TypeDefinition;
using TypeReference = Mono.Cecil.TypeReference;
using TypeSpecification = Mono.Cecil.TypeSpecification;

namespace Unbound.Host;

/// <summary>
/// Game code evaluates float arithmetic as the Editor's Mono does (<see cref="Compat.ScriptFloatsWide"/>):
/// a float32 value on the evaluation stack is a double, so arithmetic, comparisons and conversions of
/// float expressions keep that precision, and it is rounded to float32 where the IL stores it into a
/// float32 location: a local, an argument, a field, an array element, an indirect store, a boxed value,
/// a call's argument or a method's return value. <c>box</c> immediately followed by <c>unbox.any</c> of
/// float32 keeps the value as it is. CoreCLR evaluates float32 arithmetic in float32 instead, so the
/// rewrite converts every float32 value pushed onto the stack to double (<c>conv.r8</c>, exact); the
/// runtime rounds a double stored into a float32 location or passed as a float32 argument, as ECMA-335
/// III.1.1.1 and III.1.6 have it. Methods Burst compiles keep float32 arithmetic (Burst follows its own
/// rules in the Editor).
/// </summary>
static partial class WideFloats
{
    const string BurstCompile = "Unity.Burst.BurstCompileAttribute";

    /// <summary><see cref="Widen"/>, or the image as it is (reported) when it cannot be rewritten:
    /// the assembly still loads, evaluating float32 arithmetic in float32.</summary>
    internal static byte[] TryWiden(byte[] image)
    {
        try
        {
            return Widen(image);
        }
        catch (Exception e)
        {
            string name;
            try { name = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(image)).Assembly.Name.Name; }
            catch { name = "an assembly"; }
            Native.Log((int)UnityEngine.LogType.Warning,
                $"[Unbound] {name}: cannot evaluate its float arithmetic as the Editor's Mono does, so it rounds every float operation ({e.GetType().Name}: {e.Message})");
            return image;
        }
    }

    /// <summary>The image with its float32 evaluation widened, or the same array when no method
    /// handles float32 values.</summary>
    internal static byte[] Widen(byte[] image) => Widen(image, whole: false);

    /// <summary><see cref="Widen(byte[])"/>; <paramref name="whole"/> widens every float32 value
    /// of every method, as when a method's stack cannot be followed.</summary>
    internal static byte[] Widen(byte[] image, bool whole)
    {
        using var input = new MemoryStream(image);
        using var assembly = AccessChecks.Read(input);
        bool changed = false;
        foreach (var type in assembly.MainModule.GetTypes())
            foreach (var method in type.Methods)
                if (method.HasBody && !CompiledByBurst(method))
                    changed |= WidenBody(method, whole);
        if (!changed) return image;
        using var output = new MemoryStream();
        AccessChecks.Write(assembly, output);
        return output.ToArray();
    }

    static bool HasAttribute(ICustomAttributeProvider provider, string name) =>
        provider.HasCustomAttributes && provider.CustomAttributes.Any(a => a.AttributeType.FullName == name);

    /// <summary>A method Burst compiles: a [BurstCompile] method, or the Execute of a [BurstCompile]
    /// job struct (Unity's job interfaces, IJob*). The other methods of such types run as script
    /// code, as do the methods Burst-compiled code calls when script code calls them.</summary>
    static bool CompiledByBurst(MethodDefinition method)
    {
        if (HasAttribute(method, BurstCompile)) return true;
        var type = method.DeclaringType;
        return (method.Name == "Execute" || method.Name.EndsWith(".Execute", StringComparison.Ordinal)) && type.IsValueType
            && HasAttribute(type, BurstCompile) && type.Interfaces.Any(i => i.InterfaceType.Name.StartsWith("IJob", StringComparison.Ordinal));
    }

    /// <summary>Inserts the conversions into one method body; false when it needs none.</summary>
    internal static bool WidenBody(MethodDefinition method, bool whole = false)
    {
        var body = method.Body;
        var code = body.Instructions;
        HashSet<Instruction> widen;
        List<Instruction> unboxed;
        if (!whole && WideExpressions(method) is var (loads, pairs))
        {
            widen = loads;
            unboxed = pairs;
        }
        else
        {
            // The stack could not be followed: every float32 value becomes a double.
            widen = new HashSet<Instruction>();
            unboxed = new List<Instruction>();
            var targets = Targets(body);
            for (int i = 0; i < code.Count; i++)
            {
                var instruction = code[i];
                if (instruction.OpCode.Code == Code.Box && IsSingle(instruction.Operand as TypeReference) && i + 1 < code.Count
                    && code[i + 1].OpCode.Code == Code.Unbox_Any && IsSingle(code[i + 1].Operand as TypeReference)
                    && !targets.Contains(code[i + 1]))
                {
                    unboxed.Add(instruction);
                    unboxed.Add(code[++i]);
                    continue;
                }
                // A tail call's result goes straight to ret (which converts it to the return type).
                if (Pushes(method, instruction) && instruction.Previous is not { OpCode.Code: Code.Tail }) widen.Add(instruction);
            }
        }
        if (widen.Count == 0 && unboxed.Count == 0) return false;
        body.SimplifyMacros();
        foreach (var instruction in unboxed)
        {
            // Boxing straight back keeps the value: neither instruction is needed.
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }
        // Each conversion follows its instruction, so only the path through that instruction
        // reaches it: no branch or handler bound needs to move. Inserting in place keeps Cecil's
        // debug information bound to the original instructions.
        var rebuilt = new List<Instruction>(code.Count + widen.Count);
        foreach (var instruction in code)
        {
            rebuilt.Add(instruction);
            if (widen.Contains(instruction)) rebuilt.Add(Instruction.Create(OpCodes.Conv_R8));
        }
        for (int i = 0; i < rebuilt.Count; i++)
            if (i >= code.Count || !ReferenceEquals(code[i], rebuilt[i])) code.Insert(i, rebuilt[i]);
        body.OptimizeMacros();
        return true;
    }

    static HashSet<Instruction> Targets(Mono.Cecil.Cil.MethodBody body)
    {
        var targets = new HashSet<Instruction>();
        foreach (var instruction in body.Instructions)
        {
            if (instruction.Operand is Instruction target) targets.Add(target);
            else if (instruction.Operand is Instruction[] cases) targets.UnionWith(cases);
        }
        foreach (var handler in body.ExceptionHandlers)
        {
            if (handler.HandlerStart != null) targets.Add(handler.HandlerStart);
            if (handler.FilterStart != null) targets.Add(handler.FilterStart);
            if (handler.TryStart != null) targets.Add(handler.TryStart);
        }
        return targets;
    }

    /// <summary>Whether the instruction pushes a float32 value.</summary>
    static bool Pushes(MethodDefinition method, Instruction instruction)
    {
        switch (instruction.OpCode.Code)
        {
            case Code.Ldc_R4:
            case Code.Conv_R4:
            case Code.Ldelem_R4:
            case Code.Ldind_R4:
                return true;
            case Code.Ldarg_0: return IsSingle(Argument(method, 0));
            case Code.Ldarg_1: return IsSingle(Argument(method, 1));
            case Code.Ldarg_2: return IsSingle(Argument(method, 2));
            case Code.Ldarg_3: return IsSingle(Argument(method, 3));
            case Code.Ldarg:
            case Code.Ldarg_S:
                return IsSingle((instruction.Operand as ParameterDefinition)?.ParameterType);
            case Code.Ldloc_0: return IsSingle(Local(method, 0));
            case Code.Ldloc_1: return IsSingle(Local(method, 1));
            case Code.Ldloc_2: return IsSingle(Local(method, 2));
            case Code.Ldloc_3: return IsSingle(Local(method, 3));
            case Code.Ldloc:
            case Code.Ldloc_S:
                return IsSingle((instruction.Operand as VariableDefinition)?.VariableType);
            case Code.Ldfld:
            case Code.Ldsfld:
                return instruction.Operand is FieldReference field && IsSingle(Resolve(field.FieldType, field));
            case Code.Ldelem_Any:
            case Code.Ldobj:
            case Code.Unbox_Any:
                return IsSingle(instruction.Operand as TypeReference);
            case Code.Call:
            case Code.Callvirt:
                return instruction.Operand is MethodReference callee && IsSingle(Resolve(callee.ReturnType, callee));
            case Code.Calli:
                return instruction.Operand is CallSite site && IsSingle(site.ReturnType);
            default:
                return false;
        }
    }

    static TypeReference? Argument(MethodDefinition method, int index)
    {
        if (method.HasThis)
        {
            if (index == 0) return null; // this: an object or a managed pointer
            index--;
        }
        return index < method.Parameters.Count ? method.Parameters[index].ParameterType : null;
    }

    static TypeReference? Local(MethodDefinition method, int index) =>
        index < method.Body.Variables.Count ? method.Body.Variables[index].VariableType : null;

    /// <summary>A member's type with the generic parameters of its declaring type or method
    /// instantiation substituted (a field of <c>Box&lt;float&gt;</c> declared as <c>T</c> holds a float32).</summary>
    static TypeReference Resolve(TypeReference type, MemberReference member)
    {
        if (type is not GenericParameter parameter) return type;
        if (parameter.Type == GenericParameterType.Method)
            return member is GenericInstanceMethod method && parameter.Position < method.GenericArguments.Count
                ? method.GenericArguments[parameter.Position] : type;
        return member.DeclaringType is GenericInstanceType declaring && parameter.Position < declaring.GenericArguments.Count
            ? declaring.GenericArguments[parameter.Position] : type;
    }

    /// <summary>System.Single, with any custom modifiers (<c>modreq(IsVolatile)</c>) or pinning.</summary>
    static bool IsSingle(TypeReference? type)
    {
        while (type is IModifierType or PinnedType)
            type = ((TypeSpecification)type).ElementType;
        if (type == null) return false;
        return type.MetadataType == MetadataType.Single
            || (type is not TypeSpecification && type is not GenericParameter && type.Name == "Single" && type.Namespace == "System");
    }

    // ------------------------------------------------------------------ hot-reload deltas

    /// <summary>
    /// Whether a hot-reload delta changes a method that <see cref="Widen"/> would convert: such an
    /// edit cannot be patched in place, as the conversions change the size of the delta's bodies.
    /// A body that handles no float32 value (in its signature, locals, opcodes or the members and
    /// types it references) needs none; the others are looked up by type, name and parameter count
    /// in <paramref name="edited"/> (the edited compilation's image, read when first needed) and
    /// analysed as <see cref="Widen"/> does. A method that cannot be looked up counts as needing
    /// conversions. <paramref name="baseline"/> is the loaded image and <paramref name="deltas"/>
    /// the metadata of every delta applied to it, this one last.
    /// </summary>
    internal static bool DeltaNeedsWidening(byte[] baseline, IReadOnlyList<byte[]> deltas, byte[] il, Func<byte[]?> edited)
    {
        using var metadata = new DeltaMetadata(baseline, deltas);
        var names = new SingleNames(metadata);
        Mono.Cecil.ModuleDefinition? module = null;
        ILookup<(string, string, int), MethodDefinition>? methods = null;
        try
        {
            foreach (var (method, offset, token) in metadata.Bodies())
            {
                if (!HandlesFloats(names, method, il, offset)) continue;
                var identity = metadata.Identity(token);
                if (identity == null) return true;
                if (methods == null)
                {
                    var image = edited();
                    if (image == null) return true;
                    module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(image));
                    methods = module.GetTypes().SelectMany(t => t.Methods).Where(m => m.HasBody)
                        .ToLookup(m => (m.DeclaringType.FullName, m.Name, m.Parameters.Count));
                }
                var candidates = methods[identity.Value].ToList();
                if (candidates.Count == 0) return true;
                foreach (var candidate in candidates)
                {
                    if (CompiledByBurst(candidate)) continue;
                    if (WideExpressions(candidate) is not var (loads, unboxed) || loads.Count > 0 || unboxed.Count > 0) return true;
                }
            }
            return false;
        }
        finally
        {
            module?.Dispose();
        }
    }

    static bool HandlesFloats(SingleNames names, System.Reflection.Metadata.MethodDefinition method, byte[] il, int offset)
    {
        if (names.Method(method.Signature)) return true;
        var (start, end, locals) = DeltaMetadata.Body(il, offset);
        if (locals != 0 && names.Token(locals)) return true;
        foreach (var (opcode, _, operand) in DeltaMetadata.Instructions(il, start, end))
        {
            if (s_SingleOpCodes.Contains(opcode)) return true;
            if (DeltaMetadata.HasToken(opcode) && opcode != 0x72 && names.Token(BitConverter.ToInt32(il, operand))) return true; // not ldstr
        }
        return false;
    }

    /// <summary>ldc.r4, ldind.r4, stind.r4, conv.r4, conv.r.un, ldelem.r4, stelem.r4.</summary>
    static readonly HashSet<int> s_SingleOpCodes = new() { 0x22, 0x4E, 0x56, 0x6B, 0x76, 0x98, 0xA0 };

    /// <summary>Whether the signatures of a delta's tokens name float32.</summary>
    sealed class SingleNames : ISignatureTypeProvider<bool, object?>
    {
        readonly DeltaMetadata m_Metadata;

        internal SingleNames(DeltaMetadata metadata) => m_Metadata = metadata;

        internal bool Method(BlobHandle signature)
        {
            var blob = m_Metadata.Blob(signature, out var reader);
            var decoded = new SignatureDecoder<bool, object?>(this, reader, null).DecodeMethodSignature(ref blob);
            return decoded.ReturnType || decoded.ParameterTypes.Any(p => p);
        }

        bool Field(BlobHandle signature)
        {
            var blob = m_Metadata.Blob(signature, out var reader);
            return new SignatureDecoder<bool, object?>(this, reader, null).DecodeFieldSignature(ref blob);
        }

        bool Type(BlobHandle signature)
        {
            var blob = m_Metadata.Blob(signature, out var reader);
            return new SignatureDecoder<bool, object?>(this, reader, null).DecodeType(ref blob);
        }

        /// <summary>A member reference's signature is a field's or a method's: its header says which.</summary>
        bool Member(BlobHandle signature)
        {
            var blob = m_Metadata.Blob(signature, out _);
            return blob.ReadSignatureHeader().Kind == SignatureKind.Field ? Field(signature) : Method(signature);
        }

        internal bool Token(int token)
        {
            var reader = m_Metadata.Reader(token, out var local);
            switch (local.Kind)
            {
                case HandleKind.FieldDefinition:
                    return Field(reader.GetFieldDefinition((FieldDefinitionHandle)local).Signature);
                case HandleKind.MethodDefinition:
                    return Method(reader.GetMethodDefinition((MethodDefinitionHandle)local).Signature);
                case HandleKind.MemberReference:
                {
                    var member = reader.GetMemberReference((MemberReferenceHandle)local);
                    return Member(member.Signature)
                        || (member.Parent.Kind == HandleKind.TypeSpecification && Token(MetadataTokens.GetToken(member.Parent)));
                }
                case HandleKind.MethodSpecification:
                {
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)local);
                    var blob = m_Metadata.Blob(spec.Signature, out var blobReader);
                    return new SignatureDecoder<bool, object?>(this, blobReader, null).DecodeMethodSpecificationSignature(ref blob).Any(t => t)
                        || Token(MetadataTokens.GetToken(spec.Method));
                }
                case HandleKind.TypeSpecification:
                    return Type(reader.GetTypeSpecification((TypeSpecificationHandle)local).Signature);
                case HandleKind.StandaloneSignature:
                {
                    var signature = reader.GetStandaloneSignature((StandaloneSignatureHandle)local);
                    if (signature.GetKind() == StandaloneSignatureKind.Method) return Method(signature.Signature);
                    var blob = m_Metadata.Blob(signature.Signature, out var blobReader);
                    return new SignatureDecoder<bool, object?>(this, blobReader, null).DecodeLocalSignature(ref blob).Any(t => t);
                }
                case HandleKind.TypeReference:
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)local);
                    return m_Metadata.String(type.Name) == "Single" && m_Metadata.String(type.Namespace) == "System";
                }
                default:
                    return false;
            }
        }

        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode == PrimitiveTypeCode.Single;
        public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => false;
        public bool GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => false;
        public bool GetSZArrayType(bool elementType) => elementType;
        public bool GetByReferenceType(bool elementType) => elementType;
        public bool GetPointerType(bool elementType) => elementType;
        public bool GetPinnedType(bool elementType) => elementType;
        public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
        public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments) => typeArguments.Any(t => t);
        public bool GetGenericMethodParameter(object? context, int index) => false;
        public bool GetGenericTypeParameter(object? context, int index) => false;
        public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;
        public bool GetFunctionPointerType(MethodSignature<bool> signature) => signature.ReturnType || signature.ParameterTypes.Any(p => p);
    }
}
