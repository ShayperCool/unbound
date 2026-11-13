using System;
using System.Reflection;
using UnityEngine;

public static class DeclarationTextEditorControls
{
    static void Legacy(TextEditor editor, string name, bool value) => typeof(TextEditor).GetField(name).SetValue(editor, value);
    static bool Legacy(TextEditor editor, string name) => (bool)typeof(TextEditor).GetField(name).GetValue(editor);
    public static void Record(Action<string, object> row)
    {
        foreach (var pair in new[] {
            new[] { "multiline", "isMultiline" },
            new[] { "hasHorizontalCursorPos", "hasHorizontalCursor" },
            new[] { "revealCursor", "showCursor" } })
        {
            var editor = new TextEditor();
            var property = typeof(TextEditor).GetProperty(pair[1]);
            property.SetValue(editor, false, null);
            Legacy(editor, pair[0], true);
            row("textEditor.state." + pair[0] + ".legacy-true.current", property.GetValue(editor, null));
            row("textEditor.state." + pair[0] + ".legacy-true.legacy", Legacy(editor, pair[0]));
            property.SetValue(editor, true, null);
            Legacy(editor, pair[0], false);
            row("textEditor.state." + pair[0] + ".legacy-false.current", property.GetValue(editor, null));
            row("textEditor.state." + pair[0] + ".legacy-false.legacy", Legacy(editor, pair[0]));
        }
        var moved = new TextEditor { text = "abcd", showCursor = false };
        Legacy(moved, "revealCursor", false);
        moved.cursorIndex = 2;
        row("textEditor.cursor.show", moved.showCursor);
        row("textEditor.cursor.legacy", Legacy(moved, "revealCursor"));
        foreach (bool multiline in new[] { false, true })
        {
            var editor = new TextEditor { text = "abcd", isMultiline = multiline, cursorIndex = 1, selectIndex = 2, showCursor = false };
            Legacy(editor, "multiline", !multiline);
            Legacy(editor, "revealCursor", false);
            editor.OnFocus();
            string key = "textEditor.focus." + multiline + ".";
            row(key + "cursor", editor.cursorIndex);
            row(key + "select", editor.selectIndex);
            row(key + "show", editor.showCursor);
            row(key + "legacy", Legacy(editor, "revealCursor"));
            editor.OnLostFocus();
            row(key + "lost-show", editor.showCursor);
            row(key + "lost-legacy", Legacy(editor, "revealCursor"));
        }
    }
}
