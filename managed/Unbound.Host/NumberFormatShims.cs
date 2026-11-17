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
/// Game code formats floats, doubles and enum values, and reads exception messages, as under Unity's Mono (see
/// <see cref="Unbound.MonoNumberFormat"/>): calls to <c>float.ToString</c>, <c>double.ToString</c>,
/// <c>string.Format</c>, <c>string.Concat(object…)</c>, <c>string.Join</c>,
/// <c>StringBuilder.Append/AppendFormat/AppendJoin</c>,
/// <c>Convert.ToString</c>, virtual <c>ToString</c>s, virtual <c>Exception.Message</c> reads and
/// enum texts (<c>Enum.ToString</c>, <c>Enum.GetName</c>, <c>Enum.Format</c>,
/// <c>Type.GetEnumName</c>) are redirected to it when an assembly loads, and again in the IL of
/// every hot-reload delta. Every rewritten instruction keeps its size, so the assembly's PDB
/// still matches. A ToString called directly on an enum value, a nullable or a generic parameter's
/// value (<c>constrained. T callvirt object::ToString()</c>, the usual <c>e.ToString()</c>), and
/// string.Join, string.Concat and StringBuilder.AppendJoin of a generic sequence, are redirected
/// at load only: a delta cannot be given the
/// generic shims' instantiations, so edited code prints those calls as .NET does.
/// </summary>
static class NumberFormatShims
{
    static readonly HashSet<string> s_Types = new(StringComparer.Ordinal)
    {
        "System.Single", "System.Double", "System.String", "System.Text.StringBuilder", "System.Convert",
        "System.Enum", "System.Type",
    };

    static readonly string s_ShimType = typeof(Unbound.MonoNumberFormat).FullName!;

    /// <summary>The classes whose Message a call can name: Exception, which declares it, and the
    /// class library's argument exceptions, which override it.</summary>
    static readonly HashSet<string> s_MessageTypes = new(StringComparer.Ordinal)
    {
        "System.Exception", "System.ArgumentException", "System.ArgumentOutOfRangeException",
    };

    /// <summary>Shims by name and parameter types (an instance call's receiver first).</summary>
    static readonly Dictionary<string, System.Reflection.MethodInfo> s_Shims = typeof(Unbound.MonoNumberFormat)
        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(m => m.DeclaringType == typeof(Unbound.MonoNumberFormat) && !m.IsGenericMethodDefinition)
        .ToDictionary(m => Key(m.Name, m.GetParameters().Select(p => p.ParameterType.FullName!)), StringComparer.Ordinal);

    static readonly HashSet<string> s_Names = new(s_Shims.Values.Select(m => m.Name), StringComparer.Ordinal);

    /// <summary>The generic shims, instantiated per call with the call's type argument: a ToString
    /// called directly on a value that may be an enum, and string.Join of a sequence.</summary>
    static readonly System.Reflection.MethodInfo[] s_GenericShims = typeof(Unbound.MonoNumberFormat)
        .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(m => m.DeclaringType == typeof(Unbound.MonoNumberFormat) && m.IsGenericMethodDefinition)
        .ToArray();

    static readonly System.Reflection.MethodInfo s_ConstrainedToString = s_GenericShims.Single(m => m.Name == nameof(Unbound.MonoNumberFormat.ConstrainedToString));

    static string Key(string name, IEnumerable<string> parameters) => name + "(" + string.Join(",", parameters) + ")";

    /// <summary><see cref="Redirect"/>, or the image as it is (reported) when it cannot be rewritten:
    /// the assembly still loads, formatting numbers and exceptions as .NET does.</summary>
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
                $"[Unbound] {name}: cannot redirect its number, enum and exception texts to Mono's rules, so it prints floats, enum names and argument exceptions as .NET does ({e.GetType().Name}: {e.Message})");
            return image;
        }
    }

    internal static byte[] Redirect(byte[] image)
    {
        if (!ReferencesShimmedNames(image)) return image;
        using var input = new MemoryStream(image);
        using var assembly = AccessChecks.Read(input);
        var module = assembly.MainModule;
        var references = new Dictionary<System.Reflection.MethodInfo, MethodReference>();
        MethodReference ReferenceTo(System.Reflection.MethodInfo shim) =>
            references.TryGetValue(shim, out var reference) ? reference : references[shim] = Reference(module, shim);
        var genericReferences = new Dictionary<System.Reflection.MethodInfo, MethodReference>();
        MethodReference Instance(System.Reflection.MethodInfo shim, Mono.Cecil.TypeReference argument)
        {
            if (!genericReferences.TryGetValue(shim, out var definition)) genericReferences[shim] = definition = GenericReference(module, shim);
            // (The writer shares equal instantiations.)
            var instance = new GenericInstanceMethod(definition);
            instance.GenericArguments.Add(argument);
            return instance;
        }
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
                    if (constrained?.Operand is Mono.Cecil.TypeReference receiver && IsObjectToString(target) && MayBeEnum(receiver))
                    {
                        // constrained. T callvirt object::ToString() -> call ConstrainedToString<T>(ref T):
                        // the receiver is already the value's address.
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = Instance(s_ConstrainedToString, receiver);
                        Nop(il, constrained);
                        continue;
                    }
                    if (target is GenericInstanceMethod { GenericArguments.Count: 1 } generic && FindGenericShim(generic.ElementMethod) is { } genericShim)
                    {
                        // (callvirt on a StringBuilder: the shim takes it as its first argument.)
                        instruction.OpCode = OpCodes.Call;
                        instruction.Operand = Instance(genericShim, generic.GenericArguments[0]);
                        continue;
                    }
                    var shim = FindShim(target.Name, target.DeclaringType.FullName, target.HasThis,
                        target.Parameters.Select(p => p.ParameterType.FullName), (constrained?.Operand as Mono.Cecil.TypeReference)?.FullName,
                        instruction.OpCode == OpCodes.Callvirt);
                    if (shim == null) continue;
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = ReferenceTo(shim);
                    if (constrained != null) Nop(il, constrained);
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
        AccessChecks.Write(assembly, output);
        return output.ToArray();
    }

    /// <summary>"constrained. T" is six bytes: six nops keep every offset after it.</summary>
    static void Nop(ILProcessor il, Instruction constrained)
    {
        constrained.OpCode = OpCodes.Nop;
        constrained.Operand = null;
        for (int i = 0; i < 5; i++) il.InsertAfter(constrained, il.Create(OpCodes.Nop));
    }

    static bool IsObjectToString(MethodReference target) =>
        target.Name == "ToString" && target.HasThis && target.Parameters.Count == 0 && target.DeclaringType.FullName == "System.Object";

    /// <summary>Whether a constrained call's type may hold an enum value: a generic parameter, a
    /// nullable or an enum (false when it cannot be resolved).</summary>
    static bool MayBeEnum(Mono.Cecil.TypeReference type)
    {
        if (type is Mono.Cecil.GenericParameter) return true;
        if (type is GenericInstanceType { ElementType.FullName: "System.Nullable`1" }) return true;
        try
        {
            return type.Resolve()?.IsEnum == true;
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }
    }

    /// <summary>The generic shim of a call of a generic class library method (one type
    /// parameter), matched by declaring type, name and parameters (an instance call's receiver
    /// first).</summary>
    static System.Reflection.MethodInfo? FindGenericShim(MethodReference method)
    {
        var declaring = method.DeclaringType.FullName;
        if (declaring is not ("System.String" or "System.Text.StringBuilder")) return null;
        var called = method.Parameters.Select(p => GenericName(p.ParameterType)).ToList();
        if (method.HasThis) called.Insert(0, declaring);
        foreach (var shim in s_GenericShims)
        {
            if (shim.Name == method.Name && shim.GetParameters().Select(p => GenericName(p.ParameterType)).SequenceEqual(called))
                return shim;
        }
        return null;
    }

    /// <summary>A parameter type's name with method type parameters by position (<c>!!0</c>), for
    /// a shim's and for a call's open method.</summary>
    static string GenericName(Type type) => type.IsGenericMethodParameter ? "!!" + type.GenericParameterPosition
        : type.IsByRef ? GenericName(type.GetElementType()!) + "&"
        : type.IsConstructedGenericType ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(GenericName)) + ">"
        : type.FullName ?? type.Name;

    static string GenericName(Mono.Cecil.TypeReference type) => type switch
    {
        Mono.Cecil.GenericParameter { Type: GenericParameterType.Method } p => "!!" + p.Position,
        ByReferenceType r => GenericName(r.ElementType) + "&",
        GenericInstanceType g => g.ElementType.FullName + "<" + string.Join(",", g.GenericArguments.Select(GenericName)) + ">",
        _ => type.FullName,
    };

    /// <summary>A reference to the generic shim <paramref name="shim"/> (one method type parameter).</summary>
    static MethodReference GenericReference(Mono.Cecil.ModuleDefinition module, System.Reflection.MethodInfo shim)
    {
        var reference = new MethodReference(shim.Name, Type(module, shim.ReturnType), ShimType(module, shim)) { HasThis = false };
        var parameter = new Mono.Cecil.GenericParameter("T", reference);
        reference.GenericParameters.Add(parameter);
        Mono.Cecil.TypeReference Of(Type type)
        {
            if (type.IsGenericParameter) return parameter;
            if (type.IsByRef) return new ByReferenceType(Of(type.GetElementType()!));
            if (!type.IsConstructedGenericType) return Type(module, type);
            var instance = new GenericInstanceType(Type(module, type.GetGenericTypeDefinition()));
            foreach (var argument in type.GetGenericArguments()) instance.GenericArguments.Add(Of(argument));
            return instance;
        }
        foreach (var p in shim.GetParameters()) reference.Parameters.Add(new ParameterDefinition(Of(p.ParameterType)));
        return reference;
    }

    /// <summary>A reference to a shim in the assembly's own terms: the engine by the name the
    /// assembly already references it with (a second reference to it with another version would
    /// stop Edit and Continue from using the image as its baseline), framework types from the
    /// assembly's core library.</summary>
    static MethodReference Reference(Mono.Cecil.ModuleDefinition module, System.Reflection.MethodInfo shim)
    {
        var reference = new MethodReference(shim.Name, Type(module, shim.ReturnType), ShimType(module, shim)) { HasThis = false };
        foreach (var parameter in shim.GetParameters())
            reference.Parameters.Add(new ParameterDefinition(Type(module, parameter.ParameterType)));
        return reference;
    }

    static Mono.Cecil.TypeReference ShimType(Mono.Cecil.ModuleDefinition module, System.Reflection.MethodInfo shim)
    {
        var engineName = typeof(Unbound.MonoNumberFormat).Assembly.GetName().Name!;
        var engine = module.AssemblyReferences.FirstOrDefault(r => r.Name == engineName);
        if (engine == null)
        {
            engine = new AssemblyNameReference(engineName, new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(engine);
        }
        return new Mono.Cecil.TypeReference(shim.DeclaringType!.Namespace, shim.DeclaringType.Name, module, engine);
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
        if (type == typeof(char)) return system.Char;
        return new Mono.Cecil.TypeReference(type.Namespace, type.Name, module, system.CoreLibrary, type.IsValueType);
    }

    /// <summary>The shim for a call, or null when it is left alone. <paramref name="constrained"/> is
    /// the type of a <c>constrained.</c> prefix: <c>constrained. float callvirt object::ToString()</c>
    /// is a call on the float.</summary>
    static System.Reflection.MethodInfo? FindShim(string name, string declaring, bool hasThis, IEnumerable<string> parameterTypes,
        string? constrained, bool virtualCall)
    {
        if (!s_Names.Contains(name)) return null;
        // A virtual read of an exception's message. (A non-virtual one is base.Message in an
        // override: it stays.)
        if (name == "get_Message" && s_MessageTypes.Contains(declaring))
            return constrained == null && virtualCall && hasThis ? s_Shims.GetValueOrDefault(Key(name, ["System.Exception"])) : null;
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
            if (s_Types.Contains(parent) || s_MessageTypes.Contains(parent) || parent is "System.Object" or "System.IFormattable" or "System.ValueType") return true;
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
        using var metadata = new DeltaMetadata(baseline, deltas);
        var view = new AggregateView(metadata);
        // The image's shim references, by key.
        var targets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var handle in metadata.Baseline.MemberReferences)
        {
            var (declaring, name, _, parameters) = view.Describe(MetadataTokens.GetToken(handle));
            if (declaring == s_ShimType) targets[Key(name, parameters)] = MetadataTokens.GetToken(handle);
        }
        if (targets.Count == 0) return il;
        var patched = (byte[])il.Clone();
        foreach (var (_, offset, _) in metadata.Bodies())
            PatchBody(patched, offset, token => Redirection(view, targets, token));
        return patched;
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
        var (start, end, _) = DeltaMetadata.Body(il, offset);
        int constrainedAt = -1, constrainedType = 0;
        foreach (var (opcode, at, operand) in DeltaMetadata.Instructions(il, start, end))
        {
            if (opcode is 0x28 or 0x6F) // call, callvirt
            {
                int token = BitConverter.ToInt32(il, operand);
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
                constrainedType = BitConverter.ToInt32(il, operand);
            }
        }
    }

    /// <summary>Names of references across a baseline and its deltas (whose rows and heap offsets
    /// continue the generations before them).</summary>
    internal sealed class AggregateView : ISignatureTypeProvider<string, object?>
    {
        readonly DeltaMetadata m_Metadata;

        internal AggregateView(DeltaMetadata metadata) => m_Metadata = metadata;

        MetadataReader Reader(Handle handle, out Handle local) => m_Metadata.Reader(handle, out local);

        string String(StringHandle handle) => m_Metadata.String(handle);

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
