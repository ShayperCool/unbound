namespace DotRecast.Detour.Crowd
{
    internal class DtCrowdAgentConfig
    {
        public int expandOptions = 1;
        public bool anticipateTurns = true;
        public bool obstacleAvoidance = true;
        public int obstacleAvoidanceType = 3;
        public bool separation;
        public float separationWeight = 2f;

        public int GetUpdateFlags()
        {
            int updateFlags = 0;
            if (anticipateTurns)
            {
                updateFlags |= DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS;
            }

            if (obstacleAvoidance)
            {
                updateFlags |= DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE;
            }

            if (separation)
            {
                updateFlags |= DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION;
            }

            return updateFlags;
        }
    }
}