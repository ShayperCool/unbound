using System;
using System.Collections.Generic;
using UnityEditorInternal;
using Xunit;

namespace UnityEngine.Tests
{
    // Expected values: tests/projects/reorderableinput (Unity 6000.0.71f1 Game view OnGUI).
    [Collection("Serial")]
    public sealed class ReorderableListInputTests : IDisposable
    {
        readonly IDisposable m_Release = TestRelease.Pin(TestRelease.Measured);

        public ReorderableListInputTests() => Reset();

        public void Dispose()
        {
            Reset();
            m_Release.Dispose();
        }

        static void Reset()
        {
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }

        static readonly Rect Area = new Rect(10, 10, 300, 200);

        // A row's middle: header 20, top padding 4, rows of 23.
        static Vector2 Row(float row) => new Vector2(Area.x + 150, Area.y + 20 + 4 + 23 * row + 11.5f);

        static Vector2 Add(int rows) => new Vector2(Area.xMax - 10 - 8 - 50 + 4 + 12, Area.y + 20 + 4 + 23 * rows + 4 + 8);

        static Vector2 Remove(int rows) => new Vector2(Area.xMax - 10 - 29 + 12, Area.y + 20 + 4 + 23 * rows + 4 + 8);

        static bool Pass(Event e, ReorderableList list)
        {
            var previous = Event.current;
            Event.current = e;
            GUIUtility.BeginGUI(0, 51, 0);
            try
            {
                list.DoList(Area);
                return e.type == EventType.Used;
            }
            finally
            {
                GUIUtility.EndGUI(0);
                Event.current = previous;
            }
        }

        static Event Mouse(EventType type, Vector2 position, EventModifiers modifiers = EventModifiers.None) =>
            new Event { type = type, mousePosition = position, button = 0, clickCount = 1, modifiers = modifiers };

        static (ReorderableList List, List<string> Items, List<string> Calls) Logged(params string[] items)
        {
            var elements = new List<string>(items);
            var calls = new List<string>();
            var list = new ReorderableList(elements, typeof(string));
            list.onSelectCallback = l => calls.Add("select " + l.index);
            list.onMouseUpCallback = l => calls.Add("mouseUp " + l.index);
            list.onMouseDragCallback = l => calls.Add("mouseDrag " + l.index);
            list.onReorderCallbackWithDetails = (l, from, to) => calls.Add("reorderDetails " + from + ">" + to);
            list.onReorderCallback = l => calls.Add("reorder");
            list.onChangedCallback = l => calls.Add("changed " + l.index + "/" + l.count);
            return (list, elements, calls);
        }

        [Fact]
        public void AClickSelectsAndTheArrowKeysWrapOnTheFocusedList()
        {
            var (list, _, calls) = Logged("a", "b", "c", "d");
            // script/full.click.*: down selects, takes the hot control and the focus; up calls mouseUp.
            Assert.True(Pass(Mouse(EventType.MouseDown, Row(1)), list));
            Assert.Equal(1, list.index);
            Assert.NotEqual(0, GUIUtility.hotControl);
            Assert.NotEqual(0, GUIUtility.keyboardControl);
            Assert.True(Pass(Mouse(EventType.MouseUp, Row(1)), list));
            Assert.Equal(0, GUIUtility.hotControl);
            Assert.Equal(new[] { "select 1", "mouseUp 1" }, calls);
            // script/full.key.*: down to the end, round to the start, up round to the end.
            Pass(Event.KeyboardEvent("down"), list);
            Pass(Event.KeyboardEvent("down"), list);
            Assert.Equal(3, list.index);
            Pass(Event.KeyboardEvent("down"), list);
            Assert.Equal(0, list.index);
            Pass(Event.KeyboardEvent("up"), list);
            Assert.Equal(3, list.index);
        }

        [Fact]
        public void DraggingAFirstRowBelowTheThirdMovesIt()
        {
            var (list, items, calls) = Logged("a", "b", "c", "d");
            // script/full.drag.*: moved to the end, reorder with details, changed, then select.
            Pass(Mouse(EventType.MouseDown, Row(0)), list);
            calls.Clear();
            Pass(Mouse(EventType.MouseDrag, Row(1.2f)), list);
            Pass(Mouse(EventType.MouseDrag, Row(2.6f)), list);
            Assert.Equal(new[] { "mouseDrag 0", "mouseDrag 0" }, calls);
            calls.Clear();
            Assert.True(Pass(Mouse(EventType.MouseUp, Row(2.6f)), list));
            Assert.Equal("b,c,d,a", string.Join(",", items));
            Assert.Equal(new[] { "reorderDetails 0>3", "changed 3/4", "select 3" }, calls);
            Assert.Equal(0, GUIUtility.hotControl);
        }

        [Fact]
        public void EscapeDoesNotStopADrag()
        {
            var (list, items, _) = Logged("b", "c", "d", "a");
            // script/full.escape.*: a dragging list ignores keys; the drop still moves the row.
            Pass(Mouse(EventType.MouseDown, Row(3)), list);
            Pass(Mouse(EventType.MouseDrag, Row(0.5f)), list);
            Assert.False(Pass(Event.KeyboardEvent("escape"), list));
            Assert.NotEqual(0, GUIUtility.hotControl);
            Pass(Mouse(EventType.MouseUp, Row(0.5f)), list);
            Assert.Equal("b,a,c,d", string.Join(",", items));
        }

        [Fact]
        public void TheFootersDefaultAddAndRemoveAndDelete()
        {
            var (list, items, calls) = Logged("a", "b", "c");
            // script/plain.add.*: an empty string at the end, selected.
            Pass(Mouse(EventType.MouseDown, Add(3)), list);
            Pass(Mouse(EventType.MouseUp, Add(3)), list);
            Assert.Equal("a,b,c,", string.Join(",", items));
            Assert.Equal(3, list.index);
            // script/plain.remove.*: the selected row; the one above it selected.
            Pass(Mouse(EventType.MouseDown, Row(1)), list);
            Pass(Mouse(EventType.MouseUp, Row(1)), list);
            Pass(Mouse(EventType.MouseDown, Remove(4)), list);
            Pass(Mouse(EventType.MouseUp, Remove(4)), list);
            Assert.Equal("a,c,", string.Join(",", items));
            Assert.Equal(0, list.index);
            // script/plain.delete.*: Delete on the focused list removes the selected row.
            calls.Clear();
            Assert.True(Pass(Event.KeyboardEvent("delete"), list));
            Assert.Equal("c,", string.Join(",", items));
            Assert.Equal(new[] { "changed 0/2" }, calls);
        }

        [Fact]
        public void RefusedAddAndRemoveDisableTheirButtons()
        {
            var items = new List<string> { "a", "b" };
            var calls = new List<string>();
            var list = new ReorderableList(items, typeof(string))
            {
                onCanAddCallback = _ => false,
                onCanRemoveCallback = _ => false,
                onAddCallback = _ => calls.Add("add"),
                onRemoveCallback = _ => calls.Add("remove"),
            };
            // script/refusing.*.
            Pass(Mouse(EventType.MouseDown, Row(0)), list);
            Pass(Mouse(EventType.MouseUp, Row(0)), list);
            Assert.False(Pass(Mouse(EventType.MouseDown, Add(2)), list));
            Assert.False(Pass(Mouse(EventType.MouseUp, Add(2)), list));
            Assert.False(Pass(Mouse(EventType.MouseDown, Remove(2)), list));
            Assert.False(Pass(Mouse(EventType.MouseUp, Remove(2)), list));
            Assert.Empty(calls);
            Assert.Equal("a,b", string.Join(",", items));
        }

        [Fact]
        public void AMultiSelectListTogglesWithTheActionKeyAndSelectsRangesWithShift()
        {
            var list = new ReorderableList(new List<string> { "a", "b", "c", "d", "e" }, typeof(string)) { multiSelect = true };
            // script/multi.*.
            Pass(Mouse(EventType.MouseDown, Row(1)), list);
            Pass(Mouse(EventType.MouseUp, Row(1)), list);
            Pass(Mouse(EventType.MouseDown, Row(3), EventModifiers.Control), list);
            Pass(Mouse(EventType.MouseUp, Row(3), EventModifiers.Control), list);
            Assert.Equal(new[] { 1, 3 }, list.selectedIndices);
            Pass(Mouse(EventType.MouseDown, Row(1), EventModifiers.Control), list);
            Pass(Mouse(EventType.MouseUp, Row(1), EventModifiers.Control), list);
            Assert.Equal(new[] { 3 }, list.selectedIndices);
            Pass(Mouse(EventType.MouseDown, Row(0), EventModifiers.Shift), list);
            Pass(Mouse(EventType.MouseUp, Row(0), EventModifiers.Shift), list);
            Assert.Equal(new[] { 0, 1, 2, 3 }, list.selectedIndices);
        }

        [Fact]
        public void ANonDraggableListCallsMouseUpOnItsSelectedRowOnly()
        {
            var calls = new List<string>();
            var list = new ReorderableList(new List<string> { "a", "b", "c" }, typeof(string), false, true, true, true);
            list.onMouseUpCallback = l => calls.Add("mouseUp " + l.index);
            // script/fixed.*: no hot control; mouse up unused.
            Pass(Mouse(EventType.MouseDown, Row(2)), list);
            Assert.Equal(0, GUIUtility.hotControl);
            Assert.False(Pass(Mouse(EventType.MouseUp, Row(2)), list));
            Assert.False(Pass(Mouse(EventType.MouseUp, Row(0)), list));
            Assert.Equal(new[] { "mouseUp 2" }, calls);
        }
    }
}
