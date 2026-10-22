using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Unbound.ApiTools;

/// <summary>An assembly loaded for metadata reading. The image is read into memory, so no file
/// handle stays open.</summary>
sealed class LoadedAssembly
{
    public required string Path;
    public required string Name;
    public required PEReader PE;
    public required MetadataReader Reader;

    /// <summary>Loads a managed assembly, or returns null (with the reason) for a native or
    /// unreadable file.</summary>
    public static LoadedAssembly? TryLoad(string path, out string? problem)
    {
        problem = null;
        try
        {
            var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
            if (!pe.HasMetadata)
            {
                problem = "not a managed assembly";
                return null;
            }
            var md = pe.GetMetadataReader();
            var name = md.IsAssembly
                ? md.GetString(md.GetAssemblyDefinition().Name)
                : System.IO.Path.GetFileNameWithoutExtension(path);
            return new LoadedAssembly { Path = path, Name = name, PE = pe, Reader = md };
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or InvalidOperationException)
        {
            problem = e.Message;
            return null;
        }
    }
}

/// <summary>A method of an indexed type: its canonical signature and how it binds.</summary>
readonly record struct ApiMethod(string Signature, MethodAttributes Attributes)
{
    public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;
}

/// <summary>A type definition of an indexed assembly; members are read on first use.</summary>
sealed class ApiType
{
    public required LoadedAssembly Assembly;
    public required TypeDefinitionHandle Handle;
    public required string FullName;

    Dictionary<string, List<ApiMethod>>? _methods;
    Dictionary<string, string>? _fields;

    public TypeDefinition Definition => Assembly.Reader.GetTypeDefinition(Handle);
    public bool IsInterface => (Definition.Attributes & TypeAttributes.Interface) != 0;

    /// <summary>The full name of the base type (of its generic definition for an instantiation).</summary>
    public string? BaseTypeName => Definition.BaseType.IsNil ? null : MetadataNames.Any(Assembly.Reader, Definition.BaseType);

    public Dictionary<string, List<ApiMethod>> Methods
    {
        get
        {
            if (_methods == null)
            {
                var md = Assembly.Reader;
                var methods = new Dictionary<string, List<ApiMethod>>(StringComparer.Ordinal);
                foreach (var handle in Definition.GetMethods())
                {
                    var method = md.GetMethodDefinition(handle);
                    var name = md.GetString(method.Name);
                    if (!methods.TryGetValue(name, out var list))
                        methods[name] = list = new();
                    list.Add(new ApiMethod(CanonicalSignatures.MethodOf(md, handle), method.Attributes));
                }
                _methods = methods;
            }
            return _methods;
        }
    }

    public Dictionary<string, string> Fields
    {
        get
        {
            if (_fields == null)
            {
                var md = Assembly.Reader;
                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var handle in Definition.GetFields())
                    fields.TryAdd(md.GetString(md.GetFieldDefinition(handle).Name), CanonicalSignatures.FieldOf(md, handle));
                _fields = fields;
            }
            return _fields;
        }
    }
}

/// <summary>
/// The types of a set of assemblies by full name: Unbound's implementation, or a release's Unity
/// modules. Member lookups follow the base type chain within the set, as the runtime does when it
/// binds a member reference to an inherited member.
/// </summary>
sealed class ApiIndex
{
    readonly Dictionary<string, List<ApiType>> _types = new(StringComparer.Ordinal);
    /// <summary>Types the set forwards elsewhere (to the class library, say): they exist, but their
    /// members are not ours to check.</summary>
    readonly HashSet<string> _forwarded = new(StringComparer.Ordinal);

    public List<LoadedAssembly> Assemblies { get; } = new();

    public static ApiIndex Load(IEnumerable<string> paths)
    {
        var index = new ApiIndex();
        foreach (var path in paths)
        {
            var assembly = LoadedAssembly.TryLoad(path, out var problem)
                ?? throw new BadImageFormatException($"{path}: {problem}");
            index.Add(assembly);
        }
        return index;
    }

    public static ApiIndex From(IEnumerable<LoadedAssembly> assemblies)
    {
        var index = new ApiIndex();
        foreach (var assembly in assemblies)
            index.Add(assembly);
        return index;
    }

    void Add(LoadedAssembly assembly)
    {
        Assemblies.Add(assembly);
        var md = assembly.Reader;
        foreach (var handle in md.TypeDefinitions)
        {
            var name = MetadataNames.TypeDefinition(md, handle);
            if (name.StartsWith('<'))
                continue; // <Module>, <PrivateImplementationDetails>
            if (!_types.TryGetValue(name, out var list))
                _types[name] = list = new();
            list.Add(new ApiType { Assembly = assembly, Handle = handle, FullName = name });
        }
        foreach (var handle in md.ExportedTypes)
            if (md.GetExportedType(handle).Implementation.Kind != HandleKind.AssemblyFile)
                _forwarded.Add(MetadataNames.ExportedType(md, handle));
    }

    /// <summary>A type by full name; when several assemblies of the set define it, the one in
    /// <paramref name="preferredAssembly"/> if any.</summary>
    public ApiType? Find(string fullName, string? preferredAssembly = null)
    {
        if (!_types.TryGetValue(fullName, out var list))
            return null;
        if (preferredAssembly != null)
            foreach (var type in list)
                if (type.Assembly.Name == preferredAssembly)
                    return type;
        return list[0];
    }

    public bool IsForwarded(string fullName) => _forwarded.Contains(fullName);

    /// <summary>The type and its base types within the set, most derived first.</summary>
    public IEnumerable<ApiType> Hierarchy(ApiType type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (ApiType? current = type; current != null && seen.Add(current.FullName);)
        {
            yield return current;
            var baseName = current.BaseTypeName;
            current = baseName == null ? null : Find(baseName, current.Assembly.Name);
        }
    }

    public ApiType? FindMethod(ApiType type, string name, string signature)
    {
        foreach (var t in Hierarchy(type))
            if (t.Methods.TryGetValue(name, out var list) && list.Exists(m => m.Signature == signature))
                return t;
        return null;
    }

    public ApiType? FindField(ApiType type, string name, string signature)
    {
        foreach (var t in Hierarchy(type))
            if (t.Fields.TryGetValue(name, out var s) && s == signature)
                return t;
        return null;
    }

    /// <summary>The type in the hierarchy that introduces the virtual method a method with this name
    /// and signature overrides (the least derived one declaring it).</summary>
    public ApiType? FindVirtual(ApiType type, string name, string signature)
    {
        ApiType? found = null;
        foreach (var t in Hierarchy(type))
            if (t.Methods.TryGetValue(name, out var list) && list.Exists(m => m.Signature == signature && m.IsVirtual))
                found = t;
        return found;
    }
}
