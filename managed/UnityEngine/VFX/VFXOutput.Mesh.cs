using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// An output that draws each particle as a mesh: one draw for each submesh its mask
    /// names, in the submeshes' order, of as many instances as the system has particles to
    /// draw, or, through an indirect buffer, as its list counts (arguments of each submesh
    /// computed from the list's count); the vertex program finds its particle by the
    /// instance's number. The mesh and the mask are values of the effect (an exposed property
    /// may give them). Matches the Unity 6000.0.71f1 Editor (tests/projects/vfxlit,
    /// `draw-mesh*`).
    /// </summary>
    internal sealed partial class VFXOutput
    {
        /// <summary>The parameters of a mesh output's task that name the expressions of its
        /// mesh and of the mask of submeshes drawn.</summary>
        const string MeshParameter = "mesh", SubMeshMaskParameter = "subMeshMask";

        /// <summary>The submeshes a mask can name: its bits.</summary>
        const int MaskBits = 32;

        /// <summary>The words describing a submesh to the arguments' kernel: its index count,
        /// first index and base vertex.</summary>
        const int SubMeshWords = 3;

        readonly int m_MeshExpression, m_SubMeshMaskExpression;

        /// <summary>Of a mesh output drawn through an indirect buffer: the entry of arguments of
        /// each submesh (-1: none reserved yet), and the submesh each was last described by.</summary>
        readonly int[] m_MeshEntries;
        readonly uint[] m_MeshDescribed = new uint[MaskBits * SubMeshWords];

        static int[] NoMeshEntries()
        {
            var entries = new int[MaskBits];
            Array.Fill(entries, -1);
            return entries;
        }

        void ReleaseMeshEntries()
        {
            if (m_MeshEntries == null) return;
            for (int i = 0; i < m_MeshEntries.Length; i++)
            {
                VFXIndirectArgs.Shared.Release(m_MeshEntries[i]);
                m_MeshEntries[i] = -1;
            }
        }

        /// <summary>Why a mesh output is not drawn, null when it is: a task that names no mesh
        /// or no mask is not one the graph compiler writes; an output of several meshes, or of
        /// meshes chosen by distance, compiles to one task per mesh, each with the output's
        /// shader, which is not measured.</summary>
        static string MeshRefusal(in VFXEditorSystemDesc system, in VFXEditorTaskDesc task)
        {
            if (Parameter(task, MeshParameter, -1) < 0 || Parameter(task, SubMeshMaskParameter, -1) < 0) return "its task names no mesh or no submesh mask";
            foreach (var other in system.tasks ?? Array.Empty<VFXEditorTaskDesc>())
                if (other.type == VFXTaskType.ParticleMeshOutput && other.shaderSourceIndex == task.shaderSourceIndex && !ReferenceEquals(other.parameters, task.parameters))
                    return "it is one of several meshes of an output, which is not measured yet";
            return null;
        }

        /// <summary>Computes the arguments of each submesh the mask names from the list's count
        /// in <paramref name="counts"/>, each entry described by its submesh as the mesh has it
        /// now. Every entry is reserved before any is filled: a reservation may make the
        /// entries' buffer anew, losing what was filled into the one before. False when they
        /// cannot be.</summary>
        bool FillMeshArguments(VFXIndirectArgs arguments, GraphicsBuffer counts)
        {
            var values = m_System.Effect.Values;
            if (!(values.GetObject(m_MeshExpression) is Mesh mesh) || !mesh) return true;
            uint named = values.GetWord(m_SubMeshMaskExpression);
            int subMeshes = Math.Min(mesh.subMeshCount, MaskBits);
            uint mask = named;
            for (int subMesh = 0; subMesh < subMeshes && mask != 0; subMesh++, mask >>= 1)
                if ((mask & 1) != 0) MeshEntry(arguments, mesh, subMesh);
            mask = named;
            for (int subMesh = 0; subMesh < subMeshes && mask != 0; subMesh++, mask >>= 1)
                if ((mask & 1) != 0 && !arguments.Fill(m_MeshEntries[subMesh], counts, m_CountPairs)) return false;
            return true;
        }

        /// <summary>The entry of arguments of a submesh, reserved or described anew when the
        /// submesh is not the one it was described by.</summary>
        void MeshEntry(VFXIndirectArgs arguments, Mesh mesh, int subMesh)
        {
            var descriptor = mesh.GetSubMesh(subMesh);
            uint count = (uint)descriptor.indexCount, first = (uint)descriptor.indexStart, baseVertex = (uint)descriptor.baseVertex;
            int at = subMesh * SubMeshWords;
            int entry = m_MeshEntries[subMesh];
            if (entry < 0) entry = m_MeshEntries[subMesh] = arguments.Reserve(VFXIndirectArgs.Kind.Meshes, count, first, baseVertex);
            else if (m_MeshDescribed[at] != count || m_MeshDescribed[at + 1] != first || m_MeshDescribed[at + 2] != baseVertex)
                arguments.Describe(entry, VFXIndirectArgs.Kind.Meshes, count, first, baseVertex);
            else return;
            m_MeshDescribed[at] = count;
            m_MeshDescribed[at + 1] = first;
            m_MeshDescribed[at + 2] = baseVertex;
        }

        /// <summary>Adds the draws of the output's mesh, one for each submesh of the mask: of
        /// <paramref name="particles"/> instances, or, through an indirect buffer, of the
        /// instances each submesh's arguments in <paramref name="indirect"/> count.</summary>
        void CollectMeshes(Renderer renderer, uint particles, GraphicsBuffer indirect, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var values = m_System.Effect.Values;
            if (!(values.GetObject(m_MeshExpression) is Mesh mesh) || !mesh) return;
            uint mask = values.GetWord(m_SubMeshMaskExpression);
            int subMeshes = Math.Min(mesh.subMeshCount, MaskBits);
            var model = renderer.transform.localToWorldMatrix;
            for (int subMesh = 0; subMesh < subMeshes && mask != 0; subMesh++, mask >>= 1)
            {
                if ((mask & 1) == 0) continue;
                if (indirect == null) FrameBuilder.AddInstancedDraw(renderer, mesh, subMesh, m_Material, model, particles, m_Block.m_Values, camera, into);
                else if (m_MeshEntries[subMesh] >= 0)
                    FrameBuilder.AddIndirectInstancedDraw(renderer, mesh, subMesh, m_Material, model, indirect.Handle, VFXIndirectArgs.Offset(m_MeshEntries[subMesh]), m_Block.m_Values, camera, into);
            }
        }
    }
}
