using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>Editor mesh utilities. A mesh's compression setting is kept with the mesh: a new
    /// mesh has none (Off), and the setting reads back as set (matches the Unity 6000.0.71f1
    /// Editor, tests/projects/graphicsgaps meshUtility.compression*); it changes what an asset
    /// saved from the mesh stores, not the mesh's data in memory.</summary>
    public partial class MeshUtility
    {
        [Unbound.EditorFaithful("Unity's utility class has a public default constructor that sets nothing")]
        public MeshUtility() { }

        public static void SetMeshCompression(Mesh mesh, ModelImporterMeshCompression compression)
        {
            if (mesh == null) throw new System.ArgumentNullException(nameof(mesh));
            mesh.m_MeshCompression = compression;
        }

        public static ModelImporterMeshCompression GetMeshCompression(Mesh mesh)
        {
            if (mesh == null) throw new System.ArgumentNullException(nameof(mesh));
            return mesh.m_MeshCompression;
        }

        /// <summary>Mesh.AcquireReadOnlyMeshData without its Read/Write check: the Editor reads
        /// any mesh's data (Unity's C# passes checkReadWrite false).</summary>
        public static Mesh.MeshDataArray AcquireReadOnlyMeshData(Mesh mesh) => new Mesh.MeshDataArray(mesh, checkReadWrite: false);
        public static Mesh.MeshDataArray AcquireReadOnlyMeshData(Mesh[] meshes) =>
            new Mesh.MeshDataArray(meshes ?? throw new System.ArgumentNullException("meshes", "Mesh array is null"), meshes.Length, checkReadWrite: false);
        public static Mesh.MeshDataArray AcquireReadOnlyMeshData(List<Mesh> meshes) =>
            new Mesh.MeshDataArray((meshes ?? throw new System.ArgumentNullException("meshes", "Mesh list is null")).ToArray(), meshes.Count, checkReadWrite: false);
    }
}
