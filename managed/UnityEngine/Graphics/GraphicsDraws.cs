using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>What RenderMesh and its kin draw with: the material, the layer and camera, the
    /// property block, shadow and probe settings.</summary>
    public partial struct RenderParams
    {
        public RenderParams(Material mat)
        {
            layer = 0;
            renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask;
            rendererPriority = 0;
            instanceID = 0;
            worldBounds = new Bounds(Vector3.zero, Vector3.zero);
            camera = null;
            motionVectorMode = MotionVectorGenerationMode.Camera;
            reflectionProbeUsage = ReflectionProbeUsage.Off;
            material = mat;
            matProps = null;
            shadowCastingMode = ShadowCastingMode.Off;
            receiveShadows = false;
            lightProbeUsage = LightProbeUsage.Off;
            overrideSceneCullingMask = false;
            sceneCullingMask = 0;
        }

        public int layer { get; set; }
        public uint renderingLayerMask { get; set; }
        public int rendererPriority { get; set; }
        public int instanceID { get; set; }
        public Bounds worldBounds { get; set; }
        public Camera camera { get; set; }
        public MotionVectorGenerationMode motionVectorMode { get; set; }
        public ReflectionProbeUsage reflectionProbeUsage { get; set; }
        public Material material { get; set; }
        public MaterialPropertyBlock matProps { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public LightProbeUsage lightProbeUsage { get; set; }
        public bool overrideSceneCullingMask { get; set; }
        public ulong sceneCullingMask { get; set; }
    }

    /// <summary>
    /// Instanced and procedural draws queued for this frame's cameras. Unbound draws an instanced
    /// batch as one draw per instance, each with its matrix and, from the property block's arrays,
    /// its element of every array (what UNITY_ACCESS_INSTANCED_PROP reads).
    /// </summary>
    public partial class Graphics
    {
        internal static readonly int kMaxDrawMeshInstanceCount = 1023;

        /// <summary>The block's values for every draw of one instanced or multi-command call: its
        /// arrays copied once (<see cref="GfxStream.SharedArray"/>: the stream carries each once
        /// and the renderer gives each instance its element, GfxStream.MeshInstance), the rest as
        /// set; null for no values. Later changes to the block do not reach the queued draws.</summary>
        internal static Dictionary<int, object> InstanceProperties(MaterialPropertyBlock block)
        {
            if (block == null || block.isEmpty) return null;
            var values = new Dictionary<int, object>(block.m_Values.Count + 1);
            foreach (var p in block.m_Values) values[p.Key] = (object)GfxStream.SharedArray.Of(p.Value) ?? p.Value;
            return values;
        }

        static readonly int s_MeshInstanceBatchLimits = Shader.PropertyToID("<MeshInstanceBatchLimits>");

        // The property element remains submission-relative even when the shader ID restarts
        // at a native batch boundary. The compiled pass supplies its property-buffer layout.
        static Dictionary<int, object> MeshInstanceProperties(MaterialPropertyBlock block)
        {
            var values = InstanceProperties(block) ?? new Dictionary<int, object>(1);
            var limits = Compat.MeshInstanceBatchLimits;
            if (limits.x == 0) Stubs.Ignored("Graphics mesh-instancing batch ID limits", "are unmeasured for this Unity release; logical IDs remain sequential");
            values[s_MeshInstanceBatchLimits] = limits;
            return values;
        }

        static Bounds Transformed(Bounds local, Matrix4x4 matrix)
        {
            var center = matrix.MultiplyPoint3x4(local.center);
            var e = local.extents;
            var extents = new Vector3(
                Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
            return new Bounds(center, extents * 2f);
        }

        static void CheckInstanced(Mesh mesh, int submeshIndex, Material material, int count)
        {
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("Instancing is not supported.");
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (submeshIndex < 0 || submeshIndex >= mesh.subMeshCount) throw new ArgumentOutOfRangeException(nameof(submeshIndex), "submeshIndex out of range.");
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (!material.enableInstancing) throw new InvalidOperationException("Material needs to enable instancing for use with DrawMeshInstanced.");
            if (count < 0 || count > kMaxDrawMeshInstanceCount) throw new ArgumentOutOfRangeException(nameof(count), $"Count must be in the range of 0 to {kMaxDrawMeshInstanceCount}.");
        }

        /// <summary>The instances culled and sorted as a group ("Unity culls and sorts instanced
        /// Meshes as a group. It creates an axis-aligned bounding box that contains all the
        /// Meshes, calculates the center point, then uses this information to cull and sort the
        /// Mesh instances", DrawMeshInstanced 6000.0): every instance carries the box around the
        /// mesh's bounds through each drawn matrix as its world bounds, so cameras keep or cull
        /// them together and sort them at its centre, in the call's order among themselves (Unbound's
        /// choice, unmeasured; "does
        /// not further cull individual instances ... does not sort individual instances"). Each
        /// instance is still lit, probed and shadowed as before.</summary>
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage)
        {
            if (matrices == null) throw new ArgumentNullException(nameof(matrices));
            count = Math.Min(count, matrices.Length);
            CheckInstanced(mesh, submeshIndex, material, count);
            var lighting = ImmediateLighting.Of(castShadows, receiveShadows, lightProbeUsage);
            // The box around every drawn instance's mesh bounds (the union of each instance's
            // bounds through its matrix: Unbound's choice of how the box is built, unmeasured).
            var bounds = default(Bounds);
            if (count > 0)
            {
                bounds = Transformed(mesh.bounds, matrices[0]);
                for (int i = 1; i < count; i++) bounds.Encapsulate(Transformed(mesh.bounds, matrices[i]));
            }
            if (lightProbeUsage == LightProbeUsage.BlendProbes && count > 0)
            {
                // One probe lights the whole batch: the one at the instances' bounds centre
                // (measured: tests/projects/lightprobes, a row of instances all lit alike).
                lighting = new ImmediateLighting { Cast = lighting.Cast, Receive = lighting.Receive, Probes = lighting.Probes, Reflections = lighting.Reflections };
                lighting.ProbePosition = bounds.center;
            }
            int queued = Immediate.Queued.Count;
            var values = MeshInstanceProperties(properties);
            for (int i = 0; i < count; i++)
                Immediate.Queue(mesh, matrices[i], material, layer, camera, submeshIndex, values, new DrawSetup { Instances = 1, MeshInstance = (uint)i }, lighting);
            // The group's sort key is the box's centre, ties within the call kept in call order
            // (Unbound's choice, unmeasured).
            WithWorldBounds(queued, bounds);
        }

        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, receiveShadows, layer, camera, LightProbeUsage.BlendProbes);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, receiveShadows, layer, null);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, receiveShadows, 0);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) => DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, castShadows, true);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, properties, ShadowCastingMode.On);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, count, null);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, matrices?.Length ?? 0);

        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage)
        {
            if (matrices == null) throw new ArgumentNullException(nameof(matrices));
            DrawMeshInstanced(mesh, submeshIndex, material, matrices.ToArray(), matrices.Count, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage);
        }

        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, receiveShadows, layer, camera, LightProbeUsage.BlendProbes);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, receiveShadows, layer, null);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, receiveShadows, 0);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows) => DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, castShadows, true);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, properties, ShadowCastingMode.On);
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices) =>
            DrawMeshInstanced(mesh, submeshIndex, material, matrices, null);

        // ------------------------------------------------------------------ RenderMesh

        public static void RenderMesh(in RenderParams rparams, Mesh mesh, int submeshIndex, Matrix4x4 objectToWorld, Matrix4x4? prevObjectToWorld = null)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            Render(rparams, mesh, submeshIndex, objectToWorld, null);
        }

        /// <summary>A RenderParams draw queued for this frame's cameras: its material, layer, camera,
        /// property block (or <paramref name="properties"/> in its place), shadow and probe
        /// settings; <paramref name="setup"/> for instanced or indirect draws. Set world bounds go
        /// with the draw: cameras cull and sort it by them ("Used to cull and sort the rendered
        /// geometry", RenderParams.worldBounds), each draw of one call alike.</summary>
        static void Render(in RenderParams rparams, Mesh mesh, int submeshIndex, Matrix4x4 objectToWorld, DrawSetup? setup, Dictionary<int, object> properties = null)
        {
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            int queued = Immediate.Queued.Count;
            Immediate.Queue(mesh, objectToWorld, rparams.material, rparams.layer, rparams.camera, submeshIndex, properties ?? Immediate.Values(rparams.matProps), setup, Lighting(rparams));
            // Bounds left at the constructor's zero value are not set: the draw keeps its mesh's
            // bounds (Unbound's choice; a RenderMesh with them is drawn, measured:
            // tests/projects/graphics renderMesh).
            if (Immediate.Queued.Count == queued + 1 && !rparams.worldBounds.Equals(default(Bounds)))
            {
                var entry = Immediate.Queued[queued];
                entry.WorldBounds = rparams.worldBounds;
                Immediate.Queued[queued] = entry;
            }
            WithRenderingLayerMask(queued, rparams.renderingLayerMask);
        }

        /// <summary>A RenderParams call's renderingLayerMask on every draw it queued (from
        /// <paramref name="from"/> on): what scriptable pipeline renderer lists filter them by.</summary>
        static void WithRenderingLayerMask(int from, uint mask)
        {
            for (int i = from; i < Immediate.Queued.Count; i++)
            {
                var entry = Immediate.Queued[i];
                entry.RenderingLayerMask = mask;
                Immediate.Queued[i] = entry;
            }
        }

        static ImmediateLighting Lighting(in RenderParams rparams) =>
            ImmediateLighting.Of(rparams.shadowCastingMode, rparams.receiveShadows, rparams.lightProbeUsage, null, rparams.reflectionProbeUsage);

        /// <summary>Instances whose data starts with their object-to-world matrix (Matrix4x4 or a
        /// struct led by one). Cameras cull and sort all the instances of the call by one set of
        /// bounds, as one entity ("Unity automatically calculates bounds for all the instances of
        /// this Mesh unless you override the bounds using RenderParams.worldBounds. Unity uses the
        /// bounds to cull and sort all the instances of this Mesh as a single entity",
        /// RenderMeshInstanced 6000.0): set world bounds, or, when they are left at the
        /// constructor's zero value, the box around every drawn instance's mesh bounds. Each
        /// instance is still lit, probed and shadowed as before.</summary>
        static void RenderInstances(in RenderParams rparams, Mesh mesh, int submeshIndex, ReadOnlySpan<byte> data, int stride, int instanceCount, int startInstance)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            if (!rparams.material.enableInstancing) throw new InvalidOperationException("Material needs to enable instancing for use with RenderMeshInstanced.");
            if (stride < 64) throw new InvalidOperationException("The instance data must start with an object-to-world Matrix4x4.");
            int available = data.Length / stride;
            if (instanceCount < 0) instanceCount = available - startInstance;
            if (startInstance < 0 || startInstance + instanceCount > available) throw new ArgumentOutOfRangeException(nameof(startInstance), "instance range out of the data's bounds");
            var lighting = Lighting(rparams);
            // Unset world bounds: the box around each drawn instance's mesh bounds through its
            // matrix (Unbound's choice of how the automatic bounds are built, unmeasured).
            bool automatic = rparams.worldBounds.Equals(default(Bounds));
            var bounds = default(Bounds);
            int queued = Immediate.Queued.Count;
            var values = MeshInstanceProperties(rparams.matProps);
            for (int i = 0; i < instanceCount; i++)
            {
                var matrix = System.Runtime.InteropServices.MemoryMarshal.Read<Matrix4x4>(data.Slice((startInstance + i) * stride, 64));
                Immediate.Queue(mesh, matrix, rparams.material, rparams.layer, rparams.camera, submeshIndex, values, new DrawSetup { Instances = 1, MeshInstance = (uint)i }, lighting);
                if (!automatic) continue;
                if (i == 0) bounds = Transformed(mesh.bounds, matrix);
                else bounds.Encapsulate(Transformed(mesh.bounds, matrix));
            }
            // The entity's sort key is the bounds' centre, ties within the call kept in call
            // order (Unbound's choice, unmeasured).
            WithWorldBounds(queued, automatic ? bounds : rparams.worldBounds);
            WithRenderingLayerMask(queued, rparams.renderingLayerMask);
        }

        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, T[] instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            if (instanceData == null) throw new ArgumentNullException(nameof(instanceData));
            RenderInstances(rparams, mesh, submeshIndex, System.Runtime.InteropServices.MemoryMarshal.AsBytes(instanceData.AsSpan()), UnsafeUtility.SizeOf<T>(), instanceCount, startInstance);
        }

        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, List<T> instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            if (instanceData == null) throw new ArgumentNullException(nameof(instanceData));
            RenderInstances(rparams, mesh, submeshIndex, System.Runtime.InteropServices.MemoryMarshal.AsBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(instanceData)), UnsafeUtility.SizeOf<T>(), instanceCount, startInstance);
        }

        public static unsafe void RenderMeshInstanced<T>(RenderParams rparams, Mesh mesh, int submeshIndex, NativeArray<T> instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged
        {
            int size = UnsafeUtility.SizeOf<T>();
            var bytes = new ReadOnlySpan<byte>(NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(instanceData), instanceData.Length * size);
            RenderInstances(rparams, mesh, submeshIndex, bytes, size, instanceCount, startInstance);
        }

        // ------------------------------------------------------------------ RenderPrimitives, RenderMeshIndirect, RenderMeshPrimitives

        /// <summary>The RenderParams settings these draws do not act on yet, each warned once:
        /// non-default rendering layer mask, renderer priority and motion vector mode. The world
        /// bounds are honoured (cameras cull and sort the draw by them in FrameBuilder.CollectQueued), so
        /// they are not warned.
        /// The instance ID (Scene view picking) and the Scene culling mask are Editor scene-view
        /// settings a game's cameras do not observe.</summary>
        static void WarnUnhonoured(in RenderParams rparams, string method)
        {
            uint defaultMask = RenderingLayerMask.defaultRenderingLayerMask;
            if (rparams.renderingLayerMask != defaultMask) Stubs.Ignored("Graphics.Render* RenderParams.renderingLayerMask");
            if (rparams.rendererPriority != 0) Stubs.Ignored("Graphics.Render* RenderParams.rendererPriority");
            if (rparams.motionVectorMode != MotionVectorGenerationMode.Camera) Stubs.Ignored("Graphics.Render* RenderParams.motionVectorMode");
        }

        /// <summary>Non-indexed primitives without a mesh (the shader builds them from SV_VertexID
        /// and SV_InstanceID): DrawProcedural with RenderParams.</summary>
        public static void RenderPrimitives(in RenderParams rparams, MeshTopology topology, int vertexCount, int instanceCount = 1)
        {
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            WarnUnhonoured(rparams, nameof(RenderPrimitives));
            if (vertexCount <= 0 || instanceCount <= 0) return;
            Render(rparams, ProceduralMeshes.Get(topology, vertexCount), 0, Matrix4x4.identity,
                instanceCount > 1 ? new DrawSetup { Instances = (uint)instanceCount } : (DrawSetup?)null);
        }

        /// <summary>Instances of a mesh's submesh whose per-instance data the shader reads itself
        /// (SV_InstanceID): DrawMeshInstancedProcedural with RenderParams.</summary>
        public static void RenderMeshPrimitives(in RenderParams rparams, Mesh mesh, int submeshIndex, int instanceCount = 1)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            WarnUnhonoured(rparams, nameof(RenderMeshPrimitives));
            if (instanceCount <= 0) return;
            Render(rparams, mesh, submeshIndex, Matrix4x4.identity, new DrawSetup { Instances = (uint)instanceCount });
        }

        /// <summary>Non-indexed primitives whose counts the GPU reads from
        /// <paramref name="commandBuffer"/>'s GraphicsBuffer.IndirectDrawArgs (16 bytes each):
        /// commands <paramref name="startCommand"/> to startCommand + commandCount − 1, one
        /// DrawProceduralIndirect each.</summary>
        public static void RenderPrimitivesIndirect(in RenderParams rparams, MeshTopology topology, GraphicsBuffer commandBuffer, int commandCount = 1, int startCommand = 0)
        {
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            RenderCommands(rparams, nameof(RenderPrimitivesIndirect), ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices),
                commandBuffer, commandCount, startCommand, false);
        }

        /// <summary>Instances of a mesh whose counts, first index and base vertex (into the whole
        /// mesh's indices, as Mesh.GetIndexStart and GetBaseVertex give them) the GPU reads from
        /// <paramref name="commandBuffer"/>'s GraphicsBuffer.IndirectDrawIndexedArgs (20 bytes
        /// each): one DrawMeshInstancedIndirect per command.</summary>
        public static void RenderMeshIndirect(in RenderParams rparams, Mesh mesh, GraphicsBuffer commandBuffer, int commandCount = 1, int startCommand = 0)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (rparams.material == null) throw new ArgumentNullException("rparams.material");
            RenderCommands(rparams, nameof(RenderMeshIndirect), mesh, commandBuffer, commandCount, startCommand, true);
        }

        /// <summary>One indirect draw per command, its arguments at the command's offset. The
        /// shader sees the command buffer as <c>unity_IndirectDrawArgs</c> and the command's index
        /// as <c>unity_BaseCommandID</c>, the names UnityIndirect.cginc's InitIndirectDrawArgs and
        /// GetCommandID read (each command is its own draw, so its draw ID is 0).</summary>
        static void RenderCommands(in RenderParams rparams, string method, Mesh mesh, GraphicsBuffer commandBuffer, int commandCount, int startCommand, bool indexed)
        {
            if (commandBuffer == null) throw new ArgumentNullException(nameof(commandBuffer));
            if ((commandBuffer.target & GraphicsBuffer.Target.IndirectArguments) == 0)
                throw new ArgumentException($"Graphics.{method}: the command buffer must be created with GraphicsBuffer.Target.IndirectArguments.", nameof(commandBuffer));
            CheckCommands(method, commandBuffer, commandCount, startCommand, indexed ? GraphicsBuffer.IndirectDrawIndexedArgs.size : GraphicsBuffer.IndirectDrawArgs.size);
            WarnUnhonoured(rparams, method);
            QueueCommands(rparams, mesh, commandBuffer, commandCount, startCommand,
                new DrawSetup { Instances = 1, Args = commandBuffer.Handle, Indexed = indexed });
        }

        /// <summary>The argument checks of the indirect RenderParams draws: commands
        /// <paramref name="startCommand"/> to startCommand + commandCount − 1 of
        /// <paramref name="size"/> bytes each must lie in the command buffer.</summary>
        static void CheckCommands(string method, GraphicsBuffer commandBuffer, int commandCount, int startCommand, int size)
        {
            if (commandCount < 0) throw new ArgumentOutOfRangeException(nameof(commandCount), $"Graphics.{method}: commandCount must not be negative.");
            if (startCommand < 0) throw new ArgumentOutOfRangeException(nameof(startCommand), $"Graphics.{method}: startCommand must not be negative.");
            long bytes = (long)commandBuffer.count * commandBuffer.stride;
            if (((long)startCommand + commandCount) * size > bytes)
                throw new ArgumentOutOfRangeException(nameof(commandCount),
                    $"Graphics.{method}: commands {startCommand} to {(long)startCommand + commandCount - 1} of {size} bytes each do not fit the command buffer's {bytes} bytes.");
        }

        static readonly int s_IndirectDrawArgs = Shader.PropertyToID("unity_IndirectDrawArgs");

        /// <summary>One draw of <paramref name="setup"/> per command, each with its arguments'
        /// offset and its command index; the property block is copied once for all of them.</summary>
        static void QueueCommands(in RenderParams rparams, Mesh mesh, GraphicsBuffer commandBuffer, int commandCount, int startCommand, DrawSetup setup)
        {
            if (commandCount == 0) return;
            int size = setup.Indexed ? GraphicsBuffer.IndirectDrawIndexedArgs.size : GraphicsBuffer.IndirectDrawArgs.size;
            var values = InstanceProperties(rparams.matProps) ?? new Dictionary<int, object>(1);
            values[s_IndirectDrawArgs] = commandBuffer;
            for (int i = 0; i < commandCount; i++)
            {
                int command = startCommand + i;
                setup.Offset = checked((uint)((long)command * size));
                setup.Command = command;
                Render(rparams, mesh, 0, Matrix4x4.identity, setup, values);
            }
        }

        // ------------------------------------------------------------------ RenderPrimitivesIndexed, RenderPrimitivesIndexedIndirect

        const string k_Indexed = "UnityEngine.Graphics.RenderPrimitivesIndexed";

        /// <summary>Primitives whose SV_VertexID comes from 16- or 32-bit GPU indices.
        /// The indexed dispatch binds no vertex buffer for procedural shaders, so indices are
        /// not limited by the metadata mesh's vertex count.</summary>
        public static void RenderPrimitivesIndexed(in RenderParams rparams, MeshTopology topology, GraphicsBuffer indexBuffer, int indexCount, int startIndex = 0, int instanceCount = 1)
        {
            const string method = "Graphics.RenderPrimitivesIndexed";
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            if (startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex), $"{method}: startIndex must not be negative.");
            int stride = indexBuffer.stride;
            if ((stride == 2 || stride == 4) && indexCount > 0 && (long)startIndex + indexCount > indexBuffer.count)
                throw new ArgumentOutOfRangeException(nameof(indexCount),
                    $"{method}: indices {startIndex} to {(long)startIndex + indexCount - 1} do not fit the index buffer's {indexBuffer.count} indices.");
            if (rparams.material == null) return;
            WarnUnhonoured(rparams, nameof(RenderPrimitivesIndexed));
            if (indexCount <= 0 || instanceCount < 0) return;
            if (instanceCount == 0)
            {
                if (!Compat.ZeroIndexedInstancesDrawOne) return;
                instanceCount = 1;
            }
            if (!IndexedDraws.Supported(indexBuffer, topology, k_Indexed)) return;
            Render(rparams, IndexedDraws.StandIn(topology), 0, Matrix4x4.identity, IndexedDraws.Direct(indexBuffer, startIndex, indexCount, instanceCount));
        }

        /// <summary>Indexed GPU draws using IndirectDrawIndexedArgs and UnityIndirect.cginc.
        /// Each command reads both GPU buffers when the frame's camera executes the draw.</summary>
        public static void RenderPrimitivesIndexedIndirect(in RenderParams rparams, MeshTopology topology, GraphicsBuffer indexBuffer, GraphicsBuffer commandBuffer, int commandCount = 1, int startCommand = 0)
        {
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            if (commandBuffer == null) throw new ArgumentNullException(nameof(commandBuffer));
            if ((commandBuffer.target & GraphicsBuffer.Target.IndirectArguments) == 0)
            {
                Debug.LogError("RenderPrimitivesIndexedIndirect: GraphicsBuffer argument is missing IndirectArguments flag at creation time");
                return;
            }
            if (rparams.material == null) return;
            CheckCommands(nameof(RenderPrimitivesIndexedIndirect), commandBuffer, commandCount, startCommand, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            WarnUnhonoured(rparams, nameof(RenderPrimitivesIndexedIndirect));
            if (commandCount == 0) return;
            if (!IndexedDraws.Supported(indexBuffer, topology, k_Indexed + "Indirect")) return;
            QueueCommands(rparams, IndexedDraws.StandIn(topology), commandBuffer, commandCount, startCommand,
                IndexedDraws.Indirect(indexBuffer, commandBuffer.Handle, 0));
        }

        // ------------------------------------------------------------------ procedural

        /// <summary>Procedural primitives culled and sorted by <paramref name="bounds"/> ("The
        /// bounding volume surrounding the instances you intend to draw") as one entity
        /// (inferred from DrawMeshInstanced's "culls and sorts instanced Meshes as a group" and the replacements' "cull and sort ... as a single entity"; unmeasured); bounds equal to default(Bounds) are not set (see WithWorldBounds).</summary>
        public static void DrawProcedural(Material material, Bounds bounds, MeshTopology topology, int vertexCount, int instanceCount = 1, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (vertexCount <= 0 || instanceCount <= 0) return;
            int queued = Immediate.Queued.Count;
            Immediate.Queue(ProceduralMeshes.Get(topology, vertexCount), Matrix4x4.identity, material, layer, camera, 0, properties,
                instanceCount > 1 ? new DrawSetup { Instances = (uint)instanceCount } : (DrawSetup?)null, ImmediateLighting.Of(castShadows, receiveShadows));
            WithWorldBounds(queued, bounds);
        }

        // ------------------------------------------------------------------ DrawTexture

        static Mesh s_TextureQuad;
        static Material s_TextureMaterial;
        static bool s_WarnedBorders;

        /// <summary>A textured quad over <paramref name="screenRect"/> in the current GL matrices
        /// (GL.LoadPixelMatrix's pixels): the texture's top at the rect's y (GUI's orientation,
        /// measured), <paramref name="sourceRect"/> its part, the color neutral at 0.5 (doubled).</summary>
        static void DrawTextureImpl(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder,
            Color color, Material mat, int pass)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if ((leftBorder | rightBorder | topBorder | bottomBorder) != 0 && !s_WarnedBorders)
            {
                s_WarnedBorders = true;
                Debug.LogWarning("Unbound: Graphics.DrawTexture borders are not drawn yet; the whole rect is.");
            }
            if (s_TextureQuad == null)
            {
                s_TextureQuad = new Mesh { name = "Unbound DrawTexture" };
                s_TextureQuad.vertices = new Vector3[4];
                s_TextureQuad.uv = new Vector2[4];
                s_TextureQuad.colors = new Color[4];
                s_TextureQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            }
            s_TextureQuad.vertices = new[]
            {
                new Vector3(screenRect.xMin, screenRect.yMin, 0f), new Vector3(screenRect.xMin, screenRect.yMax, 0f),
                new Vector3(screenRect.xMax, screenRect.yMax, 0f), new Vector3(screenRect.xMax, screenRect.yMin, 0f),
            };
            s_TextureQuad.uv = new[]
            {
                new Vector2(sourceRect.xMin, sourceRect.yMax), new Vector2(sourceRect.xMin, sourceRect.yMin),
                new Vector2(sourceRect.xMax, sourceRect.yMin), new Vector2(sourceRect.xMax, sourceRect.yMax),
            };
            var tint = color * 2f;
            s_TextureQuad.colors = new[] { tint, tint, tint, tint };
            var material = mat;
            if (material == null)
            {
                if (s_TextureMaterial == null) s_TextureMaterial = new Material(Shader.Find("UI/Default")) { hideFlags = HideFlags.HideAndDontSave };
                material = s_TextureMaterial;
            }
            material.mainTexture = texture;
            material.SetPass(pass < 0 ? 0 : pass);
            DrawMeshNow(s_TextureQuad, Matrix4x4.identity);
        }

        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Color color, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, color, mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Color color, Material mat) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, color, mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Color color) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, color, null, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Rect sourceRect, int leftBorder, int rightBorder, int topBorder, int bottomBorder) =>
            DrawTextureImpl(screenRect, texture, sourceRect, leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), null, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), leftBorder, rightBorder, topBorder, bottomBorder, new Color(0.5f, 0.5f, 0.5f, 0.5f), null, -1);
        public static void DrawTexture(Rect screenRect, Texture texture, Material mat, int pass) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, pass);
        public static void DrawTexture(Rect screenRect, Texture texture, Material mat) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), mat, -1);
        public static void DrawTexture(Rect screenRect, Texture texture) =>
            DrawTextureImpl(screenRect, texture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f), null, -1);
    }
}
