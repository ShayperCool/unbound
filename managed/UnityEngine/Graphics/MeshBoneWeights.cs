using System;
using Unbound.Runtime;
using Unity.Collections;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        SkinWeights? m_RuntimeSkinLayout;
        internal MeshSkinWeightData RuntimeSkinWeights { get; private set; }
        MeshSkinWeightData m_ExpandedLegacyWeights;
        MeshArrayView<byte> m_BoneCountsView;
        MeshArrayView<BoneWeight1> m_BoneWeightsView;

        public SkinWeights skinWeightBufferLayout
        {
            get
            {
                MainThreadCheck.Check("GetBoneWeightBufferLayoutInternal");
                ThrowIfDestroyed();
                EnsureModelLoaded();
                return CurrentSkinWeightLayout;
            }
        }

        SkinWeights CurrentSkinWeightLayout => m_RuntimeSkinLayout ??
            (m_BoneWeights == null ? SkinWeights.None : SkinWeights.FourBones);

        MeshSkinWeightData SkinWeightData() => RuntimeSkinWeights ?? (m_BoneWeights is { Length: > 0 }
            ? (m_ExpandedLegacyWeights ??= MeshSkinWeightData.FromLegacy(m_BoneWeights)) : null);

        public NativeArray<byte> GetBonesPerVertex()
        {
            MainThreadCheck.Check("HasBoneWeights");
            ThrowIfDestroyed();
            EnsureModelLoaded();
            ThrowIfDestroyed();
            var counts = SkinWeightData()?.Counts;
            if (counts is { Length: 0 }) counts = null;
            return (m_BoneCountsView ??= new MeshArrayView<byte>(counts)).Borrow(counts?.Length ?? 0);
        }

        public NativeArray<BoneWeight1> GetAllBoneWeights()
        {
            MainThreadCheck.Check("GetAllBoneWeightsArray");
            ThrowIfDestroyed();
            EnsureModelLoaded();
            ThrowIfDestroyed();
            var data = SkinWeightData();
            int count = data?.WeightCount ?? 0;
            return (m_BoneWeightsView ??= new MeshArrayView<BoneWeight1>(count == 0 ? null : data.Weights)).Borrow(count);
        }

        public void SetBoneWeights(NativeArray<byte> bonesPerVertex, NativeArray<BoneWeight1> weights)
        {
            if ((bonesPerVertex.Length != 0 && !bonesPerVertex.IsCreated) || (weights.Length != 0 && !weights.IsCreated))
                throw new ArgumentException("A nonempty bone-weight array must have storage.");
            var counts = bonesPerVertex.AsReadOnlySpan();
            var influences = weights.AsReadOnlySpan();
            MainThreadCheck.Check("InternalSetBoneWeights");
            ThrowIfDestroyed();
            if (m_DeferredModel)
            {
                EnsureModelLoaded();
                counts = bonesPerVertex.AsReadOnlySpan();
                influences = weights.AsReadOnlySpan();
                ThrowIfDestroyed();
            }
            if (counts.Length != 0 && counts.Length != vertexCount)
            {
                Debug.LogError($"Mesh.SetBoneWeights() failed: The size of the bonesPerVertex argument must match the size of Mesh.vertices ({vertexCount}) or zero but was {counts.Length}.");
                return;
            }

            long expected = 0;
            byte maximum = 0;
            foreach (byte count in counts)
            {
                expected += count;
                maximum = Math.Max(maximum, count);
            }
            if (expected != influences.Length)
            {
                Debug.LogError($"Mesh.SetBoneWeights() failed: Expected {expected} bone weights (sum of the elements in bonesPerVertex) but got {influences.Length}.");
                return;
            }
            int emptyVertex = counts.IndexOf((byte)0);
            if (emptyVertex >= 0)
            {
                Debug.LogError($"Mesh.SetBoneWeights() failed: Vertex {emptyVertex} has zero weights.");
                return;
            }
            var layout = maximum switch
            {
                0 => SkinWeights.None,
                1 => SkinWeights.OneBone,
                2 => SkinWeights.TwoBones,
                <= 4 => SkinWeights.FourBones,
                _ => SkinWeights.Unlimited,
            };
            var data = layout == SkinWeights.None ? null : MeshSkinWeightData.FromNative(counts, influences, layout);
            InvalidateBoneWeightArrays();
            m_BoneWeights = data?.LegacyProjection(layout == SkinWeights.Unlimited);
            RuntimeSkinWeights = data;
            m_RuntimeSkinLayout = layout;
            UpdateSkinWeightLayout(layout);
            InvalidatePackedChannel(VertexAttribute.BlendWeight);
            InvalidatePackedChannel(VertexAttribute.BlendIndices);
            Changed(indices: false);
        }

        void UpdateSkinWeightLayout(SkinWeights layout)
        {
            if (m_Layout == null) return;
            Span<VertexAttributeDescriptor> attributes = stackalloc VertexAttributeDescriptor[MaxVertexAttributes];
            int count = 0;
            foreach (var attribute in m_Layout)
                if (attribute.attribute != VertexAttribute.BlendWeight && attribute.attribute != VertexAttribute.BlendIndices)
                    attributes[count++] = attribute;
            if (layout is not (SkinWeights.None or SkinWeights.OneBone))
                attributes[count++] = SkinWeightDescriptor(VertexAttribute.BlendWeight, layout);
            if (layout != SkinWeights.None)
                attributes[count++] = SkinWeightDescriptor(VertexAttribute.BlendIndices, layout);
            m_Layout = attributes.Slice(0, count).ToArray();
        }

        static VertexAttributeDescriptor SkinWeightDescriptor(VertexAttribute attribute, SkinWeights layout)
        {
            bool weight = attribute == VertexAttribute.BlendWeight;
            var format = layout == SkinWeights.Unlimited
                ? (weight ? VertexAttributeFormat.UNorm16 : VertexAttributeFormat.UInt16)
                : (weight ? VertexAttributeFormat.Float32 : VertexAttributeFormat.UInt32);
            return new VertexAttributeDescriptor(attribute, format, layout == SkinWeights.Unlimited ? 4 : (int)layout, 1);
        }

        void InvalidateBoneWeightArrays()
        {
            m_BoneCountsView?.Release();
            m_BoneWeightsView?.Release();
            m_BoneCountsView = null;
            m_BoneWeightsView = null;
            m_ExpandedLegacyWeights = null;
        }

        void ResizeRuntimeBoneWeights(int vertexCount)
        {
            InvalidateBoneWeightArrays();
            if (RuntimeSkinWeights != null) RuntimeSkinWeights = RuntimeSkinWeights.Resize(vertexCount);
        }

        internal void DropRuntimeBoneWeights(bool keepLayout = false)
        {
            InvalidateBoneWeightArrays();
            RuntimeSkinWeights = null;
            if (!keepLayout) m_RuntimeSkinLayout = null;
        }

        void CopyRuntimeBoneWeights(Mesh source)
        {
            InvalidateBoneWeightArrays();
            RuntimeSkinWeights = source.RuntimeSkinWeights;
            m_RuntimeSkinLayout = source.m_RuntimeSkinLayout;
        }
    }
}
