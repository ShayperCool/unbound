using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>A Mesh read from its document: the sub-asset of an imported model, whose geometry
    /// the native side holds (the document names the model and the mesh's index in it).</summary>
    public sealed partial class Mesh : ISerializedAsset
    {
        /// <summary>Per bind pose, the hash of its bone's path below the model's root, and the root
        /// bone's (0: none): how a renderer without bones finds them in an Animator's skeleton
        /// (Optimize Game Objects). Imported skinned meshes have them.</summary>
        internal uint[] m_BoneNameHashes;
        internal uint m_RootBoneNameHash;

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
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
            try
            {
                var json = Native.Call("model-mesh", JsonSerializer.Serialize(new { guid = model, index = body.Int("m_UnboundMesh") }));
                FillFromModel(JsonDocument.Parse(json).RootElement);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: the geometry of mesh '{m_Name}': {e.Message}");
            }
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
            // Last, so that filling never runs against a mesh that is already unreadable.
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
            var colors = Decoded<Color>(m, "colors");
            if (colors.Length == positions.Length) this.colors = colors;
            if (m.TryGetProperty("submeshes", out var submeshes))
            {
                subMeshCount = Math.Max(1, submeshes.GetArrayLength());
                int index = 0;
                foreach (var submesh in submeshes.EnumerateArray())
                {
                    var data = Convert.FromBase64String(submesh.GetString() ?? "");
                    SetTriangles(MemoryMarshal.Cast<byte, int>(data).ToArray(), index++, false);
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
                bindposes = poses;
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
            else RecalculateBounds();
        }

        static Vector3 Element(JsonElement v) => new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
    }
}
