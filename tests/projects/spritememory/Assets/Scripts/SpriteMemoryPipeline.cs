using UnityEngine;
using UnityEngine.Rendering;

sealed class SpriteMemoryPipeline : RenderPipelineAsset
{
    internal sealed class Request { internal bool Draw, Completed; }

    protected override RenderPipeline CreatePipeline() => new Pipeline();

    sealed class Pipeline : RenderPipeline
    {
        protected override bool IsRenderRequestSupported<T>(Camera camera, T request) => request is Request;

        protected override void ProcessRenderRequests<T>(ScriptableRenderContext context, Camera camera, T request)
        {
            if (request is not Request probe || !camera.TryGetCullingParameters(out var parameters)) return;
            var visible = context.Cull(ref parameters);
            context.SetupCameraProperties(camera);
            using (var commands = new CommandBuffer())
            {
                commands.SetRenderTarget(camera.targetTexture);
                commands.ClearRenderTarget(true, true, Color.black);
                context.ExecuteCommandBuffer(commands);
            }
            if (probe.Draw)
            {
                var sorting = new SortingSettings(camera) { criteria = SortingCriteria.CommonTransparent };
                var drawing = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sorting);
                var filtering = new FilteringSettings(RenderQueueRange.all);
                context.DrawRenderers(visible, ref drawing, ref filtering);
            }
            context.Submit();
            probe.Completed = true;
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras) => context.Submit();
    }
}
