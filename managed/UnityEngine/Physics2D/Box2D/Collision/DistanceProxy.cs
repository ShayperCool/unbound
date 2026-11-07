using System;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Numerics;
using System.Runtime.CompilerServices;
using Unbound.Box2D.Collision.Shapes;

namespace Unbound.Box2D.Collision
{
    /// A distance proxy is used by the GJK algorithm.
    /// It encapsulates any shape.
    internal struct DistanceProxy
    {
        /// Initialize the proxy using the given shape. The shape
        /// must remain in scope while the proxy is in use.
        public void Set(Shape shape, int index)
        {
            switch (shape)
            {
            case CircleShape circle:
            {
                Vertices = new VertexStorage(circle.Position, default);
                Count = 1;
                Radius = circle.Radius;
            }
                break;

            case PolygonShape polygon:
            {
                Vertices = new VertexStorage(polygon.Vertices);
                Count = polygon.Count;
                Radius = polygon.Radius;
            }
                break;

            case ChainShape chain:
            {
                Debug.Assert(0 <= index && index < chain.Count);
                Count = 2;
                Vertices = new VertexStorage(chain.Vertices[index],
                    chain.Vertices[index + 1 < chain.Count ? index + 1 : 0]);

                Radius = chain.Radius;
            }
                break;

            case EdgeShape edge:
            {
                Vertices = new VertexStorage(edge.Vertex1, edge.Vertex2);
                Count = 2;
                Radius = edge.Radius;
            }
                break;

            default:
                throw new NotSupportedException();
            }
        }

        /// Initialize the proxy using a vertex cloud and radius. The vertices
        /// must remain in scope while the proxy is in use.
        public void Set(Vector2[] vertices, int count, float radius)
        {
            var copy = new Vector2[vertices.Length];
            Array.Copy(vertices, copy, vertices.Length);
            Vertices = new VertexStorage(copy);
            Count = count;
            Radius = radius;
        }

        /// Get the supporting vertex index in the given direction.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Pure]
        public int GetSupport(in Vector2 d)
        {
            var bestIndex = 0;
            var bestValue = Vector2.Dot(Vertices[0], d);
            for (var i = 1; i < Count; ++i)
            {
                var value = Vector2.Dot(Vertices[i], d);
                if (value > bestValue)
                {
                    bestIndex = i;
                    bestValue = value;
                }
            }

            return bestIndex;
        }

        /// Get the supporting vertex in the given direction.
        public Vector2 GetSupportVertex(in Vector2 d)
        {
            var bestIndex = 0;
            var bestValue = Vector2.Dot(Vertices[0], d);
            for (var i = 1; i < Count; ++i)
            {
                var value = Vector2.Dot(Vertices[i], d);
                if (value > bestValue)
                {
                    bestIndex = i;
                    bestValue = value;
                }
            }

            return Vertices[bestIndex];
        }

        /// Get the vertex count.
        public int GetVertexCount()
        {
            return Count;
        }

        /// Get a vertex by index. Used by b2Distance.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Pure]
        public Vector2 GetVertex(int index)
        {
            Debug.Assert(0 <= index && index < Count);
            return Vertices[index];
        }

        // Small shapes keep their support points in the proxy value. Polygon storage stays
        // borrowed for the lifetime of the query, avoiding per-cast circle and edge arrays.
        internal readonly struct VertexStorage
        {
            readonly Vector2[] array;
            readonly Vector2 first, second;
            internal VertexStorage(Vector2[] array) { this.array = array; first = second = default; }
            internal VertexStorage(Vector2 first, Vector2 second) { array = null; this.first = first; this.second = second; }
            public Vector2 this[int index] => array != null ? array[index] : index == 0 ? first : second;
        }

        public VertexStorage Vertices;

        public int Count;

        public float Radius;
    }

    internal class GJkProfile
    {
        // GJK using Voronoi regions (Christer Ericson) and Barycentric coordinates.
        public int GjkCalls;

        public int GjkIters;

        public int GjkMaxIters;
    }
}