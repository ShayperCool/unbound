using System;

namespace UnityEngine
{
    // BakeTexture and BakeTrailsTexture: the vertex buffer and the index buffer the renderer
    // builds, written into float textures (6000.0.71f1, tests/projects/particlerender bake/*).
    public sealed partial class ParticleSystemRenderer
    {
        /// <summary>The fewest vertex (or particle) records a vertices texture holds room for.</summary>
        const int MinBakedVertexRecords = 64;
        /// <summary>The fewest indices an indices texture holds room for (particles; trails).</summary>
        const int MinBakedIndices = 128, MinBakedTrailIndices = 256;

        static float[] s_BakeVertices = Array.Empty<float>(), s_BakeIndices = Array.Empty<float>();

        /// <summary>
        /// Builds the particles (or trails) as BakeMesh (BakeTrailsMesh) does, with the same
        /// rotation, scale and position options, and writes them into textures. Measured in
        /// 6000.0.71f1 (tests/projects/particlerender bake/*):
        /// <list type="bullet">
        /// <item>The vertices texture is RFloat: each record is a vertex as the vertex buffer holds
        /// it, the active streams packed in order (position, normal, tangent, then the color as
        /// four bytes in one texel, then the other streams' floats), records back to back, row by
        /// row. PerVertex writes every vertex; otherwise one record per particle: its first vertex
        /// (for trails, one per trail point), or for meshes the particle's 3×3 transform (columns:
        /// emitter scale × rotation × size), its centre and its color.</item>
        /// <item>The indices texture holds the triangle list as floats (RFloat). With
        /// IncludeParticleIndices (particles only) it is RGFloat: each particle's own vertex
        /// indices (0 1 2 2 3 0 for a quad) and the particle's index. PerParticle without it holds
        /// one particle's indices.</item>
        /// <item>Sizes: room for max(64, next power of two of the records) + 1 records (vertices
        /// or particles), width the smallest whole number of records at least the square root of
        /// the floats, height what that needs; indices likewise for max(128, ...) + 1 (256 for
        /// trails) single texels. The Editor keeps these buffers between bakes and grows them,
        /// so after a larger bake its textures are larger (Unbound sizes each bake on its own).</item>
        /// <item>A texture passed in is kept: refilled in its format (its texels take the bytes),
        /// reinitialized to the size only when it has fewer bytes. Nothing to bake returns 0 and
        /// leaves the textures as they were. The return value is the number of indices.</item>
        /// </list>
        /// </summary>
        int BakeTextures(ref Texture2D verticesTexture, ref Texture2D indicesTexture, bool withIndices, Camera camera,
            ParticleSystemBakeTextureOptions options, bool trails)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera), "The camera used for baking is null.");
            var ps = System;
            if (ps == null) return 0;
            var view = View.Of(camera);
            Geometry geometry;
            bool rotationAndScale = (options & ParticleSystemBakeTextureOptions.BakeRotationAndScale) != 0;
            bool position = (options & ParticleSystemBakeTextureOptions.BakePosition) != 0;
            var pose = Matrix4x4.identity;
            if (trails)
            {
                if (!ps.m_Trails.enabled) return 0;
                if (!ps.m_Trails.worldSpace && ps.m_Main.simulationSpace != ParticleSystemSimulationSpace.World && rotationAndScale)
                    pose = BakePose(ps, position);
                geometry = m_TrailGeometry;
                if (!geometry.BuildTrails(ps, view, TrailStreams(ps), pose)) return 0;
            }
            else
            {
                if (ps.m_Main.simulationSpace != ParticleSystemSimulationSpace.World && rotationAndScale)
                    pose = BakePose(ps, position);
                geometry = m_Geometry;
                geometry.Baking = true;
                try
                {
                    if (!geometry.Build(this, ps, view, pose)) return 0;
                }
                finally { geometry.Baking = false; }
            }
            if (geometry.VertexCount == 0) return 0;

            bool perVertex = (options & ParticleSystemBakeTextureOptions.PerVertex) != 0;
            bool mesh = !trails && m_RenderMode == ParticleSystemRenderMode.Mesh;
            int stride = geometry.VertexFloats;
            int records = perVertex ? geometry.VertexCount : trails ? geometry.VertexCount / 2 : geometry.ParticleCount;
            int recordFloats = !perVertex && mesh ? geometry.MeshParticleFloats : stride;
            int floats = records * recordFloats;
            int capacity = Math.Max(MinBakedVertexRecords, NextPowerOfTwo(records)) + 1;
            // Room as for vertex records; mesh particle records are larger than a vertex, so the
            // room also covers them all (the measured bakes fit; larger ones are not measured).
            int room = Math.Max(capacity * stride, floats);
            int width = RoundUp(CeilSqrt(room), stride);
            int height = (room + width - 1) / width;
            int texels = Math.Max(floats, width * height);
            if (s_BakeVertices.Length < texels) s_BakeVertices = new float[texels];
            Array.Clear(s_BakeVertices, 0, texels);
            int at = 0;
            if (perVertex)
                for (int v = 0; v < geometry.VertexCount; v++) geometry.PackVertex(v, s_BakeVertices, ref at);
            else if (trails)
                for (int v = 0; v < geometry.VertexCount; v += 2) geometry.PackVertex(v, s_BakeVertices, ref at);
            else if (mesh)
                for (int k = 0; k < geometry.ParticleCount; k++) geometry.PackMeshParticle(k, s_BakeVertices, ref at);
            else
                for (int k = 0; k < geometry.ParticleCount; k++) geometry.PackVertex(geometry.ParticleFirst[k], s_BakeVertices, ref at);
            WriteFloats(ref verticesTexture, width, height, TextureFormat.RFloat, s_BakeVertices, width * height);

            if (withIndices)
            {
                bool particleIndices = !trails && (options & ParticleSystemBakeTextureOptions.IncludeParticleIndices) != 0;
                int channels = particleIndices ? 2 : 1;
                int indexCapacity = Math.Max(trails ? MinBakedTrailIndices : MinBakedIndices, NextPowerOfTwo(geometry.IndexCount)) + 1;
                int indexWidth = CeilSqrt(indexCapacity);
                int indexHeight = (indexCapacity + indexWidth - 1) / indexWidth;
                int indexTexels = indexWidth * indexHeight;
                if (s_BakeIndices.Length < indexTexels * channels) s_BakeIndices = new float[indexTexels * channels];
                Array.Clear(s_BakeIndices, 0, indexTexels * channels);
                if (particleIndices) geometry.PackParticleIndices(s_BakeIndices);
                else
                {
                    int n = perVertex || trails ? geometry.IndexCount : geometry.FirstParticleIndexCount;
                    for (int i = 0; i < n; i++) s_BakeIndices[i] = geometry.Indices[i];
                }
                WriteFloats(ref indicesTexture, indexWidth, indexHeight, particleIndices ? TextureFormat.RGFloat : TextureFormat.RFloat,
                    s_BakeIndices, indexTexels * channels);
            }
            return geometry.IndexCount;
        }

        static Matrix4x4 BakePose(ParticleSystem ps, bool position)
        {
            var pose = ps.SimulationToWorld();
            if (!position) pose.m03 = pose.m13 = pose.m23 = 0f;
            return pose;
        }

        /// <summary>Writes the floats' bytes into the texture: created at the size, or
        /// reinitialized (keeping its format) when it has fewer bytes; a larger texture keeps its
        /// size and only its first bytes change.</summary>
        static void WriteFloats(ref Texture2D texture, int width, int height, TextureFormat format, float[] data, int count)
        {
            int bytes = count * sizeof(float);
            if (texture == null) texture = new Texture2D(width, height, format, false);
            var raw = texture.GetRawTextureData<byte>();
            if (raw.Length < bytes)
            {
                texture.Reinitialize(width, height, texture.format, false);
                raw = texture.GetRawTextureData<byte>();
            }
            int size = raw.Length;
            if (s_BakeBytes.Length < size) s_BakeBytes = new byte[size];
            for (int i = 0; i < size; i++) s_BakeBytes[i] = raw[i];
            var source = global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.AsSpan(0, count));
            source.Slice(0, Math.Min(bytes, size)).CopyTo(s_BakeBytes);
            texture.SetPixelData(s_BakeBytes, 0);
            texture.Apply(false);
        }

        static byte[] s_BakeBytes = Array.Empty<byte>();

        static int NextPowerOfTwo(int n)
        {
            int p = 1;
            while (p < n && p < (1 << 30)) p <<= 1;
            return p;
        }

        static int CeilSqrt(int n)
        {
            int r = (int)Math.Ceiling(Math.Sqrt(n));
            while (r > 1 && (r - 1) * (r - 1) >= n) r--;
            while (r * r < n) r++;
            return r;
        }

        static int RoundUp(int n, int multiple) => multiple <= 0 ? n : (n + multiple - 1) / multiple * multiple;

        internal sealed partial class Geometry
        {
            /// <summary>The particles of the last build, in draw order: their first vertex, centre
            /// and (mesh particles) transform.</summary>
            internal int ParticleCount;
            internal int[] ParticleFirst = new int[16];
            int[] m_ParticleFirstIndex = new int[16];
            Vector3[] m_ParticleCentres = new Vector3[16];
            Quaternion[] m_ParticleOrientations = new Quaternion[16];
            Vector3[] m_ParticleSizes = new Vector3[16];
            Vector3 m_EmitterScale;

            void NoteParticle(int firstVertex, Vector3 centre)
            {
                if (ParticleCount == ParticleFirst.Length)
                {
                    Array.Resize(ref ParticleFirst, ParticleCount * 2);
                    Array.Resize(ref m_ParticleFirstIndex, ParticleCount * 2);
                    Array.Resize(ref m_ParticleCentres, ParticleCount * 2);
                    Array.Resize(ref m_ParticleOrientations, ParticleCount * 2);
                    Array.Resize(ref m_ParticleSizes, ParticleCount * 2);
                }
                ParticleFirst[ParticleCount] = firstVertex;
                m_ParticleFirstIndex[ParticleCount] = IndexCount;
                m_ParticleCentres[ParticleCount] = centre;
                ParticleCount++;
            }

            /// <summary>A mesh particle's orientation and size (its baked transform is built from
            /// them only when baked).</summary>
            void NoteMeshParticle(Quaternion orientation, Vector3 size, Vector3 emitterScale)
            {
                m_ParticleOrientations[ParticleCount - 1] = orientation;
                m_ParticleSizes[ParticleCount - 1] = size;
                m_EmitterScale = emitterScale;
            }

            /// <summary>The floats a vertex takes in the vertex buffer: position, the normal,
            /// tangent and color streams (a color is four bytes), then the other streams.</summary>
            internal int VertexFloats => 3 + (m_StreamNormals ? 3 : 0) + (m_StreamTangents ? 4 : 0) + (m_StreamColors ? 1 : 0) + m_StreamFloats;

            /// <summary>A mesh particle's record: its transform's three columns, its centre, its color.</summary>
            internal int MeshParticleFloats => 12 + (m_StreamColors ? 1 : 0);

            /// <summary>Indices of the first particle (PerParticle without particle indices).</summary>
            internal int FirstParticleIndexCount => ParticleCount > 1 ? IndexCount / ParticleCount : IndexCount;

            internal void PackVertex(int v, float[] into, ref int at)
            {
                var p = Positions[v];
                into[at++] = p.x; into[at++] = p.y; into[at++] = p.z;
                if (m_StreamNormals) { var n = Normals[v]; into[at++] = n.x; into[at++] = n.y; into[at++] = n.z; }
                if (m_StreamTangents) { var t = Tangents[v]; into[at++] = t.x; into[at++] = t.y; into[at++] = t.z; into[at++] = t.w; }
                if (m_StreamColors) into[at++] = PackedColor(Colors[v]);
                for (int f = 0; f < m_StreamFloats; f++) into[at++] = m_PackedUvs[f >> 2][v][f & 3];
            }

            internal void PackMeshParticle(int k, float[] into, ref int at)
            {
                var m = Matrix4x4.Scale(m_EmitterScale) * Matrix4x4.Rotate(m_ParticleOrientations[k]) * Matrix4x4.Scale(m_ParticleSizes[k]);
                for (int column = 0; column < 3; column++)
                    for (int row = 0; row < 3; row++) into[at++] = m[row, column];
                var c = m_ParticleCentres[k];
                into[at++] = c.x; into[at++] = c.y; into[at++] = c.z;
                if (m_StreamColors) into[at++] = PackedColor(Colors[ParticleFirst[k]]);
            }

            /// <summary>Each particle's own vertex indices and its index, as RG pairs: a quad's
            /// two triangles as 0 1 2 2 3 0, a mesh's as the mesh lists them.</summary>
            internal void PackParticleIndices(float[] into)
            {
                int at = 0;
                for (int k = 0; k < ParticleCount; k++)
                {
                    int first = ParticleFirst[k];
                    int next = k + 1 < ParticleCount ? ParticleFirst[k + 1] : VertexCount;
                    if (next - first == 4)
                    {
                        foreach (int local in s_QuadParticleIndices) { into[at++] = local; into[at++] = k; }
                        continue;
                    }
                    int end = k + 1 < ParticleCount ? m_ParticleFirstIndex[k + 1] : IndexCount;
                    for (int i = m_ParticleFirstIndex[k]; i < end; i++) { into[at++] = Indices[i] - first; into[at++] = k; }
                }
            }

            static readonly int[] s_QuadParticleIndices = { 0, 1, 2, 2, 3, 0 };

            static float PackedColor(Color32 c) =>
                BitConverter.Int32BitsToSingle(c.r | c.g << 8 | c.b << 16 | c.a << 24);
        }
    }
}
