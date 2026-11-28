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
            const string layout = OpenMember + " saved layout";
            using var release = TestRelease.Pin(measured ? TestRelease.Measured : TestRelease.Unmeasured);
            Stubs.Forget(OpenMember);
            Stubs.Forget(rebuild);
            Stubs.Forget(layout);
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
                    Assert.Contains(logs.Logged, line => line.Contains(layout) && line.Contains("saved window geometry and layout-dependent focus"));
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
        public void DeadTargetsAreSilentInMeasuredEditMode(bool destroyed)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Stubs.Forget(OpenMember + " in Edit mode");
            var target = destroyed ? new GameObject("Destroyed Edit mode inspector target") : null;
            if (destroyed) Object.DestroyImmediate(target);
            using var logs = new RecordedLogs();

            EditorSession.AsEditMode(() => EditorUtility.OpenPropertyEditor(target));

            Assert.DoesNotContain(logs.Logged, line => line.Contains(OpenMember));
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

        [Fact]
        public void MacBatchInspectorReportsExistingModelFocus()
        {
            if (!System.OperatingSystem.IsMacOS()) return;
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string open = OpenMember + " with existing keyboard focus";
            const string close = "UnityEditor.EditorWindow.Close on a focused inspector with existing keyboard focus";
            Stubs.Forget(open);
            Stubs.Forget(close);
            var before = Resources.FindObjectsOfTypeAll<PropertyEditor>();
            var previous = EditorWindow.focusedWindow;
            var prior = ScriptableObject.CreateInstance<EditorWindow>();
            var target = new GameObject("Focused inspector prior focus target");
            using var logs = new RecordedLogs();
            try
            {
                prior.Show();
                Assert.Same(prior, EditorWindow.focusedWindow);
                EditorUtility.OpenPropertyEditor(target);
                var inspector = Assert.Single(Resources.FindObjectsOfTypeAll<PropertyEditor>().Except(before));
                Assert.Same(prior, EditorWindow.focusedWindow);
                inspector.Close();
                Assert.Same(prior, EditorWindow.focusedWindow);
                Assert.Contains(logs.Logged, line => line.Contains(open) && line.Contains("unmeasured on macOS"));
                Assert.Contains(logs.Logged, line => line.Contains(close) && line.Contains("unmeasured on macOS"));
            }
            finally
            {
                foreach (var window in Resources.FindObjectsOfTypeAll<PropertyEditor>().Except(before))
                    window.Close();
                prior.Close();
                if (previous != null) previous.Focus();
                Object.DestroyImmediate(target);
            }
        }

        [Fact]
        public void MacInspectorReportsAnExplicitFocusRequest()
        {
            if (!System.OperatingSystem.IsMacOS()) return;
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string focus = "UnityEditor.EditorWindow.Focus on a focused inspector";
            Stubs.Forget(focus);
            var previous = EditorWindow.focusedWindow;
            var target = new GameObject("Explicit inspector focus target");
            var inspector = PropertyEditor.OpenPropertyEditor(target);
            using var logs = new RecordedLogs();
            try
            {
                inspector.Focus();

                Assert.Contains(logs.Logged, line => line.Contains(focus) && line.Contains("close effects are unsupported on macOS"));
            }
            finally
            {
                inspector.Close();
                if (previous != null) previous.Focus();
                Object.DestroyImmediate(target);
            }
        }
    }
}
