namespace Probe.ExplicitNew
{
    /// <summary>Names its plugin by file only (precompiledReferences: ProbeAliasNew.dll).</summary>
    public static class ExplicitReferences
    {
        public static string Alias() => ProbeAlias.Marker.Built + "/" + ProbeAlias.Marker.Runtime();
    }
}
