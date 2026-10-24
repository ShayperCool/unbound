using System;
using System.Collections.Generic;
using Unity.Collections;
using Unbound.Box2D.Collision.Shapes;
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
        public int GetShapes(PhysicsShapeGroup2D physicsShapeGroup) => GetShapes(physicsShapeGroup, 0, int.MaxValue);

        /// <summary>The collider's shapes as Unity reports them: in its rigidbody's space (the world
        /// for colliders without one), polygons and edges without Box2D's skin radius, loops with
        /// their first vertex repeated at the end.</summary>
        public int GetShapes(PhysicsShapeGroup2D physicsShapeGroup, int shapeIndex, [Internal.DefaultValue("1")] int shapeCount = 1)
        {
            if (physicsShapeGroup == null) throw new ArgumentNullException(nameof(physicsShapeGroup));
            W.PrepareQuery();
            physicsShapeGroup.m_GroupState.ClearGeometry();
            var rb = m_AttachedBody;
            var space = rb != null ? (Matrix4x4)rb.localToWorldMatrix : Matrix4x4.identity;
            physicsShapeGroup.localToWorldMatrix = space;
            if (shapeIndex < 0 || shapeIndex >= m_Fixtures.Count) return 0;
            int last = (int)Math.Min((long)shapeIndex + shapeCount, m_Fixtures.Count);
            var points = new List<Vector2>();
            for (int i = shapeIndex; i < last; i++)
            {
                var f = m_Fixtures[i];
                var xf = f.Body.GetTransform();
                // Body frame to world, then into the group's space.
                Vector2 Out(System.Numerics.Vector2 local)
                {
                    var world = W.ToUnity(Unbound.Box2D.Common.MathUtils.Mul(xf, local));
                    return rb != null ? (Vector2)space.inverse.MultiplyPoint3x4(world) : world;
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
                        physicsShapeGroup.AddShape(PhysicsShapeType2D.Edges, Mathf.Max(0f, chain.Radius - B2Settings.PolygonRadius), points);
                        break;
                }
            }
            return physicsShapeGroup.shapeCount;
        }
    }
}
