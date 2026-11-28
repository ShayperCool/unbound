using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("EditorState")]
    public sealed class SelectedAssetPathTests
    {
        [Fact]
        public void GetSelectedPathWarnsInUnmeasuredFamilies()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            const string member = "UnityEditor.AssetDatabase.GetUniquePathNameAtSelectedPath";
            Stubs.Forget(member);
            using var logs = new RecordedLogs();
            Assert.Null(AssetDatabase.GetUniquePathNameAtSelectedPath("Name.txt"));
            Assert.Contains(logs.Logged,
                message => message.Contains(member + " selection-based paths are unmeasured for this release family"));
        }
    }
}
