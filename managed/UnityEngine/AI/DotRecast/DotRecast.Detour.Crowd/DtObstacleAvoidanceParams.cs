namespace DotRecast.Detour.Crowd
{
    internal class DtObstacleAvoidanceParams
    {
        public float velBias;
        public float weightDesVel;
        public float weightCurVel;
        public float weightSide;
        public float weightToi;
        public float horizTime;
        public int gridSize; // < grid
        public int adaptiveDivs; // < adaptive
        public int adaptiveRings; // < adaptive
        public int adaptiveDepth; // < adaptive
        /// Unbound: how far right of the biased desired velocity the pattern is centred, as a
        /// fraction of the desired velocity, and whether candidates are judged as Unity's agents
        /// judge them (against the neighbour's velocity alone, grazing paths free).
        public float sideOffset;
        public bool unityVelocity;

        public DtObstacleAvoidanceParams()
        {
            velBias = 0.4f;
            weightDesVel = 2.0f;
            weightCurVel = 0.75f;
            weightSide = 0.75f;
            weightToi = 2.5f;
            horizTime = 2.5f;
            gridSize = 33;
            adaptiveDivs = 7;
            adaptiveRings = 2;
            adaptiveDepth = 5;
        }

        public DtObstacleAvoidanceParams(DtObstacleAvoidanceParams option)
        {
            velBias = option.velBias;
            weightDesVel = option.weightDesVel;
            weightCurVel = option.weightCurVel;
            weightSide = option.weightSide;
            weightToi = option.weightToi;
            horizTime = option.horizTime;
            gridSize = option.gridSize;
            adaptiveDivs = option.adaptiveDivs;
            adaptiveRings = option.adaptiveRings;
            adaptiveDepth = option.adaptiveDepth;
            sideOffset = option.sideOffset;
            unityVelocity = option.unityVelocity;
        }
    };
}