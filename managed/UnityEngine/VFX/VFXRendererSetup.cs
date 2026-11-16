using System;
using System.Collections.Generic;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Unbound.Runtime
{
    /// <summary>
    /// What an effect's renderer takes from the effect's asset: whether it casts shadows and
    /// whether its outputs write motion vectors, as the graph compiled (an output of the graph
    /// does either; the graph's file saves neither), whether it receives shadows, as the asset
    /// saves it, and the materials of the graph's outputs. Without an asset it does none of
    /// these and has no material. The probes it uses stay as they are. Matches the Unity
    /// 6000.0.71f1 Editor (tests/projects/vfxlit, `renderer/*`).
    /// </summary>
    internal static class VFXRendererSetup
    {
        /// <summary>The parameter of an output's task that orders it among the effect's outputs.</summary>
        const string SortPriorityParameter = "sortPriority";

        internal static void Apply(VFXRenderer renderer, VisualEffectAsset asset)
        {
            var data = asset != null ? asset.m_Data : VisualEffectCompiledData.Empty;
            renderer.shadowCastingMode = data.ShadowCastingMode;
            renderer.motionVectorGenerationMode = data.MotionVectorGenerationMode;
            renderer.receiveShadows = asset != null && asset.m_Settings.Renderer.receiveShadows;
            renderer.m_Materials = OutputMaterials(data);
        }

        /// <summary>The materials of a graph's outputs as an effect's renderer lists them: one
        /// for each task drawn with a material, in the systems' and their tasks' order, then by
        /// the outputs' sort priority, lower first (outputs of one priority keep their order).</summary>
        internal static Material[] OutputMaterials(VisualEffectCompiledData data)
        {
            List<(int Priority, Material Material)> found = null;
            for (int s = 0; s < data.Systems.Length && s < data.Processors.Length; s++)
            {
                var tasks = data.Systems[s].tasks ?? Array.Empty<VFXEditorTaskDesc>();
                for (int t = 0; t < tasks.Length && t < data.Processors[s].Length; t++)
                {
                    if (!(data.Processors[s][t] is Material material)) continue;
                    int priority = VFXOutput.Parameter(tasks[t], SortPriorityParameter, 0);
                    found ??= new List<(int, Material)>();
                    // Behind the outputs of its priority and of lower ones.
                    int at = found.Count;
                    while (at > 0 && found[at - 1].Priority > priority) at--;
                    found.Insert(at, (priority, material));
                }
            }
            if (found == null) return Array.Empty<Material>();
            var materials = new Material[found.Count];
            for (int i = 0; i < materials.Length; i++) materials[i] = found[i].Material;
            return materials;
        }
    }
}
