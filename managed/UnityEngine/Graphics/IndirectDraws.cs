using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>How a draw runs: <see cref="Instances"/> instances (SV_InstanceID 0..n), or with its
    /// counts read on the GPU from an arguments buffer (<see cref="Args"/>, a buffer handle) at
    /// <see cref="Offset"/> bytes: indexed (DrawMeshInstancedIndirect: index count per instance,
    /// instance count, start index, base vertex, start instance) or not (DrawProceduralIndirect:
    /// vertex count per instance, instance count, start vertex, start instance). A draw may read
    /// <see cref="IndexCount"/> indices of <see cref="IndexSize"/> bytes (2, or 4 from render
    /// protocol 10) from <see cref="FirstIndex"/> of buffer <see cref="Indices"/> (a buffer
    /// handle; 0: its mesh's own) with base vertex 0, or an indirect one the arguments' range of
    /// them (render protocol 8; see <see cref="IndexedDraws"/>).</summary>
    internal struct DrawSetup
    {
        internal uint Instances;
        /// <summary>The draw is this instance of a mesh-instancing call replayed as one draw per
        /// instance: its logical instance ID, and the element of the draw's property arrays its
        /// scalar uniforms take (GfxStream.MeshInstance).</summary>
        internal uint? MeshInstance;
        /// <summary>The indirect command an indirect draw runs (its <c>unity_BaseCommandID</c>,
        /// what UnityIndirect.cginc's GetCommandID reads); null: none.</summary>
        internal int? Command;
        internal ulong Args;
        internal uint Offset;
        internal bool Indexed;
        internal ulong Indices;
        internal uint FirstIndex;
        internal uint IndexCount;
        internal byte IndexSize;
    }

    /// <summary>Procedural draws whose vertex ids come from a GraphicsBuffer's indices ("a draw
    /// call on the GPU, without a vertex buffer", the indexed CommandBuffer.DrawProcedural
    /// 6000.0): the stand-in mesh of <see cref="StandInVertices"/> zeroed vertices, its
    /// indices replaced by the buffer's. The renderer draws them for programs that read no vertex
    /// inputs (any 16- or 32-bit index, base vertex and indirect count) and, for programs that
    /// read some, direct 16-bit ones only (each index has a vertex); others are skipped with a
    /// warning.</summary>
    internal static class IndexedDraws
    {
        /// <summary>One vertex for each of the 2^16 values of a 16-bit index.</summary>
        internal const int StandInVertices = 1 << 16;

        internal static Mesh StandIn(MeshTopology topology) => ProceduralMeshes.Get(topology, StandInVertices);

        /// <summary>Whether Unbound draws <paramref name="indexBuffer"/>'s indices as
        /// <paramref name="topology"/>: indices of 2 or 4 bytes, as triangles, lines or points
        /// (the pipelines set no strip index format; quads are not drawn). Others are counted and
        /// warned once under <paramref name="method"/>.</summary>
        internal static bool Supported(GraphicsBuffer indexBuffer, MeshTopology topology, string method)
        {
            if (indexBuffer.stride != 2 && indexBuffer.stride != 4)
            {
                Stubs.Ignored(method + " index stride", "other than 2 or 4 bytes is not drawn");
                return false;
            }
            if (topology != MeshTopology.Triangles && topology != MeshTopology.Lines && topology != MeshTopology.Points)
            {
                string name = topology == MeshTopology.Quads || topology == MeshTopology.LineStrip ? topology.ToString() : "other";
                Stubs.Ignored(method + " MeshTopology." + name, "is not drawn yet from an index buffer");
                return false;
            }
            return true;
        }

        /// <summary><paramref name="count"/> indices from <paramref name="first"/>, per instance.</summary>
        internal static DrawSetup Direct(GraphicsBuffer indexBuffer, int first, int count, int instances) => new DrawSetup
        {
            Instances = (uint)instances, Indices = indexBuffer.Handle, IndexSize = (byte)indexBuffer.stride, FirstIndex = (uint)first, IndexCount = (uint)count,
        };

        /// <summary>The counts, first index and base vertex read on the GPU from
        /// <paramref name="args"/> at <paramref name="offset"/> bytes (five integers).</summary>
        internal static DrawSetup Indirect(GraphicsBuffer indexBuffer, ulong args, int offset) => new DrawSetup
        {
            Instances = 1, Args = args, Offset = (uint)offset, Indexed = true,
            Indices = indexBuffer.Handle, IndexSize = (byte)indexBuffer.stride, FirstIndex = 0, IndexCount = (uint)indexBuffer.count,
        };

        internal static void Draw(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, DrawSetup setup, Dictionary<int, object> properties)
        {
            var mesh = StandIn(topology);
            GfxStream.DrawSetup(setup);
            GfxStream.Draw(mesh.Sync(), 0, material.Sync(), shaderPass < 0 ? GfxStream.AllPasses : shaderPass, matrix, Color.white, properties: properties);
        }

        /// <summary>The indices a direct draw reads: the Editor draws a count past the buffer's end
        /// without an error, what lies past it adding nothing (measured: cmdbuffers
        /// cmd.indexed.arg.past); Unbound reads the buffer's.</summary>
        internal static int Count(GraphicsBuffer indexBuffer, int indexCount) => Math.Min(indexCount, indexBuffer.count);
    }

    internal static class IndirectArgs
    {
        internal static ulong Of(ComputeBuffer buffer) =>
            buffer != null ? buffer.Handle : throw new ArgumentNullException("bufferWithArgs");

        internal static ulong Of(GraphicsBuffer buffer) =>
            buffer != null ? buffer.Handle : throw new ArgumentNullException("bufferWithArgs");

        internal static void Mesh(Mesh mesh, int submeshIndex, Material material, int shaderPass, DrawSetup setup, Dictionary<int, object> values)
        {
            int submesh = Math.Min(Math.Max(0, submeshIndex), Math.Max(0, mesh.subMeshCount - 1));
            GfxStream.DrawSetup(setup);
            GfxStream.Draw(mesh.Sync(), submesh, material.Sync(), shaderPass < 0 ? GfxStream.AllPasses : shaderPass, Matrix4x4.identity, Color.white, properties: values);
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial class CommandBuffer
    {
        void ProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ulong args, int argsOffset, MaterialPropertyBlock properties)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var values = Immediate.Values(properties);
            Add(c => ProceduralMeshes.DrawIndirect(matrix, material, shaderPass, topology, args, argsOffset, values));
        }

        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            ProceduralIndirect(matrix, material, shaderPass, topology, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, null);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, 0, null);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            ProceduralIndirect(matrix, material, shaderPass, topology, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, null);
        public void DrawProceduralIndirect(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs) =>
            DrawProceduralIndirect(matrix, material, shaderPass, topology, bufferWithArgs, 0, null);

        void MeshIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ulong args, int argsOffset, MaterialPropertyBlock properties)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            var values = Immediate.Values(properties);
            var setup = new DrawSetup { Instances = 1, Args = args, Offset = (uint)argsOffset, Indexed = true };
            Add(c => IndirectArgs.Mesh(mesh, submeshIndex, material, shaderPass, setup, values));
        }

        // ------------------------------------------------------------ indexed procedural draws

        public void DrawProcedural(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int indexCount, int instanceCount, MaterialPropertyBlock properties)
        {
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            if (material == null) throw new ArgumentNullException(nameof(material));
            int count = IndexedDraws.Count(indexBuffer, indexCount);
            if (count <= 0 || instanceCount <= 0 || !IndexedDraws.Supported(indexBuffer, topology, "CommandBuffer.DrawProcedural")) return;
            var values = Immediate.Values(properties);
            var setup = IndexedDraws.Direct(indexBuffer, 0, count, instanceCount);
            Add(c => IndexedDraws.Draw(matrix, material, shaderPass, topology, setup, values));
        }

        public void DrawProcedural(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int indexCount, int instanceCount) =>
            DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount, instanceCount, null);
        public void DrawProcedural(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int indexCount) =>
            DrawProcedural(indexBuffer, matrix, material, shaderPass, topology, indexCount, 1, null);

        void IndexedIndirect(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, Func<ulong> args, int argsOffset, MaterialPropertyBlock properties)
        {
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            if (material == null) throw new ArgumentNullException(nameof(material));
            ulong handle = args();
            if (!IndexedDraws.Supported(indexBuffer, topology, "CommandBuffer.DrawProceduralIndirect")) return;
            var values = Immediate.Values(properties);
            var setup = IndexedDraws.Indirect(indexBuffer, handle, argsOffset);
            Add(c => IndexedDraws.Draw(matrix, material, shaderPass, topology, setup, values));
        }

        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            IndexedIndirect(indexBuffer, matrix, material, shaderPass, topology, () => IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset) =>
            DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, null);
        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, ComputeBuffer bufferWithArgs) =>
            DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, 0, null);
        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            IndexedIndirect(indexBuffer, matrix, material, shaderPass, topology, () => IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset) =>
            DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, argsOffset, null);
        public void DrawProceduralIndirect(GraphicsBuffer indexBuffer, Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, GraphicsBuffer bufferWithArgs) =>
            DrawProceduralIndirect(indexBuffer, matrix, material, shaderPass, topology, bufferWithArgs, 0, null);

        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            MeshIndirect(mesh, submeshIndex, material, shaderPass, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ComputeBuffer bufferWithArgs, int argsOffset) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, null);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, ComputeBuffer bufferWithArgs) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, 0, null);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties) =>
            MeshIndirect(mesh, submeshIndex, material, shaderPass, IndirectArgs.Of(bufferWithArgs), argsOffset, properties);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, GraphicsBuffer bufferWithArgs, int argsOffset) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, argsOffset, null);
        public void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, int shaderPass, GraphicsBuffer bufferWithArgs) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, shaderPass, bufferWithArgs, 0, null);

        /// <summary>Instances of a mesh, one draw each with its matrix and its element of the
        /// block's arrays (as Graphics.DrawMeshInstanced draws them), the matrices and block copied
        /// when recorded. Checked when recorded as the Editor checks (measured: cmdbuffers
        /// cmd.instanced.arg.*): a material without instancing throws then (the Scripting API says
        /// it logs when run), a count past the matrices too (up to their length: 1024 of 1100 is
        /// drawn), null matrices throw a NullReferenceException; an invalid pass logs when run. A
        /// block's single value of an instanced property sets only instance 0 in the Editor
        /// (cmd.instanced.blockscalar); the compiled variant distinguishes it from ordinary uniforms.</summary>
        public void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, int shaderPass, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties)
        {
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("DrawMeshInstanced is not supported.");
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (submeshIndex < 0 || submeshIndex >= mesh.subMeshCount) throw new ArgumentOutOfRangeException(nameof(submeshIndex), "submeshIndex out of range.");
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (!material.enableInstancing) throw new InvalidOperationException("Material needs to enable instancing for use with DrawMeshInstanced.");
            if (matrices == null) throw new NullReferenceException("Object reference not set to an instance of an object");
            if (count < 0 || count > matrices.Length) throw new ArgumentOutOfRangeException(nameof(count), $"Count must be in the range of 0 to {matrices.Length}.");
            if (count == 0) return;
            var copies = new Matrix4x4[count];
            Array.Copy(matrices, copies, count);
            var values = Graphics.InstanceProperties(properties);
            Add(c =>
            {
                if (shaderPass >= material.passCount)
                {
                    Debug.LogError($"RenderingCommandBuffer: shader {material.shader?.name}: invalid pass index {shaderPass} in DrawMeshInstanced");
                    return;
                }
                ulong meshHandle = mesh.Sync(), materialHandle = material.Sync();
                int pass = shaderPass < 0 ? GfxStream.AllPasses : shaderPass;
                for (int i = 0; i < count; i++)
                {
                    GfxStream.MeshInstance((uint)i);
                    GfxStream.Draw(meshHandle, submeshIndex, materialHandle, pass, copies[i], Color.white, properties: values);
                }
            });
        }

        public void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, int shaderPass, Matrix4x4[] matrices, int count) =>
            DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, count, null);
        public void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, int shaderPass, Matrix4x4[] matrices) =>
            DrawMeshInstanced(mesh, submeshIndex, material, shaderPass, matrices, matrices?.Length ?? 0, null);

        /// <summary>Instances of a mesh whose per-instance data the shader reads itself (SV_InstanceID).</summary>
        public void DrawMeshInstancedProcedural(Mesh mesh, int submeshIndex, Material material, int shaderPass, int count, MaterialPropertyBlock properties = null)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (count <= 0) return;
            var values = Immediate.Values(properties);
            var setup = new DrawSetup { Instances = (uint)count };
            Add(c => IndirectArgs.Mesh(mesh, submeshIndex, material, shaderPass, setup, values));
        }
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        /// <summary>One draw of <paramref name="mesh"/> queued for this frame's cameras, culled and
        /// sorted by <paramref name="bounds"/> ("The bounding volume surrounding the instances you
        /// intend to draw") as one entity (inferred from DrawMeshInstanced's "culls and sorts instanced Meshes as a group" and the replacements' "cull and sort ... as a single entity"; unmeasured): see <see cref="WithWorldBounds"/>.</summary>
        static void QueueMesh(Mesh mesh, int submeshIndex, Material material, Bounds bounds, MaterialPropertyBlock properties, int layer, Camera camera, DrawSetup setup,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            int queued = Immediate.Queued.Count;
            Immediate.Queue(mesh, Matrix4x4.identity, material, layer, camera, submeshIndex, properties, setup, ImmediateLighting.Of(castShadows, receiveShadows, lightProbeUsage));
            WithWorldBounds(queued, bounds);
        }

        /// <summary>Gives the draws one call queued, from index <paramref name="from"/> on, the
        /// call's world bounds, which cameras cull and sort them by (FrameBuilder.CollectQueued):
        /// every draw of the call alike, so they are kept or culled together and sort at the
        /// bounds' centre. Bounds equal to default(Bounds) (centre and size zero) are not set and
        /// the draws keep their mesh's bounds, as RenderParams.worldBounds left at that value
        /// (Unbound's choice, unmeasured); set bounds of no size are never culled (the renderers'
        /// frustum rule; Unbound's choice, unmeasured).</summary>
        static void WithWorldBounds(int from, Bounds bounds)
        {
            if (bounds.Equals(default(Bounds))) return;
            for (int i = from; i < Immediate.Queued.Count; i++)
            {
                var entry = Immediate.Queued[i];
                entry.WorldBounds = bounds;
                Immediate.Queued[i] = entry;
            }
        }

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage, LightProbeProxyVolume lightProbeProxyVolume) =>
            QueueMesh(mesh, submeshIndex, material, bounds, properties, layer, camera, new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset, Indexed = true },
                castShadows, receiveShadows, lightProbeUsage);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage, LightProbeProxyVolume lightProbeProxyVolume) =>
            QueueMesh(mesh, submeshIndex, material, bounds, properties, layer, camera, new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset, Indexed = true },
                castShadows, receiveShadows, lightProbeUsage);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, ComputeBuffer bufferWithArgs, int argsOffset = 0, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, bounds, bufferWithArgs, argsOffset, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage, null);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, GraphicsBuffer bufferWithArgs, int argsOffset = 0, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, bounds, bufferWithArgs, argsOffset, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage, null);

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueMesh(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, material, bounds, properties, layer, camera,
                new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset }, castShadows, receiveShadows);

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueMesh(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, material, bounds, properties, layer, camera,
                new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset }, castShadows, receiveShadows);

        /// <summary>A procedural draw now, with the pass Material.SetPass set, into the active target.</summary>
        public static void DrawProceduralNow(MeshTopology topology, int vertexCount, int instanceCount = 1)
        {
            if (vertexCount <= 0 || instanceCount <= 0) return;
            if (instanceCount > 1) GfxStream.DrawSetup(new DrawSetup { Instances = (uint)instanceCount });
            Immediate.DrawNow(ProceduralMeshes.Get(topology, vertexCount), 0, Matrix4x4.identity);
        }

        public static void DrawProceduralIndirectNow(MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset = 0) =>
            ProceduralIndirectNow(topology, IndirectArgs.Of(bufferWithArgs), argsOffset);

        public static void DrawProceduralIndirectNow(MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset = 0) =>
            ProceduralIndirectNow(topology, IndirectArgs.Of(bufferWithArgs), argsOffset);

        static void ProceduralIndirectNow(MeshTopology topology, ulong args, int argsOffset)
        {
            GfxStream.DrawSetup(new DrawSetup { Instances = 1, Args = args, Offset = (uint)argsOffset });
            Immediate.DrawNow(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, Matrix4x4.identity);
        }

        // ------------------------------------------------------------ indexed procedural draws

        public static void DrawProceduralNow(MeshTopology topology, GraphicsBuffer indexBuffer, int indexCount, int instanceCount = 1)
        {
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            int count = IndexedDraws.Count(indexBuffer, indexCount);
            if (count <= 0 || instanceCount <= 0 || !IndexedDraws.Supported(indexBuffer, topology, "Graphics.DrawProceduralNow")) return;
            GfxStream.DrawSetup(IndexedDraws.Direct(indexBuffer, 0, count, instanceCount));
            Immediate.DrawNow(IndexedDraws.StandIn(topology), 0, Matrix4x4.identity);
        }

        public static void DrawProceduralIndirectNow(MeshTopology topology, GraphicsBuffer indexBuffer, ComputeBuffer bufferWithArgs, int argsOffset = 0) =>
            IndexedIndirectNow(topology, indexBuffer, () => IndirectArgs.Of(bufferWithArgs), argsOffset);
        public static void DrawProceduralIndirectNow(MeshTopology topology, GraphicsBuffer indexBuffer, GraphicsBuffer bufferWithArgs, int argsOffset = 0) =>
            IndexedIndirectNow(topology, indexBuffer, () => IndirectArgs.Of(bufferWithArgs), argsOffset);

        static void IndexedIndirectNow(MeshTopology topology, GraphicsBuffer indexBuffer, Func<ulong> args, int argsOffset)
        {
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            ulong handle = args();
            if (!IndexedDraws.Supported(indexBuffer, topology, "Graphics.DrawProceduralIndirectNow")) return;
            GfxStream.DrawSetup(IndexedDraws.Indirect(indexBuffer, handle, argsOffset));
            Immediate.DrawNow(IndexedDraws.StandIn(topology), 0, Matrix4x4.identity);
        }

        /// <summary>Indexed procedural primitives queued for this frame's cameras, culled and
        /// sorted by <paramref name="bounds"/> as the other procedural draws (unmeasured).</summary>
        public static void DrawProcedural(Material material, Bounds bounds, MeshTopology topology, GraphicsBuffer indexBuffer, int indexCount, int instanceCount = 1, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0)
        {
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            if (material == null) throw new ArgumentNullException(nameof(material));
            int count = IndexedDraws.Count(indexBuffer, indexCount);
            if (count <= 0 || instanceCount <= 0 || !IndexedDraws.Supported(indexBuffer, topology, "Graphics.DrawProcedural")) return;
            QueueMesh(IndexedDraws.StandIn(topology), 0, material, bounds, properties, layer, camera, IndexedDraws.Direct(indexBuffer, 0, count, instanceCount), castShadows, receiveShadows);
        }

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, GraphicsBuffer indexBuffer, ComputeBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueIndexedIndirect(material, bounds, topology, indexBuffer, () => IndirectArgs.Of(bufferWithArgs), argsOffset, camera, properties, castShadows, receiveShadows, layer);

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, GraphicsBuffer indexBuffer, GraphicsBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueIndexedIndirect(material, bounds, topology, indexBuffer, () => IndirectArgs.Of(bufferWithArgs), argsOffset, camera, properties, castShadows, receiveShadows, layer);

        static void QueueIndexedIndirect(Material material, Bounds bounds, MeshTopology topology, GraphicsBuffer indexBuffer, Func<ulong> args, int argsOffset, Camera camera,
            MaterialPropertyBlock properties, ShadowCastingMode castShadows, bool receiveShadows, int layer)
        {
            if (indexBuffer == null) throw new ArgumentNullException(nameof(indexBuffer));
            if (material == null) throw new ArgumentNullException(nameof(material));
            ulong handle = args();
            if (!IndexedDraws.Supported(indexBuffer, topology, "Graphics.DrawProceduralIndirect")) return;
            QueueMesh(IndexedDraws.StandIn(topology), 0, material, bounds, properties, layer, camera, IndexedDraws.Indirect(indexBuffer, handle, argsOffset), castShadows, receiveShadows);
        }

        public static void DrawMeshInstancedProcedural(Mesh mesh, int submeshIndex, Material material, Bounds bounds, int count, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null,
            LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes, LightProbeProxyVolume lightProbeProxyVolume = null)
        {
            if (count <= 0) return;
            QueueMesh(mesh, submeshIndex, material, bounds, properties, layer, camera, new DrawSetup { Instances = (uint)count }, castShadows, receiveShadows, lightProbeUsage);
        }
    }
}

namespace UnityEngine
{
    /// <summary>A volume of light probes sampled as a 3D texture. Unbound keeps its settings and
    /// lights its renderers with the probes interpolated at their centers, as Blend Probes does
    /// (the same where the probes are uniform, as with the ambient probe alone; measured:
    /// tests/projects/builtinfx lppv).</summary>
    [ClassId(259)]
    public sealed partial class LightProbeProxyVolume : Behaviour
    {
        BoundingBoxMode m_BoundingBoxMode = BoundingBoxMode.AutomaticLocal;
        ResolutionMode m_ResolutionMode = ResolutionMode.Automatic;
        ProbePositionMode m_ProbePositionMode = ProbePositionMode.CellCorner;
        RefreshMode m_RefreshMode = RefreshMode.Automatic;
        QualityMode m_QualityMode = QualityMode.Normal;
        DataFormat m_DataFormat = DataFormat.HalfFloat;
        float m_ProbeDensity = 0.25f;
        int m_ResolutionX = 1, m_ResolutionY = 1, m_ResolutionZ = 1;
        Vector3 m_BoundingBoxSize = Vector3.one, m_BoundingBoxOrigin;

        public LightProbeProxyVolume() { }

        public enum ResolutionMode { Automatic = 0, Custom = 1 }
        public enum BoundingBoxMode { AutomaticLocal = 0, AutomaticWorld = 1, Custom = 2 }
        public enum ProbePositionMode { CellCorner = 0, CellCenter = 1 }
        public enum RefreshMode { Automatic = 0, EveryFrame = 1, ViaScripting = 2 }
        public enum QualityMode { Low = 0, Normal = 1 }
        public enum DataFormat { HalfFloat = 0, Float = 1 }

        /// <summary>The Editor's OpenGL Core device supports proxy volumes (measured).</summary>
        [Unbound.EditorFaithful("the Editor's OpenGL Core device supports proxy volumes (measured: tests/projects/builtinfx lppv)")]
        public static bool isFeatureSupported => true;

        public BoundingBoxMode boundingBoxMode { get => m_BoundingBoxMode; set => m_BoundingBoxMode = value; }
        public ResolutionMode resolutionMode { get => m_ResolutionMode; set => m_ResolutionMode = value; }
        public ProbePositionMode probePositionMode { get => m_ProbePositionMode; set => m_ProbePositionMode = value; }
        public RefreshMode refreshMode { get => m_RefreshMode; set => m_RefreshMode = value; }
        public QualityMode qualityMode { get => m_QualityMode; set => m_QualityMode = value; }
        public DataFormat dataFormat { get => m_DataFormat; set => m_DataFormat = value; }
        public float probeDensity { get => m_ProbeDensity; set => m_ProbeDensity = Mathf.Clamp(value, 0.0001f, 1024f); }
        public Vector3 sizeCustom { get => m_BoundingBoxSize; set => m_BoundingBoxSize = value; }
        public Vector3 originCustom { get => m_BoundingBoxOrigin; set => m_BoundingBoxOrigin = value; }

        /// <summary>The grid's cells per axis: set, or (Automatic) the bounds' size times the
        /// density, a power of two from 2 to 32 (measured: a unit sphere's is 2).</summary>
        public int gridResolutionX { get => Resolution(m_ResolutionX, 0); set => m_ResolutionX = Mathf.Clamp(value, 1, 32); }
        public int gridResolutionY { get => Resolution(m_ResolutionY, 1); set => m_ResolutionY = Mathf.Clamp(value, 1, 32); }
        public int gridResolutionZ { get => Resolution(m_ResolutionZ, 2); set => m_ResolutionZ = Mathf.Clamp(value, 1, 32); }

        int Resolution(int custom, int axis)
        {
            if (m_ResolutionMode == ResolutionMode.Custom) return custom;
            float size = boundsGlobal.size[axis];
            return Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(size * m_ProbeDensity)), 2, 32);
        }

        /// <summary>The volume's world bounds: its renderer's (automatic local and world), or the
        /// custom box around the transform.</summary>
        public Bounds boundsGlobal
        {
            get
            {
                if (m_BoundingBoxMode == BoundingBoxMode.Custom)
                    return new Bounds(transform.TransformPoint(m_BoundingBoxOrigin), Vector3.Scale(m_BoundingBoxSize, transform.lossyScale));
                var renderer = m_GameObject != null ? m_GameObject.GetComponent<Renderer>() : null;
                return renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.zero);
            }
        }

        [Unbound.EditorFaithful("Unbound samples the probes when it draws: there is no volume texture to refresh")]
        public void Update() { }

        internal override void CopyFrom(Component source, System.Func<Object, Object> remap)
        {
            var o = (LightProbeProxyVolume)source;
            (m_BoundingBoxMode, m_ResolutionMode, m_ProbePositionMode, m_RefreshMode, m_QualityMode, m_DataFormat) =
                (o.m_BoundingBoxMode, o.m_ResolutionMode, o.m_ProbePositionMode, o.m_RefreshMode, o.m_QualityMode, o.m_DataFormat);
            (m_ProbeDensity, m_ResolutionX, m_ResolutionY, m_ResolutionZ, m_BoundingBoxSize, m_BoundingBoxOrigin) =
                (o.m_ProbeDensity, o.m_ResolutionX, o.m_ResolutionY, o.m_ResolutionZ, o.m_BoundingBoxSize, o.m_BoundingBoxOrigin);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_BoundingBoxMode = (BoundingBoxMode)body.Int("m_BoundingBoxMode");
            m_ResolutionMode = (ResolutionMode)body.Int("m_ResolutionMode");
            m_ProbePositionMode = (ProbePositionMode)body.Int("m_ProbePositionMode");
            m_RefreshMode = (RefreshMode)body.Int("m_RefreshMode");
            m_QualityMode = (QualityMode)body.Int("m_QualityMode", (int)QualityMode.Normal);
            m_DataFormat = (DataFormat)body.Int("m_DataFormat", (int)DataFormat.HalfFloat);
            m_ProbeDensity = body.Float("m_ProbeDensity", 0.25f);
            m_ResolutionX = body.Int("m_ResolutionX", 1);
            m_ResolutionY = body.Int("m_ResolutionY", 1);
            m_ResolutionZ = body.Int("m_ResolutionZ", 1);
            m_BoundingBoxSize = body.Get("m_BoundingBoxSize") != null ? body.Vector3("m_BoundingBoxSize") : Vector3.one;
            m_BoundingBoxOrigin = body.Vector3("m_BoundingBoxOrigin");
        }
    }
}
