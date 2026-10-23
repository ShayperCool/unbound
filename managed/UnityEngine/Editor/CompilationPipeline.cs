using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;

namespace UnityEditor.Compilation
{
    public enum AssembliesType
    {
        Editor = 0,
        Player = 1,
        PlayerWithoutTestAssemblies = 2,
    }

    [Flags]
    public enum AssemblyFlags
    {
        None = 0,
        EditorAssembly = 1,
    }

    /// <summary>A script assembly as the Editor's compilation pipeline describes it.</summary>
    public partial class Assembly
    {
        readonly string m_Name;
        readonly string m_OutputPath;
        readonly string[] m_SourceFiles;
        readonly string[] m_Defines;
        Assembly[] m_AssemblyReferences;
        readonly string[] m_CompiledAssemblyReferences;
        readonly AssemblyFlags m_Flags;
        internal string m_RootNamespace = "";

        public Assembly(string name, string outputPath, string[] sourceFiles, string[] defines, Assembly[] assemblyReferences, string[] compiledAssemblyReferences, AssemblyFlags flags)
        {
            m_Name = name;
            m_OutputPath = outputPath;
            m_SourceFiles = sourceFiles ?? Array.Empty<string>();
            m_Defines = defines ?? Array.Empty<string>();
            m_AssemblyReferences = assemblyReferences ?? Array.Empty<Assembly>();
            m_CompiledAssemblyReferences = compiledAssemblyReferences ?? Array.Empty<string>();
            m_Flags = flags;
        }

        public string name => m_Name;

        public string rootNamespace => m_RootNamespace;

        public string outputPath => m_OutputPath;

        public string[] sourceFiles => m_SourceFiles;

        public string[] defines => m_Defines;

        public Assembly[] assemblyReferences
        {
            get => m_AssemblyReferences;
            internal set => m_AssemblyReferences = value ?? Array.Empty<Assembly>();
        }

        public string[] compiledAssemblyReferences => m_CompiledAssemblyReferences;

        public AssemblyFlags flags => m_Flags;

        /// <summary>The output paths of the referenced script assemblies, then the precompiled references.</summary>
        public string[] allReferences => m_AssemblyReferences.Select(a => a.outputPath).Concat(m_CompiledAssemblyReferences).ToArray();
    }

    /// <summary>
    /// What the Editor knows about the project's script assemblies, answered from the assembly
    /// graph Unbound compiled: every assembly in Play mode (editor and runtime), their sources,
    /// defines and references, and the asmdefs that declare them.
    /// </summary>
    public static partial class CompilationPipeline
    {
        static Assembly[] s_Assemblies;
        static int s_Generation = -1;

        static IEnumerable<GameAssembly> ScriptAssemblies() => GameDomain.Descriptions.Where(d => d.PluginGuid == null);

        /// <summary>The assemblies with their references linked, built once per code load.</summary>
        static Assembly[] All()
        {
            if (s_Assemblies != null && s_Generation == GameDomain.Generation) return s_Assemblies;
            var descriptions = ScriptAssemblies().ToList();
            var byName = new Dictionary<string, Assembly>(StringComparer.Ordinal);
            var built = new List<Assembly>();
            // References point at assemblies built before them: build in dependency order.
            var pending = new Dictionary<string, GameAssembly>(StringComparer.Ordinal);
            foreach (var d in descriptions) pending[d.Name] = d;
            Assembly Build(GameAssembly d, HashSet<string> visiting)
            {
                if (byName.TryGetValue(d.Name, out var done)) return done;
                visiting.Add(d.Name);
                var references = new List<Assembly>();
                foreach (var r in d.References)
                    if (!visiting.Contains(r) && pending.TryGetValue(r, out var target)) references.Add(Build(target, visiting));
                visiting.Remove(d.Name);
                var assembly = new Assembly(d.Name, $"Library/ScriptAssemblies/{d.Name}.dll", d.Sources, d.Defines, references.ToArray(), d.CompiledReferences,
                    d.Editor ? AssemblyFlags.EditorAssembly : AssemblyFlags.None)
                {
                    m_RootNamespace = d.RootNamespace ?? "",
                };
                byName[d.Name] = assembly;
                built.Add(assembly);
                return assembly;
            }
            foreach (var d in descriptions) Build(d, new HashSet<string>(StringComparer.Ordinal));
            s_Assemblies = descriptions.Select(d => byName[d.Name]).ToArray();
            s_Generation = GameDomain.Generation;
            return s_Assemblies;
        }

        public static Assembly[] GetAssemblies() => GetAssemblies(AssembliesType.Editor);

        public static Assembly[] GetAssemblies(AssembliesType assembliesType)
        {
            var all = All();
            switch (assembliesType)
            {
                case AssembliesType.Player:
                    return all.Where(a => (a.flags & AssemblyFlags.EditorAssembly) == 0).ToArray();
                case AssembliesType.PlayerWithoutTestAssemblies:
                    return all.Where(a => (a.flags & AssemblyFlags.EditorAssembly) == 0 && !IsTestAssembly(a)).ToArray();
                default:
                    return (Assembly[])all.Clone();
            }
        }

        static bool IsTestAssembly(Assembly a) =>
            a.defines.Contains("UNITY_INCLUDE_TESTS") && a.compiledAssemblyReferences.Any(r => r.EndsWith("nunit.framework.dll", StringComparison.OrdinalIgnoreCase))
            || a.assemblyReferences.Any(r => r.name == "UnityEngine.TestRunner");

        static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            path = path.Replace('\\', '/');
            var root = UnityEngine.ProjectInfo.ProjectPath?.Replace('\\', '/').TrimEnd('/');
            if (!string.IsNullOrEmpty(root) && path.StartsWith(root + "/", StringComparison.Ordinal)) path = path.Substring(root.Length + 1);
            return path;
        }

        static GameAssembly OfScript(string sourceFilePath)
        {
            var path = Normalize(sourceFilePath);
            foreach (var d in ScriptAssemblies())
                foreach (var s in d.Sources)
                    if (string.Equals(s, path, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        static GameAssembly Named(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return null;
            if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
            return ScriptAssemblies().FirstOrDefault(d => d.Name == assemblyName);
        }

        /// <summary>The file name of the assembly a script compiles into ("Assembly-CSharp.dll").</summary>
        public static string GetAssemblyNameFromScriptPath(string sourceFilePath)
        {
            var d = OfScript(sourceFilePath);
            return d != null ? d.Name + ".dll" : null;
        }

        public static string GetAssemblyDefinitionFilePathFromScriptPath(string sourceFilePath)
        {
            var d = OfScript(sourceFilePath);
            return d != null && d.Asmdef.Length > 0 ? d.Asmdef : null;
        }

        public static string GetAssemblyDefinitionFilePathFromAssemblyName(string assemblyName)
        {
            var d = Named(assemblyName);
            return d != null && d.Asmdef.Length > 0 ? d.Asmdef : null;
        }

        public static string GetAssemblyRootNamespaceFromScriptPath(string sourceFilePath)
        {
            var d = OfScript(sourceFilePath);
            return d?.RootNamespace ?? "";
        }

        public static string[] GetDefinesFromAssemblyName(string assemblyName) => Named(assemblyName)?.Defines.ToArray();

        public static string[] GetPrecompiledAssemblyNames() =>
            GameDomain.Descriptions.Where(d => d.PluginGuid != null).Select(d => d.Name + ".dll").ToArray();

        public static string GetPrecompiledAssemblyPathFromAssemblyName(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return null;
            if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
            var d = GameDomain.Descriptions.FirstOrDefault(p => p.PluginGuid != null && p.Name == assemblyName);
            if (d == null) return null;
            var path = AssetDatabase.GUIDToAssetPath(d.PluginGuid);
            return string.IsNullOrEmpty(path) ? d.Dll : path;
        }

        /// <summary>Whether defines satisfy an asmdef's define constraints: every constraint holds,
        /// a constraint being alternatives joined by "||", each a define or a "!define".</summary>
        public static bool IsDefineConstraintsCompatible(string[] defines, string[] defineConstraints)
        {
            if (defineConstraints == null || defineConstraints.Length == 0) return true;
            var set = new HashSet<string>(defines ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var constraint in defineConstraints)
            {
                if (string.IsNullOrWhiteSpace(constraint)) continue;
                bool any = false;
                foreach (var raw in constraint.Split(new[] { "||" }, StringSplitOptions.None))
                {
                    var alternative = raw.Trim();
                    if (alternative.Length == 0) continue;
                    bool negated = alternative[0] == '!';
                    var name = negated ? alternative.Substring(1).Trim() : alternative;
                    if (set.Contains(name) != negated)
                    {
                        any = true;
                        break;
                    }
                }
                if (!any) return false;
            }
            return true;
        }

        /// <summary>The game's code reloads on its own when sources change (hot reload); a request
        /// for a recompilation has nothing more to start.</summary>
        public static void RequestScriptCompilation() { }
    }
}
