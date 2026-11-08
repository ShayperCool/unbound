using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>After the opaques, a render graph raster pass with two colour attachments (the
/// camera colour and a second target the graph clears to its own colour) draws Probe/Mrt's
/// "Write" pass; a later pass shows the second target in the camera colour ("Show"). The native
/// render pass compiler can make the first a subpass with two colour outputs.</summary>
public class MrtProbeFeature : ScriptableRendererFeature
{
    public static Material Material;
    /// <summary>Both outputs are new targets that clear (the first is shown too).</summary>
    public static bool BothCleared;
    Pass m_Pass;

    public override void Create() => m_Pass = new Pass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (Material != null) renderer.EnqueuePass(m_Pass);
    }

    class Pass : ScriptableRenderPass
    {
        class PassData { public TextureHandle Shown; public MaterialPropertyBlock Block; }

        static readonly MaterialPropertyBlock s_Upper = new MaterialPropertyBlock();
        static readonly MaterialPropertyBlock s_Lower = new MaterialPropertyBlock();

        static TextureHandle Target(RenderGraph renderGraph, UniversalCameraData camera, string name, Color clear) =>
            renderGraph.CreateTexture(new TextureDesc(camera.cameraTargetDescriptor.width, camera.cameraTargetDescriptor.height)
            {
                colorFormat = GraphicsFormat.R8G8B8A8_UNorm,
                clearBuffer = true,
                clearColor = clear,
                filterMode = FilterMode.Point,
                name = name,
            });

        static void Show(RenderGraph renderGraph, TextureHandle color, TextureHandle shown, MaterialPropertyBlock block, Vector4 rect)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>("MrtProbe Show", out var data);
            data.Shown = shown;
            data.Block = block;
            block.SetVector("_MrtRect", rect);
            builder.UseTexture(shown);
            builder.SetRenderAttachment(color, 0);
            builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
            {
                d.Block.SetTexture("_MrtSecond", (Texture)d.Shown);
                context.cmd.DrawProcedural(Matrix4x4.identity, Material, 1, MeshTopology.Triangles, 6, 1, d.Block);
            });
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            var second = Target(renderGraph, camera, "MrtSecond", new Color(0.1f, 0.1f, 0.4f, 1f));
            var first = BothCleared ? Target(renderGraph, camera, "MrtFirst", new Color(0.5f, 0.1f, 0.1f, 1f)) : resources.activeColorTexture;
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("MrtProbe Write", out var data))
            {
                builder.SetRenderAttachment(first, 0);
                builder.SetRenderAttachment(second, 1);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) =>
                    context.cmd.DrawProcedural(Matrix4x4.identity, Material, 0, MeshTopology.Triangles, 6));
            }
            Show(renderGraph, resources.activeColorTexture, second, s_Upper, new Vector4(0.4f, 0.2f, 0.9f, 0.8f));
            if (BothCleared) Show(renderGraph, resources.activeColorTexture, first, s_Lower, new Vector4(0.4f, -0.8f, 0.9f, -0.2f));
        }
    }
}
