using UnityEditor;

/// <summary>An initializer in an assembly no other probe assembly uses.</summary>
[InitializeOnLoad]
public static class ZzUnusedInit
{
    static ZzUnusedInit() => Probe.InitOrder.PackageInit.Log("zzUnused.ctor");
}
