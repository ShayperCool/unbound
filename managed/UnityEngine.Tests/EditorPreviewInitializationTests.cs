using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("EditorState")]
    public sealed class EditorPreviewInitializationTests
    {
        [Theory]
        [InlineData(false, "ConfigurePreviewObjectSRP")]
        [InlineData(true, "InitInstantiatedPreviewRecursive")]
        public void PreviewSetupReportsUnmeasuredReleaseFamilies(bool hierarchy, string method)
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var member = "UnityEditor.EditorUtility." + method;
            Stubs.Forget(member);
            using var logs = new RecordedLogs();
            var root = new GameObject();
            try
            {
                root.hideFlags = HideFlags.NotEditable;
                if (hierarchy) EditorUtility.InitInstantiatedPreviewRecursive(root);
                else EditorUtility.ConfigurePreviewObjectSRP(root);
                Assert.Equal(HideFlags.NotEditable, root.hideFlags);
                Assert.Contains(logs.Logged, message =>
                    message.Contains(member + " preview setup is unmeasured for this release family"));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
