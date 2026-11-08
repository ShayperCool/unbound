using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Unbound.ApiTools;

/// <summary>
/// Generates the runtime facades of Unity's modules. Game code and plugins are compiled against
/// Unity's real module assemblies (<c>UnityEngine.PhysicsModule</c>, ..., and the monolithic
/// <c>UnityEngine.dll</c> of old plugins); at run time Unbound has one implementation assembly,
/// <c>UnityEngine.CoreModule</c>. Each facade carries a module's exact identity (name, version,
/// culture, public key) and no types: only an ExportedType row per type the module defines or
/// forwards, all forwarded to <c>UnityEngine.CoreModule</c>, so references bind to Unbound's types.
///
/// The facade set depends only on the Unity release, so it lives in the global cache per release;
/// each complete set is published under a hash of the generator version and input contents.
/// </summary>
public static class FacadeGenerator
{
    /// <summary>Bump when the facades generated from the same inputs would differ.</summary>
    public const string FormatVersion = "unbound-facades-2";

    const string StampFile = "facades.stamp.json";

    /// <param name="platformModules">The install's platform modules (see <see cref="UnityModules"/>);
    /// they are part of the stamp, so a changed list regenerates.</param>
    public static FacadeReport Generate(string unityManagedEngineDir, string outputDir, IEnumerable<string>? platformModules = null)
    {
        var watch = Stopwatch.StartNew();
        var inputs = UnityModules.Files(unityManagedEngineDir, platformModules)
            .Where(f => Path.GetFileNameWithoutExtension(f) != UnityModules.ImplAssemblyName)
            .ToList();
        // Read each input once: the cache key and generated image must describe the same bytes.
        var sources = inputs.Select(path => (Path: path, Bytes: File.ReadAllBytes(path))).ToList();
        var stamp = new FacadeStamp
        {
            Version = FormatVersion,
            Inputs = sources.Select(f => $"{Path.GetFileName(f.Path)}\t{Convert.ToHexString(SHA256.HashData(f.Bytes))}").ToList(),
        };
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(stamp, StampJson)))).ToLowerInvariant();
        outputDir = Path.Combine(outputDir, key);
        var stampPath = Path.Combine(outputDir, StampFile);
        bool Complete() => ReadStamp(stampPath) is { } previous
            && previous.Version == stamp.Version
            && previous.Inputs.SequenceEqual(stamp.Inputs)
            && previous.Facades.Count == sources.Count
            && previous.Facades.All(f => File.Exists(Path.Combine(outputDir, f.Name + ".dll")));
        if (Complete())
            return Report(outputDir, ReadStamp(stampPath)!.Facades, reused: true, watch);

        Directory.CreateDirectory(Path.GetDirectoryName(outputDir)!);
        var staging = outputDir + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var source in sources)
                stamp.Facades.Add(WriteFacade(source.Bytes, source.Path, Path.Combine(staging, Path.GetFileName(source.Path))));
            File.WriteAllText(Path.Combine(staging, StampFile), JsonSerializer.Serialize(stamp, StampJson));
            try
            {
                // Readers learn this path only after all of its assemblies and stamp exist.
                Directory.Move(staging, outputDir);
            }
            catch (IOException) when (Directory.Exists(outputDir))
            {
                if (!Complete())
                {
                    // Repair an externally damaged entry. Every replacement is still atomic;
                    // another generator of this key can only publish the same contents.
                    foreach (var file in Directory.GetFiles(staging))
                        AtomicWrite(Path.Combine(outputDir, Path.GetFileName(file)), File.ReadAllBytes(file));
                }
            }
            return Report(outputDir, stamp.Facades, reused: false, watch);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    static void AtomicWrite(string output, byte[] bytes)
    {
        var temporary = output + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(bytes);
            File.Move(temporary, output, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    static readonly JsonSerializerOptions StampJson = new() { WriteIndented = true };

    static FacadeStamp? ReadStamp(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<FacadeStamp>(File.ReadAllText(path), StampJson) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static FacadeReport Report(string outputDir, List<FacadeInfo> facades, bool reused, Stopwatch watch) => new()
    {
        OutputDir = outputDir,
        Reused = reused,
        Assemblies = facades.Count,
        Types = facades.Sum(f => f.Types),
        Facades = facades,
        Milliseconds = watch.ElapsedMilliseconds,
    };

    /// <summary>One type to forward: its name, its ExportedType flags, and the index of its enclosing
    /// type in the list (-1 for a top-level type, forwarded to the implementation assembly).</summary>
    readonly record struct Forward(string Namespace, string Name, TypeAttributes Attributes, int Parent);

    /// <summary>Writes the facade of one Unity module and returns what it forwards.</summary>
    public static FacadeInfo WriteFacade(string input, string output)
        => WriteFacade(File.ReadAllBytes(input), input, output);

    static FacadeInfo WriteFacade(byte[] bytes, string input, string output)
    {
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        var md = pe.GetMetadataReader();
        var assembly = md.GetAssemblyDefinition();
        var name = md.GetString(assembly.Name);

        var forwards = new List<Forward>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Types the module defines (nested types under their enclosing type).
        void AddDefinition(TypeDefinitionHandle handle, int parent)
        {
            var type = md.GetTypeDefinition(handle);
            var fullName = MetadataNames.TypeDefinition(md, handle);
            if (!seen.Add(fullName))
                return;
            var attributes = parent < 0
                ? TypeForwarder
                : type.Attributes & TypeAttributes.VisibilityMask;
            forwards.Add(new Forward(parent < 0 ? md.GetString(type.Namespace) : "", md.GetString(type.Name), attributes, parent));
            var index = forwards.Count - 1;
            foreach (var nested in type.GetNestedTypes())
                AddDefinition(nested, index);
        }
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            if (type.GetDeclaringType().IsNil && md.GetString(type.Name) != "<Module>")
                AddDefinition(handle, -1);
        }

        // Types the module itself forwards (UnityEngine.dll and UnityEditor.dll forward to modules;
        // modules forward types that moved between them). Nested exported types name their
        // enclosing exported type as their implementation.
        var nestedExports = new Dictionary<ExportedTypeHandle, List<ExportedTypeHandle>>();
        foreach (var handle in md.ExportedTypes)
        {
            if (md.GetExportedType(handle).Implementation is { Kind: HandleKind.ExportedType } outer)
            {
                var key = (ExportedTypeHandle)outer;
                if (!nestedExports.TryGetValue(key, out var list))
                    nestedExports[key] = list = new();
                list.Add(handle);
            }
        }
        void AddExport(ExportedTypeHandle handle, int parent)
        {
            var type = md.GetExportedType(handle);
            if (!seen.Add(MetadataNames.ExportedType(md, handle)))
                return;
            var attributes = parent < 0 ? TypeForwarder : type.Attributes & TypeAttributes.VisibilityMask;
            forwards.Add(new Forward(parent < 0 ? md.GetString(type.Namespace) : "", md.GetString(type.Name), attributes, parent));
            var index = forwards.Count - 1;
            if (nestedExports.TryGetValue(handle, out var nested))
                foreach (var inner in nested)
                    AddExport(inner, index);
        }
        foreach (var handle in md.ExportedTypes)
        {
            var kind = md.GetExportedType(handle).Implementation.Kind;
            if (kind == HandleKind.AssemblyReference)
                AddExport(handle, -1);
            else if (kind == HandleKind.AssemblyFile)
                throw new NotSupportedException($"{input}: multi-module assemblies are not supported");
        }

        var image = BuildFacade(md, assembly, name, forwards);
        AtomicWrite(output, image);
        return new FacadeInfo { Name = name, Types = forwards.Count };
    }

    /// <summary>The flag that marks a top-level ExportedType row as a forwarder (ECMA-335 II.23.1.15,
    /// <c>IsTypeForwarder</c>); System.Reflection.Metadata does not name it publicly.</summary>
    const TypeAttributes TypeForwarder = (TypeAttributes)0x00200000;

    static byte[] BuildFacade(MetadataReader source, AssemblyDefinition assembly, string name, List<Forward> forwards)
    {
        var mb = new MetadataBuilder();
        mb.AddAssembly(
            mb.GetOrAddString(name),
            assembly.Version,
            assembly.Culture.IsNil ? default : mb.GetOrAddString(source.GetString(assembly.Culture)),
            assembly.PublicKey.IsNil ? default : mb.GetOrAddBlob(source.GetBlobBytes(assembly.PublicKey)),
            assembly.Flags,
            assembly.HashAlgorithm);
        // A deterministic module version id: a function of everything the facade contains.
        var identity = new StringBuilder(FormatVersion).Append('\n').Append(name).Append(' ').Append(assembly.Version);
        foreach (var f in forwards)
            identity.Append('\n').Append(f.Namespace).Append('.').Append(f.Name).Append(' ').Append(f.Parent);
        var mvid = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())).AsSpan(0, 16));
        mb.AddModule(0, mb.GetOrAddString(name + ".dll"), mb.GetOrAddGuid(mvid), default, default);
        mb.AddTypeDefinition(
            default, default, mb.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var implementation = mb.AddAssemblyReference(
            mb.GetOrAddString(UnityModules.ImplAssemblyName), new Version(0, 0, 0, 0),
            default, default, default, default);
        var handles = new ExportedTypeHandle[forwards.Count];
        for (int i = 0; i < forwards.Count; i++)
        {
            var f = forwards[i];
            handles[i] = mb.AddExportedType(
                f.Attributes,
                mb.GetOrAddString(f.Namespace),
                mb.GetOrAddString(f.Name),
                f.Parent < 0 ? implementation : handles[f.Parent],
                0);
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(mb),
            new BlobBuilder(),
            flags: CorFlags.ILOnly,
            deterministicIdProvider: content =>
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                foreach (var blob in content)
                    hash.AppendData(blob.GetBytes());
                return BlobContentId.FromHash(hash.GetHashAndReset());
            });
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}

public sealed class FacadeReport
{
    public string OutputDir { get; set; } = "";
    /// <summary>The facades were up to date and reused.</summary>
    public bool Reused { get; set; }
    public int Assemblies { get; set; }
    public int Types { get; set; }
    public List<FacadeInfo> Facades { get; set; } = new();
    public long Milliseconds { get; set; }
}

public sealed class FacadeInfo
{
    public string Name { get; set; } = "";
    public int Types { get; set; }
}

sealed class FacadeStamp
{
    public string Version { get; set; } = "";
    /// <summary><c>file\tsha256</c> of every input module.</summary>
    public List<string> Inputs { get; set; } = new();
    public List<FacadeInfo> Facades { get; set; } = new();
}
