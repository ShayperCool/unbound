using System;
using System.IO;
using System.Linq;
using Unbound.Runtime;

namespace UnityEditor.Scripting.ScriptCompilation
{
    /// <summary>The Editor's script compilation state, as tools inside the Editor read it (the Test
    /// Framework finds test assemblies here, Burst its assemblies): Unbound's compiled assemblies.</summary>
    internal static partial class EditorCompilationInterface
    {
        static EditorCompilation s_Instance;

        public static EditorCompilation Instance => s_Instance ??= new EditorCompilation();

        public static EditorScriptCompilationOptions GetAdditionalEditorScriptCompilationOptions() =>
            EditorScriptCompilationOptions.BuildingIncludingTestAssemblies;

        public static bool IsCompilationPending() => false;

        public static bool IsCompilationInProgress() => false;
    }

    internal partial class EditorCompilation : IEditorCompilation
    {
        /// <summary>The script assemblies Unbound compiled for the Editor (all of them: Editor Play
        /// mode compiles editor and runtime assemblies).</summary>
        public ScriptAssembly[] GetAllEditorScriptAssemblies(EditorScriptCompilationOptions additionalOptions) =>
            GameDomain.Descriptions.Where(a => a.PluginGuid == null).Select(ToScriptAssembly).ToArray();

        public ScriptAssembly[] GetAllEditorScriptAssemblies(EditorScriptCompilationOptions additionalOptions, string[] defines) =>
            GetAllEditorScriptAssemblies(additionalOptions);

        public PrecompiledAssembly[] GetAllPrecompiledAssemblies() =>
            GameDomain.Descriptions.Where(a => a.PluginGuid != null).Select(a => new PrecompiledAssembly
            {
                Path = a.SourcePath ?? a.Dll,
                Flags = a.Editor ? AssemblyFlags.EditorOnly : AssemblyFlags.None,
            }).ToArray();

        static ScriptAssembly ToScriptAssembly(GameAssembly a)
        {
            var flags = AssemblyFlags.None;
            if (a.Editor) flags |= AssemblyFlags.EditorOnly;
            if (a.Asmdef.Length == 0) flags |= AssemblyFlags.UserAssembly;
            if (a.Name.EndsWith("-firstpass", StringComparison.Ordinal) || a.Name.EndsWith("-firstpass-Editor", StringComparison.Ordinal)) flags |= AssemblyFlags.FirstPass;
            return new ScriptAssembly
            {
                Filename = a.Name + ".dll",
                OriginPath = a.Asmdef.Length > 0 ? Path.GetDirectoryName(a.Asmdef)?.Replace('\\', '/') + "/" : "Assets/",
                Flags = flags,
                OutputDirectory = "Library/ScriptAssemblies",
                References = a.CompiledReferences,
                Defines = a.Defines,
                Files = a.Sources,
                RootNamespace = a.RootNamespace,
                AsmDefPath = a.Asmdef,
                ScriptAssemblyReferences = Array.Empty<ScriptAssembly>(),
            };
        }
    }

    internal partial class ScriptAssembly
    {
        public string OriginPath { get; set; }
        public AssemblyFlags Flags { get; set; }
        public string Filename { get; set; }
        public string OutputDirectory { get; set; }
        public ScriptAssembly[] ScriptAssemblyReferences { get; set; }
        public string[] References { get; set; }
        public string[] Defines { get; set; }
        public string[] Files { get; set; }
        public string RootNamespace { get; set; }
        public string AsmDefPath { get; set; }
        public string FullPath => OutputDirectory + "/" + Filename;
    }

    internal partial struct PrecompiledAssembly
    {
        public string Path;
        public AssemblyFlags Flags;
        public bool Redirected;
    }

    [Flags]
    internal enum AssemblyFlags
    {
        None = 0,
        EditorOnly = 1,
        UseForMono = 2,
        UseForDotNet = 4,
        FirstPass = 8,
        ExcludedForRuntimeCode = 0x10,
        UserAssembly = 0x20,
        ExplicitlyReferenced = 0x40,
        ExplicitReferences = 0x80,
        UnityModule = 0x100,
        NoEngineReferences = 0x200,
        SuppressCompilerWarnings = 0x400,
        ValidateAssembly = 0x2000,
    }

    [Flags]
    internal enum EditorScriptCompilationOptions
    {
        BuildingEmpty = 0,
        BuildingDevelopmentBuild = 1,
        BuildingForEditor = 2,
        BuildingEditorOnlyAssembly = 4,
        BuildingForIl2Cpp = 8,
        BuildingWithAsserts = 0x10,
        BuildingIncludingTestAssemblies = 0x20,
        BuildingPredefinedAssembliesAllowUnsafeCode = 0x40,
        BuildingForHeadlessPlayer = 0x80,
        BuildingUseDeterministicCompilation = 0x200,
        BuildingWithoutScriptUpdater = 0x800,
        BuildingExtractTypeDB = 0x1000,
        BuildingSkipCompile = 0x2000,
        BuildingCleanCompilation = 0x4000,
    }

    internal partial interface IEditorCompilation
    {
    }
}
