using System.Collections.Generic;
using UnityEditor;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>What an asset file's load callbacks mark dirty in the file is no change
    /// (tests/projects/urprendererwrites load.*): the file's objects stay as clean as they were.</summary>
    [Collection("EditorState")]
    public sealed class LoadCallbacksDirtinessTests
    {
        sealed class Data : ScriptableObject { }

        [Fact]
        public void ALoadsCallbacksLeaveTheFilesObjectsCleanAndOthersAsMarked()
        {
            var own = ScriptableObject.CreateInstance<Data>();
            var dirtyBefore = ScriptableObject.CreateInstance<Data>();
            var other = ScriptableObject.CreateInstance<Data>();
            try
            {
                EditorUtility.SetDirty(dirtyBefore);
                int countBefore = EditorUtility.GetDirtyCount(dirtyBefore);
                EditorUtility.LoadCallbacks(new List<Object> { own, dirtyBefore }, () =>
                {
                    EditorUtility.SetDirty(own);
                    EditorUtility.SetDirty(own);
                    EditorUtility.SetDirty(dirtyBefore);
                    EditorUtility.SetDirty(other);
                });
                Assert.False(EditorUtility.IsDirty(own));
                Assert.Equal(0, EditorUtility.GetDirtyCount(own));
                // Dirty before the load's callbacks: still dirty, with what they added.
                Assert.True(EditorUtility.IsDirty(dirtyBefore));
                Assert.Equal(countBefore + 1, EditorUtility.GetDirtyCount(dirtyBefore));
                // Not the file's: marked as the callbacks marked it.
                Assert.True(EditorUtility.IsDirty(other));
                // A callback that throws still leaves the file's objects clean.
                Assert.Throws<System.InvalidOperationException>(() => EditorUtility.LoadCallbacks(new List<Object> { own }, () =>
                {
                    EditorUtility.SetDirty(own);
                    throw new System.InvalidOperationException("callback");
                }));
                Assert.False(EditorUtility.IsDirty(own));
            }
            finally
            {
                foreach (var o in new Object[] { own, dirtyBefore, other })
                {
                    EditorUtility.ClearDirty(o);
                    Object.DestroyImmediate(o);
                }
            }
        }
    }
}
