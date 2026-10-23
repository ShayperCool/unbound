using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>
    /// Unity's primitive meshes (the built-in "Cube", "Sphere", "Capsule", "Cylinder", "Plane" and
    /// "Quad"), generated with their sizes, pivots, normals and UV layout: a 1 m cube; a sphere of
    /// radius 0.5; capsule and cylinder 2 m tall along Y with radius 0.5; a 10 m plane on XZ facing up
    /// (10x10 cells); a 1 m quad on XY facing -Z. Triangles wind clockwise seen from the front, as
    /// Unity's do. The tessellation of the round ones follows Unity's closely, not vertex for vertex.
    /// </summary>
    internal static class BuiltinMeshes
    {
        internal static Mesh Create(string name)
        {
            var b = new Builder();
            switch (name)
            {
                case "Cube": b.Cube(); break;
                case "Plane": b.Plane(10f, 10); break;
                case "Quad": b.Quad(); break;
                case "Sphere": b.Sphere(0.5f, 24, 16); break;
                case "Capsule": b.Capsule(0.5f, 2f, 24, 8); break;
                case "Cylinder": b.Cylinder(0.5f, 2f, 20); break;
                default: return null;
            }
            return b.ToMesh(name);
        }

        sealed class Builder
        {
            readonly List<Vector3> m_Vertices = new List<Vector3>();
            readonly List<Vector3> m_Normals = new List<Vector3>();
            readonly List<Vector2> m_Uvs = new List<Vector2>();
            readonly List<int> m_Triangles = new List<int>();

            int Vertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                m_Vertices.Add(position);
                m_Normals.Add(normal);
                m_Uvs.Add(uv);
                return m_Vertices.Count - 1;
            }

            /// <summary>A triangle facing its vertices' normals (clockwise seen from that side).</summary>
            void Triangle(int a, int b, int c)
            {
                var face = Vector3.Cross(m_Vertices[b] - m_Vertices[a], m_Vertices[c] - m_Vertices[a]);
                var normal = m_Normals[a] + m_Normals[b] + m_Normals[c];
                if (Vector3.Dot(face, normal) < 0f)
                {
                    m_Triangles.Add(a);
                    m_Triangles.Add(c);
                    m_Triangles.Add(b);
                }
                else
                {
                    m_Triangles.Add(a);
                    m_Triangles.Add(b);
                    m_Triangles.Add(c);
                }
            }

            void QuadFace(int a, int b, int c, int d)
            {
                Triangle(a, b, c);
                Triangle(a, c, d);
            }

            internal void Cube()
            {
                var normals = new[] { Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left };
                foreach (var n in normals)
                {
                    // The face's axes as seen from outside it.
                    var up = Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up;
                    var right = Vector3.Cross(up, -n);
                    var center = n * 0.5f;
                    int a = Vertex(center + (-right + up) * 0.5f, n, new Vector2(0f, 1f));
                    int b = Vertex(center + (right + up) * 0.5f, n, new Vector2(1f, 1f));
                    int c = Vertex(center + (right - up) * 0.5f, n, new Vector2(1f, 0f));
                    int d = Vertex(center + (-right - up) * 0.5f, n, new Vector2(0f, 0f));
                    QuadFace(a, b, c, d);
                }
            }

            internal void Plane(float size, int cells)
            {
                int row = cells + 1;
                for (int z = 0; z <= cells; z++)
                for (int x = 0; x <= cells; x++)
                {
                    float u = (float)x / cells, v = (float)z / cells;
                    // Unity's plane runs from +5 to -5 along both axes as its UVs go 0 to 1.
                    Vertex(new Vector3((0.5f - u) * size, 0f, (0.5f - v) * size), Vector3.up, new Vector2(u, v));
                }
                for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int i = z * row + x;
                    QuadFace(i, i + 1, i + row + 1, i + row);
                }
            }

            internal void Quad()
            {
                int a = Vertex(new Vector3(-0.5f, -0.5f, 0f), Vector3.back, new Vector2(0f, 0f));
                int b = Vertex(new Vector3(0.5f, -0.5f, 0f), Vector3.back, new Vector2(1f, 0f));
                int c = Vertex(new Vector3(-0.5f, 0.5f, 0f), Vector3.back, new Vector2(0f, 1f));
                int d = Vertex(new Vector3(0.5f, 0.5f, 0f), Vector3.back, new Vector2(1f, 1f));
                QuadFace(a, b, d, c);
            }

            /// <summary>A latitude-longitude surface of revolution about Y: rings from the bottom (0) to
            /// the top (1), each a point at a height and radius with its normal.</summary>
            void Revolve(IReadOnlyList<(float Y, float Radius, Vector2 Normal, float V)> profile, int segments)
            {
                int start = m_Vertices.Count;
                int row = segments + 1;
                foreach (var ring in profile)
                {
                    for (int s = 0; s <= segments; s++)
                    {
                        float u = (float)s / segments;
                        float angle = u * Mathf.PI * 2f;
                        float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                        var position = new Vector3(cos * ring.Radius, ring.Y, sin * ring.Radius);
                        var normal = new Vector3(cos * ring.Normal.x, ring.Normal.y, sin * ring.Normal.x).normalized;
                        Vertex(position, normal, new Vector2(u, ring.V));
                    }
                }
                for (int r = 0; r < profile.Count - 1; r++)
                for (int s = 0; s < segments; s++)
                {
                    int i = start + r * row + s;
                    int a = i, b = i + 1, c = i + row + 1, d = i + row;
                    // Skip the triangles that collapse at a pole.
                    if (profile[r].Radius > 1e-6f) Triangle(a, b, c);
                    if (profile[r + 1].Radius > 1e-6f) Triangle(a, c, d);
                }
            }

            internal void Sphere(float radius, int segments, int rings)
            {
                var profile = new List<(float, float, Vector2, float)>();
                for (int r = 0; r <= rings; r++)
                {
                    float v = (float)r / rings;
                    float theta = (v - 0.5f) * Mathf.PI;
                    var n = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                    profile.Add((n.y * radius, n.x * radius, n, v));
                }
                Revolve(profile, segments);
            }

            internal void Capsule(float radius, float height, int segments, int capRings)
            {
                float half = Mathf.Max(0f, height * 0.5f - radius);
                var profile = new List<(float, float, Vector2, float)>();
                for (int r = 0; r <= capRings; r++)
                {
                    float theta = (-1f + (float)r / capRings) * Mathf.PI * 0.5f;
                    var n = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                    float y = -half + n.y * radius;
                    profile.Add((y, n.x * radius, n, (y + height * 0.5f) / height));
                }
                for (int r = 0; r <= capRings; r++)
                {
                    float theta = (float)r / capRings * Mathf.PI * 0.5f;
                    var n = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
                    float y = half + n.y * radius;
                    profile.Add((y, n.x * radius, n, (y + height * 0.5f) / height));
                }
                Revolve(profile, segments);
            }

            internal void Cylinder(float radius, float height, int segments)
            {
                float h = height * 0.5f;
                Revolve(new List<(float, float, Vector2, float)>
                {
                    (-h, radius, new Vector2(1f, 0f), 0f),
                    (h, radius, new Vector2(1f, 0f), 1f),
                }, segments);
                Cap(h, radius, segments, Vector3.up);
                Cap(-h, radius, segments, Vector3.down);
            }

            void Cap(float y, float radius, int segments, Vector3 normal)
            {
                int center = Vertex(new Vector3(0f, y, 0f), normal, new Vector2(0.5f, 0.5f));
                int first = m_Vertices.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float angle = (float)s / segments * Mathf.PI * 2f;
                    float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                    Vertex(new Vector3(cos * radius, y, sin * radius), normal, new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f));
                }
                for (int s = 0; s < segments; s++) Triangle(center, first + s, first + s + 1);
            }

            internal Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.vertices = m_Vertices.ToArray();
                mesh.normals = m_Normals.ToArray();
                mesh.uv = m_Uvs.ToArray();
                mesh.triangles = m_Triangles.ToArray();
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }
        }
    }
}
