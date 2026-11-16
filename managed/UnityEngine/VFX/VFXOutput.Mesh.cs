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
    /// draw; the vertex program finds its particle by the instance's number. The mesh and the
    /// mask are values of the effect (an exposed property may give them). Matches the Unity
    /// 6000.0.71f1 Editor (tests/projects/vfxlit, `draw-mesh*`).
    /// </summary>
    internal sealed partial class VFXOutput
    {
        /// <summary>The parameters of a mesh output's task that name the expressions of its
        /// mesh and of the mask of submeshes drawn. An output of several meshes, or of meshes
        /// chosen by distance, compiles to one such task per mesh, each drawn through an
        /// indirect buffer: those are refused as indirect outputs are, and whoever draws them
        /// finds each task's particles by its `indirectIndex`.</summary>
        const string MeshParameter = "mesh", SubMeshMaskParameter = "subMeshMask";

        readonly int m_MeshExpression, m_SubMeshMaskExpression;

        /// <summary>Why a mesh output is not drawn, null when it is: a task that names no
        /// mesh or no mask is not one the graph compiler writes.</summary>
        static string MeshRefusal(in VFXEditorTaskDesc task) =>
            Parameter(task, MeshParameter, -1) < 0 || Parameter(task, SubMeshMaskParameter, -1) < 0 ? "its task names no mesh or no submesh mask" : null;

        /// <summary>Adds the draws of <paramref name="particles"/> instances of the output's
        /// mesh, one for each submesh of the mask.</summary>
        void CollectMeshes(Renderer renderer, uint particles, FrameBuilder.CameraContext camera, List<FrameBuilder.DrawItem> into)
        {
            var values = m_System.Effect.Values;
            if (!(values.GetObject(m_MeshExpression) is Mesh mesh) || !mesh) return;
            uint mask = values.GetWord(m_SubMeshMaskExpression);
            int subMeshes = mesh.subMeshCount;
            var model = renderer.transform.localToWorldMatrix;
            for (int subMesh = 0; subMesh < subMeshes && mask != 0; subMesh++, mask >>= 1)
                if ((mask & 1) != 0)
                    FrameBuilder.AddInstancedDraw(renderer, mesh, subMesh, m_Material, model, particles, m_Block.m_Values, camera, into);
        }
    }
}
