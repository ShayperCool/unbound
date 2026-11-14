using System;
using System.Collections.Generic;
using Unity.Collections;
using Unbound.Runtime;

namespace UnityEngine.Rendering
{
    /// <summary>A renderer's state the GPU-driven code batches by, packed: receive shadows (bit 0),
    /// static shadow caster (1), LOD mask (2-9), shadow casting mode (10-11), light probe usage
    /// (12-14), motion vector mode (15-16), static batch (17), moved this frame (18), tree (19),
    /// small mesh culling (20), indirect drawing (21).</summary>
    internal partial struct GPUDrivenPackedRendererData
    {
        uint data;

        const int LodMaskShift = 2, ShadowShift = 10, ProbeShift = 12, MotionShift = 15;

        public GPUDrivenPackedRendererData()
        {
            data = 0;
        }

        public GPUDrivenPackedRendererData(bool receiveShadows, bool staticShadowCaster, byte lodMask, ShadowCastingMode shadowCastingMode, LightProbeUsage lightProbeUsage,
            MotionVectorGenerationMode motionVecGenMode, bool isPartOfStaticBatch, bool movedCurrentFrame, bool hasTree, bool smallMeshCulling, bool supportsIndirect)
        {
            data = (receiveShadows ? 1u : 0u)
                | (staticShadowCaster ? 1u << 1 : 0u)
                | (uint)lodMask << LodMaskShift
                | ((uint)shadowCastingMode & 3u) << ShadowShift
                | ((uint)lightProbeUsage & 7u) << ProbeShift
                | ((uint)motionVecGenMode & 3u) << MotionShift
                | (isPartOfStaticBatch ? 1u << 17 : 0u)
                | (movedCurrentFrame ? 1u << 18 : 0u)
                | (hasTree ? 1u << 19 : 0u)
                | (smallMeshCulling ? 1u << 20 : 0u)
                | (supportsIndirect ? 1u << 21 : 0u);
        }

        public bool receiveShadows => (data & 1u) != 0;
        public bool staticShadowCaster => (data & (1u << 1)) != 0;
        public byte lodMask => (byte)(data >> LodMaskShift);
        public ShadowCastingMode shadowCastingMode => (ShadowCastingMode)((data >> ShadowShift) & 3u);
        public LightProbeUsage lightProbeUsage => (LightProbeUsage)((data >> ProbeShift) & 7u);
        public MotionVectorGenerationMode motionVecGenMode => (MotionVectorGenerationMode)((data >> MotionShift) & 3u);
        public bool isPartOfStaticBatch => (data & (1u << 17)) != 0;
        public bool movedCurrentFrame => (data & (1u << 18)) != 0;
        public bool hasTree => (data & (1u << 19)) != 0;
        public bool smallMeshCulling => (data & (1u << 20)) != 0;
        public bool supportsIndirect => (data & (1u << 21)) != 0;
    }

    /// <summary>A material's state the GPU-driven code batches by, packed: transparent (its render
    /// queue past 2500, bit 0), a motion vector pass enabled (1), indirect drawing (2).</summary>
    internal partial struct GPUDrivenPackedMaterialData
    {
        uint data;

        public GPUDrivenPackedMaterialData()
        {
            data = 0;
        }

        public GPUDrivenPackedMaterialData(bool isTransparent, bool isMotionVectorsPassEnabled, bool isIndirectSupported)
        {
            data = (isTransparent ? 1u : 0u) | (isMotionVectorsPassEnabled ? 2u : 0u) | (isIndirectSupported ? 4u : 0u);
        }

        public bool isTransparent => (data & 1u) != 0;
        public bool isMotionVectorsPassEnabled => (data & 2u) != 0;
        public bool isIndirectSupported => (data & 4u) != 0;

        public bool Equals(GPUDrivenPackedMaterialData other) => data == other.data;
    }

    internal partial struct GPUDrivenRendererEditorData
    {
        public ulong sceneCullingMask;
    }

    internal delegate void GPUDrivenRendererDataCallback(in GPUDrivenRendererGroupData rendererData, IList<Mesh> meshes, IList<Material> materials);

    internal delegate void GPUDrivenLODGroupDataCallback(in GPUDrivenLODGroupData lodGroupData);

    internal enum GPUDrivenBitOpType
    {
        And = 0,
        Or = 1,
    }

    internal partial struct GPUDrivenMaterialFilterEntry
    {
        public GPUDrivenBitOpType op;
        public int minQueueValue;
        public int maxQueueValue;
        public ShaderTagId keyTag;
        public ShaderTagId valueTag;
        public int flags;
        public string keyword;
    }

    /// <summary>The renderers a GPUDrivenProcessor dispatch passes (and the ones it refused), with
    /// their meshes and materials, in the arrays the GPU-driven code reads.</summary>
    internal partial struct GPUDrivenRendererGroupData
    {
        public NativeArray<int> rendererGroupID;
        public NativeArray<Bounds> localBounds;
        public NativeArray<Vector4> lightmapScaleOffset;
        public NativeArray<int> gameObjectLayer;
        public NativeArray<uint> renderingLayerMask;
        public NativeArray<int> lodGroupID;
        public NativeArray<int> lightmapIndex;
        public NativeArray<GPUDrivenPackedRendererData> packedRendererData;
        public NativeArray<int> rendererPriority;
        public NativeArray<int> meshIndex;
        public NativeArray<short> subMeshStartIndex;
        public NativeArray<int> materialsOffset;
        public NativeArray<short> materialsCount;
        public NativeArray<int> instancesOffset;
        public NativeArray<int> instancesCount;
        public NativeArray<GPUDrivenRendererEditorData> editorData;
        public NativeArray<int> invalidRendererGroupID;
        public NativeArray<Matrix4x4> localToWorldMatrix;
        public NativeArray<Matrix4x4> prevLocalToWorldMatrix;
        public NativeArray<int> rendererGroupIndex;
        public NativeArray<int> meshID;
        public NativeArray<short> subMeshCount;
        public NativeArray<int> subMeshDescOffset;
        public NativeArray<SubMeshDescriptor> subMeshDesc;
        public NativeArray<int> materialIndex;
        public NativeArray<int> materialID;
        public NativeArray<GPUDrivenPackedMaterialData> packedMaterialData;
        public NativeArray<int> materialFilterFlags;
    }

    /// <summary>The LOD groups a dispatch passes (and the ones it refused), with their levels.</summary>
    internal partial struct GPUDrivenLODGroupData
    {
        public NativeArray<int> lodGroupID;
        public NativeArray<int> lodOffset;
        public NativeArray<int> lodCount;
        public NativeArray<LODFadeMode> fadeMode;
        public NativeArray<Vector3> worldSpaceReferencePoint;
        public NativeArray<float> worldSpaceSize;
        public NativeArray<short> renderersCount;
        public NativeArray<bool> lastLODIsBillboard;
        public NativeArray<byte> forceLODMask;
        public NativeArray<int> invalidLODGroupID;
        public NativeArray<short> lodRenderersCount;
        public NativeArray<float> lodScreenRelativeTransitionHeight;
        public NativeArray<float> lodFadeTransitionWidth;
    }

    /// <summary>
    /// The engine side of GPU-driven rendering (the render pipelines' GPU Resident Drawer): it
    /// decides which MeshRenderers GPU-driven rendering takes, hides those from the ordinary
    /// cullings, and hands their data to the caller. A MeshRenderer is taken when it is enabled in
    /// an active hierarchy and has a mesh, GPU-driven rendering is allowed on it, its visibility
    /// drives no Animator or Animation culling, it uses no light probe proxy volume, realtime GI
    /// vertex stream, probe anchor with blended probes, property block, sorting layer or order
    /// other than the default or sorting group, no script of its GameObject receives
    /// OnWillRenderObject, OnBecameVisible or OnBecameInvisible, it has no TextMesh, is not forced
    /// off, is not lightmapped (Unbound's own refusal until BatchRendererGroup draws lightmaps), its
    /// LOD group (if any) does not animate cross-fading and every material's shader has a DOTS
    /// instancing variant (a pass declaring <c>DOTS_INSTANCING_ON</c>). Refused renderers are
    /// drawn as usual. Matches the Unity 6000.0.71f1 Editor, tests/projects/residentdrawer (the
    /// sorting layer, sorting group and OnBecameInvisible rules in its controls profile), which
    /// has a renderer for each rule but the TextMesh (which Unity does not add beside a MeshFilter)
    /// and realtime GI ones: an
    /// OnRenderObject script, additional vertex streams, an Animation without clips and more than
    /// 128 materials (with the Editor's error) do not refuse a renderer.
    /// </summary>
    internal partial class GPUDrivenProcessor
    {
        internal IntPtr m_Ptr;
        bool m_PartialRendering;
        bool m_MaterialFilters;
        List<Mesh> m_ScratchMeshes = new List<Mesh>();
        List<Material> m_ScratchMaterials = new List<Material>();
        readonly RendererScratch m_Scratch = new RendererScratch();

        /// <summary>The most materials (sub-meshes) a renderer GPU-driven rendering takes.</summary>
        const int MaxMaterials = 128;

        public GPUDrivenProcessor()
        {
            m_Ptr = (IntPtr)1;
        }

        public void Dispose()
        {
            m_Ptr = IntPtr.Zero;
            m_Scratch.Clear();
        }

        internal List<Mesh> scratchMeshes { get => m_ScratchMeshes; private set => m_ScratchMeshes = value; }
        internal List<Material> scratchMaterials { get => m_ScratchMaterials; private set => m_ScratchMaterials = value; }

        /// <summary>Kept: with partial rendering a renderer whose materials are not all supported is
        /// split between the two paths; without it (the drawer's use) such a renderer is refused.</summary>
        public bool enablePartialRendering
        {
            get => m_PartialRendering;
            set => m_PartialRendering = value;
        }

        public bool enableMaterialFilters
        {
            get => m_MaterialFilters;
            set => m_MaterialFilters = value;
        }

        public void AddMaterialFilters(GPUDrivenMaterialFilterEntry[] filters)
        {
            if (filters != null && filters.Length > 0)
                Stubs.Ignored("GPUDrivenProcessor.AddMaterialFilters", "is not supported yet: materials are not filtered");
        }

        [Unbound.EditorFaithful("AddMaterialFilters keeps no filters (it is warned and ignored), so there are none to clear")]
        public void ClearMaterialFilters() { }

        [Unbound.EditorFaithful("AddMaterialFilters keeps no filters (it is warned and ignored), so no material has filter flags")]
        public int GetMaterialFilterFlags(Material material) => 0;

        public void EnableGPUDrivenRenderingAndDispatchRendererData(ReadOnlySpan<int> renderersID, GPUDrivenRendererDataCallback callback, bool materialUpdateOnly = false)
        {
            if (callback == null || renderersID.Length == 0) return;
            var scratch = m_Scratch;
            scratch.Clear();
            foreach (int id in renderersID)
            {
                if (ObjectRegistry.FindById(id) is not MeshRenderer renderer) continue;
                if (!Eligible(renderer))
                {
                    Refuse(renderer, scratch);
                    continue;
                }
                scratch.Candidates.Add(renderer);
                foreach (var material in renderer.m_Materials) scratch.Materials.Add(material);
            }
            foreach (var material in scratch.Materials)
                if (!Supported(material)) scratch.Invalid.Add(material);
            foreach (var renderer in scratch.Candidates)
            {
                if (TakesAllMaterials(renderer, scratch.Invalid))
                {
                    renderer.m_GpuDriven = true;
                    scratch.Valid.Add(renderer);
                }
                else
                    Refuse(renderer, scratch);
            }
            Dispatch(scratch, callback, materialUpdateOnly);
            scratch.Clear();
        }

        static void Refuse(MeshRenderer renderer, RendererScratch scratch)
        {
            renderer.m_GpuDriven = false;
            scratch.Refused.Add(renderer.m_InstanceID);
        }

        /// <summary>A renderer's materials all supported. With more than <see cref="MaxMaterials"/>
        /// the Editor's error is logged and the materials are not checked: the renderer is taken
        /// all the same, though the message says otherwise (measured: tests/projects/residentdrawer,
        /// ManyMaterials).</summary>
        static bool TakesAllMaterials(MeshRenderer renderer, HashSet<Material> invalid)
        {
            var materials = renderer.m_Materials;
            if (materials.Length > MaxMaterials)
            {
                Debug.LogError($"The number of sub-meshes on the MeshRenderer \"{renderer.name}\" is {materials.Length}. GPU Resident Drawer does not support more than {MaxMaterials} sub-meshes. The normal rendering path will be used for this MeshRenderer instead.");
                return true;
            }
            if (invalid.Count == 0) return true;
            foreach (var material in materials)
                if (invalid.Contains(material)) return false;
            return true;
        }

        /// <summary>Whether GPU-driven rendering may take a renderer, before its materials.</summary>
        internal static bool Eligible(MeshRenderer renderer)
        {
            var go = renderer.m_GameObject;
            if (go == null || !go.activeInHierarchy || !renderer.m_Enabled) return false;
            if (renderer.GetComponent<MeshFilter>() is not MeshFilter filter || filter.sharedMesh == null) return false;
            if (!renderer.allowGPUDrivenRendering || AnimationCullsByVisibility(renderer)) return false;
            var probes = renderer.lightProbeUsage;
            // Realtime GI's own vertex stream refuses a renderer; additional vertex streams do not
            // (measured: tests/projects/residentdrawer, VertexStreams and StreamsDrawn are taken).
            if (probes == LightProbeUsage.UseProxyVolume || renderer.enlightenVertexStream != null) return false;
            if (probes == LightProbeUsage.BlendProbes && renderer.probeAnchor != null) return false;
            if (renderer.HasPropertyBlock()) return false;
            if (ReceivesVisibilityMessages(go)) return false;
            if (renderer.forceRenderingOff || go.GetComponent<TextMesh>() != null) return false;
            // Unbound's own refusals, each warned once: BatchRendererGroup draws no lightmaps yet
            // (the Editor's drawer takes lightmapped renderers), and the drawer supports static
            // global illumination only (the Manual's requirements; whether the Editor's drawer
            // refuses realtime GI renderers is not measured): such renderers keep their lightmaps on
            // the ordinary path.
            if (Lightmapped(renderer.lightmapIndex)) return RefusedByUnbound(renderer, RefusedLightmapped);
            if (Lightmapped(renderer.realtimeLightmapIndex)) return RefusedByUnbound(renderer, RefusedRealtimeGI);
            if (SortingLayer.GetLayerValueFromID(renderer.m_SortingLayerID) != 0 || renderer.m_SortingOrder != 0 || SortingGroup.Enclosing(renderer) != null) return false;
            var group = renderer.m_LODGroup;
            return group is null || !group || !group.animateCrossFading;
        }

        const int RefusedLightmapped = 0, RefusedRealtimeGI = 1;

        static readonly string[] s_Refusals =
        {
            "GPU-driven rendering does not take lightmapped renderers yet (BatchRendererGroup draws no lightmaps)",
            "GPU-driven rendering leaves renderers with realtime GI lightmaps to the normal rendering path (the GPU Resident Drawer supports static global illumination only)",
        };

        /// <summary>The refusals warned about so far, one bit each.</summary>
        internal static uint s_WarnedRefusals;

        static bool RefusedByUnbound(Renderer renderer, int refusal)
        {
            if ((s_WarnedRefusals & (1u << refusal)) == 0)
            {
                s_WarnedRefusals |= 1u << refusal;
                Debug.LogWarning($"Unbound: {s_Refusals[refusal]}; \"{renderer.name}\" and the others are drawn by the normal rendering path.");
            }
            return false;
        }

        /// <summary>Whether a lightmap index (16 bits) names a lightmap: below 0xFFFE, which marks
        /// a renderer that only influences lightmaps, and 0xFFFF, none (the render pipelines'
        /// GPU-driven batcher reads baked indices so).</summary>
        static bool Lightmapped(int index) => (index & 0xFFFF) < 0xFFFE;

        /// <summary>Whether an Animator or Animation of the renderer's hierarchy culls by its
        /// renderers' visibility (an Animator not always animating, an Animation culling based on
        /// renderers): the nearest one up the hierarchy decides.</summary>
        static bool AnimationCullsByVisibility(Renderer renderer)
        {
            for (var t = renderer.transform; t != null; t = t.parent)
            {
                var go = t.gameObject;
                if (go.GetComponent<Animator>() is Animator animator) return animator.cullingMode != AnimatorCullingMode.AlwaysAnimate;
                // An Animation culls by its renderers only with clips to play (measured: one
                // without clips does not keep its renderer from GPU-driven rendering).
                if (go.GetComponent<Animation>() is Animation animation) return animation.cullingType == AnimationCullingType.BasedOnRenderers && animation.GetClipCount() > 0;
            }
            return false;
        }

        /// <summary>Whether a script of the GameObject receives one of the messages the ordinary
        /// culling sends to visible renderers' objects (OnWillRenderObject, OnBecameVisible,
        /// OnBecameInvisible).</summary>
        static bool ReceivesVisibilityMessages(GameObject go)
        {
            foreach (var component in go.m_Components)
            {
                if (component is not MonoBehaviour behaviour) continue;
                var messages = ScriptMessages.For(behaviour.GetType());
                if (messages.OnWillRenderObject != null || messages.OnBecameVisible != null || messages.OnBecameInvisible != null) return true;
            }
            return false;
        }

        /// <summary>Whether a material's shader has a DOTS instancing variant: a pass of it that
        /// declares <c>DOTS_INSTANCING_ON</c>. A shader whose passes are not known is not.</summary>
        internal static bool Supported(Material material)
        {
            if (material == null || material.shader is not Shader shader) return false;
            var declarations = ShaderDeclarations.Of(shader);
            if (!declarations.SubShadersKnown) return false;
            foreach (var sub in declarations.SubShaders)
                foreach (var sets in sub.KeywordSets)
                {
                    if (sets == null) continue;
                    foreach (var set in sets)
                        if (Array.IndexOf(set, DotsInstancingKeyword) >= 0) return true;
                }
            return false;
        }

        const string DotsInstancingKeyword = "DOTS_INSTANCING_ON";
        /// <summary>The render queue past which a material is transparent.</summary>
        const int LastOpaqueQueue = 2500;

        internal static GPUDrivenPackedMaterialData Packed(Material material) =>
            new GPUDrivenPackedMaterialData(material.renderQueue > LastOpaqueQueue, material.GetShaderPassEnabled("MotionVectors"), true);

        public void DisableGPUDrivenRendering(ReadOnlySpan<int> renderersID)
        {
            foreach (int id in renderersID)
                if (ObjectRegistry.FindById(id) is MeshRenderer renderer) renderer.m_GpuDriven = false;
        }

        /// <summary>The supported materials (their packed data) and the unsupported ones; returns the
        /// number of unsupported ones.</summary>
        public static int ClassifyMaterials(NativeArray<int> materialIDs, NativeArray<int> unsupportedMaterialIDs, NativeArray<int> supportedMaterialIDs, NativeArray<GPUDrivenPackedMaterialData> supportedPackedMaterialDatas)
        {
            int unsupported = 0, supported = 0;
            for (int i = 0; i < materialIDs.Length; i++)
            {
                int id = materialIDs[i];
                var material = ObjectRegistry.FindById(id) as Material;
                if (!Supported(material))
                {
                    unsupportedMaterialIDs[unsupported++] = id;
                    continue;
                }
                supportedMaterialIDs[supported] = id;
                supportedPackedMaterialDatas[supported++] = Packed(material);
            }
            return unsupported;
        }

        void Dispatch(RendererScratch scratch, GPUDrivenRendererDataCallback callback, bool materialUpdateOnly)
        {
            var valid = scratch.Valid;
            int count = valid.Count;
            var data = new GPUDrivenRendererGroupData
            {
                rendererGroupID = new NativeArray<int>(count, Allocator.TempJob),
                lightmapScaleOffset = new NativeArray<Vector4>(count, Allocator.TempJob),
                gameObjectLayer = new NativeArray<int>(count, Allocator.TempJob),
                renderingLayerMask = new NativeArray<uint>(count, Allocator.TempJob),
                lightmapIndex = new NativeArray<int>(count, Allocator.TempJob),
                packedRendererData = new NativeArray<GPUDrivenPackedRendererData>(count, Allocator.TempJob),
                rendererPriority = new NativeArray<int>(count, Allocator.TempJob),
                meshIndex = new NativeArray<int>(count, Allocator.TempJob),
                subMeshStartIndex = new NativeArray<short>(count, Allocator.TempJob),
                materialsOffset = new NativeArray<int>(count, Allocator.TempJob),
                materialsCount = new NativeArray<short>(count, Allocator.TempJob),
                instancesOffset = new NativeArray<int>(0, Allocator.TempJob),
                instancesCount = new NativeArray<int>(0, Allocator.TempJob),
                rendererGroupIndex = new NativeArray<int>(0, Allocator.TempJob),
                editorData = new NativeArray<GPUDrivenRendererEditorData>(count, Allocator.TempJob),
                invalidRendererGroupID = new NativeArray<int>(scratch.Refused.Count, Allocator.TempJob),
                localBounds = new NativeArray<Bounds>(materialUpdateOnly ? 0 : count, Allocator.TempJob),
                lodGroupID = new NativeArray<int>(materialUpdateOnly ? 0 : count, Allocator.TempJob),
                localToWorldMatrix = new NativeArray<Matrix4x4>(materialUpdateOnly ? 0 : count, Allocator.TempJob),
                prevLocalToWorldMatrix = new NativeArray<Matrix4x4>(materialUpdateOnly ? 0 : count, Allocator.TempJob),
            };
            for (int i = 0; i < scratch.Refused.Count; i++) data.invalidRendererGroupID[i] = scratch.Refused[i];

            var meshes = scratch.MeshIndex;
            var materials = scratch.MaterialIndex;
            m_ScratchMeshes.Clear();
            m_ScratchMaterials.Clear();
            scratch.MaterialSlots.Clear();
            for (int i = 0; i < count; i++)
            {
                var renderer = valid[i];
                var go = renderer.m_GameObject;
                data.rendererGroupID[i] = renderer.m_InstanceID;
                data.lightmapScaleOffset[i] = renderer.lightmapScaleOffset;
                data.gameObjectLayer[i] = go.layer;
                data.renderingLayerMask[i] = renderer.renderingLayerMask;
                data.lightmapIndex[i] = renderer.lightmapIndex & 0xFFFF;
                data.rendererPriority[i] = renderer.rendererPriority;
                data.subMeshStartIndex[i] = (short)renderer.subMeshStartIndex;
                data.editorData[i] = new GPUDrivenRendererEditorData { sceneCullingMask = go.sceneCullingMask };
                var model = renderer.transform.localToWorldMatrix;
                bool moved = renderer.motionVectorGenerationMode == MotionVectorGenerationMode.Object && renderer.m_HasPreviousModel && renderer.m_PreviousModel != model;
                data.packedRendererData[i] = new GPUDrivenPackedRendererData(renderer.receiveShadows, renderer.staticShadowCaster, LodMask(renderer), renderer.shadowCastingMode,
                    renderer.lightProbeUsage, renderer.motionVectorGenerationMode, renderer.isPartOfStaticBatch, moved, false, renderer.smallMeshCulling, false);
                if (!materialUpdateOnly)
                {
                    data.localBounds[i] = renderer.localBounds;
                    var group = renderer.m_LODGroup;
                    data.lodGroupID[i] = group is null || !group ? 0 : group.m_InstanceID;
                    data.localToWorldMatrix[i] = model;
                    data.prevLocalToWorldMatrix[i] = moved ? renderer.m_PreviousModel : model;
                }
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (!meshes.TryGetValue(mesh, out int meshAt))
                {
                    meshAt = m_ScratchMeshes.Count;
                    meshes[mesh] = meshAt;
                    m_ScratchMeshes.Add(mesh);
                }
                data.meshIndex[i] = meshAt;
                data.materialsOffset[i] = scratch.MaterialSlots.Count;
                data.materialsCount[i] = (short)renderer.m_Materials.Length;
                foreach (var material in renderer.m_Materials)
                {
                    if (!materials.TryGetValue(material, out int materialAt))
                    {
                        materialAt = m_ScratchMaterials.Count;
                        materials[material] = materialAt;
                        m_ScratchMaterials.Add(material);
                    }
                    scratch.MaterialSlots.Add(materialAt);
                }
            }

            int subMeshes = 0;
            foreach (var mesh in m_ScratchMeshes) subMeshes += mesh.subMeshCount;
            data.meshID = new NativeArray<int>(m_ScratchMeshes.Count, Allocator.TempJob);
            data.subMeshCount = new NativeArray<short>(m_ScratchMeshes.Count, Allocator.TempJob);
            data.subMeshDescOffset = new NativeArray<int>(m_ScratchMeshes.Count, Allocator.TempJob);
            data.subMeshDesc = new NativeArray<SubMeshDescriptor>(subMeshes, Allocator.TempJob);
            subMeshes = 0;
            for (int m = 0; m < m_ScratchMeshes.Count; m++)
            {
                var mesh = m_ScratchMeshes[m];
                data.meshID[m] = mesh.m_InstanceID;
                data.subMeshCount[m] = (short)mesh.subMeshCount;
                data.subMeshDescOffset[m] = subMeshes;
                for (int s = 0; s < mesh.subMeshCount; s++) data.subMeshDesc[subMeshes++] = mesh.GetSubMesh(s);
            }
            data.materialIndex = new NativeArray<int>(scratch.MaterialSlots.Count, Allocator.TempJob);
            for (int i = 0; i < scratch.MaterialSlots.Count; i++) data.materialIndex[i] = scratch.MaterialSlots[i];
            data.materialID = new NativeArray<int>(m_ScratchMaterials.Count, Allocator.TempJob);
            data.packedMaterialData = new NativeArray<GPUDrivenPackedMaterialData>(m_ScratchMaterials.Count, Allocator.TempJob);
            data.materialFilterFlags = new NativeArray<int>(m_ScratchMaterials.Count, Allocator.TempJob);
            for (int m = 0; m < m_ScratchMaterials.Count; m++)
            {
                var material = m_ScratchMaterials[m];
                data.materialID[m] = material != null ? material.m_InstanceID : 0;
                data.packedMaterialData[m] = material != null ? Packed(material) : default;
            }
            try
            {
                callback(in data, m_ScratchMeshes, m_ScratchMaterials);
            }
            finally
            {
                Dispose(ref data);
                m_ScratchMeshes.Clear();
                m_ScratchMaterials.Clear();
            }
        }

        static void Dispose(ref GPUDrivenRendererGroupData data)
        {
            data.rendererGroupID.Dispose(); data.localBounds.Dispose(); data.lightmapScaleOffset.Dispose(); data.gameObjectLayer.Dispose();
            data.renderingLayerMask.Dispose(); data.lodGroupID.Dispose(); data.lightmapIndex.Dispose(); data.packedRendererData.Dispose();
            data.rendererPriority.Dispose(); data.meshIndex.Dispose(); data.subMeshStartIndex.Dispose(); data.materialsOffset.Dispose();
            data.materialsCount.Dispose(); data.instancesOffset.Dispose(); data.instancesCount.Dispose(); data.editorData.Dispose();
            data.invalidRendererGroupID.Dispose(); data.localToWorldMatrix.Dispose(); data.prevLocalToWorldMatrix.Dispose();
            data.rendererGroupIndex.Dispose(); data.meshID.Dispose(); data.subMeshCount.Dispose(); data.subMeshDescOffset.Dispose();
            data.subMeshDesc.Dispose(); data.materialIndex.Dispose(); data.materialID.Dispose(); data.packedMaterialData.Dispose();
            data.materialFilterFlags.Dispose();
        }

        /// <summary>The levels of the renderer's LOD group that list it, one bit per level.</summary>
        static byte LodMask(Renderer renderer)
        {
            var group = renderer.m_LODGroup;
            if (group is null || !group) return 0;
            return group.LevelMask(renderer);
        }

        /// <summary>
        /// The LOD groups' data: a group is passed when it is enabled in an active hierarchy, has
        /// levels and does not animate cross-fading; its world reference point is its local one
        /// placed by its transform, its world size its size times its largest absolute scale.
        /// Others go to the invalid IDs.
        /// </summary>
        public void DispatchLODGroupData(ReadOnlySpan<int> lodGroupID, GPUDrivenLODGroupDataCallback callback)
        {
            if (callback == null || lodGroupID.Length == 0) return;
            var groups = m_Scratch.Groups;
            var invalid = m_Scratch.Refused;
            groups.Clear();
            invalid.Clear();
            int levels = 0;
            foreach (int id in lodGroupID)
            {
                if (ObjectRegistry.FindById(id) is not LODGroup group) continue;
                if (!group.gameObject.activeInHierarchy || !group.enabled || group.lodCount == 0 || group.animateCrossFading)
                {
                    invalid.Add(id);
                    continue;
                }
                groups.Add(group);
                levels += group.lodCount;
            }
            int count = groups.Count;
            var data = new GPUDrivenLODGroupData
            {
                lodGroupID = new NativeArray<int>(count, Allocator.TempJob),
                lodOffset = new NativeArray<int>(count, Allocator.TempJob),
                lodCount = new NativeArray<int>(count, Allocator.TempJob),
                fadeMode = new NativeArray<LODFadeMode>(count, Allocator.TempJob),
                worldSpaceReferencePoint = new NativeArray<Vector3>(count, Allocator.TempJob),
                worldSpaceSize = new NativeArray<float>(count, Allocator.TempJob),
                renderersCount = new NativeArray<short>(count, Allocator.TempJob),
                lastLODIsBillboard = new NativeArray<bool>(count, Allocator.TempJob),
                forceLODMask = new NativeArray<byte>(count, Allocator.TempJob),
                invalidLODGroupID = new NativeArray<int>(invalid.Count, Allocator.TempJob),
                lodRenderersCount = new NativeArray<short>(levels, Allocator.TempJob),
                lodScreenRelativeTransitionHeight = new NativeArray<float>(levels, Allocator.TempJob),
                lodFadeTransitionWidth = new NativeArray<float>(levels, Allocator.TempJob),
            };
            for (int i = 0; i < invalid.Count; i++) data.invalidLODGroupID[i] = invalid[i];
            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                var group = groups[i];
                data.lodGroupID[i] = group.m_InstanceID;
                data.lodOffset[i] = offset;
                data.lodCount[i] = group.lodCount;
                data.fadeMode[i] = group.fadeMode;
                data.worldSpaceReferencePoint[i] = group.worldReferencePoint;
                data.worldSpaceSize[i] = group.WorldSize;
                data.lastLODIsBillboard[i] = group.lastLODBillboard;
                data.forceLODMask[i] = group.ForcedMask;
                int renderers = 0;
                foreach (var lod in group.m_LODs)
                {
                    int levelRenderers = lod.renderers?.Length ?? 0;
                    renderers += levelRenderers;
                    data.lodRenderersCount[offset] = (short)levelRenderers;
                    data.lodScreenRelativeTransitionHeight[offset] = lod.screenRelativeTransitionHeight;
                    data.lodFadeTransitionWidth[offset] = lod.fadeTransitionWidth;
                    offset++;
                }
                data.renderersCount[i] = (short)renderers;
            }
            try
            {
                callback(in data);
            }
            finally
            {
                data.lodGroupID.Dispose(); data.lodOffset.Dispose(); data.lodCount.Dispose(); data.fadeMode.Dispose();
                data.worldSpaceReferencePoint.Dispose(); data.worldSpaceSize.Dispose(); data.renderersCount.Dispose();
                data.lastLODIsBillboard.Dispose(); data.forceLODMask.Dispose(); data.invalidLODGroupID.Dispose();
                data.lodRenderersCount.Dispose(); data.lodScreenRelativeTransitionHeight.Dispose(); data.lodFadeTransitionWidth.Dispose();
                groups.Clear();
                invalid.Clear();
            }
        }

        /// <summary>One dispatch's working sets, kept between dispatches (their capacity).</summary>
        sealed class RendererScratch
        {
            internal readonly List<MeshRenderer> Candidates = new List<MeshRenderer>();
            internal readonly List<MeshRenderer> Valid = new List<MeshRenderer>();
            internal readonly List<int> Refused = new List<int>();
            internal readonly HashSet<Material> Materials = new HashSet<Material>();
            internal readonly HashSet<Material> Invalid = new HashSet<Material>();
            internal readonly Dictionary<Mesh, int> MeshIndex = new Dictionary<Mesh, int>();
            internal readonly Dictionary<Material, int> MaterialIndex = new Dictionary<Material, int>(ReferenceComparer.Instance);
            internal readonly List<int> MaterialSlots = new List<int>();
            internal readonly List<LODGroup> Groups = new List<LODGroup>();

            internal void Clear()
            {
                Candidates.Clear();
                Valid.Clear();
                Refused.Clear();
                Materials.Clear();
                Invalid.Clear();
                MeshIndex.Clear();
                MaterialIndex.Clear();
                MaterialSlots.Clear();
            }
        }

        /// <summary>Materials by reference (a null slot is a key of its own).</summary>
        sealed class ReferenceComparer : IEqualityComparer<Material>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(Material a, Material b) => ReferenceEquals(a, b);
            public int GetHashCode(Material m) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m);
        }
    }
}
