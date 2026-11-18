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
        string m_Name;
        string m_OutputPath;
        string[] m_SourceFiles;
        string[] m_Defines;
        Assembly[] m_AssemblyReferences;
        string[] m_CompiledAssemblyReferences;
        AssemblyFlags m_Flags;
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

        public string name { get => m_Name; private set => m_Name = value; }

        public string rootNamespace { get => m_RootNamespace; private set => m_RootNamespace = value; }

        public string outputPath { get => m_OutputPath; private set => m_OutputPath = value; }

        public string[] sourceFiles { get => m_SourceFiles; private set => m_SourceFiles = value; }

        public string[] defines { get => m_Defines; private set => m_Defines = value; }

        public Assembly[] assemblyReferences
        {
            get => m_AssemblyReferences;
            internal set => m_AssemblyReferences = value ?? Array.Empty<Assembly>();
        }

        public string[] compiledAssemblyReferences { get => m_CompiledAssemblyReferences; private set => m_CompiledAssemblyReferences = value; }

        public AssemblyFlags flags { get => m_Flags; private set => m_Flags = value; }

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

        public static event Action<object> compilationStarted;
        public static event Action<object> compilationFinished;
        [Obsolete("Use compilationStarted, compilationFinished or assemblyCompilationFinished instead. Note that using any of these functions to do time measurements is a bad idea as they run async to actual compilation.")]
        public static event Action<string> assemblyCompilationStarted;
        public static event Action<string> assemblyCompilationNotRequired;
        public static event Action<string, CompilerMessage[]> assemblyCompilationFinished;

        /// <summary>An assembly of a recompilation: compiled (with the compiler's messages) or not
        /// required.</summary>
        internal readonly struct CompiledAssembly
        {
            internal readonly string Name;
            internal readonly bool Compiled;
            internal readonly CompilerMessage[] Messages;

            internal CompiledAssembly(string name, bool compiled, CompilerMessage[] messages)
            {
                Name = name;
                Compiled = compiled;
                Messages = messages ?? Array.Empty<CompilerMessage>();
            }
        }

        /// <summary>
        /// A recompilation of the scripts, as the Editor reports one (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/compileevents): compilationStarted with the build's context, each
        /// compiled assembly's started and finished events (output path, its errors and warnings),
        /// assemblyCompilationNotRequired for each assembly that did not need compiling, then
        /// compilationFinished with the same context. Unbound raises it when its hot reload
        /// recompiles; it patches the code in place, so no assembly reload follows (the Editor
        /// reloads the domain after a successful one).
        /// </summary>
        internal static void RaiseCompilation(IReadOnlyList<CompiledAssembly> assemblies)
        {
            var context = new Bee.BeeDriver.ActiveBuild();
            Raise(compilationStarted, context);
            foreach (var assembly in assemblies)
            {
                string path = $"Library/ScriptAssemblies/{assembly.Name}.dll";
                if (!assembly.Compiled)
                {
                    Raise(assemblyCompilationNotRequired, path);
                    continue;
                }
#pragma warning disable 618
                Raise(assemblyCompilationStarted, path);
#pragma warning restore 618
                if (assemblyCompilationFinished != null)
                    foreach (Action<string, CompilerMessage[]> callback in assemblyCompilationFinished.GetInvocationList())
                    {
                        try { callback(path, assembly.Messages); }
                        catch (Exception e) { UnityEngine.Debug.LogException(e); }
                    }
            }
            Raise(compilationFinished, context);
        }

        static void Raise<T>(Action<T> callbacks, T argument)
        {
            if (callbacks == null) return;
            foreach (Action<T> callback in callbacks.GetInvocationList())
            {
                try { callback(argument); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }

        /// <summary>A compiler diagnostic as the Editor reports it: "file(line,column): error|warning
        /// id: text", the file as the project names it.</summary>
        internal static CompilerMessage Message(bool error, string id, string text, string file, int line, int column)
        {
            string projectFile = string.IsNullOrEmpty(file) ? "" : Unbound.Runtime.UnityStackTrace.SourcePath(file, UnityEngine.ProjectInfo.ProjectPath);
            string kind = error ? "error" : "warning";
            string where = projectFile.Length == 0 ? "" : $"{projectFile}({line},{column}): ";
            return new CompilerMessage
            {
                message = $"{where}{kind} {id}: {text}",
                file = projectFile,
                line = line,
                column = column,
                type = error ? CompilerMessageType.Error : CompilerMessageType.Warning,
            };
        }

        /// <summary>The game's code reloads on its own when sources change (hot reload); a forced
        /// recompilation (with its domain reload and compilation events) is not started.</summary>
        public static void RequestScriptCompilation() =>
            Stubs.Ignored("CompilationPipeline.RequestScriptCompilation", "does not recompile (scripts reload when their sources change)");
    }

    /// <summary>A compiler diagnostic of an assembly's compilation.</summary>
    public partial struct CompilerMessage
    {
        public string message;
        public string file;
        public int line;
        public int column;
        public CompilerMessageType type;
    }

    public enum CompilerMessageType
    {
        Error = 0,
        Warning = 1,
        Info = 2,
    }
}

namespace Bee.BeeDriver
{
    /// <summary>The build a compilation's events pass as their context (the Editor's build object's
    /// type name; scripts only compare or print it).</summary>
    internal sealed class ActiveBuild
    {
    }
}
