using System;

namespace UnityEngine
{
    /// <summary>One flat run of influences per vertex, in the order supplied by the mesh.</summary>
    internal sealed class MeshSkinWeightData
    {
        internal readonly byte[] Counts;
        internal readonly BoneWeight1[] Weights;
        readonly int[] m_Offsets;
        internal int WeightCount => m_Offsets[^1];

        MeshSkinWeightData(int vertexCount, int weightCapacity)
        {
            Counts = new byte[vertexCount];
            Weights = new BoneWeight1[weightCapacity];
            m_Offsets = new int[vertexCount + 1];
        }

        internal ReadOnlySpan<BoneWeight1> ForVertex(int vertex) => vertex < Counts.Length
            ? Weights.AsSpan(m_Offsets[vertex], Counts[vertex]) : default;

        internal static MeshSkinWeightData FromNative(ReadOnlySpan<byte> counts, ReadOnlySpan<BoneWeight1> weights, SkinWeights layout)
        {
            var data = new MeshSkinWeightData(counts.Length, weights.Length);
            int source = 0, destination = 0;
            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                var vertexWeights = weights.Slice(source, counts[vertex]);
                source += vertexWeights.Length;
                int first = destination;
                if (layout == SkinWeights.OneBone)
                {
                    data.Weights[destination++] = new BoneWeight1 { boneIndex = vertexWeights[0].boneIndex, weight = 1f };
                }
                else
                {
                    float sum = 0f;
                    foreach (var influence in vertexWeights)
                        sum += MathF.Max(influence.weight, 0f);
                    float scale = 1f / sum;
                    foreach (var influence in vertexWeights)
                    {
                        if (!(influence.weight > 0f)) continue;
                        data.Weights[destination++] = new BoneWeight1 { boneIndex = influence.boneIndex, weight = influence.weight * scale };
                    }
                    if (layout == SkinWeights.Unlimited)
                        destination = first + Quantize(data.Weights.AsSpan(first, destination - first));
                }
                data.Counts[vertex] = (byte)(destination - first);
                data.m_Offsets[vertex + 1] = destination;
            }
            return data;
        }

        // The unlimited layout stores a 16-bit weight, with the accumulated rounding error
        // applied at the end of the vertex's run. GetAllBoneWeights exposes those decoded values.
        internal static ushort ToPackedWeight(float weight) => float.IsFinite(weight)
            ? (ushort)MathF.Round(Math.Clamp(weight, 0f, 1f) * ushort.MaxValue, MidpointRounding.AwayFromZero) : (ushort)0;

        static int Quantize(Span<BoneWeight1> weights)
        {
            const int MaxWeight = ushort.MaxValue;
            const float WeightUnit = 1f / MaxWeight;
            int remainder = MaxWeight;
            for (int i = 0; i < weights.Length; i++)
            {
                int bits = ToPackedWeight(weights[i].weight);
                weights[i].weight = bits * WeightUnit;
                remainder -= bits;
            }
            if (remainder == MaxWeight && !weights.IsEmpty)
            {
                weights[0].weight = 1f;
                return 1;
            }
            for (int i = weights.Length - 1; i >= 0 && remainder != 0; i--)
            {
                int bits = (int)MathF.Round(weights[i].weight * MaxWeight);
                int corrected = Math.Clamp(bits + remainder, 0, MaxWeight);
                weights[i].weight = corrected * WeightUnit;
                remainder -= corrected - bits;
            }
            int kept = 0;
            for (int i = 0; i < weights.Length; i++)
                if (weights[i].weight > 0f) weights[kept++] = weights[i];
            return kept;
        }

        internal static MeshSkinWeightData FromLegacy(BoneWeight[] weights)
        {
            var data = new MeshSkinWeightData(weights.Length, checked(weights.Length * 4));
            int written = 0;
            void Add(int boneIndex, float weight)
            {
                if (weight > 0f) data.Weights[written++] = new BoneWeight1 { boneIndex = boneIndex, weight = weight };
            }
            for (int i = 0; i < weights.Length; i++)
            {
                var value = weights[i];
                int first = written;
                Add(value.boneIndex0, value.weight0);
                Add(value.boneIndex1, value.weight1);
                Add(value.boneIndex2, value.weight2);
                Add(value.boneIndex3, value.weight3);
                data.Counts[i] = (byte)(written - first);
                data.m_Offsets[i + 1] = written;
            }
            return data;
        }

        internal BoneWeight[] LegacyProjection(bool normalize)
        {
            var values = new BoneWeight[Counts.Length];
            for (int vertex = 0; vertex < values.Length; vertex++)
            {
                var row = ForVertex(vertex);
                var a = row.Length > 0 ? row[0] : default;
                var b = row.Length > 1 ? row[1] : default;
                var c = row.Length > 2 ? row[2] : default;
                var d = row.Length > 3 ? row[3] : default;
                float sum = a.weight + b.weight + c.weight + d.weight;
                float scale = normalize && sum > 0f ? 1f / sum : 1f;
                values[vertex] = new BoneWeight
                {
                    boneIndex0 = a.boneIndex, weight0 = a.weight * scale,
                    boneIndex1 = b.boneIndex, weight1 = b.weight * scale,
                    boneIndex2 = c.boneIndex, weight2 = c.weight * scale,
                    boneIndex3 = d.boneIndex, weight3 = d.weight * scale,
                };
            }
            return values;
        }
    }
}
