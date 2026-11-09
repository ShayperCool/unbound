namespace DotRecast.Detour
{
    /// Options for dtNavMeshQuery::raycast
    internal static class DtRaycastOptions
    {
        public const int DT_RAYCAST_USE_COSTS = 0x01; //< Raycast should calculate movement cost along the ray and fill RaycastHit::cost

        /// Unbound: the cost of the straight ray in RaycastHit::pathCost, as Unity's NavMesh raycast
        /// weighs it: each polygon's part of the ray, up to where it stops, times its cost per metre.
        public const int UNBOUND_RAYCAST_STRAIGHT_COSTS = 0x02;
    }
}