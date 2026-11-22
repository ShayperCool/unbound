using UnityEditor;

/// <summary>An initializer in an assembly no other probe assembly uses.</summary>
[InitializeOnLoad]
public static class AbPackageUnusedInit
{
    static AbPackageUnusedInit() => Probe.InitOrder.PackageInit.Log("abPackage.ctor");
}
