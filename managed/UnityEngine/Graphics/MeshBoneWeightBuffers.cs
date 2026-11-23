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
                Log.Engine(LogType.Error, "Only possible to access bone weights buffer for values: OneBone, TwoBones, FourBones and Unlimited.", this);
                return null;
            }

            MainThreadCheck.Check("GetBoneWeightBufferImpl");
            EnsureModelLoaded();
            ThrowIfDestroyed();
            if ((int)layout < 0)
                Log.Engine(LogType.Error, "Unknown bonesPerVertex in GetBoneWeights()", this);
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
                int count = unlimited ? data.PackedWordCount : data.Counts.Length;
                int words = checked(count * wordsPerElement);
                if (buffer.Words == null || buffer.Words.Length != words) buffer.Words = new uint[words];
                var target = GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured;
                if (unlimited) data.WritePackedWords(buffer.Words);
                else PackFixedWeights(data, (int)packedLayout, buffer.Words);
                Upload(ref buffer.Storage, count, sizeof(uint) * wordsPerElement, target, MemoryMarshal.AsBytes(buffer.Words.AsSpan()));
                buffer.Dirty = false;
            }
            return new GraphicsBuffer(buffer.Storage);
        }

        static void PackFixedWeights(MeshSkinWeightData data, int width, Span<uint> words)
        {
            words.Clear();
            Span<float> normalized = stackalloc float[4];
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
                MeshSkinWeightData.NormalizePrefix(row, normalized, data.IsPacked);
                int start = 2 * width * vertex;
                for (int i = 0; i < row.Length; i++)
                {
                    words[start + i] = BitConverter.SingleToUInt32Bits(normalized[i]);
                    words[start + width + i] = unchecked((uint)row[i].boneIndex);
                }
            }
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
