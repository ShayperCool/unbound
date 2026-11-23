using System;
using System.Globalization;
using System.Runtime.InteropServices;
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
        MeshSkinWeightData m_CachedFixedWeights;
        MeshSkinWeightData m_QueriedPackedWeights;
        byte[] m_EmptyFixedWeightCounts;
        BoneWeight[] m_LegacyWeightsBeforeResize;
        BoneWeight[] m_RawFixedBoneWeights;
        bool m_LegacyProjectionRead;
        MeshArrayView<byte> m_BoneCountsView;
        MeshArrayView<BoneWeight1> m_BoneWeightsView;

        internal BoneWeight[] FixedSkinningWeights => m_RawFixedBoneWeights ?? m_BoneWeights;
        internal MeshSkinWeightData VariableSkinningWeights => m_RawFixedBoneWeights == null &&
            (RuntimeSkinWeights?.IsPacked != true || CurrentSkinWeightLayout == SkinWeights.Unlimited) ? RuntimeSkinWeights : null;

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
            (m_BoneWeights == null ? SkinWeights.None :
                m_Layout == null ? SkinWeights.FourBones : SkinLayoutForInfluences(SkinAttributeWidth(m_Layout)));

        MeshSkinWeightData SkinWeightData()
        {
            if (RuntimeSkinWeights is { IsPacked: true } packed) return packed;
            if (m_CachedFixedWeights != null) return m_CachedFixedWeights;
            var fixedWeights = m_RawFixedBoneWeights ?? m_LegacyWeightsBeforeResize ?? m_BoneWeights;
            m_CachedFixedWeights = RuntimeSkinWeights != null && m_RawFixedBoneWeights == null
                ? RuntimeSkinWeights : fixedWeights is { Length: > 0 } ? MeshSkinWeightData.FromLegacy(fixedWeights) : null;
            if (m_CachedFixedWeights is { WeightCount: 0 })
                m_EmptyFixedWeightCounts ??= m_CachedFixedWeights.Counts;
            return m_CachedFixedWeights;
        }

        MeshSkinWeightData QueriedSkinWeightData()
        {
            if (m_QueriedPackedWeights != null) return m_QueriedPackedWeights;
            var data = SkinWeightData();
            if (data?.IsPacked == true) m_QueriedPackedWeights = data;
            return data;
        }

        public NativeArray<byte> GetBonesPerVertex()
        {
            MainThreadCheck.Check("HasBoneWeights");
            ThrowIfDestroyed();
            EnsureModelLoaded();
            ThrowIfDestroyed();
            var data = QueriedSkinWeightData();
            var counts = data?.IsPacked == true ? data.Counts : m_EmptyFixedWeightCounts ?? data?.Counts;
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
            var data = QueriedSkinWeightData();
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
                Log.Engine(LogType.Error, $"Mesh.SetBoneWeights() failed: The size of the bonesPerVertex argument must match the size of Mesh.vertices ({vertexCount}) or zero but was {counts.Length}.", this);
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
                Log.Engine(LogType.Error, $"Mesh.SetBoneWeights() failed: Expected {expected} bone weights (sum of the elements in bonesPerVertex) but got {influences.Length}.", this);
                return;
            }
            int emptyVertex = counts.IndexOf((byte)0);
            if (emptyVertex >= 0)
            {
                Log.Engine(LogType.Error, $"Mesh.SetBoneWeights() failed: Vertex {emptyVertex} has zero weights.", this);
                return;
            }
            var layout = SkinLayoutForInfluences(maximum);
            if (layout == SkinWeights.Unlimited && !ValidateUnlimitedWeightOrder(counts, influences)) return;
            int convertingWidth = layout == SkinWeights.Unlimited && Compat.RenderingMeasured ? FloatSkinWeightWidth() : 0;
            var data = layout == SkinWeights.None ? null : MeshSkinWeightData.FromNative(counts, influences, layout);
            InvalidateBoneWeightArrays();
            m_BoneWeights = data?.LegacyProjection();
            m_RawFixedBoneWeights = null;
            RuntimeSkinWeights = data;
            m_RuntimeSkinLayout = layout;
            UpdateSkinWeightLayout(layout);
            InvalidateBoneWeightBuffers();
            InvalidatePackedChannel(VertexAttribute.BlendWeight);
            InvalidatePackedChannel(VertexAttribute.BlendIndices);
            Changed(indices: false);
            if (convertingWidth != 0)
                Log.Engine(LogType.Error, $"Unsupported conversion of vertex data (format {(int)VertexAttributeFormat.Float32} to {(int)VertexAttributeFormat.UNorm16}, dimensions {convertingWidth} to 4)", this);
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
                    var prior = CurrentSkinWeightLayout == SkinWeights.Unlimited ? RuntimeSkinWeights : null;
                    if (prior != null)
                    {
                        m_Layout ??= Layout();
                        m_RuntimeSkinLayout = SkinWeights.FourBones;
                    }
                    Log.Engine(LogType.Error, $"Vertex {vertex} bone weights are not in descending order! testing !({WeightText(current)} <= {WeightText(previous)})", this);
                    if (prior != null && !m_Destroyed && ReferenceEquals(prior, RuntimeSkinWeights))
                    {
                        m_BoneWeights = ReadFixedVertexWeights();
                        DropRuntimeBoneWeights(keepLayout: true);
                        Changed(indices: false);
                    }
                    return false;
                }
            }
            return true;
        }

        BoneWeight[] ReadFixedVertexWeights()
        {
            VertexAttributeDescriptor weights = default, indices = default;
            foreach (var descriptor in m_Layout)
            {
                if (descriptor.attribute == VertexAttribute.BlendWeight) weights = descriptor;
                if (descriptor.attribute == VertexAttribute.BlendIndices) indices = descriptor;
            }
            if (weights.format != VertexAttributeFormat.UNorm16 || indices.format != VertexAttributeFormat.UInt16 ||
                weights.dimension != 4 || indices.dimension != 4) return m_BoneWeights;

            var values = new BoneWeight[vertexCount];
            Span<ushort> packedWeights = stackalloc ushort[4];
            Span<ushort> packedIndices = stackalloc ushort[4];
            for (int vertex = 0; vertex < values.Length; vertex++)
            {
                PackAttribute(weights, vertex, MemoryMarshal.AsBytes(packedWeights));
                PackAttribute(indices, vertex, MemoryMarshal.AsBytes(packedIndices));
                var w = new Vector4(packedWeights[0], packedWeights[1], packedWeights[2], packedWeights[3]) * MeshSkinWeightData.PackedWeightUnit;
                float total = w.x + w.y + w.z + w.w;
                if (total != 0f) w *= 1f / total;
                values[vertex] = new BoneWeight
                {
                    boneIndex0 = packedIndices[0], weight0 = w.x,
                    boneIndex1 = packedIndices[1], weight1 = w.y,
                    boneIndex2 = packedIndices[2], weight2 = w.z,
                    boneIndex3 = packedIndices[3], weight3 = w.w,
                };
            }
            return values;
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

        static int SkinAttributeWidth(ReadOnlySpan<VertexAttributeDescriptor> layout)
        {
            int width = 0;
            foreach (var descriptor in layout)
                if (descriptor.attribute is VertexAttribute.BlendWeight or VertexAttribute.BlendIndices)
                    width = Math.Max(width, descriptor.dimension);
            return width;
        }

        bool AdoptRawSkinWeightLayout(ReadOnlySpan<VertexAttributeDescriptor> layout, int vertexCount, bool keepBoneWeightBuffers)
        {
            int width = SkinAttributeWidth(layout);
            bool unlimited = CurrentSkinWeightLayout == SkinWeights.Unlimited;
            if (width == 0 && !unlimited)
            {
                DropRuntimeBoneWeights(keepBuffers: keepBoneWeightBuffers);
                return false;
            }

            ChangeBoneWeightVertexCount(vertexCount);
            if (!unlimited && m_RuntimeSkinLayout.HasValue)
                m_RuntimeSkinLayout = SkinLayoutForInfluences(width);
            return true;
        }

        void RestorePackedSkinWeights(MeshSkinWeightData weights, SkinWeights layout)
        {
            RuntimeSkinWeights = weights;
            m_RuntimeSkinLayout = layout;
            if (weights != null && layout == SkinWeights.Unlimited)
            {
                m_BoneWeights = weights.LegacyProjection();
                if (m_BoneWeights.Length != vertexCount) Array.Resize(ref m_BoneWeights, vertexCount);
            }
        }

        BoneWeight[] RawSkinWriteTarget()
        {
            // The raw stream drives fixed skinning. Queries cache the first fixed table they
            // read. The unlimited table is separate from the stream and survives stream writes.
            if (m_CachedFixedWeights?.WeightCount == 0) m_CachedFixedWeights = null;
            if (RuntimeSkinWeights == null) return m_BoneWeights;
            if (RuntimeSkinWeights.IsPacked)
                return CurrentSkinWeightLayout == SkinWeights.Unlimited ? null : m_BoneWeights;
            return m_RawFixedBoneWeights ??= (BoneWeight[])m_BoneWeights.Clone();
        }

        BoneWeight[] ReadLegacyBoneWeights()
        {
            // The first legacy read of a scripted fixed layout projects the stream as it stands.
            // Further stream writes leave that projection and the scripted table cached.
            if (!m_LegacyProjectionRead && RuntimeSkinWeights is { IsPacked: false })
            {
                if (m_RawFixedBoneWeights != null)
                    m_BoneWeights = (BoneWeight[])m_RawFixedBoneWeights.Clone();
                m_LegacyProjectionRead = true;
            }
            return m_BoneWeights;
        }

        void ReleaseBoneWeightViews()
        {
            m_BoneCountsView?.Release();
            m_BoneWeightsView?.Release();
            m_BoneCountsView = null;
            m_BoneWeightsView = null;
        }

        void InvalidateBoneWeightArrays()
        {
            ReleaseBoneWeightViews();
            m_CachedFixedWeights = null;
            m_QueriedPackedWeights = null;
            m_EmptyFixedWeightCounts = null;
            m_LegacyWeightsBeforeResize = null;
            m_LegacyProjectionRead = false;
        }

        void ChangeBoneWeightVertexCount(int vertexCount)
        {
            // Vertex edits resize the per-vertex counts and legacy vertex attributes. The flat
            // skin weight data and existing GPU layouts keep the last weight assignment.
            if (RuntimeSkinWeights == null && m_BoneWeights != null && m_BoneWeights.Length != vertexCount)
                m_LegacyWeightsBeforeResize ??= m_BoneWeights;
            if (m_RawFixedBoneWeights != null && m_RawFixedBoneWeights.Length != vertexCount)
                Array.Resize(ref m_RawFixedBoneWeights, vertexCount);
            ReleaseBoneWeightViews();
        }

        internal void DropRuntimeBoneWeights(bool keepLayout = false, bool keepBuffers = false)
        {
            InvalidateBoneWeightArrays();
            if (!keepBuffers) InvalidateBoneWeightBuffers();
            RuntimeSkinWeights = null;
            m_RawFixedBoneWeights = null;
            if (!keepLayout) m_RuntimeSkinLayout = null;
        }

        void CopyRuntimeBoneWeights(Mesh source)
        {
            InvalidateBoneWeightArrays();
            InvalidateBoneWeightBuffers();
            RuntimeSkinWeights = source.RuntimeSkinWeights;
            m_CachedFixedWeights = source.m_CachedFixedWeights;
            m_QueriedPackedWeights = source.m_QueriedPackedWeights;
            m_EmptyFixedWeightCounts = source.m_EmptyFixedWeightCounts;
            m_RawFixedBoneWeights = (BoneWeight[])source.m_RawFixedBoneWeights?.Clone();
            m_LegacyProjectionRead = source.m_LegacyProjectionRead;
            m_RuntimeSkinLayout = source.m_RuntimeSkinLayout;
            m_LegacyWeightsBeforeResize = source.m_LegacyWeightsBeforeResize;
        }
    }
}
