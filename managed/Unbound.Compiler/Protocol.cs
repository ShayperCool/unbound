using System.Collections.Generic;

namespace Unbound.Compiler;

/// <summary>What to compile: the assembly graph the Rust side derived, in dependency order.</summary>
public sealed class CompileRequest
{
    /// <summary>Content-addressed outputs: <c>&lt;CacheDir&gt;/&lt;assembly&gt;/&lt;key&gt;.dll</c>.</summary>
    public string CacheDir { get; set; } = "";
    public List<AssemblySpec> Assemblies { get; set; } = new();
    /// <summary>Class library references (.NET Framework 4.8 or .NET Standard 2.1 profile).</summary>
    public List<string> BclReferences { get; set; } = new();
    /// <summary>Unity engine (and Editor) reference assemblies.</summary>
    public List<string> EngineReferences { get; set; } = new();
    public string LanguageVersion { get; set; } = "9.0";
    /// <summary>Command-line style options the Editor passes: <c>/nowarn:0169</c>.</summary>
    public List<string> CompilerOptions { get; set; } = new();
    /// <summary>Roslyn analyzers and source generators (paths) applied to every assembly.</summary>
    public List<string> Analyzers { get; set; } = new();
    /// <summary>The project directory, which the Editor passes to source generators.</summary>
    public string ProjectPath { get; set; } = "";
}

public sealed class AssemblySpec
{
    public string Name { get; set; } = "";
    public List<string> Sources { get; set; } = new();
    public List<string> Defines { get; set; } = new();
    /// <summary>Script assemblies of this request, by name.</summary>
    public List<string> References { get; set; } = new();
    /// <summary>Precompiled plugin DLLs.</summary>
    public List<string> Plugins { get; set; } = new();
    public List<string> Analyzers { get; set; } = new();
    public bool AllowUnsafe { get; set; }
    public bool NoEngineReferences { get; set; }
}

public sealed class CompileResponse
{
    public List<AssemblyResult> Assemblies { get; set; } = new();
    public long Milliseconds { get; set; }
    public int Compiled { get; set; }
    public int Cached { get; set; }
    public int Failed { get; set; }
}

public sealed class AssemblyResult
{
    public string Name { get; set; } = "";
    /// <summary><c>compiled</c>, <c>unchanged</c> (warm and identical), <c>cached</c>, <c>failed</c>
    /// or <c>skipped</c> (a reference failed).</summary>
    public string Status { get; set; } = "";
    public string? Dll { get; set; }
    public string? Pdb { get; set; }
    /// <summary>Hash of the public surface: dependents recompile only when it changes.</summary>
    public string? InterfaceHash { get; set; }
    public List<DiagnosticInfo> Errors { get; set; } = new();
    public int Warnings { get; set; }
    public long Milliseconds { get; set; }
}

public sealed class DiagnosticInfo
{
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public string? File { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}
