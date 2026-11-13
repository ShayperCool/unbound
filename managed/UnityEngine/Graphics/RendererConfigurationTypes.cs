namespace UnityEngine.Experimental.Rendering
{
    public enum RayTracingMode
    {
        Off = 0,
        Static = 1,
        DynamicTransform = 2,
        DynamicGeometry = 3,
    }
}
namespace UnityEngine.Rendering
{
    [global::System.Flags]
    public enum RayTracingAccelerationStructureBuildFlags
    {
        None = 0,
        PreferFastTrace = 1,
        PreferFastBuild = 2,
        MinimizeMemory = 4,
    }
    public enum LightUnit
    {
        Lumen = 0,
        Candela = 1,
        Lux = 2,
        Nits = 3,
        Ev100 = 4,
    }
}
namespace UnityEngine
{
    [global::System.Obsolete("LightmappingMode has been deprecated. Use LightmapBakeType instead (UnityUpgradable) -> LightmapBakeType", true)]
    public enum LightmappingMode
    {
        [global::System.Obsolete("LightmappingMode.Realtime has been deprecated. Use LightmapBakeType.Realtime instead (UnityUpgradable) -> LightmapBakeType.Realtime", true)] Realtime = 4,
        [global::System.Obsolete("LightmappingMode.Baked has been deprecated. Use LightmapBakeType.Baked instead (UnityUpgradable) -> LightmapBakeType.Baked", true)] Baked = 2,
        [global::System.Obsolete("LightmappingMode.Mixed has been deprecated. Use LightmapBakeType.Mixed instead (UnityUpgradable) -> LightmapBakeType.Mixed", true)] Mixed = 1,
    }
    public enum StereoTargetEyeMask
    {
        None = 0,
        Left = 1,
        Right = 2,
        Both = 3,
    }
}
