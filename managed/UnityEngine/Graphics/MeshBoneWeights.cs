using System;
using System.Globalization;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public sealed partial class Mesh
    {
        SkinWeights? m_RuntimeSkinLayout;
        internal MeshSkinWeightData RuntimeSkinWeights { get; private set; }
        MeshSkinWeightData m_ExpandedLegacyWeights;
        BoneWeight[] m_LegacyWeightsBeforeResize;
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

        MeshSkinWeightData SkinWeightData()
        {
            if (RuntimeSkinWeights != null) return RuntimeSkinWeights;
            var legacy = m_LegacyWeightsBeforeResize ?? m_BoneWeights;
            return legacy is { Length: > 0 } ? (m_ExpandedLegacyWeights ??= MeshSkinWeightData.FromLegacy(legacy)) : null;
        }

        public NativeArray<byte> GetBonesPerVertex()
        {
            MainThreadCheck.Check("HasBoneWeights");
            ThrowIfDestroyed();
            EnsureModelLoaded();
            ThrowIfDestroyed();
            var counts = SkinWeightData()?.Counts;
            int count = counts == null ? 0 : vertexCount;
            if (m_BoneCountsView == null)
            {
                if (count == 0) counts = null;
                else Array.Resize(ref counts, count);
                m_BoneCountsView = new MeshArrayView<byte>(counts);
            }
            return m_BoneCountsView.Borrow(count);
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
            var counts = ReadWeightInput(bonesPerVertex, nameof(bonesPerVertex));
            var influences = ReadWeightInput(weights, nameof(weights));
            MainThreadCheck.Check("InternalSetBoneWeights");
            ThrowIfDestroyed();
            if (m_DeferredModel)
            {
                EnsureModelLoaded();
                counts = ReadWeightInput(bonesPerVertex, nameof(bonesPerVertex));
                influences = ReadWeightInput(weights, nameof(weights));
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
            var layout = SkinLayoutForInfluences(maximum);
            if (layout == SkinWeights.Unlimited && !ValidateUnlimitedWeightOrder(counts, influences)) return;
            int convertingWidth = layout == SkinWeights.Unlimited && Compat.RenderingMeasured ? FloatSkinWeightWidth() : 0;
            var data = layout == SkinWeights.None ? null : MeshSkinWeightData.FromNative(counts, influences, layout);
            InvalidateBoneWeightArrays();
            m_BoneWeights = data?.LegacyProjection(layout == SkinWeights.Unlimited);
            RuntimeSkinWeights = data;
            m_RuntimeSkinLayout = layout;
            UpdateSkinWeightLayout(layout);
            InvalidateBoneWeightBuffers();
            InvalidatePackedChannel(VertexAttribute.BlendWeight);
            InvalidatePackedChannel(VertexAttribute.BlendIndices);
            Changed(indices: false);
            if (convertingWidth != 0)
                Debug.LogError($"Unsupported conversion of vertex data (format {(int)VertexAttributeFormat.Float32} to {(int)VertexAttributeFormat.UNorm16}, dimensions {convertingWidth} to 4)");
        }

        static SkinWeights SkinLayoutForInfluences(int count) => count switch
        {
            <= 0 => SkinWeights.None,
            1 => SkinWeights.OneBone,
            2 => SkinWeights.TwoBones,
            <= 4 => SkinWeights.FourBones,
            _ => SkinWeights.Unlimited,
        };

        bool ValidateUnlimitedWeightOrder(ReadOnlySpan<byte> counts, ReadOnlySpan<BoneWeight1> weights)
        {
            int offset = 0;
            for (int vertex = 0; vertex < counts.Length; vertex++)
            {
                var row = weights.Slice(offset, counts[vertex]);
                offset += row.Length;
                for (int i = 1; i < row.Length; i++)
                {
                    float previous = row[i - 1].weight;
                    float current = row[i].weight;
                    if (current <= previous) continue;
                    if (CurrentSkinWeightLayout == SkinWeights.Unlimited) SetLegacyBoneWeights(m_BoneWeights);
                    Debug.LogError($"Vertex {vertex} bone weights are not in descending order! testing !({WeightText(current)} <= {WeightText(previous)})");
                    return false;
                }
            }
            return true;
        }

        static string WeightText(float weight)
        {
            if (float.IsNaN(weight)) return "nan";
            if (float.IsPositiveInfinity(weight)) return "inf";
            if (float.IsNegativeInfinity(weight)) return "-inf";
            return weight.ToString("F9", CultureInfo.InvariantCulture);
        }

        int FloatSkinWeightWidth()
        {
            Span<VertexAttributeDescriptor> attributes = stackalloc VertexAttributeDescriptor[MaxVertexAttributes];
            int count = FillLayout(attributes);
            for (int i = 0; i < count; i++)
                if (attributes[i].attribute == VertexAttribute.BlendWeight && attributes[i].format == VertexAttributeFormat.Float32)
                    return attributes[i].dimension;
            return 0;
        }

        static ReadOnlySpan<T> ReadWeightInput<T>(NativeArray<T> input, string parameter) where T : struct
        {
            if (AtomicSafetyHandle.IsDefaultValue(NativeArrayUnsafeUtility.GetAtomicSafetyHandle(input)))
                throw new NullReferenceException("Object reference not set to an instance of an object");
            if (input.Length != 0 && !input.IsCreated) throw new ObjectDisposedException(parameter);
            return input.AsReadOnlySpan();
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
            m_LegacyWeightsBeforeResize = null;
        }

        void ChangeBoneWeightVertexCount(int vertexCount)
        {
            // Vertex edits resize the per-vertex counts and legacy vertex attributes. The flat
            // skin weight data and existing GPU layouts keep the last weight assignment.
            if (RuntimeSkinWeights == null && m_BoneWeights != null && m_BoneWeights.Length != vertexCount)
                m_LegacyWeightsBeforeResize ??= m_BoneWeights;
            m_BoneCountsView?.Release();
            m_BoneCountsView = null;
        }

        internal void DropRuntimeBoneWeights(bool keepLayout = false)
        {
            InvalidateBoneWeightArrays();
            InvalidateBoneWeightBuffers();
            RuntimeSkinWeights = null;
            if (!keepLayout) m_RuntimeSkinLayout = null;
        }

        void CopyRuntimeBoneWeights(Mesh source)
        {
            InvalidateBoneWeightArrays();
            InvalidateBoneWeightBuffers();
            RuntimeSkinWeights = source.RuntimeSkinWeights;
            m_RuntimeSkinLayout = source.m_RuntimeSkinLayout;
            m_LegacyWeightsBeforeResize = source.m_LegacyWeightsBeforeResize;
        }
    }
}
