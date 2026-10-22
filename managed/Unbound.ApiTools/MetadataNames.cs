using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Unbound.ApiTools;

/// <summary>
/// Names of types as the API tools compare them: <c>Namespace.Outer/Inner</c> (nesting with
/// <c>/</c>, as in IL), without the assembly. Unity's modules and Unbound's implementation define the
/// same types under the same full names, and the class library defines each type under one full
/// name whatever facade (<c>mscorlib</c>, <c>netstandard</c>, <c>System.Runtime</c>) a caller binds
/// through, so the full name alone identifies a type.
/// </summary>
static class MetadataNames
{
    public static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    public static string TypeDefinition(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        return declaring.IsNil
            ? Join(md.GetString(type.Namespace), md.GetString(type.Name))
            : TypeDefinition(md, declaring) + "/" + Join(md.GetString(type.Namespace), md.GetString(type.Name));
    }

    public static string TypeReference(MetadataReader md, TypeReferenceHandle handle)
    {
        var type = md.GetTypeReference(handle);
        var name = Join(md.GetString(type.Namespace), md.GetString(type.Name));
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? TypeReference(md, (TypeReferenceHandle)type.ResolutionScope) + "/" + name
            : name;
    }

    /// <summary>The assembly a type reference binds to (through its outermost type), or null when it
    /// refers into its own module or another module of the same assembly.</summary>
    public static string? TypeReferenceAssembly(MetadataReader md, TypeReferenceHandle handle)
    {
        var scope = md.GetTypeReference(handle).ResolutionScope;
        while (scope.Kind == HandleKind.TypeReference)
            scope = md.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;
        return scope.Kind == HandleKind.AssemblyReference
            ? md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
            : null;
    }

    public static string ExportedType(MetadataReader md, ExportedTypeHandle handle)
    {
        var type = md.GetExportedType(handle);
        var name = Join(md.GetString(type.Namespace), md.GetString(type.Name));
        return type.Implementation.Kind == HandleKind.ExportedType
            ? ExportedType(md, (ExportedTypeHandle)type.Implementation) + "/" + name
            : name;
    }

    /// <summary>The generic type a type specification instantiates (<c>List`1</c> for
    /// <c>List`1&lt;int&gt;</c>), or nil when it is not a generic instantiation.</summary>
    public static EntityHandle GenericDefinition(MetadataReader md, TypeSpecificationHandle handle)
    {
        var blob = md.GetBlobReader(md.GetTypeSpecification(handle).Signature);
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
            return default;
        blob.ReadSignatureTypeCode(); // CLASS or VALUETYPE
        return blob.ReadTypeHandle();
    }

    /// <summary>The full name of a TypeDef, TypeRef or (the generic definition of) a TypeSpec.</summary>
    public static string? Any(MetadataReader md, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeDefinition(md, (TypeDefinitionHandle)handle),
        HandleKind.TypeReference => TypeReference(md, (TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => GenericDefinition(md, (TypeSpecificationHandle)handle) is { IsNil: false } def
            ? Any(md, def)
            : null,
        _ => null,
    };
}

/// <summary>
/// Decodes signatures into canonical strings that compare equal across assemblies: types by full
/// name (<see cref="MetadataNames"/>), generic parameters by position (<c>!0</c>, <c>!!0</c>), custom
/// modifiers dropped. Two members with the same name and canonical signature bind to each other at
/// run time once their types are forwarded to the same definitions.
/// </summary>
sealed class CanonicalSignatures : ISignatureTypeProvider<string, object?>
{
    public static readonly CanonicalSignatures Instance = new();

    // PrimitiveTypeCode's names are the System type names (Int32, String, Void, IntPtr, ...).
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        MetadataNames.TypeDefinition(reader, handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        MetadataNames.TypeReference(reader, handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetFunctionPointerType(MethodSignature<string> signature) => "method " + Method(signature);

    /// <summary><c>instance &lt;1&gt; System.Void(System.Int32,!!0)</c>: static-ness, generic arity,
    /// return and parameter types. Calling-convention details beyond <c>this</c> do not affect
    /// binding of managed members.</summary>
    public static string Method(MethodSignature<string> sig) =>
        (sig.Header.IsInstance ? "instance " : "")
        + (sig.GenericParameterCount > 0 ? "<" + sig.GenericParameterCount + "> " : "")
        + sig.ReturnType + "(" + string.Join(",", sig.ParameterTypes) + ")";

    /// <summary>A human-readable form for reports: <c>Name&lt;1&gt;(params) : return</c>.</summary>
    public static string Display(string name, MethodSignature<string> sig) =>
        (sig.Header.IsInstance ? "" : "static ")
        + name + (sig.GenericParameterCount > 0 ? "<" + string.Join(",", Enumerable.Range(0, sig.GenericParameterCount).Select(i => "!!" + i)) + ">" : "")
        + "(" + string.Join(", ", sig.ParameterTypes) + ") : " + sig.ReturnType;

    public static string MethodOf(MetadataReader md, MethodDefinitionHandle handle) =>
        Method(md.GetMethodDefinition(handle).DecodeSignature(Instance, null));

    public static string FieldOf(MetadataReader md, FieldDefinitionHandle handle) =>
        md.GetFieldDefinition(handle).DecodeSignature(Instance, null);

    public static int Token(EntityHandle handle) => MetadataTokens.GetToken(handle);
}
