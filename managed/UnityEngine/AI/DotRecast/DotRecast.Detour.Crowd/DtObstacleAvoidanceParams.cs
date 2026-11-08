namespace DotRecast.Detour.Crowd
{
    internal class DtObstacleAvoidanceParams
    {
        // Unbound: Unity's weights (tests/projects/avoidance, navlinks `fast`): candidates around 0.7
        // of the desired velocity, weighed 0.2 by their distance from it, 0.1 from the current
        // velocity and 2 by their time to impact; no side preference.
        public float velBias = 0.7f;
        public float weightDesVel = 0.2f;
        public float weightCurVel = 0.1f;
        public float weightToi = 2.0f;
        public float horizTime = 2.0f;
        public int adaptiveDivs = 7; // < adaptive
        public int adaptiveRings = 2; // < adaptive
        public int adaptiveDepth = 5; // < adaptive

        public DtObstacleAvoidanceParams()
        {
        }

        public DtObstacleAvoidanceParams(DtObstacleAvoidanceParams option)
        {
            velBias = option.velBias;
            weightDesVel = option.weightDesVel;
            weightCurVel = option.weightCurVel;
            weightToi = option.weightToi;
            horizTime = option.horizTime;
            adaptiveDivs = option.adaptiveDivs;
            adaptiveRings = option.adaptiveRings;
            adaptiveDepth = option.adaptiveDepth;
        }
    };
}