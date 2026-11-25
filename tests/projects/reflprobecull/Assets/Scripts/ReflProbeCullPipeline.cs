using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A scriptable pipeline that only culls the probe's camera (<see cref="Camera"/>) and keeps
/// the visible reflection probes' names in culling order (<see cref="Visible"/>), with the culling
/// planes of the camera's parameters (<see cref="Planes"/>). <see cref="Criteria"/>, when set,
/// replaces the parameters' reflection probe sorting.</summary>
public sealed class ReflProbeCullPipeline : RenderPipelineAsset
{
    public static Camera Camera;
    public static ReflectionProbeSortingCriteria? Criteria;
    public static readonly List<string> Visible = new List<string>();
    public static readonly List<Plane> Planes = new List<Plane>();
    public static string DefaultCriteria;
    public static int Culls;
    /// <summary>The first visible probe's fields.</summary>
    public static string First;

    protected override RenderPipeline CreatePipeline() => new Pipeline();

    sealed class Pipeline : RenderPipeline
    {
        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
            {
                if (camera != Camera || !camera.TryGetCullingParameters(out var parameters)) continue;
                DefaultCriteria = parameters.reflectionProbeSortingCriteria.ToString();
                if (Criteria.HasValue) parameters.reflectionProbeSortingCriteria = Criteria.Value;
                Planes.Clear();
                for (int i = 0; i < parameters.cullingPlaneCount; i++) Planes.Add(parameters.GetCullingPlane(i));
                var results = context.Cull(ref parameters);
                Visible.Clear();
                var probes = results.visibleReflectionProbes;
                for (int i = 0; i < probes.Length; i++)
                    Visible.Add(probes[i].reflectionProbe != null ? probes[i].reflectionProbe.name : "null");
                if (probes.Length > 0)
                {
                    var v = probes[0];
                    First = "bounds=" + v.bounds.ToString("R") + " center=" + v.center.ToString("R") + " blendDistance=" + v.blendDistance.ToString("R")
                        + " importance=" + v.importance + " isBoxProjection=" + v.isBoxProjection + " localToWorld=" + v.localToWorldMatrix.ToString("R").Replace("\n", " ").Replace("\t", " ")
                        + " hdr=" + v.hdrData.ToString("R") + " texture=" + (v.texture != null ? v.texture.name : "null");
                }
                Culls++;
                context.Submit();
            }
        }
    }
}
