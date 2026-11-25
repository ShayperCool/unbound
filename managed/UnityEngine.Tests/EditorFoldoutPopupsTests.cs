using System;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/editorfoldoutfields (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class EditorFoldoutPopupsTests
    {
        static T Pass<T>(Event e, Func<T> draw, int instance = 31)
        {
            var previous = Event.current;
            Event.current = e;
            GUIUtility.BeginGUI(0, instance, 0);
            try { return draw(); }
            finally
            {
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        static Event Mouse(EventType type, float x, float y) => new Event { type = type, mousePosition = new Vector2(x, y), button = 0, clickCount = 1 };

        static void Reset()
        {
            GUIUtility.keyboardControl = 0;
            GUIUtility.hotControl = 0;
        }

        static readonly Rect Fold = new Rect(10, 300, 300, 18);

        // Styles of their own (the skins' images come from the native side); the foldout's arrow
        // is its style's left padding, 14 points as the Editor skin's Foldout.
        static readonly GUIStyle s_Foldout = new GUIStyle { padding = new RectOffset(14, 0, 0, 0) };
        static readonly GUIStyle s_Popup = new GUIStyle();

        [Fact]
        public void AFoldoutTogglesOnItsArrowAndArrowKeysButNotOnItsLabel()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            try
            {
                bool open = false;
                // script/fold.arrow.*: the arrow toggles on mouse up.
                open = Pass(Mouse(EventType.MouseDown, Fold.x + 5, Fold.center.y), () => EditorGUI.Foldout(Fold, open, "Fold", s_Foldout));
                Assert.False(open);
                open = Pass(Mouse(EventType.MouseUp, Fold.x + 5, Fold.center.y), () => EditorGUI.Foldout(Fold, open, "Fold", s_Foldout));
                Assert.True(open);
                // script/fold.left, fold.right: the focused foldout closes and opens.
                open = Pass(Event.KeyboardEvent("left"), () => EditorGUI.Foldout(Fold, open, "Fold", s_Foldout));
                Assert.False(open);
                open = Pass(Event.KeyboardEvent("right"), () => EditorGUI.Foldout(Fold, open, "Fold", s_Foldout));
                Assert.True(open);
                // script/fold.label.*: the label does not toggle without toggleOnLabelClick...
                Pass(Mouse(EventType.MouseDown, Fold.x + 80, Fold.center.y), () => EditorGUI.Foldout(Fold, open, "Fold", s_Foldout));
                open = Pass(Mouse(EventType.MouseUp, Fold.x + 80, Fold.center.y), () => EditorGUI.Foldout(Fold, open, "Fold", s_Foldout));
                Assert.True(open);
                // ...and does with it (script/foldLabel.*).
                Pass(Mouse(EventType.MouseDown, Fold.x + 80, Fold.center.y), () => EditorGUI.Foldout(Fold, open, "Fold", true, s_Foldout));
                open = Pass(Mouse(EventType.MouseUp, Fold.x + 80, Fold.center.y), () => EditorGUI.Foldout(Fold, open, "Fold", true, s_Foldout));
                Assert.False(open);
            }
            finally { Reset(); }
        }

        [Fact]
        public void AnIntPopupMapsValuesToIndicesAndBack()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var r = new Rect(10, 10, 300, 18);
            string[] names = { "Ten", "Twenty", "Thirty" };
            int[] values = { 10, 20, 30 };
            // rect/repaint/intPopup: a value is kept; one not among the options too; no values: the index.
            Assert.Equal(20, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.IntPopup(r, 20, names, values, s_Popup)));
            Assert.Equal(25, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.IntPopup(r, 25, names, values, s_Popup)));
            Assert.Equal(1, Pass(new Event { type = EventType.Repaint }, () => EditorGUI.IntPopup(r, 1, names, null, s_Popup)));
        }

        [Fact]
        public void AMinMaxSlidersThumbMovesBothEnds()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            Reset();
            var r = new Rect(10, 340, 200, 18);
            float min = 2f, max = 6f;
            try
            {
                // script/minMax.thumb.*: the thumb dragged 20 points moves both ends by the same amount.
                Pass(Mouse(EventType.MouseDown, r.x + 80, r.center.y), () => { EditorGUI.MinMaxSlider(r, ref min, ref max, 0f, 10f); return 0; });
                Pass(new Event { type = EventType.MouseDrag, mousePosition = new Vector2(r.x + 100, r.center.y), delta = new Vector2(20, 0) },
                    () => { EditorGUI.MinMaxSlider(r, ref min, ref max, 0f, 10f); return 0; });
                Assert.True(min > 2f);
                Assert.Equal(4f, max - min, 4);
                Pass(Mouse(EventType.MouseUp, r.x + 100, r.center.y), () => { EditorGUI.MinMaxSlider(r, ref min, ref max, 0f, 10f); return 0; });
                Assert.Equal(0, GUIUtility.hotControl);
            }
            finally { Reset(); }
        }
    }
}
