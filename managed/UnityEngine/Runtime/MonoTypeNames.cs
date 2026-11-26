using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Unbound
{
    /// <summary>
    /// The names game code reads for types and assemblies, as the Editor's Mono gives them
    /// (tests/projects/typenames, Unity 6000.0.71f1): a class library type is in the Mono assembly
    /// that defines it (<c>System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral,
    /// PublicKeyToken=b77a5c561934e089</c>, <c>System.Uri, System, ...</c>, <c>HashSet`1</c> in
    /// System.Core) where .NET says System.Private.CoreLib 10.0.0.0, System.Private.Uri, ...; an
    /// engine type is in its Unity module (<c>UnityEngine.Rigidbody, UnityEngine.PhysicsModule</c>)
    /// where Unbound builds every module into UnityEngine.CoreModule. A constructed generic type's
    /// name holds its arguments' names the same way. Game code persists these names (settings
    /// files, [SerializeReference] data) that the Editor then reads. Game and package assemblies'
    /// uses of <c>Type.AssemblyQualifiedName</c>, <c>Type.FullName</c>, <c>Assembly.FullName</c> and
    /// <c>Assembly.GetName()</c> are redirected here when they load (Unbound.Host.FrameworkShims);
    /// Unbound's own code keeps .NET's names. Not redirected: <c>Assembly.ToString()</c> (a call to
    /// <c>Object.ToString</c>) and other members that name assemblies.
    /// </summary>
    public static class MonoTypeNames
    {
        static Dictionary<string, string> s_ClassLibrary;
        static Dictionary<string, string> s_Modules;
        static readonly object s_Lock = new object();

        /// <summary>The Mono class library directory (Mono's lib/mono/4.5) and the module facades'
        /// directory, set before game code runs.</summary>
        internal static string FrameworkDir => Runtime.GameDomain.FrameworkDir;
        internal static string FacadeDir;

        public static string get_AssemblyQualifiedName(Type type)
        {
            if (type == null) throw new NullReferenceException();
            if (type.AssemblyQualifiedName == null) return null;
            return Name(type) + ", " + AssemblyOf(type);
        }

        public static string get_FullName(Type type)
        {
            if (type == null) throw new NullReferenceException();
            return type.FullName == null ? null : Name(type);
        }

        public static string get_FullName(Assembly assembly)
        {
            if (assembly == null) throw new NullReferenceException();
            return Mapped(assembly) ?? assembly.FullName;
        }

        public static AssemblyName GetName(Assembly assembly)
        {
            if (assembly == null) throw new NullReferenceException();
            var mapped = Mapped(assembly);
            if (mapped == null) return assembly.GetName();
            var name = new AssemblyName(mapped);
            var own = assembly.GetName();
            name.CodeBase = own.CodeBase;
            return name;
        }

        /// <summary>A type's full name with its generic arguments' assembly-qualified names Mono's way.</summary>
        static string Name(Type type)
        {
            if (type.HasElementType)
            {
                var element = type.GetElementType();
                return Name(element) + type.Name.Substring(element.Name.Length);
            }
            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                var sb = new StringBuilder(type.GetGenericTypeDefinition().FullName).Append('[');
                var arguments = type.GetGenericArguments();
                for (int i = 0; i < arguments.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append('[').Append(get_AssemblyQualifiedName(arguments[i])).Append(']');
                }
                return sb.Append(']').ToString();
            }
            return type.FullName;
        }

        /// <summary>The assembly name Mono gives a type: its element's, its generic definition's.</summary>
        static string AssemblyOf(Type type)
        {
            while (type.HasElementType) type = type.GetElementType();
            if (type.IsGenericType) type = type.GetGenericTypeDefinition();
            return MappedType(type) ?? type.Assembly.FullName;
        }

        static string MappedType(Type type)
        {
            if (IsClassLibrary(type.Assembly))
                return ClassLibrary().TryGetValue(type.FullName, out var mono) ? mono : null;
            if (type.Assembly == typeof(UnityEngine.Object).Assembly)
                return Modules().TryGetValue(type.FullName, out var module) ? module : null;
            return null;
        }

        /// <summary>An assembly's name in the Editor's domain: the .NET core library is Mono's
        /// mscorlib (measured for its primitive and collection types; a type Mono keeps elsewhere,
        /// HashSet in System.Core, still names mscorlib this way). Another class library assembly
        /// holds types of several Mono assemblies and keeps .NET's name, with a notice; the engine
        /// keeps UnityEngine.CoreModule (unmeasured which module Mono would name).</summary>
        static string Mapped(Assembly assembly)
        {
            if (assembly == typeof(object).Assembly)
                return ClassLibrary().TryGetValue("System.Object", out var corlib) ? corlib : null;
            if (IsClassLibrary(assembly))
                Runtime.Stubs.Ignored("Assembly.FullName of .NET class library assemblies",
                    "an assembly other than the core library keeps its .NET name (" + assembly.GetName().Name + " first): .NET's assemblies hold other sets of types than the Editor's Mono ones");
            return null;
        }

        static bool IsClassLibrary(Assembly assembly)
        {
            if (assembly == typeof(object).Assembly) return true;
            var name = assembly.GetName().Name;
            return name == "netstandard" || name == "mscorlib" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.", StringComparison.Ordinal);
        }

        /// <summary>Each public type of the Mono class library by full name (nested types with '+'),
        /// with its assembly's full name; the first assembly defining a name keeps it, mscorlib
        /// first. Empty without the directory.</summary>
        static Dictionary<string, string> ClassLibrary()
        {
            lock (s_Lock)
            {
                if (s_ClassLibrary != null) return s_ClassLibrary;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                var dir = FrameworkDir;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    var files = Directory.GetFiles(dir, "*.dll").OrderBy(f => Path.GetFileName(f) == "mscorlib.dll" ? 0 : 1)
                        .ThenBy(f => f, StringComparer.Ordinal);
                    foreach (var file in files) AddDefinitions(file, map);
                }
                return s_ClassLibrary = map;
            }
        }

        /// <summary>A file's type definitions into <paramref name="map"/> with its assembly's name:
        /// public ones, or (<paramref name="all"/>) every one but the module type and compiler-made
        /// ones (Unity's UnityEditor assembly defines its obsolete types without public).</summary>
        static void AddDefinitions(string file, Dictionary<string, string> map, bool all = false)
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) return;
                var md = pe.GetMetadataReader();
                if (!md.IsAssembly) return;
                var assembly = md.GetAssemblyDefinition().GetAssemblyName().FullName;
                foreach (var handle in md.TypeDefinitions)
                {
                    var definition = md.GetTypeDefinition(handle);
                    if (all ? md.GetString(definition.Name).StartsWith('<') : !Public(md, definition)) continue;
                    var name = DefinitionName(md, definition);
                    map.TryAdd(name, assembly);
                }
            }
            catch (BadImageFormatException) { }
        }

        static bool Public(MetadataReader md, TypeDefinition definition)
        {
            var visibility = definition.Attributes & TypeAttributes.VisibilityMask;
            if (visibility == TypeAttributes.Public) return true;
            if (visibility != TypeAttributes.NestedPublic) return false;
            return Public(md, md.GetTypeDefinition(definition.GetDeclaringType()));
        }

        static string DefinitionName(MetadataReader md, TypeDefinition definition)
        {
            var name = md.GetString(definition.Name);
            var declaring = definition.GetDeclaringType();
            if (!declaring.IsNil) return DefinitionName(md, md.GetTypeDefinition(declaring)) + "+" + name;
            var ns = md.GetString(definition.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        /// <summary>Each engine type by full name with its Unity assembly's full name: the UnityEngine
        /// or UnityEditor assembly that defines it itself, else the module whose facade forwards it.
        /// Other types (UnityEngine.CoreModule's own) are not listed.</summary>
        static Dictionary<string, string> Modules()
        {
            lock (s_Lock)
            {
                if (s_Modules != null) return s_Modules;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                // The types Unity's UnityEngine and UnityEditor assemblies define themselves, read
                // from the installation, before the modules' (a type Unbound's module facades
                // forward too: tests/projects/enginetypemodules UnityEditor.AssetStatus).
                var data = UnityEditor.UnityInstallInfo.DataPath;
                if (!string.IsNullOrEmpty(data))
                {
                    foreach (var umbrella in new[] { "UnityEngine", "UnityEditor" })
                    {
                        var file = Path.Combine(data, "Managed", "UnityEngine", umbrella + ".dll");
                        if (File.Exists(file)) AddDefinitions(file, map, all: true);
                    }
                }
                var dir = FacadeDir;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.dll").OrderBy(f => f, StringComparer.Ordinal))
                        if (Path.GetFileNameWithoutExtension(file) is not ("UnityEngine" or "UnityEditor")) AddForwarders(file, map);
                }
                return s_Modules = map;
            }
        }

        static void AddForwarders(string file, Dictionary<string, string> map)
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var pe = new PEReader(stream);
                if (!pe.HasMetadata) return;
                var md = pe.GetMetadataReader();
                if (!md.IsAssembly) return;
                var assembly = md.GetAssemblyDefinition().GetAssemblyName().FullName;
                foreach (var handle in md.ExportedTypes)
                {
                    var exported = md.GetExportedType(handle);
                    if (!Forwarded(md, exported)) continue;
                    map.TryAdd(ExportedName(md, exported), assembly);
                }
            }
            catch (BadImageFormatException) { }
        }

        /// <summary>Whether a type is forwarded: a nested type's outer type carries the flag.</summary>
        static bool Forwarded(MetadataReader md, ExportedType exported) =>
            exported.IsForwarder || exported.Implementation.Kind == HandleKind.ExportedType
                && Forwarded(md, md.GetExportedType((ExportedTypeHandle)exported.Implementation));

        static string ExportedName(MetadataReader md, ExportedType exported)
        {
            var name = md.GetString(exported.Name);
            if (exported.Implementation.Kind == HandleKind.ExportedType)
                return ExportedName(md, md.GetExportedType((ExportedTypeHandle)exported.Implementation)) + "+" + name;
            var ns = md.GetString(exported.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
    }
}
