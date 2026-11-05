/*
 * Unbound: the edge/polygon collider for connected two-sided edges, b2EPCollider from
 * Box2D v2.3.1 Box2D/Box2D/Collision/b2CollideEdge.cpp (https://github.com/erincatto/box2d,
 * tag v2.3.1). That release is under the zlib licence:
 *
 * Copyright (c) 2007-2009 Erin Catto http://www.box2d.org
 *
 * This software is provided 'as-is', without any express or implied
 * warranty.  In no event will the authors be held liable for any damages
 * arising from the use of this software.
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 * 1. The origin of this software must not be misrepresented; you must not
 * claim that you wrote the original software. If you use this software
 * in a product, an acknowledgment in the product documentation would be
 * appreciated but is not required.
 * 2. Altered source versions must be plainly marked as such, and must not be
 * misrepresented as being the original software.
 * 3. This notice may not be removed or altered from any source distribution.
 *
 * Altered: translated to C# for Unbound's Box2D v2.4 port (types, names, ClipSegmentToLine).
 * The contact radius is the sum of both shape radii, as in v2.4, because Unity rounds
 * polygons and edges; v2.3 used 2 * b2_polygonRadius, which is the same for unrounded shapes.
 * An exactly zero-length ghost segment counts as no ghost vertex (v2.3 has no such check).
 *
 * Box2D v2.4 replaced this collider with one-sided smooth collision, but Unity's
 * EdgeCollider2D is two-sided and keeps v2.3's adjacency (useAdjacentStartPoint/EndPoint).
 */

using System;
using System.Numerics;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Collision
{
    internal static partial class CollisionUtils
    {
        // This class collides an edge and a polygon, taking into account edge adjacency.
        private struct EPCollider
        {
            private enum AxisType
            {
                Unknown,

                EdgeA,

                EdgeB
            }

            // This structure is used to keep track of the best separating axis.
            private struct Axis
            {
                public AxisType Type;

                public int Index;

                public float Separation;
            }

            private TempPolygon _polygonB;

            private Transform _xf;

            private Vector2 _centroidB;

            private Vector2 _v0, _v1, _v2, _v3;

            private Vector2 _normal0, _normal1, _normal2;

            private Vector2 _normal;

            private Vector2 _lowerLimit, _upperLimit;

            private float _radius;

            private bool _front;

            // Algorithm:
            // 1. Classify v1 and v2
            // 2. Classify polygon centroid as front or back
            // 3. Flip normal if necessary
            // 4. Initialize normal range to [-pi, pi] about face normal
            // 5. Adjust normal range according to adjacent edges
            // 6. Visit each separating axes, only accept axes within the range
            // 7. Return if _any_ axis indicates separation
            // 8. Clip
            public void Collide(ref Manifold manifold, EdgeShape edgeA, in Transform xfA, PolygonShape polygonB, in Transform xfB)
            {
                _xf = MathUtils.MulT(xfA, xfB);

                _centroidB = MathUtils.Mul(_xf, polygonB.Centroid);

                _v0 = edgeA.Vertex0;
                _v1 = edgeA.Vertex1;
                _v2 = edgeA.Vertex2;
                _v3 = edgeA.Vertex3;

                // Unbound: an exactly zero-length ghost segment is absent (EdgeShape.UsesVertex0/3).
                var hasVertex0 = edgeA.UsesVertex0;
                var hasVertex3 = edgeA.UsesVertex3;

                var edge1 = _v2 - _v1;
                edge1.Normalize();
                _normal1 = new Vector2(edge1.Y, -edge1.X);
                var offset1 = Vector2.Dot(_normal1, _centroidB - _v1);
                float offset0 = 0.0f, offset2 = 0.0f;
                bool convex1 = false, convex2 = false;

                // Is there a preceding edge?
                if (hasVertex0)
                {
                    var edge0 = _v1 - _v0;
                    edge0.Normalize();
                    _normal0 = new Vector2(edge0.Y, -edge0.X);
                    convex1 = MathUtils.Cross(edge0, edge1) >= 0.0f;
                    offset0 = Vector2.Dot(_normal0, _centroidB - _v0);
                }

                // Is there a following edge?
                if (hasVertex3)
                {
                    var edge2 = _v3 - _v2;
                    edge2.Normalize();
                    _normal2 = new Vector2(edge2.Y, -edge2.X);
                    convex2 = MathUtils.Cross(edge1, edge2) > 0.0f;
                    offset2 = Vector2.Dot(_normal2, _centroidB - _v2);
                }

                // Determine front or back collision. Determine collision normal limits.
                if (hasVertex0 && hasVertex3)
                {
                    if (convex1 && convex2)
                    {
                        _front = offset0 >= 0.0f || offset1 >= 0.0f || offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal0;
                            _upperLimit = _normal2;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = -_normal1;
                        }
                    }
                    else if (convex1)
                    {
                        _front = offset0 >= 0.0f || (offset1 >= 0.0f && offset2 >= 0.0f);
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal0;
                            _upperLimit = _normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal2;
                            _upperLimit = -_normal1;
                        }
                    }
                    else if (convex2)
                    {
                        _front = offset2 >= 0.0f || (offset0 >= 0.0f && offset1 >= 0.0f);
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = _normal2;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = -_normal0;
                        }
                    }
                    else
                    {
                        _front = offset0 >= 0.0f && offset1 >= 0.0f && offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = _normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal2;
                            _upperLimit = -_normal0;
                        }
                    }
                }
                else if (hasVertex0)
                {
                    if (convex1)
                    {
                        _front = offset0 >= 0.0f || offset1 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal0;
                            _upperLimit = -_normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = -_normal1;
                        }
                    }
                    else
                    {
                        _front = offset0 >= 0.0f && offset1 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = -_normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = _normal1;
                            _upperLimit = -_normal0;
                        }
                    }
                }
                else if (hasVertex3)
                {
                    if (convex2)
                    {
                        _front = offset1 >= 0.0f || offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = _normal2;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = _normal1;
                        }
                    }
                    else
                    {
                        _front = offset1 >= 0.0f && offset2 >= 0.0f;
                        if (_front)
                        {
                            _normal = _normal1;
                            _lowerLimit = -_normal1;
                            _upperLimit = _normal1;
                        }
                        else
                        {
                            _normal = -_normal1;
                            _lowerLimit = -_normal2;
                            _upperLimit = _normal1;
                        }
                    }
                }
                else
                {
                    _front = offset1 >= 0.0f;
                    if (_front)
                    {
                        _normal = _normal1;
                        _lowerLimit = -_normal1;
                        _upperLimit = -_normal1;
                    }
                    else
                    {
                        _normal = -_normal1;
                        _lowerLimit = _normal1;
                        _upperLimit = _normal1;
                    }
                }

                // Get polygonB in frameA
                _polygonB = new TempPolygon {Count = polygonB.Count};
                for (var i = 0; i < polygonB.Count; ++i)
                {
                    _polygonB.Vertices[i] = MathUtils.Mul(_xf, polygonB.Vertices[i]);
                    _polygonB.Normals[i] = MathUtils.Mul(_xf.Rotation, polygonB.Normals[i]);
                }

                _radius = polygonB.Radius + edgeA.Radius;

                manifold.PointCount = 0;

                var edgeAxis = ComputeEdgeSeparation();

                // If no valid normal can be found than this edge should not collide.
                if (edgeAxis.Type == AxisType.Unknown)
                {
                    return;
                }

                if (edgeAxis.Separation > _radius)
                {
                    return;
                }

                var polygonAxis = ComputePolygonSeparation();
                if (polygonAxis.Type != AxisType.Unknown && polygonAxis.Separation > _radius)
                {
                    return;
                }

                // Use hysteresis for jitter reduction.
                const float k_relativeTol = 0.98f;
                const float k_absoluteTol = 0.001f;

                Axis primaryAxis;
                if (polygonAxis.Type == AxisType.Unknown)
                {
                    primaryAxis = edgeAxis;
                }
                else if (polygonAxis.Separation > k_relativeTol * edgeAxis.Separation + k_absoluteTol)
                {
                    primaryAxis = polygonAxis;
                }
                else
                {
                    primaryAxis = edgeAxis;
                }

                Span<ClipVertex> ie = stackalloc ClipVertex[2];
                ReferenceFace rf = default;
                if (primaryAxis.Type == AxisType.EdgeA)
                {
                    manifold.Type = ManifoldType.FaceA;

                    // Search for the polygon normal that is most anti-parallel to the edge normal.
                    var bestIndex = 0;
                    var bestValue = Vector2.Dot(_normal, _polygonB.Normals[0]);
                    for (var i = 1; i < _polygonB.Count; ++i)
                    {
                        var value = Vector2.Dot(_normal, _polygonB.Normals[i]);
                        if (value < bestValue)
                        {
                            bestValue = value;
                            bestIndex = i;
                        }
                    }

                    var i1 = bestIndex;
                    var i2 = i1 + 1 < _polygonB.Count ? i1 + 1 : 0;

                    ie[0].Vector = _polygonB.Vertices[i1];
                    ie[0].Id.ContactFeature.IndexA = 0;
                    ie[0].Id.ContactFeature.IndexB = (byte)i1;
                    ie[0].Id.ContactFeature.TypeA = (byte)ContactFeature.FeatureType.Face;
                    ie[0].Id.ContactFeature.TypeB = (byte)ContactFeature.FeatureType.Vertex;

                    ie[1].Vector = _polygonB.Vertices[i2];
                    ie[1].Id.ContactFeature.IndexA = 0;
                    ie[1].Id.ContactFeature.IndexB = (byte)i2;
                    ie[1].Id.ContactFeature.TypeA = (byte)ContactFeature.FeatureType.Face;
                    ie[1].Id.ContactFeature.TypeB = (byte)ContactFeature.FeatureType.Vertex;

                    if (_front)
                    {
                        rf.I1 = 0;
                        rf.I2 = 1;
                        rf.V1 = _v1;
                        rf.V2 = _v2;
                        rf.Normal = _normal1;
                    }
                    else
                    {
                        rf.I1 = 1;
                        rf.I2 = 0;
                        rf.V1 = _v2;
                        rf.V2 = _v1;
                        rf.Normal = -_normal1;
                    }
                }
                else
                {
                    manifold.Type = ManifoldType.FaceB;

                    ie[0].Vector = _v1;
                    ie[0].Id.ContactFeature.IndexA = 0;
                    ie[0].Id.ContactFeature.IndexB = (byte)primaryAxis.Index;
                    ie[0].Id.ContactFeature.TypeA = (byte)ContactFeature.FeatureType.Vertex;
                    ie[0].Id.ContactFeature.TypeB = (byte)ContactFeature.FeatureType.Face;

                    ie[1].Vector = _v2;
                    ie[1].Id.ContactFeature.IndexA = 0;
                    ie[1].Id.ContactFeature.IndexB = (byte)primaryAxis.Index;
                    ie[1].Id.ContactFeature.TypeA = (byte)ContactFeature.FeatureType.Vertex;
                    ie[1].Id.ContactFeature.TypeB = (byte)ContactFeature.FeatureType.Face;

                    rf.I1 = primaryAxis.Index;
                    rf.I2 = rf.I1 + 1 < _polygonB.Count ? rf.I1 + 1 : 0;
                    rf.V1 = _polygonB.Vertices[rf.I1];
                    rf.V2 = _polygonB.Vertices[rf.I2];
                    rf.Normal = _polygonB.Normals[rf.I1];
                }

                rf.SideNormal1 = new Vector2(rf.Normal.Y, -rf.Normal.X);
                rf.SideNormal2 = -rf.SideNormal1;
                rf.SideOffset1 = Vector2.Dot(rf.SideNormal1, rf.V1);
                rf.SideOffset2 = Vector2.Dot(rf.SideNormal2, rf.V2);

                // Clip incident edge against extruded edge1 side edges.
                Span<ClipVertex> clipPoints1 = stackalloc ClipVertex[2];
                Span<ClipVertex> clipPoints2 = stackalloc ClipVertex[2];

                // Clip to box side 1
                var np = ClipSegmentToLine(clipPoints1, ie, rf.SideNormal1, rf.SideOffset1, rf.I1);

                if (np < Settings.MaxManifoldPoints)
                {
                    return;
                }

                // Clip to negative box side 1
                np = ClipSegmentToLine(clipPoints2, clipPoints1, rf.SideNormal2, rf.SideOffset2, rf.I2);

                if (np < Settings.MaxManifoldPoints)
                {
                    return;
                }

                // Now clipPoints2 contains the clipped points.
                if (primaryAxis.Type == AxisType.EdgeA)
                {
                    manifold.LocalNormal = rf.Normal;
                    manifold.LocalPoint = rf.V1;
                }
                else
                {
                    manifold.LocalNormal = polygonB.Normals[rf.I1];
                    manifold.LocalPoint = polygonB.Vertices[rf.I1];
                }

                var pointCount = 0;
                for (var i = 0; i < Settings.MaxManifoldPoints; ++i)
                {
                    var separation = Vector2.Dot(rf.Normal, clipPoints2[i].Vector - rf.V1);

                    if (separation <= _radius)
                    {
                        ref var cp = ref manifold.Points[pointCount];

                        if (primaryAxis.Type == AxisType.EdgeA)
                        {
                            cp.LocalPoint = MathUtils.MulT(_xf, clipPoints2[i].Vector);
                            cp.Id = clipPoints2[i].Id;
                        }
                        else
                        {
                            cp.LocalPoint = clipPoints2[i].Vector;
                            cp.Id.ContactFeature.TypeA = clipPoints2[i].Id.ContactFeature.TypeB;
                            cp.Id.ContactFeature.TypeB = clipPoints2[i].Id.ContactFeature.TypeA;
                            cp.Id.ContactFeature.IndexA = clipPoints2[i].Id.ContactFeature.IndexB;
                            cp.Id.ContactFeature.IndexB = clipPoints2[i].Id.ContactFeature.IndexA;
                        }

                        ++pointCount;
                    }
                }

                manifold.PointCount = pointCount;
            }

            private Axis ComputeEdgeSeparation()
            {
                var axis = new Axis
                {
                    Type = AxisType.EdgeA,
                    Index = _front ? 0 : 1,
                    Separation = Settings.MaxFloat
                };

                for (var i = 0; i < _polygonB.Count; ++i)
                {
                    var s = Vector2.Dot(_normal, _polygonB.Vertices[i] - _v1);
                    if (s < axis.Separation)
                    {
                        axis.Separation = s;
                    }
                }

                return axis;
            }

            private Axis ComputePolygonSeparation()
            {
                var axis = new Axis
                {
                    Type = AxisType.Unknown,
                    Index = -1,
                    Separation = -Settings.MaxFloat
                };

                var perp = new Vector2(-_normal.Y, _normal.X);

                for (var i = 0; i < _polygonB.Count; ++i)
                {
                    var n = -_polygonB.Normals[i];

                    var s1 = Vector2.Dot(n, _polygonB.Vertices[i] - _v1);
                    var s2 = Vector2.Dot(n, _polygonB.Vertices[i] - _v2);
                    var s = Math.Min(s1, s2);

                    if (s > _radius)
                    {
                        // No collision
                        axis.Type = AxisType.EdgeB;
                        axis.Index = i;
                        axis.Separation = s;
                        return axis;
                    }

                    // Adjacency
                    if (Vector2.Dot(n, perp) >= 0.0f)
                    {
                        if (Vector2.Dot(n - _upperLimit, _normal) < -Settings.AngularSlop)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        if (Vector2.Dot(n - _lowerLimit, _normal) < -Settings.AngularSlop)
                        {
                            continue;
                        }
                    }

                    if (s > axis.Separation)
                    {
                        axis.Type = AxisType.EdgeB;
                        axis.Index = i;
                        axis.Separation = s;
                    }
                }

                return axis;
            }
        }

        /// <summary>
        ///     Unbound: edge versus polygon for a two-sided edge connected to its neighbours
        ///     (HasVertex0/HasVertex3), Box2D v2.3's b2CollideEdgeAndPolygon. Polygon axes outside
        ///     the normal range the adjacent edges allow are ignored, so a polygon sliding across a
        ///     connection does not catch on the internal vertex (a ghost collision).
        /// </summary>
        public static void CollideEdgeAndPolygonAdjacent(
            ref Manifold manifold,
            EdgeShape edgeA,
            in Transform xfA,
            PolygonShape polygonB,
            in Transform xfB)
        {
            var collider = new EPCollider();
            collider.Collide(ref manifold, edgeA, xfA, polygonB, xfB);
        }
    }
}
