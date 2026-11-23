using System;
using System.Runtime.InteropServices;
using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        sealed class BoneWeightBuffer
        {
            internal GpuBufferStorage Storage;
            internal uint[] Words;
            internal bool Dirty = true;
        }

        BoneWeightBuffer[] m_BoneWeightBuffers;

        public GraphicsBuffer GetBoneWeightBuffer(SkinWeights layout)
        {
            if (this == null) throw new NullReferenceException("Object reference not set to an instance of an object");
            if (layout == SkinWeights.None)
            {
                Debug.LogError("Only possible to access bone weights buffer for values: OneBone, TwoBones, FourBones and Unlimited.");
                return null;
            }

            MainThreadCheck.Check("GetBoneWeightBufferImpl");
            EnsureModelLoaded();
            ThrowIfDestroyed();
            var packedLayout = SkinLayoutForInfluences((int)layout);
            int slot = packedLayout switch
            {
                SkinWeights.OneBone => 0,
                SkinWeights.TwoBones => 1,
                SkinWeights.FourBones => 2,
                SkinWeights.Unlimited => 3,
                _ => -1,
            };
            var data = SkinWeightData();
            if (slot < 0 || data == null || data.Counts.Length == 0 ||
                (packedLayout == SkinWeights.Unlimited && CurrentSkinWeightLayout != SkinWeights.Unlimited))
                throw new ArgumentException($"Bone Weight buffer for {(int)layout} bones per vertex on {name} is null");

            m_BoneWeightBuffers ??= new BoneWeightBuffer[4];
            var buffer = m_BoneWeightBuffers[slot] ??= new BoneWeightBuffer();
            if (buffer.Dirty)
            {
                bool unlimited = packedLayout == SkinWeights.Unlimited;
                int wordsPerElement = unlimited || packedLayout == SkinWeights.OneBone ? 1 : 2 * (int)packedLayout;
                int count = unlimited ? checked(data.Counts.Length + 1 + data.WeightCount) : data.Counts.Length;
                int words = checked(count * wordsPerElement);
                if (buffer.Words == null || buffer.Words.Length != words) buffer.Words = new uint[words];
                var target = GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured;
                if (unlimited) PackVariableWeights(data, buffer.Words);
                else PackFixedWeights(data, (int)packedLayout, buffer.Words);
                Upload(ref buffer.Storage, count, sizeof(uint) * wordsPerElement, target, MemoryMarshal.AsBytes(buffer.Words.AsSpan()));
                buffer.Dirty = false;
            }
            return new GraphicsBuffer(buffer.Storage);
        }

        static void PackFixedWeights(MeshSkinWeightData data, int width, Span<uint> words)
        {
            words.Clear();
            for (int vertex = 0; vertex < data.Counts.Length; vertex++)
            {
                var influences = data.ForVertex(vertex);
                if (influences.Length == 0) continue;
                if (width == 1)
                {
                    words[vertex] = unchecked((uint)influences[0].boneIndex);
                    continue;
                }
                var row = influences.Slice(0, Math.Min(width, influences.Length));
                float total = 0f;
                foreach (var influence in row) total += influence.weight;
                float scale = total != 0f ? 1f / total : 0f;
                int start = 2 * width * vertex;
                for (int i = 0; i < row.Length; i++)
                {
                    words[start + i] = BitConverter.SingleToUInt32Bits(row[i].weight * scale);
                    words[start + width + i] = unchecked((uint)row[i].boneIndex);
                }
            }
        }

        static void PackVariableWeights(MeshSkinWeightData data, Span<uint> words)
        {
            int destination = data.Counts.Length + 1;
            for (int vertex = 0; vertex < data.Counts.Length; vertex++)
            {
                words[vertex] = (uint)destination;
                foreach (var influence in data.ForVertex(vertex))
                    words[destination++] = ((uint)MeshSkinWeightData.ToPackedWeight(influence.weight) << 16)
                        | unchecked((ushort)influence.boneIndex);
            }
            words[data.Counts.Length] = (uint)destination;
        }

        void InvalidateBoneWeightBuffers()
        {
            if (m_BoneWeightBuffers == null) return;
            foreach (var buffer in m_BoneWeightBuffers)
            {
                if (buffer == null || buffer.Dirty) continue;
                Array.Clear(buffer.Words);
                buffer.Storage.Write(0, MemoryMarshal.AsBytes(buffer.Words.AsSpan()));
                buffer.Dirty = true;
            }
        }

        void ReleaseBoneWeightBuffers()
        {
            if (m_BoneWeightBuffers != null)
                foreach (var buffer in m_BoneWeightBuffers) buffer?.Storage?.ReleaseMeshOwner();
            m_BoneWeightBuffers = null;
        }
    }
}
