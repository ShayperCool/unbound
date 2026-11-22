using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;

namespace UnityEditor
{
    public enum ApiCompatibilityLevel
    {
        NET_2_0 = 1,
        NET_2_0_Subset = 2,
        NET_4_6 = 3,
        NET_Web = 4,
        NET_Micro = 5,
        NET_Standard_2_0 = 6,
        NET_Standard = 6,
        NET_Unity_4_8 = 3,
    }

    public enum EditorAssembliesCompatibilityLevel
    {
        Default = 1,
        NET_Unity_4_8 = 2,
        NET_Standard = 3,
    }
}

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

    /// <summary>A script assembly as the Editor's compilation pipeline describes it. The
    /// constructors keep what they are given, null arrays too; one without compiler options gets
    /// default ones (Unity 6000.0.71f1, tests/projects/gameinterop compilation.ctor_*).</summary>
    public partial class Assembly
    {
        string m_Name;
        string m_OutputPath;
        string[] m_SourceFiles;
        string[] m_Defines;
        Assembly[] m_AssemblyReferences;
        string[] m_CompiledAssemblyReferences;
        AssemblyFlags m_Flags;
        ScriptCompilerOptions m_CompilerOptions;
        internal string m_RootNamespace = "";

        public Assembly(string name, string outputPath, string[] sourceFiles, string[] defines, Assembly[] assemblyReferences, string[] compiledAssemblyReferences, AssemblyFlags flags)
            : this(name, outputPath, sourceFiles, defines, assemblyReferences, compiledAssemblyReferences, flags, new ScriptCompilerOptions(), "") { }

        public Assembly(string name, string outputPath, string[] sourceFiles, string[] defines, Assembly[] assemblyReferences, string[] compiledAssemblyReferences, AssemblyFlags flags, ScriptCompilerOptions compilerOptions)
            : this(name, outputPath, sourceFiles, defines, assemblyReferences, compiledAssemblyReferences, flags, compilerOptions, "") { }

        public Assembly(string name, string outputPath, string[] sourceFiles, string[] defines, Assembly[] assemblyReferences, string[] compiledAssemblyReferences, AssemblyFlags flags, ScriptCompilerOptions compilerOptions, string rootNamespace)
        {
            m_Name = name;
            m_OutputPath = outputPath;
            m_SourceFiles = sourceFiles;
            m_Defines = defines;
            m_AssemblyReferences = assemblyReferences;
            m_CompiledAssemblyReferences = compiledAssemblyReferences;
            m_Flags = flags;
            m_CompilerOptions = compilerOptions;
            m_RootNamespace = rootNamespace;
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

        public ScriptCompilerOptions compilerOptions { get => m_CompilerOptions; internal set => m_CompilerOptions = value; }

        /// <summary>The output paths of the referenced script assemblies, then the precompiled references.</summary>
        public string[] allReferences =>
            (m_AssemblyReferences ?? Array.Empty<Assembly>()).Select(a => a.outputPath).Concat(m_CompiledAssemblyReferences ?? Array.Empty<string>()).ToArray();
    }

    /// <summary>A response file's options (<see cref="CompilationPipeline.ParseResponseFile"/>).</summary>
    public partial class ResponseFileData
    {
        public string[] Defines;
        public string[] FullPathReferences;
        public string[] Errors;
        public string[] OtherArguments;
        public bool Unsafe;
    }

    /// <summary>The compiler settings of an assembly. A new instance has the defaults of Unity
    /// 6000.0.71f1 (tests/projects/gameinterop compilation.options_new).</summary>
    public partial class ScriptCompilerOptions
    {
        string[] m_ResponseFiles = Array.Empty<string>();

        public ScriptCompilerOptions()
        {
            ApiCompatibilityLevel = ApiCompatibilityLevel.NET_Unity_4_8;
            CodeOptimization = CodeOptimization.None;
            EditorAssembliesCompatibilityLevel = EditorAssembliesCompatibilityLevel.Default;
            RoslynAnalyzerDllPaths = Array.Empty<string>();
            RoslynAdditionalFilePaths = Array.Empty<string>();
            // The release's compiler language version (the compatibility profile's).
            LanguageVersion = CompilationPipeline.s_Compiler.LanguageVersion;
            UseDeterministicCompilation = true;
        }

        internal ScriptCompilerOptions(ScriptCompilerOptions scriptCompilerOptions)
        {
            AllowUnsafeCode = scriptCompilerOptions.AllowUnsafeCode;
            ApiCompatibilityLevel = scriptCompilerOptions.ApiCompatibilityLevel;
            CodeOptimization = scriptCompilerOptions.CodeOptimization;
            EditorAssembliesCompatibilityLevel = scriptCompilerOptions.EditorAssembliesCompatibilityLevel;
            RoslynAnalyzerDllPaths = scriptCompilerOptions.RoslynAnalyzerDllPaths;
            RoslynAnalyzerRulesetPath = scriptCompilerOptions.RoslynAnalyzerRulesetPath;
            RoslynAdditionalFilePaths = scriptCompilerOptions.RoslynAdditionalFilePaths;
            AnalyzerConfigPath = scriptCompilerOptions.AnalyzerConfigPath;
            AdditionalCompilerArguments = scriptCompilerOptions.AdditionalCompilerArguments;
            m_ResponseFiles = scriptCompilerOptions.m_ResponseFiles;
            LanguageVersion = scriptCompilerOptions.LanguageVersion;
            UseDeterministicCompilation = scriptCompilerOptions.UseDeterministicCompilation;
        }

        public string RoslynAnalyzerRulesetPath { get; set; }
        public string[] RoslynAnalyzerDllPaths { get; set; }
        public string[] RoslynAdditionalFilePaths { get; set; }
        public string AnalyzerConfigPath { get; set; }
        public bool AllowUnsafeCode { get; set; }
        /// <summary>Removed: reading it throws (Unity 6000.0.71f1, tests/projects/defaultstate).</summary>
        [Obsolete("Use of reference assemblies is always enabled", true)]
        public bool EmitReferenceAssembly { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        internal bool UseDeterministicCompilation { get; set; }
        public string[] AdditionalCompilerArguments { get; set; }
        public CodeOptimization CodeOptimization { get; set; }
        public ApiCompatibilityLevel ApiCompatibilityLevel { get; set; }
        public EditorAssembliesCompatibilityLevel EditorAssembliesCompatibilityLevel { get; set; }
        public string[] ResponseFiles { get => m_ResponseFiles; set => m_ResponseFiles = value ?? Array.Empty<string>(); }
        public string LanguageVersion { get; internal set; }
    }

    /// <summary>
    /// What the Editor knows about the project's script assemblies, answered from the assembly
    /// graph Unbound compiled: every assembly in Play mode (editor and runtime), their sources,
    /// defines and references, and the asmdefs that declare them.
    /// </summary>
    public static partial class CompilationPipeline
    {
        static Assembly[] s_Assemblies, s_PlayerAssemblies;
        static int s_Generation = -1;
        static int s_DefinesVersion = -1;

        /// <summary>The release's compiler language version, compiler arguments and analyzers (the
        /// compatibility profile's), and whether the project's API compatibility level for its
        /// target is .NET Standard. The game's configuration always sets it; the initial value
        /// serves managed tests only.</summary>
        internal static (string LanguageVersion, string[] Arguments, string[] Analyzers, bool NetStandard) s_Compiler = ("9.0", Array.Empty<string>(), Array.Empty<string>(), true);

        /// <summary>An assembly's compiler options as the pipeline reports them: its unsafe-code
        /// flag, the project's API compatibility level (Editor assemblies too), the release's
        /// language version, arguments and analyzers, and the optimization it builds with (Unity 6000.0.71f1, tests/projects/gameinterop
        /// compilation.*.options).</summary>
        static ScriptCompilerOptions OptionsOf(GameAssembly d, CodeOptimization optimization) => new ScriptCompilerOptions
        {
            AllowUnsafeCode = d.AllowUnsafe,
            ApiCompatibilityLevel = s_Compiler.NetStandard ? ApiCompatibilityLevel.NET_Standard : ApiCompatibilityLevel.NET_Unity_4_8,
            CodeOptimization = optimization,
            LanguageVersion = s_Compiler.LanguageVersion,
            AdditionalCompilerArguments = (string[])s_Compiler.Arguments.Clone(),
            RoslynAnalyzerDllPaths = (string[])s_Compiler.Analyzers.Clone(),
            RoslynAnalyzerRulesetPath = "",
            AnalyzerConfigPath = "",
            // The response file the assembly compiled with, by its full path.
            ResponseFiles = string.IsNullOrEmpty(d.ResponseFile)
                ? Array.Empty<string>()
                : new[] { UnityEngine.ProjectInfo.ProjectPath.Replace('\\', '/').TrimEnd('/') + "/" + d.ResponseFile },
        };

        static IEnumerable<GameAssembly> ScriptAssemblies() => GameDomain.Descriptions.Where(d => d.PluginGuid == null);

        /// <summary>The assemblies with their references linked, built once per code load: as the
        /// Editor builds them (its code optimization), and as a player build would (release).</summary>
        static Assembly[] All(bool player = false)
        {
            if (s_Generation != GameDomain.Generation || s_DefinesVersion != UnityEditor.Build.BuildDefines.Version)
            {
                s_DefinesVersion = UnityEditor.Build.BuildDefines.Version;
                s_Assemblies = BuildAll(s_CodeOptimization);
                s_PlayerAssemblies = BuildAll(CodeOptimization.Release);
                s_Generation = GameDomain.Generation;
            }
            return player ? s_PlayerAssemblies : s_Assemblies;
        }

        static Assembly[] BuildAll(CodeOptimization optimization)
        {
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
                var assembly = new Assembly(d.Name, $"Library/ScriptAssemblies/{d.Name}.dll", d.Sources, WithDelegateDefines(d.Defines), references.ToArray(), d.CompiledReferences,
                    d.Editor ? AssemblyFlags.EditorAssembly : AssemblyFlags.None, OptionsOf(d, optimization), d.RootNamespace ?? "");
                byName[d.Name] = assembly;
                built.Add(assembly);
                return assembly;
            }
            foreach (var d in descriptions) Build(d, new HashSet<string>(StringComparer.Ordinal));
            return descriptions.Select(d => byName[d.Name]).ToArray();
        }

        public static Assembly[] GetAssemblies() => GetAssemblies(AssembliesType.Editor);

        public static Assembly[] GetAssemblies(AssembliesType assembliesType)
        {
            var all = All(player: assembliesType != AssembliesType.Editor);
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

        /// <summary>An assembly definition's defines, with those that BuildDefines delegates add; null
        /// for a predefined assembly or an unknown name (Unity 6000.0.71f1, tests/projects/gameinterop
        /// builddefines.pipeline, rsp.defines_from_name).</summary>
        public static string[] GetDefinesFromAssemblyName(string assemblyName)
        {
            var d = Named(assemblyName);
            return d == null || d.Asmdef.Length == 0 ? null : WithDelegateDefines(d.Defines);
        }

        /// <summary>The defines an assembly definition's response file gives it (its own, else the
        /// project's), or null for a predefined assembly or an unknown name.</summary>
        public static string[] GetResponseFileDefinesFromAssemblyName(string assemblyName)
        {
            var d = Named(assemblyName);
            return d == null || d.Asmdef.Length == 0 ? null : d.ResponseFileDefines;
        }

        /// <summary>Defines with those the BuildDefines delegates add for the active target.</summary>
        static string[] WithDelegateDefines(string[] defines) =>
            UnityEditor.Build.BuildDefines.HasDelegates
                ? UnityEditor.Build.BuildDefines.GetScriptCompilationDefines(EditorUserBuildSettings.activeBuildTarget, defines)
                : (string[])defines.Clone();

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

        /// <summary>A response file's options as the Editor reads them (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/gameinterop rsp.parse.*): arguments separated by spaces, quoted
        /// parts kept whole, <c>#</c> ending a line; <c>-define</c>/<c>-d</c> defines, <c>-r</c>/<c>-reference</c>
        /// paths resolved against the system directories then the project directory (which wins),
        /// <c>-unsafe</c>, and every other argument with a <c>/</c> prefix. Unbound's compile reads
        /// project response files in the same way (crates/unbound-project/src/response_files.rs).</summary>
        public static ResponseFileData ParseResponseFile(string relativePath, string projectDirectory, string[] systemReferenceDirectories)
        {
            var data = new ResponseFileData { Defines = Array.Empty<string>(), FullPathReferences = Array.Empty<string>(), Errors = Array.Empty<string>(), OtherArguments = Array.Empty<string>() };
            var path = System.IO.Path.IsPathRooted(relativePath) ? relativePath : System.IO.Path.Combine(projectDirectory ?? "", relativePath);
            path = path.Replace('\\', '/');
            if (!System.IO.File.Exists(path)) return data;
            var defines = new List<string>();
            var references = new List<string>();
            var errors = new List<string>();
            var other = new List<string>();
            var separators = new[] { ';', ',' };
            foreach (var token in ResponseFileTokens(System.IO.File.ReadAllText(path)))
            {
                int colon = token.IndexOf(':');
                string arg = colon < 0 ? token : token.Substring(0, colon), value = colon < 0 ? "" : token.Substring(colon + 1);
                if (arg.Length > 0 && arg[0] == '-') arg = "/" + arg.Substring(1);
                if (arg == "/d" || arg == "/define") defines.AddRange(value.Split(separators));
                else if (arg == "/r" || arg == "/reference")
                {
                    var parts = value.Split(separators);
                    if (value.Length == 0) errors.Add("No value set for reference");
                    else if (parts.Length != 1) errors.Add("Cannot specify multiple aliases using single /reference option");
                    else if (parts[0].Length > 0)
                    {
                        var reference = parts[0].Substring(parts[0].IndexOf('=') + 1);
                        var full = ResolveReference(reference, projectDirectory, systemReferenceDirectories);
                        if (full != null) references.Add(full.Replace('\\', '/'));
                        else errors.Add($"{path}: not parsed correctly: {reference} could not be found as a system library.\n" +
                            "If this was meant as a user reference please provide the relative path from project root (parent of the Assets folder) in the response file.");
                    }
                }
                else if (arg == "/unsafe" || arg == "/unsafe+") data.Unsafe = true;
                else other.Add(arg + (value.Length == 0 ? "" : ":" + (value.Any(char.IsWhiteSpace) ? "\"" + value + "\"" : value)));
            }
            data.Defines = defines.ToArray();
            data.FullPathReferences = references.ToArray();
            data.Errors = errors.ToArray();
            data.OtherArguments = other.ToArray();
            return data;
        }

        static string ResolveReference(string reference, string projectDirectory, string[] systemReferenceDirectories)
        {
            if (System.IO.Path.IsPathRooted(reference)) return reference;
            var user = System.IO.Path.Combine(projectDirectory ?? "", reference);
            if (System.IO.File.Exists(user)) return user;
            foreach (var directory in systemReferenceDirectories ?? Array.Empty<string>())
            {
                var system = System.IO.Path.Combine(directory, reference);
                if (System.IO.File.Exists(system)) return system;
            }
            return null;
        }

        static List<string> ResponseFileTokens(string text)
        {
            var tokens = new List<string>();
            var current = new System.Text.StringBuilder();
            foreach (var line in text.Split('\n', '\r'))
            {
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '"' || c == '\'')
                    {
                        for (i++; i < line.Length && line[i] != c; i++) current.Append(line[i]);
                    }
                    else if (c == ' ')
                    {
                        if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                    }
                    else if (c == '#') break;
                    else current.Append(c);
                }
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            return tokens;
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
            /// <summary>The defines it was compiled with; null when not known.</summary>
            internal readonly string[] Defines;

            internal CompiledAssembly(string name, bool compiled, CompilerMessage[] messages, string[] defines = null)
            {
                Name = name;
                Compiled = compiled;
                Messages = messages ?? Array.Empty<CompilerMessage>();
                Defines = defines;
            }
        }

        /// <summary>
        /// A recompilation of the scripts, as the Editor reports one (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/compileevents): compilationStarted with the build's context, each
        /// compiled assembly's started and finished events (output path, its errors and warnings)
        /// followed by the Editor compilation's own finished event of it (its HasCompileErrors set
        /// when any assembly of the compilation failed),
        /// assemblyCompilationNotRequired for each assembly that did not need compiling, then
        /// compilationFinished with the same context. Unbound raises it when its hot reload
        /// recompiles; an edit it patches in place reloads no assembly (the Editor reloads the
        /// domain after a successful one); a requested compilation reloads them.
        /// </summary>
        internal static void RaiseCompilation(IReadOnlyList<CompiledAssembly> assemblies)
        {
            var context = new Bee.BeeDriver.ActiveBuild();
            bool failed = false;
            foreach (var assembly in assemblies)
                if (assembly.Compiled && Array.Exists(assembly.Messages, m => m.type == CompilerMessageType.Error)) failed = true;
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
                UnityEditor.Scripting.ScriptCompilation.EditorCompilationInterface.Instance.RaiseAssemblyCompilationFinished(assembly.Name, assembly.Defines, failed, assembly.Messages);
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

        /// <summary>Asks for a compilation of the scripts, run between frames: its compilation
        /// events, every assembly not required when nothing changed, and then a reload of the
        /// scripts whatever changed (Unbound restarts the session), as the Unity 6000.0.71f1 Editor
        /// does (tests/projects/compileevents).</summary>
        public static void RequestScriptCompilation() => RequestScriptCompilation(RequestScriptCompilationOptions.None);

        /// <summary><see cref="RequestScriptCompilation()"/>; with CleanBuildCache the Editor reports
        /// the same events (every assembly not required when nothing changed), and so does Unbound,
        /// whose compile cache is keyed by its inputs.</summary>
        public static void RequestScriptCompilation(RequestScriptCompilationOptions options) =>
            Unbound.Runtime.Native.Call("request-script-compilation", "{}");
    }

    [global::System.Flags]
    public enum RequestScriptCompilationOptions
    {
        None = 0,
        CleanBuildCache = 1,
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

namespace UnityEditor.Scripting.Compilers
{
    /// <summary>The response file names the Editor reads: <c>csc.rsp</c>, and at the project root the
    /// obsolete <c>mcs.rsp</c> (Unity 6000.0.71f1, tests/projects/gameinterop rsp.obsolete_names;
    /// crates/unbound-project/src/response_files.rs reads them).</summary>
    internal partial class CompilerSpecificResponseFiles
    {
        public static string[] MicrosoftCSharpCompilerObsolete = { "mcs.rsp" };
    }
}

namespace UnityEditor.Build
{
    internal delegate void GetScriptCompilationDefinesDelegate(BuildTarget target, System.Collections.Generic.HashSet<string> defines);

    /// <summary>The script defines of a build target, with those editor code adds through
    /// getScriptCompilationDefinesDelegates. As in the Editor, the defines a delegate adds reach what
    /// the compilation pipeline reports (assemblies, EditorUserBuildSettings) but not the code
    /// already compiled, which was built before the delegate registered (Unity 6000.0.71f1,
    /// tests/projects/gameinterop builddefines.*).</summary>
    internal partial class BuildDefines
    {
        static GetScriptCompilationDefinesDelegate s_Delegates;
        /// <summary>Changes whenever a delegate is added or removed.</summary>
        internal static int Version;

        internal static bool HasDelegates => s_Delegates != null;

        public static event GetScriptCompilationDefinesDelegate getScriptCompilationDefinesDelegates
        {
            add { s_Delegates += value; Version++; }
            remove { s_Delegates -= value; Version++; }
        }

        /// <summary>The given defines and those each delegate adds for the target.</summary>
        public static string[] GetScriptCompilationDefines(BuildTarget target, string[] defines)
        {
            var set = new System.Collections.Generic.HashSet<string>(defines ?? System.Array.Empty<string>());
            if (s_Delegates != null)
                foreach (GetScriptCompilationDefinesDelegate call in s_Delegates.GetInvocationList())
                {
                    try { call(target, set); }
                    catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
                }
            var result = new string[set.Count];
            set.CopyTo(result);
            return result;
        }
    }
}
