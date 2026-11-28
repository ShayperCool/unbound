using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DisplayCustomMenuTests
    {
        const string Menu = "UnityEditor.EditorUtility.DisplayCustomMenu";
        const string Separators = "UnityEditor.EditorUtility.DisplayCustomMenuWithSeparators";
        const string Reset = "UnityEditor.EditorUtility.ResetMouseDown";

        [Fact]
        public void MenuPresentationWarningFollowsSuccessfulPreparation()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previous = GUIUtility.hotControl;
            int previousButton = Tools.s_ButtonDown;
            using var logs = new RecordedLogs();
            Unbound.Runtime.Stubs.Forget(Menu);
            Unbound.Runtime.Stubs.Forget(Reset);
            try
            {
                GUIUtility.hotControl = 66;
                Tools.s_ButtonDown = 1;
                var options = new[] { new GUIContent("First"), new GUIContent("Second") };
                Assert.Throws<InvalidOperationException>(() => EditorUtility.DisplayCustomMenu(default, options,
                    _ => throw new InvalidOperationException("enable predicate"), -1, null, null));
                Assert.Equal(66, GUIUtility.hotControl);
                Assert.Equal(1, Tools.s_ButtonDown);
                Assert.DoesNotContain(logs.Logged, line => line.Contains(Menu));
                Assert.DoesNotContain(logs.Logged, line => line.Contains(Reset));
                EditorUtility.DisplayCustomMenu(default, options, -1, (_, _, _) => throw new Exception("selection"), null);
                Assert.Equal(0, GUIUtility.hotControl);
                Assert.Equal(-1, Tools.s_ButtonDown);
                Assert.Contains(logs.Logged, line => line.Contains(Menu) && line.Contains("cannot show an Editor context menu"));
                Assert.Contains(logs.Logged, line => line.Contains(Reset) && line.Contains("cannot reset Editor shortcuts"));
            }
            finally
            {
                GUIUtility.hotControl = previous;
                Tools.s_ButtonDown = previousButton;
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SeparatorsReportPresentationAfterMeasuredCompletion(bool empty)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            WithMouseCapture(logs =>
            {
                EditorUtility.DisplayCustomMenuWithSeparators(default, empty ? null : new[] { "A" },
                    empty ? null : new[] { true }, empty ? null : new[] { false }, empty ? null : new[] { 0 }, null, null);
                Assert.Contains(logs.Logged, line => line.Contains(Separators) && line.Contains("cannot show an Editor context menu"));
                Assert.Equal(0, GUIUtility.hotControl);
                Assert.Equal(-1, Tools.s_ButtonDown);
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void InvalidSeparatorMasksAreReportedBeforeClearingCapture(bool missingEnabled)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            WithMouseCapture(logs =>
            {
                EditorUtility.DisplayCustomMenuWithSeparators(default, new[] { "A" },
                    missingEnabled ? null : new[] { true },
                    missingEnabled ? new[] { false } : Array.Empty<bool>(), new[] { 0 }, null, null);
                Assert.Contains(logs.Logged, line => line.Contains(Separators) && line.Contains("requires option masks"));
                Assert.DoesNotContain(logs.Logged, line => line.Contains(Reset));
                Assert.Equal(581, GUIUtility.hotControl);
                Assert.Equal(2, Tools.s_ButtonDown);
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UnmeasuredMenuCompletionReportsAndPreservesCapture(bool separators)
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            WithMouseCapture(logs =>
            {
                string member;
                if (separators)
                {
                    member = Separators;
                    EditorUtility.DisplayCustomMenuWithSeparators(default, null, null, null, null, null, null);
                }
                else
                {
                    member = Menu;
                    int enabled = 0;
                    EditorUtility.DisplayCustomMenu(default, new[] { new GUIContent("A") },
                        _ => { enabled++; return true; }, 0, null, null);
                    Assert.Equal(1, enabled);
                }
                Assert.Contains(logs.Logged, line => line.Contains(member) && line.Contains("unmeasured for this release family"));
                Assert.DoesNotContain(logs.Logged, line => line.Contains(Reset));
                Assert.Equal(581, GUIUtility.hotControl);
                Assert.Equal(2, Tools.s_ButtonDown);
            });
        }

        static void WithMouseCapture(Action<RecordedLogs> check)
        {
            var previous = GUIUtility.hotControl;
            int previousButton = Tools.s_ButtonDown;
            using var logs = new RecordedLogs();
            Unbound.Runtime.Stubs.Forget(Menu);
            Unbound.Runtime.Stubs.Forget(Separators);
            Unbound.Runtime.Stubs.Forget(Reset);
            Unbound.Runtime.Stubs.Forget(Separators + " (option masks)");
            Unbound.Runtime.Stubs.Forget(Menu + " (release family)");
            Unbound.Runtime.Stubs.Forget(Separators + " (release family)");
            try
            {
                GUIUtility.hotControl = 581;
                Tools.s_ButtonDown = 2;
                check(logs);
            }
            finally
            {
                GUIUtility.hotControl = previous;
                Tools.s_ButtonDown = previousButton;
            }
        }
    }
}
