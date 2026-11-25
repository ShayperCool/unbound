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
        public void OutsideOnGuiTheControlsThrowMonosNullReference()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var previous = Event.current;
            var clips = GUIClip.Save();
            Event.current = null;
            GUIClip.ClearForTests();
            try
            {
                // editorui EditorGUI.Foldout outside OnGUI: Mono's message, without a final period.
                var r = new Rect(0, 0, 100, 18);
                float min = 1f, max = 2f;
                foreach (var call in new Action[] {
                             () => EditorGUI.Foldout(r, true, "x", s_Foldout), () => EditorGUI.IntPopup(r, 20, new[] { "a" }, new[] { 20 }, s_Popup),
                             () => EditorGUI.SelectableLabel(r, "s", s_Popup) })
                    Assert.Equal("Object reference not set to an instance of an object", Assert.Throws<NullReferenceException>(call).Message);
                // The min-max slider reads GUI.skin first (outside/editorGUI).
                Assert.Equal("You can only call GUI functions from inside OnGUI.",
                    Assert.Throws<ArgumentException>(() => EditorGUI.MinMaxSlider(r, ref min, ref max, 0f, 10f)).Message);
            }
            finally
            {
                Event.current = previous;
                GUIClip.Restore(clips);
            }
        }

        [Fact]
        public void AnUnmeasuredFamilysSkinStyledControlsKeepTheirValues()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var r = new Rect(0, 0, 100, 18);
            float min = 1f, max = 2f;
            Assert.True(EditorGUI.Foldout(r, true, "x"));
            Assert.Equal(20, EditorGUI.IntPopup(r, 20, new[] { "a" }, new[] { 10 }));
            EditorGUI.MinMaxSlider(r, ref min, ref max, 0f, 10f);
            Assert.Equal((1f, 2f), (min, max));
            Assert.False(EditorGUILayout.Foldout(false, "x"));
            Assert.Equal(30, EditorGUILayout.IntPopup(30, new[] { "a" }, new[] { 10 }));
        }

        // The Editor skin's slider and min-max thumb paddings (editorskin): 19 points per unit on a
        // 200-point track over 0..10.
        static readonly GUIStyle s_Slider = new GUIStyle { padding = new RectOffset(-1, -1, 0, 0) };
        static readonly GUIStyle s_Thumb = new GUIStyle { padding = new RectOffset(6, 6, 0, 0) };

        static void Slide(Rect r, ref float min, ref float max)
        {
            float size = max - min;
            int id = GUIUtility.GetControlID("EditorMinMaxSlider".GetHashCode(), FocusType.Passive);
            EditorGUIExt.DoMinMaxSlider(r, id, ref min, ref size, 0f, 10f, 0f, 10f, s_Slider, s_Thumb, true);
            max = min + size;
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
                // script/minMax.thumb.*: the thumb dragged 20 points moves both ends by 20/19.
                Pass(Mouse(EventType.MouseDown, r.x + 80, r.center.y), () => { Slide(r, ref min, ref max); return 0; });
                Pass(new Event { type = EventType.MouseDrag, mousePosition = new Vector2(r.x + 100, r.center.y), delta = new Vector2(20, 0) },
                    () => { Slide(r, ref min, ref max); return 0; });
                Assert.Equal(3.05263162f, min, 6);
                Assert.Equal(7.05263138f, max, 6);
                Pass(Mouse(EventType.MouseUp, r.x + 100, r.center.y), () => { Slide(r, ref min, ref max); return 0; });
                Assert.Equal(0, GUIUtility.hotControl);
            }
            finally { Reset(); }
        }
    }
}
