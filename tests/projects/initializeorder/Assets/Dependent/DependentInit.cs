using UnityEditor;

[InitializeOnLoad]
public static class DependentInit
{
    static DependentInit() => Probe.InitOrder.PackageInit.Log("dependent.ctor base=" + (typeof(BaseInit) != null));
}
