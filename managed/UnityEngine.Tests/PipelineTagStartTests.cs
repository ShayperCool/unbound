using System;
using UnityEngine.Rendering;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Play mode starts with the configured pipeline's shader tag active and no pipeline
    /// made (tests/projects/graphqueue `early`): the first scene's materials see its SubShaders.</summary>
    public class PipelineTagStartTests : IDisposable
    {
        readonly RenderPipelineAsset m_Previous = GraphicsSettings.defaultRenderPipeline;
        readonly string m_Tag = Shader.globalRenderPipeline;

        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline = m_Previous;
            Shader.globalRenderPipeline = m_Tag;
        }

        sealed class TaggedPipelineAsset : RenderPipelineAsset
        {
            internal static int Made;
            protected override RenderPipeline CreatePipeline() { Made++; return null; }
            public override string renderPipelineShaderTag => "TestPipeline";
        }

        [Fact]
        public void TheConfiguredPipelinesTagIsActiveBeforeItIsMade()
        {
            var asset = ScriptableObject.CreateInstance<TaggedPipelineAsset>();
            try
            {
                Shader.globalRenderPipeline = "";
                GraphicsSettings.defaultRenderPipeline = asset;
                int made = TaggedPipelineAsset.Made;
                RenderPipelineManager.ActivateConfiguredPipelineTag();
                Assert.Equal("TestPipeline", Shader.globalRenderPipeline);
                Assert.Equal(made, TaggedPipelineAsset.Made);
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = m_Previous;
                Object.DestroyImmediate(asset);
            }
        }

        [Fact]
        public void WithoutAConfiguredPipelineTheTagStaysAsItIs()
        {
            GraphicsSettings.defaultRenderPipeline = null;
            Shader.globalRenderPipeline = "";
            RenderPipelineManager.ActivateConfiguredPipelineTag();
            Assert.Equal("", Shader.globalRenderPipeline);
        }
    }
}
