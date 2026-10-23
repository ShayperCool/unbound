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
        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var model = body.Str("m_UnboundModel");
            if (string.IsNullOrEmpty(model))
            {
                Debug.LogWarning($"Unbound: Mesh '{m_Name}' is in Unity's own serialization, which is not supported yet; it stays empty.");
                return;
            }
            try
            {
                var json = Native.Call("model-mesh", JsonSerializer.Serialize(new { guid = model, index = body.Int("m_UnboundMesh") }));
                Fill(JsonDocument.Parse(json).RootElement);
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
            RecalculateBounds();
        }
    }
}
