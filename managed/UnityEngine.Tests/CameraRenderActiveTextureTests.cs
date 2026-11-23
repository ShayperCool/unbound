using System.Collections.Generic;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Camera.Render leaves the render texture that was active before it active again, as
    /// the Unity 6000.0.71f1 Editor does (tests/projects/camerarender2d *.activeAfter).</summary>
    public sealed class CameraRenderActiveTextureTests
    {
        /// <summary>A pipeline that makes each camera's target the active texture, as a pass that
        /// sets its render target does.</summary>
        sealed class TargetPipelineAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => new TargetPipeline();
        }

        sealed class TargetPipeline : RenderPipeline
        {
            internal static int Renders;

            protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

            protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
            {
                foreach (var camera in cameras) RenderTexture.active = camera.targetTexture;
                Renders++;
            }
        }

        [Fact]
        public void APipelineThatSetsTheTargetLeavesTheTextureActiveBefore()
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            var target = new RenderTexture(4, 4, 0) { name = "Target" };
            var other = new RenderTexture(4, 4, 0) { name = "Other" };
            var previous = RenderTexture.active;
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            var asset = ScriptableObject.CreateInstance<TargetPipelineAsset>();
            try
            {
                GraphicsSettings.defaultRenderPipeline = asset;
                camera.enabled = false;
                camera.targetTexture = target;
                RenderTexture.active = other;
                int renders = TargetPipeline.Renders;
                camera.Render();
                Assert.Equal(renders + 1, TargetPipeline.Renders);
                Assert.Same(other, RenderTexture.active);
                RenderTexture.active = null;
                camera.Render();
                Assert.Null(RenderTexture.active);
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(asset);
            }
        }

        [Fact]
        public void TheTextureActiveBeforeIsActiveAfter()
        {
            var camera = new GameObject("Camera").AddComponent<Camera>();
            var target = new RenderTexture(4, 4, 0) { name = "Target" };
            var other = new RenderTexture(4, 4, 0) { name = "Other" };
            var previous = RenderTexture.active;
            try
            {
                camera.enabled = false;
                camera.targetTexture = target;
                RenderTexture.active = other;
                camera.Render();
                Assert.Same(other, RenderTexture.active);
                RenderTexture.active = null;
                camera.Render();
                Assert.Null(RenderTexture.active);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(other);
            }
        }
    }
}
