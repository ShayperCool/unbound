using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>One compiled script assembly or precompiled plugin of the game.</summary>
    internal sealed class GameAssembly
    {
        internal string Name;
        internal string Dll;
        internal string Pdb;
        /// <summary>The asset GUID of the plugin DLL; scripts of a plugin are `{fileID: hash, guid: this}`.</summary>
        internal string PluginGuid;
        /// <summary>Script files: (file name without extension, asset guid) — a script's class has
        /// the file's name.</summary>
        internal List<(string FileName, string Guid, string Namespace)> Scripts = new List<(string, string, string)>();
        /// <summary>One of the assembly's source files (or the plugin DLL): where it lives.</summary>
        internal string SourcePath;
        // As CompilationPipeline reports them (script assemblies only).
        internal bool Editor;
        /// <summary>`Assets/...` and `Packages/<name>/...` paths.</summary>
        internal string[] Sources = Array.Empty<string>();
        internal string[] Defines = Array.Empty<string>();
        /// <summary>Script assemblies referenced, by name.</summary>
        internal string[] References = Array.Empty<string>();
        /// <summary>Precompiled DLLs referenced.</summary>
        internal string[] CompiledReferences = Array.Empty<string>();
        /// <summary>The asmdef's Unity path, "" for a predefined assembly.</summary>
        internal string Asmdef = "";
        internal string RootNamespace = "";
    }

    /// <summary>
    /// The game's code: script assemblies and plugins, loaded into a load context of their own that
    /// binds Unity's module names to the facades and those to Unbound's engine. A reload creates a new
    /// context (the old one is kept alive while its objects are).
    /// </summary>
    internal static class GameDomain
    {
        /// <summary>The release's .NET Framework directory (Mono's lib/mono/4.5).</summary>
        internal static string FrameworkDir;
        /// <summary>The project's native libraries the Editor would load here.</summary>
        internal static IReadOnlyList<string> NativePlugins = Array.Empty<string>();
        /// <summary>Applied to every game assembly's image before it loads (set by the host).</summary>
        internal static Func<byte[], byte[]> ImageFilter;
        /// <summary>Identifies what <see cref="ImageFilter"/> does: adapted images are cached under
        /// their input's hash plus this (set by the host with the filter).</summary>
        internal static string ImageFilterVersion = "";
        static GameLoadContext s_Context;
        static readonly List<Assembly> s_Assemblies = new List<Assembly>();
        static readonly Dictionary<string, Type> s_ScriptsByGuid = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<(string Guid, long FileId), Type> s_PluginScripts = new Dictionary<(string, long), Type>();
        static readonly Dictionary<Type, string> s_GuidByScript = new Dictionary<Type, string>();
        static int s_Generation;

        internal static IReadOnlyList<Assembly> Assemblies() => s_Assemblies;

        static IReadOnlyList<GameAssembly> s_Descriptions = Array.Empty<GameAssembly>();

        /// <summary>The script assemblies and plugins as they were compiled and loaded.</summary>
        internal static IReadOnlyList<GameAssembly> Descriptions => s_Descriptions;

        static readonly Dictionary<Assembly, string> s_SourcePaths = new Dictionary<Assembly, string>();

        /// <summary>Where a game assembly's code lives: one of its source files, or its plugin DLL.</summary>
        internal static string SourcePathOf(Assembly assembly) => s_SourcePaths.TryGetValue(assembly, out var p) ? p : null;

        internal static int Generation => s_Generation;

        internal static void Load(IReadOnlyList<GameAssembly> assemblies, string facadeDir)
        {
            s_Generation++;
            if (s_Generation > 1) Unbound.Runtime.Playables.PlayableGraphData.ForgetAll();
            s_Context = new GameLoadContext($"Unbound.Game.{s_Generation}", facadeDir, assemblies);
            s_Assemblies.Clear();
            s_Descriptions = assemblies;
            s_SourcePaths.Clear();
            s_ScriptsByGuid.Clear();
            s_PluginScripts.Clear();
            s_LegacyScripts.Clear();
            s_GuidByScript.Clear();
            TypeResolver.ClearCache();
            SerializationRules.ClearCache();
            ScriptMessages.ClearCache();
            Physics2DWorld.ClearCaches();
            foreach (var a in assemblies)
            {
                Assembly assembly;
                try
                {
                    assembly = s_Context.LoadFromAssemblyName(new AssemblyName(a.Name));
                }
                catch (Exception e)
                {
                    Debug.LogError($"Unbound: could not load assembly {a.Name}: {e.Message}");
                    continue;
                }
                s_Assemblies.Add(assembly);
                s_SourcePaths[assembly] = a.SourcePath ?? a.Dll;
                IndexScripts(assembly, a);
            }
        }

        static Type[] SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                foreach (var loaderException in e.LoaderExceptions)
                {
                    if (loaderException != null)
                        Debug.LogWarning($"Unbound: {assembly.GetName().Name}: {loaderException.Message}");
                }
                var types = new List<Type>();
                foreach (var t in e.Types) if (t != null) types.Add(t);
                return types.ToArray();
            }
        }

        static void IndexScripts(Assembly assembly, GameAssembly info)
        {
            var types = SafeTypes(assembly);
            if (info.PluginGuid != null)
            {
                foreach (var type in types)
                {
                    try
                    {
                        if (!IsScriptClass(type)) continue;
                        s_PluginScripts[(info.PluginGuid.ToLowerInvariant(), PluginScriptFileId(type))] = type;
                    }
                    catch (Exception)
                    {
                        // Its base type is not available yet.
                    }
                }
                return;
            }
            var byName = new Dictionary<string, List<Type>>();
            foreach (var type in types)
            {
                try
                {
                    if (type.IsNested || type.IsGenericTypeDefinition) continue;
                    if (!byName.TryGetValue(type.Name, out var list)) byName[type.Name] = list = new List<Type>();
                    list.Add(type);
                }
                catch (Exception e)
                {
                    // A type whose base or interfaces are not available yet: it cannot be a script here.
                    Debug.LogWarning($"Unbound: {info.Name}: skipping a type: {e.Message}");
                }
            }
            foreach (var (fileName, guid, ns) in info.Scripts)
            {
                if (!byName.TryGetValue(fileName, out var candidates)) continue;
                // The class the file declares (by namespace) wins over others of the same name.
                Type chosen = null;
                if (ns != null)
                    foreach (var candidate in candidates)
                        if ((candidate.Namespace ?? "") == ns) { chosen = candidate; break; }
                if (chosen == null)
                {
                    foreach (var candidate in candidates)
                    {
                        bool isScript;
                        try { isScript = IsScriptClass(candidate); }
                        catch (Exception) { isScript = false; }
                        if (isScript) { chosen = candidate; break; }
                    }
                }
                chosen ??= candidates[0];
                s_ScriptsByGuid[guid] = chosen;
                s_GuidByScript[chosen] = guid;
            }
        }

        /// <summary>A class a script file can stand for: any concrete UnityEngine.Object class
        /// (behaviours, scriptable objects, importers, editors).</summary>
        static bool IsScriptClass(Type type) =>
            !type.IsAbstract && !type.ContainsGenericParameters && typeof(UnityEngine.Object).IsAssignableFrom(type);

        /// <summary>The class a serialized `m_Script` reference names.</summary>
        internal static Type ResolveScript(string guid, long fileId)
        {
            if (guid == null) return null;
            if (fileId == 11500000 && s_ScriptsByGuid.TryGetValue(guid, out var type)) return type;
            if (s_PluginScripts.TryGetValue((guid.ToLowerInvariant(), fileId), out type)) return type;
            if (s_ScriptsByGuid.TryGetValue(guid, out type)) return type;
            return ResolveLegacy(guid, fileId) ?? EngineScripts.Resolve(guid, fileId);
        }

        /// <summary>Script DLLs Unity replaced by packages, with the release feature that made the
        /// change: references into them (`{fileID: class hash, guid: the DLL's}`) resolve to the same
        /// class in the package's assembly, as the Editor remaps them.</summary>
        static readonly (string Guid, string Assembly, string Feature)[] s_LegacyScriptDlls =
        {
            // UnityEngine.UI.dll of the Editor's UnityExtensions, before uGUI became a package.
            ("f70555f144d8491a825f0804e09c671c", "UnityEngine.UI", "ImplicitUguiReferences"),
        };

        static readonly Dictionary<string, Dictionary<long, Type>> s_LegacyScripts = new Dictionary<string, Dictionary<long, Type>>(StringComparer.OrdinalIgnoreCase);

        static Type ResolveLegacy(string guid, long fileId)
        {
            foreach (var (legacyGuid, assemblyName, feature) in s_LegacyScriptDlls)
            {
                if (!string.Equals(guid, legacyGuid, StringComparison.OrdinalIgnoreCase)) continue;
                if (!ProjectInfo.Features.Contains(feature)) return null;
                if (!s_LegacyScripts.TryGetValue(legacyGuid, out var byFileId))
                {
                    byFileId = new Dictionary<long, Type>();
                    var assembly = Find(assemblyName);
                    if (assembly != null)
                        foreach (var type in SafeTypes(assembly))
                            if (IsScriptClass(type)) byFileId[PluginScriptFileId(type)] = type;
                    s_LegacyScripts[legacyGuid] = byFileId;
                }
                return byFileId.TryGetValue(fileId, out var found) ? found : null;
            }
            return null;
        }

        internal static Assembly Find(string name)
        {
            foreach (var assembly in s_Assemblies)
                if (assembly.GetName().Name == name) return assembly;
            return null;
        }

        /// <summary>Code of loaded assemblies changed in place (Edit and Continue): reflection-based
        /// caches are rebuilt and behaviours re-register their messages (an added Update runs).</summary>
        internal static void AfterCodeUpdate()
        {
            TypeResolver.ClearCache();
            SerializationRules.ClearCache();
            ScriptMessages.ClearCache();
            Messaging.ClearCaches();
            Physics2DWorld.ClearCaches();
            Lifecycle.RefreshRegistrations();
        }

        internal static string GuidOfScript(Type type) => s_GuidByScript.TryGetValue(type, out var guid) ? guid : null;

        internal static Type FindTypeByName(string className)
        {
            foreach (var assembly in s_Assemblies)
                foreach (var type in SafeTypes(assembly))
                    if (type.Name == className || type.FullName == className) return type;
            return null;
        }

        /// <summary>Unity's file id of a class in a DLL: the first four bytes of
        /// MD4("s\0\0\0" + namespace + name), little endian.</summary>
        internal static long PluginScriptFileId(Type type)
        {
            var text = "s\0\0\0" + (type.Namespace ?? "") + type.Name;
            var hash = Md4.Hash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToInt32(hash, 0);
        }

        sealed class GameLoadContext : AssemblyLoadContext
        {
            readonly string m_FacadeDir;
            readonly Dictionary<string, GameAssembly> m_ByName = new Dictionary<string, GameAssembly>(StringComparer.OrdinalIgnoreCase);
            static readonly Assembly s_Engine = typeof(UnityEngine.Object).Assembly;

            /// <summary>Framework assemblies .NET ships only as a thin compatibility facade: the game
            /// gets Mono's full one, which is what it was compiled against.</summary>
            static readonly HashSet<string> s_MonoOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System.Web" };

            internal GameLoadContext(string name, string facadeDir, IReadOnlyList<GameAssembly> assemblies) : base(name, isCollectible: false)
            {
                m_FacadeDir = facadeDir;
                foreach (var a in assemblies) m_ByName[a.Name] = a;
                Resolving += ResolveFrameworkAssembly;
            }

            /// <summary>.NET Framework assemblies .NET lacks (System.Web, ...) come from the release's
            /// Mono, as they do in the Editor; asked only when nothing else has the assembly.</summary>
            static Assembly ResolveFrameworkAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
            {
                if (string.IsNullOrEmpty(FrameworkDir)) return null;
                var path = Path.Combine(FrameworkDir, assemblyName.Name + ".dll");
                return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                var name = assemblyName.Name;
                if (name == s_Engine.GetName().Name) return s_Engine;
                if (m_ByName.TryGetValue(name, out var game))
                    return LoadGameAssembly(game);
                if (m_FacadeDir != null)
                {
                    var facade = Path.Combine(m_FacadeDir, name + ".dll");
                    if (File.Exists(facade)) return LoadFromAssemblyPath(facade);
                }
                if (s_MonoOnly.Contains(name) && !string.IsNullOrEmpty(FrameworkDir))
                {
                    var framework = Path.Combine(FrameworkDir, name + ".dll");
                    if (File.Exists(framework)) return LoadFromAssemblyPath(framework);
                }
                return null; // the default context: the class library and Unbound's own assemblies
            }

            /// <summary>P/Invokes bind to the project's native plugins the Editor loads on this
            /// machine (see the PluginImporter's Editor OS/CPU settings); others fail when called,
            /// with DllNotFoundException, as in the Editor.</summary>
            /// <summary>
            /// Loads from a file, as Unity does, so <c>Assembly.Location</c> names one (code
            /// compares it with asset paths): the assembly's own file when Unbound keeps its image
            /// as it is, otherwise the adapted image saved under Library/Unbound/AdaptedAssemblies.
            /// </summary>
            Assembly LoadGameAssembly(GameAssembly game)
            {
                var original = File.ReadAllBytes(game.Dll);
                // Adapting is slow (most assemblies are read and rewritten): the outcome is cached
                // under the input's hash plus the filters' version.
                var adapted = Path.Combine(ProjectInfo.ProjectPath, "Library", "Unbound", "AdaptedAssemblies");
                var key = Hash(original, Path.GetFileName(game.Dll) + "|" + ImageFilterVersion);
                var record = Path.Combine(adapted, "inputs", key);
                string path = File.Exists(record) ? File.ReadAllText(record) : null;
                if (path == null || (path.Length > 0 && !File.Exists(path)))
                {
                    var image = PeImage.MakePortable(original);
                    if (ImageFilter != null) image = ImageFilter(image);
                    // Script assemblies load from a file named as Unity names it (the Test Framework
                    // matches Assembly.Location's file name with the compiled assemblies').
                    var fileName = game.PluginGuid == null ? game.Name + ".dll" : Path.GetFileName(game.Dll);
                    path = ReferenceEquals(image, original) && fileName == Path.GetFileName(game.Dll)
                        ? "" : Path.Combine(adapted, Hash(image, ""), fileName);
                    if (path.Length > 0 && !File.Exists(path)) WriteAtomically(path, image);
                    WriteAtomically(record, System.Text.Encoding.UTF8.GetBytes(path));
                }
                if (path.Length == 0) return LoadFromAssemblyPath(game.Dll);
                // The original's symbols still match: only metadata Unbound added differs.
                if (game.Pdb != null && File.Exists(game.Pdb))
                {
                    var pdb = Path.ChangeExtension(path, ".pdb");
                    if (!File.Exists(pdb)) File.Copy(game.Pdb, pdb, overwrite: true);
                }
                return LoadFromAssemblyPath(path);
            }

            static string Hash(byte[] data, string salt)
            {
                using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                hash.AppendData(data);
                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(salt));
                return Convert.ToHexString(hash.GetHashAndReset()).Substring(0, 32);
            }

            static void WriteAtomically(string path, byte[] data)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + "." + Environment.ProcessId + ".tmp";
                File.WriteAllBytes(temp, data);
                File.Move(temp, path, overwrite: true);
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                var wanted = NativeName(unmanagedDllName);
                foreach (var path in NativePlugins)
                    if (NativeName(Path.GetFileName(path)) == wanted)
                        return LoadUnmanagedDllFromPath(path);
                return IntPtr.Zero;
            }

            /// <summary>"libfoo.so", "foo.so", "foo" and "libfoo" name the same library.</summary>
            static string NativeName(string name)
            {
                if (name.EndsWith(".so", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 3);
                else if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.StartsWith("lib", StringComparison.Ordinal)) name = name.Substring(3);
                return name;
            }
        }
    }

    /// <summary>
    /// Mono ignores the 32-bit flags of IL-only assemblies; CoreCLR refuses them in a 64-bit process.
    /// Plugins built for 32-bit-preferred .NET Framework (Firebase's) are pure IL, so the flags are
    /// cleared in the image before it is loaded.
    /// </summary>
    internal static class PeImage
    {
        const int ComImageFlagsILOnly = 0x1;
        const int ComImageFlags32BitRequired = 0x2;
        const int ComImageFlags32BitPreferred = 0x20000;

        internal static byte[] MakePortable(byte[] image)
        {
            try
            {
                int pe = BitConverter.ToInt32(image, 0x3c);
                if (image.Length < pe + 24 || image[pe] != 'P' || image[pe + 1] != 'E') return image;
                int optional = pe + 24;
                ushort magic = BitConverter.ToUInt16(image, optional);
                int dataDirectories = optional + (magic == 0x20b ? 112 : 96);
                int cliRva = BitConverter.ToInt32(image, dataDirectories + 14 * 8);
                if (cliRva == 0) return image;
                int cliOffset = RvaToOffset(image, pe, cliRva);
                if (cliOffset < 0) return image;
                int flagsOffset = cliOffset + 16;
                int flags = BitConverter.ToInt32(image, flagsOffset);
                if ((flags & ComImageFlagsILOnly) == 0) return image;
                int cleared = flags & ~(ComImageFlags32BitRequired | ComImageFlags32BitPreferred);
                if (cleared == flags) return image;
                var copy = (byte[])image.Clone();
                BitConverter.GetBytes(cleared).CopyTo(copy, flagsOffset);
                return copy;
            }
            catch (Exception)
            {
                return image;
            }
        }

        static int RvaToOffset(byte[] image, int pe, int rva)
        {
            int sections = BitConverter.ToUInt16(image, pe + 6);
            int optionalSize = BitConverter.ToUInt16(image, pe + 20);
            int table = pe + 24 + optionalSize;
            for (int i = 0; i < sections; i++)
            {
                int s = table + i * 40;
                int virtualSize = BitConverter.ToInt32(image, s + 8);
                int virtualAddress = BitConverter.ToInt32(image, s + 12);
                int rawSize = BitConverter.ToInt32(image, s + 16);
                int rawPointer = BitConverter.ToInt32(image, s + 20);
                if (rva >= virtualAddress && rva < virtualAddress + Math.Max(virtualSize, rawSize))
                    return rva - virtualAddress + rawPointer;
            }
            return -1;
        }
    }

    /// <summary>Script classes Unbound implements itself (engine-side MonoBehaviours of packages are
    /// compiled from source and resolve like any script).</summary>
    internal static class EngineScripts
    {
        /// <summary>The guid of the engine's built-in resources, which built-in script classes'
        /// MonoScripts live in.</summary>
        const string BuiltinGuid = "0000000000000000e000000000000000";

        /// <summary>Engine script classes by their MonoScript's file id in the built-in resources
        /// (as serialized `m_Script` references name them).</summary>
        static readonly Dictionary<long, Type> s_Builtin = new Dictionary<long, Type>
        {
            { 13312, typeof(UnityEngine.Tilemaps.Tile) },
        };

        internal static Type Resolve(string guid, long fileId) =>
            string.Equals(guid, BuiltinGuid, StringComparison.OrdinalIgnoreCase) && s_Builtin.TryGetValue(fileId, out var type) ? type : null;
    }

    /// <summary>MD4 (RFC 1320), which Unity uses for script file ids of DLL classes.</summary>
    internal static class Md4
    {
        internal static byte[] Hash(byte[] input)
        {
            uint a = 0x67452301, b = 0xefcdab89, c = 0x98badcfe, d = 0x10325476;
            long bitLength = (long)input.Length * 8;
            int paddedLength = ((input.Length + 8) / 64 + 1) * 64;
            var message = new byte[paddedLength];
            Array.Copy(input, message, input.Length);
            message[input.Length] = 0x80;
            for (int i = 0; i < 8; i++) message[paddedLength - 8 + i] = (byte)(bitLength >> (8 * i));
            var x = new uint[16];
            for (int block = 0; block < paddedLength; block += 64)
            {
                for (int i = 0; i < 16; i++) x[i] = BitConverter.ToUInt32(message, block + i * 4);
                uint aa = a, bb = b, cc = c, dd = d;
                static uint F(uint x1, uint y, uint z) => (x1 & y) | (~x1 & z);
                static uint G(uint x1, uint y, uint z) => (x1 & y) | (x1 & z) | (y & z);
                static uint H(uint x1, uint y, uint z) => x1 ^ y ^ z;
                static uint Rot(uint v, int s) => (v << s) | (v >> (32 - s));
                int[] s1 = { 3, 7, 11, 19 };
                for (int i = 0; i < 16; i++)
                {
                    uint t = a + F(b, c, d) + x[i];
                    a = d; d = c; c = b; b = Rot(t, s1[i % 4]);
                }
                int[] s2 = { 3, 5, 9, 13 };
                int[] order2 = { 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15 };
                for (int i = 0; i < 16; i++)
                {
                    uint t = a + G(b, c, d) + x[order2[i]] + 0x5a827999;
                    a = d; d = c; c = b; b = Rot(t, s2[i % 4]);
                }
                int[] s3 = { 3, 9, 11, 15 };
                int[] order3 = { 0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15 };
                for (int i = 0; i < 16; i++)
                {
                    uint t = a + H(b, c, d) + x[order3[i]] + 0x6ed9eba1;
                    a = d; d = c; c = b; b = Rot(t, s3[i % 4]);
                }
                a += aa; b += bb; c += cc; d += dd;
            }
            var result = new byte[16];
            BitConverter.GetBytes(a).CopyTo(result, 0);
            BitConverter.GetBytes(b).CopyTo(result, 4);
            BitConverter.GetBytes(c).CopyTo(result, 8);
            BitConverter.GetBytes(d).CopyTo(result, 12);
            return result;
        }
    }
}
