using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;
using UnityEngine.U2D;

namespace UnityEngine.U2D
{
    /// <summary>A bone of a sprite (2D Animation): the root's position is in units from the
    /// sprite's pivot, a child's in its parent's space.</summary>
    [Serializable]
    public partial struct SpriteBone
    {
        [SerializeField] string m_Name;
        [SerializeField] string m_Guid;
        [SerializeField] Vector3 m_Position;
        [SerializeField] Quaternion m_Rotation;
        [SerializeField] float m_Length;
        [SerializeField] int m_ParentId;
        [SerializeField] Color32 m_Color;

        public string name { get => m_Name; set => m_Name = value; }
        public string guid { get => m_Guid; set => m_Guid = value; }
        public Vector3 position { get => m_Position; set => m_Position = value; }
        public Quaternion rotation { get => m_Rotation; set => m_Rotation = value; }
        public float length { get => m_Length; set => m_Length = value; }
        public int parentId { get => m_ParentId; set => m_ParentId = value; }
        public Color32 color { get => m_Color; set => m_Color = value; }
    }

    /// <summary>
    /// A sprite's mesh as the data access API gives it: a vertex count, one buffer per channel
    /// (Position, TexCoord0, and Tangent and BlendWeight for skinned sprites, or whatever was set),
    /// indices and bind poses, all in unmanaged memory under one safety handle. Built from the
    /// sprite's geometry the first time it is asked for; what is set through it becomes the
    /// sprite's geometry.
    /// </summary>
    internal sealed unsafe class SpriteMeshData
    {
        internal int VertexCount;
        internal readonly Dictionary<VertexAttribute, IntPtr> Channels = new Dictionary<VertexAttribute, IntPtr>();
        internal ushort* Indices;
        internal int IndexCount;
        internal Matrix4x4* BindPoses;
        internal int BindPoseCount;
        internal AtomicSafetyHandle Safety = AtomicSafetyHandle.Create();

        internal static int ElementSize(VertexAttribute channel)
        {
            switch (channel)
            {
                case VertexAttribute.Position:
                case VertexAttribute.Normal:
                    return 12;
                case VertexAttribute.Tangent:
                    return 16;
                case VertexAttribute.Color:
                    return 4;
                case VertexAttribute.BlendWeight:
                    return sizeof(BoneWeight);
                default:
                    return 8;
            }
        }

        internal byte* Channel(VertexAttribute channel) => Channels.TryGetValue(channel, out var p) ? (byte*)p : null;

        /// <summary>The channel's buffer, made (zeroed) when missing.</summary>
        internal byte* EnsureChannel(VertexAttribute channel)
        {
            if (Channels.TryGetValue(channel, out var p)) return (byte*)p;
            long bytes = Math.Max(1, VertexCount) * (long)ElementSize(channel);
            var data = (byte*)UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(data, bytes);
            Channels[channel] = (IntPtr)data;
            return data;
        }

        internal void Resize(int count)
        {
            foreach (var channel in new List<VertexAttribute>(Channels.Keys))
            {
                int size = ElementSize(channel);
                var old = (byte*)Channels[channel];
                var data = (byte*)UnsafeUtility.Malloc(Math.Max(1, count) * (long)size, 16, Allocator.Persistent);
                UnsafeUtility.MemClear(data, Math.Max(1, count) * (long)size);
                UnsafeUtility.MemCpy(data, old, Math.Min(count, VertexCount) * (long)size);
                UnsafeUtility.Free(old, Allocator.Persistent);
                Channels[channel] = (IntPtr)data;
            }
            VertexCount = count;
        }

        internal void SetIndices(ushort* source, int count)
        {
            if (Indices != null) UnsafeUtility.Free(Indices, Allocator.Persistent);
            Indices = (ushort*)UnsafeUtility.Malloc(Math.Max(1, count) * 2L, 16, Allocator.Persistent);
            if (count > 0) UnsafeUtility.MemCpy(Indices, source, count * 2L);
            IndexCount = count;
        }

        internal void SetBindPoses(Matrix4x4* source, int count)
        {
            if (BindPoses != null) UnsafeUtility.Free(BindPoses, Allocator.Persistent);
            BindPoses = (Matrix4x4*)UnsafeUtility.Malloc(Math.Max(1, count) * (long)sizeof(Matrix4x4), 16, Allocator.Persistent);
            if (count > 0) UnsafeUtility.MemCpy(BindPoses, source, count * (long)sizeof(Matrix4x4));
            BindPoseCount = count;
        }

        internal void Free()
        {
            foreach (var p in Channels.Values) UnsafeUtility.Free((void*)p, Allocator.Persistent);
            Channels.Clear();
            if (Indices != null) UnsafeUtility.Free(Indices, Allocator.Persistent);
            if (BindPoses != null) UnsafeUtility.Free(BindPoses, Allocator.Persistent);
            Indices = null;
            BindPoses = null;
            VertexCount = IndexCount = BindPoseCount = 0;
            AtomicSafetyHandle.Release(Safety);
        }
    }

    /// <summary>Reads and writes a sprite's mesh, bones and bind poses (2D Animation reads skinned
    /// sprites through it).</summary>
    public static unsafe partial class SpriteDataAccessExtensions
    {
        static void CheckAttributeTypeMatchesAndThrow<T>(VertexAttribute channel)
        {
            bool matches;
            switch (channel)
            {
                case VertexAttribute.Position:
                case VertexAttribute.Normal:
                    matches = typeof(T) == typeof(Vector3);
                    break;
                case VertexAttribute.Tangent:
                    matches = typeof(T) == typeof(Vector4);
                    break;
                case VertexAttribute.Color:
                    matches = typeof(T) == typeof(Color32);
                    break;
                case VertexAttribute.TexCoord0:
                case VertexAttribute.TexCoord1:
                case VertexAttribute.TexCoord2:
                case VertexAttribute.TexCoord3:
                case VertexAttribute.TexCoord4:
                case VertexAttribute.TexCoord5:
                case VertexAttribute.TexCoord6:
                case VertexAttribute.TexCoord7:
                    matches = typeof(T) == typeof(Vector2);
                    break;
                case VertexAttribute.BlendWeight:
                    matches = typeof(T) == typeof(BoneWeight);
                    break;
                default:
                    throw new InvalidOperationException($"The requested channel '{channel}' is unknown.");
            }
            if (!matches)
                throw new InvalidOperationException($"The requested channel '{channel}' does not match the return type {typeof(T).Name}.");
        }

        static void CheckSprite(Sprite sprite)
        {
            if ((object)sprite == null) throw new ArgumentNullException(nameof(sprite));
            if (!sprite) throw new ArgumentNullException(nameof(sprite));
        }

        /// <summary>The channel's values (an empty slice when the sprite has no such channel).</summary>
        public static NativeSlice<T> GetVertexAttribute<T>(this Sprite sprite, VertexAttribute channel) where T : struct
        {
            CheckAttributeTypeMatchesAndThrow<T>(channel);
            CheckSprite(sprite);
            var data = sprite.MeshData();
            var buffer = data.Channel(channel);
            int size = SpriteMeshData.ElementSize(channel);
            var slice = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<T>(buffer, size, buffer != null ? data.VertexCount : 0);
            NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref slice, data.Safety);
            return slice;
        }

        public static void SetVertexAttribute<T>(this Sprite sprite, VertexAttribute channel, NativeArray<T> src) where T : struct
        {
            CheckAttributeTypeMatchesAndThrow<T>(channel);
            CheckSprite(sprite);
            var data = sprite.MeshData();
            var buffer = data.EnsureChannel(channel);
            int count = Math.Min(src.Length, data.VertexCount);
            UnsafeUtility.MemCpy(buffer, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src), count * (long)SpriteMeshData.ElementSize(channel));
            sprite.MeshDataChanged();
        }

        public static NativeArray<Matrix4x4> GetBindPoses(this Sprite sprite)
        {
            CheckSprite(sprite);
            var data = sprite.MeshData();
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Matrix4x4>(data.BindPoses, data.BindPoseCount, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, data.Safety);
            return array;
        }

        public static void SetBindPoses(this Sprite sprite, NativeArray<Matrix4x4> src)
        {
            CheckSprite(sprite);
            sprite.MeshData().SetBindPoses((Matrix4x4*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src), src.Length);
            sprite.MeshDataChanged();
        }

        public static NativeArray<ushort> GetIndices(this Sprite sprite)
        {
            CheckSprite(sprite);
            var data = sprite.MeshData();
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<ushort>(data.Indices, data.IndexCount, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, data.Safety);
            return array;
        }

        public static void SetIndices(this Sprite sprite, NativeArray<ushort> src)
        {
            CheckSprite(sprite);
            sprite.MeshData().SetIndices((ushort*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src), src.Length);
            sprite.MeshDataChanged();
        }

        public static SpriteBone[] GetBones(this Sprite sprite)
        {
            CheckSprite(sprite);
            return (SpriteBone[])sprite.m_Bones.Clone();
        }

        public static void SetBones(this Sprite sprite, SpriteBone[] src)
        {
            CheckSprite(sprite);
            sprite.m_Bones = src != null ? (SpriteBone[])src.Clone() : Array.Empty<SpriteBone>();
        }

        public static bool HasVertexAttribute(this Sprite sprite, VertexAttribute channel)
        {
            CheckSprite(sprite);
            var data = sprite.MeshData();
            if (channel == VertexAttribute.BlendIndices) return data.Channel(VertexAttribute.BlendWeight) != null;
            return data.Channel(channel) != null;
        }

        public static void SetVertexCount(this Sprite sprite, int count)
        {
            CheckSprite(sprite);
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var data = sprite.MeshData();
            data.Resize(count);
            data.EnsureChannel(VertexAttribute.Position);
            data.EnsureChannel(VertexAttribute.TexCoord0);
            sprite.MeshDataChanged();
        }

        public static int GetVertexCount(this Sprite sprite)
        {
            CheckSprite(sprite);
            return sprite.MeshData().VertexCount;
        }

        /// <summary>The bytes per vertex of the stream a deformed sprite is drawn from: position, then
        /// normal and tangent when the sprite has them.</summary>
        internal static int GetPrimaryVertexStreamSize(Sprite sprite)
        {
            CheckSprite(sprite);
            var data = sprite.MeshData();
            int size = 12;
            if (data.Channel(VertexAttribute.Normal) != null) size += 12;
            if (data.Channel(VertexAttribute.Tangent) != null) size += 16;
            return size;
        }

        internal static AtomicSafetyHandle GetSafetyHandle(this Sprite sprite)
        {
            CheckSprite(sprite);
            return sprite.MeshData().Safety;
        }
    }

    /// <summary>
    /// Lets 2D Animation draw a sprite renderer from deformed vertices: the deformable buffer holds
    /// each vertex's position (then normal and tangent when the sprite has them) in the renderer's
    /// space; the sprite's texture coordinates and triangles complete the mesh. Deformation runs on
    /// the CPU (GPU skinning is reported off).
    /// </summary>
    public static unsafe partial class SpriteRendererDataAccessExtensions
    {
        internal static void SetDeformableBuffer(this SpriteRenderer spriteRenderer, NativeArray<byte> src) =>
            spriteRenderer.SetDeformed((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src), src.Length);

        internal static void SetDeformableBuffer(this SpriteRenderer spriteRenderer, NativeArray<Vector3> src) =>
            spriteRenderer.SetDeformed((byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src), src.Length * 12);

        internal static void SetBatchDeformableBufferAndLocalAABBArray(SpriteRenderer[] spriteRenderers, NativeArray<IntPtr> buffers, NativeArray<int> bufferSizes, NativeArray<Bounds> bounds)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                var renderer = spriteRenderers[i];
                if (renderer == null || i >= buffers.Length || buffers[i] == IntPtr.Zero || bufferSizes[i] <= 0) continue;
                renderer.SetDeformed((byte*)buffers[i], bufferSizes[i]);
                renderer.SetDeformedLocalAABB(bounds[i]);
            }
        }

        /// <summary>The GPU skinning path's bone matrices: GPU skinning is off, so nothing reads them.</summary>
        internal static void SetBoneTransformsArray(SpriteRenderer[] spriteRenderers, NativeArray<IntPtr> buffers, NativeArray<int> bufferSizes, NativeArray<Bounds> bounds)
        {
            for (int i = 0; i < spriteRenderers.Length && i < bounds.Length; i++)
                if (spriteRenderers[i] != null) spriteRenderers[i].SetDeformedLocalAABB(bounds[i]);
        }

        /// <summary>Whether the renderer's deformable buffer is <paramref name="buffer"/> (zero: it
        /// draws the sprite's own vertices).</summary>
        internal static bool IsUsingDeformableBuffer(this SpriteRenderer spriteRenderer, IntPtr buffer) =>
            spriteRenderer.m_DeformSource == buffer;

        internal static void SetBoneTransforms(this SpriteRenderer spriteRenderer, NativeArray<Matrix4x4> src) { }

        public static void DeactivateDeformableBuffer(this SpriteRenderer renderer) => renderer.Undeform();

        internal static void SetLocalAABB(this SpriteRenderer renderer, Bounds aabb) => renderer.SetDeformedLocalAABB(aabb);

        internal static void SetupMaterialProperties(SpriteRenderer spriteRenderer) { }

        internal static bool IsGPUSkinningEnabled() => false;

        internal static bool IsSRPBatchingEnabled(SpriteRenderer spriteRenderer) => false;
    }
}

namespace UnityEngine
{
    public sealed partial class Sprite
    {
        internal SpriteBone[] m_Bones = Array.Empty<SpriteBone>();
        /// <summary>Per vertex skin weights (null when the sprite is not skinned).</summary>
        internal BoneWeight[] m_BoneWeights;
        internal Matrix4x4[] m_BindPoses = Array.Empty<Matrix4x4>();
        SpriteMeshData m_MeshData;

        /// <summary>The authored mesh and skin of an imported sprite: vertices in pixels from the
        /// rect's bottom-left, bones in pixels (the root's from the rect's bottom-left).</summary>
        internal void SetImportedMesh(Vector2[] pixelVertices, ushort[] triangles, BoneWeight[] weights, SpriteBone[] pixelBones)
        {
            if (pixelVertices != null && pixelVertices.Length > 0 && triangles != null && triangles.Length > 0)
                OverrideGeometry(pixelVertices, triangles);
            if (weights != null && weights.Length == m_Vertices.Length) m_BoneWeights = (BoneWeight[])weights.Clone();
            if (pixelBones == null || pixelBones.Length == 0) return;
            var bones = new SpriteBone[pixelBones.Length];
            var world = new Matrix4x4[bones.Length];
            m_BindPoses = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = pixelBones[i];
                var p = b.position;
                b.position = b.parentId < 0
                    ? new Vector3((p.x - m_Pivot.x) / m_PixelsPerUnit, (p.y - m_Pivot.y) / m_PixelsPerUnit, p.z)
                    : new Vector3(p.x / m_PixelsPerUnit, p.y / m_PixelsPerUnit, p.z);
                b.length /= m_PixelsPerUnit;
                bones[i] = b;
            }
            // Bind poses: the inverse of each bone's pose in the sprite's space (parents come first).
            for (int i = 0; i < bones.Length; i++)
            {
                var local = Matrix4x4.TRS(bones[i].position, bones[i].rotation, Vector3.one);
                int parent = bones[i].parentId;
                world[i] = parent >= 0 && parent < i ? world[parent] * local : local;
                m_BindPoses[i] = world[i].inverse;
            }
            m_Bones = bones;
        }

        /// <summary>The data access view of the mesh, made from the sprite's geometry when first asked
        /// for. Skinned sprites carry tangents (1, 0, 0, -1) and their weights, as Unity imports them.</summary>
        internal unsafe SpriteMeshData MeshData()
        {
            if (m_MeshData != null) return m_MeshData;
            var data = new SpriteMeshData { VertexCount = m_Vertices.Length };
            var positions = (Vector3*)data.EnsureChannel(Rendering.VertexAttribute.Position);
            var uvs = (Vector2*)data.EnsureChannel(Rendering.VertexAttribute.TexCoord0);
            for (int i = 0; i < m_Vertices.Length; i++)
            {
                positions[i] = m_Vertices[i];
                uvs[i] = i < m_UV.Length ? m_UV[i] : default;
            }
            bool skinned = m_Bones.Length > 0 || m_BoneWeights != null;
            if (skinned)
            {
                var tangents = (Vector4*)data.EnsureChannel(Rendering.VertexAttribute.Tangent);
                for (int i = 0; i < m_Vertices.Length; i++) tangents[i] = new Vector4(1f, 0f, 0f, -1f);
            }
            if (m_BoneWeights != null)
            {
                var weights = (BoneWeight*)data.EnsureChannel(Rendering.VertexAttribute.BlendWeight);
                for (int i = 0; i < m_Vertices.Length; i++) weights[i] = m_BoneWeights[i];
            }
            fixed (ushort* t = m_Triangles) data.SetIndices(t, m_Triangles.Length);
            fixed (Matrix4x4* b = m_BindPoses) data.SetBindPoses(b, m_BindPoses.Length);
            m_MeshData = data;
            return data;
        }

        /// <summary>Takes what was set through the data access API as the sprite's geometry.</summary>
        internal unsafe void MeshDataChanged()
        {
            var data = m_MeshData;
            var positions = (Vector3*)data.Channel(Rendering.VertexAttribute.Position);
            var uvs = (Vector2*)data.Channel(Rendering.VertexAttribute.TexCoord0);
            m_Vertices = new Vector2[data.VertexCount];
            m_UV = new Vector2[data.VertexCount];
            for (int i = 0; i < data.VertexCount; i++)
            {
                if (positions != null) m_Vertices[i] = positions[i];
                if (uvs != null) m_UV[i] = uvs[i];
            }
            m_Triangles = new ushort[data.IndexCount];
            for (int i = 0; i < data.IndexCount; i++) m_Triangles[i] = data.Indices[i];
            var weights = (BoneWeight*)data.Channel(Rendering.VertexAttribute.BlendWeight);
            if (weights != null)
            {
                m_BoneWeights = new BoneWeight[data.VertexCount];
                for (int i = 0; i < data.VertexCount; i++) m_BoneWeights[i] = weights[i];
            }
            m_BindPoses = new Matrix4x4[data.BindPoseCount];
            for (int i = 0; i < data.BindPoseCount; i++) m_BindPoses[i] = data.BindPoses[i];
            m_Mesh = null;
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            m_MeshData?.Free();
            m_MeshData = null;
        }
    }

    public sealed partial class SpriteRenderer
    {
        /// <summary>The deformed vertices drawn instead of the sprite's (a copy of the last buffer
        /// set), and the buffer they were copied from.</summary>
        byte[] m_Deformed;
        int m_DeformedBytes;
        internal IntPtr m_DeformSource;
        bool m_HasLocalAABB;
        Bounds m_LocalAABB;
        Mesh m_DeformMesh;
        bool m_DeformMeshStale;

        internal unsafe void SetDeformed(byte* data, int bytes)
        {
            if (m_Deformed == null || m_Deformed.Length < bytes) m_Deformed = new byte[bytes];
            fixed (byte* d = m_Deformed) UnsafeUtility.MemCpy(d, data, bytes);
            m_DeformedBytes = bytes;
            m_DeformSource = (IntPtr)data;
            m_DeformMeshStale = true;
        }

        internal void Undeform()
        {
            m_DeformSource = IntPtr.Zero;
            m_DeformedBytes = 0;
            m_HasLocalAABB = false;
        }

        internal void SetDeformedLocalAABB(Bounds aabb)
        {
            m_LocalAABB = aabb;
            m_HasLocalAABB = true;
        }

        /// <summary>The sprite's mesh with the deformed positions, when a deformable buffer is active
        /// and matches the sprite's vertices; null otherwise.</summary>
        unsafe Mesh DeformedMesh()
        {
            if (m_DeformSource == IntPtr.Zero || m_DeformedBytes == 0 || m_Sprite == null) return null;
            int count = m_Sprite.m_Vertices.Length;
            int stride = SpriteDataAccessExtensions.GetPrimaryVertexStreamSize(m_Sprite);
            if (count == 0 || m_DeformedBytes < count * stride) return null;
            if (m_DeformMesh != null && !m_DeformMeshStale) return m_DeformMesh;
            var positions = new Vector3[count];
            fixed (byte* d = m_Deformed)
                for (int i = 0; i < count; i++) positions[i] = *(Vector3*)(d + i * stride);
            m_DeformMesh ??= new Mesh { name = "Deformed " + m_Sprite.name, hideFlags = HideFlags.HideAndDontSave };
            m_DeformMesh.Clear();
            m_DeformMesh.vertices = positions;
            m_DeformMesh.uv = m_Sprite.m_UV;
            var triangles = new int[m_Sprite.m_Triangles.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = m_Sprite.m_Triangles[i];
            m_DeformMesh.triangles = triangles;
            m_DeformMeshStale = false;
            return m_DeformMesh;
        }
    }
}
