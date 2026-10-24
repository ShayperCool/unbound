using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The parts of UnityGUI that need no font: layout groups, events and text editing.
    /// Drawing and dispatch are checked by the `hello` fixture project.</summary>
    public class IMGUITests
    {
        static GUIStyle Margins(int m) => new GUIStyle { margin = new RectOffset(m, m, m, m) };

        static GUILayoutEntry Entry(float width, float height, GUIStyle style, params GUILayoutOption[] options) =>
            new GUILayoutEntry(width, width, height, height, style, options);

        [Fact]
        public void ARowCollapsesMarginsAndGivesExtraSpaceToStretchingEntries()
        {
            var row = new GUILayoutGroup { isVertical = false };
            var a = Entry(30, 20, Margins(4), GUILayout.ExpandWidth(false));
            var flexible = Entry(0, 0, new GUIStyle { stretchWidth = false }, GUILayout.ExpandWidth(true));
            flexible.consideredForMargin = false;
            var b = Entry(50, 20, Margins(6), GUILayout.Width(50));
            row.Add(a);
            row.Add(flexible);
            row.Add(b);
            row.CalcWidth();
            // The row's own margins are its outer children's.
            Assert.Equal(4, row.marginLeft);
            Assert.Equal(6, row.marginRight);
            Assert.Equal(30 + 6 + 50, row.minWidth);
            row.SetHorizontal(0, 200);
            Assert.Equal(0, a.rect.x);
            Assert.Equal(30, a.rect.width);
            Assert.Equal(200, b.rect.xMax);
            Assert.Equal(50, b.rect.width);
            Assert.Equal(200 - 30 - 6 - 50, flexible.rect.width);
        }

        [Fact]
        public void AColumnStacksEntriesWithTheLargerMarginBetween()
        {
            var column = new GUILayoutGroup { isVertical = true };
            var a = Entry(40, 10, Margins(4));
            var b = Entry(60, 15, Margins(8));
            column.Add(a);
            column.Add(b);
            column.CalcWidth();
            column.SetHorizontal(0, 100);
            column.CalcHeight();
            column.SetVertical(0, 100);
            Assert.Equal(0, a.rect.y);
            Assert.Equal(10 + 8, b.rect.y);
            Assert.Equal(10 + 8 + 15, column.minHeight);
            // Stretching entries span the column (less the margin beyond the column's own).
            Assert.Equal(0, a.rect.x);
            Assert.Equal(100, a.rect.width);
            Assert.Equal(4, b.rect.x);
            Assert.Equal(100 - 8, b.rect.width);
        }

        [Fact]
        public void AStyledGroupPadsItsChildren()
        {
            var box = new GUIStyle { padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(2, 2, 2, 2) };
            var group = new GUILayoutGroup(box, null) { isVertical = true };
            var a = Entry(40, 10, Margins(4), GUILayout.ExpandWidth(false));
            group.Add(a);
            group.CalcWidth();
            group.SetHorizontal(100, 80);
            group.CalcHeight();
            group.SetVertical(50, 40);
            Assert.Equal(2, group.marginLeft);
            Assert.Equal(40 + 20, group.minWidth);
            Assert.Equal(110, a.rect.x);
            Assert.Equal(56, a.rect.y);
        }

        [Fact]
        public void KeyboardEventsParseModifiersAndKeys()
        {
            var e = Event.KeyboardEvent("^#a");
            Assert.Equal(EventType.KeyDown, e.type);
            Assert.True(e.control && e.shift && !e.alt);
            Assert.Equal(KeyCode.A, e.keyCode);
            // As in Unity, a character key with modifiers carries no character.
            Assert.Equal('\0', e.character);
            Assert.Equal('a', Event.KeyboardEvent("a").character);
            var f = Event.KeyboardEvent("&F5");
            Assert.True(f.alt && f.functionKey);
            Assert.Equal(KeyCode.F5, f.keyCode);
            Assert.Equal(KeyCode.Return, Event.KeyboardEvent("return").keyCode);
            // Editing keys carry the function key modifier (text fields match them with it).
            Assert.True(Event.KeyboardEvent("backspace").functionKey);
            Assert.False(Event.KeyboardEvent("tab").functionKey);
            Assert.Equal(KeyCode.PageDown, Event.KeyboardEvent("pgup").keyCode);
        }

        [Fact]
        public void MouseEventsBelongToTheHotControl()
        {
            var e = new Event { type = EventType.MouseDown };
            GUIUtility.hotControl = 0;
            Assert.Equal(EventType.MouseDown, e.GetTypeForControl(5));
            GUIUtility.hotControl = 7;
            Assert.Equal(EventType.Ignore, e.GetTypeForControl(5));
            Assert.Equal(EventType.MouseDown, e.GetTypeForControl(7));
            var key = new Event { type = EventType.KeyDown };
            Assert.Equal(EventType.KeyDown, key.GetTypeForControl(5));
            GUIUtility.hotControl = 0;
            e.Use();
            Assert.Equal(EventType.Used, e.type);
        }

        [Fact]
        public void TheTextEditorEditsWordsAndUsesTheCopyBuffer()
        {
            var editor = new TextEditor { text = "hello big world" };
            editor.MoveTextEnd();
            editor.DeleteWordBack();
            Assert.Equal("hello big ", editor.text);
            editor.MoveWordLeft();
            Assert.Equal(6, editor.cursorIndex);
            editor.SelectAll();
            editor.Copy();
            Assert.Equal("hello big ", GUIUtility.systemCopyBuffer);
            GUIUtility.systemCopyBuffer = "a\nb";
            editor.MoveTextEnd();
            editor.Paste();
            // A single-line editor pastes newlines as spaces.
            Assert.Equal("hello big a b", editor.text);
            editor.SaveBackup();
            editor.Backspace();
            Assert.Equal("hello big a ", editor.text);
            editor.Undo();
            Assert.Equal("hello big a b", editor.text);
            Assert.True(editor.HandleKeyEvent(Event.KeyboardEvent("home")));
            Assert.Equal(0, editor.cursorIndex);
        }
    }
}
