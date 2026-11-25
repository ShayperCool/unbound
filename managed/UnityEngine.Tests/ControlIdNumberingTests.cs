using Xunit;

namespace UnityEngine.Tests
{
    // Expected structure: tests/projects/imguicontrolids (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class ControlIdNumberingTests
    {
        static T Pass<T>(EventType type, System.Func<T> draw, int instance)
        {
            var previous = Event.current;
            Event.current = new Event { type = type };
            GUIUtility.BeginGUI(0, instance, 0);
            try { return draw(); }
            finally
            {
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        [Fact]
        public void PermanentIdsComeFromTheCounterNewControlIdsComeFrom()
        {
            // f0/layout/main: passive, keyboard, a permanent id, passive: one counter.
            var ids = Pass(EventType.Layout, () => new[]
            {
                GUIUtility.GetControlID(FocusType.Passive), GUIUtility.GetControlID(FocusType.Keyboard),
                GUIUtility.GetPermanentControlID(), GUIUtility.GetControlID(FocusType.Passive),
            }, 9101);
            Assert.True(ids[0] > 0);
            Assert.Equal(new[] { ids[0] + 1, ids[0] + 2, ids[0] + 3 }, new[] { ids[1], ids[2], ids[3] });
            // f0/repaint/main: the controls keep their ids; a permanent id is the counter's next.
            var again = Pass(EventType.Repaint, () => new[]
            {
                GUIUtility.GetControlID(FocusType.Passive), GUIUtility.GetControlID(FocusType.Keyboard),
                GUIUtility.GetPermanentControlID(), GUIUtility.GetControlID(FocusType.Passive),
            }, 9101);
            Assert.Equal(new[] { ids[0], ids[1], ids[3] }, new[] { again[0], again[1], again[3] });
            Assert.Equal(ids[3] + 1, again[2]);
            // f0/layout/second: another component's new controls continue the same counter.
            int other = Pass(EventType.Layout, () => GUIUtility.GetControlID(FocusType.Passive), 9102);
            Assert.Equal(again[2] + 1, other);
        }

        [Fact]
        public void OutsideOnGuiAnIdTakesTheNextNumber()
        {
            // outside: two permanent ids, then GetControlID outside OnGUI, one after another.
            var previous = Event.current;
            Event.current = null;
            try
            {
                int a = GUIUtility.GetPermanentControlID(), b = GUIUtility.GetPermanentControlID();
                int c = GUIUtility.GetControlID(FocusType.Passive);
                Assert.Equal((a + 1, a + 2), (b, c));
            }
            finally { Event.current = previous; }
        }
    }
}
