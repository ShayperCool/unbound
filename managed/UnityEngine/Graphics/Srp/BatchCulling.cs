using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// BatchRendererGroup cullings and draws. Every scriptable culling invokes the culling callback
    /// of each live group whose enabled view types include the culling's (in creation order), with
    /// the culling's planes, a split holding all of them, its LOD parameters and masks, completes the
    /// job it returns and keeps the draw commands it wrote until the frame ends, when the arrays
    /// it allocated are freed (the Manual: "Unity releases it after it finishes rendering with the
    /// draw commands"). The culling's renderer lists then draw the commands of its outputs with the
    /// lists' filters: each command draws its visible instances with its material's DOTS
    /// instancing variant (<c>DOTS_INSTANCING_ON</c>), at most <see cref="MaxInstancesPerDraw"/>
    /// instances per draw. Matches the Unity 6000.0.71f1 Editor on its OpenGL Core and Vulkan
    /// devices, tests/projects/brg.
    /// </summary>
    internal static unsafe class BatchCulling
    {
        /// <summary>BatchRendererGroup.GetConstantBufferMaxWindowSize on both devices (measured:
        /// tests/projects/urp CapsProbe, tests/projects/brg): the 1024 float4s of
        /// <c>unity_DOTSInstanceDataRaw</c> that DOTS instancing shaders declare.</summary>
        internal const int MaxWindowSize = 16384;

        /// <summary>The instances one draw takes: the length of <c>unity_DOTSVisibleInstances</c>
        /// in DOTS instancing shaders (render_commands.rs <c>MAX_DOTS_INSTANCES</c>).</summary>
        internal const int MaxInstancesPerDraw = 256;

        /// <summary>The scene culling mask of the Editor's Game view objects
        /// (<c>SceneCullingMasks.GameViewObjects</c>), which a game camera's cullings pass
        /// (measured: tests/projects/brg <c>sceneCullingMask</c>).</summary>
        const ulong GameViewSceneCullingMask = 1UL << 63;

        internal static readonly int InstanceDataId = Shader.PropertyToID("unity_DOTSInstanceData");

        /// <summary>The prefix of the draw property that carries a batch's metadata value for a
        /// property name; the renderer writes it into the program's
        /// <c>unity_DOTSInstancing&lt;type&gt;_Metadata&lt;name&gt;</c> uniform (a reserved name: no
        /// shader property can be called so).</summary>
        internal const string MetadataPrefix = "<DOTS>";

        static readonly Dictionary<int, int> s_MetadataIds = new Dictionary<int, int>();

        /// <summary>The draw property ID of a metadata value for the property <paramref name="nameId"/>.</summary>
        internal static int MetadataProperty(int nameId)
        {
            lock (s_MetadataIds)
            {
                if (!s_MetadataIds.TryGetValue(nameId, out int id))
                    s_MetadataIds[nameId] = id = Shader.PropertyToID(MetadataPrefix + Shader.IdToName(nameId));
                return id;
            }
        }

        /// <summary>One culling callback's output, kept until the frame ends.</summary>
        internal sealed class Output
        {
            internal BatchRendererGroup Group;
            internal BatchCullingViewType View;
            /// <summary>A light culling's light: its index among the culling's visible lights (-1
            /// for a camera culling).</summary>
            internal int Light = -1;
            /// <summary>The culling's layer mask: a camera culling's range on a layer it leaves out
            /// is not drawn.</summary>
            internal uint LayerMask;
            internal NativeArray<BatchCullingOutputDrawCommands> Commands = new NativeArray<BatchCullingOutputDrawCommands>(1, Allocator.Persistent);
            internal NativeArray<IntPtr> Custom = new NativeArray<IntPtr>(1, Allocator.Persistent);

            internal ref BatchCullingOutputDrawCommands Drawn => ref UnsafeUtility.ArrayElementAsRef<BatchCullingOutputDrawCommands>(Commands.GetUnsafePtr(), 0);

            /// <summary>Frees what the callback allocated and empties the output for reuse.</summary>
            internal void Release()
            {
                ref var d = ref Drawn;
                Free(d.drawCommands);
                Free(d.indirectDrawCommands);
                Free(d.proceduralDrawCommands);
                Free(d.proceduralIndirectDrawCommands);
                Free(d.visibleInstances);
                Free(d.drawRanges);
                Free(d.instanceSortingPositions);
                Free(d.drawCommandPickingInstanceIDs);
                d = default;
                Custom[0] = IntPtr.Zero;
                Group = null;
                Light = -1;
            }

            static void Free(void* memory)
            {
                if (memory != null) UnsafeUtility.Free(memory, Allocator.TempJob);
            }
        }

        /// <summary>A draw item's draw command: its output and index, and its header.</summary>
        internal struct DrawRef
        {
            internal Output Output;
            internal int Command;
            internal Header Header;
            /// <summary>An indirect draw command (of the output's indirect commands).</summary>
            internal bool Indirect;
        }

        /// <summary>The bit of an indirect command's packed visible offset that marks it indirect
        /// (core's UnityDOTSInstancing.hlsl <c>kIndirectVisibleOffsetEnabledBit</c>).</summary>
        const uint IndirectVisibleOffsetEnabled = 0x80000000;
        internal static readonly int IndirectVisibilityId = Shader.PropertyToID("unity_DOTSInstancing_IndirectInstanceVisibility");

        /// <summary>
        /// What DOTS instancing shaders read of a draw besides its instances (the z and w of the
        /// first visible instance elements: core's LoadDOTSInstancedData_RenderingLayer,
        /// _LightData, _WorldTransformParams, _MotionVectorsParams, _MeshLocalBound*): the range's
        /// rendering layer mask, the flags, and the mesh's bounds. The flags: the command's
        /// FlipWinding and LODCrossFadeValuePacked, camera motion for a range of
        /// MotionVectorGenerationMode.Camera, zero motion for ForceNoMotion, and the main light for
        /// the commands it lights: there is one, its culling mask has the range's layer and,
        /// the command lightmapped, its direct light is not in the lightmaps (the rule of
        /// unity_LightData.z: <see cref="FrameBuilder.InLightmap"/>). The probe
        /// (tests/projects/brg, matching the Unity 6000.0.71f1 Editor) covers the flip and the main
        /// light of an unlightmapped command at the default motion mode; the other motion modes
        /// (no motion vector pass is drawn) and lightmapped commands are not measured.
        /// </summary>
        internal struct Header
        {
            internal uint RenderingLayer;
            internal uint Flags;
            internal Vector3 Center;
            internal Vector3 Extent;
        }

        // The DOTS instancing flags (core's UnityDOTSInstancing.hlsl kDOTSInstancingFlag*).
        const uint FlagFlipWinding = 1, FlagForceZeroMotion = 2, FlagCameraMotion = 4, FlagMainLightEnabled = 16, FlagLodCrossFadeValuePacked = 32;

        static readonly Stack<Output> s_Free = new Stack<Output>();
        static NativeArray<Plane> s_Planes = new NativeArray<Plane>(ReceiverPlanes + 10, Allocator.Persistent);
        static NativeArray<CullingSplit> s_Splits = new NativeArray<CullingSplit>(1, Allocator.Persistent);

        /// <summary>The receiver planes of a light culling: the camera's frustum.</summary>
        const int ReceiverPlanes = 6;

        /// <summary>Room for <paramref name="planes"/> planes and <paramref name="splits"/> splits
        /// (the arrays are kept: they grow, never shrink).</summary>
        static void Reserve(int planes, int splits)
        {
            if (s_Planes.Length < planes)
            {
                s_Planes.Dispose();
                s_Planes = new NativeArray<Plane>(Math.Max(planes, s_Planes.Length * 2), Allocator.Persistent);
            }
            if (s_Splits.Length < splits)
            {
                s_Splits.Dispose();
                s_Splits = new NativeArray<CullingSplit>(Math.Max(splits, s_Splits.Length * 2), Allocator.Persistent);
            }
        }
        static readonly List<BatchRendererGroup> s_Invoked = new List<BatchRendererGroup>();

        /// <summary>A camera's culling: the callbacks of the groups that take camera views, with
        /// the culling's planes as its one split.</summary>
        internal static void CullCamera(CullingData data, ref ScriptableCullingParameters parameters, Camera camera)
        {
            if (BatchRendererGroup.s_Groups.Count == 0) return;
            int planes = parameters.cullingPlaneCount;
            Reserve(planes, 1);
            for (int i = 0; i < planes; i++) s_Planes[i] = parameters.GetCullingPlane(i);
            s_Splits[0] = new CullingSplit
            {
                cullingPlaneOffset = 0,
                cullingPlaneCount = planes,
                cascadeBlendCullingFactor = 1f,
                nearPlane = camera != null ? camera.nearClipPlane : 0f,
                cullingMatrix = parameters.cullingMatrix,
            };
            var context = new BatchCullingContext(s_Planes.GetSubArray(0, planes), s_Splits.GetSubArray(0, 1), parameters.lodParameters, Matrix4x4.identity,
                BatchCullingViewType.Camera, parameters.isOrthographic ? BatchCullingProjectionType.Orthographic : BatchCullingProjectionType.Perspective,
                BatchCullingFlags.None, new BatchPackedCullingViewID(camera != null ? camera.GetInstanceID() : 0, 0).handle, parameters.cullingMask,
                GameViewSceneCullingMask, 0, 0, 0, IntPtr.Zero);
            Invoke(data, context, -1);
        }

        /// <summary>
        /// A culling's shadow casters (ScriptableRenderContext.CullShadowCasters): for each of its
        /// visible lights with splits not all excluded, the callbacks of the groups that take light
        /// views, with every split's culling planes in order (each a CullingSplit with its sphere,
        /// planes, blend factor, near plane and matrix), then, for a light that culls by the view
        /// frustum (Light.useViewFrustumForShadowCasterCull), the camera's six frustum planes as
        /// receiver planes, the far one at the shadow distance; the light's matrix, the camera's
        /// LOD parameters, the camera's layers that the light lights, and the light as the view.
        /// Matches the Unity 6000.0.71f1 Editor, tests/projects/brg <c>shadows</c> (a directional
        /// light of two cascades).
        /// </summary>
        internal static void CullShadows(CullingData data, ShadowCastersCullingInfos infos)
        {
            if (data == null || !infos.perLightInfos.IsCreated || !AnyTakes(BatchCullingViewType.Light)) return;
            var parameters = data.Parameters;
            int lights = Math.Min(data.Lights.Length, infos.perLightInfos.Length);
            for (int index = 0; index < lights; index++)
            {
                var info = infos.perLightInfos[index];
                int splits = info.splitRange.length;
                if (splits <= 0 || info.splitRange.start < 0 || info.splitRange.start + splits > infos.splitBuffer.Length) continue;
                // Every split excluded (by the mask's low byte): no culling.
                uint all = splits >= 8 ? 0xffu : (1u << splits) - 1;
                if ((info.splitExclusionMask & 0xffu) == all) continue;
                var light = data.Lights[index].light;
                if (light == null) continue;
                // Measured for directional lights; point and spot lights take the same path.
                if (light.type != LightType.Directional) WarnOnce(UnsupportedLocalLightCulling);
                int planes = 0;
                for (int s = 0; s < splits; s++) planes += infos.splitBuffer[info.splitRange.start + s].cullingPlaneCount;
                Reserve(planes + ReceiverPlanes, splits);
                planes = 0;
                for (int s = 0; s < splits; s++)
                {
                    var split = infos.splitBuffer[info.splitRange.start + s];
                    var sphere = split.cullingSphere;
                    s_Splits[s] = new CullingSplit
                    {
                        sphereCenter = new Vector3(sphere.x, sphere.y, sphere.z),
                        sphereRadius = sphere.w,
                        cullingPlaneOffset = planes,
                        cullingPlaneCount = split.cullingPlaneCount,
                        cascadeBlendCullingFactor = split.shadowCascadeBlendCullingFactor,
                        nearPlane = split.cullingNearPlane,
                        cullingMatrix = split.cullingMatrix,
                    };
                    for (int p = 0; p < split.cullingPlaneCount; p++) s_Planes[planes++] = split.GetCullingPlane(p);
                }
                int receiverOffset = 0, receivers = 0;
                if (light.useViewFrustumForShadowCasterCull && parameters.cullingPlaneCount == ReceiverPlanes)
                {
                    receiverOffset = planes;
                    receivers = ReceiverPlanes;
                    for (int p = 0; p < ReceiverPlanes; p++) s_Planes[planes++] = parameters.GetCullingPlane(p);
                    // The far plane at the shadow distance along the camera's view (shadows fall no
                    // further).
                    if (data.Camera != null)
                    {
                        var view = data.Camera.transform;
                        var forward = view.forward;
                        float distance = Mathf.Min(parameters.shadowDistance, data.Camera.farClipPlane);
                        s_Planes[planes - 1] = new Plane(-forward, Vector3.Dot(forward, view.position) + distance);
                    }
                }
                var context = new BatchCullingContext(s_Planes.GetSubArray(0, planes), s_Splits.GetSubArray(0, splits), parameters.lodParameters,
                    light.transform.localToWorldMatrix, BatchCullingViewType.Light, info.projectionType,
                    ExcludesLightmappedCasters(light) ? BatchCullingFlags.CullLightmappedShadowCasters : BatchCullingFlags.None,
                    new BatchPackedCullingViewID(light.GetInstanceID(), 0).handle, parameters.cullingMask & (uint)light.cullingMask,
                    GameViewSceneCullingMask, (byte)info.splitExclusionMask, receiverOffset, receivers, IntPtr.Zero);
                Invoke(data, context, index);
            }
        }

        static bool AnyTakes(BatchCullingViewType view)
        {
            foreach (var group in BatchRendererGroup.s_Groups)
                if (group.Enabled(view)) return true;
            return false;
        }

        /// <summary>Whether a light's shadow culling leaves out lightmapped casters
        /// (BatchCullingFlags.CullLightmappedShadowCasters): its direct light is in the lightmaps
        /// (<see cref="FrameBuilder.InLightmap"/>), or it is a mixed light of a Shadowmask bake whose
        /// shadow caster mode leaves them out (NonLightmappedOnly, or Default in the quality's
        /// Shadowmask mode). Not measured: the probe's light is realtime.</summary>
        static bool ExcludesLightmappedCasters(Light light)
        {
            if (FrameBuilder.InLightmap(light)) return true;
            var baking = light.bakingOutput;
            if (!baking.isBaked || baking.lightmapBakeType != LightmapBakeType.Mixed || baking.mixedLightingMode != MixedLightingMode.Shadowmask) return false;
            return light.lightShadowCasterMode switch
            {
                LightShadowCasterMode.NonLightmappedOnly => true,
                LightShadowCasterMode.Everything => false,
                _ => QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask,
            };
        }

        /// <summary>Invokes the callbacks for a culling (the groups created by a callback wait for
        /// the next culling); a light culling's outputs keep its light.</summary>
        static void Invoke(CullingData data, in BatchCullingContext context, int light)
        {
            s_Invoked.Clear();
            s_Invoked.AddRange(BatchRendererGroup.s_Groups);
            foreach (var group in s_Invoked)
            {
                if (!group.Enabled(context.viewType) || !BatchRendererGroup.s_Groups.Contains(group)) continue;
                var output = s_Free.Count > 0 ? s_Free.Pop() : new Output();
                output.Group = group;
                output.View = context.viewType;
                output.Light = light;
                output.LayerMask = context.cullingLayerMask;
                data.Batches.Add(output);
                try
                {
                    var handle = group.m_Culling(group, context, new BatchCullingOutput { drawCommands = output.Commands, customCullingResult = output.Custom }, group.m_UserContext);
                    handle.Complete();
                    group.m_Finished?.Invoke(output.Custom[0]);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            s_Invoked.Clear();
        }

        /// <summary>Frees a culling's outputs (at the frame's end).</summary>
        internal static void Release(List<Output> outputs)
        {
            foreach (var output in outputs)
            {
                output.Release();
                s_Free.Push(output);
            }
            outputs.Clear();
        }

        static readonly Dictionary<(ShadowCastingMode, bool), ImmediateLighting> s_Lighting = new Dictionary<(ShadowCastingMode, bool), ImmediateLighting>();

        /// <summary>A draw range's lighting: its shadow settings, without light or reflection probes
        /// (a group provides its instances' lighting data itself).</summary>
        static ImmediateLighting LightingOf(in BatchFilterSettings filter)
        {
            var key = (filter.shadowCastingMode, filter.receiveShadows);
            if (!s_Lighting.TryGetValue(key, out var lighting))
                s_Lighting[key] = lighting = ImmediateLighting.Of(key.Item1, key.Item2, LightProbeUsage.Off, null, ReflectionProbeUsage.Off);
            return lighting;
        }

        /// <summary>The unsupported parts of draw commands warned about so far, one bit each
        /// (<see cref="WarnOnce"/>): checking costs no allocation once warned.</summary>
        internal static uint s_Warned;

        // Unsupported parts of draw commands (bits of s_Warned).
        const int UnsupportedIndirectTopology = 0, UnsupportedProcedural = 1, UnsupportedProceduralIndirect = 2, UnsupportedLightmap = 3,
            UnsupportedCrossFadeKeyword = 4, UnsupportedMissingArrays = 5, UnsupportedLocalLightCulling = 6;

        static readonly string[] s_Unsupported =
        {
            "BatchRendererGroup indirect draw commands of a topology other than triangles are not supported yet; they are not drawn.",
            "BatchRendererGroup procedural draw commands are not supported yet; they are not drawn.",
            "BatchRendererGroup procedural indirect draw commands are not supported yet; they are not drawn.",
            "BatchRendererGroup draw commands flagged IsLightMapped draw without their lightmaps (LIGHTMAP_ON and the lightmap of lightmapIndex are not bound yet).",
            "BatchRendererGroup draw commands flagged LODCrossFadeKeyword draw without LOD_FADE_CROSSFADE (not enabled yet).",
            "a BatchRendererGroup culling callback gave draw commands or visible instances without allocating their arrays; its output is not drawn.",
            "BatchRendererGroup light cullings of point and spot lights take a directional light's context (one call with every split, view slice 0); the Editor's is not measured.",
        };

        /// <summary>The invalid-ID errors logged so far: the Editor logs each distinct message once.</summary>
        internal static readonly HashSet<(long, long, int, uint, uint, uint)> s_InvalidIds = new HashSet<(long, long, int, uint, uint, uint)>();

        /// <summary>The Editor's error for a draw command whose batch, mesh or material is not
        /// registered (measured: tests/projects/brg errors, in the Editor's log; the command is
        /// not drawn). The pass is not known where the commands are read.</summary>
        static void LogInvalidId(long command, long inRange, int range, BatchMaterialID materialId, Material material, BatchMeshID meshId, Mesh mesh, BatchID batchId)
        {
            if (!s_InvalidIds.Add((command, inRange, range, materialId.value, meshId.value, batchId.value))) return;
            Debug.LogError("A BatchDrawCommand was submitted with an invalid Batch, Mesh, or Material ID. This is not supported when rendering with a BatchRendererGroup or anything based on it like GPU Resident Drawer or Entities Graphics.\n"
                + $"Shader pass: <unknown>, BatchDrawCommand index: {command}, (index in range: {inRange}), BatchDrawRange index: {range} MaterialID: {materialId.value} (\"{(material != null ? material.name : "<null>")}\"), MeshID: {meshId.value} (\"{(mesh != null ? mesh.name : "<null>")}\"), BatchID: {batchId.value}.");
        }

        static void WarnOnce(int what)
        {
            if ((s_Warned & (1u << what)) != 0) return;
            s_Warned |= 1u << what;
            Debug.LogWarning("Unbound: " + s_Unsupported[what]);
        }

        /// <summary>
        /// The draw commands a renderer list draws of a camera culling's outputs: the direct
        /// commands of the ranges whose filter settings pass the list's layer, rendering layer and
        /// motion filters (shadow-only ranges are left out), each an item of its mesh, submesh and
        /// material at the default sorting layer. Commands of removed batches or unregistered
        /// meshes and materials are skipped.
        /// </summary>
        internal static void Collect(CullingData data, FilteringSettings filtering, int mainLight, List<FrameBuilder.DrawItem> into, ref long sequence) =>
            Collect(data, filtering, mainLight, -1, into, ref sequence);

        /// <summary>The draw commands a light's shadow caster list draws: those of the light's
        /// culling outputs whose ranges cast shadows (On, TwoSided, ShadowsOnly), through the
        /// list's filters as a camera list's.</summary>
        internal static void CollectShadows(CullingData data, FilteringSettings filtering, int light, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            if (light >= 0) Collect(data, filtering, -1, light, into, ref sequence);
        }

        static void Collect(CullingData data, FilteringSettings filtering, int mainLight, int shadowLight, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            var main = mainLight >= 0 && mainLight < data.Lights.Length ? data.Lights[mainLight].light : null;
            bool shadows = shadowLight >= 0;
            foreach (var output in data.Batches)
            {
                if (output.Group == null) continue;
                if (shadows ? output.View != BatchCullingViewType.Light || output.Light != shadowLight : output.View != BatchCullingViewType.Camera) continue;
                ref var d = ref output.Drawn;
                if ((d.drawCommandCount > 0 && d.drawCommands == null) || (d.visibleInstanceCount > 0 && d.visibleInstances == null)
                    || (d.drawRangeCount > 0 && d.drawRanges == null))
                {
                    WarnOnce(UnsupportedMissingArrays);
                    continue;
                }
                for (int r = 0; r < d.drawRangeCount; r++)
                {
                    var range = d.drawRanges[r];
                    var filter = range.filterSettings;
                    // The Scripting API (BatchCullingContext.cullingLayerMask): "The draw command is
                    // discarded by the internal culling if the expression (1 << layer) &
                    // cullingLayerMask is false." So a camera culling's; a light culling's casters
                    // are drawn whatever their layer, though its mask leaves the layer out (measured:
                    // tests/projects/brg shadows, pillars on layers the sun's and the camera's culling
                    // masks leave out still cast shadows).
                    if (!shadows && (output.LayerMask & (1u << filter.layer)) == 0) continue;
                    if (shadows ? filter.shadowCastingMode == ShadowCastingMode.Off : filter.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    if ((filtering.layerMask & (1 << filter.layer)) == 0) continue;
                    if ((filtering.renderingLayerMask & filter.renderingLayerMask) == 0) continue;
                    if (filtering.excludeMotionVectorObjects && filter.motionMode == MotionVectorGenerationMode.Object) continue;
                    if (range.drawCommandsType == BatchDrawCommandType.Indirect)
                    {
                        CollectIndirect(output, ref d, range, filter, main, into, ref sequence);
                        continue;
                    }
                    if (range.drawCommandsType != BatchDrawCommandType.Direct)
                    {
                        WarnOnce(range.drawCommandsType == BatchDrawCommandType.Procedural ? UnsupportedProcedural : UnsupportedProceduralIndirect);
                        continue;
                    }
                    long end = Math.Min((long)range.drawCommandsBegin + range.drawCommandsCount, d.drawCommandCount);
                    for (long c = range.drawCommandsBegin; c < end; c++)
                    {
                        var command = d.drawCommands[c];
                        var group = output.Group;
                        var batch = group.GetBatch(command.batchID);
                        var mesh = group.GetRegisteredMesh(command.meshID);
                        var material = group.GetRegisteredMaterial(command.materialID);
                        if (batch == null || mesh == null || material == null)
                        {
                            LogInvalidId(c, c - range.drawCommandsBegin, r, command.materialID, material, command.meshID, mesh, command.batchID);
                            continue;
                        }
                        if (command.visibleCount == 0) continue;
                        if ((long)command.visibleOffset + command.visibleCount > d.visibleInstanceCount) continue;
                        into.Add(Item(output, (int)c, false, mesh, command.submeshIndex, material, command.flags, batch.Properties, filter, main, ref sequence));
                    }
                }
            }
        }

        /// <summary>A range's indirect commands: each draws its mesh's indices with the counts its
        /// arguments buffer holds at its offset (IndirectDrawIndexedArgs), the instances' batch
        /// indices read from its visible instance buffer from its visible offset on.</summary>
        static void CollectIndirect(Output output, ref BatchCullingOutputDrawCommands d, in BatchDrawRange range, in BatchFilterSettings filter, Light main,
            List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            if (range.drawCommandsCount > 0 && d.indirectDrawCommands == null)
            {
                WarnOnce(UnsupportedMissingArrays);
                return;
            }
            long end = Math.Min((long)range.drawCommandsBegin + range.drawCommandsCount, d.indirectDrawCommandCount);
            for (long c = range.drawCommandsBegin; c < end; c++)
            {
                var command = d.indirectDrawCommands[c];
                var group = output.Group;
                var batch = group.GetBatch(command.batchID);
                var mesh = group.GetRegisteredMesh(command.meshID);
                var material = group.GetRegisteredMaterial(command.materialID);
                if (batch == null || mesh == null || material == null) continue;
                if (command.topology != MeshTopology.Triangles)
                {
                    WarnOnce(UnsupportedIndirectTopology);
                    continue;
                }
                // The arguments give the indices (the first one and the count): the mesh's whole
                // index buffer is read.
                into.Add(Item(output, (int)c, true, mesh, 0, material, command.flags,
                    batch.WithVisibility(command.visibleInstancesBufferHandle), filter, main, ref sequence));
            }
        }

        /// <summary>A command's draw item: its mesh, submesh and material at the default sorting
        /// layer, its batch's properties and its header.</summary>
        static FrameBuilder.DrawItem Item(Output output, int command, bool indirect, Mesh mesh, int submesh, Material material, BatchDrawCommandFlags commandFlags,
            Dictionary<int, object> properties, in BatchFilterSettings filter, Light main, ref long sequence)
        {
            uint motion = filter.motionMode switch
            {
                MotionVectorGenerationMode.Camera => FlagCameraMotion,
                MotionVectorGenerationMode.ForceNoMotion => FlagForceZeroMotion,
                _ => 0u,
            };
            bool lit = main != null && (main.cullingMask & (1 << filter.layer)) != 0;
            var flags = (uint)commandFlags;
            bool lightmapped = (commandFlags & BatchDrawCommandFlags.IsLightMapped) != 0;
            if (lightmapped) WarnOnce(UnsupportedLightmap);
            if ((commandFlags & BatchDrawCommandFlags.LODCrossFadeKeyword) == BatchDrawCommandFlags.LODCrossFadeKeyword) WarnOnce(UnsupportedCrossFadeKeyword);
            var bounds = mesh.bounds;
            var header = new Header
            {
                RenderingLayer = filter.renderingLayerMask,
                Flags = (flags & (FlagFlipWinding | FlagLodCrossFadeValuePacked)) | motion
                    | (lit && !(lightmapped && FrameBuilder.InLightmap(main)) ? FlagMainLightEnabled : 0u),
                Center = bounds.center,
                Extent = bounds.extents,
            };
            var group = output.Group;
            return new FrameBuilder.DrawItem
            {
                Mesh = mesh.Sync(),
                Submesh = Math.Min(submesh, Math.Max(0, mesh.subMeshCount - 1)),
                Material = material,
                Model = Matrix4x4.identity,
                Color = Color.white,
                Queue = material.renderQueue,
                Sequence = sequence++,
                Properties = properties,
                Lighting = LightingOf(filter),
                Bounds = group.m_GlobalBounds ?? default,
                CullBounds = group.m_GlobalBounds ?? default,
                Layer = filter.layer,
                Dots = new DrawRef { Output = output, Command = command, Header = header, Indirect = indirect },
            };
        }

        /// <summary>A draw command's draws: its visible instances in draws of at most
        /// <see cref="MaxInstancesPerDraw"/>, each with its batch's window.</summary>
        internal static void Draw(in FrameBuilder.DrawItem item, DrawRef dots, ulong material, int pass, Dictionary<int, object> properties)
        {
            ref var d = ref dots.Output.Drawn;
            if (dots.Indirect)
            {
                var indirect = d.indirectDrawCommands[dots.Command];
                var indirectBatch = dots.Output.Group?.GetBatch(indirect.batchID);
                if (indirectBatch == null) return;
                GfxStream.DotsDraw(dots.Header, indirectBatch.Offset, indirectBatch.Window, indirect.visibleOffset | IndirectVisibleOffsetEnabled, null, 0);
                GfxStream.DrawSetup(new DrawSetup { Instances = 1, Args = indirect.indirectArgsBufferHandle.value, Offset = indirect.indirectArgsBufferOffset, Indexed = true });
                GfxStream.Draw(item.Mesh, item.Submesh, material, pass, item.Model, item.Color, 0, null, properties);
                return;
            }
            var command = d.drawCommands[dots.Command];
            var batch = dots.Output.Group?.GetBatch(command.batchID);
            if (batch == null) return;
            int* visible = d.visibleInstances + command.visibleOffset;
            for (uint first = 0; first < command.visibleCount; first += MaxInstancesPerDraw)
            {
                int count = (int)Math.Min(MaxInstancesPerDraw, command.visibleCount - first);
                GfxStream.DotsDraw(dots.Header, batch.Offset, batch.Window, 0, visible + first, count);
                GfxStream.Draw(item.Mesh, item.Submesh, material, pass, item.Model, item.Color, 0, null, properties);
            }
        }
    }
}
