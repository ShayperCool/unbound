using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class StripShadowFeature : ScriptableRendererFeature
{
    public static RTHandle Output;
    public static bool Main;
    public static Material Read;
    Pass m_Pass;
    public override void Create() => m_Pass = new Pass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (Output != null) renderer.EnqueuePass(m_Pass);
    }
    class Pass : ScriptableRenderPass
    {
        class Data { public TextureHandle Source; public MaterialPropertyBlock Block; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var source = Main ? resources.mainShadowsTexture : resources.additionalShadowsTexture;
            if (!source.IsValid()) return;
            var target = graph.ImportTexture(Output);
            using var builder = graph.AddRasterRenderPass<Data>("StripShadowRead",out var data);
            data.Source=source; data.Block=new MaterialPropertyBlock();
            builder.UseTexture(source);
            builder.SetRenderAttachment(target,0);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((Data d, RasterGraphContext context) =>
            {
                d.Block.SetTexture("_ProbeShadowMap",(Texture)d.Source);
                context.cmd.DrawProcedural(Matrix4x4.identity,Read,0,MeshTopology.Triangles,3,1,d.Block);
            });
        }
    }
}
