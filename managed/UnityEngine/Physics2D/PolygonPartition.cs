using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using V = System.Numerics.Vector2;

namespace Unbound.Runtime
{
    /// <summary>Convex polygons of at most eight vertices, preserving contour holes through
    /// odd-even filling. The optional refinement and polygon order are measured in
    /// tests/projects/geometryframe2d on Unity 6000.0.71f1.</summary>
    internal static class PolygonPartition
    {
        const int PolygonSize = Unbound.Box2D.Common.Settings.MaxPolygonVertices;

        // Remove nearly collinear intermediate vertices within .01 body units of
        // their chord, measured by tests/projects/geometryframe2d.
        const float CollinearCosine = .99999f;
        const float CollinearDistance = .01f;

        static void Clean(List<V> polygon)
        {
            int i = 0;
            while (polygon.Count >= 3 && i < polygon.Count)
            {
                var before = polygon[(i + polygon.Count - 1) % polygon.Count];
                var point = polygon[i];
                var after = polygon[(i + 1) % polygon.Count];
                var incoming = point - before;
                var outgoing = after - point;
                float product = incoming.Length() * outgoing.Length();
                var chord = after - before;
                float length = chord.Length();
                if (length >= Unbound.Box2D.Common.Settings.Epsilon) chord *= 1f / length;
                float distance = incoming.X * chord.Y - incoming.Y * chord.X;
                bool remove = product < Unbound.Box2D.Common.Settings.Epsilon ||
                    V.Dot(incoming, outgoing) / product > CollinearCosine && distance <= CollinearDistance;
                if (remove) polygon.RemoveAt(i);
                else i++;
            }
        }

        [DllImport("unbound_physx", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe int ua_tess_polygons(float* points, int* counts, int contours, int oddEven,
            int delaunay, int polygonSize, float* vertices, int* vertexIndices, int vertexCapacity,
            int* elements, int elementCapacity, int* vertexCount, int* elementCount);

        internal static unsafe bool TryGenerate(IReadOnlyList<IReadOnlyList<V>> paths, bool delaunay,
            out List<List<V>> result)
        {
            result = new List<List<V>>();
            int pointCount = 0;
            foreach (var path in paths) pointCount = checked(pointCount + path.Count);
            if (pointCount == 0) return true;
            var input = new float[checked(pointCount * 2)];
            var counts = new int[paths.Count];
            int offset = 0;
            for (int i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                counts[i] = path.Count;
                foreach (var point in path)
                {
                    if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return false;
                    input[offset++] = point.X;
                    input[offset++] = point.Y;
                }
            }
            int vertexCount = 0, elementCount = 0;
            fixed (float* points = input)
            fixed (int* contourCounts = counts)
            {
                int status = ua_tess_polygons(points, contourCounts, counts.Length, 1, delaunay ? 1 : 0,
                    PolygonSize, null, null, 0, null, 0, &vertexCount, &elementCount);
                if (status < 0) return false;
                if (elementCount == 0) return true;
                var vertices = new float[checked(vertexCount * 2)];
                var elements = new int[checked(elementCount * PolygonSize)];
                fixed (float* outputVertices = vertices)
                fixed (int* outputElements = elements)
                {
                    status = ua_tess_polygons(points, contourCounts, counts.Length, 1, delaunay ? 1 : 0,
                        PolygonSize, outputVertices, null, vertexCount, outputElements, elementCount,
                        &vertexCount, &elementCount);
                    if (status != 0) return false;
                }
                for (int i = 0; i < elementCount; i++)
                {
                    var polygon = new List<V>(PolygonSize);
                    for (int j = 0; j < PolygonSize; j++)
                    {
                        int index = elements[i * PolygonSize + j];
                        if (index < 0) break;
                        polygon.Add(new V(vertices[index * 2], vertices[index * 2 + 1]));
                    }
                    Clean(polygon);
                    if (polygon.Count >= 3) result.Add(polygon);
                }
            }
            return true;
        }
    }
}
