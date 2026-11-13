using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A scriptable pipeline that draws nothing. While <see cref="OnCull"/> is set, it culls
/// each camera and hands the results over.</summary>
public sealed class ProbePipelineAsset : RenderPipelineAsset
{
    public static Action<Camera, CullingResults> OnCull;

    protected override RenderPipeline CreatePipeline() => new ProbePipeline();

    sealed class ProbePipeline : RenderPipeline
    {
        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
            {
                var callback = OnCull;
                if (callback == null || !camera.TryGetCullingParameters(out var parameters)) continue;
                callback(camera, context.Cull(ref parameters));
            }
            context.Submit();
        }
    }
}
