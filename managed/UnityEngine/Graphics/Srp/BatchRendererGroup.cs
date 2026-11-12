using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace UnityEngine.Rendering
{
    public partial struct BatchID : IEquatable<BatchID>
    {
        public static readonly BatchID Null = new BatchID { value = 0 };
        public uint value;

        public override int GetHashCode() => value.GetHashCode();
        public override bool Equals(object obj) => obj is BatchID other && Equals(other);
        public bool Equals(BatchID other) => value == other.value;
        public int CompareTo(BatchID other) => value.CompareTo(other.value);
        public static bool operator ==(BatchID a, BatchID b) => a.Equals(b);
        public static bool operator !=(BatchID a, BatchID b) => !a.Equals(b);
    }

    public partial struct BatchMaterialID : IEquatable<BatchMaterialID>
    {
        public static readonly BatchMaterialID Null = new BatchMaterialID { value = 0 };
        public uint value;

        public override int GetHashCode() => value.GetHashCode();
        public override bool Equals(object obj) => obj is BatchMaterialID other && Equals(other);
        public bool Equals(BatchMaterialID other) => value == other.value;
        public int CompareTo(BatchMaterialID other) => value.CompareTo(other.value);
        public static bool operator ==(BatchMaterialID a, BatchMaterialID b) => a.Equals(b);
        public static bool operator !=(BatchMaterialID a, BatchMaterialID b) => !a.Equals(b);
    }

    public partial struct BatchMeshID : IEquatable<BatchMeshID>
    {
        public static readonly BatchMeshID Null = new BatchMeshID { value = 0 };
        public uint value;

        public override int GetHashCode() => value.GetHashCode();
        public override bool Equals(object obj) => obj is BatchMeshID other && Equals(other);
        public bool Equals(BatchMeshID other) => value == other.value;
        public int CompareTo(BatchMeshID other) => value.CompareTo(other.value);
        public static bool operator ==(BatchMeshID a, BatchMeshID b) => a.Equals(b);
        public static bool operator !=(BatchMeshID a, BatchMeshID b) => !a.Equals(b);
    }

    /// <summary>A culling view: the instance ID of its camera or light and its slice (a shadow
    /// cube face or cascade group), packed as the low and high 32 bits.</summary>
    public partial struct BatchPackedCullingViewID : IEquatable<BatchPackedCullingViewID>
    {
        internal ulong handle;

        public BatchPackedCullingViewID(int instanceID, int sliceIndex) => handle = (uint)instanceID | ((ulong)(uint)sliceIndex << 32);

        public int GetInstanceID() => (int)(handle & 0xffffffff);
        public int GetSliceIndex() => (int)(handle >> 32);
        public override int GetHashCode() => handle.GetHashCode();
        public bool Equals(BatchPackedCullingViewID other) => handle == other.handle;
        public override bool Equals(object obj) => obj is BatchPackedCullingViewID other && Equals(other);
        public static bool operator ==(BatchPackedCullingViewID lhs, BatchPackedCullingViewID rhs) => lhs.Equals(rhs);
        public static bool operator !=(BatchPackedCullingViewID lhs, BatchPackedCullingViewID rhs) => !lhs.Equals(rhs);
    }

    public partial struct MetadataValue
    {
        public int NameID;
        public uint Value;
    }

    public partial struct CullingSplit
    {
        public Vector3 sphereCenter;
        public float sphereRadius;
        public int cullingPlaneOffset;
        public int cullingPlaneCount;
        public float cascadeBlendCullingFactor;
        public float nearPlane;
        public Matrix4x4 cullingMatrix;
    }

    public partial struct BatchCullingContext
    {
        public readonly NativeArray<Plane> cullingPlanes;
        public readonly NativeArray<CullingSplit> cullingSplits;
        public readonly LODParameters lodParameters;
        public readonly Matrix4x4 localToWorldMatrix;
        public readonly BatchCullingViewType viewType;
        public readonly BatchCullingProjectionType projectionType;
        public readonly BatchCullingFlags cullingFlags;
        public readonly BatchPackedCullingViewID viewID;
        public readonly uint cullingLayerMask;
        public readonly ulong sceneCullingMask;
        public readonly ushort splitExclusionMask;
        [Obsolete("BatchCullingContext.isOrthographic is deprecated. Use BatchCullingContext.projectionType instead.")]
        public readonly byte isOrthographic;
        public readonly int receiverPlaneOffset;
        public readonly int receiverPlaneCount;
        internal readonly IntPtr occlusionBuffer;

        internal BatchCullingContext(NativeArray<Plane> inCullingPlanes, NativeArray<CullingSplit> inCullingSplits, LODParameters inLodParameters,
            Matrix4x4 inLocalToWorldMatrix, BatchCullingViewType inViewType, BatchCullingProjectionType inProjectionType, BatchCullingFlags inBatchCullingFlags,
            ulong inViewID, uint inCullingLayerMask, ulong inSceneCullingMask, byte inExclusionSplitMask, int inReceiverPlaneOffset, int inReceiverPlaneCount,
            IntPtr inOcclusionBuffer)
        {
            cullingPlanes = inCullingPlanes;
            cullingSplits = inCullingSplits;
            lodParameters = inLodParameters;
            localToWorldMatrix = inLocalToWorldMatrix;
            viewType = inViewType;
            projectionType = inProjectionType;
            cullingFlags = inBatchCullingFlags;
            viewID = new BatchPackedCullingViewID { handle = inViewID };
            cullingLayerMask = inCullingLayerMask;
            sceneCullingMask = inSceneCullingMask;
            splitExclusionMask = inExclusionSplitMask;
#pragma warning disable 618
            isOrthographic = (byte)(inProjectionType == BatchCullingProjectionType.Orthographic ? 1 : 0);
#pragma warning restore 618
            receiverPlaneOffset = inReceiverPlaneOffset;
            receiverPlaneCount = inReceiverPlaneCount;
            occlusionBuffer = inOcclusionBuffer;
        }
    }

    public partial struct BatchCullingOutput
    {
        public NativeArray<BatchCullingOutputDrawCommands> drawCommands;
        public NativeArray<IntPtr> customCullingResult;
    }

    public unsafe partial struct BatchCullingOutputDrawCommands
    {
        public BatchDrawCommand* drawCommands;
        public BatchDrawCommandIndirect* indirectDrawCommands;
        public BatchDrawCommandProcedural* proceduralDrawCommands;
        public BatchDrawCommandProceduralIndirect* proceduralIndirectDrawCommands;
        public int* visibleInstances;
        public BatchDrawRange* drawRanges;
        public float* instanceSortingPositions;
        public int* drawCommandPickingInstanceIDs;
        public int drawCommandCount;
        public int indirectDrawCommandCount;
        public int proceduralDrawCommandCount;
        public int proceduralIndirectDrawCommandCount;
        public int visibleInstanceCount;
        public int drawRangeCount;
        public int instanceSortingPositionFloatCount;
    }

    public partial struct BatchDrawCommand
    {
        public BatchDrawCommandFlags flags;
        public BatchID batchID;
        public BatchMaterialID materialID;
        public ushort splitVisibilityMask;
        public ushort lightmapIndex;
        public int sortingPosition;
        public uint visibleOffset;
        public uint visibleCount;
        public BatchMeshID meshID;
        public ushort submeshIndex;
    }

    public partial struct BatchDrawCommandIndirect
    {
        public BatchDrawCommandFlags flags;
        public BatchID batchID;
        public BatchMaterialID materialID;
        public ushort splitVisibilityMask;
        public ushort lightmapIndex;
        public int sortingPosition;
        public uint visibleOffset;
        public BatchMeshID meshID;
        public MeshTopology topology;
        public GraphicsBufferHandle visibleInstancesBufferHandle;
        public uint visibleInstancesBufferWindowOffset;
        public uint visibleInstancesBufferWindowSizeBytes;
        public GraphicsBufferHandle indirectArgsBufferHandle;
        public uint indirectArgsBufferOffset;
    }

    public partial struct BatchDrawCommandProcedural
    {
        public BatchDrawCommandFlags flags;
        public BatchID batchID;
        public BatchMaterialID materialID;
        public ushort splitVisibilityMask;
        public ushort lightmapIndex;
        public int sortingPosition;
        public uint visibleOffset;
        public uint visibleCount;
        public MeshTopology topology;
        public GraphicsBufferHandle indexBufferHandle;
        public uint baseVertex;
        public uint indexOffsetBytes;
        public uint elementCount;
    }

    public partial struct BatchDrawCommandProceduralIndirect
    {
        public BatchDrawCommandFlags flags;
        public BatchID batchID;
        public BatchMaterialID materialID;
        public ushort splitVisibilityMask;
        public ushort lightmapIndex;
        public int sortingPosition;
        public uint visibleOffset;
        public MeshTopology topology;
        public GraphicsBufferHandle indexBufferHandle;
        public GraphicsBufferHandle visibleInstancesBufferHandle;
        public uint visibleInstancesBufferWindowOffset;
        public uint visibleInstancesBufferWindowSizeBytes;
        public GraphicsBufferHandle indirectArgsBufferHandle;
        public uint indirectArgsBufferOffset;
    }

    public partial struct BatchDrawRange
    {
        public BatchDrawCommandType drawCommandsType;
        public uint drawCommandsBegin;
        public uint drawCommandsCount;
        public BatchFilterSettings filterSettings;
    }

    public partial struct BatchFilterSettings
    {
        public uint renderingLayerMask;
        public int rendererPriority;
        ulong m_sceneCullingMask;
        public byte layer;
        byte m_batchLayer;
        byte m_motionMode;
        byte m_shadowMode;
        byte m_receiveShadows;
        byte m_staticShadowCaster;
        byte m_allDepthSorted;
        byte m_isSceneCullingMaskSet;

        public byte batchLayer { get => m_batchLayer; set => m_batchLayer = value; }
        public MotionVectorGenerationMode motionMode { get => (MotionVectorGenerationMode)m_motionMode; set => m_motionMode = (byte)value; }
        public ShadowCastingMode shadowCastingMode { get => (ShadowCastingMode)m_shadowMode; set => m_shadowMode = (byte)value; }
        public bool receiveShadows { get => m_receiveShadows != 0; set => m_receiveShadows = (byte)(value ? 1 : 0); }
        public bool staticShadowCaster { get => m_staticShadowCaster != 0; set => m_staticShadowCaster = (byte)(value ? 1 : 0); }
        public bool allDepthSorted { get => m_allDepthSorted != 0; set => m_allDepthSorted = (byte)(value ? 1 : 0); }
        /// <summary>Every scene's objects until set.</summary>
        public ulong sceneCullingMask
        {
            get => m_isSceneCullingMaskSet != 0 ? m_sceneCullingMask : ulong.MaxValue;
            set { m_isSceneCullingMaskSet = 1; m_sceneCullingMask = value; }
        }
    }

    public partial struct BatchRendererGroupCreateInfo
    {
        public BatchRendererGroup.OnPerformCulling cullingCallback;
        public BatchRendererGroup.OnFinishedCulling finishedCullingCallback;
        public IntPtr userContext;
    }

    /// <summary>The batch calls of a group for jobs: they reach the group through its handle and
    /// take its lock.</summary>
    public partial struct ThreadedBatchContext
    {
        public IntPtr batchRendererGroup;

        BatchRendererGroup Group => batchRendererGroup == IntPtr.Zero ? null : GCHandle.FromIntPtr(batchRendererGroup).Target as BatchRendererGroup
            ?? throw new ObjectDisposedException(nameof(BatchRendererGroup));

        public BatchID AddBatch(NativeArray<MetadataValue> batchMetadata, GraphicsBufferHandle buffer) => Group.AddBatch(batchMetadata, buffer);
        public BatchID AddBatch(NativeArray<MetadataValue> batchMetadata, GraphicsBufferHandle buffer, uint bufferOffset, uint windowSize) =>
            Group.AddBatch(batchMetadata, buffer, bufferOffset, windowSize);
        public void SetBatchBuffer(BatchID batchID, GraphicsBufferHandle buffer) => Group.SetBatchBuffer(batchID, buffer);
        public void RemoveBatch(BatchID batchID) => Group.RemoveBatch(batchID);
    }

    /// <summary>
    /// Instanced rendering the caller culls itself: registered meshes and materials, batches of
    /// instance data in a GraphicsBuffer described by metadata values, and a culling callback that
    /// every scriptable culling of an enabled view type invokes to get draw commands
    /// (<see cref="Unbound.Runtime.BatchCulling"/>). The draws join the culling's renderer lists
    /// with their materials' DOTS instancing variants.
    /// </summary>
    public partial class BatchRendererGroup : IDisposable
    {
        public delegate JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext);
        public delegate void OnFinishedCulling(IntPtr customCullingResult);

        /// <summary>The live groups, in creation order (the order their callbacks run in).</summary>
        internal static readonly List<BatchRendererGroup> s_Groups = new List<BatchRendererGroup>();

        /// <summary>A registered mesh or material and its number of owners.</summary>
        internal struct Registered<T> where T : class
        {
            internal T Value;
            internal int Owners;
        }

        /// <summary>A batch: its buffer, the window a constant buffer is read through, and its
        /// metadata as the draw properties that carry it (see <see cref="Unbound.Runtime.BatchCulling"/>).</summary>
        internal sealed class Batch
        {
            internal GraphicsBufferHandle Buffer;
            internal uint Offset;
            internal uint Window;
            internal readonly Dictionary<int, object> Properties = new Dictionary<int, object>();
        }

        internal readonly OnPerformCulling m_Culling;
        internal readonly OnFinishedCulling m_Finished;
        internal readonly IntPtr m_UserContext;
        // Index 0 is the null ID: IDs are 1-based, as the Editor gives them.
        internal readonly List<Registered<Mesh>> m_Meshes = new List<Registered<Mesh>> { default };
        internal readonly List<Registered<Material>> m_Materials = new List<Registered<Material>> { default };
        internal readonly Dictionary<Mesh, uint> m_MeshIds = new Dictionary<Mesh, uint>();
        internal readonly Dictionary<Material, uint> m_MaterialIds = new Dictionary<Material, uint>();
        internal readonly List<Batch> m_Batches = new List<Batch> { null };
        /// <summary>The view types whose cullings invoke the callback, one bit per type.</summary>
        internal uint m_ViewTypes = AllViewTypes;
        internal Bounds? m_GlobalBounds;
        GCHandle m_Handle;
        bool m_Disposed;
        internal readonly object m_Lock = new object();

        const uint AllViewTypes = uint.MaxValue;

        public BatchRendererGroup(OnPerformCulling cullingCallback, IntPtr userContext)
            : this(new BatchRendererGroupCreateInfo { cullingCallback = cullingCallback, userContext = userContext }) { }

        public BatchRendererGroup(BatchRendererGroupCreateInfo info)
        {
            m_Culling = info.cullingCallback ?? throw new ArgumentNullException("cullingCallback");
            m_Finished = info.finishedCullingCallback;
            m_UserContext = info.userContext;
            m_Handle = GCHandle.Alloc(this, GCHandleType.Weak);
            s_Groups.Add(this);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            s_Groups.Remove(this);
            if (m_Handle.IsAllocated) m_Handle.Free();
        }

        public ThreadedBatchContext GetThreadedBatchContext() => new ThreadedBatchContext { batchRendererGroup = GCHandle.ToIntPtr(m_Handle) };

        // The Editor's answers on the run's device (measured on both: CapsProbe).
        public static BatchBufferTarget BufferTarget => Unbound.Runtime.GraphicsDeviceProfile.Current.BatchBufferTarget;

        [Unbound.EditorFaithful("the Editor's answer on both devices (measured: tests/projects/urp CapsProbe, tests/projects/brg)")]
        public static int GetConstantBufferMaxWindowSize() => Unbound.Runtime.BatchCulling.MaxWindowSize;

        [Unbound.EditorFaithful("the device's answer (measured on both: tests/projects/urp CapsProbe)")]
        public static int GetConstantBufferOffsetAlignment() => Unbound.Runtime.GraphicsDeviceProfile.Current.ConstantBufferOffsetAlignment;

        public BatchID AddBatch(NativeArray<MetadataValue> batchMetadata, GraphicsBufferHandle buffer) => AddBatch(batchMetadata, buffer, 0, 0);

        public BatchID AddBatch(NativeArray<MetadataValue> batchMetadata, GraphicsBufferHandle buffer, uint bufferOffset, uint windowSize)
        {
            if (!WindowAccepted(bufferOffset, windowSize)) return BatchID.Null;
            var batch = new Batch();
            foreach (var value in batchMetadata)
                batch.Properties[Unbound.Runtime.BatchCulling.MetadataProperty(value.NameID)] = unchecked((int)value.Value);
            lock (m_Lock)
            {
                SetBuffer(batch, buffer, bufferOffset, windowSize);
                m_Batches.Add(batch);
                return new BatchID { value = (uint)(m_Batches.Count - 1) };
            }
        }

        /// <summary>
        /// Whether the device takes AddBatch's window, else the Editor's error and no batch
        /// (BatchID.Null): a constant buffer needs a window, at an offset the device's constant
        /// buffer alignment divides and no larger than its constant buffer size (SystemInfo's
        /// maxConstantBufferSize: a 32768-byte window is taken, a 65792-byte one refused); a raw
        /// buffer takes neither a window nor an offset. Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/brg <c>errors.add</c>.
        /// </summary>
        static bool WindowAccepted(uint offset, uint window)
        {
            if (BufferTarget == BatchBufferTarget.RawBuffer)
            {
                if (offset == 0 && window == 0) return true;
                Debug.LogError($"BatchRendererGroup.AddBatch had non-zero ConstantBuffer mode parameters when BufferTarget is RawBuffer. Pass zero for both windowOffset and windowSize in RawBuffer mode. (got windowOffset: {offset}, windowSize: {window})");
                return false;
            }
            if (window == 0)
            {
                Debug.LogError("BatchRendererGroup.AddBatch requires a non-zero windowSize parameter when BufferTarget is ConstantBuffer.");
                return false;
            }
            uint alignment = (uint)GetConstantBufferOffsetAlignment();
            if (alignment > 0 && offset % alignment != 0)
            {
                Debug.LogError($"BatchRendererGroup.AddBatch windowOffset parameter ({offset}) is not aligned according to the constant buffer alignment requirement of the Graphics API (constantBufferOffsetAlignment: {alignment}).");
                return false;
            }
            uint limit = (uint)SystemInfo.maxConstantBufferSize;
            if (window > limit)
            {
                Debug.LogError($"BatchRendererGroup.AddBatch windowSize parameter ({window}) is over the limit allowed by the Graphics API (maxConstantBufferSize: {limit}).");
                return false;
            }
            return true;
        }

        static void SetBuffer(Batch batch, GraphicsBufferHandle handle, uint offset, uint window)
        {
            batch.Buffer = handle;
            batch.Offset = offset;
            batch.Window = window;
            // The buffer by its handle, as unity_DOTSInstanceData (a byte address buffer, or the
            // constant buffer the window is read from).
            batch.Properties[Unbound.Runtime.BatchCulling.InstanceDataId] = handle;
        }

        public void RemoveBatch(BatchID batchID)
        {
            lock (m_Lock)
                if (batchID.value > 0 && batchID.value < m_Batches.Count) m_Batches[(int)batchID.value] = null;
        }

        public void SetBatchBuffer(BatchID batchID, GraphicsBufferHandle buffer)
        {
            lock (m_Lock)
            {
                var batch = GetBatch(batchID);
                if (batch != null) SetBuffer(batch, buffer, batch.Offset, batch.Window);
            }
        }

        internal Batch GetBatch(BatchID id) => id.value > 0 && id.value < m_Batches.Count ? m_Batches[(int)id.value] : null;

        public BatchMaterialID RegisterMaterial(Material material) => new BatchMaterialID { value = Register(m_Materials, m_MaterialIds, material) };

        public BatchMeshID RegisterMesh(Mesh mesh) => new BatchMeshID { value = Register(m_Meshes, m_MeshIds, mesh) };

        internal void RegisterMaterials(ReadOnlySpan<int> materialID, Span<BatchMaterialID> batchMaterialID)
        {
            for (int i = 0; i < materialID.Length && i < batchMaterialID.Length; i++)
                batchMaterialID[i] = RegisterMaterial(Unbound.Runtime.ObjectRegistry.FindById(materialID[i]) as Material);
        }

        internal void RegisterMeshes(ReadOnlySpan<int> meshID, Span<BatchMeshID> batchMeshID)
        {
            for (int i = 0; i < meshID.Length && i < batchMeshID.Length; i++)
                batchMeshID[i] = RegisterMesh(Unbound.Runtime.ObjectRegistry.FindById(meshID[i]) as Mesh);
        }

        /// <summary>A registration: the object's ID, one more owner for one already registered; a
        /// new object takes the next ID.</summary>
        static uint Register<T>(List<Registered<T>> table, Dictionary<T, uint> ids, T value) where T : Object
        {
            if (value == null) return 0;
            if (ids.TryGetValue(value, out uint id))
            {
                var entry = table[(int)id];
                entry.Owners++;
                table[(int)id] = entry;
                return id;
            }
            table.Add(new Registered<T> { Value = value, Owners = 1 });
            id = (uint)(table.Count - 1);
            ids[value] = id;
            return id;
        }

        /// <summary>One owner fewer; the last one's leaving forgets the object (its ID is not
        /// reused).</summary>
        static void Unregister<T>(List<Registered<T>> table, Dictionary<T, uint> ids, uint id) where T : Object
        {
            if (id == 0 || id >= table.Count || table[(int)id].Value is not T value) return;
            var entry = table[(int)id];
            if (--entry.Owners > 0)
            {
                table[(int)id] = entry;
                return;
            }
            ids.Remove(value);
            table[(int)id] = default;
        }

        public void UnregisterMaterial(BatchMaterialID material) => Unregister(m_Materials, m_MaterialIds, material.value);

        public void UnregisterMesh(BatchMeshID mesh) => Unregister(m_Meshes, m_MeshIds, mesh.value);

        public Material GetRegisteredMaterial(BatchMaterialID material) =>
            material.value < m_Materials.Count ? m_Materials[(int)material.value].Value : null;

        public Mesh GetRegisteredMesh(BatchMeshID mesh) => mesh.value < m_Meshes.Count ? m_Meshes[(int)mesh.value].Value : null;

        /// <summary>Kept: Unity culls a group by these bounds; Unbound invokes the callback of every
        /// group for every culling (it does no culling of the group's own).</summary>
        public void SetGlobalBounds(Bounds bounds) => m_GlobalBounds = bounds;

        public void SetPickingMaterial(Material material) => Unbound.Runtime.Stubs.Ignored("BatchRendererGroup.SetPickingMaterial", "has no effect: Unbound has no Scene view picking");

        public void SetErrorMaterial(Material material) => Unbound.Runtime.Stubs.Ignored("BatchRendererGroup.SetErrorMaterial", "is not honoured yet: draws of shaders without DOTS instancing are skipped with a warning");

        public void SetLoadingMaterial(Material material) => Unbound.Runtime.Stubs.Ignored("BatchRendererGroup.SetLoadingMaterial", "has no effect: shaders compile before they draw");

        public void SetEnabledViewTypes(BatchCullingViewType[] viewTypes)
        {
            uint bits = 0;
            if (viewTypes != null)
                foreach (var type in viewTypes) bits |= 1u << (int)type;
            m_ViewTypes = bits;
        }

        internal bool Enabled(BatchCullingViewType type) => (m_ViewTypes & (1u << (int)type)) != 0;

        internal static bool OcclusionTestAABB(IntPtr occlusionBuffer, Bounds aabb) => true;
    }
}
