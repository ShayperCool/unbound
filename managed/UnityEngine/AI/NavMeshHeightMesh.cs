using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// One of a NavMesh data's baked height meshes (Build Height Mesh: the data's m_HeightMeshes):
    /// triangles of the level's geometry under the NavMesh with a bounding volume tree, in the data's
    /// own frame. A point placed on the NavMesh takes its height from them: the highest part of any
    /// triangle within a square of the build's voxel size around the point, among those lower than
    /// the point plus the agent climb (Unity 6000.0.71f1: tests/projects/navbaked `height`,
    /// `height.agents`).
    /// </summary>
    internal sealed class NavMeshHeightMesh
    {
        /// <summary>The smallest half side of the square the height is looked for in.</summary>
        internal const float MinReach = 0.001f;

        struct Node
        {
            internal float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
            /// <summary>A leaf's first triangle (with <see cref="Count"/> of them), or minus how many
            /// nodes the subtree of an inner node holds (the step past it).</summary>
            internal int First, Count;
        }

        readonly float[] m_Vertices;
        readonly int[] m_Indices;
        readonly Node[] m_Nodes;
        readonly float m_MinX, m_MinY, m_MinZ, m_MaxX, m_MaxY, m_MaxZ;

        NavMeshHeightMesh(float[] vertices, int[] indices, Node[] nodes, Vector3 center, Vector3 extent)
        {
            m_Vertices = vertices;
            m_Indices = indices;
            m_Nodes = nodes;
            m_MinX = center.x - extent.x;
            m_MinY = center.y - extent.y;
            m_MinZ = center.z - extent.z;
            m_MaxX = center.x + extent.x;
            m_MaxY = center.y + extent.y;
            m_MaxZ = center.z + extent.z;
        }

        /// <summary>A HeightMeshData of a serialized NavMeshData; null when it has no triangles or its
        /// indices reach past its vertices.</summary>
        internal static NavMeshHeightMesh Read(YNode node)
        {
            var vertices = new List<float>();
            foreach (var v in node.Get("m_Vertices")?.Seq() ?? Array.Empty<YNode>())
            {
                vertices.Add(v.Float("x"));
                vertices.Add(v.Float("y"));
                vertices.Add(v.Float("z"));
            }
            var indices = new List<int>();
            foreach (var i in node.Get("m_Indices")?.Seq() ?? Array.Empty<YNode>())
                indices.Add(i.AsInt());
            int count = vertices.Count / 3;
            if (indices.Count < 3) return null;
            foreach (int i in indices)
                if (i < 0 || i >= count) return null;
            var nodes = new List<Node>();
            foreach (var n in node.Get("m_Nodes")?.Seq() ?? Array.Empty<YNode>())
            {
                var min = n.Vector3("min");
                var max = n.Vector3("max");
                nodes.Add(new Node
                {
                    MinX = min.x, MinY = min.y, MinZ = min.z, MaxX = max.x, MaxY = max.y, MaxZ = max.z,
                    First = n.Int("i"), Count = n.Int("n"),
                });
            }
            // A tree whose leaves reach past the triangles is not used (they are searched one by one).
            foreach (var n in nodes)
                if (n.First >= 0 && (n.First + n.Count) * 3 > indices.Count) { nodes.Clear(); break; }
            var bounds = node.Get("m_Bounds");
            Vector3 center, extent;
            if (bounds != null && !bounds.IsNull)
            {
                center = bounds.Vector3("m_Center");
                extent = bounds.Vector3("m_Extent");
            }
            else
            {
                // No bounds written: those of its vertices.
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = -min;
                for (int v = 0; v + 2 < vertices.Count; v += 3)
                {
                    var p = new Vector3(vertices[v], vertices[v + 1], vertices[v + 2]);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                center = (min + max) * 0.5f;
                extent = (max - min) * 0.5f;
            }
            return new NavMeshHeightMesh(vertices.ToArray(), indices.ToArray(), nodes.ToArray(), center, extent);
        }

        /// <summary>Raises <paramref name="best"/> to the highest part, lower than <paramref name="top"/>,
        /// of the triangles within <paramref name="reach"/> of (x, z) along x and z.</summary>
        internal void Highest(float x, float z, float reach, float top, ref float best)
        {
            float x0 = x - reach, x1 = x + reach, z0 = z - reach, z1 = z + reach;
            if (m_MinZ > z1 || m_MinY > top || x0 > m_MaxX || m_MinX > x1 || best > m_MaxY || z0 > m_MaxZ) return;
            if (m_Nodes.Length == 0)
            {
                for (int t = 0; t + 2 < m_Indices.Length; t += 3) Triangle(t, x0, x1, z0, z1, top, ref best);
                return;
            }
            int index = 0;
            while (index < m_Nodes.Length)
            {
                ref readonly var node = ref m_Nodes[index];
                bool leaf = node.First >= 0;
                bool overlap = !(node.MaxX < x0 || x1 < node.MinX || node.MaxY < best || top < node.MinY || node.MaxZ < z0 || z1 < node.MinZ);
                if (overlap && leaf)
                {
                    int end = (node.First + node.Count) * 3;
                    for (int t = node.First * 3; t < end; t += 3) Triangle(t, x0, x1, z0, z1, top, ref best);
                }
                index += overlap || leaf ? 1 : -node.First;
            }
        }

        // Sutherland-Hodgman clipping of one triangle to the square, in three scratch polygons of at
        // most 7 corners (a triangle cut by four lines).
        [ThreadStatic] static float[] s_A, s_B;

        void Triangle(int t, float x0, float x1, float z0, float z1, float top, ref float best)
        {
            var a = s_A ??= new float[3 * 8];
            var b = s_B ??= new float[3 * 8];
            for (int k = 0; k < 3; k++)
            {
                int v = m_Indices[t + k] * 3;
                a[k * 3] = m_Vertices[v];
                a[k * 3 + 1] = m_Vertices[v + 1];
                a[k * 3 + 2] = m_Vertices[v + 2];
            }
            int n = 3;
            n = Clip(a, n, b, 0, 1f, -x0);
            if (n < 3) return;
            n = Clip(b, n, a, 0, -1f, x1);
            if (n < 3) return;
            n = Clip(a, n, b, 2, 1f, -z0);
            if (n < 3) return;
            n = Clip(b, n, a, 2, -1f, z1);
            if (n < 3) return;
            float h = float.NegativeInfinity;
            for (int k = 0; k < n; k++) h = Mathf.Max(h, a[k * 3 + 1]);
            if (h < top && h > best) best = h;
        }

        /// <summary>The part of polygon <paramref name="src"/> where sign * p[axis] + offset >= 0.</summary>
        static int Clip(float[] src, int n, float[] dst, int axis, float sign, float offset)
        {
            int m = 0;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float dj = sign * src[j * 3 + axis] + offset;
                float di = sign * src[i * 3 + axis] + offset;
                bool inJ = dj >= 0f, inI = di >= 0f;
                if (inJ != inI)
                {
                    float s = dj / (dj - di);
                    for (int c = 0; c < 3; c++) dst[m * 3 + c] = src[j * 3 + c] + (src[i * 3 + c] - src[j * 3 + c]) * s;
                    m++;
                }
                if (inI)
                {
                    for (int c = 0; c < 3; c++) dst[m * 3 + c] = src[i * 3 + c];
                    m++;
                }
            }
            return m;
        }
    }
}
