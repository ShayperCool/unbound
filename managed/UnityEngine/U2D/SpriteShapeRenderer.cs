using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unbound.Runtime;

namespace UnityEngine.U2D
{
    /// <summary>A spline point as the sprite shape generator reads it.</summary>
    public partial struct ShapeControlPoint
    {
        public Vector3 position;
        public Vector3 leftTangent;
        public Vector3 rightTangent;
        public int mode;
    }

    /// <summary>Per spline point data for the generator.</summary>
    public partial struct SpriteShapeMetaData
    {
        public float height;
        public float bevelCutoff;
        public float bevelSize;
        public uint spriteIndex;
        public bool corner;
    }

    /// <summary>An angle range of a sprite shape profile, with the indices of its sprites.</summary>
    public partial struct AngleRangeInfo
    {
        public float start;
        public float end;
        public uint order;
        public int[] sprites;
    }

    /// <summary>The settings a sprite shape is generated and drawn with.</summary>
    public partial struct SpriteShapeParameters
    {
        public Matrix4x4 transform;
        public Texture2D fillTexture;
        public uint fillScale;
        public uint splineDetail;
        public float angleThreshold;
        public float borderPivot;
        public float bevelCutoff;
        public float bevelSize;
        public bool carpet;
        public bool smartSprite;
        public bool adaptiveUV;
        public bool spriteBorders;
        public bool stretchUV;
    }

    /// <summary>One part of the generated geometry: its vertices and indices (relative to its first
    /// vertex) follow the previous parts'; sprite index -1 is the fill.</summary>
    public partial struct SpriteShapeSegment
    {
        int m_GeomIndex;
        int m_IndexCount;
        int m_VertexCount;
        int m_SpriteIndex;

        public int geomIndex { get => m_GeomIndex; set => m_GeomIndex = value; }
        public int indexCount { get => m_IndexCount; set => m_IndexCount = value; }
        public int vertexCount { get => m_VertexCount; set => m_VertexCount = value; }
        public int spriteIndex { get => m_SpriteIndex; set => m_SpriteIndex = value; }
    }

    /// <summary>
    /// Draws the geometry com.unity.2d.spriteshape generates: the renderer owns the buffers
    /// (indices, interleaved vertex channels, segments, bounds) the package's job writes into, and
    /// <see cref="Prepare"/> hands it the job; when drawn, the job is completed and the segments
    /// drawn by sprite index, the fill (sprite index -1) first with the fill texture and the first
    /// material, the edges and corners with their sprite's texture and the second material.
    /// </summary>
    [ClassId(1971053207)]
    public unsafe partial class SpriteShapeRenderer : Renderer
    {
        Color m_Color = Color.white;
        SpriteMaskInteraction m_MaskInteraction;
        Texture m_ShapeTexture;
        Sprite[] m_Sprites = Array.Empty<Sprite>();
        Bounds m_LocalAABB;
        SpriteSortPoint m_SpriteSortPoint;

        /// <summary>The buffers the generator writes: indices, the vertices interleaved (position,
        /// uv0, then the color, tangent and normal channels asked for), segments and bounds. Each kind
        /// of data has its own safety handle, renewed whenever it is handed out (the arrays handed out
        /// before become invalid).</summary>
        ushort* m_Indices;
        byte* m_Vertices;
        int m_DataSize = -1;
        int m_Channels = -1;
        int m_Stride;
        int m_ColorOffset = -1, m_TangentOffset = -1, m_NormalOffset = -1;
        SpriteShapeSegment* m_Segments;
        int m_SegmentCount;
        Bounds* m_Bounds;
        readonly AtomicSafetyHandle[] m_Safety = new AtomicSafetyHandle[k_DataKinds];

        // The kinds of data (UnityEngine.U2D.SpriteShapeDataType).
        const int k_IndexData = 0, k_SegmentData = 1, k_BoundsData = 2, k_VertexData = 3, k_TexCoord0Data = 4, k_NormalData = 5,
            k_TangentData = 6, k_ColorData = 7, k_DataKinds = 8;

        JobHandle m_Job;
        bool m_JobPending;
        int m_PreparedVersion;
        int m_BuiltVersion = -1;
        Mesh m_Mesh;
        readonly List<(int Submesh, Texture Texture, int Material, int Sprite)> m_Parts = new List<(int, Texture, int, int)>();
        int m_SplineMeshCount;

        const int k_Position = 1, k_TexCoord0 = 2, k_Color = 4, k_Tangent = 8, k_Normal = 16;

        public SpriteShapeRenderer() { }

        public Color color { get => m_Color; set => m_Color = value; }

        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }

        /// <summary>A renderer added by a script starts with two Sprites-Default materials (fill and
        /// edges), as the Editor makes it.</summary>
        internal override void OnAttached()
        {
            base.OnAttached();
            var material = BuiltinObjects.SpritesDefault;
            if (material != null) m_Materials = new[] { material, material };
        }

        internal override Bounds GetLocalBounds() => m_LocalAABB;

        public void SetLocalAABB(Bounds bounds) => m_LocalAABB = bounds;

        internal Bounds GetLocalAABB() => m_LocalAABB;

        internal int GetVertexCount() => Math.Max(m_DataSize, 0);

        internal int GetIndexCount() => Math.Max(m_DataSize, 0);

        /// <summary>The meshes the renderer draws the shape with: none until the shape is first drawn,
        /// then two (measured the same for filled and edge-only shapes).</summary>
        public int GetSplineMeshCount() => m_SplineMeshCount;

        AtomicSafetyHandle Renew(int kind)
        {
            ref var handle = ref m_Safety[kind];
            if (!AtomicSafetyHandle.IsDefaultValue(handle)) AtomicSafetyHandle.Release(handle);
            handle = AtomicSafetyHandle.Create();
            return handle;
        }

        NativeArray<T> DataArray<T>(void* data, int count, int kind) where T : struct
        {
            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(data, count, Allocator.None);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref array, Renew(kind));
            return array;
        }

        public NativeArray<Bounds> GetBounds()
        {
            CompletePending();
            if (m_Bounds == null)
            {
                m_Bounds = (Bounds*)UnsafeUtility.Malloc(sizeof(Bounds), 16, Allocator.Persistent);
                *m_Bounds = default;
            }
            return DataArray<Bounds>(m_Bounds, 1, k_BoundsData);
        }

        public NativeArray<SpriteShapeSegment> GetSegments(int dataSize)
        {
            if (dataSize < 0) throw new ArgumentOutOfRangeException(nameof(dataSize));
            CompletePending();
            if (m_Segments == null || m_SegmentCount != dataSize)
            {
                if (m_Segments != null) UnsafeUtility.Free(m_Segments, Allocator.Persistent);
                m_Segments = (SpriteShapeSegment*)UnsafeUtility.Malloc(Math.Max(1, dataSize) * (long)sizeof(SpriteShapeSegment), 16, Allocator.Persistent);
                m_SegmentCount = dataSize;
            }
            UnsafeUtility.MemClear(m_Segments, (long)dataSize * sizeof(SpriteShapeSegment));
            return DataArray<SpriteShapeSegment>(m_Segments, dataSize, k_SegmentData);
        }

        /// <summary>Sizes the buffers for <paramref name="dataSize"/> vertices and indices with the
        /// channels asked for (the earlier slices stay valid only while neither changes).</summary>
        void Channels(int dataSize, int channels)
        {
            if (dataSize < 0) throw new ArgumentOutOfRangeException(nameof(dataSize));
            CompletePending();
            if (dataSize == m_DataSize && channels == m_Channels) return;
            int stride = 20;
            m_ColorOffset = (channels & k_Color) != 0 ? stride : -1;
            if (m_ColorOffset >= 0) stride += 4;
            m_TangentOffset = (channels & k_Tangent) != 0 ? stride : -1;
            if (m_TangentOffset >= 0) stride += 16;
            m_NormalOffset = (channels & k_Normal) != 0 ? stride : -1;
            if (m_NormalOffset >= 0) stride += 12;
            FreeChannels();
            m_Indices = (ushort*)UnsafeUtility.Malloc(Math.Max(1, dataSize) * 2L, 16, Allocator.Persistent);
            m_Vertices = (byte*)UnsafeUtility.Malloc(Math.Max(1, dataSize) * (long)stride, 16, Allocator.Persistent);
            UnsafeUtility.MemClear(m_Indices, dataSize * 2L);
            UnsafeUtility.MemClear(m_Vertices, dataSize * (long)stride);
            m_Stride = stride;
            m_DataSize = dataSize;
            m_Channels = channels;
        }

        void FreeChannels()
        {
            if (m_Indices != null) UnsafeUtility.Free(m_Indices, Allocator.Persistent);
            if (m_Vertices != null) UnsafeUtility.Free(m_Vertices, Allocator.Persistent);
            m_Indices = null;
            m_Vertices = null;
            m_DataSize = m_Channels = -1;
        }

        NativeSlice<T> Slice<T>(int offset, int kind) where T : struct
        {
            var slice = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<T>(m_Vertices + offset, m_Stride, m_DataSize);
            NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref slice, Renew(kind));
            return slice;
        }

        NativeArray<ushort> IndexArray() => DataArray<ushort>(m_Indices, m_DataSize, k_IndexData);

        public void GetChannels(int dataSize, out NativeArray<ushort> indices, out NativeSlice<Vector3> vertices, out NativeSlice<Vector2> texcoords)
        {
            Channels(dataSize, k_Position | k_TexCoord0);
            indices = IndexArray();
            vertices = Slice<Vector3>(0, k_VertexData);
            texcoords = Slice<Vector2>(12, k_TexCoord0Data);
        }

        public void GetChannels(int dataSize, out NativeArray<ushort> indices, out NativeSlice<Vector3> vertices, out NativeSlice<Vector2> texcoords, out NativeSlice<Color32> colors)
        {
            Channels(dataSize, k_Position | k_TexCoord0 | k_Color);
            indices = IndexArray();
            vertices = Slice<Vector3>(0, k_VertexData);
            texcoords = Slice<Vector2>(12, k_TexCoord0Data);
            colors = Slice<Color32>(m_ColorOffset, k_ColorData);
        }

        public void GetChannels(int dataSize, out NativeArray<ushort> indices, out NativeSlice<Vector3> vertices, out NativeSlice<Vector2> texcoords, out NativeSlice<Vector4> tangents)
        {
            Channels(dataSize, k_Position | k_TexCoord0 | k_Tangent);
            indices = IndexArray();
            vertices = Slice<Vector3>(0, k_VertexData);
            texcoords = Slice<Vector2>(12, k_TexCoord0Data);
            tangents = Slice<Vector4>(m_TangentOffset, k_TangentData);
        }

        public void GetChannels(int dataSize, out NativeArray<ushort> indices, out NativeSlice<Vector3> vertices, out NativeSlice<Vector2> texcoords, out NativeSlice<Color32> colors, out NativeSlice<Vector4> tangents)
        {
            Channels(dataSize, k_Position | k_TexCoord0 | k_Color | k_Tangent);
            indices = IndexArray();
            vertices = Slice<Vector3>(0, k_VertexData);
            texcoords = Slice<Vector2>(12, k_TexCoord0Data);
            colors = Slice<Color32>(m_ColorOffset, k_ColorData);
            tangents = Slice<Vector4>(m_TangentOffset, k_TangentData);
        }

        public void GetChannels(int dataSize, out NativeArray<ushort> indices, out NativeSlice<Vector3> vertices, out NativeSlice<Vector2> texcoords, out NativeSlice<Vector4> tangents, out NativeSlice<Vector3> normals)
        {
            Channels(dataSize, k_Position | k_TexCoord0 | k_Tangent | k_Normal);
            indices = IndexArray();
            vertices = Slice<Vector3>(0, k_VertexData);
            texcoords = Slice<Vector2>(12, k_TexCoord0Data);
            tangents = Slice<Vector4>(m_TangentOffset, k_TangentData);
            normals = Slice<Vector3>(m_NormalOffset, k_NormalData);
        }

        public void GetChannels(int dataSize, out NativeArray<ushort> indices, out NativeSlice<Vector3> vertices, out NativeSlice<Vector2> texcoords, out NativeSlice<Color32> colors, out NativeSlice<Vector4> tangents, out NativeSlice<Vector3> normals)
        {
            Channels(dataSize, k_Position | k_TexCoord0 | k_Color | k_Tangent | k_Normal);
            indices = IndexArray();
            vertices = Slice<Vector3>(0, k_VertexData);
            texcoords = Slice<Vector2>(12, k_TexCoord0Data);
            colors = Slice<Color32>(m_ColorOffset, k_ColorData);
            tangents = Slice<Vector4>(m_TangentOffset, k_TangentData);
            normals = Slice<Vector3>(m_NormalOffset, k_NormalData);
        }

        /// <summary>The job writing the buffers, and what to draw them with; the geometry is read when
        /// the renderer is next drawn.</summary>
        public void Prepare(JobHandle handle, SpriteShapeParameters shapeParams, Sprite[] sprites)
        {
            m_Job = handle;
            m_JobPending = true;
            m_ShapeTexture = shapeParams.fillTexture;
            m_Sprites = sprites != null ? (Sprite[])sprites.Clone() : Array.Empty<Sprite>();
            m_PreparedVersion++;
        }

        void CompletePending()
        {
            if (!m_JobPending) return;
            m_JobPending = false;
            m_Job.Complete();
        }

        /// <summary>The prepared geometry as a mesh with a submesh per segment.</summary>
        /// <summary>The sprite index of each part, in the order the parts are drawn.</summary>
        internal int[] DrawnSprites() => m_Parts.ConvertAll(p => p.Sprite).ToArray();

        internal void Build()
        {
            CompletePending();
            m_BuiltVersion = m_PreparedVersion;
            m_Parts.Clear();
            // The renderer's bounds become the generated geometry's (measured once a shape is drawn).
            if (m_Bounds != null) m_LocalAABB = *m_Bounds;
            if (m_Segments == null || m_Indices == null) return;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = m_ColorOffset >= 0 ? new List<Color32>() : null;
            var tangents = m_TangentOffset >= 0 ? new List<Vector4>() : null;
            var normals = m_NormalOffset >= 0 ? new List<Vector3>() : null;
            var triangles = new List<List<int>>();
            byte* data = m_Vertices;
            int vertexStart = 0, indexStart = 0;
            for (int s = 0; s < m_SegmentCount; s++)
            {
                var segment = m_Segments[s];
                if (segment.vertexCount <= 0 && segment.indexCount <= 0) continue;
                int vertexCount = Math.Min(segment.vertexCount, m_DataSize - vertexStart);
                int indexCount = Math.Min(segment.indexCount, m_DataSize - indexStart);
                if (vertexCount < 0 || indexCount < 0) break;
                int baseVertex = vertices.Count;
                for (int v = 0; v < vertexCount; v++)
                {
                    byte* p = data + (long)(vertexStart + v) * m_Stride;
                    vertices.Add(*(Vector3*)p);
                    uvs.Add(*(Vector2*)(p + 12));
                    colors?.Add(*(Color32*)(p + m_ColorOffset));
                    tangents?.Add(*(Vector4*)(p + m_TangentOffset));
                    normals?.Add(*(Vector3*)(p + m_NormalOffset));
                }
                if (indexCount > 0)
                {
                    var list = new List<int>(indexCount);
                    for (int i = 0; i < indexCount; i++)
                    {
                        int index = m_Indices[indexStart + i];
                        list.Add(index < vertexCount ? baseVertex + index : baseVertex);
                    }
                    bool fill = segment.spriteIndex < 0;
                    Texture texture = fill ? m_ShapeTexture
                        : segment.spriteIndex < m_Sprites.Length && m_Sprites[segment.spriteIndex] != null ? m_Sprites[segment.spriteIndex].texture : null;
                    m_Parts.Add((triangles.Count, texture, fill ? 0 : 1, segment.spriteIndex));
                    triangles.Add(list);
                }
                vertexStart += Math.Max(segment.vertexCount, 0);
                indexStart += Math.Max(segment.indexCount, 0);
            }
            // Drawn by sprite: the fill, then the edges of the first sprite in their order, the next
            // sprite's, ... (measured: where edges of different sprites overlap, the later sprite's
            // edge is on top whatever the segment order).
            var ordered = new List<(int Order, (int, Texture, int, int) Part)>();
            for (int i = 0; i < m_Parts.Count; i++) ordered.Add((i, m_Parts[i]));
            ordered.Sort((a, b) => a.Part.Item4 != b.Part.Item4 ? a.Part.Item4.CompareTo(b.Part.Item4) : a.Order.CompareTo(b.Order));
            m_Parts.Clear();
            foreach (var o in ordered) m_Parts.Add(o.Part);
            if (m_Mesh == null) m_Mesh = new Mesh { name = "SpriteShape", hideFlags = HideFlags.HideAndDontSave };
            m_Mesh.Clear();
            if (vertices.Count > 65535) m_Mesh.indexFormat = Rendering.IndexFormat.UInt32;
            m_Mesh.SetVertices(vertices);
            m_Mesh.SetUVs(0, uvs);
            if (colors != null) m_Mesh.SetColors(colors);
            if (tangents != null) m_Mesh.SetTangents(tangents);
            if (normals != null) m_Mesh.SetNormals(normals);
            m_Mesh.subMeshCount = Math.Max(1, triangles.Count);
            for (int i = 0; i < triangles.Count; i++) m_Mesh.SetTriangles(triangles[i], i, false);
            m_SplineMeshCount = 2;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            // Culled shapes are not drawn, so their prepared geometry is not taken yet.
            if (!FrameBuilder.InFrustum(camera.Projection * camera.View, bounds)) return;
            if (m_BuiltVersion != m_PreparedVersion) Build();
            if (m_Parts.Count == 0 || m_Materials.Length == 0 || m_Mesh == null) return;
            ulong mesh = m_Mesh.Sync();
            var model = transform.localToWorldMatrix;
            float distance = FrameBuilder.Distance(camera, bounds.center);
            var groups = FrameBuilder.GroupsOf(this, camera);
            int layer = SortingLayer.GetLayerValueFromID(m_SortingLayerID);
            foreach (var part in m_Parts)
            {
                int index = Math.Min(part.Material, m_Materials.Length - 1);
                var material = DrawnMaterial(index, m_Materials[index]);
                if (material == null || part.Texture == null) continue;
                into.Add(new FrameBuilder.DrawItem
                {
                    Mesh = mesh,
                    Submesh = part.Submesh,
                    Material = material,
                    Model = model,
                    Color = m_Color,
                    MainTexture = TextureHandles.Of(part.Texture),
                    Queue = material.renderQueue,
                    SortingLayer = layer,
                    SortingOrder = m_SortingOrder,
                    Distance = distance,
                    Sequence = FrameBuilder.NextSequence(),
                    Groups = groups,
                    Renderer = this,
                });
            }
        }

        internal override void OnEngineDestroy()
        {
            base.OnEngineDestroy();
            CompletePending();
            FreeChannels();
            if (m_Segments != null) UnsafeUtility.Free(m_Segments, Allocator.Persistent);
            if (m_Bounds != null) UnsafeUtility.Free(m_Bounds, Allocator.Persistent);
            m_Segments = null;
            m_Bounds = null;
            m_SegmentCount = 0;
            for (int i = 0; i < m_Safety.Length; i++)
            {
                if (!AtomicSafetyHandle.IsDefaultValue(m_Safety[i])) AtomicSafetyHandle.Release(m_Safety[i]);
                m_Safety[i] = default;
            }
            if (m_Mesh != null) DestroyImmediate(m_Mesh);
            m_Mesh = null;
            m_Parts.Clear();
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            m_Color = body.Color("m_Color", Color.white);
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
            var texture = body.Ref("m_ShapeTexture");
            m_ShapeTexture = texture.IsNull ? null : resolver.Resolve(texture, typeof(Texture2D)) as Texture;
            var sprites = new List<Sprite>();
            foreach (var item in body.Get("m_Sprites")?.Seq() ?? Array.Empty<YNode>())
            {
                var r = item.AsRef();
                sprites.Add(r.IsNull ? null : resolver.Resolve(r, typeof(Sprite)) as Sprite);
            }
            m_Sprites = sprites.ToArray();
            var aabb = body.Get("m_LocalAABB");
            if (aabb != null) m_LocalAABB = new Bounds(aabb.Vector3("m_Center"), aabb.Vector3("m_Extent") * 2f);
            m_SpriteSortPoint = (SpriteSortPoint)body.Int("m_SpriteSortPoint");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SpriteShapeRenderer)source;
            CopyRendererData(s);
            m_Color = s.m_Color;
            m_MaskInteraction = s.m_MaskInteraction;
            m_ShapeTexture = s.m_ShapeTexture;
            m_Sprites = (Sprite[])s.m_Sprites.Clone();
            m_LocalAABB = s.m_LocalAABB;
            m_SpriteSortPoint = s.m_SpriteSortPoint;
        }
    }
}
