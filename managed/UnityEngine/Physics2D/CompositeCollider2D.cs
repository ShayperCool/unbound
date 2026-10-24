using System;
using System.Collections.Generic;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Clipper;
using Unbound.Runtime;
using B2Settings = Unbound.Box2D.Common.Settings;
using Shape = Unbound.Box2D.Collision.Shapes.Shape;
using W = Unbound.Runtime.Physics2DWorld;
using Path = System.Collections.Generic.List<Unbound.Clipper.IntPoint>;
using Paths = System.Collections.Generic.List<System.Collections.Generic.List<Unbound.Clipper.IntPoint>>;

namespace UnityEngine
{
    /// <summary>
    /// Merges the geometry of the colliders on its rigidbody that use it (their composite
    /// operation is not None) into outlines, as Unity 6 does with Clipper (measured,
    /// `tests/projects/twod`): each source grows by the offset distance with square corners,
    /// the sources combine by their operations in composite order, the result shrinks by twice the
    /// offset and grows back by it (all square joins, 1e7 units per world unit), and vertices
    /// closer than the vertex distance are cleaned. Outlines become edge loops; Polygons geometry
    /// splits the outlines (with their holes) into convex polygons.
    /// </summary>
    [ClassId(66)]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed partial class CompositeCollider2D : Collider2D
    {
        GeometryType m_GeometryType;
        GenerationType m_GenerationType;
        bool m_UseDelaunayMesh;
        float m_VertexDistance = 0.0005f;
        float m_OffsetDistance = 0.00005f;
        float m_EdgeRadius;
        /// <summary>The generated outlines, in the composite's space.</summary>
        List<Vector2[]> m_Paths = new List<Vector2[]>();
        bool m_HavePaths;

        public CompositeCollider2D() { }

        public GeometryType geometryType
        {
            get => m_GeometryType;
            set
            {
                m_GeometryType = value;
                ShapeChanged();
            }
        }

        public GenerationType generationType
        {
            get => m_GenerationType;
            set
            {
                m_GenerationType = value;
                if (value == GenerationType.Synchronous) ShapeChanged();
            }
        }

        public bool useDelaunayMesh
        {
            get => m_UseDelaunayMesh;
            set
            {
                m_UseDelaunayMesh = value;
                ShapeChanged();
            }
        }

        public float vertexDistance
        {
            get => m_VertexDistance;
            set
            {
                m_VertexDistance = Mathf.Max(value, 1e-5f);
                Regenerate();
            }
        }

        public float edgeRadius
        {
            get => m_EdgeRadius;
            set
            {
                m_EdgeRadius = Mathf.Max(0f, value);
                ShapeChanged();
            }
        }

        public float offsetDistance
        {
            get => m_OffsetDistance;
            set
            {
                m_OffsetDistance = Mathf.Max(0f, value);
                Regenerate();
            }
        }

        void Regenerate()
        {
            if (m_GenerationType == GenerationType.Synchronous)
            {
                m_HavePaths = false;
                ShapeChanged();
            }
        }

        /// <summary>Its sources changed: build again (unless generation is manual).</summary>
        internal void SourcesChanged()
        {
            if (m_GenerationType == GenerationType.Manual && m_HavePaths) return;
            m_HavePaths = false;
            ShapeChanged();
        }

        public int pathCount
        {
            get
            {
                W.Flush();
                return m_Paths.Count;
            }
        }

        public int pointCount
        {
            get
            {
                W.Flush();
                int count = 0;
                foreach (var p in m_Paths) count += p.Length;
                return count;
            }
        }

        public int GetPathPointCount(int index)
        {
            W.Flush();
            CheckPath(index);
            return m_Paths[index].Length;
        }

        void CheckPath(int index)
        {
            if (index < 0 || index >= m_Paths.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Path {index} does not exist.");
        }

        public int GetPath(int index, Vector2[] points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            W.Flush();
            CheckPath(index);
            var path = m_Paths[index];
            int n = Math.Min(points.Length, path.Length);
            Array.Copy(path, points, n);
            return n;
        }

        public int GetPath(int index, List<Vector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            W.Flush();
            CheckPath(index);
            points.Clear();
            points.AddRange(m_Paths[index]);
            return points.Count;
        }

        public void GenerateGeometry()
        {
            m_HavePaths = false;
            if (m_Registered)
            {
                W.MarkShapeDirty(this);
                W.Flush();
            }
        }

        /// <summary>The colliders this composite merges: on its rigidbody, enabled, using it.</summary>
        internal List<Collider2D> Sources()
        {
            var sources = new List<Collider2D>();
            var rb = m_GameObject.GetComponent<Rigidbody2D>();
            if (rb == null) return sources;
            foreach (var c in W.Colliders)
            {
                if (c == this || c is CompositeCollider2D || c.m_CompositeOperation == CompositeOperation.None) continue;
                if (!ReferenceEquals(W.FindRigidbody(c), rb)) continue;
                sources.Add(c);
            }
            // Stable by composite order.
            var ordered = new List<(Collider2D C, int I)>();
            for (int i = 0; i < sources.Count; i++) ordered.Add((sources[i], i));
            ordered.Sort((a, b) =>
            {
                int c = a.C.m_CompositeOrder.CompareTo(b.C.m_CompositeOrder);
                return c != 0 ? c : a.I.CompareTo(b.I);
            });
            sources.Clear();
            foreach (var o in ordered) sources.Add(o.C);
            return sources;
        }

        public int GetCompositedColliders(List<Collider2D> colliders)
        {
            if (colliders == null) throw new ArgumentNullException(nameof(colliders));
            W.Flush();
            colliders.Clear();
            colliders.AddRange(Sources());
            return colliders.Count;
        }

        void Generate(Matrix4x4 toBody)
        {
            m_Paths = new List<Vector2[]>();
            m_HavePaths = true;
            var fromBody = toBody.inverse;
            var result = new Paths();
            double scale = ClipperOps.Scale;
            var rb = m_GameObject.GetComponent<Rigidbody2D>();
            if (rb == null) return;
            W.PoseOf(rb.transform, out var bodyPosition, out var bodyAngle);
            foreach (var source in Sources())
            {
                // The source in the composite's body frame, grown by the offset distance.
                var polygons = new List<Vector2[]>();
                source.BuildCompositeSource(W.ToBodyMatrix(source.transform, bodyPosition, bodyAngle), polygons);
                var grown = ClipperOps.OffsetInt(ClipperOps.ToInt(polygons), m_OffsetDistance * scale);
                switch (source.m_CompositeOperation)
                {
                    case CompositeOperation.Merge: result = ClipperOps.Combine(result, grown, ClipType.ctUnion); break;
                    case CompositeOperation.Intersect: result = ClipperOps.Combine(result, grown, ClipType.ctIntersection); break;
                    case CompositeOperation.Difference: result = ClipperOps.Combine(result, grown, ClipType.ctDifference); break;
                    case CompositeOperation.Flip: result = ClipperOps.Combine(result, grown, ClipType.ctXor); break;
                }
            }
            if (result.Count == 0) return;
            var shrunk = ClipperOps.OffsetInt(result, -2.0 * m_OffsetDistance * scale);
            var restored = ClipperOps.OffsetInt(shrunk, m_OffsetDistance * scale);
            var clean = Clipper.CleanPolygons(restored, m_VertexDistance * scale);
            foreach (var path in clean)
            {
                if (path.Count < 3) continue;
                var points = new Vector2[path.Count];
                for (int i = 0; i < points.Length; i++)
                    points[i] = fromBody.MultiplyPoint3x4(new Vector2((float)(path[i].X / scale), (float)(path[i].Y / scale)));
                m_Paths.Add(points);
            }
        }

        internal override void BuildShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            if (!m_HavePaths) Generate(toBody);
            int made = 0, dropped = 0;
            if (m_GeometryType == GeometryType.Outlines)
            {
                foreach (var path in m_Paths)
                {
                    var points = new System.Numerics.Vector2[path.Length];
                    for (int i = 0; i < points.Length; i++) points[i] = ToBody(toBody, path[i]);
                    if (points.Length < 3) { dropped++; continue; }
                    var chain = new ChainShape();
                    chain.CreateLoop(points);
                    chain.Radius = B2Settings.PolygonRadius + m_EdgeRadius;
                    shapes.Add(chain);
                    made++;
                }
            }
            else
            {
                var body = new List<List<System.Numerics.Vector2>>();
                foreach (var path in m_Paths)
                {
                    var points = new List<System.Numerics.Vector2>(path.Length);
                    foreach (var p in path) points.Add(ToBody(toBody, p));
                    body.Add(points);
                }
                foreach (var piece in PolygonDecomposition.ConvexPiecesWithHoles(body))
                {
                    var shape = ConvexShape(piece);
                    if (shape == null) { dropped++; continue; }
                    if (m_EdgeRadius > 0f) shape.Radius += m_EdgeRadius;
                    shapes.Add(shape);
                    made++;
                }
            }
            m_ErrorState = dropped > 0 ? ColliderErrorState2D.RemovedShapes : ColliderErrorState2D.None;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_GeometryType = (GeometryType)body.Int("m_GeometryType");
            m_GenerationType = (GenerationType)body.Int("m_GenerationType");
            m_EdgeRadius = body.Float("m_EdgeRadius");
            m_VertexDistance = body.Float("m_VertexDistance", 0.0005f);
            m_OffsetDistance = body.Float("m_OffsetDistance", 0.00005f);
            m_UseDelaunayMesh = body.Bool("m_UseDelaunayMesh");
            // Manual generation keeps the outlines the Editor generated and saved.
            if (m_GenerationType == GenerationType.Manual)
            {
                var paths = body.Get("m_CompositePaths")?.Get("m_Paths");
                if (paths != null)
                {
                    m_Paths = new List<Vector2[]>();
                    foreach (var path in paths.Seq())
                    {
                        var list = new List<Vector2>();
                        foreach (var p in path.Seq()) list.Add(new Vector2(p.Float("x"), p.Float("y")));
                        m_Paths.Add(list.ToArray());
                    }
                    m_HavePaths = true;
                }
            }
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (CompositeCollider2D)source;
            CopyColliderData(s);
            m_GeometryType = s.m_GeometryType;
            m_GenerationType = s.m_GenerationType;
            m_UseDelaunayMesh = s.m_UseDelaunayMesh;
            m_VertexDistance = s.m_VertexDistance;
            m_OffsetDistance = s.m_OffsetDistance;
            m_EdgeRadius = s.m_EdgeRadius;
            m_Paths = new List<Vector2[]>();
            foreach (var p in s.m_Paths) m_Paths.Add((Vector2[])p.Clone());
            m_HavePaths = s.m_HavePaths && s.m_GenerationType == GenerationType.Manual;
        }

        public enum GeometryType
        {
            Outlines = 0,
            Polygons = 1,
        }

        public enum GenerationType
        {
            Synchronous = 0,
            Manual = 1,
        }
    }

    public partial class Collider2D
    {
        /// <summary>This collider's geometry as polygons in the body frame, for a composite.</summary>
        internal virtual void BuildCompositeSource(Matrix4x4 toBody, List<Vector2[]> polygons)
        {
            var shapes = new List<Shape>();
            BuildShapes(toBody, shapes);
            foreach (var shape in shapes)
            {
                switch (shape)
                {
                    case CapsuleShape capsule:
                        polygons.Add(CapsuleOutline(W.ToUnity(capsule.P1), W.ToUnity(capsule.P2), capsule.Radius));
                        break;
                    case PolygonShape polygon:
                    {
                        var points = new Vector2[polygon.Count];
                        for (int i = 0; i < points.Length; i++) points[i] = W.ToUnity(polygon.Vertices[i]);
                        polygons.Add(points);
                        break;
                    }
                    case CircleShape circle:
                        polygons.Add(CapsuleOutline(W.ToUnity(circle.Position), W.ToUnity(circle.Position), circle.Radius));
                        break;
                }
            }
        }

        /// <summary>A capsule (or, with equal ends, a circle) as a polygon.</summary>
        static Vector2[] CapsuleOutline(Vector2 a, Vector2 b, float radius)
        {
            const int segments = 32;
            var points = new List<Vector2>();
            var axis = b - a;
            float angle = axis.sqrMagnitude > 1e-12f ? Mathf.Atan2(axis.y, axis.x) : 0f;
            for (int i = 0; i <= segments / 2; i++)
            {
                float t = angle - Mathf.PI * 0.5f + Mathf.PI * i / (segments / 2);
                points.Add(b + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius);
            }
            for (int i = 0; i <= segments / 2; i++)
            {
                float t = angle + Mathf.PI * 0.5f + Mathf.PI * i / (segments / 2);
                points.Add(a + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * radius);
            }
            return points.ToArray();
        }

        /// <summary>The composite this collider gives its geometry to: on its rigidbody's
        /// GameObject, enabled.</summary>
        internal CompositeCollider2D Composite()
        {
            if (m_CompositeOperation == CompositeOperation.None || this is CompositeCollider2D) return null;
            var rb = W.FindRigidbody(this);
            if (rb == null) return null;
            var composite = rb.m_GameObject.GetComponent<CompositeCollider2D>();
            return composite != null && composite.m_Registered ? composite : null;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using Unbound.Clipper;

    /// <summary>Clipper offsets and boolean operations on float polygons (1e7 integer units per
    /// world unit, as Unity's 2D physics uses).</summary>
    internal static class ClipperOps
    {
        internal const double Scale = 1e7;

        internal static Paths ToInt(List<Vector2[]> polygons)
        {
            var paths = new Paths(polygons.Count);
            foreach (var polygon in polygons)
            {
                var path = new Path(polygon.Length);
                foreach (var p in polygon) path.Add(new IntPoint((long)Math.Round(p.x * Scale), (long)Math.Round(p.y * Scale)));
                paths.Add(path);
            }
            return paths;
        }

        internal static List<Vector2[]> ToFloat(Paths paths)
        {
            var result = new List<Vector2[]>(paths.Count);
            foreach (var path in paths)
            {
                var points = new Vector2[path.Count];
                for (int i = 0; i < points.Length; i++) points[i] = new Vector2((float)(path[i].X / Scale), (float)(path[i].Y / Scale));
                result.Add(points);
            }
            return result;
        }

        /// <summary>Grows (or shrinks) polygons by a distance in integer units, square corners.</summary>
        internal static Paths OffsetInt(Paths paths, double delta)
        {
            var offset = new ClipperOffset();
            offset.AddPaths(paths, JoinType.jtSquare, EndType.etClosedPolygon);
            var result = new Paths();
            offset.Execute(ref result, delta);
            return result;
        }

        internal static List<Vector2[]> Offset(List<Vector2[]> polygons, float distance) =>
            ToFloat(OffsetInt(ToInt(polygons), distance * Scale));

        internal static Paths Combine(Paths subject, Paths clip, ClipType operation)
        {
            var clipper = new Clipper();
            clipper.AddPaths(subject, PolyType.ptSubject, true);
            clipper.AddPaths(clip, PolyType.ptClip, true);
            var result = new Paths();
            clipper.Execute(operation, result, PolyFillType.pftNonZero, PolyFillType.pftNonZero);
            return result;
        }
    }
}
