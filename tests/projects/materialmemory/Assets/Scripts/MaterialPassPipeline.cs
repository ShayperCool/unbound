using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

sealed class MaterialPassPipeline : RenderPipelineAsset
{
    internal sealed class Request
    {
        internal Material Material;
        internal int Pass;
        internal bool FilterOut;
        internal bool Completed;
        internal long AfterCull, AfterDraw, AfterSubmit;
    }

    internal string PipelineTag = "";
    public override string renderPipelineShaderTag => PipelineTag;
    public override System.Type pipelineType => typeof(Pipeline);
    protected override RenderPipeline CreatePipeline() => new Pipeline();

    sealed class Pipeline : RenderPipeline
    {
        protected override bool IsRenderRequestSupported<T>(Camera camera, T request) => request is Request;
        protected override void Render(ScriptableRenderContext context, Camera[] cameras) => context.Submit();

        protected override void ProcessRenderRequests<T>(ScriptableRenderContext context, Camera camera, T request)
        {
            if (request is not Request probe || !camera.TryGetCullingParameters(out var parameters)) return;
            var visible = context.Cull(ref parameters);
            probe.AfterCull = Profiler.GetRuntimeMemorySizeLong(probe.Material);
            context.SetupCameraProperties(camera);
            using (var commands = new CommandBuffer())
            {
                commands.SetRenderTarget(camera.targetTexture);
                commands.ClearRenderTarget(true, true, Color.black);
                context.ExecuteCommandBuffer(commands);
            }
            var sorting = new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque };
            var drawing = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), sorting)
            {
                overrideMaterial = probe.Material,
                overrideMaterialPassIndex = probe.Pass
            };
            var filtering = new FilteringSettings(RenderQueueRange.all, probe.FilterOut ? 0 : camera.cullingMask);
            context.DrawRenderers(visible, ref drawing, ref filtering);
            probe.AfterDraw = Profiler.GetRuntimeMemorySizeLong(probe.Material);
            context.Submit();
            probe.AfterSubmit = Profiler.GetRuntimeMemorySizeLong(probe.Material);
            probe.Completed = true;
        }
    }
}
