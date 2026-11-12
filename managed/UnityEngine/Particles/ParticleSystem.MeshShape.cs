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
    // - Lines and line strips emit too, each segment weighed by its length where a triangle
    //   weighs its area. Triangle takes a segment as the triangle (a, b, b). A mesh with line
    //   submeshes samples every entry as a segment in the other types: Edge at the area draw's
    //   place along a→b, Vertex at one of a, b (a whole draw word modulo 2); measured with a
    //   triangle submesh before a lines submesh, which also asserts that the submeshes mix
    //   primitive types. Points and quads emit nothing.
    // - A mesh without triangles or segments logs "not valid" and a surface without area (also
    //   no mesh) logs "zero surface area" for Edge and Triangle; such a system emits at the
    //   shape's origin. Single Material past the last submesh uses the last one.
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
        internal MeshShapeTable m_MeshShapeTable;
        HashSet<(int, MeshShapeProblem)> m_MeshShapeWarnings;
        static readonly int s_ColorId = Shader.PropertyToID("_Color");
        static readonly int s_TintColorId = Shader.PropertyToID("_TintColor");

        enum MeshShapeProblem { NotValid, ZeroSurface, MixedTopology }

        /// <summary>A mesh prepared for sampling, and what it was prepared from. Its two parts are
        /// kept apart: the triangles with their area weights change only with the topology (a
        /// skinned renderer's rest mesh), the positions, normals and colours with every pose; a
        /// part is rebuilt only when what it was built from changed, into the arrays it already has
        /// when the sizes allow.</summary>
        internal sealed class MeshShapeTable
        {
            // What the vertex part was built from.
            internal Mesh Mesh;
            internal Vector3[] SourceVertices, SourceNormals;
            internal Color32[] SourceColors;
            internal int SyncVersion, PositionsVersion;
            internal Matrix4x4 ToLocal;
            internal Vector3 Scale;
            /// <summary>Built while the mesh had unsent changes: valid for the frame it was built
            /// in only (in-place vertex buffer writes keep the arrays), so a mesh that is never
            /// drawn is prepared at most once a frame, not once a particle.</summary>
            internal bool BuiltDirty;
            internal int BuiltFrame;

            // What the topology part was built from.
            internal Mesh TopologyMesh;
            internal Vector3[] AreaVertices;
            internal (int[] Indices, int BaseVertex, MeshTopology Topology)[] SourceSubMeshes = Array.Empty<(int[], int, MeshTopology)>();
            internal int TopologySyncVersion;
            internal int SubMesh;
            internal bool TopologyBuiltDirty;
            internal int TopologyBuiltFrame;
            internal bool HasTopology;

            internal Vector3[] Vertices = Array.Empty<Vector3>(), Normals;
            internal Color32[] Colors;
            // Arrays the table owns: positions and normals are written only into these, never into
            // a mesh's own arrays (which Vertices and Normals may point at).
            Vector3[] m_LocalScratch = Array.Empty<Vector3>(), m_ScaledScratch = Array.Empty<Vector3>(), m_NormalScratch = Array.Empty<Vector3>();
            /// <summary>Triangle corners of the triangle submeshes (the selected one with Single
            /// Material), degenerate triangles included, and the segments of line submeshes as
            /// (a, b, b).</summary>
            internal int[] Triangles = Array.Empty<int>();
            internal int TriangleCount;
            /// <summary>Per submesh in <see cref="SubMeshes"/>, the end of its triangles.</summary>
            internal int[] SubMeshTriangleEnds = Array.Empty<int>();
            internal int[] SubMeshes = Array.Empty<int>();
            /// <summary>The mesh has line submeshes: Edge and Vertex sample every entry as a segment.</summary>
            internal bool Segments;
            internal int SubMeshCount;
            /// <summary>Running sums of the triangle areas (segment lengths for lines).</summary>
            internal float[] AreaEnds = Array.Empty<float>();
            /// <summary>The mesh's submeshes use more than one primitive type.</summary>
            internal bool MixedTopology;

            internal float TotalArea => TriangleCount > 0 ? AreaEnds[TriangleCount - 1] : 0f;

            static bool Fresh(Mesh mesh, bool builtDirty, int builtFrame, int frame) => !mesh.m_Dirty || builtDirty && builtFrame == frame;

            internal void Update(Mesh mesh, Matrix4x4 toLocal, Vector3 scale, Mesh topologyMesh, int subMesh, int frame)
            {
                bool vertices = ReferenceEquals(Mesh, mesh) && ReferenceEquals(SourceVertices, mesh.m_Vertices) && ReferenceEquals(SourceNormals, mesh.m_Normals)
                    && ReferenceEquals(SourceColors, mesh.m_Colors) && SyncVersion == mesh.m_SyncVersion && PositionsVersion == mesh.m_PositionsVersion
                    && ToLocal.Equals(toLocal) && Scale == scale
                    && Fresh(mesh, BuiltDirty, BuiltFrame, frame);
                if (!vertices) BuildVertices(mesh, toLocal, scale, frame);
                // The areas weigh the unscaled mesh: the rest mesh's vertices for a deformed pose,
                // the mesh's own when it is drawn as it is, else the pose brought back unscaled
                // (then the topology follows each rebuild of the positions).
                var areaVertices = !ReferenceEquals(topologyMesh, mesh) ? topologyMesh.m_Vertices : toLocal.isIdentity ? mesh.m_Vertices : null;
                if (HasTopology && (areaVertices == null ? vertices : TopologyMatches(topologyMesh, areaVertices, subMesh, frame))) return;
                BuildTopology(topologyMesh, areaVertices ?? m_LocalScratch, subMesh, frame);
                AreaVertices = areaVertices;
            }

            bool TopologyMatches(Mesh mesh, Vector3[] areaVertices, int subMesh, int frame)
            {
                if (!ReferenceEquals(AreaVertices, areaVertices)) return false;
                if (!ReferenceEquals(TopologyMesh, mesh) || TopologySyncVersion != mesh.m_SyncVersion || SubMesh != subMesh
                    || SourceSubMeshes.Length != mesh.m_SubMeshes.Count || !Fresh(mesh, TopologyBuiltDirty, TopologyBuiltFrame, frame))
                    return false;
                for (int s = 0; s < SourceSubMeshes.Length; s++)
                {
                    var sm = mesh.m_SubMeshes[s];
                    var built = SourceSubMeshes[s];
                    if (!ReferenceEquals(built.Indices, sm.Indices) || built.BaseVertex != sm.BaseVertex || built.Topology != sm.Topology) return false;
                }
                return true;
            }

            void BuildVertices(Mesh mesh, Matrix4x4 toLocal, Vector3 scale, int frame)
            {
                var source = mesh.m_Vertices;
                int n = source.Length;
                Mesh = mesh; SourceVertices = source; SourceNormals = mesh.m_Normals; SourceColors = mesh.m_Colors;
                SyncVersion = mesh.m_SyncVersion; PositionsVersion = mesh.m_PositionsVersion; ToLocal = toLocal; Scale = scale;
                BuiltDirty = mesh.m_Dirty; BuiltFrame = frame;
                // Positions and normals take the scale (the areas do not).
                var local = source;
                if (!toLocal.isIdentity)
                {
                    if (m_LocalScratch.Length != n) m_LocalScratch = new Vector3[n];
                    for (int i = 0; i < n; i++) m_LocalScratch[i] = toLocal.MultiplyPoint3x4(source[i]);
                    local = m_LocalScratch;
                }
                if (scale == Vector3.one) Vertices = local;
                else
                {
                    if (m_ScaledScratch.Length != n) m_ScaledScratch = new Vector3[n];
                    for (int i = 0; i < n; i++) m_ScaledScratch[i] = Vector3.Scale(local[i], scale);
                    Vertices = m_ScaledScratch;
                }
                var toFrame = Matrix4x4.Scale(scale) * toLocal;
                var normals = mesh.m_Normals;
                if (normals == null || normals.Length != n) Normals = null;
                else if (toFrame.isIdentity) Normals = normals;
                else
                {
                    if (m_NormalScratch.Length != n) m_NormalScratch = new Vector3[n];
                    for (int i = 0; i < n; i++) m_NormalScratch[i] = toFrame.MultiplyVector(normals[i]);
                    Normals = m_NormalScratch;
                }
                var colors = mesh.m_Colors;
                Colors = colors != null && colors.Length == n ? colors : null;
            }

            void BuildTopology(Mesh mesh, Vector3[] areaVertices, int subMesh, int frame)
            {
                int n = areaVertices.Length;
                TopologyMesh = mesh; TopologySyncVersion = mesh.m_SyncVersion; SubMesh = subMesh;
                TopologyBuiltDirty = mesh.m_Dirty; TopologyBuiltFrame = frame; HasTopology = true;
                if (SourceSubMeshes.Length != mesh.m_SubMeshes.Count) SourceSubMeshes = new (int[], int, MeshTopology)[mesh.m_SubMeshes.Count];
                int count = 0, parts = 0;
                MixedTopology = false;
                Segments = false;
                MeshTopology? first = null;
                for (int s = 0; s < mesh.m_SubMeshes.Count; s++)
                {
                    var sm = mesh.m_SubMeshes[s];
                    SourceSubMeshes[s] = (sm.Indices, sm.BaseVertex, sm.Topology);
                    if (sm.Indices.Length > 0)
                    {
                        first ??= sm.Topology;
                        if (first != sm.Topology) MixedTopology = true;
                        // Edge and Vertex sample every entry as a segment when the submeshes
                        // sampled hold lines; a triangle submesh picked alone keeps the triangle
                        // rules beside a lines submesh (tests/projects/particlemeshshape
                        // topology/mixed0, mixed1).
                        if ((subMesh < 0 || s == subMesh) && (sm.Topology == MeshTopology.Lines || sm.Topology == MeshTopology.LineStrip)) Segments = true;
                    }
                    if (subMesh >= 0 && s != subMesh) continue;
                    count += Primitives(sm.Topology, sm.Indices.Length);
                    parts++;
                }
                if (Triangles.Length < count * 3) Triangles = new int[count * 3];
                if (AreaEnds.Length < count) AreaEnds = new float[count];
                if (SubMeshes.Length < parts)
                {
                    SubMeshes = new int[parts];
                    SubMeshTriangleEnds = new int[parts];
                }
                int k = 0, part = 0;
                float area = 0f;
                for (int s = 0; s < mesh.m_SubMeshes.Count; s++)
                {
                    var sm = mesh.m_SubMeshes[s];
                    if (subMesh >= 0 && s != subMesh) continue;
                    var indices = sm.Indices;
                    int primitives = Primitives(sm.Topology, indices.Length);
                    for (int i = 0; i < primitives; i++)
                    {
                        int a, b, c;
                        switch (sm.Topology)
                        {
                            case MeshTopology.Lines: a = indices[2 * i]; b = c = indices[2 * i + 1]; break;
                            case MeshTopology.LineStrip: a = indices[i]; b = c = indices[i + 1]; break;
                            default: a = indices[3 * i]; b = indices[3 * i + 1]; c = indices[3 * i + 2]; break;
                        }
                        a += sm.BaseVertex; b += sm.BaseVertex; c += sm.BaseVertex;
                        if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                        Triangles[3 * k] = a;
                        Triangles[3 * k + 1] = b;
                        Triangles[3 * k + 2] = c;
                        var pa = areaVertices[a];
                        area += sm.Topology == MeshTopology.Triangles
                            ? 0.5f * Vector3.Cross(areaVertices[b] - pa, areaVertices[c] - pa).magnitude
                            : (areaVertices[b] - pa).magnitude;
                        AreaEnds[k++] = area;
                    }
                    SubMeshes[part] = s;
                    SubMeshTriangleEnds[part++] = k;
                }
                TriangleCount = k;
                SubMeshCount = part;
            }

            /// <summary>The triangles or segments a submesh's indices make; none for points and
            /// quads (measured: they emit nothing).</summary>
            static int Primitives(MeshTopology topology, int indices) => topology switch
            {
                MeshTopology.Triangles => indices / 3,
                MeshTopology.Lines => indices / 2,
                MeshTopology.LineStrip => Math.Max(indices - 1, 0),
                _ => 0,
            };
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
            var mesh = MeshShapeSource(out var toLocal, out var scale, out var renderer, out var topologyMesh);
            if (mesh == null)
            {
                if (S.meshShapeType != ParticleSystemMeshShapeType.Vertex) WarnMeshShape(null, MeshShapeProblem.ZeroSurface);
                return;
            }
            var t = MeshShapeTableFor(mesh, toLocal, scale, topologyMesh);
            if (t.TriangleCount == 0)
            {
                WarnMeshShape(mesh, MeshShapeProblem.NotValid);
                if (S.meshShapeType != ParticleSystemMeshShapeType.Vertex) WarnMeshShape(mesh, MeshShapeProblem.ZeroSurface);
                return;
            }
            if (t.MixedTopology) WarnMeshShape(mesh, MeshShapeProblem.MixedTopology);
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
                    // The material of the particle's submesh (Single Material: its submesh's slot).
                    int slot = S.useMeshMaterialIndex ? t.SubMesh : triangle >= 0 ? MeshShapeSubMesh(t, triangle) : 0;
                    var materials = renderer.m_Materials;
                    m_ShapeColorSet = slot >= 0 && slot < materials.Length && MaterialShapeColor(materials[slot], out m_ShapeColor);
                }
            }
        }

        /// <summary>The submesh a triangle of the table belongs to.</summary>
        static int MeshShapeSubMesh(MeshShapeTable t, int triangle)
        {
            for (int i = 0; i < t.SubMeshCount; i++)
                if (triangle < t.SubMeshTriangleEnds[i]) return t.SubMeshes[i];
            return 0;
        }

        /// <summary>The mesh the shape emits from; the matrix from its vertices into the renderer's
        /// unscaled space (identity unless the renderer's current pose is a deformed copy) and
        /// the renderer's scale (sets <see cref="m_ShapeFrameSet"/> for renderers); the renderer,
        /// whose materials Use Mesh Colors falls back to.</summary>
        Mesh MeshShapeSource(out Matrix4x4 toLocal, out Vector3 scale, out Renderer renderer, out Mesh topologyMesh)
        {
            var S = m_Shape;
            topologyMesh = null;
            toLocal = Matrix4x4.identity;
            scale = Vector3.one;
            renderer = null;
            Mesh mesh = null;
            switch (S.shapeType)
            {
                case ParticleSystemShapeType.Mesh:
                    topologyMesh = S.mesh;
                    return S.mesh;
                // The sprite's geometry in the shape's own frame: a SpriteRenderer's transform and
                // flip do not apply (6000.0.71f1, tests/projects/particlemeshshape sprite/renderer/*).
                case ParticleSystemShapeType.Sprite:
                case ParticleSystemShapeType.SpriteRenderer:
                    var sprite = S.shapeType == ParticleSystemShapeType.Sprite ? S.sprite : S.spriteRenderer != null ? S.spriteRenderer.sprite : null;
                    topologyMesh = sprite != null ? sprite.ParticleShapeMesh() : null;
                    return topologyMesh;
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
                        mesh = skinned.CurrentGeometry(out var model);
                        var shared = skinned.sharedMesh;
                        if (mesh != null && shared != null && !ReferenceEquals(mesh, shared))
                        {
                            var localToWorld = skinned.transform.localToWorldMatrix;
                            if (!model.Equals(localToWorld)) toLocal = skinned.transform.worldToLocalMatrix * model;
                            // Its rest mesh holds the triangles and weighs them (same vertices and indices).
                            if (shared.m_Vertices.Length == mesh.m_Vertices.Length) topologyMesh = shared;
                        }
                    }
                    break;
            }
            if (renderer == null) return null;
            topologyMesh ??= mesh;
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

        MeshShapeTable MeshShapeTableFor(Mesh mesh, Matrix4x4 toLocal, Vector3 scale, Mesh topologyMesh)
        {
            var S = m_Shape;
            // Single Material past the last submesh uses the last one (measured).
            int subMesh = S.useMeshMaterialIndex ? MeshShapeSubMeshIndex(topologyMesh) : -1;
            m_MeshShapeTable ??= new MeshShapeTable();
            m_MeshShapeTable.Update(mesh, toLocal, scale, topologyMesh, subMesh, Time.frameCount);
            return m_MeshShapeTable;
        }

        int MeshShapeSubMeshIndex(Mesh mesh) => Mathf.Clamp(m_Shape.meshMaterialIndex, 0, Math.Max(mesh.m_SubMeshes.Count - 1, 0));

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
                        uint corners = t.Segments ? 2u : 3u;
                        a = T[triangle * 3 + (int)(ShapeWord() % corners)];
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
                    // point along it; a segment's place is the point along it.
                    float f = within;
                    int e = 0;
                    if (!t.Segments)
                    {
                        f = within * 3f;
                        e = Math.Min((int)f, 2);
                        f -= e;
                    }
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
            // The texture reads the topology mesh's UVs (a skinned pose shares its vertex order).
            if (S.texture != null) SetMeshShapeUv(t.TopologyMesh, a, b, c, wa, wb, wc);
            var V = t.Vertices;
            pos = V[a] * wa + V[b] * wb + V[c] * wc;
            var N = t.Normals;
            normal = N != null ? N[a] * wa + N[b] * wb + N[c] * wc : Vector3.forward;
            color = t.Colors != null ? MeshShapeColor(t.Colors[a], t.Colors[b], t.Colors[c], wa, wb, wc) : Color.white;
            return true;
        }

        /// <summary>The vertex colours blended with the weights in 1/255 steps, truncated: each
        /// colour times its weight rounded to a byte, the three added with saturation
        /// (6000.0.71f1, tests/projects/particlemeshshape */color).</summary>
        static Color MeshShapeColor(Color32 a, Color32 b, Color32 c, float wa, float wb, float wc)
        {
            byte ia = (byte)(wa * 255f), ib = (byte)(wb * 255f), ic = (byte)(wc * 255f);
            return new Color32(Blend(a.r, b.r, c.r), Blend(a.g, b.g, c.g), Blend(a.b, b.b, c.b), Blend(a.a, b.a, c.a));

            byte Blend(byte x, byte y, byte z) =>
                (byte)Math.Min(255, Unbound.Runtime.ParticleColors.Multiply(x, ia) + Unbound.Runtime.ParticleColors.Multiply(y, ib) + Unbound.Runtime.ParticleColors.Multiply(z, ic));
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
            int lo = 0, hi = t.TriangleCount - 1;
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
                    Debug.LogAssertion("Particle System can only spawn from mesh if all submeshes use the same primitive type", this);
                    break;
            }
        }
    }
}
