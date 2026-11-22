using UnityEditor;

/// <summary>An initializer in an assembly no other probe assembly uses.</summary>
[InitializeOnLoad]
public static class AaUnusedInit
{
    static AaUnusedInit() => Probe.InitOrder.PackageInit.Log("aaUnused.ctor");
}
