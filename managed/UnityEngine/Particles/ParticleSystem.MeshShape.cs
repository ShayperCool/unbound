using System;
using System.Collections.Generic;

namespace UnityEngine
{
    // The Shape module's Mesh, MeshRenderer and SkinnedMeshRenderer shapes. Documented (Unity
    // 6000.0 ShapeModule, ParticleSystemMeshShapeType): Vertex emits from the vertices, Edge from
    // the edges, Triangle from the surface; meshSpawnMode/Spread/Speed, useMeshMaterialIndex,
    // useMeshColors (vertex colours, else the material's _Color or _TintColor) and normalOffset.
    // Everything below is measured in the 6000.0.71f1 Editor (tests/projects/particlemeshshape):
    // - Triangles are picked by area with one shape draw. Triangle then takes two more draws
    //   (u, v), folded into the triangle when u + v > 1, as the weights of the first two corners
    //   (the third gets 1 - u - v). Edge reuses the area draw: three times its position within
    //   the picked triangle selects the edge (a→b, b→c, c→a) and the point along it. Vertex takes
    //   two whole draw words: a triangle uniformly by count (degenerate ones included), then one
    //   of its corners.
    // - Loop and Ping-Pong: Edge moves the area draw with the spawn speed and snaps it to the
    //   spread; Vertex walks the vertices in buffer order, one per particle (speed and spread do
    //   not matter); Triangle stays random.
    // - The direction is the interpolated vertex normal, normalized, +Z without normals;
    //   normalOffset moves along the interpolated normal as it is (shorter inside a curved
    //   triangle), before the shape's own transform.
    // - Use Mesh Colors blends the vertex colours with byte weights; without vertex
    //   colours a renderer's particle takes the colour of the material of the submesh it came from.
    // - Renderer shapes sample the renderer's mesh (a skinned renderer's current pose) scaled by
    //   the renderer, in the frame of its position and rotation: local simulation space keeps
    //   that frame's coordinates, world space places the particle with the renderer. The area
    //   weights are those of the unscaled mesh, a skinned renderer's in its rest pose.
    // - A mesh without triangles logs "not valid" and a surface without area (also no mesh)
    //   logs "zero surface area" for Edge and Triangle; such a system emits at the shape's origin.
    //   Single Material past the last submesh uses the last one.
    public sealed partial class ParticleSystem
    {
        /// <summary>The colour the start colour is multiplied by (Use Mesh Colors), when
        /// <see cref="m_ShapeColorSet"/>; set by the last shape sample.</summary>
        Color m_ShapeColor;
        bool m_ShapeColorSet;
        /// <summary>A renderer shape's frame (the renderer's position and rotation), set by the
        /// last shape sample: the sample's coordinates are in it.</summary>
        bool m_ShapeFrameSet;
        Vector3 m_ShapeFramePosition;
        Quaternion m_ShapeFrameRotation;
        /// <summary>The next vertex of the Vertex type's Loop and Ping-Pong walk (per playback).</summary>
        int m_MeshVertexCursor;
        /// <summary>The particle being sampled fills a batch lane that is discarded.</summary>
        bool m_ShapeSampleDiscarded;
        MeshShapeTable m_MeshShapeTable;
        HashSet<(int, MeshShapeProblem)> m_MeshShapeWarnings;
        static readonly int s_ColorId = Shader.PropertyToID("_Color");
        static readonly int s_TintColorId = Shader.PropertyToID("_TintColor");

        enum MeshShapeProblem { NotValid, ZeroSurface, NotTriangles }

        /// <summary>A mesh prepared for sampling, and what it was prepared from: a table is used
        /// again only while all of that is unchanged.</summary>
        sealed class MeshShapeTable
        {
            internal Mesh Mesh;
            internal Vector3[] SourceVertices, SourceNormals;
            internal Color32[] SourceColors;
            internal (int[] Indices, int BaseVertex, MeshTopology Topology)[] SourceSubMeshes;
            internal int SyncVersion;
            internal Matrix4x4 ToLocal;
            internal Vector3 Scale;
            /// <summary>A skinned renderer's rest-pose vertices, whose areas weigh the triangles.</summary>
            internal Vector3[] RestVertices;
            internal int SubMesh;
            /// <summary>Built while the mesh had unsent changes: valid for the frame it was built
            /// in only (in-place vertex buffer writes keep the arrays), so a mesh that is never
            /// drawn is prepared at most once a frame, not once a particle.</summary>
            internal bool BuiltDirty;
            internal int BuiltFrame;

            internal Vector3[] Vertices, Normals;
            internal Color32[] Colors;
            /// <summary>Triangle corners of the triangle submeshes (the selected one with Single
            /// Material), degenerate triangles included.</summary>
            internal int[] Triangles;
            /// <summary>Per submesh in <see cref="SubMeshes"/>, the end of its triangles.</summary>
            internal int[] SubMeshTriangleEnds;
            internal int[] SubMeshes;
            /// <summary>Running sums of the triangle areas.</summary>
            internal float[] AreaEnds;
            /// <summary>Some (selected) submesh is not made of triangles.</summary>
            internal bool SkippedTopology;

            internal int TriangleCount => Triangles.Length / 3;
            internal float TotalArea => AreaEnds.Length > 0 ? AreaEnds[AreaEnds.Length - 1] : 0f;

            internal bool Matches(Mesh mesh, Matrix4x4 toLocal, Vector3 scale, int subMesh, int frame)
            {
                if (!ReferenceEquals(Mesh, mesh) || !ReferenceEquals(SourceVertices, mesh.m_Vertices) || !ReferenceEquals(SourceNormals, mesh.m_Normals)
                    || !ReferenceEquals(SourceColors, mesh.m_Colors) || SyncVersion != mesh.m_SyncVersion || SubMesh != subMesh || !ToLocal.Equals(toLocal) || Scale != scale
                    || SourceSubMeshes.Length != mesh.m_SubMeshes.Count)
                    return false;
                if (mesh.m_Dirty && !(BuiltDirty && BuiltFrame == frame)) return false;
                for (int s = 0; s < SourceSubMeshes.Length; s++)
                {
                    var sm = mesh.m_SubMeshes[s];
                    var built = SourceSubMeshes[s];
                    if (!ReferenceEquals(built.Indices, sm.Indices) || built.BaseVertex != sm.BaseVertex || built.Topology != sm.Topology) return false;
                }
                return true;
            }
        }

        static bool IsMeshShape(ParticleSystemShapeType type) =>
            type == ParticleSystemShapeType.Mesh || type == ParticleSystemShapeType.MeshRenderer || type == ParticleSystemShapeType.SkinnedMeshRenderer;

        /// <summary>A point of the mesh with its normal, in the mesh's frame (see the notes above);
        /// the shape's origin and +Z when there is nothing to emit from.</summary>
        void SampleMeshShape(out Vector3 pos, out Vector3 dir)
        {
            var S = m_Shape;
            pos = Vector3.zero;
            dir = Vector3.forward;
            var mesh = MeshShapeSource(out var toLocal, out var scale, out var renderer, out var restVertices);
            if (mesh == null)
            {
                if (S.meshShapeType != ParticleSystemMeshShapeType.Vertex) WarnMeshShape(null, MeshShapeProblem.ZeroSurface);
                return;
            }
            var t = MeshShapeTableFor(mesh, toLocal, scale, restVertices);
            if (t.TriangleCount == 0)
            {
                WarnMeshShape(mesh, MeshShapeProblem.NotValid);
                if (S.meshShapeType != ParticleSystemMeshShapeType.Vertex) WarnMeshShape(mesh, MeshShapeProblem.ZeroSurface);
                return;
            }
            if (t.SkippedTopology) WarnMeshShape(mesh, MeshShapeProblem.NotTriangles);
            if (!SampleMeshTable(t, out pos, out var normal, out var meshColor, out int triangle))
            {
                WarnMeshShape(mesh, MeshShapeProblem.ZeroSurface);
                return;
            }
            dir = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.forward;
            pos += normal * S.normalOffset;
            if (S.useMeshColors)
            {
                if (t.Colors != null)
                {
                    m_ShapeColor = meshColor;
                    m_ShapeColorSet = true;
                }
                else if (renderer != null)
                {
                    // The material of the particle's submesh (Single Material: its slot).
                    int slot = S.useMeshMaterialIndex ? S.meshMaterialIndex : triangle >= 0 ? MeshShapeSubMesh(t, triangle) : 0;
                    var materials = renderer.m_Materials;
                    m_ShapeColorSet = slot >= 0 && slot < materials.Length && MaterialShapeColor(materials[slot], out m_ShapeColor);
                }
            }
        }

        /// <summary>The submesh a triangle of the table belongs to.</summary>
        static int MeshShapeSubMesh(MeshShapeTable t, int triangle)
        {
            var ends = t.SubMeshTriangleEnds;
            for (int i = 0; i < ends.Length; i++)
                if (triangle < ends[i]) return t.SubMeshes[i];
            return 0;
        }

        /// <summary>The mesh the shape emits from; the matrix from its vertices into the renderer's
        /// unscaled space (identity unless the renderer's current pose is a deformed copy) and
        /// the renderer's scale (sets <see cref="m_ShapeFrameSet"/> for renderers); the renderer,
        /// whose materials Use Mesh Colors falls back to.</summary>
        Mesh MeshShapeSource(out Matrix4x4 toLocal, out Vector3 scale, out Renderer renderer, out Vector3[] restVertices)
        {
            var S = m_Shape;
            restVertices = null;
            toLocal = Matrix4x4.identity;
            scale = Vector3.one;
            renderer = null;
            Mesh mesh = null;
            switch (S.shapeType)
            {
                case ParticleSystemShapeType.Mesh:
                    return S.mesh;
                case ParticleSystemShapeType.MeshRenderer:
                    if (S.meshRenderer != null)
                    {
                        renderer = S.meshRenderer;
                        var filter = S.meshRenderer.GetComponent<MeshFilter>();
                        mesh = filter != null ? filter.m_Mesh : null;
                    }
                    break;
                case ParticleSystemShapeType.SkinnedMeshRenderer:
                    if (S.skinnedMeshRenderer is SkinnedMeshRenderer skinned && skinned != null)
                    {
                        renderer = skinned;
                        // The current pose: the (skinned, blend-shaped or cloth) mesh the renderer
                        // draws this frame, brought back into the renderer's space.
                        mesh = skinned.DrawGeometry(out var model);
                        var shared = skinned.sharedMesh;
                        if (mesh != null && shared != null && !ReferenceEquals(mesh, shared))
                        {
                            var localToWorld = skinned.transform.localToWorldMatrix;
                            if (!model.Equals(localToWorld)) toLocal = skinned.transform.worldToLocalMatrix * model;
                            if (shared.m_Vertices.Length == mesh.m_Vertices.Length) restVertices = shared.m_Vertices;
                        }
                    }
                    break;
            }
            if (renderer == null) return null;
            var rt = renderer.transform;
            scale = rt.lossyScale;
            m_ShapeFrameSet = true;
            m_ShapeFramePosition = rt.position;
            m_ShapeFrameRotation = rt.rotation;
            return mesh;
        }

        /// <summary>Use Mesh Colors without vertex colours: the material's "_Color", else its
        /// "_TintColor" (the documented fallback); nothing without either.</summary>
        static bool MaterialShapeColor(Material material, out Color color)
        {
            color = Color.white;
            if (material == null) return false;
            if (material.HasProperty(s_ColorId)) { color = material.GetColor(s_ColorId); return true; }
            if (material.HasProperty(s_TintColorId)) { color = material.GetColor(s_TintColorId); return true; }
            return false;
        }

        MeshShapeTable MeshShapeTableFor(Mesh mesh, Matrix4x4 toLocal, Vector3 scale, Vector3[] restVertices)
        {
            var S = m_Shape;
            // Single Material past the last submesh uses the last one (measured).
            int subMesh = S.useMeshMaterialIndex ? Mathf.Clamp(S.meshMaterialIndex, 0, Math.Max(mesh.m_SubMeshes.Count - 1, 0)) : -1;
            int frame = Time.frameCount;
            var table = m_MeshShapeTable;
            if (table != null && table.Matches(mesh, toLocal, scale, subMesh, frame) && ReferenceEquals(table.RestVertices, restVertices)) return table;
            table = BuildMeshShapeTable(mesh, toLocal, scale, subMesh, restVertices);
            table.BuiltDirty = mesh.m_Dirty;
            table.BuiltFrame = frame;
            m_MeshShapeTable = table;
            return table;
        }

        static MeshShapeTable BuildMeshShapeTable(Mesh mesh, Matrix4x4 toLocal, Vector3 scale, int subMesh, Vector3[] restVertices)
        {
            var source = mesh.m_Vertices;
            int n = source.Length;
            var t = new MeshShapeTable
            {
                Mesh = mesh, SourceVertices = source, SourceNormals = mesh.m_Normals, SourceColors = mesh.m_Colors,
                SyncVersion = mesh.m_SyncVersion, ToLocal = toLocal, Scale = scale, SubMesh = subMesh, RestVertices = restVertices,
                SourceSubMeshes = new (int[], int, MeshTopology)[mesh.m_SubMeshes.Count],
            };
            // Areas weigh the unscaled (local) mesh; positions and normals take the scale.
            var local = source;
            if (!toLocal.isIdentity)
            {
                local = new Vector3[n];
                for (int i = 0; i < n; i++) local[i] = toLocal.MultiplyPoint3x4(source[i]);
            }
            var toFrame = Matrix4x4.Scale(scale) * toLocal;
            bool identity = toFrame.isIdentity;
            if (scale == Vector3.one) t.Vertices = local;
            else
            {
                t.Vertices = new Vector3[n];
                for (int i = 0; i < n; i++) t.Vertices[i] = Vector3.Scale(local[i], scale);
            }
            var normals = mesh.m_Normals;
            if (normals != null && normals.Length == n)
            {
                if (identity) t.Normals = normals;
                else
                {
                    t.Normals = new Vector3[n];
                    for (int i = 0; i < n; i++) t.Normals[i] = toFrame.MultiplyVector(normals[i]);
                }
            }
            var colors = mesh.m_Colors;
            if (colors != null && colors.Length == n) t.Colors = colors;

            int count = 0, parts = 0;
            for (int s = 0; s < mesh.m_SubMeshes.Count; s++)
            {
                var sm = mesh.m_SubMeshes[s];
                t.SourceSubMeshes[s] = (sm.Indices, sm.BaseVertex, sm.Topology);
                if (subMesh >= 0 && s != subMesh) continue;
                if (sm.Topology == MeshTopology.Triangles)
                {
                    count += sm.Indices.Length / 3;
                    parts++;
                }
                else if (sm.Indices.Length > 0) t.SkippedTopology = true;
            }
            var triangles = new int[count * 3];
            t.SubMeshTriangleEnds = new int[parts];
            t.SubMeshes = new int[parts];
            int k = 0, part = 0;
            for (int s = 0; s < mesh.m_SubMeshes.Count; s++)
            {
                var sm = mesh.m_SubMeshes[s];
                if (subMesh >= 0 && s != subMesh || sm.Topology != MeshTopology.Triangles) continue;
                var indices = sm.Indices;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i] + sm.BaseVertex, b = indices[i + 1] + sm.BaseVertex, c = indices[i + 2] + sm.BaseVertex;
                    if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                    triangles[k++] = a;
                    triangles[k++] = b;
                    triangles[k++] = c;
                }
                t.SubMeshes[part] = s;
                t.SubMeshTriangleEnds[part++] = k / 3;
            }
            if (k != triangles.Length) Array.Resize(ref triangles, k);
            t.Triangles = triangles;
            t.AreaEnds = new float[k / 3];
            var weighed = restVertices ?? local;
            float area = 0f;
            for (int i = 0; i < t.AreaEnds.Length; i++)
            {
                var a = weighed[triangles[i * 3]];
                area += 0.5f * Vector3.Cross(weighed[triangles[i * 3 + 1]] - a, weighed[triangles[i * 3 + 2]] - a).magnitude;
                t.AreaEnds[i] = area;
            }
            return t;
        }

        /// <summary>A point of the mesh by the shape's mesh type and spawn mode, in the mesh's
        /// frame, with its interpolated normal (not normalized), its colour (when the mesh has
        /// vertex colours) and the triangle it came from (-1 for a vertex walk); false when the
        /// type needs a surface and the triangles have no area.</summary>
        bool SampleMeshTable(MeshShapeTable t, out Vector3 pos, out Vector3 normal, out Color color, out int triangle)
        {
            var S = m_Shape;
            var T = t.Triangles;
            bool walk = S.meshSpawnMode == ParticleSystemShapeMultiModeValue.Loop || S.meshSpawnMode == ParticleSystemShapeMultiModeValue.PingPong;
            int a, b, c;
            float wa, wb, wc;
            switch (S.meshShapeType)
            {
                case ParticleSystemMeshShapeType.Vertex:
                {
                    if (walk)
                    {
                        // One vertex per emitted particle in buffer order: Loop wraps, Ping-Pong
                        // turns back at each end, visiting it twice. A lane the batch discards
                        // does not move the walk.
                        int n = t.Vertices.Length;
                        int step = m_MeshVertexCursor % (2 * n);
                        if (!m_ShapeSampleDiscarded) m_MeshVertexCursor = step + 1;
                        a = S.meshSpawnMode == ParticleSystemShapeMultiModeValue.Loop ? step % n : step < n ? step : 2 * n - 1 - step;
                        triangle = -1;
                    }
                    else
                    {
                        triangle = (int)(ShapeWord() % (uint)t.TriangleCount);
                        a = T[triangle * 3 + (int)(ShapeWord() % 3u)];
                    }
                    b = c = a;
                    wa = 1f;
                    wb = wc = 0f;
                    break;
                }
                case ParticleSystemMeshShapeType.Edge:
                {
                    float u;
                    if (walk)
                    {
                        float travel = S.meshSpawnSpeed.Evaluate(0f, 1f) * m_ShapeTime;
                        u = S.meshSpawnMode == ParticleSystemShapeMultiModeValue.Loop ? Frac(travel) : Mathf.PingPong(travel, 1f);
                        if (S.meshSpawnSpread > 0f) u = Mathf.Floor(u / S.meshSpawnSpread) * S.meshSpawnSpread;
                    }
                    else u = ShapeValue();
                    if (!MeshShapeTriangle(t, u, out triangle, out float within))
                    {
                        pos = normal = default;
                        color = default;
                        return false;
                    }
                    // Three times the draw's place in the triangle's share: the edge, then the
                    // point along it.
                    float f = within * 3f;
                    int e = Math.Min((int)f, 2);
                    f -= e;
                    a = T[triangle * 3 + e];
                    b = c = T[triangle * 3 + (e + 1) % 3];
                    wa = 1f - f;
                    wb = f;
                    wc = 0f;
                    break;
                }
                default:
                {
                    if (!MeshShapeTriangle(t, ShapeValue(), out triangle, out _))
                    {
                        pos = normal = default;
                        color = default;
                        return false;
                    }
                    wa = ShapeValue();
                    wb = ShapeValue();
                    if (wa + wb > 1f)
                    {
                        wa = 1f - wa;
                        wb = 1f - wb;
                    }
                    wc = 1f - wa - wb;
                    a = T[triangle * 3];
                    b = T[triangle * 3 + 1];
                    c = T[triangle * 3 + 2];
                    break;
                }
            }
            var V = t.Vertices;
            pos = V[a] * wa + V[b] * wb + V[c] * wc;
            var N = t.Normals;
            normal = N != null ? N[a] * wa + N[b] * wb + N[c] * wc : Vector3.forward;
            color = t.Colors != null ? MeshShapeColor(t.Colors[a], t.Colors[b], t.Colors[c], wa, wb, wc) : Color.white;
            return true;
        }

        /// <summary>The vertex colours blended with the weights in 1/255 steps, truncated, and the
        /// sum rounded to a byte (measured: red, green and blue exact; alpha within 1).</summary>
        static Color MeshShapeColor(Color32 a, Color32 b, Color32 c, float wa, float wb, float wc)
        {
            int ia = (int)(wa * 255f), ib = (int)(wb * 255f), ic = (int)(wc * 255f);
            return new Color32(Blend(a.r, b.r, c.r), Blend(a.g, b.g, c.g), Blend(a.b, b.b, c.b), Blend(a.a, b.a, c.a));

            byte Blend(byte x, byte y, byte z) => (byte)((x * ia + y * ib + z * ic + 127) / 255);
        }

        /// <summary>The triangle a draw picks by area, and where in its share the draw lies (0..1);
        /// false when the triangles have no area.</summary>
        static bool MeshShapeTriangle(MeshShapeTable t, float u, out int triangle, out float within)
        {
            var ends = t.AreaEnds;
            float total = t.TotalArea;
            triangle = 0;
            within = 0f;
            if (!(total > 0f)) return false;
            float x = u * total;
            int lo = 0, hi = ends.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (ends[mid] > x) hi = mid;
                else lo = mid + 1;
            }
            // Past the end (u = 1): the last triangle with an area.
            while (lo > 0 && ends[lo] == ends[lo - 1]) lo--;
            float start = lo > 0 ? ends[lo - 1] : 0f;
            within = Mathf.Clamp01((x - start) / (ends[lo] - start));
            triangle = lo;
            return true;
        }

        void WarnMeshShape(Mesh mesh, MeshShapeProblem problem)
        {
            m_MeshShapeWarnings ??= new HashSet<(int, MeshShapeProblem)>();
            if (!m_MeshShapeWarnings.Add((mesh != null ? mesh.GetInstanceID() : 0, problem))) return;
            switch (problem)
            {
                // The messages the 6000.0.71f1 Editor logs.
                case MeshShapeProblem.NotValid:
                    Debug.LogWarning("Mesh used in Particle System Shape Module is not valid, possibly due to missing read/write flag", this);
                    break;
                case MeshShapeProblem.ZeroSurface:
                    Debug.LogWarning($"Particle System '{name}' is trying to spawn on a mesh with zero surface area", this);
                    break;
                default:
                    Debug.LogWarning($"Unbound: the shape of particle system '{name}': mesh '{mesh.name}' has submeshes that are not triangles; emitting from its triangle submeshes only.", this);
                    break;
            }
        }
    }
}
