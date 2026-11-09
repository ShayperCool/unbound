namespace DotRecast.Detour.Crowd
{
    /// Crowd agent update flags.
    /// @ingroup crowd
    /// @see dtCrowdAgentParams::updateFlags
    internal static class DtCrowdAgentUpdateFlags
    {
        public const int DT_CROWD_ANTICIPATE_TURNS = 1;
        public const int DT_CROWD_OBSTACLE_AVOIDANCE = 2;
        public const int DT_CROWD_SEPARATION = 4;
        // Unbound: no flags to optimize the path; Unity's agents always do (DtCrowd.FindCorners, WaitForTopology).
        public const int UNBOUND_NO_BRAKING = 256; //< Unbound: the agent does not slow down at the end of its path.
    }
}