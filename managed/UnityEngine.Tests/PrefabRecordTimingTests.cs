using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The order of a Transform's changes and the points that take them into a prefab
    /// instance's modifications (tests/projects/scenesave modification-timing): a change after a
    /// frame boundary or record point is not taken until the next one, and Unbound's own writes to
    /// an instance (a revert, a merge) are no change.</summary>
    public class PrefabRecordTimingTests
    {
        [Fact]
        public void ChangesAreOrderedAgainstBoundariesAndRecordPoints()
        {
            var go = new GameObject("Timed");
            try
            {
                var t = go.transform;
                var data = new PrefabInstanceData();
                PrefabRecordTiming.FrameBoundary();
                t.localPosition = new Vector3(1f, 0f, 0f);
                Assert.False(t.m_ChangeStamp < System.Math.Max(PrefabRecordTiming.TransformsTaken, data.RecordStamp));
                PrefabRecordTiming.Recorded(data);
                Assert.True(t.m_ChangeStamp < data.RecordStamp);
                t.localPosition = new Vector3(2f, 0f, 0f);
                Assert.False(t.m_ChangeStamp < System.Math.Max(PrefabRecordTiming.TransformsTaken, data.RecordStamp));
                PrefabRecordTiming.SceneSaved();
                Assert.True(t.m_ChangeStamp < PrefabRecordTiming.TransformsTaken);
                // An instance read from a file was not made by InstantiatePrefab in the session.
                Assert.Equal(-1, data.InstantiatedStamp);

                long before = t.m_ChangeStamp;
                PrefabRecordTiming.FrameBoundary();
                using (PrefabRecordTiming.QuietScope.Begin())
                    t.localPosition = new Vector3(3f, 0f, 0f);
                Assert.Equal(before, t.m_ChangeStamp);
                Assert.Equal(0, PrefabRecordTiming.Quiet);

                // Changed in two updates in a row: the pose the last update took is the one after the
                // first update's change.
                t.localPosition = new Vector3(4f, 0f, 0f);
                PrefabRecordTiming.FrameBoundary();
                t.localPosition = new Vector3(5f, 0f, 0f);
                Assert.True(t.m_HasTakenPose);
                Assert.Equal(new Vector3(4f, 0f, 0f), t.m_TakenPose.Position);
                t.localPosition = new Vector3(6f, 0f, 0f);
                Assert.Equal(new Vector3(4f, 0f, 0f), t.m_TakenPose.Position);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
