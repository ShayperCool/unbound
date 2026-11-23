using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A Mesh read from its document: the sub-asset of an imported model, whose geometry
    /// the native side holds (the document names the model and the mesh's index in it).</summary>
    public sealed partial class Mesh
    {
        /// <summary>Per bind pose, the hash of its bone's path below the model's root, and the root
        /// bone's (0: none): how a renderer without bones finds them in an Animator's skeleton
        /// (Optimize Game Objects). Imported skinned meshes have them.</summary>
        internal uint[] m_BoneNameHashes;
        internal uint m_RootBoneNameHash;

        // Deferred imported unreadable model mesh: metadata lives here and Sync() uploads directly
        // from the native ImportedModel to the GPU without allocating managed vertex/index arrays.
        // Any CPU reader or mutator materializes the full CPU arrays on demand via EnsureModelLoaded().
        internal bool m_DeferredModel;
        internal string m_ModelGuid;
        internal int m_ModelMeshIndex;
        internal int m_DeferredVertexCount;
        internal int m_DeferredChannels;
        bool m_LoadingModel;

        const int DeferredNormalBit = 1 << 0;
        const int DeferredTangentBit = 1 << 1;
        const int DeferredColorBit = 1 << 2;
        const int DeferredUv0Shift = 3;

        /// <summary>Fetches a model mesh's JSON geometry from the native side (tests replace it).</summary>
        internal static Func<string, int, string> LoadModelMeshJson = (guid, index) =>
            Native.Call("model-mesh", JsonSerializer.Serialize(new { guid, index }));

        /// <summary>Uploads a deferred model mesh directly from the native side (tests replace it).</summary>
        internal static Func<ulong, string, int, bool> UploadDeferredModelMesh = (handle, guid, index) =>
            Native.UploadModelMesh(handle, guid, index);

        /// <summary>The mesh is being read from its file: its changes are no edits.</summary>
        bool m_Reading;

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Reading = true;
            try { ReadSerializedData(body); }
            finally { m_Reading = false; }
        }

        void ReadSerializedData(YNode body)
        {
            if (body.Get("m_UnboundBonePaths") is YNode bones)
            {
                var hashes = new System.Collections.Generic.List<uint>();
                foreach (var path in bones.Seq()) hashes.Add(HashedBindings.Crc32(path.AsString()));
                m_BoneNameHashes = hashes.ToArray();
                var rootPath = body.Str("m_UnboundRootBonePath");
                m_RootBoneNameHash = string.IsNullOrEmpty(rootPath) ? 0u : HashedBindings.Crc32(rootPath);
            }
            var model = body.Str("m_UnboundModel");
            if (string.IsNullOrEmpty(model))
            {
                if (!ReadUnitySerialized(body))
                    Debug.LogWarning($"Unbound: Mesh '{m_Name}' has no vertex data Unbound reads; it stays empty.");
                return;
            }
            int index = body.Int("m_UnboundMesh");
            if (body.Int("m_UnboundDeferred") != 0)
            {
                ReadDeferredModel(body, model, index);
                return;
            }
            try
            {
                var json = LoadModelMeshJson(model, index);
                using var doc = JsonDocument.Parse(json);
                FillFromModel(doc.RootElement);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the geometry of mesh '{m_Name}': {e.Message}");
            }
        }

        void ReadDeferredModel(YNode body, string guid, int index)
        {
            m_DeferredModel = true;
            m_ModelGuid = guid;
            m_ModelMeshIndex = index;
            m_DeferredVertexCount = Math.Max(0, body.Int("m_UnboundVertexCount"));
            m_DeferredChannels = body.Int("m_UnboundChannels");
            m_HasPositionChannel = true;
            for (int ch = 0; ch < 8; ch++)
                m_UVDimensions[ch] = (m_DeferredChannels & (1 << (DeferredUv0Shift + ch))) != 0 ? 2 : 0;
            m_SubMeshes.Clear();
            var subs = body.Get("m_UnboundSubmeshes")?.Seq();
            if (subs != null)
            {
                foreach (var sm in subs)
                {
                    var sub = new SubMesh
                    {
                        DeferredIndexCount = Math.Max(0, sm.Int("indexCount")),
                        Topology = MeshTopology.Triangles,
                        BaseVertex = 0,
                        IndexStart = -1,
                        FirstVertex = sm.Int("firstVertex"),
                        VertexCount = sm.Int("vertexCount"),
                    };
                    if (sub.VertexCount > 0)
                        sub.Bounds = BoundsOf(Vec3(sm.Get("boundsMin")), Vec3(sm.Get("boundsMax")));
                    m_SubMeshes.Add(sub);
                }
            }
            if (m_SubMeshes.Count == 0) m_SubMeshes.Add(new SubMesh());
            if (m_DeferredVertexCount > 0)
            {
                var b = new Bounds();
                b.SetMinMax(Vec3(body.Get("m_UnboundBoundsMin")), Vec3(body.Get("m_UnboundBoundsMax")));
                m_Bounds = b;
            }
            else m_Bounds = default;
            m_IsReadable = false;
            m_Dirty = true;
            m_GraphicsBuffersDirty = true;
        }

        /// <summary>Materializes the managed CPU arrays of a deferred imported model mesh on first
        /// CPU access. Preserves readability, dirty state, handle, sync version and positions
        /// version so lazy hydration is invisible to callers and observers.</summary>
        internal void EnsureModelLoaded()
        {
            if (!m_DeferredModel || m_LoadingModel) return;
            m_LoadingModel = true;
            bool wasDirty = m_Dirty;
            bool wasGfxDirty = m_GraphicsBuffersDirty;
            bool wasGfxVertsDirty = m_GraphicsVerticesDirty;
            bool wasGfxIndicesDirty = m_GraphicsDirtyIndices;
            bool wasReadable = m_IsReadable;
            int prevPositionsVersion = m_PositionsVersion;
            // Loading the arrays changes no state a script sees: bounds a script set stay.
            var bounds = m_Bounds;
            m_DeferredModel = false;
            m_IsReadable = true;
            foreach (var s in m_SubMeshes) s.DeferredIndexCount = 0;
            try
            {
                var json = LoadModelMeshJson(m_ModelGuid, m_ModelMeshIndex);
                using var doc = JsonDocument.Parse(json);
                Fill(doc.RootElement);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the geometry of mesh '{m_Name}': {e.Message}");
            }
            finally
            {
                m_IsReadable = wasReadable;
                m_Dirty = wasDirty;
                m_GraphicsBuffersDirty = wasGfxDirty;
                m_GraphicsVerticesDirty = wasGfxVertsDirty;
                m_GraphicsDirtyIndices = wasGfxIndicesDirty;
                m_PositionsVersion = prevPositionsVersion;
                m_Bounds = bounds;
                m_LoadingModel = false;
            }
        }

        void RevertToDeferred(Mesh saved)
        {
            m_Vertices = Array.Empty<Vector3>();
            m_Normals = null;
            m_Tangents = null;
            m_Colors = null;
            m_PositionsVersion++;
            for (int i = 0; i < 8; i++)
            {
                m_UVs[i] = null;
                m_UVDimensions[i] = saved.m_UVDimensions[i];
            }
            m_SubMeshes.Clear();
            foreach (var s in saved.m_SubMeshes)
            {
                m_SubMeshes.Add(new SubMesh
                {
                    Indices = Array.Empty<int>(),
                    DeferredIndexCount = s.DeferredIndexCount,
                    Topology = s.Topology,
                    BaseVertex = s.BaseVertex,
                    IndexStart = s.IndexStart,
                    Bounds = s.Bounds,
                    FirstVertex = s.FirstVertex,
                    VertexCount = s.VertexCount,
                });
            }
            m_UnmeasuredSkinBoundsMutation = false;
            m_Layout = null;
            m_HasPositionChannel = saved.m_HasPositionChannel;
            m_IndexBuffer = null;
            m_BoneWeights = null;
            ResetBindposes();
            m_BoneNameHashes = (uint[])saved.m_BoneNameHashes?.Clone();
            m_RootBoneNameHash = saved.m_RootBoneNameHash;
            m_Bounds = saved.m_Bounds;
            m_IndexFormat = saved.m_IndexFormat;
            ClearPackedChannels(false);
            m_BlendShapes.Clear();
            m_DeferredModel = true;
            m_ModelGuid = saved.m_ModelGuid;
            m_ModelMeshIndex = saved.m_ModelMeshIndex;
            m_DeferredVertexCount = saved.m_DeferredVertexCount;
            m_DeferredChannels = saved.m_DeferredChannels;
            m_IsReadable = saved.m_IsReadable;
            Changed();
        }

        static T[] Decoded<T>(JsonElement element, string name) where T : unmanaged
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return System.Array.Empty<T>();
            var bytes = Convert.FromBase64String(value.GetString() ?? "");
            return MemoryMarshal.Cast<byte, T>(bytes).ToArray();
        }

        /// <summary>Fills the geometry of a <c>model-mesh</c> answer, then adopts its readability.</summary>
        internal void FillFromModel(JsonElement m)
        {
            Fill(m);
            AdoptReadability(m);
        }

        /// <summary>Adopts the <c>readable</c> field of a host answer (a model's mesh, a built-in
        /// mesh of the install). Last, so that filling never runs against a mesh that is already
        /// unreadable.</summary>
        internal void AdoptReadability(JsonElement m)
        {
            if (ModelReadable(m) is bool readable) m_IsReadable = readable;
        }

        /// <summary>The model's Read/Write Enabled import setting, the <c>readable</c> field of a
        /// <c>model-mesh</c> answer. Unity 6000.0 Mesh.isReadable: "You can set this value using the
        /// Read/Write Enabled checkbox when importing a model to Unity." An answer without the field
        /// (from a native side older than it) gives null: the mesh keeps the value it was created
        /// with (true), which is how such meshes behaved before, rather than a guessed setting.</summary>
        internal static bool? ModelReadable(JsonElement m) =>
            m.ValueKind == JsonValueKind.Object && m.TryGetProperty("readable", out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

        void Fill(JsonElement m)
        {
            var positions = Decoded<Vector3>(m, "positions");
            vertices = positions;
            var normals = Decoded<Vector3>(m, "normals");
            if (normals.Length == positions.Length) this.normals = normals;
            var tangents = Decoded<Vector4>(m, "tangents");
            if (tangents.Length == positions.Length) this.tangents = tangents;
            if (m.TryGetProperty("uvs", out var uvs))
            {
                int channel = 0;
                foreach (var uv in uvs.EnumerateArray())
                {
                    var data = Convert.FromBase64String(uv.GetString() ?? "");
                    var values = MemoryMarshal.Cast<byte, Vector2>(data).ToArray();
                    if (values.Length == positions.Length) SetUVs(channel, values);
                    channel++;
                }
            }
            // The importer keeps colors as bytes (Color: UNorm8 x4, matches the Unity 6000.0.71f1
            // Editor, tests/projects/meshreadable `layers.attribute.Color`).
            var colors = Decoded<Color>(m, "colors");
            if (colors.Length == positions.Length)
            {
                var bytes = new Color32[colors.Length];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = colors[i];
                colors32 = bytes;
            }
            if (m.TryGetProperty("submeshes", out var submeshes))
            {
                subMeshCount = Math.Max(1, submeshes.GetArrayLength());
                int index = 0;
                foreach (var submesh in submeshes.EnumerateArray())
                {
                    var data = Convert.FromBase64String(submesh.GetString() ?? "");
                    SetTriangles(MemoryMarshal.Cast<byte, int>(data).ToArray(), index++, true);
                }
            }
            // Skinning: four bone indices then four weights per vertex.
            var weights = Convert.FromBase64String(m.TryGetProperty("boneWeights", out var w) ? w.GetString() ?? "" : "");
            if (weights.Length == positions.Length * 32)
            {
                var result = new BoneWeight[positions.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    var indices = MemoryMarshal.Cast<byte, int>(weights.AsSpan(i * 32, 16));
                    var values = MemoryMarshal.Cast<byte, float>(weights.AsSpan(i * 32 + 16, 16));
                    result[i] = new BoneWeight
                    {
                        boneIndex0 = indices[0], boneIndex1 = indices[1], boneIndex2 = indices[2], boneIndex3 = indices[3],
                        weight0 = values[0], weight1 = values[1], weight2 = values[2], weight3 = values[3],
                    };
                }
                boneWeights = result;
            }
            var bindPoses = Decoded<float>(m, "bindPoses");
            if (bindPoses.Length > 0 && bindPoses.Length % 16 == 0)
            {
                var poses = new Matrix4x4[bindPoses.Length / 16];
                for (int i = 0; i < poses.Length; i++)
                {
                    var p = new Matrix4x4();
                    for (int k = 0; k < 16; k++) p[k] = bindPoses[i * 16 + k];
                    poses[i] = p;
                }
                StoreBindposes(poses, true);
            }
            // Blend shapes: per frame the deltas of every vertex.
            if (m.TryGetProperty("blendShapes", out var shapes))
                foreach (var shape in shapes.EnumerateArray())
                {
                    var name = shape.GetProperty("name").GetString();
                    foreach (var frame in shape.GetProperty("frames").EnumerateArray())
                    {
                        var deltas = Decoded<Vector3>(frame, "vertices");
                        if (deltas.Length != positions.Length) continue;
                        var deltaNormals = Decoded<Vector3>(frame, "normals");
                        var deltaTangents = Decoded<Vector3>(frame, "tangents");
                        AddBlendShapeFrame(name, frame.GetProperty("weight").GetSingle(), deltas,
                            deltaNormals.Length == positions.Length ? deltaNormals : null,
                            deltaTangents.Length == positions.Length ? deltaTangents : null);
                    }
                }
            // The importer's bounds also hold the blend shape frames (measured,
            // tests/projects/modelaxis Skinned set).
            if (m.TryGetProperty("boundsMin", out var min) && m.TryGetProperty("boundsMax", out var max)
                && min.ValueKind == JsonValueKind.Array && max.ValueKind == JsonValueKind.Array && positions.Length > 0)
            {
                var b = new Bounds();
                b.SetMinMax(Element(min), Element(max));
                bounds = b;
            }
            else UpdateBounds();
        }

        static Vector3 Element(JsonElement v) => new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    }
}
