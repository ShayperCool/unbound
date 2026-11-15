using System;
using System.Collections.Generic;
using Unity.Collections;
using Unbound.Box2D.Collision.Shapes;
using Shape = Unbound.Box2D.Collision.Shapes.Shape;
using XxHash32 = Unbound.Runtime.XxHash32;
using B2Settings = Unbound.Box2D.Common.Settings;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    public enum PhysicsShapeType2D
    {
        Circle = 0,
        Capsule = 1,
        Polygon = 2,
        Edges = 3,
    }

    /// <summary>One shape of a <see cref="PhysicsShapeGroup2D"/>: its type and radius, and where its
    /// vertices are in the group's vertex list.</summary>
    public partial struct PhysicsShape2D
    {
        PhysicsShapeType2D m_ShapeType;
        float m_Radius;
        int m_VertexStartIndex;
        int m_VertexCount;
        int m_UseAdjacentStart;
        int m_UseAdjacentEnd;
        Vector2 m_AdjacentStart;
        Vector2 m_AdjacentEnd;

        public PhysicsShapeType2D shapeType { get => m_ShapeType; set => m_ShapeType = value; }
        public float radius { get => m_Radius; set => m_Radius = value; }
        public int vertexStartIndex { get => m_VertexStartIndex; set => m_VertexStartIndex = value; }
        public int vertexCount { get => m_VertexCount; set => m_VertexCount = value; }
        public bool useAdjacentStart { get => m_UseAdjacentStart != 0; set => m_UseAdjacentStart = value ? 1 : 0; }
        public bool useAdjacentEnd { get => m_UseAdjacentEnd != 0; set => m_UseAdjacentEnd = value ? 1 : 0; }
        public Vector2 adjacentStart { get => m_AdjacentStart; set => m_AdjacentStart = value; }
        public Vector2 adjacentEnd { get => m_AdjacentEnd; set => m_AdjacentEnd = value; }
    }

    /// <summary>A list of 2D shapes and their vertices, in the space of
    /// <see cref="localToWorldMatrix"/> (a collider's rigidbody, or the world).</summary>
    public partial class PhysicsShapeGroup2D
    {
        internal GroupState m_GroupState;

        internal partial struct GroupState
        {
            public List<PhysicsShape2D> m_Shapes;
            public List<Vector2> m_Vertices;
            public Matrix4x4 m_LocalToWorld;

            public void ClearGeometry()
            {
                m_Shapes.Clear();
                m_Vertices.Clear();
            }
        }

        public PhysicsShapeGroup2D([Internal.DefaultValue("1")] int shapeCapacity = 1, [Internal.DefaultValue("8")] int vertexCapacity = 8)
        {
            m_GroupState = new GroupState
            {
                m_Shapes = new List<PhysicsShape2D>(shapeCapacity),
                m_Vertices = new List<Vector2>(vertexCapacity),
                m_LocalToWorld = Matrix4x4.identity,
            };
        }

        internal List<Vector2> groupVertices => m_GroupState.m_Vertices;
        internal List<PhysicsShape2D> groupShapes => m_GroupState.m_Shapes;

        public int shapeCount => m_GroupState.m_Shapes.Count;
        public int vertexCount => m_GroupState.m_Vertices.Count;

        public Matrix4x4 localToWorldMatrix
        {
            get => m_GroupState.m_LocalToWorld;
            set => m_GroupState.m_LocalToWorld = value;
        }

        public void Clear()
        {
            m_GroupState.ClearGeometry();
            m_GroupState.m_LocalToWorld = Matrix4x4.identity;
        }

        public void Add(PhysicsShapeGroup2D physicsShapeGroup)
        {
            if (physicsShapeGroup == null) throw new ArgumentNullException(nameof(physicsShapeGroup));
            int offset = vertexCount;
            foreach (var shape in physicsShapeGroup.groupShapes)
            {
                var s = shape;
                s.vertexStartIndex += offset;
                groupShapes.Add(s);
            }
            groupVertices.AddRange(physicsShapeGroup.groupVertices);
        }

        public void GetShapeData(List<PhysicsShape2D> shapes, List<Vector2> vertices)
        {
            shapes.Clear();
            shapes.AddRange(groupShapes);
            vertices.Clear();
            vertices.AddRange(groupVertices);
        }

        public void GetShapeData(NativeArray<PhysicsShape2D> shapes, NativeArray<Vector2> vertices)
        {
            if (shapes.Length < shapeCount || vertices.Length < vertexCount)
                throw new ArgumentException("The native arrays are too small for the group's shapes and vertices.");
            for (int i = 0; i < shapeCount; i++) shapes[i] = groupShapes[i];
            for (int i = 0; i < vertexCount; i++) vertices[i] = groupVertices[i];
        }

        void CheckIndex(int shapeIndex)
        {
            if (shapeIndex < 0 || shapeIndex >= shapeCount)
                throw new ArgumentOutOfRangeException(nameof(shapeIndex), $"Cannot access shape index {shapeIndex} as it is out of range (shape count {shapeCount}).");
        }

        public void GetShapeVertices(int shapeIndex, List<Vector2> vertices)
        {
            CheckIndex(shapeIndex);
            var shape = groupShapes[shapeIndex];
            vertices.Clear();
            for (int i = 0; i < shape.vertexCount; i++) vertices.Add(groupVertices[shape.vertexStartIndex + i]);
        }

        public Vector2 GetShapeVertex(int shapeIndex, int vertexIndex)
        {
            CheckIndex(shapeIndex);
            var shape = groupShapes[shapeIndex];
            if (vertexIndex < 0 || vertexIndex >= shape.vertexCount) throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            return groupVertices[shape.vertexStartIndex + vertexIndex];
        }

        public void SetShapeVertex(int shapeIndex, int vertexIndex, Vector2 vertex)
        {
            CheckIndex(shapeIndex);
            var shape = groupShapes[shapeIndex];
            if (vertexIndex < 0 || vertexIndex >= shape.vertexCount) throw new ArgumentOutOfRangeException(nameof(vertexIndex));
            groupVertices[shape.vertexStartIndex + vertexIndex] = vertex;
        }

        public void SetShapeRadius(int shapeIndex, float radius)
        {
            CheckIndex(shapeIndex);
            var shape = groupShapes[shapeIndex];
            shape.radius = Mathf.Max(0f, radius);
            groupShapes[shapeIndex] = shape;
        }

        public void SetShapeAdjacentVertices(int shapeIndex, bool useAdjacentStart, bool useAdjacentEnd, Vector2 adjacentStart, Vector2 adjacentEnd)
        {
            CheckIndex(shapeIndex);
            var shape = groupShapes[shapeIndex];
            shape.useAdjacentStart = useAdjacentStart;
            shape.useAdjacentEnd = useAdjacentEnd;
            shape.adjacentStart = adjacentStart;
            shape.adjacentEnd = adjacentEnd;
            groupShapes[shapeIndex] = shape;
        }

        public void DeleteShape(int shapeIndex)
        {
            CheckIndex(shapeIndex);
            var shape = groupShapes[shapeIndex];
            groupVertices.RemoveRange(shape.vertexStartIndex, shape.vertexCount);
            groupShapes.RemoveAt(shapeIndex);
            for (int i = 0; i < groupShapes.Count; i++)
            {
                var s = groupShapes[i];
                if (s.vertexStartIndex > shape.vertexStartIndex)
                {
                    s.vertexStartIndex -= shape.vertexCount;
                    groupShapes[i] = s;
                }
            }
        }

        public PhysicsShape2D GetShape(int shapeIndex)
        {
            CheckIndex(shapeIndex);
            return groupShapes[shapeIndex];
        }

        internal int AddShape(PhysicsShapeType2D type, float radius, IList<Vector2> vertices)
        {
            var shape = new PhysicsShape2D { shapeType = type, radius = radius, vertexStartIndex = vertexCount, vertexCount = vertices.Count };
            groupVertices.AddRange(vertices);
            groupShapes.Add(shape);
            return shapeCount - 1;
        }

        public int AddCircle(Vector2 center, float radius)
        {
            if (radius <= 0f) throw new ArgumentException("radius must be greater than zero.", nameof(radius));
            return AddShape(PhysicsShapeType2D.Circle, radius, new[] { center });
        }

        public int AddCapsule(Vector2 vertex0, Vector2 vertex1, float radius)
        {
            if (radius <= 0f) throw new ArgumentException("radius must be greater than zero.", nameof(radius));
            return AddShape(PhysicsShapeType2D.Capsule, radius, new[] { vertex0, vertex1 });
        }

        public int AddBox(Vector2 center, Vector2 size, [Internal.DefaultValue("0f")] float angle = 0f, [Internal.DefaultValue("0f")] float edgeRadius = 0f)
        {
            if (size.x <= 0f || size.y <= 0f) throw new ArgumentException("size must be greater than zero.", nameof(size));
            var rotation = Quaternion.Euler(0f, 0f, angle);
            var half = size * 0.5f;
            var corners = new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(half.x, half.y), new Vector2(-half.x, half.y) };
            for (int i = 0; i < 4; i++) corners[i] = center + (Vector2)(rotation * corners[i]);
            return AddShape(PhysicsShapeType2D.Polygon, edgeRadius, corners);
        }

        public int AddPolygon(List<Vector2> vertices)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (vertices.Count < 3 || vertices.Count > B2Settings.MaxPolygonVertices)
                throw new ArgumentException($"A polygon needs between 3 and {B2Settings.MaxPolygonVertices} vertices.", nameof(vertices));
            return AddShape(PhysicsShapeType2D.Polygon, 0f, vertices);
        }

        public int AddEdges(List<Vector2> vertices, [Internal.DefaultValue("0f")] float edgeRadius = 0f) =>
            AddEdges(vertices, false, false, Vector2.zero, Vector2.zero, edgeRadius);

        public int AddEdges(List<Vector2> vertices, bool useAdjacentStart, bool useAdjacentEnd, Vector2 adjacentStart, Vector2 adjacentEnd, [Internal.DefaultValue("0f")] float edgeRadius = 0f)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (vertices.Count < 2) throw new ArgumentException("Edges need at least two vertices.", nameof(vertices));
            int index = AddShape(PhysicsShapeType2D.Edges, edgeRadius, vertices);
            SetShapeAdjacentVertices(index, useAdjacentStart, useAdjacentEnd, adjacentStart, adjacentEnd);
            return index;
        }
    }

    public partial class Collider2D
    {
        public int GetShapes(PhysicsShapeGroup2D physicsShapeGroup) => GetShapes(physicsShapeGroup, 0, 0, true);

        /// <summary>The collider's shapes as Unity reports them: in its rigidbody's space (the world
        /// for colliders without one), polygons and edges without Box2D's skin radius, loops with
        /// their first vertex repeated at the end.</summary>
        public int GetShapes(PhysicsShapeGroup2D physicsShapeGroup, int shapeIndex, [Internal.DefaultValue("1")] int shapeCount = 1)
            => GetShapes(physicsShapeGroup, shapeIndex, shapeCount, false);

        int GetShapes(PhysicsShapeGroup2D physicsShapeGroup, int shapeIndex, int shapeCount, bool all)
        {
            if (physicsShapeGroup == null) throw new ArgumentNullException(nameof(physicsShapeGroup));
            W.PrepareQuery();
            if (all)
            {
                shapeCount = m_Fixtures.Count;
                if (shapeCount == 0) { physicsShapeGroup.Clear(); return 0; }
            }
            else if (shapeIndex < 0 || shapeIndex >= m_Fixtures.Count || shapeCount <= 0 ||
                (long)shapeIndex + shapeCount > m_Fixtures.Count)
                throw new ArgumentOutOfRangeException($"Cannot get shape range from {shapeIndex} to {(long)shapeIndex + shapeCount - 1} as Collider2D only has {m_Fixtures.Count} shape(s).");
            physicsShapeGroup.m_GroupState.ClearGeometry();
            var rb = m_AttachedBody;
            var space = rb != null ? (Matrix4x4)rb.localToWorldMatrix : Matrix4x4.identity;
            physicsShapeGroup.localToWorldMatrix = space;
            int last = shapeIndex + shapeCount;
            var points = new List<Vector2>();
            for (int i = shapeIndex; i < last; i++)
            {
                var f = m_Fixtures[i];
                // Fixtures already use the group's body frame. A world-space
                // round trip changes the indexed local vertices.
                Vector2 Out(System.Numerics.Vector2 local)
                {
                    return W.ToUnity(local);
                }
                points.Clear();
                switch (f.Shape)
                {
                    case CircleShape circle:
                        points.Add(Out(circle.Position));
                        physicsShapeGroup.AddShape(PhysicsShapeType2D.Circle, circle.Radius, points);
                        break;
                    case CapsuleShape capsule:
                        points.Add(Out(capsule.P1));
                        points.Add(Out(capsule.P2));
                        physicsShapeGroup.AddShape(PhysicsShapeType2D.Capsule, capsule.Radius, points);
                        break;
                    case PolygonShape polygon:
                        for (int v = 0; v < polygon.Count; v++) points.Add(Out(polygon.Vertices[v]));
                        physicsShapeGroup.AddShape(PhysicsShapeType2D.Polygon, Mathf.Max(0f, polygon.Radius - B2Settings.PolygonRadius), points);
                        break;
                    case EdgeShape edge:
                    {
                        points.Add(Out(edge.Vertex1));
                        points.Add(Out(edge.Vertex2));
                        int index = physicsShapeGroup.AddShape(PhysicsShapeType2D.Edges, Mathf.Max(0f, edge.Radius - B2Settings.PolygonRadius), points);
                        if (edge.OneSided)
                            physicsShapeGroup.SetShapeAdjacentVertices(index, true, true, Out(edge.Vertex0), Out(edge.Vertex3));
                        break;
                    }
                    case ChainShape chain:
                        for (int v = 0; v < chain.Count; v++) points.Add(Out(chain.Vertices[v]));
                        int chainIndex = physicsShapeGroup.AddShape(PhysicsShapeType2D.Edges, Mathf.Max(0f, chain.Radius - B2Settings.PolygonRadius), points);
                        physicsShapeGroup.SetShapeAdjacentVertices(chainIndex, chain.HasPrevVertex, chain.HasNextVertex,
                            Out(chain.PrevVertex), Out(chain.NextVertex));
                        break;
                }
            }
            return physicsShapeGroup.shapeCount;
        }

        /// <summary>The hash of a collider without shapes and the seed of the first shape's hash
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/shapes2d).</summary>
        internal const uint ShapeHashSeed = 0x8f37154bu;

        // Shape type numbers inside the hashed circle and capsule records.
        const int CircleShapeTypeId = 0;
        const int CapsuleShapeTypeId = 4;

        /// <summary>
        /// The hash of the collider's shapes as they are now: each shape's hash is seeded with the
        /// previous one's, in shape order. A polygon hashes its radius and then its vertices (as the
        /// rigidbody space holds them, in hull order), a chain likewise. Edge colliders make chains
        /// on the measured release; a lone edge shape (only built for unmeasured releases) leaves
        /// the hash unchanged, unmeasured. A circle or capsule hashes an eight-byte record header, its type, radius and
        /// points: the header is zero here, but Unity's differs between Editor processes, so only
        /// equality within one run is reproducible for those (tests/projects/shapes2d).
        /// </summary>
        public uint GetShapeHash()
        {
            W.Flush();
            return m_ShapeHashCarried ? m_CarriedShapeHash : FreshShapeHash();
        }

        /// <summary>The hash as a full recreation of the shapes leaves it: every shape in order
        /// from the seed.</summary>
        internal uint FreshShapeHash()
        {
            uint hash = ShapeHashSeed;
            foreach (var fixture in m_Fixtures) hash = ShapeHash(fixture.Shape, hash);
            return hash;
        }

        /// <summary>Set when the shapes changed in place (a tilemap's processed tile changes):
        /// the hash then carries the removed and added shapes on top of the previous value
        /// instead of starting from the seed.</summary>
        internal bool m_ShapeHashCarried;
        internal uint m_CarriedShapeHash;

        /// <summary>After the fixtures were created again: a full recreation unless a subclass
        /// carries the hash over.</summary>
        internal virtual void ShapesRebuilt() => m_ShapeHashCarried = false;

        internal static uint ShapeHash(Shape shape, uint seed)
        {
            Span<byte> record = stackalloc byte[32];
            record.Clear();
            switch (shape)
            {
                case CircleShape circle:
                    WriteHeader(record, CircleShapeTypeId, circle.Radius);
                    WriteFloats(record.Slice(16), circle.Position.X, circle.Position.Y);
                    return XxHash32.Hash(record.Slice(0, 24), seed);
                case CapsuleShape capsule:
                    WriteHeader(record, CapsuleShapeTypeId, capsule.Radius);
                    WriteFloats(record.Slice(16), capsule.P1.X, capsule.P1.Y);
                    WriteFloats(record.Slice(24), capsule.P2.X, capsule.P2.Y);
                    return XxHash32.Hash(record, seed);
                case PolygonShape polygon:
                    return PointsHash(polygon.Radius, polygon.Vertices.AsSpan(0, polygon.Count), seed);
                case ChainShape chain:
                    return PointsHash(chain.Radius, chain.Vertices.AsSpan(0, chain.Count), seed);
                default:
                    return seed;
            }
        }

        static void WriteHeader(Span<byte> record, int type, float radius)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(record.Slice(8), type);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(record.Slice(12), radius);
        }

        static void WriteFloats(Span<byte> record, float x, float y)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(record, x);
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(record.Slice(4), y);
        }

        static uint PointsHash(float radius, ReadOnlySpan<System.Numerics.Vector2> points, uint seed)
        {
            if (points.Length == 0) return seed;
            Span<byte> radiusBytes = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(radiusBytes, radius);
            seed = XxHash32.Hash(radiusBytes, seed);
            return XxHash32.Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(points), seed);
        }

        /// <summary>
        /// Each shape's bounds (the union of its children's) in rigidbody space, or in world space,
        /// and their union. Circles and capsules always include their radius; with
        /// <paramref name="useRadii"/>, polygons and edges include the radius beyond the current
        /// default contact offset, or their whole radius when it is no larger. The z center is the
        /// collider transform's, the z extent zero. A collider without shapes returns its
        /// transform's position with no extent and leaves the list as it was; pending transform
        /// changes are not synchronized first (tests/projects/shapes2d).
        /// </summary>
        public Bounds GetShapeBounds(List<Bounds> bounds, bool useRadii, bool useWorldSpace)
        {
            W.Flush();
            var position = m_GameObject.m_Transform.position;
            if (m_Fixtures.Count == 0) return new Bounds(position, Vector3.zero);
            bounds?.Clear();
            var frame = useWorldSpace ? m_Fixtures[0].Body.GetTransform()
                : new Unbound.Box2D.Common.Transform(System.Numerics.Vector2.Zero, new Unbound.Box2D.Common.Rotation(0f, 1f));
            var lower = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var upper = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var fixture in m_Fixtures)
            {
                ShapeBounds(fixture.Shape, frame, useRadii, out var min, out var max);
                bounds?.Add(Box(min, max, position.z));
                lower = Vector2.Min(lower, min);
                upper = Vector2.Max(upper, max);
            }
            return Box(lower, upper, position.z);
        }

        static Bounds Box(Vector2 min, Vector2 max, float z)
        {
            var box = new Bounds();
            box.center = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, z);
            box.extents = new Vector3((max.x - min.x) * 0.5f, (max.y - min.y) * 0.5f, 0f);
            return box;
        }

        static void ShapeBounds(Shape shape, in Unbound.Box2D.Common.Transform frame, bool useRadii, out Vector2 min, out Vector2 max)
        {
            ReadOnlySpan<System.Numerics.Vector2> points;
            float radius;
            switch (shape)
            {
                case CircleShape circle:
                    points = new ReadOnlySpan<System.Numerics.Vector2>(in circle.Position);
                    radius = circle.Radius;
                    break;
                case CapsuleShape capsule:
                    points = capsule.Vertices.AsSpan(0, 2);
                    radius = capsule.Radius;
                    break;
                case PolygonShape polygon:
                    points = polygon.Vertices.AsSpan(0, polygon.Count);
                    radius = useRadii ? EffectiveRadius(polygon.Radius) : 0f;
                    break;
                case ChainShape chain:
                    points = chain.Vertices.AsSpan(0, chain.Count);
                    radius = useRadii ? EffectiveRadius(chain.Radius) : 0f;
                    break;
                case EdgeShape edge:
                {
                    var a = Unbound.Box2D.Common.MathUtils.Mul(frame, edge.Vertex1);
                    var b = Unbound.Box2D.Common.MathUtils.Mul(frame, edge.Vertex2);
                    float r = useRadii ? EffectiveRadius(edge.Radius) : 0f;
                    min = new Vector2(Math.Min(a.X, b.X) - r, Math.Min(a.Y, b.Y) - r);
                    max = new Vector2(Math.Max(a.X, b.X) + r, Math.Max(a.Y, b.Y) + r);
                    return;
                }
                default:
                    throw new NotSupportedException("Unknown 2D shape " + shape.GetType().Name);
            }
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var point in points)
            {
                var p = Unbound.Box2D.Common.MathUtils.Mul(frame, point);
                min = new Vector2(Math.Min(min.x, p.X), Math.Min(min.y, p.Y));
                max = new Vector2(Math.Max(max.x, p.X), Math.Max(max.y, p.Y));
            }
            min.x -= radius; min.y -= radius;
            max.x += radius; max.y += radius;
        }

        /// <summary>The part of a polygon or edge radius beyond the default contact offset, or the
        /// whole radius when it is no larger than the offset.</summary>
        static float EffectiveRadius(float radius) =>
            radius <= B2Settings.PolygonRadius ? radius : radius - B2Settings.PolygonRadius;
    }
}
