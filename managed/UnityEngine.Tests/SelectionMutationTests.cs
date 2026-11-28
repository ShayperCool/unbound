using UnityEditor;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("EditorState")]
    public sealed class SelectionMutationTests
    {
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void UnmeasuredMutationsWarnWithoutChangingSelection(bool add, bool byId)
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var previous = Selection.instanceIDs;
            var previousActive = Selection.activeInstanceID;
            var original = new GameObject();
            var another = new GameObject();
            try
            {
                Selection.activeObject = original;
                var member = "UnityEditor.Selection." + (add ? "Add" : "Remove");
                Stubs.Forget(member);
                using var logs = new RecordedLogs();
                if (add)
                {
                    if (byId) Selection.Add(another.GetInstanceID());
                    else Selection.Add(another);
                }
                else
                {
                    if (byId) Selection.Remove(original.GetInstanceID());
                    else Selection.Remove(original);
                }

                Assert.Equal(new[] { original.GetInstanceID() }, Selection.instanceIDs);
                Assert.Same(original, Selection.activeObject);
                Assert.Contains(logs.Logged, message =>
                    message.Contains(member + " selection mutations are unmeasured for this release family"));
            }
            finally
            {
                Selection.Restore(previous, previousActive);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(another);
            }
        }
    }
}
