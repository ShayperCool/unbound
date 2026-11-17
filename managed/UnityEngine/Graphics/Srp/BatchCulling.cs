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

        /// <summary>The kind of a draw item's command: which of the output's command arrays it is in
        /// (BatchDrawCommandType).</summary>
        internal enum CommandKind : byte { Direct, Indirect, Procedural, ProceduralIndirect }

        /// <summary>A draw item's draw command: its output, kind and index, and its header.</summary>
        internal struct DrawRef
        {
            internal Output Output;
            internal int Command;
            internal Header Header;
            internal CommandKind Kind;
            /// <summary>The one visible instance (from the command's first) a draw of a command
            /// with sorting positions draws, -1 for the command's whole visible range.</summary>
            internal int Instance;
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
        /// LOD parameters, the camera's layers that the light lights, and the light as the view
        /// (slice 0 for a directional light, -1 for a spot or point light). Every light type takes
        /// the same context. Matches the Unity 6000.0.71f1 Editor, tests/projects/brg
        /// <c>shadows</c> (a directional light of two cascades) and <c>local_shadows</c> (a spot
        /// light and a point light's six faces).
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
                    new BatchPackedCullingViewID(light.GetInstanceID(), light.type == LightType.Directional ? 0 : LocalLightViewSlice).handle, parameters.cullingMask & (uint)light.cullingMask,
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

        static readonly Dictionary<(ShadowCastingMode, bool, int), ImmediateLighting> s_Lighting = new Dictionary<(ShadowCastingMode, bool, int), ImmediateLighting>();

        /// <summary>A draw's lighting: its range's shadow settings and its command's lightmap (-1:
        /// none), without light or reflection probes (a group provides its instances' lighting data
        /// itself).</summary>
        static ImmediateLighting LightingOf(in BatchFilterSettings filter, int lightmap)
        {
            var key = (filter.shadowCastingMode, filter.receiveShadows, lightmap);
            if (!s_Lighting.TryGetValue(key, out var lighting))
                s_Lighting[key] = lighting = new ImmediateLighting
                {
                    Cast = key.Item1, Receive = key.Item2, Probes = LightProbeUsage.Off, Reflections = ReflectionProbeUsage.Off, LightmapIndex = lightmap,
                };
            return lighting;
        }

        /// <summary>The unsupported parts of draw commands warned about so far, one bit each
        /// (<see cref="WarnOnce"/>): checking costs no allocation once warned.</summary>
        internal static uint s_Warned;

        // Unsupported parts of draw commands (bits of s_Warned).
        const int UnsupportedIndirectTopology = 0, UnsupportedQuads = 1, UnsupportedIndexBuffer = 2,
            UnsupportedCrossFadeKeyword = 3, UnsupportedMissingArrays = 4, UnsupportedSortingPosition = 5, UnsupportedSplit = 6,
            UnsupportedVisibilityWindow = 7;

        /// <summary>The view slice of a spot or point light's culling (a directional light's is 0):
        /// measured, tests/projects/brg local_shadows.</summary>
        const int LocalLightViewSlice = -1;

        static readonly string[] s_Unsupported =
        {
            "BatchRendererGroup indirect draw commands of a mesh read as a line strip or quads are not supported yet; they are not drawn.",
            "BatchRendererGroup procedural draw commands of quads are not supported on this graphics device yet; they are not drawn.",
            "a BatchRendererGroup procedural draw command's index buffer is not a live index buffer, or its byte offset is not a multiple of its index size; it is not drawn.",
            "BatchRendererGroup draw commands flagged LODCrossFadeKeyword draw without LOD_FADE_CROSSFADE (not enabled yet).",
            "a BatchRendererGroup culling callback gave draw commands or visible instances without allocating their arrays; its output is not drawn.",
            "a BatchRendererGroup draw command flagged HasSortingPosition points outside instanceSortingPositions; it sorts by its sortingPosition as an approximate depth.",
            "a shadow caster list's split is not one of the six a BatchRendererGroup splitVisibilityMask covers; its commands are drawn whatever their mask.",
            "a BatchRendererGroup indirect draw command's visible instances start past the 16 KB a constant buffer window reads from the buffer's start; it is not drawn.",
        };

        /// <summary>The visible instance indices an indirect command's constant buffer reads
        /// (core's UnityDOTSInstancing.hlsl: <c>unity_DOTSInstancing_IndirectInstanceVisibilityRaw[1024]</c>,
        /// four indices per float4).</summary>
        const uint IndirectVisibilityElements = 4096;

        /// <summary>The Editor's error for an indirect command with a visible instance buffer window
        /// where batches are raw buffers, by the window (kept: the message is logged again for
        /// every list that reads the command, as the Editor's).</summary>
        static readonly Dictionary<(uint, uint), string> s_RawWindowErrors = new Dictionary<(uint, uint), string>();

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
        internal static void Collect(FrameBuilder.CameraContext camera, CullingData data, FilteringSettings filtering, int mainLight, List<FrameBuilder.DrawItem> into, ref long sequence) =>
            Collect(camera, data, filtering, mainLight, -1, into, ref sequence);

        /// <summary>The draw commands a light's shadow caster list for split <paramref name="split"/>
        /// draws: those of the light's culling outputs whose ranges cast shadows (On, TwoSided,
        /// ShadowsOnly) and whose splitVisibilityMask has the split's bit, through the list's
        /// filters as a camera list's.</summary>
        internal static void CollectShadows(CullingData data, FilteringSettings filtering, int light, int split, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            if (light < 0) return;
            var bit = SplitBit(split);
            if (bit == 0) WarnOnce(UnsupportedSplit);
            Collect(null, data, filtering, -1, light, into, ref sequence, bit);
        }

        /// <summary>The splitVisibilityMask bit of a shadow split: "the least significant bit
        /// corresponds to the first culling split" (the Scripting API's
        /// BatchDrawCommand.splitVisibilityMask; a camera culling's draws ignore the mask, measured:
        /// tests/projects/brg split_masks). A split past the sixth is unmeasured: it takes no bit and the mask is not applied.</summary>
        static ushort SplitBit(int split) => split >= 0 && split < MaxSplits ? (ushort)(1 << split) : (ushort)0;

        /// <summary>The culling splits a mask covers: a directional light's cascades or a point
        /// light's six cube faces.</summary>
        const int MaxSplits = 6;

        static void Collect(FrameBuilder.CameraContext camera, CullingData data, FilteringSettings filtering, int mainLight, int shadowLight, List<FrameBuilder.DrawItem> into, ref long sequence,
            ushort splitBit = 0)
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
                    switch (range.drawCommandsType)
                    {
                        case BatchDrawCommandType.Direct:
                            CollectDirect(camera, output, ref d, range, r, filter, main, splitBit, into, ref sequence);
                            break;
                        case BatchDrawCommandType.Indirect:
                            CollectIndirect(camera, output, ref d, range, filter, main, splitBit, into, ref sequence);
                            break;
                        case BatchDrawCommandType.Procedural:
                            CollectProcedural(camera, output, ref d, range, filter, main, splitBit, into, ref sequence);
                            break;
                        case BatchDrawCommandType.ProceduralIndirect:
                            CollectProceduralIndirect(camera, output, ref d, range, filter, main, splitBit, into, ref sequence);
                            break;
                    }
                }
            }
        }

        /// <summary>A range's direct commands: each draws its mesh's submesh for its visible
        /// instances. A shadow split's list (<paramref name="splitBit"/> set) draws the commands
        /// visible in the split.</summary>
        static void CollectDirect(FrameBuilder.CameraContext camera, Output output, ref BatchCullingOutputDrawCommands d, in BatchDrawRange range, int r, in BatchFilterSettings filter,
            Light main, ushort splitBit, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
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
                if (splitBit != 0 && (command.splitVisibilityMask & splitBit) == 0) continue;
                if ((long)command.visibleOffset + command.visibleCount > d.visibleInstanceCount) continue;
                var item = Item(output, (int)c, CommandKind.Direct, mesh, command.submeshIndex, mesh.bounds, material, command.flags, command.lightmapIndex, batch.Properties, filter, main, ref sequence);
                AddSorted(camera, ref d, item, command.flags, command.sortingPosition, command.visibleCount, into, ref sequence);
            }
        }

        /// <summary>A direct or procedural command's item: sorted by its sortingPosition, or, with
        /// sorting positions and a camera, as one item per visible instance, each at its own
        /// position.</summary>
        static void AddSorted(FrameBuilder.CameraContext camera, ref BatchCullingOutputDrawCommands d, in FrameBuilder.DrawItem item, BatchDrawCommandFlags flags, int sortingPosition,
            uint visibleCount, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            if ((flags & BatchDrawCommandFlags.HasSortingPosition) == 0 || camera == null)
            {
                var sorted = item;
                sorted.Distance = ApproximateDistance(sortingPosition);
                into.Add(sorted);
                return;
            }
            // Each instance is drawn and sorted on its own, at its sorting position.
            for (int k = 0; k < visibleCount; k++)
            {
                var instance = item;
                instance.Sequence = k == 0 ? item.Sequence : sequence++;
                instance.Dots = item.Dots.Value with { Instance = k };
                instance.Distance = SortingDistance(camera, ref d, sortingPosition + 3L * k, sortingPosition);
                into.Add(instance);
            }
        }

        /// <summary>A range's procedural commands (BatchDrawCommandProcedural): each draws its
        /// element count of vertex ids, or of its index buffer's indices from its byte offset plus
        /// its base vertex, as its topology, for its visible instances, with no mesh (the vertex
        /// program builds its vertices; their attributes read zero). A non-indexed command's base
        /// vertex is not a first vertex: its ids start at 0 (measured: tests/projects/brg
        /// procedural.c1, as procedural.c0).</summary>
        static void CollectProcedural(FrameBuilder.CameraContext camera, Output output, ref BatchCullingOutputDrawCommands d, in BatchDrawRange range, in BatchFilterSettings filter,
            Light main, ushort splitBit, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            if (range.drawCommandsCount > 0 && d.proceduralDrawCommands == null)
            {
                WarnOnce(UnsupportedMissingArrays);
                return;
            }
            long end = Math.Min((long)range.drawCommandsBegin + range.drawCommandsCount, d.proceduralDrawCommandCount);
            for (long c = range.drawCommandsBegin; c < end; c++)
            {
                var command = d.proceduralDrawCommands[c];
                var group = output.Group;
                var batch = group.GetBatch(command.batchID);
                var material = group.GetRegisteredMaterial(command.materialID);
                // An unknown batch or material is skipped without the invalid-ID error, which
                // names a mesh (unmeasured for procedural commands).
                if (batch == null || material == null) continue;
                if (command.visibleCount == 0 || command.elementCount == 0) continue;
                if (splitBit != 0 && (command.splitVisibilityMask & splitBit) == 0) continue;
                if ((long)command.visibleOffset + command.visibleCount > d.visibleInstanceCount) continue;
                if (!ProceduralShape(command.topology, command.indexBufferHandle, command.indexOffsetBytes, command.elementCount, false, out var mesh, out _)) continue;
                var item = Item(output, (int)c, CommandKind.Procedural, mesh, 0, default, material, command.flags, command.lightmapIndex, batch.Properties, filter, main, ref sequence);
                AddSorted(camera, ref d, item, command.flags, command.sortingPosition, command.visibleCount, into, ref sequence);
            }
        }

        /// <summary>
        /// The stand-in mesh a procedural command draws and the indices it reads: vertex ids from
        /// 0 (<see cref="ProceduralMeshes"/>; an indirect command's count from its arguments), or
        /// its index buffer's indices over the 65536 zeroed vertices of <see cref="IndexedDraws"/>
        /// (a direct command's from its byte offset, at most its element count and the buffer's;
        /// an indirect command's first index and count from its arguments). Quads, which the
        /// devices have no primitive for, are drawn as the device draws them
        /// (<see cref="GraphicsDeviceProfile.ProceduralQuads"/>). False, warned once, when Unbound
        /// cannot draw it: quads on a device where they are unmeasured, an index buffer that is not
        /// a live index buffer or a byte offset that does not start an index, index sizes and
        /// topologies <see cref="IndexedDraws"/> does not draw.
        /// </summary>
        static bool ProceduralShape(MeshTopology topology, GraphicsBufferHandle indexBuffer, uint indexOffsetBytes, uint count, bool indirect, out Mesh mesh, out DrawSetup setup)
        {
            mesh = null;
            setup = default;
            if (topology == MeshTopology.Quads)
            {
                if (GraphicsDeviceProfile.Current.ProceduralQuads is not MeshTopology quads)
                {
                    WarnOnce(UnsupportedQuads);
                    return false;
                }
                topology = quads;
            }
            if (indexBuffer.value == 0)
            {
                mesh = ProceduralMeshes.Get(topology, indirect ? ProceduralMeshes.IndirectVertices : (int)Math.Min(count, int.MaxValue));
                return true;
            }
            var indices = GpuBufferStorage.Find(indexBuffer.value);
            if (indices == null || indices.Released || !indices.Has(GraphicsBuffer.Target.Index) || indexOffsetBytes % (uint)indices.Stride != 0)
            {
                WarnOnce(UnsupportedIndexBuffer);
                return false;
            }
            if (!IndexedDraws.Supported(indices.Stride, topology, indirect ? "BatchDrawCommandProceduralIndirect" : "BatchDrawCommandProcedural")) return false;
            mesh = IndexedDraws.StandIn(topology);
            if (indirect)
            {
                setup = IndexedDraws.Indirect(indices.Handle, indices.Stride, indices.Count, 0, 0);
                return true;
            }
            long first = indexOffsetBytes / indices.Stride;
            // The Editor draws a count past the buffer's end without an error (as
            // IndexedDraws.Count); Unbound reads the buffer's.
            long drawn = Math.Min(count, indices.Count - first);
            if (drawn <= 0) return false;
            setup = IndexedDraws.Direct(indices.Handle, indices.Stride, (int)first, (int)drawn, 0);
            return true;
        }

        /// <summary>Whether an indirect command's visible instance buffer window is refused, with
        /// the Editor's error: where batches are raw buffers, a window offset or size other than
        /// zero (measured on Vulkan: tests/projects/brg procedural, each indirect command with a
        /// window is logged for every list that reads it and is not drawn).</summary>
        static bool RefusedWindow(uint offset, uint size)
        {
            if ((offset == 0 && size == 0) || BatchRendererGroup.BufferTarget != BatchBufferTarget.RawBuffer) return false;
            if (!s_RawWindowErrors.TryGetValue((offset, size), out var message))
                s_RawWindowErrors[(offset, size)] = message = "BatchRendererGroup had non-zero ConstantBuffer mode parameters when BufferTarget is RawBuffer. "
                    + $"Pass zero for both visibleInstancesBufferWindowOffset and visibleInstancesBufferWindowSizeBytes in RawBuffer mode. (got {offset}, {size})";
            Debug.LogError(message);
            return true;
        }

        /// <summary>An indirect command's packed visible offset (<see cref="IndirectVisibleOffsetEnabled"/>
        /// set): its visible offset in the window of its visible instance buffer. A constant buffer
        /// is read from the buffer's start, so the window's offset is counted in (measured on
        /// OpenGL Core: tests/projects/brg procedural.c16, a window at byte 256 reads the indices
        /// there); false, warned once, when that passes what the constant buffer reads.</summary>
        static bool PackedVisibleOffset(uint visibleOffset, uint windowOffset, out uint packed)
        {
            uint start = BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer ? visibleOffset + windowOffset / 4 : visibleOffset;
            packed = start | IndirectVisibleOffsetEnabled;
            if (BatchRendererGroup.BufferTarget != BatchBufferTarget.ConstantBuffer || start < IndirectVisibilityElements) return true;
            WarnOnce(UnsupportedVisibilityWindow);
            return false;
        }

        /// <summary>The sorting distance of an indirect command, whose instance count is on the GPU:
        /// with sorting positions and a camera, its first instance's (unmeasured for indirect
        /// commands), else its sortingPosition's.</summary>
        static float IndirectDistance(FrameBuilder.CameraContext camera, ref BatchCullingOutputDrawCommands d, BatchDrawCommandFlags flags, int sortingPosition) =>
            (flags & BatchDrawCommandFlags.HasSortingPosition) != 0 && camera != null
                ? SortingDistance(camera, ref d, sortingPosition, sortingPosition)
                : ApproximateDistance(sortingPosition);

        /// <summary>A range's procedural indirect commands (BatchDrawCommandProceduralIndirect):
        /// each draws, as its topology, the vertex ids or its index buffer's indices its arguments
        /// buffer counts (IndirectDrawArgs, or IndirectDrawIndexedArgs with an index buffer), the
        /// instances' batch indices read from its visible instance buffer as an indirect
        /// command's.</summary>
        static void CollectProceduralIndirect(FrameBuilder.CameraContext camera, Output output, ref BatchCullingOutputDrawCommands d, in BatchDrawRange range, in BatchFilterSettings filter,
            Light main, ushort splitBit, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            if (range.drawCommandsCount > 0 && d.proceduralIndirectDrawCommands == null)
            {
                WarnOnce(UnsupportedMissingArrays);
                return;
            }
            long end = Math.Min((long)range.drawCommandsBegin + range.drawCommandsCount, d.proceduralIndirectDrawCommandCount);
            for (long c = range.drawCommandsBegin; c < end; c++)
            {
                var command = d.proceduralIndirectDrawCommands[c];
                var group = output.Group;
                var batch = group.GetBatch(command.batchID);
                var material = group.GetRegisteredMaterial(command.materialID);
                if (batch == null || material == null) continue;
                if (splitBit != 0 && (command.splitVisibilityMask & splitBit) == 0) continue;
                if (RefusedWindow(command.visibleInstancesBufferWindowOffset, command.visibleInstancesBufferWindowSizeBytes)) continue;
                if (!PackedVisibleOffset(command.visibleOffset, command.visibleInstancesBufferWindowOffset, out _)) continue;
                if (!ProceduralShape(command.topology, command.indexBufferHandle, 0, 0, true, out var mesh, out _)) continue;
                var item = Item(output, (int)c, CommandKind.ProceduralIndirect, mesh, 0, default, material, command.flags, command.lightmapIndex,
                    batch.WithVisibility(command.visibleInstancesBufferHandle), filter, main, ref sequence);
                item.Distance = IndirectDistance(camera, ref d, command.flags, command.sortingPosition);
                into.Add(item);
            }
        }

        /// <summary>A range's indirect commands: each draws its mesh's indices, read as its
        /// topology (triangles, lines or points: measured, tests/projects/brg procedural), with the
        /// counts its arguments buffer holds at its offset (IndirectDrawIndexedArgs), the instances'
        /// batch indices read from its visible instance buffer from its visible offset on.</summary>
        static void CollectIndirect(FrameBuilder.CameraContext camera, Output output, ref BatchCullingOutputDrawCommands d, in BatchDrawRange range, in BatchFilterSettings filter, Light main,
            ushort splitBit, List<FrameBuilder.DrawItem> into, ref long sequence)
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
                // A shadow split's list (splitBit set) draws the commands visible in the split.
                if (splitBit != 0 && (command.splitVisibilityMask & splitBit) == 0) continue;
                if (IndirectTopology(command.topology) is null)
                {
                    WarnOnce(UnsupportedIndirectTopology);
                    continue;
                }
                if (RefusedWindow(command.visibleInstancesBufferWindowOffset, command.visibleInstancesBufferWindowSizeBytes)) continue;
                if (!PackedVisibleOffset(command.visibleOffset, command.visibleInstancesBufferWindowOffset, out _)) continue;
                // The arguments give the indices (the first one and the count): the mesh's whole
                // index buffer is read.
                var item = Item(output, (int)c, CommandKind.Indirect, mesh, 0, mesh.bounds, material, command.flags, command.lightmapIndex,
                    batch.WithVisibility(command.visibleInstancesBufferHandle), filter, main, ref sequence);
                item.Distance = IndirectDistance(camera, ref d, command.flags, command.sortingPosition);
                into.Add(item);
            }
        }

        /// <summary>What an indirect command's mesh indices are read as (<see cref="GfxStream.DrawShape"/>,
        /// whatever the submesh's own topology): triangles, lines or points; null for the
        /// topologies not drawn yet (a line strip and quads, unmeasured).</summary>
        static MeshTopology? IndirectTopology(MeshTopology topology) => topology switch
        {
            MeshTopology.Triangles or MeshTopology.Lines or MeshTopology.Points => topology,
            _ => null,
        };

        /// <summary>The sorting distance of a command without sorting positions: its
        /// sortingPosition cast to a float is "an approximate depth" (the Scripting API's
        /// BatchDrawCommand.sortingPosition), which sorts as minus a value of the camera's
        /// distance metric: a larger value is drawn later among transparent draws, and against an
        /// ordinary renderer it sorts nearer above minus the renderer's squared distance through a
        /// perspective camera, minus its view depth through an orthographic one (measured:
        /// tests/projects/brg sorting, sorting_ortho; equal values keep the commands' order).</summary>
        static float ApproximateDistance(int sortingPosition) => -(float)sortingPosition;

        /// <summary>The sorting distance of an instance at <paramref name="at"/> of the culling's
        /// instanceSortingPositions (three floats, its world position): the camera's distance
        /// metric, as an ordinary renderer's (measured: tests/projects/brg sorting). A position
        /// outside the array is warned about and the command's sortingPosition is taken as an
        /// approximate depth (unmeasured).</summary>
        static float SortingDistance(FrameBuilder.CameraContext camera, ref BatchCullingOutputDrawCommands d, long at, int sortingPosition)
        {
            if (d.instanceSortingPositions == null || at < 0 || at + 3 > d.instanceSortingPositionFloatCount)
            {
                WarnOnce(UnsupportedSortingPosition);
                return ApproximateDistance(sortingPosition);
            }
            var p = d.instanceSortingPositions + at;
            return FrameBuilder.Distance(camera, new Vector3(p[0], p[1], p[2]));
        }

        /// <summary>A command's draw item: its mesh (a procedural command's stand-in), submesh and
        /// material at the default sorting layer, its batch's properties and its header, whose
        /// mesh bounds are <paramref name="bounds"/> (a procedural command's: none).</summary>
        static FrameBuilder.DrawItem Item(Output output, int command, CommandKind kind, Mesh mesh, int submesh, in Bounds bounds, Material material, BatchDrawCommandFlags commandFlags,
            ushort lightmapIndex, Dictionary<int, object> properties, in BatchFilterSettings filter, Light main, ref long sequence)
        {
            uint motion = filter.motionMode switch
            {
                MotionVectorGenerationMode.Camera => FlagCameraMotion,
                MotionVectorGenerationMode.ForceNoMotion => FlagForceZeroMotion,
                _ => 0u,
            };
            bool lit = main != null && (main.cullingMask & (1 << filter.layer)) != 0;
            var flags = (uint)commandFlags;
            // The legacy lightmaps keyword's commands (URP's GPU Resident Drawer sets it on every
            // command) sample the scene lightmap of their index. Without it a command's lightmaps
            // are texture arrays its material carries with LIGHTMAP_ON, as Entities Graphics
            // makes them: the material's keywords and textures and the instances'
            // unity_LightmapIndex and unity_LightmapST draw them, the IsLightMapped flag or not
            // (tests/projects/brg lightmap_arrays).
            bool legacy = (commandFlags & BatchDrawCommandFlags.UseLegacyLightmapsKeyword) != 0;
            int lightmap = legacy && BakedLighting.IsLightmapIndex(lightmapIndex) ? lightmapIndex : -1;
            bool lightmapped = lightmap >= 0 || (commandFlags & BatchDrawCommandFlags.IsLightMapped) != 0;
            if ((commandFlags & BatchDrawCommandFlags.LODCrossFadeKeyword) == BatchDrawCommandFlags.LODCrossFadeKeyword) WarnOnce(UnsupportedCrossFadeKeyword);
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
                Lighting = LightingOf(filter, lightmap),
                Bounds = group.m_GlobalBounds ?? default,
                CullBounds = group.m_GlobalBounds ?? default,
                Layer = filter.layer,
                Dots = new DrawRef { Output = output, Command = command, Header = header, Kind = kind, Instance = -1 },
            };
        }

        /// <summary>A draw command's draws: a direct or procedural command's visible instances in
        /// draws of at most <see cref="MaxInstancesPerDraw"/>, each with its batch's window (a sorted
        /// instance's item draws that instance alone); an indirect command's one draw, its counts
        /// from its arguments buffer.</summary>
        internal static void Draw(in FrameBuilder.DrawItem item, DrawRef dots, ulong material, int pass, Dictionary<int, object> properties)
        {
            ref var d = ref dots.Output.Drawn;
            var group = dots.Output.Group;
            switch (dots.Kind)
            {
                case CommandKind.Indirect:
                {
                    var command = d.indirectDrawCommands[dots.Command];
                    var batch = group?.GetBatch(command.batchID);
                    if (batch == null || !PackedVisibleOffset(command.visibleOffset, command.visibleInstancesBufferWindowOffset, out uint packed)) return;
                    GfxStream.DotsDraw(dots.Header, batch.Offset, batch.Window, packed, null, 0);
                    GfxStream.DrawShape(IndirectTopology(command.topology), 0);
                    GfxStream.DrawSetup(new DrawSetup { Instances = 1, Args = command.indirectArgsBufferHandle.value, Offset = command.indirectArgsBufferOffset, Indexed = true });
                    GfxStream.Draw(item.Mesh, item.Submesh, material, pass, item.Model, item.Color, 0, null, properties);
                    return;
                }
                case CommandKind.ProceduralIndirect:
                {
                    var command = d.proceduralIndirectDrawCommands[dots.Command];
                    var batch = group?.GetBatch(command.batchID);
                    if (batch == null || !PackedVisibleOffset(command.visibleOffset, command.visibleInstancesBufferWindowOffset, out uint packed)
                        || !ProceduralShape(command.topology, command.indexBufferHandle, 0, 0, true, out _, out var setup)) return;
                    GfxStream.DotsDraw(dots.Header, batch.Offset, batch.Window, packed, null, 0);
                    setup.Instances = 1;
                    setup.Args = command.indirectArgsBufferHandle.value;
                    setup.Offset = command.indirectArgsBufferOffset;
                    GfxStream.DrawSetup(setup);
                    GfxStream.Draw(item.Mesh, item.Submesh, material, pass, item.Model, item.Color, 0, null, properties);
                    return;
                }
                case CommandKind.Procedural:
                {
                    var command = d.proceduralDrawCommands[dots.Command];
                    var batch = group?.GetBatch(command.batchID);
                    if (batch == null || !ProceduralShape(command.topology, command.indexBufferHandle, command.indexOffsetBytes, command.elementCount, false, out _, out var setup)) return;
                    DrawInstances(item, dots, batch, d.visibleInstances + command.visibleOffset, command.visibleCount, setup, (int)command.baseVertex, material, pass, properties);
                    return;
                }
                default:
                {
                    var command = d.drawCommands[dots.Command];
                    var batch = group?.GetBatch(command.batchID);
                    if (batch == null) return;
                    DrawInstances(item, dots, batch, d.visibleInstances + command.visibleOffset, command.visibleCount, default, 0, material, pass, properties);
                    return;
                }
            }
        }

        /// <summary>A direct or procedural command's visible instances in draws of at most
        /// <see cref="MaxInstancesPerDraw"/> (a sorted instance's item draws that instance alone),
        /// each reading <paramref name="indices"/>' indices plus <paramref name="baseVertex"/> when it
        /// has a buffer.</summary>
        static void DrawInstances(in FrameBuilder.DrawItem item, DrawRef dots, BatchRendererGroup.Batch batch, int* visible, uint visibleCount, DrawSetup indices, int baseVertex,
            ulong material, int pass, Dictionary<int, object> properties)
        {
            uint begin = dots.Instance >= 0 ? (uint)dots.Instance : 0u;
            uint end = dots.Instance >= 0 ? Math.Min(begin + 1, visibleCount) : visibleCount;
            for (uint first = begin; first < end; first += MaxInstancesPerDraw)
            {
                int count = (int)Math.Min(MaxInstancesPerDraw, end - first);
                GfxStream.DotsDraw(dots.Header, batch.Offset, batch.Window, 0, visible + first, count);
                if (indices.Indices != 0)
                {
                    if (baseVertex != 0) GfxStream.DrawShape(null, baseVertex);
                    indices.Instances = (uint)count;
                    GfxStream.DrawSetup(indices);
                }
                GfxStream.Draw(item.Mesh, item.Submesh, material, pass, item.Model, item.Color, 0, null, properties);
            }
        }
    }
}
