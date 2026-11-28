using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class FocusedPropertyEditorTests
    {
        const string OpenMember = "UnityEditor.EditorUtility.OpenPropertyEditor";

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FocusedInspectorReportsItsPresentationOrReleaseLimit(bool measured)
        {
            const string rebuild = "UnityEditor.ActiveEditorTracker.ForceRebuild";
            using var release = TestRelease.Pin(measured ? TestRelease.Measured : TestRelease.Unmeasured);
            Stubs.Forget(OpenMember);
            Stubs.Forget(rebuild);
            var before = Resources.FindObjectsOfTypeAll<PropertyEditor>();
            var target = new GameObject("Focused inspector test target");
            using var logs = new RecordedLogs();
            try
            {
                EditorUtility.OpenPropertyEditor(target);
                var added = Resources.FindObjectsOfTypeAll<PropertyEditor>().Except(before);
                if (measured)
                {
                    var window = Assert.Single(added);
                    Assert.Same(target, window.GetInspectedObject());
                    window.tracker.ForceRebuild();
                    Assert.Contains(logs.Logged, line => line.Contains(rebuild) && line.Contains("cannot rebuild editors"));
                }
                else
                    Assert.Empty(added);
                var message = measured ? "cannot draw or edit properties" : "unmeasured for this release family";
                Assert.Contains(logs.Logged, line => line.Contains(OpenMember) && line.Contains(message));
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<PropertyEditor>().Except(before))
                    window.Close();
                Object.DestroyImmediate(target);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DeadTargetsStillReportAnUnmeasuredRelease(bool destroyed)
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            Stubs.Forget(OpenMember);
            var target = destroyed ? new GameObject("Destroyed focused inspector target") : null;
            if (destroyed) Object.DestroyImmediate(target);
            using var logs = new RecordedLogs();

            EditorUtility.OpenPropertyEditor(target);

            Assert.Contains(logs.Logged, line => line.Contains(OpenMember) && line.Contains("unmeasured for this release family"));
        }

        [Fact]
        public void EditModeOpeningReportsItsLimitAndCreatesNoWindow()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string editMode = OpenMember + " in Edit mode";
            Stubs.Forget(editMode);
            var before = Resources.FindObjectsOfTypeAll<PropertyEditor>();
            var target = new GameObject("Edit mode focused inspector target");
            using var logs = new RecordedLogs();
            try
            {
                EditorSession.AsEditMode(() => EditorUtility.OpenPropertyEditor(target));

                Assert.Empty(Resources.FindObjectsOfTypeAll<PropertyEditor>().Except(before));
                Assert.Contains(logs.Logged, line => line.Contains(editMode) && line.Contains("is unmeasured"));
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<PropertyEditor>().Except(before))
                    window.Close();
                Object.DestroyImmediate(target);
            }
        }
    }
}
