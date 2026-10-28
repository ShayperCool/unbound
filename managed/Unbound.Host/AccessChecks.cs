using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Mono.Cecil;
using AssemblyDefinition = Mono.Cecil.AssemblyDefinition;
using CustomAttribute = Mono.Cecil.CustomAttribute;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using TypeDefinition = Mono.Cecil.TypeDefinition;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace Unbound.Host;

/// <summary>
/// Mono lets an assembly that asks to skip verification (<c>SecurityPermission(SkipVerification)</c>)
/// use the engine's internal types, and plugins such as Odin rely on it. .NET enforces type and
/// member access unless the accessing assembly carries <c>IgnoresAccessChecksTo</c>, so such
/// assemblies get one for every assembly they reference, the engine included.
/// </summary>
static class AccessChecks
{
    const string AttributeNamespace = "System.Runtime.CompilerServices";
    const string AttributeName = "IgnoresAccessChecksToAttribute";

    internal static DefaultAssemblyResolver Resolver => s_Resolver;

    static DefaultAssemblyResolver s_Resolver = new GameAssemblyResolver(new Dictionary<string, string>());

    /// <summary>Where the rewrite finds referenced assemblies (writing attributes and parameter
    /// defaults may need their types): the game's own by name (their files are named by content),
    /// others in these directories.</summary>
    internal static void SetSearchDirectories(IEnumerable<string> directories, IReadOnlyDictionary<string, string> gameAssemblies)
    {
        s_Resolver.Dispose();
        s_Resolver = new GameAssemblyResolver(gameAssemblies);
        foreach (var d in s_Resolver.GetSearchDirectories()) s_Resolver.RemoveSearchDirectory(d);
        foreach (var d in directories)
            if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) s_Resolver.AddSearchDirectory(d);
    }

    /// <summary>Drops the assemblies the resolver has read (it reads them again when asked).</summary>
    internal static void ReleaseResolved() => s_Resolver.Dispose();

    /// <summary>The portable PDB of the image the filters are rewriting (null: none). They read the
    /// image with it and write both back, so an adapted assembly keeps its source lines in stack
    /// traces; set by the caller around the filters, per thread.</summary>
    [ThreadStatic] internal static byte[]? Symbols;

    /// <summary>Reads an image to rewrite, with <see cref="Symbols"/> when they match it.</summary>
    internal static AssemblyDefinition Read(MemoryStream input)
    {
        if (Symbols != null)
        {
            try
            {
                return AssemblyDefinition.ReadAssembly(input, new ReaderParameters
                {
                    AssemblyResolver = s_Resolver,
                    ReadSymbols = true,
                    SymbolStream = new MemoryStream(Symbols),
                    SymbolReaderProvider = new Mono.Cecil.Cil.PortablePdbReaderProvider(),
                });
            }
            catch (Exception)
            {
                Symbols = null;
                input.Position = 0;
            }
        }
        return AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = s_Resolver });
    }

    /// <summary>Writes a rewritten image to <paramref name="output"/>, and its symbols to
    /// <see cref="Symbols"/> when it was read with them.</summary>
    internal static void Write(AssemblyDefinition assembly, MemoryStream output)
    {
        if (Symbols != null && assembly.MainModule.HasSymbols)
        {
            using var pdb = new MemoryStream();
            assembly.Write(output, new WriterParameters
            {
                WriteSymbols = true,
                SymbolStream = pdb,
                SymbolWriterProvider = new Mono.Cecil.Cil.PortablePdbWriterProvider(),
            });
            Symbols = pdb.ToArray();
            return;
        }
        Symbols = null;
        assembly.Write(output);
    }

    internal static byte[] Relax(byte[] image)
    {
        bool skips = SkipsVerification(image);
        bool jobs = HasJobRegistration(image);
        if (!skips && !jobs) return image;
        using var input = new MemoryStream(image);
        using var assembly = Read(input);
        var module = assembly.MainModule;
        var attribute = DefineAttribute(module);
        var targets = new SortedSet<string>(StringComparer.Ordinal);
        if (skips)
        {
            targets.Add(typeof(UnityEngine.Object).Assembly.GetName().Name!);
            foreach (var reference in module.AssemblyReferences) targets.Add(reference.Name);
        }
        // The jobs IL post-processor registers private nested job structs from a top-level class
        // (EarlyJobInit<Outer.PrivateJob>): Mono allows it, .NET only with access checks off for
        // the assembly's own types.
        if (jobs) targets.Add(assembly.Name.Name);
        foreach (var target in targets)
        {
            var custom = new CustomAttribute(attribute);
            custom.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, target));
            assembly.CustomAttributes.Add(custom);
        }
        using var output = new MemoryStream();
        Write(assembly, output);
        return output.ToArray();
    }

    /// <summary>Whether the jobs IL post-processor added its registration class.</summary>
    static bool HasJobRegistration(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        if (!pe.HasMetadata) return false;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) return false;
        foreach (var handle in reader.TypeDefinitions)
            if (reader.GetString(reader.GetTypeDefinition(handle).Name).StartsWith("__JobReflectionRegistrationOutput__", StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>Whether the assembly's permission requests include SkipVerification.</summary>
    static bool SkipsVerification(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        if (!pe.HasMetadata) return false;
        var reader = pe.GetMetadataReader();
        if (!reader.IsAssembly) return false;
        foreach (var handle in reader.GetAssemblyDefinition().GetDeclarativeSecurityAttributes())
        {
            var blob = reader.GetBlobBytes(reader.GetDeclarativeSecurityAttribute(handle).PermissionSet);
            if (Encoding.ASCII.GetString(blob).Contains("SkipVerification", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The attribute's constructor: .NET recognises the attribute by name in any assembly.</summary>
    static MethodReference DefineAttribute(Mono.Cecil.ModuleDefinition module)
    {
        var baseType = module.ImportReference(typeof(Attribute));
        var type = new TypeDefinition(AttributeNamespace, AttributeName,
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit | TypeAttributes.Class, baseType);
        var ctor = new MethodDefinition(".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition("assemblyName", ParameterAttributes.None, module.TypeSystem.String));
        var il = ctor.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, module.ImportReference(typeof(Attribute).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)!));
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        type.Methods.Add(ctor);
        module.Types.Add(type);
        return ctor;
    }
}

/// <summary>Resolves the game's assemblies by name to their files, the rest in search directories.</summary>
sealed class GameAssemblyResolver : DefaultAssemblyResolver
{
    readonly IReadOnlyDictionary<string, string> m_Game;

    internal GameAssemblyResolver(IReadOnlyDictionary<string, string> game) => m_Game = game;

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        if (m_Game.TryGetValue(name.Name, out var path) && File.Exists(path))
        {
            var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = this });
            RegisterAssembly(assembly);
            return assembly;
        }
        return base.Resolve(name);
    }
}
