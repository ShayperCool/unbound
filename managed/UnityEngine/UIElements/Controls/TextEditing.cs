using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unbound.UIElements
{
    /// <summary>The cursor and selection of a selectable text element and the keyboard and pointer
    /// operations on them, as Unity's TextSelectingUtilities (standard text mode) performs them:
    /// indices count characters of the laid out text, the trailing zero-width space excluded.</summary>
    internal sealed class UaTextSelecting
    {
        enum CharacterType { LetterLike, Symbol, Symbol2, WhiteSpace, NewLine }

        enum Direction { Forward, Backward }

        enum Op
        {
            SelectLeft, SelectRight, SelectUp, SelectDown, SelectWordRight, SelectWordLeft, SelectToEndOfPreviousWord,
            SelectToStartOfNextWord, SelectTextStart, SelectTextEnd, ExpandSelectGraphicalLineStart, ExpandSelectGraphicalLineEnd,
            SelectParagraphForward, SelectParagraphBackward, SelectGraphicalLineStart, SelectGraphicalLineEnd, Copy, SelectAll, SelectNone,
        }

        readonly UaTextHandle m_Handle;
        int m_CursorIndex;
        int m_SelectIndex;
        bool m_RevealCursor;
        bool m_JustSelected;
        bool m_MouseDragSelectsWholeWords;
        int m_DblClickInitPosStart;
        int m_DblClickInitPosEnd;
        internal TextEditor.DblClickSnapping dblClickSnap = TextEditor.DblClickSnapping.WORDS;
        internal int iAltCursorPos = -1;
        internal bool hasHorizontalCursorPos;

        static Dictionary<Event, Op> s_KeyOps;

        internal Action OnCursorIndexChange;
        internal Action OnSelectIndexChange;
        internal Action OnRevealCursorChange;

        internal UaTextSelecting(UaTextHandle handle) { m_Handle = handle; }

        string text => m_Handle.layoutText;

        /// <summary>Characters a cursor can stand after: the laid out text without its trailing zero-width space.</summary>
        internal int characterCount
        {
            get
            {
                var t = text;
                int n = t.Length;
                return n > 0 && t[n - 1] == '​' ? n - 1 : n;
            }
        }

        char CharAt(int index) => text[index];

        internal bool hasSelection => cursorIndex != selectIndex;

        internal bool revealCursor
        {
            get => m_RevealCursor;
            set
            {
                if (m_RevealCursor == value) return;
                m_RevealCursor = value;
                OnRevealCursorChange?.Invoke();
            }
        }

        internal int cursorIndex
        {
            get => m_Handle.isPlaceholder ? 0 : Clamp(m_CursorIndex);
            set
            {
                if (m_CursorIndex == value) return;
                m_CursorIndex = value;
                OnCursorIndexChange?.Invoke();
            }
        }

        internal int cursorIndexNoValidation
        {
            get => m_CursorIndex;
            set
            {
                if (m_CursorIndex == value) return;
                m_CursorIndex = value;
                OnCursorIndexChange?.Invoke();
            }
        }

        internal int selectIndex
        {
            get => m_Handle.isPlaceholder ? 0 : Clamp(m_SelectIndex);
            set
            {
                if (m_SelectIndex == value) return;
                m_SelectIndex = value;
                OnSelectIndexChange?.Invoke();
            }
        }

        internal int selectIndexNoValidation
        {
            get => m_SelectIndex;
            set
            {
                if (m_SelectIndex == value) return;
                m_SelectIndex = value;
                OnSelectIndexChange?.Invoke();
            }
        }

        internal string selectedText
        {
            get
            {
                if (cursorIndex == selectIndex) return "";
                int start = Math.Min(cursorIndex, selectIndex), end = Math.Max(cursorIndex, selectIndex);
                return text.Substring(start, end - start);
            }
        }

        int Clamp(int index) => Mathf.Clamp(index, 0, characterCount);

        internal bool HandleKeyEvent(Event e)
        {
            InitKeyActions();
            var modifiers = e.modifiers;
            e.modifiers &= ~EventModifiers.CapsLock;
            bool found = s_KeyOps.TryGetValue(e, out var op);
            e.modifiers = modifiers;
            if (!found) return false;
            Perform(op);
            return true;
        }

        void Perform(Op op)
        {
            switch (op)
            {
                case Op.SelectLeft: SelectLeft(); break;
                case Op.SelectRight: SelectRight(); break;
                case Op.SelectUp: cursorIndex = m_Handle.LineUpCharacterPosition(cursorIndex); break;
                case Op.SelectDown: cursorIndex = m_Handle.LineDownCharacterPosition(cursorIndex); break;
                case Op.SelectWordRight: SelectWordRight(); break;
                case Op.SelectWordLeft: SelectWordLeft(); break;
                case Op.SelectToEndOfPreviousWord: ClearCursorPos(); cursorIndex = FindEndOfPreviousWord(cursorIndex); break;
                case Op.SelectToStartOfNextWord: ClearCursorPos(); cursorIndex = FindStartOfNextWord(cursorIndex); break;
                case Op.SelectTextStart: cursorIndex = 0; break;
                case Op.SelectTextEnd: cursorIndex = characterCount; break;
                case Op.ExpandSelectGraphicalLineStart: ExpandSelectGraphicalLineStart(); break;
                case Op.ExpandSelectGraphicalLineEnd: ExpandSelectGraphicalLineEnd(); break;
                case Op.SelectParagraphForward: SelectParagraphForward(); break;
                case Op.SelectParagraphBackward: SelectParagraphBackward(); break;
                case Op.SelectGraphicalLineStart: ClearCursorPos(); cursorIndex = GetGraphicalLineStart(cursorIndex); break;
                case Op.SelectGraphicalLineEnd: ClearCursorPos(); cursorIndex = GetGraphicalLineEnd(cursorIndex); break;
                case Op.Copy: Copy(); break;
                case Op.SelectAll: SelectAll(); break;
                case Op.SelectNone: SelectNone(); break;
            }
        }

        static void Map(string key, Op op) => s_KeyOps[Event.KeyboardEvent(key)] = op;

        static void InitKeyActions()
        {
            if (s_KeyOps != null) return;
            s_KeyOps = new Dictionary<Event, Op>();
            Map("#left", Op.SelectLeft);
            Map("#right", Op.SelectRight);
            Map("#up", Op.SelectUp);
            Map("#down", Op.SelectDown);
            if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX)
            {
                Map("#home", Op.SelectTextStart);
                Map("#end", Op.SelectTextEnd);
                Map("#^left", Op.ExpandSelectGraphicalLineStart);
                Map("#^right", Op.ExpandSelectGraphicalLineEnd);
                Map("#^up", Op.SelectParagraphBackward);
                Map("#^down", Op.SelectParagraphForward);
                Map("#&left", Op.SelectWordLeft);
                Map("#&right", Op.SelectWordRight);
                Map("#&up", Op.SelectParagraphBackward);
                Map("#&down", Op.SelectParagraphForward);
                Map("#%left", Op.ExpandSelectGraphicalLineStart);
                Map("#%right", Op.ExpandSelectGraphicalLineEnd);
                Map("#%up", Op.SelectTextStart);
                Map("#%down", Op.SelectTextEnd);
                Map("%a", Op.SelectAll);
                Map("%c", Op.Copy);
            }
            else
            {
                Map("#^left", Op.SelectToEndOfPreviousWord);
                Map("#^right", Op.SelectToStartOfNextWord);
                Map("#^up", Op.SelectParagraphBackward);
                Map("#^down", Op.SelectParagraphForward);
                Map("#home", Op.SelectGraphicalLineStart);
                Map("#end", Op.SelectGraphicalLineEnd);
                Map("^a", Op.SelectAll);
                Map("^c", Op.Copy);
                Map("^insert", Op.Copy);
            }
        }

        internal void ClearCursorPos()
        {
            hasHorizontalCursorPos = false;
            iAltCursorPos = -1;
        }

        internal void OnFocus(bool selectAll = true)
        {
            if (selectAll) SelectAll();
            revealCursor = true;
        }

        internal void SelectAll()
        {
            cursorIndex = 0;
            selectIndex = int.MaxValue;
            ClearCursorPos();
        }

        internal void SelectNone()
        {
            selectIndex = cursorIndex;
            ClearCursorPos();
        }

        void SelectLeft()
        {
            if (m_JustSelected && cursorIndex > selectIndex) (cursorIndex, selectIndex) = (selectIndex, cursorIndex);
            m_JustSelected = false;
            cursorIndex = PreviousCodePointIndex(cursorIndex);
        }

        void SelectRight()
        {
            if (m_JustSelected && cursorIndex < selectIndex) (cursorIndex, selectIndex) = (selectIndex, cursorIndex);
            m_JustSelected = false;
            cursorIndex = NextCodePointIndex(cursorIndex);
        }

        void SelectWordRight()
        {
            ClearCursorPos();
            int keep = selectIndex;
            if (cursorIndex < selectIndex)
            {
                selectIndex = cursorIndex;
                MoveWordRight();
                selectIndex = keep;
                cursorIndex = cursorIndex < selectIndex ? cursorIndex : selectIndex;
            }
            else
            {
                selectIndex = cursorIndex;
                MoveWordRight();
                selectIndex = keep;
            }
        }

        void SelectWordLeft()
        {
            ClearCursorPos();
            int keep = selectIndex;
            if (cursorIndex > selectIndex)
            {
                selectIndex = cursorIndex;
                MoveWordLeft();
                selectIndex = keep;
                cursorIndex = cursorIndex > selectIndex ? cursorIndex : selectIndex;
            }
            else
            {
                selectIndex = cursorIndex;
                MoveWordLeft();
                selectIndex = keep;
            }
        }

        void SelectParagraphForward()
        {
            ClearCursorPos();
            bool wasBefore = cursorIndex < selectIndex;
            if (cursorIndex < characterCount)
            {
                cursorIndex = IndexOfEndOfLine(cursorIndex + 1);
                if (wasBefore && cursorIndex > selectIndex) cursorIndex = selectIndex;
            }
        }

        void SelectParagraphBackward()
        {
            ClearCursorPos();
            bool wasAfter = cursorIndex > selectIndex;
            if (cursorIndex > 1)
            {
                cursorIndex = LastIndexOf('\n', cursorIndex - 2) + 1;
                if (wasAfter && cursorIndex < selectIndex) cursorIndex = selectIndex;
            }
            else
            {
                selectIndex = cursorIndex = 0;
            }
        }

        internal void SelectCurrentWord()
        {
            int p = cursorIndex;
            if (cursorIndex < selectIndex)
            {
                cursorIndex = FindEndOfClassification(p, Direction.Backward);
                selectIndex = FindEndOfClassification(p, Direction.Forward);
            }
            else
            {
                cursorIndex = FindEndOfClassification(p, Direction.Forward);
                selectIndex = FindEndOfClassification(p, Direction.Backward);
            }
            ClearCursorPos();
            m_JustSelected = true;
        }

        internal void SelectCurrentParagraph()
        {
            ClearCursorPos();
            if (cursorIndex < characterCount) cursorIndex = IndexOfEndOfLine(cursorIndex);
            if (selectIndex != 0) selectIndex = LastIndexOf('\n', selectIndex - 1) + 1;
        }

        internal void MoveRight()
        {
            ClearCursorPos();
            if (selectIndex == cursorIndex)
            {
                cursorIndex = NextCodePointIndex(cursorIndex);
                selectIndex = cursorIndex;
            }
            else if (selectIndex > cursorIndex) cursorIndex = selectIndex;
            else selectIndex = cursorIndex;
        }

        internal void MoveLeft()
        {
            if (selectIndex == cursorIndex)
            {
                cursorIndex = PreviousCodePointIndex(cursorIndex);
                selectIndex = cursorIndex;
            }
            else if (selectIndex > cursorIndex) selectIndex = cursorIndex;
            else cursorIndex = selectIndex;
            ClearCursorPos();
        }

        internal void MoveUp()
        {
            if (selectIndex < cursorIndex) selectIndex = cursorIndex;
            else cursorIndex = selectIndex;
            cursorIndex = selectIndex = m_Handle.LineUpCharacterPosition(cursorIndex);
            if (cursorIndex <= 0) ClearCursorPos();
        }

        internal void MoveDown()
        {
            if (selectIndex > cursorIndex) selectIndex = cursorIndex;
            else cursorIndex = selectIndex;
            cursorIndex = selectIndex = m_Handle.LineDownCharacterPosition(cursorIndex);
            if (cursorIndex == characterCount) ClearCursorPos();
        }

        internal void MoveLineStart()
        {
            int p = selectIndex < cursorIndex ? selectIndex : cursorIndex;
            while (p-- != 0)
            {
                if (CharAt(p) == '\n')
                {
                    cursorIndex = selectIndex = p + 1;
                    return;
                }
            }
            cursorIndex = selectIndex = 0;
        }

        internal void MoveLineEnd()
        {
            int p = selectIndex > cursorIndex ? selectIndex : cursorIndex;
            int count = characterCount;
            for (; p < count; p++)
            {
                if (CharAt(p) == '\n')
                {
                    cursorIndex = selectIndex = p;
                    return;
                }
            }
            cursorIndex = selectIndex = count;
        }

        internal void MoveGraphicalLineStart() => cursorIndex = selectIndex = GetGraphicalLineStart(cursorIndex < selectIndex ? cursorIndex : selectIndex);

        internal void MoveGraphicalLineEnd() => cursorIndex = selectIndex = GetGraphicalLineEnd(cursorIndex > selectIndex ? cursorIndex : selectIndex);

        internal void MoveTextStart() => cursorIndex = selectIndex = 0;

        internal void MoveTextEnd() => cursorIndex = selectIndex = characterCount;

        internal void MoveParagraphForward()
        {
            cursorIndex = cursorIndex > selectIndex ? cursorIndex : selectIndex;
            if (cursorIndex < characterCount) cursorIndex = selectIndex = IndexOfEndOfLine(cursorIndex + 1);
        }

        internal void MoveParagraphBackward()
        {
            cursorIndex = cursorIndex < selectIndex ? cursorIndex : selectIndex;
            if (cursorIndex > 1) cursorIndex = selectIndex = LastIndexOf('\n', cursorIndex - 2) + 1;
            else cursorIndex = selectIndex = 0;
        }

        internal void MoveWordRight()
        {
            cursorIndex = cursorIndex > selectIndex ? cursorIndex : selectIndex;
            cursorIndex = selectIndex = FindNextSeperator(cursorIndex);
            ClearCursorPos();
        }

        internal void MoveToStartOfNextWord()
        {
            ClearCursorPos();
            if (cursorIndex != selectIndex)
            {
                MoveRight();
                return;
            }
            cursorIndex = selectIndex = FindStartOfNextWord(cursorIndex);
        }

        internal void MoveToEndOfPreviousWord()
        {
            ClearCursorPos();
            if (cursorIndex != selectIndex)
            {
                MoveLeft();
                return;
            }
            cursorIndex = selectIndex = FindEndOfPreviousWord(cursorIndex);
        }

        internal void MoveWordLeft()
        {
            cursorIndex = cursorIndex < selectIndex ? cursorIndex : selectIndex;
            cursorIndex = FindPrevSeperator(cursorIndex);
            selectIndex = cursorIndex;
        }

        internal void MouseDragSelectsWholeWords(bool on)
        {
            m_MouseDragSelectsWholeWords = on;
            m_DblClickInitPosStart = Math.Min(cursorIndex, selectIndex);
            m_DblClickInitPosEnd = Math.Max(cursorIndex, selectIndex);
        }

        void ExpandSelectGraphicalLineStart()
        {
            ClearCursorPos();
            if (cursorIndex < selectIndex)
            {
                cursorIndex = GetGraphicalLineStart(cursorIndex);
                return;
            }
            int keep = cursorIndex;
            cursorIndex = GetGraphicalLineStart(selectIndex);
            selectIndex = keep;
        }

        void ExpandSelectGraphicalLineEnd()
        {
            ClearCursorPos();
            if (cursorIndex > selectIndex)
            {
                cursorIndex = GetGraphicalLineEnd(cursorIndex);
                return;
            }
            int keep = cursorIndex;
            cursorIndex = GetGraphicalLineEnd(selectIndex);
            selectIndex = keep;
        }

        internal void DblClickSnap(TextEditor.DblClickSnapping snapping) => dblClickSnap = snapping;

        internal void MoveCursorToPosition_Internal(Vector2 cursorPosition, bool shift)
        {
            selectIndex = m_Handle.GetCursorIndexFromPosition(cursorPosition);
            if (!shift) cursorIndex = selectIndex;
        }

        internal void SelectToPosition(Vector2 cursorPosition)
        {
            if (characterCount == 0) return;
            int index = m_Handle.GetCursorIndexFromPosition(cursorPosition);
            if (!m_MouseDragSelectsWholeWords)
            {
                cursorIndex = index;
                return;
            }
            if (dblClickSnap == TextEditor.DblClickSnapping.WORDS)
            {
                if (index <= m_DblClickInitPosStart)
                {
                    cursorIndex = FindEndOfClassification(index, Direction.Backward);
                    selectIndex = FindEndOfClassification(m_DblClickInitPosEnd - 1, Direction.Forward);
                }
                else if (index >= m_DblClickInitPosEnd)
                {
                    cursorIndex = FindEndOfClassification(index - 1, Direction.Forward);
                    selectIndex = FindEndOfClassification(m_DblClickInitPosStart + 1, Direction.Backward);
                }
                else
                {
                    cursorIndex = m_DblClickInitPosStart;
                    selectIndex = m_DblClickInitPosEnd;
                }
            }
            else if (index <= m_DblClickInitPosStart)
            {
                cursorIndex = index > 0 ? LastIndexOf('\n', Mathf.Max(0, index - 1)) + 1 : 0;
                selectIndex = LastIndexOf('\n', Mathf.Min(characterCount - 1, m_DblClickInitPosEnd + 1));
            }
            else if (index >= m_DblClickInitPosEnd)
            {
                cursorIndex = index < characterCount ? IndexOfEndOfLine(index) : characterCount;
                selectIndex = LastIndexOf('\n', Mathf.Max(0, m_DblClickInitPosEnd - 2)) + 1;
            }
            else
            {
                cursorIndex = m_DblClickInitPosStart;
                selectIndex = m_DblClickInitPosEnd;
            }
        }

        int FindNextSeperator(int p)
        {
            int count = characterCount;
            while (p < count && ClassifyChar(p) != CharacterType.LetterLike) p = NextCodePointIndex(p);
            while (p < count && ClassifyChar(p) == CharacterType.LetterLike) p = NextCodePointIndex(p);
            return p;
        }

        int FindPrevSeperator(int p)
        {
            p = PreviousCodePointIndex(p);
            while (p > 0 && ClassifyChar(p) != CharacterType.LetterLike) p = PreviousCodePointIndex(p);
            if (p == 0) return 0;
            while (p > 0 && ClassifyChar(p) == CharacterType.LetterLike) p = PreviousCodePointIndex(p);
            return ClassifyChar(p) == CharacterType.LetterLike ? p : NextCodePointIndex(p);
        }

        internal int FindStartOfNextWord(int p)
        {
            int count = characterCount;
            if (p == count) return p;
            var type = ClassifyChar(p);
            if (type != CharacterType.WhiteSpace)
            {
                p = NextCodePointIndex(p);
                while (p < count && ClassifyChar(p) == type) p = NextCodePointIndex(p);
            }
            else if (CharAt(p) == '\t' || CharAt(p) == '\n')
            {
                return NextCodePointIndex(p);
            }
            if (p == count) return p;
            if (CharAt(p) == ' ')
            {
                while (p < count && ClassifyChar(p) == CharacterType.WhiteSpace) p = NextCodePointIndex(p);
            }
            return p;
        }

        internal int FindEndOfPreviousWord(int p)
        {
            if (p == 0) return p;
            p = PreviousCodePointIndex(p);
            while (p > 0 && CharAt(p) == ' ') p = PreviousCodePointIndex(p);
            var type = ClassifyChar(p);
            if (type != CharacterType.WhiteSpace)
            {
                while (p > 0 && ClassifyChar(PreviousCodePointIndex(p)) == type) p = PreviousCodePointIndex(p);
            }
            return p;
        }

        int FindEndOfClassification(int p, Direction dir)
        {
            int count = characterCount;
            if (count == 0) return 0;
            if (p >= count) p = count - 1;
            var type = ClassifyChar(p);
            if (type == CharacterType.NewLine) return p;
            do
            {
                if (dir == Direction.Backward)
                {
                    p = PreviousCodePointIndex(p);
                    if (p == 0) return ClassifyChar(0) != type ? NextCodePointIndex(0) : 0;
                }
                else
                {
                    p = NextCodePointIndex(p);
                    if (p >= count) return count;
                }
            }
            while (ClassifyChar(p) == type);
            return dir == Direction.Forward ? p : NextCodePointIndex(p);
        }

        int IndexOfEndOfLine(int start)
        {
            int i = start < text.Length ? text.IndexOf('\n', start) : -1;
            return i != -1 ? i : characterCount;
        }

        int LastIndexOf(char c, int start)
        {
            if (start < 0 || text.Length == 0) return -1;
            return text.LastIndexOf(c, Math.Min(start, text.Length - 1));
        }

        internal int PreviousCodePointIndex(int index) => index > 0 ? index - 1 : index;

        internal int NextCodePointIndex(int index) => index < characterCount ? index + 1 : index;

        int GetGraphicalLineStart(int p)
        {
            var pos = m_Handle.GetCursorPositionFromStringIndexUsingLineHeight(p);
            pos.y -= 1f / GUIUtility.pixelsPerPoint;
            pos.x = 0f;
            return m_Handle.GetCursorIndexFromPosition(pos);
        }

        int GetGraphicalLineEnd(int p)
        {
            var pos = m_Handle.GetCursorPositionFromStringIndexUsingLineHeight(p);
            pos.y -= 1f / GUIUtility.pixelsPerPoint;
            pos.x += 5000f;
            return m_Handle.GetCursorIndexFromPosition(pos);
        }

        internal void Copy()
        {
            if (selectIndex != cursorIndex) GUIUtility.systemCopyBuffer = selectedText;
        }

        CharacterType ClassifyChar(int index)
        {
            char c = CharAt(index);
            if (c == '\n') return CharacterType.NewLine;
            if (char.IsWhiteSpace(c)) return CharacterType.WhiteSpace;
            if (char.IsLetterOrDigit(c) || c == '\'') return CharacterType.LetterLike;
            return CharacterType.Symbol;
        }
    }

    /// <summary>Edits of a text field's text at its cursor (Unity's TextEditingUtilities, standard
    /// text mode, without IME composition).</summary>
    internal sealed class UaTextEditing
    {
        enum Op { MoveLeft, MoveRight, MoveUp, MoveDown, MoveLineStart, MoveLineEnd, MoveWordRight, MoveWordLeft, MoveToStartOfNextWord, MoveToEndOfPreviousWord, MoveTextStart, MoveTextEnd, MoveParagraphForward, MoveParagraphBackward, MoveGraphicalLineStart, MoveGraphicalLineEnd, Delete, Backspace, Cut, Paste, DeleteWordBack, DeleteLineBack, DeleteWordForward }

        readonly UaTextSelecting m_Selecting;
        string m_Text;
        static Dictionary<Event, Op> s_KeyOps;

        internal bool multiline;
        internal Action OnTextChanged;

        internal UaTextEditing(UaTextSelecting selecting, string text)
        {
            m_Selecting = selecting;
            m_Text = text;
        }

        internal string text
        {
            get => m_Text;
            set
            {
                if (value == m_Text) return;
                m_Text = value ?? string.Empty;
                OnTextChanged?.Invoke();
            }
        }

        bool hasSelection => m_Selecting.hasSelection;

        int cursorIndex
        {
            get => m_Selecting.cursorIndex;
            set => m_Selecting.cursorIndex = value;
        }

        int selectIndex
        {
            get => m_Selecting.selectIndex;
            set => m_Selecting.selectIndex = value;
        }

        internal bool HandleKeyEvent(Event e)
        {
            InitKeyActions();
            var modifiers = e.modifiers;
            e.modifiers &= ~EventModifiers.CapsLock;
            bool found = s_KeyOps.TryGetValue(e, out var op);
            e.modifiers = modifiers;
            if (!found) return false;
            Perform(op);
            return true;
        }

        void Perform(Op op)
        {
            m_Selecting.revealCursor = true;
            switch (op)
            {
                case Op.MoveLeft: m_Selecting.MoveLeft(); break;
                case Op.MoveRight: m_Selecting.MoveRight(); break;
                case Op.MoveUp: m_Selecting.MoveUp(); break;
                case Op.MoveDown: m_Selecting.MoveDown(); break;
                case Op.MoveLineStart: m_Selecting.MoveLineStart(); break;
                case Op.MoveLineEnd: m_Selecting.MoveLineEnd(); break;
                case Op.MoveWordRight: m_Selecting.MoveWordRight(); break;
                case Op.MoveToStartOfNextWord: m_Selecting.MoveToStartOfNextWord(); break;
                case Op.MoveToEndOfPreviousWord: m_Selecting.MoveToEndOfPreviousWord(); break;
                case Op.MoveWordLeft: m_Selecting.MoveWordLeft(); break;
                case Op.MoveTextStart: m_Selecting.MoveTextStart(); break;
                case Op.MoveTextEnd: m_Selecting.MoveTextEnd(); break;
                case Op.MoveParagraphForward: m_Selecting.MoveParagraphForward(); break;
                case Op.MoveParagraphBackward: m_Selecting.MoveParagraphBackward(); break;
                case Op.MoveGraphicalLineStart: m_Selecting.MoveGraphicalLineStart(); break;
                case Op.MoveGraphicalLineEnd: m_Selecting.MoveGraphicalLineEnd(); break;
                case Op.Delete: Delete(); break;
                case Op.Backspace: Backspace(); break;
                case Op.Cut: Cut(); break;
                case Op.Paste: Paste(); break;
                case Op.DeleteWordBack: DeleteWordBack(); break;
                case Op.DeleteLineBack: DeleteLineBack(); break;
                case Op.DeleteWordForward: DeleteWordForward(); break;
            }
        }

        static void Map(string key, Op op) => s_KeyOps[Event.KeyboardEvent(key)] = op;

        static void InitKeyActions()
        {
            if (s_KeyOps != null) return;
            s_KeyOps = new Dictionary<Event, Op>();
            Map("left", Op.MoveLeft);
            Map("right", Op.MoveRight);
            Map("up", Op.MoveUp);
            Map("down", Op.MoveDown);
            Map("delete", Op.Delete);
            Map("backspace", Op.Backspace);
            Map("#backspace", Op.Backspace);
            if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX)
            {
                Map("^left", Op.MoveGraphicalLineStart);
                Map("^right", Op.MoveGraphicalLineEnd);
                Map("&left", Op.MoveWordLeft);
                Map("&right", Op.MoveWordRight);
                Map("&up", Op.MoveParagraphBackward);
                Map("&down", Op.MoveParagraphForward);
                Map("%left", Op.MoveGraphicalLineStart);
                Map("%right", Op.MoveGraphicalLineEnd);
                Map("%up", Op.MoveTextStart);
                Map("%down", Op.MoveTextEnd);
                Map("%x", Op.Cut);
                Map("%v", Op.Paste);
                Map("^d", Op.Delete);
                Map("^h", Op.Backspace);
                Map("^b", Op.MoveLeft);
                Map("^f", Op.MoveRight);
                Map("^a", Op.MoveLineStart);
                Map("^e", Op.MoveLineEnd);
                Map("&delete", Op.DeleteWordForward);
                Map("&backspace", Op.DeleteWordBack);
                Map("%backspace", Op.DeleteLineBack);
            }
            else
            {
                Map("home", Op.MoveGraphicalLineStart);
                Map("end", Op.MoveGraphicalLineEnd);
                Map("%left", Op.MoveWordLeft);
                Map("%right", Op.MoveWordRight);
                Map("%up", Op.MoveParagraphBackward);
                Map("%down", Op.MoveParagraphForward);
                Map("^left", Op.MoveToEndOfPreviousWord);
                Map("^right", Op.MoveToStartOfNextWord);
                Map("^up", Op.MoveParagraphBackward);
                Map("^down", Op.MoveParagraphForward);
                Map("^delete", Op.DeleteWordForward);
                Map("^backspace", Op.DeleteWordBack);
                Map("%backspace", Op.DeleteLineBack);
                Map("^x", Op.Cut);
                Map("^v", Op.Paste);
                Map("#delete", Op.Cut);
                Map("#insert", Op.Paste);
            }
        }

        internal bool DeleteLineBack()
        {
            if (hasSelection)
            {
                DeleteSelection();
                return true;
            }
            int lineStart = cursorIndex;
            while (lineStart > 0 && m_Text[lineStart - 1] != '\n') lineStart--;
            if (lineStart == cursorIndex) return false;
            text = m_Text.Remove(lineStart, cursorIndex - lineStart);
            cursorIndex = selectIndex = lineStart;
            return true;
        }

        internal bool DeleteWordBack()
        {
            if (hasSelection)
            {
                DeleteSelection();
                return true;
            }
            int end = m_Selecting.FindEndOfPreviousWord(cursorIndex);
            if (cursorIndex == end) return false;
            text = m_Text.Remove(end, cursorIndex - end);
            cursorIndex = selectIndex = end;
            return true;
        }

        internal bool DeleteWordForward()
        {
            if (hasSelection)
            {
                DeleteSelection();
                return true;
            }
            int next = m_Selecting.FindStartOfNextWord(cursorIndex);
            if (cursorIndex >= m_Text.Length) return false;
            text = m_Text.Remove(cursorIndex, next - cursorIndex);
            return true;
        }

        internal bool Delete()
        {
            if (hasSelection)
            {
                DeleteSelection();
                return true;
            }
            if (cursorIndex >= m_Text.Length) return false;
            text = m_Text.Remove(cursorIndex, 1);
            return true;
        }

        internal bool Backspace()
        {
            if (hasSelection)
            {
                DeleteSelection();
                return true;
            }
            if (cursorIndex <= 0) return false;
            int previous = m_Selecting.PreviousCodePointIndex(cursorIndex);
            text = m_Text.Remove(cursorIndex - 1, 1);
            cursorIndex = previous;
            selectIndex = previous;
            m_Selecting.ClearCursorPos();
            return true;
        }

        internal bool DeleteSelection()
        {
            if (cursorIndex == selectIndex) return false;
            if (cursorIndex < selectIndex)
            {
                text = m_Text.Substring(0, cursorIndex) + m_Text.Substring(selectIndex, m_Text.Length - selectIndex);
                selectIndex = cursorIndex;
            }
            else
            {
                text = m_Text.Substring(0, selectIndex) + m_Text.Substring(cursorIndex, m_Text.Length - cursorIndex);
                cursorIndex = selectIndex;
            }
            m_Selecting.ClearCursorPos();
            return true;
        }

        internal void ReplaceSelection(string replace)
        {
            DeleteSelection();
            text = m_Text.Insert(Math.Min(cursorIndex, m_Text.Length), replace);
            m_Selecting.selectIndexNoValidation = m_Selecting.cursorIndexNoValidation += new System.Globalization.StringInfo(replace).LengthInTextElements;
            m_Selecting.ClearCursorPos();
        }

        char m_HighSurrogate;

        internal bool Insert(char c)
        {
            if (char.IsHighSurrogate(c))
            {
                m_HighSurrogate = c;
                return false;
            }
            if (char.IsLowSurrogate(c))
            {
                ReplaceSelection(new string(new[] { m_HighSurrogate, c }));
                return true;
            }
            ReplaceSelection(c.ToString());
            return true;
        }

        internal bool CanPaste() => GUIUtility.systemCopyBuffer.Length != 0;

        internal bool Cut()
        {
            m_Selecting.Copy();
            return DeleteSelection();
        }

        internal bool Paste()
        {
            var pasted = GUIUtility.systemCopyBuffer;
            if (pasted == "") return false;
            if (!multiline) pasted = pasted.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            ReplaceSelection(pasted);
            return true;
        }

        internal void OnBlur()
        {
            m_Selecting.revealCursor = false;
            m_Selecting.SelectNone();
        }
    }
}
