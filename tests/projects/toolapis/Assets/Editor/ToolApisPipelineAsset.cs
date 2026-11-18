using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A render pipeline that draws nothing, whose default 2D material the probe sets
/// (tests/projects/toolapis spriteRenderer.pipeline).</summary>
public class ToolApisPipelineAsset : RenderPipelineAsset<ToolApisPipeline>
{
    internal Material material;

    public override Material default2DMaterial => material;

    protected override RenderPipeline CreatePipeline() => new ToolApisPipeline();
}

public class ToolApisPipeline : RenderPipeline
{
    protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
}
