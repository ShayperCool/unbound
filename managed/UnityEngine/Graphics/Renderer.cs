using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>What every renderer has: materials, sorting, visibility, bounds.</summary>
    public partial class Renderer : Component
    {
        internal bool m_Enabled = true;
        internal Material[] m_Materials = new Material[1]; // one empty slot when added by script (measured)
        /// <summary>Instances `material`/`materials` made for this renderer.</summary>
        internal readonly HashSet<Material> m_OwnedMaterials = new HashSet<Material>();
        internal int m_SortingLayerID;
        internal short m_SortingOrder;
        internal MaterialPropertyBlock m_PropertyBlock;
        /// <summary>The LOD group whose levels list the renderer (drawn only in its camera's level).</summary>
        internal LODGroup m_LODGroup;
        ShadowCastingMode m_CastShadows = ShadowCastingMode.On;
        bool m_ReceiveShadows = true;
        uint m_RenderingLayerMask = 1;
        bool m_IsVisible;
        /// <summary>The frame a camera's culling or a light's shadow casters last took it in.</summary>
        internal int m_RenderedFrame = -1;
        internal bool m_Registered;

        public Renderer() { }

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value) return;
                m_Enabled = value;
                if (m_GameObject != null && m_GameObject.activeInHierarchy)
                {
                    if (value) OnEngineEnable();
                    else OnEngineDisable();
                }
            }
        }

        internal override void OnEngineEnable()
        {
            if (!m_Enabled || m_Registered) return;
            m_Registered = true;
            RenderRegistry.Renderers.Add(this);
        }

        /// <summary>The object-to-world matrix of the last frame a player-loop camera drew motion
        /// vectors; until then the one of its first motion vector draw (it has not moved).</summary>
        internal Matrix4x4 m_PreviousModel;
        internal bool m_HasPreviousModel;

        /// <summary>A visible renderer turned off (disabled, or its object deactivated) becomes
        /// invisible at once (measured: tests/projects/messages, OnBecameInvisible during the
        /// Update that turned it off).</summary>
        internal override void OnEngineDisable()
        {
            if (!m_Registered) return;
            m_Registered = false;
            RenderRegistry.Renderers.Remove(this);
            SetVisible(false);
        }

        internal override void OnEngineDestroy()
        {
            m_IsVisible = false;
            OnEngineDisable();
            foreach (var m in m_OwnedMaterials) if (m) Destroy(m);
            m_OwnedMaterials.Clear();
            if (m_BlockMaterials != null)
                foreach (var b in m_BlockMaterials) if (b.Drawn) Destroy(b.Drawn);
            m_BlockMaterials = null;
        }

        public bool isVisible => m_IsVisible;
        public bool forceRenderingOff { get; set; }
        public ShadowCastingMode shadowCastingMode { get => m_CastShadows; set => m_CastShadows = value; }
        public bool receiveShadows { get => m_ReceiveShadows; set => m_ReceiveShadows = value; }
        public bool staticShadowCaster { get; set; }
        public MotionVectorGenerationMode motionVectorGenerationMode { get; set; } = MotionVectorGenerationMode.Object;
        public LightProbeUsage lightProbeUsage { get; set; } = LightProbeUsage.BlendProbes;
        public ReflectionProbeUsage reflectionProbeUsage { get; set; } = ReflectionProbeUsage.BlendProbes;
        public uint renderingLayerMask { get => m_RenderingLayerMask; set => m_RenderingLayerMask = value; }
        public int rendererPriority { get; set; }
        public bool allowOcclusionWhenDynamic { get; set; } = true;
        /// <summary>Whether StaticBatchingUtility combined the renderer.</summary>
        public bool isPartOfStaticBatch => m_StaticBatched;

        /// <summary>Static batching: the renderer's matrix at the time it was combined, relative to
        /// the batch root (which may still move it; null: the world).</summary>
        internal bool m_StaticBatched;
        internal Transform m_StaticBatchRoot;
        internal Matrix4x4 m_StaticBatchLocal;

        /// <summary>The matrix the local bounds go into the world with.</summary>
        internal virtual Matrix4x4 BoundsMatrix => RenderMatrix;

        /// <summary>The renderer's object-to-world matrix: a statically batched one stays where it
        /// was combined, relative to its root (measured: tests/projects/builtinfx static_batching_*).</summary>
        internal Matrix4x4 RenderMatrix
        {
            get
            {
                if (!m_StaticBatched) return transform.localToWorldMatrix;
                return m_StaticBatchRoot != null ? m_StaticBatchRoot.localToWorldMatrix * m_StaticBatchLocal : m_StaticBatchLocal;
            }
        }
        public int lightmapIndex { get; set; } = -1;
        public int realtimeLightmapIndex { get; set; } = -1;
        public Vector4 lightmapScaleOffset { get; set; } = new Vector4(1, 1, 0, 0);
        public Vector4 realtimeLightmapScaleOffset { get; set; } = new Vector4(1, 1, 0, 0);
        public Transform probeAnchor { get; set; }
        /// <summary>The light probe cell the renderer was last in (where the next lookup starts).</summary>
        internal int m_ProbeCell = -1;
        public GameObject lightProbeProxyVolumeOverride { get; set; }
        public Matrix4x4 localToWorldMatrix => transform.localToWorldMatrix;
        public Matrix4x4 worldToLocalMatrix => transform.worldToLocalMatrix;

        public int sortingLayerID
        {
            get => m_SortingLayerID;
            set => m_SortingLayerID = value;
        }

        public string sortingLayerName
        {
            get => SortingLayer.IDToName(m_SortingLayerID);
            set => m_SortingLayerID = SortingLayer.NameToID(value);
        }

        public int sortingOrder
        {
            get => m_SortingOrder;
            set => m_SortingOrder = (short)Mathf.Clamp(value, short.MinValue, short.MaxValue);
        }

        internal int sortingGroupID { get; set; }
        internal int sortingGroupOrder { get; set; }

        // ------------------------------------------------------------------ materials

        public Material sharedMaterial
        {
            get => m_Materials.Length > 0 ? m_Materials[0] : null;
            set
            {
                if (m_Materials.Length == 0) m_Materials = new Material[1];
                m_Materials[0] = value;
            }
        }

        public Material[] sharedMaterials
        {
            get => (Material[])m_Materials.Clone();
            set => m_Materials = value != null ? (Material[])value.Clone() : Array.Empty<Material>();
        }

        /// <summary>The first material, instantiated for this renderer on first access.</summary>
        public Material material
        {
            get
            {
                if (m_Materials.Length == 0) return null;
                m_Materials[0] = Own(m_Materials[0]);
                return m_Materials[0];
            }
            set => sharedMaterial = value;
        }

        public Material[] materials
        {
            get
            {
                for (int i = 0; i < m_Materials.Length; i++) m_Materials[i] = Own(m_Materials[i]);
                return (Material[])m_Materials.Clone();
            }
            set => sharedMaterials = value;
        }

        Material Own(Material shared)
        {
            if (shared == null || m_OwnedMaterials.Contains(shared)) return shared;
            var instance = new Material(shared) { name = shared.name + " (Instance)" };
            m_OwnedMaterials.Add(instance);
            return instance;
        }

        public void GetMaterials(List<Material> m)
        {
            m.Clear();
            m.AddRange(materials);
        }

        public void GetSharedMaterials(List<Material> m)
        {
            m.Clear();
            m.AddRange(m_Materials);
        }

        public void SetMaterials(List<Material> materials) => sharedMaterials = materials.ToArray();

        public void SetSharedMaterials(List<Material> materials) => sharedMaterials = materials.ToArray();

        public bool HasPropertyBlock()
        {
            if (m_PropertyBlock != null && !m_PropertyBlock.isEmpty) return true;
            if (m_MaterialBlocks != null) foreach (var b in m_MaterialBlocks.Values) if (!b.isEmpty) return true;
            return false;
        }

        /// <summary>Property blocks of single material slots: a slot with one draws with it instead
        /// of the renderer's block (measured: tests/projects/builtinrp property_blocks, the two do
        /// not combine).</summary>
        Dictionary<int, MaterialPropertyBlock> m_MaterialBlocks;

        /// <summary>The block material slot <paramref name="index"/> draws under (null: none).</summary>
        internal MaterialPropertyBlock BlockFor(int index) =>
            m_MaterialBlocks != null && m_MaterialBlocks.TryGetValue(index, out var own) ? own : m_PropertyBlock;

        /// <summary>Per material slot: the material drawn under the property block, and what it was
        /// made from (source, its version, the block).</summary>
        (Material Drawn, Material Source, int Version, MaterialPropertyBlock Block)[] m_BlockMaterials;

        /// <summary>The material slot <paramref name="index"/> draws with: the material itself, or,
        /// under a property block, a copy with the block's values (Unity applies them per draw).</summary>
        internal Material DrawnMaterial(int index, Material source)
        {
            var block = BlockFor(index);
            if (block == null || block.isEmpty || source == null) return source;
            if (m_BlockMaterials == null || m_BlockMaterials.Length <= index)
                Array.Resize(ref m_BlockMaterials, Math.Max(index + 1, m_Materials.Length));
            var cached = m_BlockMaterials[index];
            if (cached.Drawn != null && ReferenceEquals(cached.Source, source) && cached.Version == source.m_Version && ReferenceEquals(cached.Block, block))
                return cached.Drawn;
            if (cached.Drawn != null) DestroyImmediate(cached.Drawn);
            Material drawn;
            using (ObjectRegistry.AsAssets())
                drawn = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            foreach (var p in block.m_Values)
            {
                switch (p.Value)
                {
                    case float f: drawn.SetFloat(p.Key, f); drawn.m_RawBindings.Add(p.Key); break;
                    case int i: drawn.SetInteger(p.Key, i); break;
                    case Vector4 v: drawn.SetVector(p.Key, v); drawn.m_RawBindings.Add(p.Key); break;
                    case Color c: drawn.SetVector(p.Key, c); drawn.m_RawBindings.Add(p.Key); break;
                    case Matrix4x4 m: drawn.SetMatrix(p.Key, m); break;
                    case Texture t: drawn.SetTexture(p.Key, t); break;
                    case ComputeBuffer c: drawn.SetBuffer(p.Key, c); break;
                    case GraphicsBuffer g: drawn.SetBuffer(p.Key, g); break;
                    case float[] fs: drawn.SetFloatArray(p.Key, fs); break;
                    case Vector4[] vs: drawn.SetVectorArray(p.Key, vs); break;
                    case Matrix4x4[] ms: drawn.SetMatrixArray(p.Key, ms); break;
                }
            }
            m_BlockMaterials[index] = (drawn, source, source.m_Version, block);
            return drawn;
        }

        public void SetPropertyBlock(MaterialPropertyBlock properties) => m_PropertyBlock = properties?.Copy();

        public void SetPropertyBlock(MaterialPropertyBlock properties, int materialIndex)
        {
            if (properties == null || properties.isEmpty)
            {
                m_MaterialBlocks?.Remove(materialIndex);
                return;
            }
            (m_MaterialBlocks ??= new Dictionary<int, MaterialPropertyBlock>())[materialIndex] = properties.Copy();
        }

        public void GetPropertyBlock(MaterialPropertyBlock properties)
        {
            properties.Clear();
            if (m_PropertyBlock != null) properties.CopyFrom(m_PropertyBlock);
        }

        public void GetPropertyBlock(MaterialPropertyBlock properties, int materialIndex)
        {
            properties.Clear();
            if (m_MaterialBlocks != null && m_MaterialBlocks.TryGetValue(materialIndex, out var own)) properties.CopyFrom(own);
        }

        public void GetClosestReflectionProbes(List<Rendering.ReflectionProbeBlendInfo> result) => ReflectionProbe.Closest(this, result);

        public void ResetBounds() => m_BoundsOverride = null;
        public void ResetLocalBounds() => m_LocalBoundsOverride = null;

        // Set through bounds / localBounds: used (for culling too) until reset, as in Unity.
        Bounds? m_BoundsOverride;
        Bounds? m_LocalBoundsOverride;

        // ------------------------------------------------------------------ bounds

        public Bounds bounds
        {
            get
            {
                if (m_BoundsOverride is { } overridden)
                    return overridden;
                var local = localBounds;
                var m = BoundsMatrix;
                var c = m.MultiplyPoint3x4(local.center);
                var e = local.extents;
                var x = m.MultiplyVector(new Vector3(e.x, 0, 0));
                var y = m.MultiplyVector(new Vector3(0, e.y, 0));
                var z = m.MultiplyVector(new Vector3(0, 0, e.z));
                var extents = new Vector3(
                    Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                    Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                    Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                return new Bounds(c, extents * 2f);
            }
            set => m_BoundsOverride = value;
        }

        public Bounds localBounds
        {
            get => m_LocalBoundsOverride ?? GetLocalBounds();
            set => m_LocalBoundsOverride = value;
        }

        internal virtual Bounds GetLocalBounds() => new Bounds(Vector3.zero, Vector3.zero);

        // ------------------------------------------------------------------ rendering

        /// <summary>Adds this renderer's draws for a camera. Overridden by each renderer kind.</summary>
        internal virtual void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into) { }

        /// <summary>The current mesh and its matrix, also used by command buffer draws.</summary>
        internal virtual Mesh DrawGeometry(out Matrix4x4 model)
        {
            model = RenderMatrix;
            return null;
        }

        /// <summary>How the renderer's draws interact with sprite masks (renderers that have a
        /// maskInteraction).</summary>
        internal virtual SpriteMaskInteraction MaskInteraction => SpriteMaskInteraction.None;

        static readonly List<Renderer> s_WillRender = new List<Renderer>();

        /// <summary>OnWillRenderObject to the enabled scripts on the objects of the renderers a camera
        /// sees (its culling mask lets them through and their bounds touch its frustum), before any
        /// of them is drawn (Unity sends it from the camera's culling, once per camera). Scripts may
        /// change the scene meanwhile.</summary>
        internal static void SendWillRenderObject(Matrix4x4 viewProjection, int cullingMask)
        {
            s_WillRender.Clear();
            s_WillRender.AddRange(RenderRegistry.Renderers);
            foreach (var renderer in s_WillRender)
            {
                if (!renderer || renderer.forceRenderingOff || !renderer.m_Enabled) continue;
                var go = renderer.m_GameObject;
                if ((cullingMask & (1 << go.m_Layer)) == 0 || !go.activeInHierarchy) continue;
                if (!FrameBuilder.InFrustum(viewProjection, renderer.bounds)) continue;
                var components = go.m_Components;
                for (int i = 0; i < components.Count; i++)
                {
                    if (!(components[i] is MonoBehaviour script) || !script.isActiveAndEnabled) continue;
                    var call = ScriptMessages.For(script.GetType()).OnWillRenderObject;
                    if (call == null) continue;
                    try { call(script); }
                    catch (Exception e) { Debug.LogException(e, script); }
                }
            }
            s_WillRender.Clear();
        }

        internal void SetVisible(bool visible)
        {
            if (m_IsVisible == visible) return;
            m_IsVisible = visible;
            Messaging.SendFast(m_GameObject, visible ? "OnBecameVisible" : "OnBecameInvisible");
        }

        /// <summary>A camera's culling (or a light's shadow casters) took it this frame: it becomes
        /// visible at once, OnBecameVisible inside the culling (measured: tests/projects/messages).</summary>
        internal void MarkRendered()
        {
            m_RenderedFrame = Time.frameCount;
            if (!m_IsVisible) SetVisible(true);
        }

        static readonly List<Renderer> s_NotifyScratch = new List<Renderer>();

        /// <summary>EarlyUpdate.RendererNotifyInvisible: the visible renderers no camera or light
        /// took in the last frame become invisible (measured: tests/projects/messages,
        /// OnBecameInvisible early in the frame after the first one that did not draw them).</summary>
        internal static void NotifyInvisible()
        {
            int last = Time.frameCount - 1;
            s_NotifyScratch.Clear();
            foreach (var renderer in RenderRegistry.Renderers)
                if (renderer && renderer.m_IsVisible && renderer.m_RenderedFrame != last) s_NotifyScratch.Add(renderer);
            foreach (var renderer in s_NotifyScratch) renderer.SetVisible(false);
            s_NotifyScratch.Clear();
        }

        internal void ReadRendererData(YNode body, IReferenceResolver resolver)
        {
            m_Enabled = body.Bool("m_Enabled", true);
            var list = new List<Material>();
            foreach (var item in body.Get("m_Materials")?.Seq() ?? Array.Empty<YNode>())
            {
                var r = item.AsRef();
                list.Add(r.IsNull ? null : resolver.Resolve(r, typeof(Material)) as Material);
            }
            m_Materials = list.ToArray();
            m_SortingLayerID = body.Int("m_SortingLayerID");
            m_SortingOrder = (short)body.Int("m_SortingOrder");
            m_CastShadows = (ShadowCastingMode)body.Int("m_CastShadows", 1);
            m_ReceiveShadows = body.Bool("m_ReceiveShadows", true);
            m_RenderingLayerMask = (uint)body.Long("m_RenderingLayerMask", 1);
            lightProbeUsage = (LightProbeUsage)body.Int("m_LightProbeUsage", (int)LightProbeUsage.BlendProbes);
            reflectionProbeUsage = (ReflectionProbeUsage)body.Int("m_ReflectionProbeUsage", (int)ReflectionProbeUsage.BlendProbes);
            var anchor = body.Ref("m_ProbeAnchor");
            if (!anchor.IsNull) probeAnchor = resolver.Resolve(anchor, typeof(Transform)) as Transform;
        }

        internal void CopyRendererData(Renderer source)
        {
            m_Enabled = source.m_Enabled;
            m_Materials = (Material[])source.m_Materials.Clone();
            m_SortingLayerID = source.m_SortingLayerID;
            m_SortingOrder = source.m_SortingOrder;
            m_CastShadows = source.m_CastShadows;
            m_ReceiveShadows = source.m_ReceiveShadows;
            m_RenderingLayerMask = source.m_RenderingLayerMask;
            m_PropertyBlock = source.m_PropertyBlock?.Copy();
            m_MaterialBlocks = null;
            if (source.m_MaterialBlocks != null)
                foreach (var pair in source.m_MaterialBlocks) (m_MaterialBlocks ??= new Dictionary<int, MaterialPropertyBlock>())[pair.Key] = pair.Value.Copy();
            lightProbeUsage = source.lightProbeUsage;
            reflectionProbeUsage = source.reflectionProbeUsage;
            probeAnchor = source.probeAnchor;
            lightmapIndex = source.lightmapIndex;
            lightmapScaleOffset = source.lightmapScaleOffset;
            realtimeLightmapIndex = source.realtimeLightmapIndex;
            realtimeLightmapScaleOffset = source.realtimeLightmapScaleOffset;
        }
    }

    [ClassId(33)]
    public sealed partial class MeshFilter : Component
    {
        internal Mesh m_Mesh;
        bool m_OwnsMesh;

        public MeshFilter() { }

        public Mesh sharedMesh
        {
            get => m_Mesh;
            set
            {
                m_Mesh = value;
                m_OwnsMesh = false;
            }
        }

        /// <summary>The mesh, instantiated for this filter on first access.</summary>
        public Mesh mesh
        {
            get
            {
                if (m_Mesh == null) return null;
                if (!m_OwnsMesh)
                {
                    m_Mesh = (Mesh)((Instantiation.ICloneable)m_Mesh).CloneObject();
                    m_Mesh.name = m_Mesh.name + " Instance";
                    m_OwnsMesh = true;
                }
                return m_Mesh;
            }
            set => sharedMesh = value;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var r = body.Ref("m_Mesh");
            m_Mesh = r.IsNull ? null : resolver.Resolve(r, typeof(Mesh)) as Mesh;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (MeshFilter)source;
            m_Mesh = s.m_Mesh;
        }
    }

    [ClassId(23)]
    public partial class MeshRenderer : Renderer
    {
        public MeshRenderer() { }

        /// <summary>A mesh whose channels (colors, normals, tangents, uvs) replace the mesh's for
        /// this renderer (vertex painting; measured: tests/projects/builtinfx vertex_streams).</summary>
        public Mesh additionalVertexStreams { get; set; }
        public Mesh enlightenVertexStream { get; set; }
        /// <summary>A statically batched renderer keeps drawing its own mesh (Unbound combines no
        /// meshes): its submeshes start at 0.</summary>
        [Unbound.EditorFaithful("static batches draw the renderers' own meshes in Unbound; their first submesh is 0")]
        public int subMeshStartIndex => 0;
        Mesh m_StreamedMesh;
        (Mesh, int, Mesh, int) m_StreamedKey;

        /// <summary>The mesh with the additional vertex streams' channels over its own.</summary>
        Mesh Streamed(Mesh mesh)
        {
            var streams = additionalVertexStreams;
            if (streams == null) return mesh;
            mesh.Sync();
            streams.Sync();
            var key = (mesh, mesh.m_SyncVersion, streams, streams.m_SyncVersion);
            if (m_StreamedMesh != null && m_StreamedKey == key) return m_StreamedMesh;
            if (m_StreamedMesh == null)
                using (Unbound.Runtime.ObjectRegistry.AsAssets())
                    m_StreamedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            m_StreamedMesh.CopyGeometryFrom(mesh);
            m_StreamedMesh.name = mesh.name;
            int count = mesh.m_Vertices.Length;
            if (streams.m_Colors != null && streams.m_Colors.Length == count) m_StreamedMesh.m_Colors = streams.m_Colors;
            if (streams.m_Normals != null && streams.m_Normals.Length == count) m_StreamedMesh.m_Normals = streams.m_Normals;
            if (streams.m_Tangents != null && streams.m_Tangents.Length == count) m_StreamedMesh.m_Tangents = streams.m_Tangents;
            for (int i = 0; i < 8; i++)
                if (streams.m_UVs[i] != null && streams.m_UVs[i].Length == count)
                {
                    m_StreamedMesh.m_UVs[i] = streams.m_UVs[i];
                    m_StreamedMesh.m_UVDimensions[i] = streams.m_UVDimensions[i];
                }
            m_StreamedMesh.m_Dirty = true;
            m_StreamedKey = key;
            return m_StreamedMesh;
        }
        public float scaleInLightmap { get; set; } = 1f;
        public ReceiveGI receiveGI { get; set; } = ReceiveGI.Lightmaps;
        public bool stitchLightmapSeams { get; set; }

        internal override Bounds GetLocalBounds()
        {
            var filter = m_GameObject.GetComponent<MeshFilter>();
            return filter != null && filter.m_Mesh != null ? filter.m_Mesh.bounds : default;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var mesh = DrawGeometry(out var model);
            if (mesh == null) return;
            FrameBuilder.AddMeshDraws(this, mesh, model, Color.white, camera, into);
        }

        internal override Mesh DrawGeometry(out Matrix4x4 model)
        {
            model = RenderMatrix;
            var mesh = m_GameObject.GetComponent<MeshFilter>()?.m_Mesh;
            return mesh != null ? Streamed(mesh) : null;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadRendererData(body, resolver);

        internal override void CopyFrom(Component source, Func<Object, Object> remap) => CopyRendererData((Renderer)source);
    }

    [ClassId(212)]
    public sealed partial class SpriteRenderer : Renderer
    {
        Sprite m_Sprite;
        Color m_Color = Color.white;
        bool m_FlipX;
        bool m_FlipY;
        SpriteDrawMode m_DrawMode;
        Vector2 m_Size = Vector2.one;
        SpriteMaskInteraction m_MaskInteraction;
        SpriteTileMode m_TileMode;
        float m_AdaptiveModeThreshold = 0.5f;
        internal SpriteSortPoint m_SpriteSortPoint;
        Mesh m_SlicedMesh;
        (Sprite, Vector2, SpriteDrawMode, SpriteTileMode, float) m_SlicedKey;
        bool m_WarnedTight;
        Mesh m_FlippedMesh;
        (Mesh, int, bool, bool) m_FlippedKey;

        public SpriteRenderer() { }

        public Sprite sprite
        {
            get => m_Sprite;
            set
            {
                if (m_Sprite == value)
                    return;
                // A first sprite sets the size to its own (measured: tests/projects/resources size.*).
                if (m_Sprite == null && value != null) m_Size = SpriteSize(value);
                m_Sprite = value;
                InvokeSpriteChanged();
            }
        }

        static Vector2 SpriteSize(Sprite sprite) => new Vector2(sprite.rect.width / sprite.pixelsPerUnit, sprite.rect.height / sprite.pixelsPerUnit);

        UnityEngine.Events.UnityEvent<SpriteRenderer> m_SpriteChangeEvent;

        void InvokeSpriteChanged()
        {
            if (m_SpriteChangeEvent == null)
                return;
            try { m_SpriteChangeEvent.Invoke(this); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        public Color color { get => m_Color; set => m_Color = value; }
        public bool flipX { get => m_FlipX; set => m_FlipX = value; }
        public bool flipY { get => m_FlipY; set => m_FlipY = value; }
        /// <summary>Leaving or entering Simple keeps the sprite's look: the transform's scale takes the
        /// ratio of the sprite's size to the renderer's size (measured: tests/projects/resources
        /// size.*).</summary>
        public SpriteDrawMode drawMode
        {
            get => m_DrawMode;
            set
            {
                if (value == m_DrawMode) return;
                bool wasSimple = m_DrawMode == SpriteDrawMode.Simple, isSimple = value == SpriteDrawMode.Simple;
                m_DrawMode = value;
                if (wasSimple == isSimple || m_Sprite == null || m_Size.x == 0f || m_Size.y == 0f) return;
                var sprite = SpriteSize(m_Sprite);
                var ratio = wasSimple ? new Vector2(sprite.x / m_Size.x, sprite.y / m_Size.y) : new Vector2(m_Size.x / sprite.x, m_Size.y / sprite.y);
                var scale = transform.localScale;
                transform.localScale = new Vector3(scale.x * ratio.x, scale.y * ratio.y, scale.z);
            }
        }
        public Vector2 size { get => m_Size; set => m_Size = value; }
        public SpriteMaskInteraction maskInteraction { get => m_MaskInteraction; set => m_MaskInteraction = value; }
        internal override SpriteMaskInteraction MaskInteraction => m_MaskInteraction;
        public SpriteTileMode tileMode { get => m_TileMode; set => m_TileMode = value; }
        public float adaptiveModeThreshold { get => m_AdaptiveModeThreshold; set => m_AdaptiveModeThreshold = value; }
        public SpriteSortPoint spriteSortPoint { get => m_SpriteSortPoint; set => m_SpriteSortPoint = value; }

        public event Action<SpriteRenderer> spriteChangeEvent;

        /// <summary>A sprite renderer starts with the built-in Sprites-Default material (a scene's or
        /// prefab's own materials replace it when read).</summary>
        internal override void OnAttached()
        {
            base.OnAttached();
            var material = BuiltinObjects.SpritesDefault;
            if (material != null) m_Materials = new[] { material };
        }

        /// <summary>The sprite's secondary textures into a property block (what the 2D renderers
        /// bind for the sprite).</summary>
        internal void GetSecondaryTextureProperties(MaterialPropertyBlock mbp)
        {
            var properties = m_Sprite != null ? m_Sprite.SecondaryProperties() : null;
            if (mbp == null || properties == null) return;
            foreach (var p in properties) mbp.SetTexture(p.Key, (Texture)p.Value);
        }

        public void RegisterSpriteChangeCallback(UnityEngine.Events.UnityAction<SpriteRenderer> callback) =>
            (m_SpriteChangeEvent ??= new UnityEngine.Events.UnityEvent<SpriteRenderer>()).AddListener(callback);

        public void UnregisterSpriteChangeCallback(UnityEngine.Events.UnityAction<SpriteRenderer> callback) => m_SpriteChangeEvent?.RemoveListener(callback);

        internal override Bounds GetLocalBounds()
        {
            // A deformed sprite's bounds are the ones its deformer set (2D Animation).
            if (m_HasLocalAABB) return m_LocalAABB;
            if (m_Sprite == null) return default;
            if (m_DrawMode != SpriteDrawMode.Simple)
            {
                // The size around the pivot, 0.2 deep as the sprite's own bounds.
                var r = m_Sprite.rect;
                var p = m_Sprite.pivot;
                var min = new Vector2(-p.x / r.width * m_Size.x, -p.y / r.height * m_Size.y);
                return new Bounds(new Vector3(min.x + m_Size.x * 0.5f, min.y + m_Size.y * 0.5f, 0f), new Vector3(m_Size.x, m_Size.y, 0.2f));
            }
            return m_Sprite.bounds;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!MaskShape(out var mesh, out var model, out var texture)) return;
            FrameBuilder.AddMeshDraws(this, mesh, model, m_Color, camera, into, texture, m_Sprite.SecondaryProperties());
        }

        /// <summary>The geometry the renderer draws (also what a mask beside it masks with).</summary>
        internal bool MaskShape(out Mesh mesh, out Matrix4x4 model, out Texture texture)
        {
            mesh = null;
            model = default;
            texture = null;
            if (m_Sprite == null || m_Sprite.texture == null) return false;
            mesh = m_DrawMode == SpriteDrawMode.Simple ? DeformedMesh() ?? m_Sprite.RenderMesh() : SlicedMesh();
            model = transform.localToWorldMatrix;
            if (m_FlipX || m_FlipY) mesh = FlippedMesh(mesh);
            texture = m_Sprite.texture;
            return true;
        }

        /// <summary>Unity flips the vertex data, leaving the object's matrix alone. Tangents follow
        /// the flipped x axis and change handedness for an odd number of flips (resources probe,
        /// sprite.attributes). Each renderer keeps its own copy so a shared sprite stays intact.</summary>
        Mesh FlippedMesh(Mesh source)
        {
            source.Sync();
            var key = (source, source.m_SyncVersion, m_FlipX, m_FlipY);
            if (m_FlippedMesh != null && m_FlippedKey == key) return m_FlippedMesh;
            if (m_FlippedMesh == null)
                using (Unbound.Runtime.ObjectRegistry.AsAssets())
                    m_FlippedMesh = new Mesh { name = "Flipped sprite", hideFlags = HideFlags.HideAndDontSave };
            m_FlippedMesh.CopyGeometryFrom(source);
            float x = m_FlipX ? -1f : 1f, y = m_FlipY ? -1f : 1f;
            for (int i = 0; i < m_FlippedMesh.m_Vertices.Length; i++)
            {
                m_FlippedMesh.m_Vertices[i].x *= x;
                m_FlippedMesh.m_Vertices[i].y *= y;
            }
            if (m_FlippedMesh.m_Normals != null)
                for (int i = 0; i < m_FlippedMesh.m_Normals.Length; i++)
                {
                    m_FlippedMesh.m_Normals[i].x *= x;
                    m_FlippedMesh.m_Normals[i].y *= y;
                }
            if (m_FlippedMesh.m_Tangents != null)
                for (int i = 0; i < m_FlippedMesh.m_Tangents.Length; i++)
                {
                    m_FlippedMesh.m_Tangents[i].x *= x;
                    m_FlippedMesh.m_Tangents[i].y *= y;
                    m_FlippedMesh.m_Tangents[i].w *= x * y;
                }
            m_FlippedMesh.RecalculateBounds();
            m_FlippedKey = key;
            return m_FlippedMesh;
        }

        /// <summary>A stretch of a sliced or tiled sprite along one axis: its local extent and the
        /// texture coordinates over it.</summary>
        struct Segment
        {
            internal float X0, X1, U0, U1;
            internal Segment(float x0, float x1, float u0, float u1) { X0 = x0; X1 = x1; U0 = u0; U1 = u1; }
        }

        static readonly List<Segment> s_SegmentsX = new List<Segment>(), s_SegmentsY = new List<Segment>();

        /// <summary>One axis of a Sliced or Tiled sprite, as Unity builds it (measured:
        /// tests/projects/resources tiled_*, sliced_*): borders keep their size unless the size is
        /// smaller than both together, then both shrink in proportion (Sliced squeezes their texels,
        /// Tiled cuts them, keeping the texels' size: a low border loses its inner end, a high one its
        /// outer end). The centre stretches (Sliced) or repeats from the low end, the last tile cut
        /// (Tiled Continuous); Adaptive stretches a whole number of tiles over it, adding one once the
        /// fraction of a tile left over reaches the threshold times the tile's size in units.</summary>
        static void AxisSegments(List<Segment> into, float size, float pivot01, float rectMin, float rectSize, float lo, float hi,
            float ppu, float textureSize, bool tiled, bool adaptive, float threshold)
        {
            into.Clear();
            float start = -pivot01 * size;
            float loU = lo / ppu, hiU = hi / ppu;
            if (loU + hiU > size && loU + hiU > 0f)
            {
                float k = size / (loU + hiU);
                loU *= k;
                hiU *= k;
            }
            float center = size - loU - hiU;
            if (loU > 0f) into.Add(new Segment(start, start + loU, rectMin / textureSize, (rectMin + (tiled ? loU * ppu : lo)) / textureSize));
            float c0 = rectMin + lo, c1 = rectMin + rectSize - hi;
            if (center > 0f)
            {
                float x = start + loU;
                float tile = (c1 - c0) / ppu;
                if (!tiled || tile <= 0f) into.Add(new Segment(x, x + center, c0 / textureSize, c1 / textureSize));
                else if (adaptive)
                {
                    float n = center / tile;
                    int count = (int)Math.Floor(n);
                    if (n - count >= threshold * tile) count++;
                    count = Math.Max(1, count);
                    float step = center / count;
                    for (int i = 0; i < count; i++)
                        into.Add(new Segment(x + i * step, i == count - 1 ? x + center : x + (i + 1) * step, c0 / textureSize, c1 / textureSize));
                }
                else
                {
                    int count = Math.Max(1, (int)Math.Ceiling(center / tile - 1e-4f));
                    for (int i = 0; i < count; i++)
                    {
                        float w = Math.Min(tile, center - i * tile);
                        into.Add(new Segment(x + i * tile, x + i * tile + w, c0 / textureSize, (c0 + w * ppu) / textureSize));
                    }
                }
            }
            if (hiU > 0f)
            {
                float u = rectMin + rectSize - hi;
                into.Add(new Segment(start + size - hiU, start + size, u / textureSize, (u + (tiled ? hiU * ppu : hi)) / textureSize));
            }
        }

        /// <summary>The Sliced or Tiled geometry: a quad per pair of segments of the two axes. A
        /// tight sprite mesh is drawn as its full rect (Unity warns about it, as here).</summary>
        Mesh SlicedMesh()
        {
            var key = (m_Sprite, m_Size, m_DrawMode, m_TileMode, m_AdaptiveModeThreshold);
            if (m_SlicedMesh != null && m_SlicedKey.Equals(key)) return m_SlicedMesh;
            m_SlicedKey = key;
            var s = m_Sprite;
            float ppu = s.pixelsPerUnit;
            var rect = s.rect;
            var border = s.border;
            var tex = s.texture;
            bool tiled = m_DrawMode == SpriteDrawMode.Tiled, adaptive = m_TileMode == SpriteTileMode.Adaptive;
            AxisSegments(s_SegmentsX, m_Size.x, s.pivot.x / rect.width, rect.x, rect.width, border.x, border.z, ppu, tex.width, tiled, adaptive, m_AdaptiveModeThreshold);
            AxisSegments(s_SegmentsY, m_Size.y, s.pivot.y / rect.height, rect.y, rect.height, border.y, border.w, ppu, tex.height, tiled, adaptive, m_AdaptiveModeThreshold);
            int quads = s_SegmentsX.Count * s_SegmentsY.Count;
            var vertices = new Vector3[quads * 4];
            var uvs = new Vector2[quads * 4];
            var tris = new int[quads * 6];
            int q = 0;
            foreach (var sy in s_SegmentsY)
                foreach (var sx in s_SegmentsX)
                {
                    int v = q * 4;
                    vertices[v] = new Vector3(sx.X0, sy.X0);
                    vertices[v + 1] = new Vector3(sx.X0, sy.X1);
                    vertices[v + 2] = new Vector3(sx.X1, sy.X1);
                    vertices[v + 3] = new Vector3(sx.X1, sy.X0);
                    uvs[v] = new Vector2(sx.U0, sy.U0);
                    uvs[v + 1] = new Vector2(sx.U0, sy.U1);
                    uvs[v + 2] = new Vector2(sx.U1, sy.U1);
                    uvs[v + 3] = new Vector2(sx.U1, sy.U0);
                    int t = q * 6;
                    tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                    tris[t + 3] = v + 2; tris[t + 4] = v + 3; tris[t + 5] = v;
                    q++;
                }
            if (m_DrawMode == SpriteDrawMode.Tiled && s.m_MeshType == SpriteMeshType.Tight && !m_WarnedTight)
            {
                m_WarnedTight = true;
                Debug.LogWarning("Sprite Tiling might not appear correctly because the Sprite used is not generated with Full Rect. To fix this, change the Mesh Type in the Sprite's import setting to Full Rect");
            }
            m_SlicedMesh ??= new Mesh();
            m_SlicedMesh.Clear();
            m_SlicedMesh.vertices = vertices;
            m_SlicedMesh.uv = uvs;
            Sprite.SpriteVertexFrames(m_SlicedMesh, vertices.Length);
            m_SlicedMesh.triangles = tris;
            return m_SlicedMesh;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            var r = body.Ref("m_Sprite");
            m_Sprite = r.IsNull ? null : resolver.Resolve(r, typeof(Sprite)) as Sprite;
            m_Color = body.Color("m_Color", Color.white);
            m_FlipX = body.Bool("m_FlipX");
            m_FlipY = body.Bool("m_FlipY");
            m_DrawMode = (SpriteDrawMode)body.Int("m_DrawMode");
            m_Size = body.Get("m_Size") != null ? body.Vector2("m_Size") : Vector2.one;
            m_AdaptiveModeThreshold = body.Float("m_AdaptiveModeThreshold", 0.5f);
            m_TileMode = (SpriteTileMode)body.Int("m_SpriteTileMode");
            m_MaskInteraction = (SpriteMaskInteraction)body.Int("m_MaskInteraction");
            m_SpriteSortPoint = (SpriteSortPoint)body.Int("m_SpriteSortPoint");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SpriteRenderer)source;
            CopyRendererData(s);
            m_Sprite = s.m_Sprite;
            m_Color = s.m_Color;
            m_FlipX = s.m_FlipX;
            m_FlipY = s.m_FlipY;
            m_DrawMode = s.m_DrawMode;
            m_Size = s.m_Size;
            m_MaskInteraction = s.m_MaskInteraction;
            m_TileMode = s.m_TileMode;
            m_SpriteSortPoint = s.m_SpriteSortPoint;
        }
    }

    /// <summary>Per-renderer overrides of material properties.</summary>
    public sealed partial class MaterialPropertyBlock
    {
        internal readonly Dictionary<int, object> m_Values = new Dictionary<int, object>();

        public bool isEmpty => m_Values.Count == 0;

        public void Clear() => m_Values.Clear();

        internal MaterialPropertyBlock Copy()
        {
            var b = new MaterialPropertyBlock();
            b.CopyFrom(this);
            return b;
        }

        internal void CopyFrom(MaterialPropertyBlock other)
        {
            foreach (var p in other.m_Values) m_Values[p.Key] = p.Value is Array a ? a.Clone() : p.Value;
        }

        public void SetFloat(string name, float value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetFloat(int nameID, float value) => m_Values[nameID] = value;
        public void SetInt(string name, int value) => m_Values[Shader.PropertyToID(name)] = (float)value;
        public void SetInt(int nameID, int value) => m_Values[nameID] = (float)value;
        public void SetInteger(string name, int value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetInteger(int nameID, int value) => m_Values[nameID] = value;
        public void SetVector(string name, Vector4 value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetVector(int nameID, Vector4 value) => m_Values[nameID] = value;
        public void SetColor(string name, Color value) => SetColor(Shader.PropertyToID(name), value);
        // SetColor converts when assigned, independently of the shader declaration. SetVector is raw.
        public void SetColor(int nameID, Color value) => m_Values[nameID] =
            QualitySettings.activeColorSpace == ColorSpace.Linear ? value.linear : value;
        public void SetMatrix(string name, Matrix4x4 value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetMatrix(int nameID, Matrix4x4 value) => m_Values[nameID] = value;
        public void SetTexture(string name, Texture value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetTexture(int nameID, Texture value) => m_Values[nameID] = value;
        public void SetFloatArray(string name, float[] values) => SetFloatArray(Shader.PropertyToID(name), values);
        public void SetFloatArray(int nameID, float[] values) => PropertyArrays.Set(m_Values, nameID, values);
        public void SetFloatArray(string name, List<float> values) => SetFloatArray(Shader.PropertyToID(name), values);
        public void SetFloatArray(int nameID, List<float> values) => PropertyArrays.Set(m_Values, nameID, values);
        public void SetVectorArray(string name, Vector4[] values) => SetVectorArray(Shader.PropertyToID(name), values);
        public void SetVectorArray(int nameID, Vector4[] values) => PropertyArrays.Set(m_Values, nameID, values);
        public void SetVectorArray(string name, List<Vector4> values) => SetVectorArray(Shader.PropertyToID(name), values);
        public void SetVectorArray(int nameID, List<Vector4> values) => PropertyArrays.Set(m_Values, nameID, values);
        public void SetMatrixArray(string name, Matrix4x4[] values) => SetMatrixArray(Shader.PropertyToID(name), values);
        public void SetMatrixArray(int nameID, Matrix4x4[] values) => PropertyArrays.Set(m_Values, nameID, values);
        public void SetMatrixArray(string name, List<Matrix4x4> values) => SetMatrixArray(Shader.PropertyToID(name), values);
        public void SetMatrixArray(int nameID, List<Matrix4x4> values) => PropertyArrays.Set(m_Values, nameID, values);
        public float[] GetFloatArray(string name) => GetFloatArray(Shader.PropertyToID(name));
        public float[] GetFloatArray(int nameID) => PropertyArrays.Get<float>(m_Values, nameID);
        public Vector4[] GetVectorArray(string name) => GetVectorArray(Shader.PropertyToID(name));
        public Vector4[] GetVectorArray(int nameID) => PropertyArrays.Get<Vector4>(m_Values, nameID);
        public Matrix4x4[] GetMatrixArray(string name) => GetMatrixArray(Shader.PropertyToID(name));
        public Matrix4x4[] GetMatrixArray(int nameID) => PropertyArrays.Get<Matrix4x4>(m_Values, nameID);
        public void GetFloatArray(string name, List<float> values) => PropertyArrays.Get(m_Values, Shader.PropertyToID(name), values);
        public void GetFloatArray(int nameID, List<float> values) => PropertyArrays.Get(m_Values, nameID, values);
        public void GetVectorArray(string name, List<Vector4> values) => PropertyArrays.Get(m_Values, Shader.PropertyToID(name), values);
        public void GetVectorArray(int nameID, List<Vector4> values) => PropertyArrays.Get(m_Values, nameID, values);
        public void GetMatrixArray(string name, List<Matrix4x4> values) => PropertyArrays.Get(m_Values, Shader.PropertyToID(name), values);
        public void GetMatrixArray(int nameID, List<Matrix4x4> values) => PropertyArrays.Get(m_Values, nameID, values);
        public void SetBuffer(string name, ComputeBuffer value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetBuffer(int nameID, ComputeBuffer value) => m_Values[nameID] = value;
        public void SetBuffer(string name, GraphicsBuffer value) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetBuffer(int nameID, GraphicsBuffer value) => m_Values[nameID] = value;
        /// <summary>A constant buffer for the cbuffer of that name (read from its start).</summary>
        public void SetConstantBuffer(string name, ComputeBuffer value, int offset, int size) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetConstantBuffer(int nameID, ComputeBuffer value, int offset, int size) => m_Values[nameID] = value;
        public void SetConstantBuffer(string name, GraphicsBuffer value, int offset, int size) => m_Values[Shader.PropertyToID(name)] = value;
        public void SetConstantBuffer(int nameID, GraphicsBuffer value, int offset, int size) => m_Values[nameID] = value;

        public bool HasProperty(string name) => m_Values.ContainsKey(Shader.PropertyToID(name));
        public bool HasProperty(int nameID) => m_Values.ContainsKey(nameID);
        public bool HasFloat(string name) => HasProperty(name);
        public bool HasFloat(int nameID) => HasProperty(nameID);
        public bool HasColor(string name) => HasProperty(name);
        public bool HasColor(int nameID) => HasProperty(nameID);
        public bool HasVector(string name) => HasProperty(name);
        public bool HasVector(int nameID) => HasProperty(nameID);
        public bool HasTexture(string name) => HasProperty(name);
        public bool HasTexture(int nameID) => HasProperty(nameID);

        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));
        public float GetFloat(int nameID) => m_Values.TryGetValue(nameID, out var v) && v is float f ? f : 0f;
        public int GetInt(string name) => (int)GetFloat(name);
        public int GetInt(int nameID) => (int)GetFloat(nameID);
        public Vector4 GetVector(string name) => GetVector(Shader.PropertyToID(name));
        public Vector4 GetVector(int nameID) => m_Values.TryGetValue(nameID, out var v)
            ? v is Vector4 x ? x : v is Color c ? (Vector4)c : Vector4.zero : Vector4.zero;
        public Color GetColor(string name) => GetColor(Shader.PropertyToID(name));
        public Color GetColor(int nameID) => m_Values.TryGetValue(nameID, out var v) && v is Color c
            ? QualitySettings.activeColorSpace == ColorSpace.Linear ? c.gamma : c : (Color)GetVector(nameID);
        public Matrix4x4 GetMatrix(string name) => GetMatrix(Shader.PropertyToID(name));
        public Matrix4x4 GetMatrix(int nameID) => m_Values.TryGetValue(nameID, out var v) && v is Matrix4x4 m ? m : Matrix4x4.identity;
        public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
        public Texture GetTexture(int nameID) => m_Values.TryGetValue(nameID, out var v) ? v as Texture : null;
    }

    /// <summary>The project's sorting layers (ProjectSettings/TagManager.asset `m_SortingLayers`).</summary>
    public partial struct SortingLayer
    {
        int m_Id;

        internal static readonly List<(string Name, int Id)> s_Layers = new List<(string, int)> { ("Default", 0) };

        public int id => m_Id;
        public string name => IDToName(m_Id);
        public int value => GetLayerValueFromID(m_Id);

        public static SortingLayer[] layers
        {
            get
            {
                var result = new SortingLayer[s_Layers.Count];
                for (int i = 0; i < result.Length; i++) result[i].m_Id = s_Layers[i].Id;
                return result;
            }
        }

        public static int GetLayerValueFromID(int id)
        {
            int defaultIndex = s_Layers.FindIndex(l => l.Id == 0);
            int index = s_Layers.FindIndex(l => l.Id == id);
            return index < 0 ? 0 : index - defaultIndex;
        }

        public static int GetLayerValueFromName(string name) => GetLayerValueFromID(NameToID(name));

        public static int NameToID(string name)
        {
            foreach (var l in s_Layers) if (l.Name == name) return l.Id;
            return 0;
        }

        public static string IDToName(int id)
        {
            foreach (var l in s_Layers) if (l.Id == id) return l.Name;
            return "<unknown layer>";
        }

        public static bool IsValid(int id) => s_Layers.Exists(l => l.Id == id);
    }

    public enum ReceiveGI
    {
        Lightmaps = 1,
        LightProbes = 2,
    }
}

namespace Unbound.Runtime
{
    internal static class RenderRegistry
    {
        /// <summary>The enabled renderers in the order they were turned on (draws that tie on every
        /// sort key keep it: measured, tests/projects/builtinrp sort_priority_none, the sprite
        /// made later is drawn over the earlier one).</summary>
        internal static readonly OrderedRenderers Renderers = new OrderedRenderers();
    }

    /// <summary>A set that enumerates in insertion order.</summary>
    internal sealed class OrderedRenderers : IEnumerable<UnityEngine.Renderer>
    {
        readonly List<UnityEngine.Renderer> m_Items = new List<UnityEngine.Renderer>();
        readonly Dictionary<UnityEngine.Renderer, int> m_Index = new Dictionary<UnityEngine.Renderer, int>();
        int m_Holes;

        internal int Count => m_Index.Count;

        internal bool Contains(UnityEngine.Renderer renderer) => m_Index.ContainsKey(renderer);

        /// <summary>A renderer's place in the order (larger: turned on later); -1 when absent.</summary>
        internal int IndexOf(UnityEngine.Renderer renderer) => m_Index.TryGetValue(renderer, out int i) ? i : -1;

        internal bool Add(UnityEngine.Renderer renderer)
        {
            if (m_Index.ContainsKey(renderer)) return false;
            m_Index[renderer] = m_Items.Count;
            m_Items.Add(renderer);
            return true;
        }

        internal bool Remove(UnityEngine.Renderer renderer)
        {
            if (!m_Index.Remove(renderer, out int at)) return false;
            m_Items[at] = null;
            if (++m_Holes > 64 && m_Holes > m_Items.Count / 2 && m_Enumerating == 0) Compact();
            return true;
        }

        int m_Enumerating;

        void Compact()
        {
            int to = 0;
            for (int i = 0; i < m_Items.Count; i++)
            {
                var r = m_Items[i];
                if (r is null) continue;
                m_Items[to] = r;
                m_Index[r] = to;
                to++;
            }
            m_Items.RemoveRange(to, m_Items.Count - to);
            m_Holes = 0;
        }

        /// <summary>Enumerates a snapshot of the count at the start; renderers added meanwhile
        /// are left for the next pass, removed ones are skipped.</summary>
        public IEnumerator<UnityEngine.Renderer> GetEnumerator()
        {
            m_Enumerating++;
            try
            {
                int count = m_Items.Count;
                for (int i = 0; i < count && i < m_Items.Count; i++)
                {
                    var r = m_Items[i];
                    if (r is not null) yield return r;
                }
            }
            finally
            {
                m_Enumerating--;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace UnityEngine.Rendering
{
    public partial struct ReflectionProbeBlendInfo
    {
        public ReflectionProbe probe;
        public float weight;
    }
}
