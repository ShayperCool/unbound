namespace Probe.Explicit
{
    /// <summary>Names its plugins by file only (precompiledReferences: ProbeAlias.dll and
    /// ProbeDup.dll).</summary>
    public static class ExplicitReferences
    {
        public static string Alias() => ProbeAlias.Marker.Built + "/" + ProbeAlias.Marker.Runtime();
    }
}
