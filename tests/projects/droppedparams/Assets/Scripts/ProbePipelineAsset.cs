using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A scriptable pipeline that draws nothing. While <see cref="OnCull"/> is set, it culls
/// each camera with the shadow distance <see cref="ShadowDistance"/> and hands the results over.</summary>
public sealed class ProbePipelineAsset : RenderPipelineAsset
{
    public static Action<Camera, CullingResults> OnCull;
    public static float ShadowDistance = 30f;

    protected override RenderPipeline CreatePipeline() => new ProbePipeline();

    sealed class ProbePipeline : RenderPipeline
    {
        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
            {
                var callback = OnCull;
                if (callback == null || !camera.TryGetCullingParameters(out var parameters)) continue;
                parameters.shadowDistance = ShadowDistance;
                callback(camera, context.Cull(ref parameters));
            }
            context.Submit();
        }
    }
}
