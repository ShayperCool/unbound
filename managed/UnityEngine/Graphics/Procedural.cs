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

        internal static UnityEngine.Mesh Get(UnityEngine.MeshTopology topology, int vertexCount)
        {
            if (s_Meshes.TryGetValue((topology, vertexCount), out var mesh) && mesh) return mesh;
            using (ObjectRegistry.AsAssets())
            {
                mesh = new UnityEngine.Mesh { name = $"Unbound Procedural {topology} {vertexCount}" };
                if (vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = new UnityEngine.Vector3[vertexCount];
                // Whole primitives only: triangles drop the vertices after the last whole one, as
                // a draw of them does, and the mesh accepts no partial triangle.
                var indices = new int[topology == UnityEngine.MeshTopology.Triangles ? vertexCount - vertexCount % 3 : vertexCount];
                for (int i = 0; i < indices.Length; i++) indices[i] = i;
                mesh.SetIndices(indices, topology, 0, false, 0);
                mesh.bounds = new UnityEngine.Bounds(UnityEngine.Vector3.zero, UnityEngine.Vector3.one * 1e6f);
            }
            s_Meshes[(topology, vertexCount)] = mesh;
            return mesh;
        }

        /// <summary>The vertices an indirect procedural draw can use: its count comes from the GPU.</summary>
        internal const int IndirectVertices = 65536;

        /// <summary>The most vertex ids a shared stand-in holds (<see cref="SharedStandIn"/>): its
        /// vertex buffer takes 120 bytes a vertex, 30 MB at this size.</summary>
        internal const int MaxSharedVertices = 1 << 18;

        /// <summary>A stand-in for <paramref name="count"/> vertex ids that draws of other counts
        /// share, drawn with that count (GfxStream.DrawShape): the 65536-vertex one, else one of the
        /// next power of two, so a count that changes every frame makes no mesh; null past
        /// <see cref="MaxSharedVertices"/>.</summary>
        internal static UnityEngine.Mesh SharedStandIn(UnityEngine.MeshTopology topology, uint count)
        {
            if (count > MaxSharedVertices) return null;
            int vertices = IndirectVertices;
            while (vertices < count) vertices *= 2;
            return Get(topology, vertices);
        }

        internal static void Draw(UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int shaderPass, UnityEngine.MeshTopology topology, int vertexCount, int instanceCount, Dictionary<int, object> properties)
        {
            if (vertexCount <= 0 || instanceCount <= 0) return;
            var mesh = Get(topology, vertexCount);
            if (instanceCount > 1) GfxStream.DrawSetup(new DrawSetup { Instances = (uint)instanceCount });
            GfxStream.Draw(mesh.Sync(), 0, material.Sync(), shaderPass < 0 ? GfxStream.AllPasses : shaderPass, matrix, UnityEngine.Color.white, properties: properties);
        }

        /// <summary>The Editor's error for a non-indexed indirect procedural draw of quads, logged
        /// for each draw when it is drawn; the draw is not drawn (measured on OpenGL Core and Vulkan,
        /// tests/projects/brg procedural_graphics: Graphics.RenderPrimitivesIndirect and
        /// DrawProceduralIndirect; direct procedural quads are drawn, four ids to a quad).</summary>
        internal const string IndirectQuadsError = "DrawProceduralIndirect can't draw quads if platform does not support quad topology";

        /// <summary>Whether a non-indexed indirect procedural draw of <paramref name="topology"/>
        /// is refused (quads), with <see cref="IndirectQuadsError"/>.</summary>
        internal static bool RefusesIndirect(UnityEngine.MeshTopology topology)
        {
            if (topology != UnityEngine.MeshTopology.Quads) return false;
            UnityEngine.Debug.LogError(IndirectQuadsError);
            return true;
        }

        /// <summary>Whether a queued draw is a non-indexed indirect procedural draw of quads, which
        /// no list draws (a camera's that would draw it logs <see cref="IndirectQuadsError"/>).</summary>
        internal static bool IsIndirectQuads(in Immediate.QueuedMesh q) =>
            q.Setup is DrawSetup setup && setup.Args != 0 && !setup.Indexed && setup.Indices == 0
            && s_Meshes.TryGetValue((UnityEngine.MeshTopology.Quads, IndirectVertices), out var quads) && ReferenceEquals(q.Mesh, quads);

        internal static void DrawIndirect(UnityEngine.Matrix4x4 matrix, UnityEngine.Material material, int shaderPass, UnityEngine.MeshTopology topology, ulong args, int argsOffset, Dictionary<int, object> properties)
        {
            if (RefusesIndirect(topology)) return;
            var mesh = Get(topology, IndirectVertices);
            GfxStream.DrawSetup(new DrawSetup { Instances = 1, Args = args, Offset = (uint)argsOffset });
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
            Add(c => ProceduralMeshes.Draw(matrix, material, shaderPass, topology, vertexCount, instanceCount, values), 92);
        }

        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount, int instanceCount) =>
            DrawProcedural(matrix, material, shaderPass, topology, vertexCount, instanceCount, null);

        public void DrawProcedural(Matrix4x4 matrix, Material material, int shaderPass, MeshTopology topology, int vertexCount) =>
            DrawProcedural(matrix, material, shaderPass, topology, vertexCount, 1, null);
    }
}
