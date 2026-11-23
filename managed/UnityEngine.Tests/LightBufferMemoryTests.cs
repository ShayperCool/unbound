using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

                light.AddCommandBuffer(evt, first, ShadowMapPass.AreaLight);
                light.CommandBuffersAt(evt, ShadowMapPass.All, selected);
                Assert.Empty(selected);
                light.CommandBuffersAt(evt, ShadowMapPass.AreaLight, selected);
                Assert.Equal(new[] { first }, selected);
            }
            finally
            {
                QualitySettings.renderPipeline = qualityPipeline;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                Object.DestroyImmediate(owner);
            }
        }

        [Fact]
        public void ClearingAndReaddingCommandsReusesTheWarmedStorage()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            using var buffer = new CommandBuffer();
            var owner = new GameObject("Light");
            try
            {
                var light = owner.AddComponent<Light>();
                RebuildCommands(light, buffer, 1024);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                RebuildCommands(light, buffer, 64);
                Assert.Equal(before, System.GC.GetAllocatedBytesForCurrentThread());
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RebuildCommands(Light light, CommandBuffer buffer, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                light.AddCommandBuffer(LightEvent.BeforeShadowMap, buffer);
                light.RemoveAllCommandBuffers();
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
