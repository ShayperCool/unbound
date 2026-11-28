using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("EditorState")]
    public sealed class EditorPreviewInitializationTests
    {
        sealed class PreviewPipelineAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => null;
        }

        [Theory]
        [InlineData(false, "ConfigurePreviewObjectSRP")]
        [InlineData(true, "InitInstantiatedPreviewRecursive")]
        public void PreviewSetupReportsUnmeasuredReleaseFamilies(bool hierarchy, string method)
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var member = "UnityEditor.EditorUtility." + method;
            Stubs.Forget(member);
            using var logs = new RecordedLogs();
            var originalDefault = GraphicsSettings.defaultRenderPipeline;
            var originalOverride = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<PreviewPipelineAsset>();
            var root = new GameObject();
            try
            {
                QualitySettings.renderPipeline = null;
                GraphicsSettings.defaultRenderPipeline = pipeline;
                Assert.True(GraphicsSettings.isScriptableRenderPipelineEnabled);
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                root.hideFlags = HideFlags.NotEditable;
                if (hierarchy) EditorUtility.InitInstantiatedPreviewRecursive(root);
                else EditorUtility.ConfigurePreviewObjectSRP(root);
                Assert.Equal(HideFlags.NotEditable, root.hideFlags);
                Assert.Equal(LightProbeUsage.CustomProvided, renderer.lightProbeUsage);
                Assert.Contains(logs.Logged, message =>
                    message.Contains(member + " preview setup is unmeasured for this release family"));
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = originalDefault;
                QualitySettings.renderPipeline = originalOverride;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(pipeline);
            }
        }
    }
}
