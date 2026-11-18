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
        internal const int SubMeshWords = 3;

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
        /// or no mask is not one the graph compiler writes. An output of several meshes compiles
        /// to one task per mesh, each drawing the list of the particles whose mesh index names it
        /// (tests/projects/vfxlit, `mesh pair`); meshes chosen by the size a particle takes on
        /// screen are not drawn (<see cref="LodRefusal"/>): which the Editor draws differs from
        /// one run to the next (`mesh lod` keeps no image), so it is not measured.</summary>
        static string MeshRefusal(in VFXEditorTaskDesc task)
        {
            if (Parameter(task, MeshParameter, -1) < 0 || Parameter(task, SubMeshMaskParameter, -1) < 0) return "its task names no mesh or no submesh mask";
            return null;
        }

        internal const string LodRefusal = "its meshes are chosen by their size on screen, which is not measured yet";

        /// <summary>The define the graph compiler writes into a system's output update when it
        /// chooses each particle's mesh by its size on screen (the sizes themselves are constants
        /// it writes into that program).</summary>
        const string LodDefine = "#define VFX_FEATURE_LOD";

        /// <summary>The lists an output update of a system writes when it chooses each particle's
        /// mesh, of several, by its size on screen (its source says so; it writes one list per
        /// mesh, `outputBuffer0`, `outputBuffer1`, ...): the buffers its output's mesh tasks draw.
        /// An update choosing among one mesh only culls by size, which is drawn.</summary>
        static List<int> ListsChosenBySize(in VFXEditorSystemDesc system, VFXShaderSourceDesc[] sources)
        {
            List<int> lists = null;
            foreach (var task in system.tasks ?? Array.Empty<VFXEditorTaskDesc>())
            {
                int index = task.shaderSourceIndex;
                if (index < 0 || index >= sources.Length || !sources[index].compute
                    || sources[index].source == null || !sources[index].source.Contains(LodDefine, StringComparison.Ordinal)) continue;
                int written = 0;
                foreach (var buffer in task.buffers ?? Array.Empty<VFXMapping>())
                    if (buffer.name.StartsWith(OutputListPrefix, StringComparison.Ordinal)) written++;
                if (written < 2) continue;
                foreach (var buffer in task.buffers ?? Array.Empty<VFXMapping>())
                    if (buffer.name.StartsWith(OutputListPrefix, StringComparison.Ordinal)) (lists ??= new List<int>()).Add(buffer.index);
            }
            return lists;
        }

        /// <summary>The names of the lists an output update writes, one per mesh.</summary>
        const string OutputListPrefix = "outputBuffer";

        /// <summary>Computes the arguments of each submesh the mask names from the list's count
        /// in <paramref name="counts"/>, each entry described by its submesh as the mesh has it
        /// now. Every entry is reserved before any is filled: a fill after a reservation may make
        /// the entries' buffer anew, losing what was filled into the one before (draws collected
        /// earlier keep the old one: <see cref="VFXRetiredBuffers"/>). False when they cannot
        /// be.</summary>
        bool FillMeshArguments(VFXIndirectArgs arguments, GraphicsBuffer counts)
        {
            var values = m_System.Effect.Values;
            if (!(values.GetObject(m_MeshExpression) is Mesh mesh) || !mesh) return true;
            uint named = values.GetWord(m_SubMeshMaskExpression);
            int subMeshes = Math.Min(mesh.subMeshCount, MaskBits);
            uint mask = named;
            for (int subMesh = 0; subMesh < subMeshes && mask != 0; subMesh++, mask >>= 1)
                if ((mask & 1) != 0) MeshEntry(arguments, mesh, subMesh, m_MeshEntries, m_MeshDescribed);
            mask = named;
            for (int subMesh = 0; subMesh < subMeshes && mask != 0; subMesh++, mask >>= 1)
                if ((mask & 1) != 0 && !arguments.Fill(m_MeshEntries[subMesh], counts, m_CountPairs)) return false;
            return true;
        }

        /// <summary>The entry of arguments of a submesh, reserved or described anew when the
        /// submesh is not the one it was described by (<paramref name="entries"/> by submesh,
        /// <paramref name="described"/> <see cref="SubMeshWords"/> per submesh: an output's).</summary>
        internal static void MeshEntry(VFXIndirectArgs arguments, Mesh mesh, int subMesh, int[] entries, uint[] described)
        {
            // The indices as the renderer holds them, not as SetSubMesh placed them.
            var (first, count, baseVertex) = Uploaded(mesh, subMesh);
            int at = subMesh * SubMeshWords;
            int entry = entries[subMesh];
            if (entry < 0) entry = entries[subMesh] = arguments.Reserve(VFXIndirectArgs.Kind.Meshes, count, first, baseVertex);
            else if (described[at] != count || described[at + 1] != first || described[at + 2] != baseVertex)
                arguments.Describe(entry, VFXIndirectArgs.Kind.Meshes, count, first, baseVertex);
            else return;
            described[at] = count;
            described[at + 1] = first;
            described[at + 2] = baseVertex;
        }

        /// <summary>The range of a submesh's indices an indirect draw of it reads: as the
        /// renderer holds them (<see cref="Mesh.UploadedIndexRange"/>).</summary>
        static (uint First, uint Count, uint BaseVertex) Uploaded(Mesh mesh, int subMesh)
        {
            var range = mesh.UploadedIndexRange(subMesh);
            return (range.Start, range.Count, (uint)range.BaseVertex);
        }

        /// <summary>Whether each submesh the mask names has an entry described by that submesh
        /// as the mesh has it now: a script may change the mesh or the mask in a frame where
        /// the system takes no step (paused), whose lists are not made for the camera again.</summary>
        bool MeshArgumentsCurrent()
        {
            var values = m_System.Effect.Values;
            if (!(values.GetObject(m_MeshExpression) is Mesh mesh) || !mesh) return true;
            uint mask = values.GetWord(m_SubMeshMaskExpression);
            int subMeshes = Math.Min(mesh.subMeshCount, MaskBits);
            for (int subMesh = 0; subMesh < subMeshes && mask != 0; subMesh++, mask >>= 1)
            {
                if ((mask & 1) == 0) continue;
                if (m_MeshEntries[subMesh] < 0) return false;
                var (first, count, baseVertex) = Uploaded(mesh, subMesh);
                int at = subMesh * SubMeshWords;
                if (m_MeshDescribed[at] != count || m_MeshDescribed[at + 1] != first || m_MeshDescribed[at + 2] != baseVertex) return false;
            }
            return true;
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
