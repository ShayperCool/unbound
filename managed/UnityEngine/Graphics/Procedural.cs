using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>Procedural draws (no mesh: shaders build vertices from SV_VertexID) as draws of a
    /// mesh of zeroed vertices indexed 0..n−1, so vertex ids run as Unity's do.</summary>
    internal static class ProceduralMeshes
    {
        static readonly Dictionary<(UnityEngine.MeshTopology, int), UnityEngine.Mesh> s_Meshes = new Dictionary<(UnityEngine.MeshTopology, int), UnityEngine.Mesh>();
        static bool s_WarnedInstances;

        internal static UnityEngine.Mesh Get(UnityEngine.MeshTopology topology, int vertexCount)
        {
            if (s_Meshes.TryGetValue((topology, vertexCount), out var mesh) && mesh) return mesh;
            using (ObjectRegistry.AsAssets())
            {
                mesh = new UnityEngine.Mesh { name = $"Unbound Procedural {topology} {vertexCount}" };
                if (vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = new UnityEngine.Vector3[vertexCount];
                var indices = new int[vertexCount];
                for (int i = 0; i < vertexCount; i++) indices[i] = i;
                mesh.SetIndices(indices, topology, 0, false, 0);
                mesh.bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one * 1e6f);
            }
            s_Meshes[(topology, vertexCount)] = mesh;
            return mesh;
        }

        internal static void Draw(UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int shaderPass, UnityEngine.MeshTopology topology, int vertexCount, int instanceCount, Dictionary<int, object> properties)
        {
            if (vertexCount <= 0 || instanceCount <= 0) return;
            if (instanceCount > 1 && !s_WarnedInstances)
            {
                s_WarnedInstances = true;
                UnityEngine.Debug.LogWarning("Unbound: instanced procedural draws are not supported yet; the first instance is drawn.");
            }
            var mesh = Get(topology, vertexCount);
            GfxStream.Draw(mesh.Sync(), 0, material.Sync(), shaderPass < 0 ? GfxStream.AllPasses : shaderPass, matrix, UnityEngine.Color.white, properties: properties);
        }
    }
}

namespace UnityEngine.Rendering
{
    public partial class CommandBuffer
    {
        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount, int instanceCount, MaterialPropertyBlock properties)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var values = properties != null && !properties.isEmpty ? new Dictionary<int, object>(properties.m_Values) : null;
            Add(c => ProceduralMeshes.Draw(matrix, material, shaderPass, topology, vertexCount, instanceCount, values));
        }

        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount, int instanceCount) =>
            DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount, null);

        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount) =>
            DrawProcedural(matrix, material, shaderPass, topology, vertexCount, 1, null);
    }
}
