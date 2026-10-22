namespace UnityEngine
{
    // Placeholder for UnityEngine.Time.deltaTime until the player loop owns Time. The overloads of
    // SmoothDamp without a deltaTime argument read it, as Unity's read Time.deltaTime.
    // TODO(core): replace the uses with Time.deltaTime once Time exists, then delete this shim.
    internal static partial class TimeShim
    {
        internal static float deltaTime;
    }
}
