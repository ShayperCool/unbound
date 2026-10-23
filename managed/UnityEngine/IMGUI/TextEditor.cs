using System;
using System.Text;

namespace UnityEngine
{
    /// <summary>
    /// The editing state of a text field: the text, the cursor and the selection's other end,
    /// with the editing operations keys map to. Copy and paste go through
    /// <see cref="GUIUtility.systemCopyBuffer"/>.
    /// </summary>
    public partial class TextEditor
    {
        public enum DblClickSnapping : byte
        {
            WORDS = 0,
            PARAGRAPHS = 1,
        }

        public TouchScreenKeyboard keyboardOnScreen;
        public int controlID;
        public GUIStyle style = GUIStyle.none;
        public bool multiline;
        public bool hasHorizontalCursorPos;
        public bool isPasswordField;
        public Vector2 scrollOffset;
        public bool revealCursor;
        public Vector2 graphicalCursorPos;
        public Vector2 graphicalSelectCursorPos;

        string m_Text = "";
        Rect m_Position;
        int m_CursorIndex;
        int m_SelectIndex;
        bool m_Focused;
        bool m_MouseDragSelectsWholeWords;
        int m_DblClickInitPos;
        DblClickSnapping m_DblClickSnap = DblClickSnapping.WORDS;
        string m_UndoText = "";
        int m_UndoCursor, m_UndoSelect;

        public TextEditor() { }

        public string text
        {
            get => m_Text;
            set
            {
                m_Text = value ?? "";
                ClampIndices();
            }
        }

        [Obsolete("Please use 'text' instead of 'content'", true)]
        public GUIContent content
        {
            get => new GUIContent(m_Text);
            set => text = value?.text;
        }

        public Rect position
        {
            get => m_Position;
            set => m_Position = value;
        }

        public int cursorIndex
        {
            get => m_CursorIndex;
            set
            {
                m_CursorIndex = Mathf.Clamp(value, 0, m_Text.Length);
                revealCursor = true;
            }
        }

        public int selectIndex
        {
            get => m_SelectIndex;
            set => m_SelectIndex = Mathf.Clamp(value, 0, m_Text.Length);
        }

        public DblClickSnapping doubleClickSnapping
        {
            get => m_DblClickSnap;
            set => m_DblClickSnap = value;
        }

        public int altCursorPosition { get; set; } = -1;

        public bool hasSelection => m_CursorIndex != m_SelectIndex;

        public bool isMultiline
        {
            get => multiline;
            set => multiline = value;
        }

        public bool hasHorizontalCursor
        {
            get => hasHorizontalCursorPos;
            set => hasHorizontalCursorPos = value;
        }

        public bool showCursor { get; set; }

        internal bool m_HasFocus
        {
            get => m_Focused;
            set => m_Focused = value;
        }

        public string SelectedText
        {
            get
            {
                if (!hasSelection) return "";
                int from = Math.Min(m_CursorIndex, m_SelectIndex), to = Math.Max(m_CursorIndex, m_SelectIndex);
                return m_Text.Substring(from, to - from);
            }
        }

        void ClampIndices()
        {
            m_CursorIndex = Mathf.Clamp(m_CursorIndex, 0, m_Text.Length);
            m_SelectIndex = Mathf.Clamp(m_SelectIndex, 0, m_Text.Length);
        }

        public void OnFocus()
        {
            m_Focused = true;
            if (multiline) m_CursorIndex = m_SelectIndex = 0;
            else SelectAll();
        }

        public void OnLostFocus()
        {
            m_Focused = false;
            scrollOffset = Vector2.zero;
        }

        public void DetectFocusChange()
        {
            bool focused = GUIUtility.keyboardControl == controlID && controlID != 0;
            if (m_Focused && !focused) OnLostFocus();
            else if (!m_Focused && focused) OnFocus();
        }

        public bool HasClickedOnLink(Vector2 mousePosition, out string linkData)
        {
            linkData = "";
            return false;
        }

        public bool HasClickedOnHREF(Vector2 mousePosition, out string href)
        {
            href = "";
            return false;
        }

        /// <summary>Editing keys (arrows, Home and End, deletion, clipboard shortcuts); true when
        /// the key did something.</summary>
        public bool HandleKeyEvent(Event e)
        {
            if (e.type != EventType.KeyDown) return false;
            bool shift = e.shift;
            bool word = e.control || e.alt;
            bool shortcut = e.control || e.command;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow:
                    if (shift) { if (word) SelectWordLeft(); else SelectLeft(); }
                    else if (word) MoveWordLeft();
                    else MoveLeft();
                    return true;
                case KeyCode.RightArrow:
                    if (shift) { if (word) SelectWordRight(); else SelectRight(); }
                    else if (word) MoveWordRight();
                    else MoveRight();
                    return true;
                case KeyCode.UpArrow:
                    if (shift) SelectUp(); else MoveUp();
                    return true;
                case KeyCode.DownArrow:
                    if (shift) SelectDown(); else MoveDown();
                    return true;
                case KeyCode.Home:
                    if (shift) { if (shortcut) SelectTextStart(); else SelectGraphicalLineStart(); }
                    else if (shortcut) MoveTextStart();
                    else MoveGraphicalLineStart();
                    return true;
                case KeyCode.End:
                    if (shift) { if (shortcut) SelectTextEnd(); else SelectGraphicalLineEnd(); }
                    else if (shortcut) MoveTextEnd();
                    else MoveGraphicalLineEnd();
                    return true;
                case KeyCode.Backspace:
                    if (word) DeleteWordBack(); else Backspace();
                    return true;
                case KeyCode.Delete:
                    if (shift && !word) Cut();
                    else if (word) DeleteWordForward();
                    else Delete();
                    return true;
                case KeyCode.Insert:
                    if (shift) { Paste(); return true; }
                    if (shortcut) { Copy(); return true; }
                    return false;
            }
            if (!shortcut) return false;
            switch (e.keyCode)
            {
                case KeyCode.A:
                    SelectAll();
                    return true;
                case KeyCode.C:
                    Copy();
                    return true;
                case KeyCode.X:
                    Cut();
                    return true;
                case KeyCode.V:
                    Paste();
                    return true;
                case KeyCode.Z:
                    Undo();
                    return true;
            }
            return false;
        }

        public bool DeleteLineBack()
        {
            if (hasSelection) return DeleteSelection();
            int p = m_CursorIndex;
            int start = p;
            while (start > 0 && m_Text[start - 1] != '\n') start--;
            if (start == p && start > 0) start--;
            if (start == p) return false;
            m_Text = m_Text.Remove(start, p - start);
            m_CursorIndex = m_SelectIndex = start;
            return true;
        }

        public bool DeleteWordBack()
        {
            if (hasSelection) return DeleteSelection();
            int prev = FindEndOfPreviousWord(m_CursorIndex);
            if (m_CursorIndex == prev) return false;
            m_Text = m_Text.Remove(prev, m_CursorIndex - prev);
            m_CursorIndex = m_SelectIndex = prev;
            return true;
        }

        public bool DeleteWordForward()
        {
            if (hasSelection) return DeleteSelection();
            int next = FindStartOfNextWord(m_CursorIndex);
            if (m_CursorIndex >= m_Text.Length) return false;
            m_Text = m_Text.Remove(m_CursorIndex, next - m_CursorIndex);
            return true;
        }

        public bool Delete()
        {
            if (hasSelection) return DeleteSelection();
            if (m_CursorIndex >= m_Text.Length) return false;
            int length = char.IsHighSurrogate(m_Text[m_CursorIndex]) && m_CursorIndex + 1 < m_Text.Length ? 2 : 1;
            m_Text = m_Text.Remove(m_CursorIndex, length);
            return true;
        }

        public bool CanPaste() => GUIUtility.systemCopyBuffer.Length != 0;

        public bool Backspace()
        {
            if (hasSelection) return DeleteSelection();
            if (m_CursorIndex <= 0) return false;
            int length = m_CursorIndex >= 2 && char.IsLowSurrogate(m_Text[m_CursorIndex - 1]) ? 2 : 1;
            m_Text = m_Text.Remove(m_CursorIndex - length, length);
            m_CursorIndex = m_SelectIndex = m_CursorIndex - length;
            return true;
        }

        public void SelectAll()
        {
            m_CursorIndex = 0;
            m_SelectIndex = m_Text.Length;
        }

        public void SelectNone() => m_SelectIndex = m_CursorIndex;

        public bool DeleteSelection()
        {
            if (!hasSelection) return false;
            int from = Math.Min(m_CursorIndex, m_SelectIndex), to = Math.Max(m_CursorIndex, m_SelectIndex);
            m_Text = m_Text.Remove(from, to - from);
            m_CursorIndex = m_SelectIndex = from;
            return true;
        }

        public void ReplaceSelection(string replace)
        {
            DeleteSelection();
            replace ??= "";
            m_Text = m_Text.Insert(m_CursorIndex, replace);
            m_CursorIndex = m_SelectIndex = m_CursorIndex + replace.Length;
        }

        public void Insert(char c) => ReplaceSelection(c.ToString());

        public void MoveSelectionToAltCursor()
        {
            if (altCursorPosition < 0) return;
            int p = altCursorPosition;
            string selected = SelectedText;
            m_Text = m_Text.Insert(p, selected);
            if (p < m_CursorIndex)
            {
                m_CursorIndex += selected.Length;
                m_SelectIndex += selected.Length;
            }
            DeleteSelection();
            m_SelectIndex = m_CursorIndex = p;
        }

        public void MoveRight()
        {
            if (hasSelection) m_CursorIndex = m_SelectIndex = Math.Max(m_CursorIndex, m_SelectIndex);
            else m_CursorIndex = m_SelectIndex = NextIndex(m_CursorIndex);
        }

        public void MoveLeft()
        {
            if (hasSelection) m_CursorIndex = m_SelectIndex = Math.Min(m_CursorIndex, m_SelectIndex);
            else m_CursorIndex = m_SelectIndex = PreviousIndex(m_CursorIndex);
        }

        int NextIndex(int i) => i >= m_Text.Length ? m_Text.Length : char.IsHighSurrogate(m_Text[i]) && i + 2 <= m_Text.Length ? i + 2 : i + 1;

        int PreviousIndex(int i) => i <= 0 ? 0 : i >= 2 && char.IsLowSurrogate(m_Text[i - 1]) ? i - 2 : i - 1;

        int LineStart(int i)
        {
            while (i > 0 && m_Text[i - 1] != '\n') i--;
            return i;
        }

        int LineEnd(int i)
        {
            while (i < m_Text.Length && m_Text[i] != '\n') i++;
            return i;
        }

        /// <summary>The same column on the line above or below.</summary>
        int VerticalMove(int index, int direction)
        {
            int start = LineStart(index);
            int column = index - start;
            if (direction < 0)
            {
                if (start == 0) return 0;
                int prevStart = LineStart(start - 1);
                return Math.Min(prevStart + column, start - 1);
            }
            int end = LineEnd(index);
            if (end >= m_Text.Length) return m_Text.Length;
            int nextStart = end + 1;
            return Math.Min(nextStart + column, LineEnd(nextStart));
        }

        public void MoveUp() => m_CursorIndex = m_SelectIndex = VerticalMove(Math.Min(m_CursorIndex, m_SelectIndex), -1);

        public void MoveDown() => m_CursorIndex = m_SelectIndex = VerticalMove(Math.Max(m_CursorIndex, m_SelectIndex), 1);

        public void MoveLineStart() => m_CursorIndex = m_SelectIndex = LineStart(Math.Min(m_CursorIndex, m_SelectIndex));

        public void MoveLineEnd() => m_CursorIndex = m_SelectIndex = LineEnd(Math.Max(m_CursorIndex, m_SelectIndex));

        public void MoveGraphicalLineStart() => MoveLineStart();

        public void MoveGraphicalLineEnd() => MoveLineEnd();

        public void MoveTextStart() => m_CursorIndex = m_SelectIndex = 0;

        public void MoveTextEnd() => m_CursorIndex = m_SelectIndex = m_Text.Length;

        public void MoveParagraphForward()
        {
            int i = Math.Max(m_CursorIndex, m_SelectIndex);
            if (i < m_Text.Length) i = Math.Min(m_Text.Length, LineEnd(i + 1));
            m_CursorIndex = m_SelectIndex = i;
        }

        public void MoveParagraphBackward()
        {
            int i = Math.Min(m_CursorIndex, m_SelectIndex);
            m_CursorIndex = m_SelectIndex = i > 1 ? LineStart(i - 1) : 0;
        }

        int IndexAt(Vector2 cursorPosition)
        {
            var shown = isPasswordField ? new string('*', m_Text.Length) : m_Text;
            return style.GetCursorStringIndex(m_Position, new GUIContent(shown), cursorPosition + scrollOffset);
        }

        public void MoveCursorToPosition(Vector2 cursorPosition) => MoveCursorToPosition_Internal(cursorPosition, Event.current != null && Event.current.shift);

        protected internal void MoveCursorToPosition_Internal(Vector2 cursorPosition, bool shift)
        {
            m_CursorIndex = IndexAt(cursorPosition);
            if (!shift) m_SelectIndex = m_CursorIndex;
            m_DblClickInitPos = m_CursorIndex;
        }

        public void MoveAltCursorToPosition(Vector2 cursorPosition) => altCursorPosition = Mathf.Clamp(IndexAt(cursorPosition), 0, m_Text.Length);

        public bool IsOverSelection(Vector2 cursorPosition)
        {
            int p = IndexAt(cursorPosition);
            return p > Math.Min(m_CursorIndex, m_SelectIndex) && p < Math.Max(m_CursorIndex, m_SelectIndex);
        }

        /// <summary>Extends the selection to a point (whole words or lines after a double or
        /// triple click).</summary>
        public void SelectToPosition(Vector2 cursorPosition)
        {
            int p = IndexAt(cursorPosition);
            if (!m_MouseDragSelectsWholeWords)
            {
                m_CursorIndex = p;
                return;
            }
            if (m_DblClickSnap == DblClickSnapping.WORDS)
            {
                if (p < m_DblClickInitPos)
                {
                    m_CursorIndex = FindEndOfPreviousWord(p);
                    m_SelectIndex = FindStartOfNextWord(m_DblClickInitPos);
                }
                else
                {
                    m_CursorIndex = FindStartOfNextWord(p);
                    m_SelectIndex = FindEndOfPreviousWord(m_DblClickInitPos);
                }
            }
            else
            {
                if (p < m_DblClickInitPos)
                {
                    m_CursorIndex = LineStart(p);
                    m_SelectIndex = LineEnd(m_DblClickInitPos);
                }
                else
                {
                    m_CursorIndex = LineEnd(p);
                    m_SelectIndex = LineStart(m_DblClickInitPos);
                }
            }
        }

        public void SelectLeft() => m_CursorIndex = PreviousIndex(m_CursorIndex);

        public void SelectRight() => m_CursorIndex = NextIndex(m_CursorIndex);

        public void SelectUp() => m_CursorIndex = VerticalMove(m_CursorIndex, -1);

        public void SelectDown() => m_CursorIndex = VerticalMove(m_CursorIndex, 1);

        public void SelectTextEnd() => m_CursorIndex = m_Text.Length;

        public void SelectTextStart() => m_CursorIndex = 0;

        public void MouseDragSelectsWholeWords(bool on)
        {
            m_MouseDragSelectsWholeWords = on;
            m_DblClickInitPos = m_CursorIndex;
        }

        public void DblClickSnap(DblClickSnapping snapping) => m_DblClickSnap = snapping;

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        public int FindStartOfNextWord(int p)
        {
            int length = m_Text.Length;
            if (p >= length) return length;
            if (IsWord(m_Text[p]))
                while (p < length && IsWord(m_Text[p])) p++;
            else if (!char.IsWhiteSpace(m_Text[p]))
                while (p < length && !IsWord(m_Text[p]) && !char.IsWhiteSpace(m_Text[p])) p++;
            while (p < length && char.IsWhiteSpace(m_Text[p]) && m_Text[p] != '\n') p++;
            if (p < length && m_Text[p] == '\n' && p == 0) p++;
            return p;
        }

        int FindEndOfPreviousWord(int p)
        {
            if (p <= 0) return 0;
            p--;
            while (p > 0 && char.IsWhiteSpace(m_Text[p]) && m_Text[p] != '\n') p--;
            if (IsWord(m_Text[p]))
                while (p > 0 && IsWord(m_Text[p - 1])) p--;
            else if (!char.IsWhiteSpace(m_Text[p]))
                while (p > 0 && !IsWord(m_Text[p - 1]) && !char.IsWhiteSpace(m_Text[p - 1])) p--;
            return p;
        }

        public void MoveWordRight() => m_CursorIndex = m_SelectIndex = FindStartOfNextWord(Math.Max(m_CursorIndex, m_SelectIndex));

        public void MoveToStartOfNextWord() => MoveWordRight();

        public void MoveToEndOfPreviousWord() => MoveWordLeft();

        public void SelectToStartOfNextWord() => m_CursorIndex = FindStartOfNextWord(m_CursorIndex);

        public void SelectToEndOfPreviousWord() => m_CursorIndex = FindEndOfPreviousWord(m_CursorIndex);

        public void MoveWordLeft() => m_CursorIndex = m_SelectIndex = FindEndOfPreviousWord(Math.Min(m_CursorIndex, m_SelectIndex));

        public void SelectWordRight() => m_CursorIndex = FindStartOfNextWord(m_CursorIndex);

        public void SelectWordLeft() => m_CursorIndex = FindEndOfPreviousWord(m_CursorIndex);

        public void ExpandSelectGraphicalLineStart()
        {
            if (m_CursorIndex < m_SelectIndex) m_CursorIndex = LineStart(m_CursorIndex);
            else
            {
                int temp = m_CursorIndex;
                m_CursorIndex = LineStart(m_SelectIndex);
                m_SelectIndex = temp;
            }
        }

        public void ExpandSelectGraphicalLineEnd()
        {
            if (m_CursorIndex > m_SelectIndex) m_CursorIndex = LineEnd(m_CursorIndex);
            else
            {
                int temp = m_CursorIndex;
                m_CursorIndex = LineEnd(m_SelectIndex);
                m_SelectIndex = temp;
            }
        }

        public void SelectGraphicalLineStart() => m_CursorIndex = LineStart(m_CursorIndex);

        public void SelectGraphicalLineEnd() => m_CursorIndex = LineEnd(m_CursorIndex);

        public void SelectParagraphForward()
        {
            bool wasBehind = m_CursorIndex < m_SelectIndex;
            if (m_CursorIndex < m_Text.Length)
            {
                m_CursorIndex = LineEnd(m_CursorIndex + 1);
                if (wasBehind && m_CursorIndex > m_SelectIndex) m_CursorIndex = m_SelectIndex;
            }
        }

        public void SelectParagraphBackward()
        {
            bool wasInFront = m_CursorIndex > m_SelectIndex;
            if (m_CursorIndex > 1)
            {
                m_CursorIndex = LineStart(m_CursorIndex - 1);
                if (wasInFront && m_CursorIndex < m_SelectIndex) m_CursorIndex = m_SelectIndex;
            }
            else
            {
                m_SelectIndex = m_CursorIndex = 0;
            }
        }

        public void SelectCurrentWord()
        {
            int p = m_CursorIndex;
            if (m_Text.Length == 0) return;
            if (p >= m_Text.Length) p = m_Text.Length - 1;
            int start = p, end = p;
            if (IsWord(m_Text[p]))
            {
                while (start > 0 && IsWord(m_Text[start - 1])) start--;
                while (end < m_Text.Length && IsWord(m_Text[end])) end++;
            }
            else
            {
                end = p + 1;
            }
            m_SelectIndex = start;
            m_CursorIndex = end;
        }

        public void SelectCurrentParagraph()
        {
            m_SelectIndex = LineStart(m_CursorIndex);
            m_CursorIndex = Math.Min(m_Text.Length, LineEnd(m_CursorIndex) + (LineEnd(m_CursorIndex) < m_Text.Length ? 1 : 0));
        }

        /// <summary>Scrolls a single-line field so the cursor stays inside it.</summary>
        public void UpdateScrollOffsetIfNeeded(Event evt)
        {
            if (evt.type != EventType.Repaint && evt.type != EventType.Layout) UpdateScrollOffset();
        }

        internal void UpdateScrollOffset()
        {
            if (style == null || style == GUIStyle.none) return;
            var shown = new GUIContent(isPasswordField ? new string('*', m_Text.Length) : m_Text);
            var inner = style.padding.Remove(m_Position);
            var unscrolled = new Rect(m_Position.x, m_Position.y, multiline ? m_Position.width : 100000f, m_Position.height);
            var cursor = style.GetCursorPixelPosition(unscrolled, shown, m_CursorIndex);
            graphicalCursorPos = cursor;
            graphicalSelectCursorPos = style.GetCursorPixelPosition(unscrolled, shown, m_SelectIndex);
            if (multiline)
            {
                scrollOffset.x = 0f;
                return;
            }
            float x = cursor.x - m_Position.x - style.padding.left;
            float view = inner.width;
            if (x - scrollOffset.x > view) scrollOffset.x = x - view;
            else if (x - scrollOffset.x < 0f) scrollOffset.x = x;
            var textWidth = style.CalcSize(shown).x - style.padding.horizontal;
            if (textWidth <= view) scrollOffset.x = 0f;
            scrollOffset.x = Math.Max(0f, scrollOffset.x);
            scrollOffset.y = 0f;
            revealCursor = false;
        }

        public void DrawCursor(string newText)
        {
            var saved = m_Text;
            m_Text = newText ?? "";
            ClampIndices();
            var content = new GUIContent(m_Text);
            style.DrawWithTextSelection(m_Position, content, controlID, m_CursorIndex, m_SelectIndex);
            m_Text = saved;
            ClampIndices();
        }

        /// <summary>Remembers the text for <see cref="Undo"/>.</summary>
        public void SaveBackup()
        {
            m_UndoText = m_Text;
            m_UndoCursor = m_CursorIndex;
            m_UndoSelect = m_SelectIndex;
        }

        public void Undo()
        {
            m_Text = m_UndoText ?? "";
            m_CursorIndex = m_UndoCursor;
            m_SelectIndex = m_UndoSelect;
            ClampIndices();
        }

        public bool Cut()
        {
            if (isPasswordField) return false;
            Copy();
            return DeleteSelection();
        }

        public void Copy()
        {
            if (m_SelectIndex == m_CursorIndex || isPasswordField) return;
            GUIUtility.systemCopyBuffer = SelectedText;
        }

        static string ReplaceNewlinesWithSpaces(string value) =>
            value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        public bool Paste()
        {
            var pasted = GUIUtility.systemCopyBuffer;
            if (pasted.Length == 0) return false;
            if (!multiline) pasted = ReplaceNewlinesWithSpaces(pasted);
            ReplaceSelection(pasted);
            return true;
        }
    }
}
