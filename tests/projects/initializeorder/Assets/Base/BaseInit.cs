using UnityEditor;

[InitializeOnLoad]
public static class BaseInit
{
    static BaseInit() => Probe.InitOrder.PackageInit.Log("base.ctor");
}
