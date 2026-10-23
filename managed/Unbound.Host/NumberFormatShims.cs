using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unbound.Runtime;

namespace Unbound.Host;

/// <summary>
/// Game code formats floats and doubles as under Unity's Mono (see <see cref="Unbound.MonoNumberFormat"/>):
/// calls to <c>float.ToString</c>, <c>double.ToString</c>, <c>string.Format</c>, <c>string.Concat(object…)</c>,
/// <c>StringBuilder.Append/AppendFormat</c>, <c>Convert.ToString</c> and virtual <c>ToString</c>s are
/// redirected to it when an assembly loads, and again in the IL of every hot-reload delta. Every
/// rewritten instruction keeps its size, so the assembly's symbols still match.
/// </summary>
static class NumberFormatShims
{
    static readonly HashSet<string> s_Types = new(StringComparer.Ordinal)
    {
        "System.Single", "System.Double", "System.String", "System.Text.StringBuilder", "System.Convert",
    };

    static readonly string s_ShimType = typeof(Unbound.MonoNumberFormat).FullName!;

    /// <summary>Shims by name and parameter types (an instance call's receiver first).</summary>
    static readonly Dictionary<string, System.Reflection.MethodInfo> s_Shims = typeof(Unbound.MonoNumberFormat)
        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(m => m.DeclaringType == typeof(Unbound.MonoNumberFormat))
        .ToDictionary(m => Key(m.Name, m.GetParameters().Select(p => p.ParameterType.FullName!)), StringComparer.Ordinal);

    static readonly HashSet<string> s_Names = new(s_Shims.Values.Select(m => m.Name), StringComparer.Ordinal);

    static string Key(string name, IEnumerable<string> parameters) => name + "(" + string.Join(",", parameters) + ")";

    /// <summary><see cref="Redirect"/>, or the image as it is (reported) when it cannot be rewritten:
    /// the assembly still loads, formatting numbers as .NET does.</summary>
    internal static byte[] TryRedirect(byte[] image)
    {
        try
        {
            return Redirect(image);
        }
        catch (Exception e)
        {
            string name;
            try { name = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(image)).Assembly.Name.Name; }
            catch { name = "an assembly"; }
            Native.Log((int)UnityEngine.LogType.Warning,
                $"[Unbound] {name}: cannot redirect its number formatting to Mono's rules, so it prints floats as .NET does ({e.GetType().Name}: {e.Message})");
            return image;
        }
    }

    internal static byte[] Redirect(byte[] image)
    {
        if (!ReferencesShimmedNames(image)) return image;
        using var input = new MemoryStream(image);
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = AccessChecks.Resolver });
        var module = assembly.MainModule;
        var references = new Dictionary<System.Reflection.MethodInfo, MethodReference>();
        MethodReference ReferenceTo(System.Reflection.MethodInfo shim) =>
            references.TryGetValue(shim, out var reference) ? reference : references[shim] = Reference(module, shim);
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                var il = method.Body.GetILProcessor();
                foreach (var instruction in method.Body.Instructions.ToList())
                {
                    if ((instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) || instruction.Operand is not MethodReference target)
                        continue;
                    var constrained = instruction.Previous is { OpCode.Code: Code.Constrained } prefix ? prefix : null;
                    var shim = FindShim(target.Name, target.DeclaringType.FullName, target.HasThis,
                        target.Parameters.Select(p => p.ParameterType.FullName), (constrained?.Operand as Mono.Cecil.TypeReference)?.FullName,
                        instruction.OpCode == OpCodes.Callvirt);
                    if (shim == null) continue;
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = ReferenceTo(shim);
                    if (constrained != null)
                    {
                        // "constrained. T" is six bytes: six nops keep every offset after it.
                        constrained.OpCode = OpCodes.Nop;
                        constrained.Operand = null;
                        for (int i = 0; i < 5; i++) il.InsertAfter(constrained, il.Create(OpCodes.Nop));
                    }
                }
            }
        }
        // Every shim referenced, used or not: a hot-reload delta's calls are redirected to these
        // references (a delta cannot be given new ones once emitted).
        var holder = new Mono.Cecil.TypeDefinition("", "<Unbound>NumberFormatShims",
            Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
        var keep = new Mono.Cecil.MethodDefinition("References",
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig, module.TypeSystem.Void);
        var body = keep.Body.GetILProcessor();
        foreach (var shim in s_Shims.Values.OrderBy(m => Key(m.Name, m.GetParameters().Select(p => p.ParameterType.FullName!)), StringComparer.Ordinal))
        {
            body.Emit(OpCodes.Ldtoken, ReferenceTo(shim));
            body.Emit(OpCodes.Pop);
        }
        body.Emit(OpCodes.Ret);
        holder.Methods.Add(keep);
        module.Types.Add(holder);
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }

    /// <summary>A reference to a shim in the assembly's own terms: the engine by the name the
    /// assembly already references it with (a second reference to it with another version would
    /// stop Edit and Continue from using the image as its baseline), framework types from the
    /// assembly's core library.</summary>
    static MethodReference Reference(Mono.Cecil.ModuleDefinition module, System.Reflection.MethodInfo shim)
    {
        var engineName = typeof(Unbound.MonoNumberFormat).Assembly.GetName().Name!;
        var engine = module.AssemblyReferences.FirstOrDefault(r => r.Name == engineName);
        if (engine == null)
        {
            engine = new AssemblyNameReference(engineName, new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(engine);
        }
        var declaring = new Mono.Cecil.TypeReference(shim.DeclaringType!.Namespace, shim.DeclaringType.Name, module, engine);
        var reference = new MethodReference(shim.Name, Type(module, shim.ReturnType), declaring) { HasThis = false };
        foreach (var parameter in shim.GetParameters())
            reference.Parameters.Add(new ParameterDefinition(Type(module, parameter.ParameterType)));
        return reference;
    }

    static Mono.Cecil.TypeReference Type(Mono.Cecil.ModuleDefinition module, Type type)
    {
        if (type.IsByRef) return new ByReferenceType(Type(module, type.GetElementType()!));
        if (type.IsArray) return new ArrayType(Type(module, type.GetElementType()!));
        var system = module.TypeSystem;
        if (type == typeof(string)) return system.String;
        if (type == typeof(object)) return system.Object;
        if (type == typeof(float)) return system.Single;
        if (type == typeof(double)) return system.Double;
        return new Mono.Cecil.TypeReference(type.Namespace, type.Name, module, system.CoreLibrary, type.IsValueType);
    }

    /// <summary>The shim for a call, or null when it is left alone. <paramref name="constrained"/> is
    /// the type of a <c>constrained.</c> prefix: <c>constrained. float callvirt object::ToString()</c>
    /// is a call on the float.</summary>
    static System.Reflection.MethodInfo? FindShim(string name, string declaring, bool hasThis, IEnumerable<string> parameterTypes,
        string? constrained, bool virtualCall)
    {
        if (!s_Names.Contains(name)) return null;
        // A virtual ToString on an object that may be a boxed float. (A non-virtual call is
        // base.ToString() in an override: it stays.)
        if (constrained == null && virtualCall && name == "ToString" && declaring is "System.Object" or "System.IFormattable")
            return s_Shims.GetValueOrDefault(Key("VirtualToString", parameterTypes.Prepend(declaring)));
        if (constrained != null)
        {
            if (constrained is not ("System.Single" or "System.Double")) return null;
            if (declaring is not ("System.Object" or "System.IFormattable" or "System.ValueType")) return null;
            declaring = constrained;
        }
        else if (!s_Types.Contains(declaring)) return null;
        var parameters = new List<string>();
        if (hasThis)
            parameters.Add(declaring is "System.Single" or "System.Double" ? declaring + "&" : declaring);
        parameters.AddRange(parameterTypes);
        return s_Shims.GetValueOrDefault(Key(name, parameters));
    }

    static bool ReferencesShimmedNames(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        if (!pe.HasMetadata) return false;
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (!s_Names.Contains(reader.GetString(member.Name)) || member.Parent.Kind != HandleKind.TypeReference) continue;
            var parent = FullName(reader, (TypeReferenceHandle)member.Parent);
            if (s_Types.Contains(parent) || parent is "System.Object" or "System.IFormattable" or "System.ValueType") return true;
        }
        return false;
    }

    static string FullName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        var ns = reader.GetString(type.Namespace);
        var name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    // ------------------------------------------------------------------ hot-reload deltas

    /// <summary>
    /// The IL of a hot-reload delta with its calls redirected as <see cref="Redirect"/> does, to the
    /// shim references the loaded image holds. <paramref name="baseline"/> is that image and
    /// <paramref name="deltas"/> the metadata of every delta applied to it, this one last.
    /// </summary>
    internal static byte[] RedirectDelta(byte[] baseline, IReadOnlyList<byte[]> deltas, byte[] il)
    {
        using var pe = new PEReader(ImmutableArray.Create(baseline));
        var baseReader = pe.GetMetadataReader();
        var providers = deltas.Select(d => MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(d))).ToList();
        try
        {
            var readers = new List<MetadataReader> { baseReader };
            readers.AddRange(providers.Select(p => p.GetMetadataReader()));
            var view = new AggregateView(new MetadataAggregator(baseReader, readers.Skip(1).ToList()), readers);
            // The image's shim references, by key.
            var targets = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var handle in baseReader.MemberReferences)
            {
                var (declaring, name, _, parameters) = view.Describe(MetadataTokens.GetToken(handle));
                if (declaring == s_ShimType) targets[Key(name, parameters)] = MetadataTokens.GetToken(handle);
            }
            if (targets.Count == 0) return il;
            var patched = (byte[])il.Clone();
            var delta = readers[^1];
            foreach (var handle in delta.MethodDefinitions)
            {
                var rva = delta.GetMethodDefinition(handle).RelativeVirtualAddress;
                if (rva != 0) PatchBody(patched, rva, token => Redirection(view, targets, token));
            }
            return patched;
        }
        finally
        {
            foreach (var provider in providers) provider.Dispose();
        }
    }

    /// <summary>The shim token for a call's member token (and its constrained type), or 0.</summary>
    static int Redirection(AggregateView view, Dictionary<string, int> targets, (int Token, int Constrained, bool Virtual) call)
    {
        if ((call.Token >> 24) != 0x0A) return 0; // member references only
        var (declaring, name, hasThis, parameters) = view.Describe(call.Token);
        var constrained = call.Constrained == 0 ? null : view.TypeName(call.Constrained);
        var shim = FindShim(name, declaring, hasThis, parameters, constrained, call.Virtual);
        if (shim == null) return 0;
        return targets.GetValueOrDefault(Key(shim.Name, shim.GetParameters().Select(p => p.ParameterType.FullName!)));
    }

    /// <summary>Rewrites the calls in the method body at <paramref name="offset"/>.</summary>
    static void PatchBody(byte[] il, int offset, Func<(int Token, int Constrained, bool Virtual), int> redirect)
    {
        int codeSize, start;
        if ((il[offset] & 0x3) == 0x2)
        {
            codeSize = il[offset] >> 2;
            start = offset + 1;
        }
        else
        {
            int headerSize = (il[offset + 1] >> 4) * 4;
            codeSize = BitConverter.ToInt32(il, offset + 4);
            start = offset + headerSize;
        }
        int end = start + codeSize, constrainedAt = -1, constrainedType = 0;
        for (int at = start; at < end;)
        {
            int opcode = il[at] == 0xFE ? 0xFE00 | il[at + 1] : il[at];
            int size = opcode > 0xFF ? 2 : 1;
            int operand = OperandSize(opcode, il, at + size);
            if (opcode is 0x28 or 0x6F) // call, callvirt
            {
                int token = BitConverter.ToInt32(il, at + 1);
                bool prefixed = constrainedAt >= 0 && constrainedAt + 6 == at;
                int target = redirect((token, prefixed ? constrainedType : 0, opcode == 0x6F));
                if (target != 0)
                {
                    il[at] = 0x28;
                    BitConverter.TryWriteBytes(il.AsSpan(at + 1, 4), target);
                    if (prefixed) il.AsSpan(constrainedAt, 6).Fill(0x00); // nops
                }
            }
            if (opcode == 0xFE16) // constrained.
            {
                constrainedAt = at;
                constrainedType = BitConverter.ToInt32(il, at + 2);
            }
            at += size + operand;
        }
    }

    static readonly Dictionary<int, System.Reflection.Emit.OperandType> s_Operands = typeof(System.Reflection.Emit.OpCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!)
        .ToDictionary(o => (int)(ushort)o.Value, o => o.OperandType);

    static int OperandSize(int opcode, byte[] il, int at) => s_Operands[opcode] switch
    {
        System.Reflection.Emit.OperandType.InlineNone => 0,
        System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI
            or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
        System.Reflection.Emit.OperandType.InlineVar => 2,
        System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
        System.Reflection.Emit.OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
        _ => 4,
    };

    /// <summary>Names of references across a baseline and its deltas (whose rows and heap offsets
    /// continue the generations before them).</summary>
    sealed class AggregateView : ISignatureTypeProvider<string, object?>
    {
        readonly MetadataAggregator m_Aggregator;
        readonly List<MetadataReader> m_Readers;

        internal AggregateView(MetadataAggregator aggregator, List<MetadataReader> readers)
        {
            m_Aggregator = aggregator;
            m_Readers = readers;
        }

        MetadataReader Reader(Handle handle, out Handle local)
        {
            local = m_Aggregator.GetGenerationHandle(handle, out int generation);
            return m_Readers[generation];
        }

        string String(StringHandle handle)
        {
            if (handle.IsNil) return "";
            var reader = Reader(handle, out var local);
            return reader.GetString((StringHandle)local);
        }

        internal (string Declaring, string Name, bool HasThis, IReadOnlyList<string> Parameters) Describe(int token)
        {
            var reader = Reader(MetadataTokens.EntityHandle(token), out var local);
            var member = reader.GetMemberReference((MemberReferenceHandle)local);
            var name = String(member.Name);
            // The signature's heap offset counts every generation's heap (as do all handles here).
            var blobReader = Reader(member.Signature, out var blob);
            var signature = blobReader.GetBlobReader((BlobHandle)blob);
            if (signature.ReadSignatureHeader().Kind != SignatureKind.Method) return ("", name, false, Array.Empty<string>());
            signature.Reset();
            var declaring = member.Parent.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition or HandleKind.TypeSpecification
                ? TypeName(MetadataTokens.GetToken(member.Parent))
                : "";
            var decoded = new SignatureDecoder<string, object?>(this, blobReader, null).DecodeMethodSignature(ref signature);
            return (declaring, name, decoded.Header.IsInstance, decoded.ParameterTypes);
        }

        internal string TypeName(int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            var reader = Reader(handle, out var local);
            switch (local.Kind)
            {
                case HandleKind.TypeReference:
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)local);
                    var ns = String(type.Namespace);
                    return ns.Length == 0 ? String(type.Name) : ns + "." + String(type.Name);
                }
                case HandleKind.TypeDefinition:
                {
                    var type = reader.GetTypeDefinition((TypeDefinitionHandle)local);
                    var ns = String(type.Namespace);
                    return ns.Length == 0 ? String(type.Name) : ns + "." + String(type.Name);
                }
                case HandleKind.TypeSpecification:
                {
                    var spec = reader.GetTypeSpecification((TypeSpecificationHandle)local);
                    var blobReader = Reader(spec.Signature, out var blob);
                    var signature = blobReader.GetBlobReader((BlobHandle)blob);
                    return new SignatureDecoder<string, object?>(this, blobReader, null).DecodeType(ref signature);
                }
                default:
                    return "";
            }
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.Void => "System.Void",
            _ => "System." + typeCode,
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => TypeName(MetadataTokens.GetToken(handle));
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeName(MetadataTokens.GetToken(handle));
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => TypeName(MetadataTokens.GetToken(handle));
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "method";
    }
}
