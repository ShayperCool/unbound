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
        }

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
        static NativeArray<Plane> s_Planes = new NativeArray<Plane>(10, Allocator.Persistent);
        static NativeArray<CullingSplit> s_Splits = new NativeArray<CullingSplit>(1, Allocator.Persistent);
        static readonly List<BatchRendererGroup> s_Invoked = new List<BatchRendererGroup>();

        /// <summary>A camera's culling: the callbacks of the groups that take camera views, with
        /// the culling's planes as its one split.</summary>
        internal static void CullCamera(CullingData data, ref ScriptableCullingParameters parameters, Camera camera)
        {
            if (BatchRendererGroup.s_Groups.Count == 0) return;
            int planes = parameters.cullingPlaneCount;
            for (int i = 0; i < planes; i++) s_Planes[i] = parameters.GetCullingPlane(i);
            s_Splits[0] = new CullingSplit
            {
                cullingPlaneOffset = 0,
                cullingPlaneCount = planes,
                cascadeBlendCullingFactor = 1f,
                nearPlane = camera != null ? camera.nearClipPlane : 0f,
                cullingMatrix = parameters.cullingMatrix,
            };
            var context = new BatchCullingContext(s_Planes.GetSubArray(0, planes), s_Splits, parameters.lodParameters, Matrix4x4.identity,
                BatchCullingViewType.Camera, parameters.isOrthographic ? BatchCullingProjectionType.Orthographic : BatchCullingProjectionType.Perspective,
                BatchCullingFlags.None, new BatchPackedCullingViewID(camera != null ? camera.GetInstanceID() : 0, 0).handle, parameters.cullingMask,
                GameViewSceneCullingMask, 0, 0, 0, IntPtr.Zero);
            Invoke(data, context);
        }

        /// <summary>Invokes the callbacks for a culling (the groups created by a callback wait for
        /// the next culling).</summary>
        static void Invoke(CullingData data, in BatchCullingContext context)
        {
            s_Invoked.Clear();
            s_Invoked.AddRange(BatchRendererGroup.s_Groups);
            foreach (var group in s_Invoked)
            {
                if (!group.Enabled(context.viewType) || !BatchRendererGroup.s_Groups.Contains(group)) continue;
                var output = s_Free.Count > 0 ? s_Free.Pop() : new Output();
                output.Group = group;
                output.View = context.viewType;
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
        const int UnsupportedIndirect = 0, UnsupportedProcedural = 1, UnsupportedProceduralIndirect = 2, UnsupportedLightmap = 3,
            UnsupportedCrossFadeKeyword = 4, UnsupportedMissingArrays = 5;

        static readonly string[] s_Unsupported =
        {
            "BatchRendererGroup indirect draw commands are not supported yet; they are not drawn.",
            "BatchRendererGroup procedural draw commands are not supported yet; they are not drawn.",
            "BatchRendererGroup procedural indirect draw commands are not supported yet; they are not drawn.",
            "BatchRendererGroup draw commands flagged IsLightMapped draw without their lightmaps (LIGHTMAP_ON and the lightmap of lightmapIndex are not bound yet).",
            "BatchRendererGroup draw commands flagged LODCrossFadeKeyword draw without LOD_FADE_CROSSFADE (not enabled yet).",
            "a BatchRendererGroup culling callback gave draw commands or visible instances without allocating their arrays; its output is not drawn.",
        };

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
        internal static void Collect(CullingData data, FilteringSettings filtering, int mainLight, List<FrameBuilder.DrawItem> into, ref long sequence)
        {
            var main = mainLight >= 0 && mainLight < data.Lights.Length ? data.Lights[mainLight].light : null;
            foreach (var output in data.Batches)
            {
                if (output.View != BatchCullingViewType.Camera || output.Group == null) continue;
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
                    if (filter.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    if ((filtering.layerMask & (1 << filter.layer)) == 0) continue;
                    if ((filtering.renderingLayerMask & filter.renderingLayerMask) == 0) continue;
                    if (filtering.excludeMotionVectorObjects && filter.motionMode == MotionVectorGenerationMode.Object) continue;
                    if (range.drawCommandsType != BatchDrawCommandType.Direct)
                    {
                        WarnOnce(range.drawCommandsType switch
                        {
                            BatchDrawCommandType.Indirect => UnsupportedIndirect,
                            BatchDrawCommandType.Procedural => UnsupportedProcedural,
                            _ => UnsupportedProceduralIndirect,
                        });
                        continue;
                    }
                    var lighting = LightingOf(filter);
                    uint motion = filter.motionMode switch
                    {
                        MotionVectorGenerationMode.Camera => FlagCameraMotion,
                        MotionVectorGenerationMode.ForceNoMotion => FlagForceZeroMotion,
                        _ => 0u,
                    };
                    bool lit = main != null && (main.cullingMask & (1 << filter.layer)) != 0;
                    long end = Math.Min((long)range.drawCommandsBegin + range.drawCommandsCount, d.drawCommandCount);
                    for (long c = range.drawCommandsBegin; c < end; c++)
                    {
                        var command = d.drawCommands[c];
                        var group = output.Group;
                        var batch = group.GetBatch(command.batchID);
                        var mesh = group.GetRegisteredMesh(command.meshID);
                        var material = group.GetRegisteredMaterial(command.materialID);
                        if (batch == null || mesh == null || material == null || command.visibleCount == 0) continue;
                        if ((long)command.visibleOffset + command.visibleCount > d.visibleInstanceCount) continue;
                        var flags = (uint)command.flags;
                        bool lightmapped = (command.flags & BatchDrawCommandFlags.IsLightMapped) != 0;
                        if (lightmapped) WarnOnce(UnsupportedLightmap);
                        if ((command.flags & BatchDrawCommandFlags.LODCrossFadeKeyword) == BatchDrawCommandFlags.LODCrossFadeKeyword) WarnOnce(UnsupportedCrossFadeKeyword);
                        var bounds = mesh.bounds;
                        var header = new Header
                        {
                            RenderingLayer = filter.renderingLayerMask,
                            Flags = (flags & (FlagFlipWinding | FlagLodCrossFadeValuePacked)) | motion
                                | (lit && !(lightmapped && FrameBuilder.InLightmap(main)) ? FlagMainLightEnabled : 0u),
                            Center = bounds.center,
                            Extent = bounds.extents,
                        };
                        into.Add(new FrameBuilder.DrawItem
                        {
                            Mesh = mesh.Sync(),
                            Submesh = Math.Min(command.submeshIndex, Math.Max(0, mesh.subMeshCount - 1)),
                            Material = material,
                            Model = Matrix4x4.identity,
                            Color = Color.white,
                            Queue = material.renderQueue,
                            Sequence = sequence++,
                            Properties = batch.Properties,
                            Lighting = lighting,
                            Bounds = group.m_GlobalBounds ?? default,
                            CullBounds = group.m_GlobalBounds ?? default,
                            Layer = filter.layer,
                            Dots = new DrawRef { Output = output, Command = (int)c, Header = header },
                        });
                    }
                }
            }
        }

        /// <summary>A draw command's draws: its visible instances in draws of at most
        /// <see cref="MaxInstancesPerDraw"/>, each with its batch's window.</summary>
        internal static void Draw(in FrameBuilder.DrawItem item, DrawRef dots, ulong material, int pass, Dictionary<int, object> properties)
        {
            ref var d = ref dots.Output.Drawn;
            var command = d.drawCommands[dots.Command];
            var batch = dots.Output.Group?.GetBatch(command.batchID);
            if (batch == null) return;
            int* visible = d.visibleInstances + command.visibleOffset;
            for (uint first = 0; first < command.visibleCount; first += MaxInstancesPerDraw)
            {
                int count = (int)Math.Min(MaxInstancesPerDraw, command.visibleCount - first);
                GfxStream.DotsDraw(dots.Header, batch.Offset, batch.Window, visible + first, count);
                GfxStream.Draw(item.Mesh, item.Submesh, material, pass, item.Model, item.Color, 0, null, properties);
            }
        }
    }
}
