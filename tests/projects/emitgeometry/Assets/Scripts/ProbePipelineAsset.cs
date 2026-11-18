using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A scriptable pipeline that, for each camera, first emits UI geometry as <see cref="Emit"/>
/// asks (ScriptableRenderContext.EmitGeometryForCamera, EmitWorldGeometryForSceneView, before
/// culling, as URP does), then culls, sets up the camera, clears to black and draws every
/// renderer's untagged passes (SRPDefaultUnlit: UI/Default's) sorted as transparent. The target
/// is the camera's, set after SetupCameraProperties (<see cref="Target"/> 0); 1 sets none (what
/// the render starts with, or SetupCameraProperties sets), 2 sets <see cref="Other"/> before
/// SetupCameraProperties and none after it.
/// </summary>
public sealed class ProbePipelineAsset : RenderPipelineAsset
{
    /// <summary>Bit 0: EmitGeometryForCamera; bit 1: EmitWorldGeometryForSceneView.</summary>
    public static int Emit;
    public static int Target;
    public static RenderTexture Other;

    protected override RenderPipeline CreatePipeline() => new ProbePipeline();

    sealed class ProbePipeline : RenderPipeline
    {
        static readonly ShaderTagId s_Unlit = new ShaderTagId("SRPDefaultUnlit");

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            foreach (var camera in cameras)
            {
                if ((Emit & 1) != 0) ScriptableRenderContext.EmitGeometryForCamera(camera);
                if ((Emit & 2) != 0) ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                if (!camera.TryGetCullingParameters(out var parameters)) continue;
                var results = context.Cull(ref parameters);
                if (Target == 2)
                {
                    var other = new CommandBuffer { name = "Other" };
                    other.SetRenderTarget(Other);
                    context.ExecuteCommandBuffer(other);
                    other.Release();
                }
                context.SetupCameraProperties(camera);
                var clear = new CommandBuffer { name = "Clear" };
                if (Target == 0) clear.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
                clear.ClearRenderTarget(true, true, Color.black);
                context.ExecuteCommandBuffer(clear);
                clear.Release();
                var sorting = new SortingSettings(camera) { criteria = SortingCriteria.CommonTransparent };
                var drawing = new DrawingSettings(s_Unlit, sorting);
                var filtering = new FilteringSettings(RenderQueueRange.all);
                context.DrawRenderers(results, ref drawing, ref filtering);
                context.Submit();
            }
        }
    }
}
