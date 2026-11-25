using System;
using UnityEditor;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/imguitreeview Reference (clip/*, draw/*, script/*), Unity
    // 6000.0.71f1 Game view OnGUI.
    [Collection("Serial")]
    public sealed class GuiClipRootTests
    {
        static void Pass(EventType type, Action body, int scope = 4711)
        {
            var previous = Event.current;
            Event.current = new Event { type = type, mousePosition = new Vector2(20f, 30f) };
            GUIUtility.BeginGUI(0, scope, 0);
            try { body(); }
            finally
            {
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        static float[] R(Rect r) => new[] { r.x, r.y, r.width, r.height };

        [Fact]
        public void AGameOnGuisRootClipIsFortyThousandPointsWide()
        {
            Pass(EventType.Repaint, () =>
            {
                Assert.Equal(new[] { -10000f, -10000f, 40000f, 40000f }, R(GUIClip.visibleRect));
                Assert.Equal(new[] { -10000f, -10000f, 40000f, 40000f }, R(GUIClip.topmostRect));
            });
        }

        [Fact]
        public void AGroupBeyondTheScreenKeepsItsVisibleRectAndTheTopmostRectIsAbsolute()
        {
            Pass(EventType.Layout, () =>
            {
                GUI.BeginGroup(new Rect(Screen.width + 60f, 300f, 100f, 50f));
                Assert.Equal(new[] { 0f, 0f, 100f, 50f }, R(GUIClip.visibleRect));
                Assert.Equal(new[] { Screen.width + 60f, 300f, 100f, 50f }, R(GUIClip.topmostRect));
                GUI.BeginGroup(new Rect(-5f, -5f, 20f, 20f));
                Assert.Equal(new[] { 5f, 5f, 15f, 15f }, R(GUIClip.visibleRect));
                Assert.Equal(new[] { Screen.width + 60f, 300f, 15f, 15f }, R(GUIClip.topmostRect));
                GUI.EndGroup();
                GUI.EndGroup();
            });
        }

        [Fact]
        public void AGroupWithNothingVisibleHasAZeroVisibleRect()
        {
            Pass(EventType.Repaint, () =>
            {
                GUI.BeginGroup(new Rect(600f, 300f, 100f, 50f));
                GUI.BeginGroup(new Rect(10f, 60f, 20f, 20f));
                Assert.Equal(new[] { 0f, 0f, 0f, 0f }, R(GUIClip.visibleRect));
                Assert.Equal(new[] { 610f, 360f, 20f, 0f }, R(GUIClip.topmostRect));
                GUI.EndGroup();
                GUI.BeginGroup(new Rect(100f, 10f, 20f, 20f));
                Assert.Equal(new[] { 0f, 0f, 0f, 0f }, R(GUIClip.visibleRect));
                Assert.Equal(new[] { 700f, 310f, 0f, 20f }, R(GUIClip.topmostRect));
                GUI.EndGroup();
                GUI.EndGroup();
            });
        }

        [Fact]
        public void AScrollViewTakesSevenIdsInLayoutWithoutShrinkingForScrollbars()
        {
            int layoutIds = 0;
            Rect layoutVisible = default;
            Pass(EventType.Layout, () =>
            {
                int before = GUIUtility.GetControlID(FocusType.Passive);
                GUI.BeginScrollView(new Rect(10f, 300f, 100f, 50f), new Vector2(5f, 7f), new Rect(0f, 0f, 300f, 300f));
                layoutVisible = GUIClip.visibleRect;
                GUI.EndScrollView();
                layoutIds = GUIUtility.GetControlID(FocusType.Passive) - before - 1;
            });
            Assert.Equal(7, layoutIds);
            Assert.Equal(new[] { 5f, 7f, 100f, 50f }, R(layoutVisible));
        }

        [Theory]
        [InlineData(EventType.Layout, 5f, 7f)]
        [InlineData(EventType.Used, 5f, 7f)]
        [InlineData(EventType.Repaint, 0f, 0f)]
        public void AScrollViewClampsItsPositionOnlyWhenItLaysOutItsScrollbars(EventType type, float x, float y)
        {
            // A view smaller than its rect has no scrollbars: outside Layout and a used event the
            // position goes back to 0 (Unity's GUI.BeginScrollView).
            Vector2 position = default;
            Pass(type, () =>
            {
                position = GUI.BeginScrollView(new Rect(10f, 10f, 100f, 50f), new Vector2(5f, 7f), new Rect(0f, 0f, 50f, 20f));
                GUI.EndScrollView();
            });
            Assert.Equal(new Vector2(x, y), position);
        }

        [Fact]
        public void AClipGivesAnEventThatReplacesTheCurrentOneThePassesMousePosition()
        {
            // tests/projects/imguitreeview script/*: inside a clip the rows see the real cursor,
            // not a scripted event's position, and after the pops the scripted position is gone
            // too (measured); that it is then exactly the pass's position is the model's.
            Vector2 inGroup = default, afterGroup = default;
            Pass(EventType.Repaint, () =>
            {
                var real = Event.current;
                Event.current = new Event { type = EventType.MouseDown, mousePosition = new Vector2(5000f, 5000f) };
                try
                {
                    GUI.BeginGroup(new Rect(10f, 10f, 300f, 200f));
                    inGroup = Event.current.mousePosition;
                    GUI.EndGroup();
                    afterGroup = Event.current.mousePosition;
                }
                finally { Event.current = real; }
            });
            Assert.Equal(new Vector2(10f, 20f), inGroup);
            Assert.Equal(new Vector2(20f, 30f), afterGroup);
        }

        [Fact]
        public void AMouseEventsKeyCodeIsItsButtonsAndAWheelEventsItsDirection()
        {
            // Unity's Event.keyCode (Event.bindings.cs): Mouse0 + button for a mouse event,
            // WheelUp or WheelDown for a wheel event, whatever key code was set.
            Assert.Equal(KeyCode.Mouse0, new Event { type = EventType.MouseMove }.keyCode);
            Assert.Equal(KeyCode.Mouse2, new Event { type = EventType.MouseDown, button = 2, keyCode = KeyCode.A }.keyCode);
            Assert.Equal(KeyCode.WheelUp, new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, -1f) }.keyCode);
            Assert.Equal(KeyCode.WheelDown, new Event { type = EventType.ScrollWheel, delta = new Vector2(0f, 1f) }.keyCode);
            Assert.Equal(KeyCode.A, new Event { type = EventType.KeyDown, keyCode = KeyCode.A }.keyCode);
            Assert.Equal(KeyCode.None, new Event { type = EventType.Repaint }.keyCode);
        }

        [Fact]
        public void AGameOnGuiStartsWithoutAnOverriddenViewWidth()
        {
            EditorGUIUtility.currentViewWidth = 123f;
            try
            {
                Pass(EventType.Layout, () => Assert.Equal((float)Screen.width, EditorGUIUtility.currentViewWidth));
            }
            finally { EditorGUIUtility.currentViewWidth = 0f; }
        }
    }
}
