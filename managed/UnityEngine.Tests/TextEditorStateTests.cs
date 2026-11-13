using System.Reflection;
using Xunit;

namespace UnityEngine.Tests;

public class TextEditorStateTests
{
    static FieldInfo Legacy(string name) => typeof(TextEditor).GetField(name);

    [Theory]
    [InlineData("multiline", "isMultiline")]
    [InlineData("hasHorizontalCursorPos", "hasHorizontalCursor")]
    [InlineData("revealCursor", "showCursor")]
    public void LegacyWritesDoNotChangeCurrentState(string field, string property)
    {
        string saved = ProjectInfo.UnityVersion;
        try
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var editor = new TextEditor();
            var current = typeof(TextEditor).GetProperty(property);
            current.SetValue(editor, false);
            Legacy(field).SetValue(editor, true);
            Assert.False((bool)current.GetValue(editor));
            current.SetValue(editor, true);
            Legacy(field).SetValue(editor, false);
            Assert.True((bool)current.GetValue(editor));
        }
        finally { ProjectInfo.UnityVersion = saved; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusSelectsAllAndShowsTheCursorInBothModes(bool multiline)
    {
        string saved = ProjectInfo.UnityVersion;
        try
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var editor = new TextEditor { text = "abcd", isMultiline = multiline, cursorIndex = 1, selectIndex = 2 };
            Legacy("multiline").SetValue(editor, !multiline);
            editor.OnFocus();
            Assert.Equal(0, editor.cursorIndex);
            Assert.Equal(4, editor.selectIndex);
            Assert.True(editor.showCursor);
            Assert.False((bool)Legacy("revealCursor").GetValue(editor));
            editor.OnLostFocus();
            Assert.True(editor.showCursor);
        }
        finally { ProjectInfo.UnityVersion = saved; }
    }

    [Fact]
    public void OtherReleasesRetainTheirPreviousFieldLinkage()
    {
        string saved = ProjectInfo.UnityVersion;
        try
        {
            ProjectInfo.UnityVersion = "2022.3.62f1";
            var editor = new TextEditor { text = "abcd" };
            Legacy("multiline").SetValue(editor, true);
            Assert.True(editor.isMultiline);
            editor.hasHorizontalCursor = true;
            Assert.True((bool)Legacy("hasHorizontalCursorPos").GetValue(editor));
            editor.cursorIndex = 2;
            Assert.True((bool)Legacy("revealCursor").GetValue(editor));
            editor.OnFocus();
            Assert.Equal(0, editor.cursorIndex);
            Assert.Equal(0, editor.selectIndex);
        }
        finally { ProjectInfo.UnityVersion = saved; }
    }
}
