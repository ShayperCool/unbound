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
    /// vertex count per instance, instance count, start vertex, start instance).</summary>
    internal struct DrawSetup
    {
        internal uint Instances;
        internal ulong Args;
        internal uint Offset;
        internal bool Indexed;
    }

    internal static class IndirectArgs
    {
        internal static ulong Of(ComputeBuffer buffer) =>
            buffer != null ? buffer.Handle : throw new ArgumentNullException("bufferWithArgs");

        internal static ulong Of(GraphicsBuffer buffer) =>
            buffer != null ? buffer.Handle : throw new ArgumentNullException("bufferWithArgs");

        internal static Dictionary<int, object> Values(MaterialPropertyBlock properties) =>
            properties != null && !properties.isEmpty ? new Dictionary<int, object>(properties.m_Values) : null;

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
            var values = IndirectArgs.Values(properties);
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
            var values = IndirectArgs.Values(properties);
            var setup = new DrawSetup { Instances = 1, Args = args, Offset = (uint)argsOffset, Indexed = true };
            Add(c => IndirectArgs.Mesh(mesh, submeshIndex, material, shaderPass, setup, values));
        }

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

        /// <summary>Instances of a mesh whose per-instance data the shader reads itself (SV_InstanceID).</summary>
        public void DrawMeshInstancedProcedural(Mesh mesh, int submeshIndex, Material material, int shaderPass, int count, MaterialPropertyBlock properties = null)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (count <= 0) return;
            var values = IndirectArgs.Values(properties);
            var setup = new DrawSetup { Instances = (uint)count };
            Add(c => IndirectArgs.Mesh(mesh, submeshIndex, material, shaderPass, setup, values));
        }
    }
}

namespace UnityEngine
{
    public partial class Graphics
    {
        static void QueueMesh(Mesh mesh, int submeshIndex, Material material, MaterialPropertyBlock properties, int layer, Camera camera, DrawSetup setup,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (material == null) throw new ArgumentNullException(nameof(material));
            Immediate.Queue(mesh, Matrix4x4.identity, material, layer, camera, submeshIndex, properties, setup, ImmediateLighting.Of(castShadows, receiveShadows, lightProbeUsage));
        }

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, ComputeBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage, LightProbeProxyVolume lightProbeProxyVolume) =>
            QueueMesh(mesh, submeshIndex, material, properties, layer, camera, new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset, Indexed = true },
                castShadows, receiveShadows, lightProbeUsage);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, GraphicsBuffer bufferWithArgs, int argsOffset, MaterialPropertyBlock properties,
            ShadowCastingMode castShadows, bool receiveShadows, int layer, Camera camera, LightProbeUsage lightProbeUsage, LightProbeProxyVolume lightProbeProxyVolume) =>
            QueueMesh(mesh, submeshIndex, material, properties, layer, camera, new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset, Indexed = true },
                castShadows, receiveShadows, lightProbeUsage);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, ComputeBuffer bufferWithArgs, int argsOffset = 0, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, bounds, bufferWithArgs, argsOffset, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage, null);

        public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, Bounds bounds, GraphicsBuffer bufferWithArgs, int argsOffset = 0, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null, LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes) =>
            DrawMeshInstancedIndirect(mesh, submeshIndex, material, bounds, bufferWithArgs, argsOffset, properties, castShadows, receiveShadows, layer, camera, lightProbeUsage, null);

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, ComputeBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueMesh(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, material, properties, layer, camera,
                new DrawSetup { Instances = 1, Args = IndirectArgs.Of(bufferWithArgs), Offset = (uint)argsOffset }, castShadows, receiveShadows);

        public static void DrawProceduralIndirect(Material material, Bounds bounds, MeshTopology topology, GraphicsBuffer bufferWithArgs, int argsOffset = 0, Camera camera = null,
            MaterialPropertyBlock properties = null, ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0) =>
            QueueMesh(ProceduralMeshes.Get(topology, ProceduralMeshes.IndirectVertices), 0, material, properties, layer, camera,
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

        public static void DrawMeshInstancedProcedural(Mesh mesh, int submeshIndex, Material material, Bounds bounds, int count, MaterialPropertyBlock properties = null,
            ShadowCastingMode castShadows = ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null,
            LightProbeUsage lightProbeUsage = LightProbeUsage.BlendProbes, LightProbeProxyVolume lightProbeProxyVolume = null)
        {
            if (count <= 0) return;
            QueueMesh(mesh, submeshIndex, material, properties, layer, camera, new DrawSetup { Instances = (uint)count }, castShadows, receiveShadows, lightProbeUsage);
        }
    }
}

namespace UnityEngine
{
    /// <summary>A volume of light probes sampled as a 3D texture (draws take one as an argument;
    /// Unbound lights draws from the scene's probes).</summary>
    public sealed partial class LightProbeProxyVolume : Behaviour
    {
    }
}
