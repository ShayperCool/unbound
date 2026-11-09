using System;
using DotRecast.Core;
using DotRecast.Core.Numerics;

namespace DotRecast.Detour
{
    /// <summary>
    /// The polygon nearest a point (Unbound: as Unity's NavMesh queries pick it). Each polygon is
    /// either under or over the point (inside it seen from above, or within a float epsilon of
    /// one of its edges), ranked by the height between them, or beside it, ranked by the straight
    /// distance to its nearest boundary point; the first of equally near polygons stays. Of the two
    /// best, see <see cref="Pick"/>.
    /// </summary>
    // Matches the Unity 6000.0.71f1 Editor (tests/projects/navbaked `sample`: points over a floor
    // under a nearer platform's edge sample the floor; points on area boundaries).
    internal struct DtFindNearestPolyQuery : IDtPolyQuery
    {
        /// <summary>Squared distance (x, z) from an edge within which a point counts as on it.</summary>
        const float OnEdgeSqr = 1.1920929e-07f;

        private readonly DtNavMeshQuery _query;
        private readonly DtNavMesh _nav;
        private readonly RcVec3f _center;
        // The best polygon beside the point [0] and under or over it [1].
        private float _besideDistanceSqr, _overDistanceSqr;
        private long _besideRef, _overRef;
        private RcVec3f _besidePoint, _overPoint;
        private long _nearestRef;
        private RcVec3f _nearestPoint;
        private bool _overPoly;

        public DtFindNearestPolyQuery(DtNavMeshQuery query, DtNavMesh nav, RcVec3f center)
        {
            _query = query;
            _nav = nav;
            _center = center;
            _besideDistanceSqr = float.MaxValue;
            _overDistanceSqr = float.MaxValue;
            _besideRef = 0;
            _overRef = 0;
            _besidePoint = center;
            _overPoint = center;
            _nearestRef = 0;
            _nearestPoint = center;
            _overPoly = false;
        }

        public void Process(DtMeshTile tile, ReadOnlySpan<int> polys, ReadOnlySpan<long> refs, int count)
        {
            Span<float> verts = stackalloc float[_nav.GetMaxVertsPerPoly() * 3];
            for (int i = 0; i < count; ++i)
            {
                long polyRef = refs[i];
                var poly = tile.data.polys[polys[i]];
                if (Over(tile, poly, verts, out var point))
                {
                    point.Y = _nav.PolyHeight(tile, poly, point);
                    float dy = _center.Y - point.Y;
                    float d = dy * dy;
                    if (d < _overDistanceSqr)
                    {
                        _overDistanceSqr = d;
                        _overRef = polyRef;
                        _overPoint = point;
                    }
                }
                else
                {
                    _query.ClosestPointOnPoly(polyRef, _center, out point, out _);
                    float dx = point.X - _center.X, dy = point.Y - _center.Y, dz = point.Z - _center.Z;
                    float d = dz * dz + dx * dx + dy * dy;
                    if (d < _besideDistanceSqr)
                    {
                        _besideDistanceSqr = d;
                        _besideRef = polyRef;
                        _besidePoint = point;
                    }
                }
            }
        }

        /// <summary>The point is inside the polygon seen from above (a crossing count with
        /// half-open edges) or within <see cref="OnEdgeSqr"/> of an edge; <paramref name="point"/>
        /// is the point, or the nearest point of that edge.</summary>
        private bool Over(DtMeshTile tile, DtPoly poly, Span<float> verts, out RcVec3f point)
        {
            point = _center;
            int nv = poly.vertCount;
            if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION || nv < 3) return false;
            for (int k = 0; k < nv; ++k)
                RcSpans.Copy(tile.data.verts, poly.verts[k] * 3, verts, k * 3, 3);
            bool inside = false;
            for (int k = 0, j = nv - 1; k < nv; j = k++)
            {
                float zk = verts[k * 3 + 2], zj = verts[j * 3 + 2];
                if ((_center.Z < zk) != (_center.Z < zj))
                {
                    float x = (_center.Z - zk) * (verts[j * 3] - verts[k * 3]) / (zj - zk) + verts[k * 3];
                    if (_center.X < x) inside = !inside;
                }
            }
            if (inside) return true;
            float best = float.MaxValue, bestT = 0f;
            int edge = -1;
            for (int k = 0, j = nv - 1; k < nv; j = k++)
            {
                var a = new RcVec3f(verts[j * 3], verts[j * 3 + 1], verts[j * 3 + 2]);
                var b = new RcVec3f(verts[k * 3], verts[k * 3 + 1], verts[k * 3 + 2]);
                float d = DtUtils.DistancePtSegSqr2D(_center, a, b, out float t);
                if (d < best)
                {
                    best = d;
                    bestT = t;
                    edge = j;
                }
            }
            if (!(best < OnEdgeSqr)) return false;
            int next = edge + 1 == nv ? 0 : edge + 1;
            float s = 1f - bestT;
            point = new RcVec3f(
                verts[edge * 3] * s + verts[next * 3] * bestT,
                verts[edge * 3 + 1] * s + verts[next * 3 + 1] * bestT,
                s * verts[edge * 3 + 2] + verts[next * 3 + 2] * bestT);
            return true;
        }

        /// <summary>Picks the nearest of the two best, each only when its point lies within the query
        /// box: the polygon under or over the point when <paramref name="preferOver"/> (Unity's
        /// SamplePosition, Raycast and FindClosestEdge), else the one beside it unless the other is
        /// nearer in height than it is in a straight line (paths and agents).</summary>
        public void Pick(RcVec3f halfExtents, bool preferOver)
        {
            _nearestRef = 0;
            _nearestPoint = _center;
            _overPoly = false;
            if (_besideRef != 0 && InBox(_besidePoint, halfExtents))
            {
                _nearestRef = _besideRef;
                _nearestPoint = _besidePoint;
                if (!preferOver && _besideDistanceSqr <= _overDistanceSqr) return;
            }
            if (_overRef != 0 && InBox(_overPoint, halfExtents))
            {
                _nearestRef = _overRef;
                _nearestPoint = _overPoint;
                _overPoly = true;
            }
        }

        private bool InBox(RcVec3f p, RcVec3f halfExtents) =>
            _center.X - halfExtents.X <= p.X && p.X <= _center.X + halfExtents.X &&
            _center.Y - halfExtents.Y <= p.Y && p.Y <= _center.Y + halfExtents.Y &&
            _center.Z - halfExtents.Z <= p.Z && p.Z <= _center.Z + halfExtents.Z;

        public long NearestRef()
        {
            return _nearestRef;
        }

        public RcVec3f NearestPt()
        {
            return _nearestPoint;
        }

        public bool OverPoly()
        {
            return _overPoly;
        }
    }
}
