using UnityEditor;

/// <summary>An initializer in an assembly no other probe assembly uses.</summary>
[InitializeOnLoad]
public static class ZyAssetsUnusedInit
{
    static ZyAssetsUnusedInit() => Probe.InitOrder.PackageInit.Log("zyAssets.ctor");
}
