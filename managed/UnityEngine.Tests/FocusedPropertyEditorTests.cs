using System.Linq;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class FocusedPropertyEditorTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FocusedInspectorReportsItsPresentationOrReleaseLimit(bool measured)
        {
            const string member = "UnityEditor.EditorUtility.OpenPropertyEditor";
            const string rebuild = "UnityEditor.ActiveEditorTracker.ForceRebuild";
            using var release = TestRelease.Pin(measured ? TestRelease.Measured : TestRelease.Unmeasured);
            Unbound.Runtime.Stubs.Forget(member);
            Unbound.Runtime.Stubs.Forget(rebuild);
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
                Assert.Contains(logs.Logged, line => line.Contains(member) && line.Contains(message));
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
