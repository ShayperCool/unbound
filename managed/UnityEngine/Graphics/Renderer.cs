using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>What every renderer has: materials, sorting, visibility, bounds.</summary>
    [global::UnityEngine.@RequireComponent(typeof(global::UnityEngine.Transform))] public partial class Renderer : Component
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
            get { MainThreadCheck.Check("get_enabled"); return m_Enabled; }
            set
            { MainThreadCheck.Check("set_enabled");
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
            Dirty();
        }

        /// <summary>The object-to-world matrix of the last frame a player-loop camera drew motion
        /// vectors; until then the one of its first motion vector draw (it has not moved).</summary>
        internal Matrix4x4 m_PreviousModel;
        internal bool m_HasPreviousModel;

        /// <summary>The object matrix the renderer's object motion follows (what
        /// <see cref="m_PreviousModel"/> keeps).</summary>
        internal virtual Matrix4x4 MotionMatrix => transform.localToWorldMatrix;

        /// <summary>A visible renderer turned off (disabled, or its object deactivated) becomes
        /// invisible at once (measured: tests/projects/messages, OnBecameInvisible during the
        /// Update that turned it off).</summary>
        internal override void OnEngineDisable()
        {
            if (!m_Registered) return;
            m_Registered = false;
            RenderRegistry.Renderers.Remove(this);
            SetVisible(false);
            Dirty();
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

        public bool isVisible { get { MainThreadCheck.Check("get_isVisible"); return m_IsVisible; } }

        /// <summary>Records a change of the renderer's state for the ObjectDispatchers tracking
        /// its type.</summary>
        internal void Dirty() => ObjectDispatch.Changed(this);

        bool m_AllowGPUDrivenRendering = true;
        /// <summary>Whether GPU-driven rendering may take the renderer (the render pipelines'
        /// DisallowGPUDrivenRendering turns it off); on by default, as the Editor's GPU Resident
        /// Drawer takes new renderers (tests/projects/residentdrawer).</summary>
        internal bool allowGPUDrivenRendering
        {
            get => m_AllowGPUDrivenRendering;
            set
            {
                if (m_AllowGPUDrivenRendering == value) return;
                m_AllowGPUDrivenRendering = value;
                Dirty();
            }
        }

        bool m_SmallMeshCulling = true;
        internal bool smallMeshCulling
        {
            get => m_SmallMeshCulling;
            set
            {
                if (m_SmallMeshCulling == value) return;
                m_SmallMeshCulling = value;
                Dirty();
            }
        }

        /// <summary>Whether GPU-driven rendering draws all of the renderer's materials
        /// (GPUDrivenProcessor): the ordinary cullings then leave it out of what they draw.</summary>
        internal bool m_GpuDriven;
        bool m_ForceRenderingOff;
        public bool forceRenderingOff
        {
            get { MainThreadCheck.Check("get_forceRenderingOff"); return m_ForceRenderingOff; }
            set { MainThreadCheck.Check("set_forceRenderingOff"); m_ForceRenderingOff = value; Dirty(); }
        }
        public ShadowCastingMode shadowCastingMode
        { get { MainThreadCheck.Check("get_shadowCastingMode"); return m_CastShadows; } set { MainThreadCheck.Check("set_shadowCastingMode"); m_CastShadows = value; Dirty(); } }
        public bool receiveShadows
        { get { MainThreadCheck.Check("get_receiveShadows"); return m_ReceiveShadows; } set { MainThreadCheck.Check("set_receiveShadows"); m_ReceiveShadows = value; Dirty(); } }
        bool m_StaticShadowCaster;
        public bool staticShadowCaster
        {
            get { MainThreadCheck.Check("GetIsStaticShadowCaster"); return m_StaticShadowCaster; }
            set { MainThreadCheck.Check("SetIsStaticShadowCaster"); m_StaticShadowCaster = value; Dirty(); }
        }
        MotionVectorGenerationMode m_MotionVectorGenerationMode = MotionVectorGenerationMode.Object;
        public MotionVectorGenerationMode motionVectorGenerationMode
        {
            get { MainThreadCheck.Check("get_motionVectorGenerationMode"); return m_MotionVectorGenerationMode; }
            set { MainThreadCheck.Check("set_motionVectorGenerationMode"); m_MotionVectorGenerationMode = value; Dirty(); }
        }
        LightProbeUsage m_LightProbeUsage = LightProbeUsage.BlendProbes;
        public LightProbeUsage lightProbeUsage
        {
            get { MainThreadCheck.Check("get_lightProbeUsage"); return m_LightProbeUsage; }
            set { MainThreadCheck.Check("set_lightProbeUsage"); m_LightProbeUsage = value; Dirty(); }
        }
        ReflectionProbeUsage m_ReflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        public ReflectionProbeUsage reflectionProbeUsage
        {
            get { MainThreadCheck.Check("get_reflectionProbeUsage"); return m_ReflectionProbeUsage; }
            set { MainThreadCheck.Check("set_reflectionProbeUsage"); m_ReflectionProbeUsage = value; Dirty(); }
        }
        public uint renderingLayerMask
        { get { MainThreadCheck.Check("get_renderingLayerMask"); return m_RenderingLayerMask; } set { MainThreadCheck.Check("set_renderingLayerMask"); m_RenderingLayerMask = value; Dirty(); } }
        int m_RendererPriority;
        public int rendererPriority
        {
            get { MainThreadCheck.Check("get_rendererPriority"); return m_RendererPriority; }
            set { MainThreadCheck.Check("set_rendererPriority"); m_RendererPriority = value; Dirty(); }
        }
        bool m_AllowOcclusionWhenDynamic = true;
        public bool allowOcclusionWhenDynamic
        {
            get { MainThreadCheck.Check("get_allowOcclusionWhenDynamic"); return m_AllowOcclusionWhenDynamic; }
            set { MainThreadCheck.Check("set_allowOcclusionWhenDynamic"); m_AllowOcclusionWhenDynamic = value; Dirty(); }
        }
        /// <summary>Whether StaticBatchingUtility combined the renderer.</summary>
        public bool isPartOfStaticBatch
        { get { MainThreadCheck.Check("get_isPartOfStaticBatch"); return m_StaticBatched; } }

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
        int m_LightmapIndex = -1;
        public int lightmapIndex
        {
            get { MainThreadCheck.Check("GetLightmapIndex"); return m_LightmapIndex; }
            set { MainThreadCheck.Check("SetLightmapIndex"); m_LightmapIndex = value; Dirty(); }
        }
        int m_RealtimeLightmapIndex = -1;
        public int realtimeLightmapIndex
        {
            get { MainThreadCheck.Check("GetLightmapIndex"); return m_RealtimeLightmapIndex; }
            set
            {
                MainThreadCheck.Check("SetLightmapIndex");
                // Another index is a script's own: no realtime GI data stands behind it.
                if (value != m_RealtimeLightmapIndex) m_RealtimeLightmapFromData = false;
                m_RealtimeLightmapIndex = value;
                Dirty();
            }
        }
        Vector4 m_LightmapScaleOffset = new Vector4(1, 1, 0, 0);
        public Vector4 lightmapScaleOffset
        {
            get { MainThreadCheck.Check("GetLightmapST"); return m_LightmapScaleOffset; }
            set { MainThreadCheck.Check("SetStaticLightmapST"); m_LightmapScaleOffset = value; Dirty(); }
        }
        Vector4 m_RealtimeLightmapScaleOffset = new Vector4(1, 1, 0, 0);
        /// <summary>The realtime lightmap atlas precomputed realtime GI updates for this renderer
        /// (<see cref="RealtimeGI"/>), null without one.</summary>
        internal Texture2D m_RealtimeLightmap;
        /// <summary>That atlas's directionality, once a directional scene's update made it.</summary>
        internal Texture2D m_RealtimeDirectionality;
        /// <summary>Its realtime lightmap index came from its scene's lighting data, so a realtime
        /// lightmap stands behind it even while Unbound has none for it; cleared when the index
        /// changes.</summary>
        internal bool m_RealtimeLightmapFromData;
        public Vector4 realtimeLightmapScaleOffset
        {
            get { MainThreadCheck.Check("GetLightmapST"); return m_RealtimeLightmapScaleOffset; }
            set { MainThreadCheck.Check("SetLightmapST"); m_RealtimeLightmapScaleOffset = value; Dirty(); }
        }
        Transform m_ProbeAnchor;
        public Transform probeAnchor
        {
            get { MainThreadCheck.Check("get_probeAnchor"); return m_ProbeAnchor; }
            set { MainThreadCheck.Check("set_probeAnchor"); m_ProbeAnchor = value; Dirty(); }
        }
        /// <summary>The light probe cell the renderer was last in (where the next lookup starts).</summary>
        internal int m_ProbeCell = -1;
        /// <summary>The probe constants its draws were last given, boxed (see <see cref="BoxedVectors"/>).</summary>
        internal BoxedVectors m_ProbeBoxes;
        /// <summary>The reflection probe vectors its draws were last given, boxed.</summary>
        internal BoxedVectors m_ReflectionBoxes;
        GameObject m_LightProbeProxyVolumeOverride;
        public GameObject lightProbeProxyVolumeOverride
        {
            get { MainThreadCheck.Check("get_lightProbeProxyVolumeOverride"); return m_LightProbeProxyVolumeOverride; }
            set { MainThreadCheck.Check("set_lightProbeProxyVolumeOverride"); m_LightProbeProxyVolumeOverride = value; Dirty(); }
        }
        public Matrix4x4 localToWorldMatrix
        { get { MainThreadCheck.Check("get_localToWorldMatrix"); return transform.localToWorldMatrix; } }
        public Matrix4x4 worldToLocalMatrix
        { get { MainThreadCheck.Check("get_worldToLocalMatrix"); return transform.worldToLocalMatrix; } }

        public int sortingLayerID
        {
            get { MainThreadCheck.Check("get_sortingLayerID"); return m_SortingLayerID; }
            set { MainThreadCheck.Check("set_sortingLayerID"); m_SortingLayerID = value; Dirty(); }
        }

        public string sortingLayerName
        {
            get { MainThreadCheck.Check("get_sortingLayerName"); return SortingLayer.IDToName(m_SortingLayerID); }
            set { MainThreadCheck.Check("set_sortingLayerName"); m_SortingLayerID = SortingLayer.NameToID(value); Dirty(); }
        }

        public int sortingOrder
        {
            get { MainThreadCheck.Check("get_sortingOrder"); return m_SortingOrder; }
            set { MainThreadCheck.Check("set_sortingOrder"); m_SortingOrder = (short)Mathf.Clamp(value, short.MinValue, short.MaxValue); Dirty(); }
        }

        internal int sortingGroupID { get; set; }
        internal int sortingGroupOrder { get; set; }

        // ------------------------------------------------------------------ materials

        public Material sharedMaterial
        {
            get { MainThreadCheck.Check("GetSharedMaterial"); return m_Materials.Length > 0 ? m_Materials[0] : null; }
            set
            { MainThreadCheck.Check("SetMaterial");
                if (m_Materials.Length == 0) m_Materials = new Material[1];
                m_Materials[0] = value;
                Dirty();
            }
        }

        public Material[] sharedMaterials
        {
            get { MainThreadCheck.Check("GetSharedMaterialArray"); return (Material[])m_Materials.Clone(); }
            set { MainThreadCheck.Check("SetMaterialArray"); m_Materials = value != null ? (Material[])value.Clone() : Array.Empty<Material>(); Dirty(); }
        }

        /// <summary>The first material, instantiated for this renderer on first access.</summary>
        public Material material
        {
            get
            { MainThreadCheck.Check("IsPersistent");
                if (m_Materials.Length == 0) m_Materials = new Material[1];
                var owned = Own(m_Materials[0]);
                if (!ReferenceEquals(owned, m_Materials[0])) Dirty();
                m_Materials[0] = owned;
                return m_Materials[0];
            }
            set { MainThreadCheck.Check("SetMaterial"); sharedMaterial = value; }
        }

        public Material[] materials
        {
            get
            { MainThreadCheck.Check("IsPersistent");
                for (int i = 0; i < m_Materials.Length; i++)
                {
                    var owned = Own(m_Materials[i]);
                    if (!ReferenceEquals(owned, m_Materials[i])) Dirty();
                    m_Materials[i] = owned;
                }
                return (Material[])m_Materials.Clone();
            }
            set { MainThreadCheck.Check("SetMaterialArray"); sharedMaterials = value; }
        }

        Material Own(Material shared)
        {
            if (shared == null)
            {
                var pipeline = GraphicsSettings.currentRenderPipeline;
                shared = pipeline != null ? pipeline.defaultMaterial : null;
                shared ??= BuiltinShaders.Resolve(BuiltinShaders.ExtraGuid, BuiltinShaders.DefaultMaterialFileId) as Material;
            }
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

        /// <summary>The block a material this renderer draws with was made under (null: none).</summary>
        internal MaterialPropertyBlock BlockOfDrawn(Material drawn)
        {
            if (m_BlockMaterials != null)
                foreach (var b in m_BlockMaterials)
                    if (b.Drawn != null && ReferenceEquals(b.Drawn, drawn)) return b.Block;
            return null;
        }

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
                drawn = new Material(source) { hideFlags = HideFlags.HideAndDontSave, parent = null };
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

        public void SetPropertyBlock(MaterialPropertyBlock properties)
        {
            m_PropertyBlock = properties?.Copy();
            Dirty();
        }

        public void SetPropertyBlock(MaterialPropertyBlock properties, int materialIndex)
        {
            if (properties == null || properties.isEmpty)
                m_MaterialBlocks?.Remove(materialIndex);
            else
                (m_MaterialBlocks ??= new Dictionary<int, MaterialPropertyBlock>())[materialIndex] = properties.Copy();
            Dirty();
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
        public void ResetLocalBounds() => ResetLocalBoundsCore();
        internal virtual void ResetLocalBoundsCore() => m_LocalBoundsOverride = null;

        // Set through bounds / localBounds: used (for culling too) until reset, as in Unity.
        Bounds? m_BoundsOverride;
        Bounds? m_LocalBoundsOverride;

        // ------------------------------------------------------------------ bounds

        public Bounds bounds
        {
            get
            { MainThreadCheck.Check("get_bounds");
                if (m_BoundsOverride is { } overridden)
                    return overridden;
                return GetWorldBounds();
            }
            set { MainThreadCheck.Check("set_bounds"); m_BoundsOverride = value; }
        }

        public Bounds localBounds
        {
            get { MainThreadCheck.Check("get_localBounds"); return m_LocalBoundsOverride ?? GetLocalBounds(); }
            set { MainThreadCheck.Check("set_localBounds"); SetLocalBounds(value); }
        }

        internal virtual void SetLocalBounds(Bounds value) => m_LocalBoundsOverride = value;

        /// <summary>The local bounds set through bounds / localBounds, if any.</summary>
        internal Bounds? LocalBoundsOverride => m_LocalBoundsOverride;

        internal virtual Bounds GetWorldBounds()
        {
            var local = localBounds;
            if (m_StaticBatched) return TransformBounds(local, BoundsMatrix);
            // Cameras ask every frame: the box is made again only when the local bounds or the
            // transform (or a parent) changed.
            var transform = m_GameObject.m_Transform;
            int version = transform.SyncedVersion;
            if (m_WorldBoundsVersion != version || !ReferenceEquals(m_WorldBoundsTransform, transform) || !(FloatBits.Same(m_WorldBoundsLocal.center, local.center) && FloatBits.Same(m_WorldBoundsLocal.extents, local.extents)))
            {
                m_WorldBounds = TransformBounds(local, BoundsMatrix);
                m_WorldBoundsLocal = local;
                m_WorldBoundsVersion = version;
                m_WorldBoundsTransform = transform;
            }
            return m_WorldBounds;
        }

        Bounds m_WorldBounds, m_WorldBoundsLocal;
        /// <summary>The transform's version <see cref="m_WorldBounds"/> were made at; none yet.</summary>
        int? m_WorldBoundsVersion;
        /// <summary>The transform those were made with (a RectTransform can replace it).</summary>
        Transform m_WorldBoundsTransform;

        internal static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            var c = matrix.MultiplyPoint3x4(local.center);
            var e = local.extents;
            var x = matrix.MultiplyVector(new Vector3(e.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, e.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, e.z));
            var extents = new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
            return new Bounds(c, extents * 2f);
        }

        internal virtual void PrepareCullingBounds() { }

        /// <summary>The renderer has nothing to draw this frame: cameras neither draw it nor see
        /// it (no visibility, no OnWillRenderObject).</summary>
        internal virtual bool NothingToDraw => false;

        /// <summary>The world bounds cameras cull this renderer by (after
        /// <see cref="PrepareCullingBounds"/>): its bounds, unless the renderer keeps others.</summary>
        internal virtual Bounds CullingBounds => bounds;

        internal virtual Bounds GetLocalBounds() => new Bounds(Vector3.zero, Vector3.zero);

        // ------------------------------------------------------------------ rendering

        /// <summary>Adds this renderer's draws for a camera. Overridden by each renderer kind.</summary>
        internal virtual void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into) { }

        /// <summary>The current mesh and its matrix, also used by command buffer draws.</summary>
        /// <summary>A renderer reflection already baked into its submitted geometry.</summary>
        internal virtual bool DrawFlipWinding(in Matrix4x4 model) => false;

        internal virtual Mesh DrawGeometry(out Matrix4x4 model)
        {
            model = RenderMatrix;
            return null;
        }

        /// <summary>How the renderer's draws interact with sprite masks (renderers that have a
        /// maskInteraction).</summary>
        internal virtual SpriteMaskInteraction MaskInteraction => SpriteMaskInteraction.None;

        /// <summary>The lists of the passes below in progress, kept for the next ones (a script
        /// may render a camera from OnWillRenderObject: each pass has its own).</summary>
        static readonly Stack<List<Renderer>> s_WillRenderLists = new Stack<List<Renderer>>();

        /// <summary>OnWillRenderObject to the enabled scripts on the objects of the renderers a camera
        /// sees (its culling mask lets them through and their bounds touch its frustum), before any
        /// of them is drawn (Unity sends it from the camera's culling, once per camera). Scripts may
        /// change the scene meanwhile.</summary>
        internal static void SendWillRenderObject(Matrix4x4 viewProjection, int cullingMask)
        {
            // Nothing changes the registry before the first script gets the message: walk it as it
            // is, and copy the rest from that renderer on, since scripts may change the scene.
            var list = s_WillRenderLists.Count > 0 ? s_WillRenderLists.Pop() : new List<Renderer>();
            try
            {
                foreach (var renderer in RenderRegistry.Renderers)
                    if (list.Count > 0 || WillRender(renderer, viewProjection, cullingMask)) list.Add(renderer);
                for (int r = 0; r < list.Count; r++)
                {
                    var renderer = list[r];
                    if (r > 0 && !WillRender(renderer, viewProjection, cullingMask)) continue;
                    var components = renderer.m_GameObject.m_Components;
                    for (int i = 0; i < components.Count; i++)
                    {
                        if (!(components[i] is MonoBehaviour script) || !script.IsActiveAndEnabledNoThrow) continue;
                        var call = ScriptMessages.For(script.GetType()).OnWillRenderObject;
                        if (call == null) continue;
                        try { call(script); }
                        catch (Exception e) { Debug.LogException(e, script); }
                    }
                }
            }
            finally
            {
                list.Clear();
                s_WillRenderLists.Push(list);
            }
        }

        /// <summary>Whether a camera with this mask sends the renderer's object OnWillRenderObject:
        /// the renderer draws on a layer the camera sees, has an enabled script with the message
        /// and bounds that touch the frustum (the bounds and frustum are left alone for objects
        /// without such a script).</summary>
        static bool WillRender(Renderer renderer, Matrix4x4 viewProjection, int cullingMask)
        {
            if (!renderer || renderer.forceRenderingOff || !renderer.m_Enabled) return false;
            var go = renderer.m_GameObject;
            if ((cullingMask & (1 << go.m_Layer)) == 0 || !go.activeInHierarchy || renderer.NothingToDraw) return false;
            renderer.PrepareCullingBounds();
            return HasWillRenderScript(go) && FrameBuilder.InFrustum(viewProjection, renderer.CullingBounds);
        }

        static bool HasWillRenderScript(GameObject go)
        {
            var components = go.m_Components;
            for (int i = 0; i < components.Count; i++)
                if (components[i] is MonoBehaviour script && script.IsActiveAndEnabledNoThrow
                    && ScriptMessages.For(script.GetType()).OnWillRenderObject != null)
                    return true;
            return false;
        }

        internal void SetVisible(bool visible)
        {
            if (m_IsVisible == visible) return;
            m_IsVisible = visible;
            Messaging.SendFast(m_GameObject, visible ? "OnBecameVisible" : "OnBecameInvisible");
            if (visible) Animator.RendererBecameVisible(m_GameObject);
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
            m_CastShadows = (ShadowCastingMode)body.Int("m_CastShadows", (int)m_CastShadows);
            m_ReceiveShadows = body.Bool("m_ReceiveShadows", m_ReceiveShadows);
            m_RenderingLayerMask = (uint)body.Long("m_RenderingLayerMask", 1);
            lightProbeUsage = (LightProbeUsage)body.Int("m_LightProbeUsage", (int)lightProbeUsage);
            reflectionProbeUsage = (ReflectionProbeUsage)body.Int("m_ReflectionProbeUsage", (int)reflectionProbeUsage);
            motionVectorGenerationMode = (MotionVectorGenerationMode)body.Int("m_MotionVectors", (int)motionVectorGenerationMode);
            rayTracingMode = (UnityEngine.Experimental.Rendering.RayTracingMode)body.Int("m_RayTracingMode", (int)rayTracingMode);
            rayTracingAccelerationStructureBuildFlags = (RayTracingAccelerationStructureBuildFlags)body.Int(
                "m_RayTracingAccelStructBuildFlags", (int)rayTracingAccelerationStructureBuildFlags);
            rayTracingAccelerationStructureBuildFlagsOverride = body.Bool("m_RayTracingAccelStructBuildFlagsOverride");
            var anchor = body.Ref("m_ProbeAnchor");
            if (!anchor.IsNull) probeAnchor = resolver.Resolve(anchor, typeof(Transform)) as Transform;
        }

        internal void WriteRendererData(UnityYamlWriter.Writer w)
        {
            w.Bool("m_Enabled", m_Enabled);
            w.Int("m_CastShadows", (int)m_CastShadows);
            w.Bool("m_ReceiveShadows", m_ReceiveShadows);
            w.Int("m_MotionVectors", (int)motionVectorGenerationMode);
            w.Int("m_LightProbeUsage", (int)lightProbeUsage);
            w.Int("m_ReflectionProbeUsage", (int)reflectionProbeUsage);
            w.Int("m_RayTracingMode", (int)rayTracingMode);
            w.Bool("m_RayTracingAccelStructBuildFlagsOverride", rayTracingAccelerationStructureBuildFlagsOverride);
            w.Int("m_RayTracingAccelStructBuildFlags", (int)rayTracingAccelerationStructureBuildFlags);
            w.Int("m_RenderingLayerMask", m_RenderingLayerMask);
            w.RefList("m_Materials", m_Materials);
            w.Ref("m_ProbeAnchor", probeAnchor);
            w.Int("m_SortingLayerID", m_SortingLayerID);
            w.Int("m_SortingOrder", m_SortingOrder);
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
            motionVectorGenerationMode = source.motionVectorGenerationMode;
            rayTracingMode = source.rayTracingMode;
            rayTracingAccelerationStructureBuildFlags = source.rayTracingAccelerationStructureBuildFlags;
            rayTracingAccelerationStructureBuildFlagsOverride = source.rayTracingAccelerationStructureBuildFlagsOverride;
            probeAnchor = source.probeAnchor;
            lightmapIndex = source.lightmapIndex;
            lightmapScaleOffset = source.lightmapScaleOffset;
            realtimeLightmapIndex = source.realtimeLightmapIndex;
            realtimeLightmapScaleOffset = source.realtimeLightmapScaleOffset;
            m_RealtimeLightmapFromData = source.m_RealtimeLightmapFromData;
        }
    }

    [global::UnityEngine.@RequireComponent(typeof(global::UnityEngine.Transform))] [ClassId(33)]
    public sealed partial class MeshFilter : Component
    {
        internal Mesh m_Mesh;

        public MeshFilter() { }

        public Mesh sharedMesh
        {
            get => m_Mesh;
            set
            {
                m_Mesh = value;
                RendererChanged();
            }
        }

        /// <summary>The renderer draws the filter's mesh: a change of the mesh is the renderer's.</summary>
        void RendererChanged()
        {
            if (m_GameObject != null && m_GameObject.GetComponent<MeshRenderer>() is MeshRenderer renderer) renderer.Dirty();
        }

        /// <summary>The mesh, instantiated for this filter on first access.</summary>
        public Mesh mesh
        {
            get
            {
                if (m_Mesh != null && m_Mesh.m_InstanceOwnerId == GetInstanceID()) return m_Mesh;
                var before = m_Mesh;
                if (m_Mesh != null && m_Mesh.vertexCount > 0)
                {
                    var source = m_Mesh;
                    m_Mesh = (Mesh)((Instantiation.ICloneable)source).CloneObject();
                    m_Mesh.name = source.name + " Instance";
                }
                else
                {
                    m_Mesh ??= new Mesh();
                    m_Mesh.name = gameObject.name;
                }
                m_Mesh.m_InstanceOwnerId = GetInstanceID();
                // A new mesh drawn: the renderer's change.
                if (!ReferenceEquals(before, m_Mesh)) RendererChanged();
                return m_Mesh;
            }
            set => sharedMesh = value;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var r = body.Ref("m_Mesh");
            m_Mesh = r.IsNull ? null : resolver.Resolve(r, typeof(Mesh)) as Mesh;
        }

        internal override void WriteSerialized(UnityYamlWriter.Writer w) => w.Ref("m_Mesh", m_Mesh);

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (MeshFilter)source;
            m_Mesh = s.m_Mesh;
        }
    }

    [ClassId(23)]
    public partial class MeshRenderer : Renderer
    {
        public MeshRenderer()
        {
            if (Compat.DefaultConstructorStateMeasured) rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.DynamicTransform;
        }

        /// <summary>A mesh whose channels (colors, normals, tangents, uvs) replace the mesh's for
        /// this renderer (vertex painting; measured: tests/projects/builtinfx vertex_streams).</summary>
        Mesh m_AdditionalVertexStreams;
        public Mesh additionalVertexStreams
        {
            get => m_AdditionalVertexStreams;
            set
            {
                m_AdditionalVertexStreams = value;
                Dirty();
            }
        }
        Mesh m_EnlightenVertexStream;
        /// <summary>Realtime GI's own lightmap UVs for this renderer (GPU-driven rendering refuses
        /// a renderer that has them).</summary>
        public Mesh enlightenVertexStream
        {
            get => m_EnlightenVertexStream;
            set
            {
                m_EnlightenVertexStream = value;
                Dirty();
            }
        }
        /// <summary>A statically batched renderer keeps drawing its own mesh (Unbound combines no
        /// meshes): its submeshes start at 0.</summary>
        [Unbound.EditorFaithful("static batches draw the renderers' own meshes in Unbound; their first submesh is 0")]
        public int subMeshStartIndex => 0;
        Mesh m_StreamedMesh;
        (Mesh, int, Mesh, int, Mesh, int) m_StreamedKey;
        /// <summary>The UV channel realtime lightmap UVs are drawn from (TEXCOORD2).</summary>
        const int RealtimeUVChannel = 2;

        /// <summary>The mesh with the additional vertex streams' channels over its own, and the
        /// realtime lightmap UVs of <see cref="enlightenVertexStream"/> over those.</summary>
        Mesh Streamed(Mesh mesh)
        {
            var streams = additionalVertexStreams;
            var realtime = enlightenVertexStream;
            if (streams == null && realtime == null)
            {
                // The merged mesh of streams that are gone is freed.
                if (m_StreamedMesh != null)
                {
                    Object.DestroyImmediate(m_StreamedMesh);
                    m_StreamedMesh = null;
                    m_StreamedKey = default;
                }
                return mesh;
            }
            mesh.Sync();
            streams?.Sync();
            realtime?.Sync();
            var key = (mesh, mesh.m_SyncVersion, streams, streams?.m_SyncVersion ?? 0, realtime, realtime?.m_SyncVersion ?? 0);
            if (m_StreamedMesh != null && m_StreamedKey == key) return m_StreamedMesh;
            if (m_StreamedMesh == null)
                using (Unbound.Runtime.ObjectRegistry.AsAssets())
                    m_StreamedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            m_StreamedMesh.CopyGeometryFrom(mesh);
            m_StreamedMesh.name = mesh.name;
            uint overridden = streams != null ? m_StreamedMesh.OverrideVertexChannels(streams) : 0;
            // The realtime lightmap UVs (TEXCOORD2) alone, from this mesh's own CPU channel.
            uint realtimeUVs = realtime != null ? m_StreamedMesh.OverrideUVChannel(realtime, RealtimeUVChannel) : 0;
            m_StreamedMesh.ShareDrawIndices(mesh);
            m_StreamedMesh.ShareVertexSources(mesh, streams, overridden & ~realtimeUVs, realtimeUVs);
            m_StreamedMesh.m_Dirty = true;
            m_StreamedKey = key;
            return m_StreamedMesh;
        }
        public float scaleInLightmap { get; set; } = 1f;
        public ReceiveGI receiveGI { get; set; } = ReceiveGI.Lightmaps;
        /// <summary>Lighting settings of the renderer that realtime lightmap UVs depend on
        /// (Preserve UVs, Min Chart Size, its own Lightmap Parameters; without Preserve UVs,
        /// the limits of the charts the precompute makes: Max Distance, Max Angle in degrees and
        /// Ignore Normals).</summary>
        internal bool m_PreserveUVs;
        internal int m_MinimumChartSize = 4;
        internal float m_AutoUVMaxDistance = 0.5f;
        internal float m_AutoUVMaxAngle = 89f;
        internal bool m_IgnoreNormalsForChartDetection;
        internal Unbound.Runtime.ObjectRef m_LightmapParameters;

        /// <summary>The mesh this renderer draws its realtime lightmap with: its mesh with the
        /// vertex streams over it (null without a mesh).</summary>
        internal Mesh RealtimeGIMesh() =>
            m_GameObject.GetComponent<MeshFilter>()?.m_Mesh is Mesh mesh ? Streamed(mesh) : null;
        public bool stitchLightmapSeams { get; set; } = Compat.DefaultConstructorStateMeasured;

        internal override Bounds GetLocalBounds()
        {
            // A TextMesh's generated text replaces a MeshFilter's mesh (see TextMesh).
            if (TextMesh.Of(m_GameObject) is TextMesh text)
                return text.RenderMesh() is Mesh generated ? generated.bounds : default;
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
            // A TextMesh's generated text, drawn with this renderer's materials; none without a font.
            var mesh = TextMesh.Of(m_GameObject) is TextMesh text ? text.RenderMesh() : m_GameObject.GetComponent<MeshFilter>()?.m_Mesh;
            return mesh != null ? Streamed(mesh) : null;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadRendererData(body, resolver);
            scaleInLightmap = body.Float("m_ScaleInLightmap", 1f);
            m_PreserveUVs = body.Bool("m_PreserveUVs");
            m_MinimumChartSize = body.Int("m_MinimumChartSize", 4);
            m_AutoUVMaxDistance = body.Float("m_AutoUVMaxDistance", 0.5f);
            m_AutoUVMaxAngle = body.Float("m_AutoUVMaxAngle", 89f);
            m_IgnoreNormalsForChartDetection = body.Bool("m_IgnoreNormalsForChartDetection");
            m_LightmapParameters = body.Ref("m_LightmapParameters");
        }

        internal override void WriteSerialized(UnityYamlWriter.Writer w)
        {
            WriteRendererData(w);
            w.Float("m_ScaleInLightmap", scaleInLightmap);
            w.Bool("m_PreserveUVs", m_PreserveUVs);
            w.Int("m_MinimumChartSize", m_MinimumChartSize);
            w.Float("m_AutoUVMaxDistance", m_AutoUVMaxDistance);
            w.Float("m_AutoUVMaxAngle", m_AutoUVMaxAngle);
            w.Bool("m_IgnoreNormalsForChartDetection", m_IgnoreNormalsForChartDetection);
            w.Line(1, "m_LightmapParameters", UnityYamlWriter.Writer.ReferenceText(m_LightmapParameters));
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            CopyRendererData((Renderer)source);
            stitchLightmapSeams = ((MeshRenderer)source).stitchLightmapSeams;
            var s = (MeshRenderer)source;
            scaleInLightmap = s.scaleInLightmap;
            m_PreserveUVs = s.m_PreserveUVs;
            m_MinimumChartSize = s.m_MinimumChartSize;
            m_AutoUVMaxDistance = s.m_AutoUVMaxDistance;
            m_AutoUVMaxAngle = s.m_AutoUVMaxAngle;
            m_IgnoreNormalsForChartDetection = s.m_IgnoreNormalsForChartDetection;
            m_LightmapParameters = s.m_LightmapParameters;
        }
    }

    [global::UnityEngine.@RequireComponent(typeof(global::UnityEngine.Transform))] [ClassId(212)]
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

        public SpriteRenderer()
        {
            if (!Compat.DefaultConstructorStateMeasured) return;
            shadowCastingMode = ShadowCastingMode.Off;
            receiveShadows = false;
        }

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
                return Flipped(new Bounds(new Vector3(min.x + m_Size.x * 0.5f, min.y + m_Size.y * 0.5f, 0f), new Vector3(m_Size.x, m_Size.y, 0.2f)));
            }
            return Flipped(m_Sprite.bounds);
        }

        /// <summary>The sprite's bounds as Simple drawing has them, flipped, else the set local bounds
        /// (what a BoxCollider2D added to the object fits, whatever the draw mode); empty without a
        /// sprite (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteoutlines).</summary>
        internal Bounds SimpleSpriteBounds() => LocalBoundsOverride ?? (m_Sprite == null ? default : Flipped(m_Sprite.bounds));

        /// <summary>Bounds mirrored through the pivot along each flipped axis, as the flipped vertices
        /// are (matches the Unity 6000.0.71f1 Editor's localBounds, tests/projects/spriteoutlines).</summary>
        Bounds Flipped(Bounds bounds)
        {
            if (!m_FlipX && !m_FlipY) return bounds;
            var c = bounds.center;
            bounds.center = new Vector3(m_FlipX ? -c.x : c.x, m_FlipY ? -c.y : c.y, c.z);
            return bounds;
        }

        internal override void CollectDraws(FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            if (!MaskShape(out var mesh, out var model, out var texture)) return;
            FrameBuilder.AddMeshDraws(this, mesh, model, m_Color, camera, into, texture, m_Sprite.SecondaryProperties());
        }

        bool m_WarnedTexturelessLayout;

        /// <summary>The geometry the renderer draws (also what a mask beside it masks with).</summary>
        internal bool MaskShape(out Mesh mesh, out Matrix4x4 model, out Texture texture)
        {
            mesh = null;
            model = default;
            texture = null;
            if (m_Sprite == null || m_Sprite.texture == null && !m_Sprite.m_DrawnWithoutTexture) return false;
            // Sliced and tiled drawing lays the texture out; a sprite without one has none to lay out
            // (unmeasured): it is not drawn, with a warning.
            if (m_Sprite.texture == null && m_DrawMode != SpriteDrawMode.Simple)
            {
                if (!m_WarnedTexturelessLayout)
                {
                    m_WarnedTexturelessLayout = true;
                    Debug.LogWarning($"Unbound: the sprite renderer '{name}' draws '{m_Sprite.name}', a sprite without a texture, in {m_DrawMode} mode, which is not supported; it is not drawn.");
                }
                return false;
            }
            mesh = m_DrawMode == SpriteDrawMode.Simple ? DeformedMesh() ?? m_Sprite.RenderMesh() : SlicedMesh();
            model = UnityEngine.U2D.PixelPerfectRendering.Snap(transform.localToWorldMatrix);
            if (Unbound.Runtime.Compat.RenderingMeasured && (!float.IsFinite(model.m03) || !float.IsFinite(model.m13) || !float.IsFinite(model.m23))) return false;
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
            // The rect where the sprite is drawn from: on a packed atlas's page, placed as its
            // texture rect is (in the page's rect space: a variant's master's).
            var rect = s.DrawnRect;
            var space = s.RectSpace;
            var border = s.border;
            bool tiled = m_DrawMode == SpriteDrawMode.Tiled, adaptive = m_TileMode == SpriteTileMode.Adaptive;
            AxisSegments(s_SegmentsX, m_Size.x, s.pivot.x / rect.width, rect.x, rect.width, border.x, border.z, ppu, space.x, tiled, adaptive, m_AdaptiveModeThreshold);
            AxisSegments(s_SegmentsY, m_Size.y, s.pivot.y / rect.height, rect.y, rect.height, border.y, border.w, ppu, space.y, tiled, adaptive, m_AdaptiveModeThreshold);
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
            s.MirrorPackedUVs(uvs);
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

        internal override void WriteSerialized(UnityYamlWriter.Writer w)
        {
            WriteRendererData(w);
            w.Ref("m_Sprite", m_Sprite);
            w.Color("m_Color", m_Color);
            w.Bool("m_FlipX", m_FlipX);
            w.Bool("m_FlipY", m_FlipY);
            w.Int("m_DrawMode", (int)m_DrawMode);
            w.Vector2("m_Size", m_Size);
            w.Float("m_AdaptiveModeThreshold", m_AdaptiveModeThreshold);
            w.Int("m_SpriteTileMode", (int)m_TileMode);
            w.Int("m_MaskInteraction", (int)m_MaskInteraction);
            w.Int("m_SpriteSortPoint", (int)m_SpriteSortPoint);
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

        static readonly int[] s_ShArrayIds =
        {
            Shader.PropertyToID("unity_SHAr"), Shader.PropertyToID("unity_SHAg"), Shader.PropertyToID("unity_SHAb"),
            Shader.PropertyToID("unity_SHBr"), Shader.PropertyToID("unity_SHBg"), Shader.PropertyToID("unity_SHBb"),
            Shader.PropertyToID("unity_SHC"),
        };
        static readonly int s_ProbesOcclusionId = Shader.PropertyToID("unity_ProbesOcclusion");

        /// <summary>The checks of the probe array copies, with the Editor's messages (measured:
        /// tests/projects/lightprobes mpb errors).</summary>
        static void CheckProbeCopy(int length, int sourceStart, int destStart, int count)
        {
            if (sourceStart < 0) throw new ArgumentOutOfRangeException(nameof(sourceStart), "Argument sourceStart must not be negative.");
            if (destStart < 0) throw new ArgumentOutOfRangeException(nameof(sourceStart), "Argument destStart must not be negative.");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Argument count must not be negative.");
            if (length < sourceStart + count) throw new ArgumentOutOfRangeException("The specified source start index or count is out of the range.");
        }

        /// <summary>The Vector4 array property <paramref name="nameID"/>, made of
        /// <paramref name="count"/> zeros when the block has none; one it has keeps its length.</summary>
        Vector4[] ProbeArray(int nameID, int count)
        {
            if (m_Values.TryGetValue(nameID, out var v) && v is Vector4[] existing) return existing;
            var array = new Vector4[count];
            m_Values[nameID] = array;
            return array;
        }

        /// <summary>
        /// Writes probes as the seven ShadeSH9 constant arrays (unity_SHAr .. unity_SHC) instanced
        /// draws and renderers with LightProbeUsage.CustomProvided take theirs from. A block without
        /// them gets arrays of <paramref name="count"/> elements; arrays it has keep their length,
        /// and elements past it are not written.
        /// </summary>
        void CopySH(IReadOnlyList<SphericalHarmonicsL2> lightProbes, int sourceStart, int destStart, int count)
        {
            if (count == 0) return;
            var arrays = new Vector4[7][];
            for (int k = 0; k < 7; k++) arrays[k] = ProbeArray(s_ShArrayIds[k], count);
            for (int i = 0; i < count; i++)
            {
                lightProbes[sourceStart + i].ShaderConstants(out var ar, out var ag, out var ab, out var br, out var bg, out var bb, out var c);
                Span<Vector4> values = stackalloc Vector4[] { ar, ag, ab, br, bg, bb, c };
                for (int k = 0; k < 7; k++)
                    if (destStart + i < arrays[k].Length) arrays[k][destStart + i] = values[k];
            }
        }

        void CopyOcclusion(IReadOnlyList<Vector4> occlusionProbes, int sourceStart, int destStart, int count)
        {
            if (count == 0) return;
            var array = ProbeArray(s_ProbesOcclusionId, count);
            for (int i = 0; i < count && destStart + i < array.Length; i++) array[destStart + i] = occlusionProbes[sourceStart + i];
        }

        public void CopySHCoefficientArraysFrom(List<SphericalHarmonicsL2> lightProbes)
        {
            if (lightProbes == null) throw new ArgumentNullException(nameof(lightProbes));
            CopySHCoefficientArraysFrom(lightProbes, 0, 0, lightProbes.Count);
        }

        public void CopySHCoefficientArraysFrom(SphericalHarmonicsL2[] lightProbes)
        {
            if (lightProbes == null) throw new ArgumentNullException(nameof(lightProbes));
            CopySHCoefficientArraysFrom(lightProbes, 0, 0, lightProbes.Length);
        }

        public void CopySHCoefficientArraysFrom(List<SphericalHarmonicsL2> lightProbes, int sourceStart, int destStart, int count)
        {
            if (lightProbes == null) throw new ArgumentNullException(nameof(lightProbes));
            CheckProbeCopy(lightProbes.Count, sourceStart, destStart, count);
            CopySH(lightProbes, sourceStart, destStart, count);
        }

        public void CopySHCoefficientArraysFrom(SphericalHarmonicsL2[] lightProbes, int sourceStart, int destStart, int count)
        {
            if (lightProbes == null) throw new ArgumentNullException(nameof(lightProbes));
            CheckProbeCopy(lightProbes.Length, sourceStart, destStart, count);
            CopySH(lightProbes, sourceStart, destStart, count);
        }

        public void CopyProbeOcclusionArrayFrom(List<Vector4> occlusionProbes)
        {
            if (occlusionProbes == null) throw new ArgumentNullException(nameof(occlusionProbes));
            CopyProbeOcclusionArrayFrom(occlusionProbes, 0, 0, occlusionProbes.Count);
        }

        public void CopyProbeOcclusionArrayFrom(Vector4[] occlusionProbes)
        {
            if (occlusionProbes == null) throw new ArgumentNullException(nameof(occlusionProbes));
            CopyProbeOcclusionArrayFrom(occlusionProbes, 0, 0, occlusionProbes.Length);
        }

        public void CopyProbeOcclusionArrayFrom(List<Vector4> occlusionProbes, int sourceStart, int destStart, int count)
        {
            if (occlusionProbes == null) throw new ArgumentNullException(nameof(occlusionProbes));
            CheckProbeCopy(occlusionProbes.Count, sourceStart, destStart, count);
            CopyOcclusion(occlusionProbes, sourceStart, destStart, count);
        }

        public void CopyProbeOcclusionArrayFrom(Vector4[] occlusionProbes, int sourceStart, int destStart, int count)
        {
            if (occlusionProbes == null) throw new ArgumentNullException(nameof(occlusionProbes));
            CheckProbeCopy(occlusionProbes.Length, sourceStart, destStart, count);
            CopyOcclusion(occlusionProbes, sourceStart, destStart, count);
        }
    }

    /// <summary>The project's sorting layers (ProjectSettings/TagManager.asset `m_SortingLayers`).</summary>
    public partial struct SortingLayer
    {
        int m_Id;

        public delegate void LayerCallback(SortingLayer layer);
        public static LayerCallback onLayerAdded;
        public static LayerCallback onLayerRemoved;

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

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<UnityEngine.Renderer> IEnumerable<UnityEngine.Renderer>.GetEnumerator() => GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Enumerates a snapshot of the count when it starts; renderers added meanwhile
        /// are left for the next pass, removed ones are skipped (the set is not compacted while
        /// it is enumerated). A struct, so the per-frame loops over every renderer allocate
        /// nothing.</summary>
        internal struct Enumerator : IEnumerator<UnityEngine.Renderer>
        {
            readonly OrderedRenderers m_Set;
            int m_Count, m_Next;
            bool m_Started, m_Done;
            UnityEngine.Renderer m_Current;

            internal Enumerator(OrderedRenderers set)
            {
                m_Set = set;
                m_Count = m_Next = 0;
                m_Started = m_Done = false;
                m_Current = null;
            }

            public UnityEngine.Renderer Current => m_Current;

            object System.Collections.IEnumerator.Current => m_Current;

            public bool MoveNext()
            {
                if (m_Done) return false;
                var items = m_Set.m_Items;
                if (!m_Started)
                {
                    m_Started = true;
                    m_Count = items.Count;
                    m_Set.m_Enumerating++;
                }
                while (m_Next < m_Count && m_Next < items.Count)
                {
                    var r = items[m_Next++];
                    if (r is not null)
                    {
                        m_Current = r;
                        return true;
                    }
                }
                Dispose();
                return false;
            }

            public void Dispose()
            {
                if (m_Started && !m_Done) m_Set.m_Enumerating--;
                m_Done = true;
                m_Current = null;
            }

            public void Reset() => throw new NotSupportedException();
        }
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
