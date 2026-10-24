using System.Collections.Generic;

namespace DotRecast.Recast.Geom
{
    internal class RcBoundsItemYComparer : IComparer<RcBoundsItem>
    {
        public static readonly RcBoundsItemYComparer Shared = new RcBoundsItemYComparer();

        private RcBoundsItemYComparer()
        {
        }

        public int Compare(RcBoundsItem a, RcBoundsItem b)
        {
            return a.bmin.Y.CompareTo(b.bmin.Y);
        }
    }
}