using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class DisplayCustomMenuTests
    {
        [Fact]
        public void MenuPresentationWarningFollowsSuccessfulPreparation()
        {
            const string member = "UnityEditor.EditorUtility.DisplayCustomMenu";
            const string reset = "UnityEditor.EditorUtility.ResetMouseDown";
            var previous = GUIUtility.hotControl;
            int previousButton = Tools.s_ButtonDown;
            using var logs = new RecordedLogs();
            Unbound.Runtime.Stubs.Forget(member);
            Unbound.Runtime.Stubs.Forget(reset);
            try
            {
                GUIUtility.hotControl = 66;
                Tools.s_ButtonDown = 1;
                var options = new[] { new GUIContent("First"), new GUIContent("Second") };
                Assert.Throws<InvalidOperationException>(() => EditorUtility.DisplayCustomMenu(default, options,
                    _ => throw new InvalidOperationException("enable predicate"), -1, null, null));
                Assert.Equal(66, GUIUtility.hotControl);
                Assert.Equal(1, Tools.s_ButtonDown);
                Assert.DoesNotContain(logs.Logged, line => line.Contains(member));
                Assert.DoesNotContain(logs.Logged, line => line.Contains(reset));
                EditorUtility.DisplayCustomMenu(default, options, -1, (_, _, _) => throw new Exception("selection"), null);
                Assert.Equal(0, GUIUtility.hotControl);
                Assert.Equal(-1, Tools.s_ButtonDown);
                Assert.Contains(logs.Logged, line => line.Contains(member) && line.Contains("cannot show an Editor context menu"));
                Assert.Contains(logs.Logged, line => line.Contains(reset) && line.Contains("cannot reset Editor shortcuts"));
            }
            finally
            {
                GUIUtility.hotControl = previous;
                Tools.s_ButtonDown = previousButton;
            }
        }
    }
}
