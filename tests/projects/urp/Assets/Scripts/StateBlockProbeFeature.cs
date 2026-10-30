using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>Draws the opaque renderers again after the opaques with a RenderStateBlock (depth test
/// Greater without writes, alpha blending, no culling) and an override material: what lies behind
/// something shows through it, as URP's RenderObjects feature does.</summary>
public class StateBlockProbeFeature : ScriptableRendererFeature
{
    public static Material Material;
    public static Shader OverrideShader;
    public static int ShaderPass;
    Pass m_Pass;

    public override void Create() => m_Pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (Material != null || OverrideShader != null) renderer.EnqueuePass(m_Pass);
    }

    class Pass : ScriptableRenderPass
    {
        class PassData { public RendererListHandle List; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var camera = frameData.Get<UniversalCameraData>();
            var lights = frameData.Get<UniversalLightData>();
            using var builder = renderGraph.AddRasterRenderPass<PassData>("StateBlockProbe", out var data);
            var drawing = RenderingUtils.CreateDrawingSettings(new ShaderTagId("UniversalForward"), rendering, camera, lights, SortingCriteria.CommonOpaque);
            drawing.overrideMaterial = Material;
            drawing.overrideShader = OverrideShader;
            drawing.overrideShaderPassIndex = ShaderPass;
            var filtering = new FilteringSettings(RenderQueueRange.opaque);
            var block = new RenderStateBlock(RenderStateMask.Depth | RenderStateMask.Blend | RenderStateMask.Raster)
            {
                depthState = new DepthState(false, CompareFunction.Greater),
                blendState = new BlendState { blendState0 = new RenderTargetBlendState(ColorWriteMask.All, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha) },
                rasterState = new RasterState(CullMode.Off),
            };
            var param = new RendererListParams(rendering.cullResults, drawing, filtering)
            {
                tagValues = new NativeArray<ShaderTagId>(new[] { ShaderTagId.none }, Allocator.Temp),
                stateBlocks = new NativeArray<RenderStateBlock>(new[] { block }, Allocator.Temp),
                isPassTagName = false,
            };
            data.List = renderGraph.CreateRendererList(param);
            builder.UseRendererList(data.List);
            builder.SetRenderAttachment(resources.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
            builder.SetRenderFunc((PassData d, RasterGraphContext context) => context.cmd.DrawRendererList(d.List));
        }
    }
}
