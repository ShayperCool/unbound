using System.Collections.Generic;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class LightBufferMemoryTests
    {
        sealed class CustomPipeline : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => null;
        }

        [Fact]
        public void EventListsRetainPassSelectionAndReservedSlots()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var first = new CommandBuffer();
            using var otherPass = new CommandBuffer();
            using var last = new CommandBuffer();
            var owner = new GameObject("Light");
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            var qualityPipeline = QualitySettings.renderPipeline;
            try
            {
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = null;
                var light = owner.AddComponent<Light>();
                const LightEvent evt = LightEvent.BeforeShadowMapPass;
                light.AddCommandBuffer(evt, first, ShadowMapPass.PointlightPositiveX);
                light.AddCommandBuffer(LightEvent.AfterShadowMapPass, otherPass);
                light.AddCommandBuffer(evt, otherPass, ShadowMapPass.PointlightNegativeX);
                light.AddCommandBuffer(evt, last);
                Assert.Equal(new[] { first, otherPass, last }, light.GetCommandBuffers(evt));
                var selected = new List<CommandBuffer>();
                light.CommandBuffersAt(evt, ShadowMapPass.PointlightPositiveX, selected);
                Assert.Equal(new[] { first, last }, selected);
                Assert.Equal(4, light.commandBufferCount);
                Assert.Equal(1236, Profiler.GetRuntimeMemorySizeLong(light));

                light.RemoveCommandBuffer(evt, first);
                Assert.Equal(1236, Profiler.GetRuntimeMemorySizeLong(light));
                light.RemoveCommandBuffers(evt);
                Assert.Equal(1188, Profiler.GetRuntimeMemorySizeLong(light));
                Assert.Equal(new[] { otherPass }, light.GetCommandBuffers(LightEvent.AfterShadowMapPass));
                light.CommandBuffersAt(evt, ShadowMapPass.All, selected);
                Assert.Empty(selected);
            }
            finally
            {
                QualitySettings.renderPipeline = qualityPipeline;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                Object.DestroyImmediate(owner);
            }
        }

        [Fact]
        public void ScriptablePipelineKeepsTheUnmeasuredLightResult()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var owner = new GameObject("Light");
            var custom = ScriptableObject.CreateInstance<CustomPipeline>();
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            var qualityPipeline = QualitySettings.renderPipeline;
            try
            {
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = custom;
                var light = owner.AddComponent<Light>();
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(light));
            }
            finally
            {
                QualitySettings.renderPipeline = qualityPipeline;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                Object.DestroyImmediate(custom);
                Object.DestroyImmediate(owner);
            }
        }
    }
}
