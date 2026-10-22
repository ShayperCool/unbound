using System.Collections.Generic;

namespace Unbound.ApiTools;

/// <summary>The <c>facades</c> host method: generate (or reuse) a release's module facades.</summary>
public sealed class FacadesRequest
{
    /// <summary>The release's <c>Editor/Data/Managed/UnityEngine</c>.</summary>
    public string UnityManagedEngineDir { get; set; } = "";
    /// <summary>Per release: <c>&lt;cache&gt;/facades/&lt;unity-version&gt;</c>.</summary>
    public string OutputDir { get; set; } = "";
}

/// <summary>The <c>api-gaps</c> host method.</summary>
public sealed class ApiGapsRequest
{
    /// <summary>Compiled script assemblies and the precompiled plugins they reference.</summary>
    public List<string> GameAssemblies { get; set; } = new();
    /// <summary>Unbound's <c>UnityEngine.CoreModule.dll</c>.</summary>
    public string ImplAssembly { get; set; } = "";
    public string UnityManagedEngineDir { get; set; } = "";
}
