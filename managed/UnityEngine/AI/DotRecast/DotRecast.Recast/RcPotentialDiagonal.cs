namespace DotRecast.Recast
{
    internal readonly struct RcPotentialDiagonal
    {
        public readonly int vert;
        public readonly int dist;

        public RcPotentialDiagonal(int vert, int dist)
        {
            this.vert = vert;
            this.dist = dist;
        }
    }
}