using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Command and IME events sent to text fields in a runtime panel: which ValidateCommand and
// ExecuteCommand names a focused field takes (stops) and what Copy, Cut, Paste, Delete and
// SelectAll do to its text, selection and the clipboard; unfocused, read-only, disabled and
// multiline fields; events sent to the panel without a target; pooled event reuse; the IME mode a
// focused field sets and the internal IMEEvent's pooling and propagation.
public class TextCommandProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("TextCommandProbe").AddComponent<TextCommandProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    // Counts the events that pass the container of the fields on the way down and up.
    class Recorder : VisualElement
    {
        public int trickled, bubbled;
        static bool Counted(EventBase evt) => evt is ICommandEvent || evt.GetType().Name == "IMEEvent";
        protected override void HandleEventTrickleDown(EventBase evt) { if (Counted(evt)) trickled++; }
        protected override void HandleEventBubbleUp(EventBase evt) { if (Counted(evt)) bubbled++; }
    }

    int reached;
    int changes;
    string lastChange = "";
    Recorder recorder;

    IEnumerator Start()
    {
        string path = Environment.GetEnvironmentVariable("TEXT_COMMAND_OUT"); if (string.IsNullOrEmpty(path)) yield break;
        output = new StreamWriter(path);
        // The steps below copy to the system clipboard, which other programs on the same desktop
        // (and probes that read it) share; its text is put back when the probe ends.
        string clipboardBefore = GUIUtility.systemCopyBuffer;
        bool failed = false;
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        if (Screen.dpi > 0) settings.scale = 96f / Screen.dpi;
        var document = new GameObject("Document").AddComponent<UIDocument>();
        document.panelSettings = settings;
        var root = document.rootVisualElement;
        // No theme: the text needs a font to be laid out (selection indices are clamped to it).
        root.style.unityFontDefinition = FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        root.style.fontSize = 14;
        // Counts the command events that bubble past the fields.
        root.RegisterCallback<ValidateCommandEvent>(_ => reached++);
        root.RegisterCallback<ExecuteCommandEvent>(_ => reached++);
        recorder = new Recorder();
        root.Add(recorder);
        var field = new TextField("Text") { value = "hello world" };
        var other = new TextField("Other") { value = "other" };
        var locked = new TextField("Locked") { value = "locked text", isReadOnly = true };
        var multi = new TextField("Multi") { value = "ab", multiline = true };
        recorder.Add(field);
        recorder.Add(other);
        recorder.Add(locked);
        recorder.Add(multi);
        foreach (var f in new[] { field, locked, multi })
            f.RegisterValueChangedCallback(e => { changes++; lastChange = e.previousValue + ">" + e.newValue; });
        for (int i = 0; i < 3; i++) yield return null;
        var steps = Record(field, other, locked, multi);
        while (true)
        {
            bool more;
            try { more = steps.MoveNext(); }
            catch (Exception ex) { Debug.LogException(ex); failed = true; break; }
            if (!more) break;
            yield return steps.Current;
        }
        if (!failed) Put("complete", 1);
        GUIUtility.systemCopyBuffer = clipboardBefore;
        output.Dispose();
        EditorApplication.Exit(failed ? 1 : 0);
    }

    static TextElement Input(TextField field) => (field.Q(className: TextField.inputUssClassName) ?? field).Q<TextElement>();

    void Reset()
    {
        reached = 0;
        changes = 0;
        lastChange = "";
        recorder.trickled = 0;
        recorder.bubbled = 0;
    }

    string Counts() => "reached " + reached + " trickled " + recorder.trickled + " bubbled " + recorder.bubbled;

    string State(TextField field) => " text " + field.text + " value " + field.value + " cursor " + field.cursorIndex + " select " + field.selectIndex
        + " clipboard " + GUIUtility.systemCopyBuffer + " changes " + changes + " " + lastChange;

    void Send(EventBase e, VisualElement target)
    {
        if (target == null) recorder.panel.visualTree.SendEvent(e);
        else { e.target = target; target.SendEvent(e); }
    }

    void Validate(string key, TextField field, string command)
    {
        Reset();
        using (var e = ValidateCommandEvent.GetPooled(command)) Send(e, Input(field));
        Put(key, Counts());
    }

    void Execute(string key, TextField field, string command, bool toPanel = false)
    {
        Reset();
        using (var e = ExecuteCommandEvent.GetPooled(command)) Send(e, toPanel ? null : Input(field));
        Put(key, Counts() + State(field));
    }

    // Selection changes settle over frames; the text must be laid out before indices stick
    // (they are clamped to it), so the range is applied again until it holds.
    IEnumerator Select(TextField field, int cursor, int select)
    {
        for (int i = 0; i < 60; i++)
        {
            field.SelectRange(cursor, select);
            yield return null;
            yield return null;
            if (field.cursorIndex == cursor && field.selectIndex == select) yield break;
        }
    }

    static IEnumerator Settle() { yield return null; yield return null; }

    static string Focused(VisualElement element)
    {
        var focused = element.focusController?.focusedElement as VisualElement;
        return focused == null ? "<none>" : focused.GetType().Name + (focused.parent != null ? " in " + focused.parent.GetType().Name : "");
    }

    IEnumerator Record(TextField field, TextField other, TextField locked, TextField multi)
    {
        GUIUtility.systemCopyBuffer = "CLIP";
        Put("ime.beforeFocus", UnityEngine.Input.imeCompositionMode);
        // Not focused: nothing is taken.
        Validate("unfocused.validate.selectAll", field, "SelectAll");
        Execute("unfocused.execute.selectAll", field, "SelectAll");
        Execute("unfocused.execute.paste", field, "Paste");

        field.Focus();
        yield return Settle();
        yield return Select(field, 0, 0);
        Put("focused", Focused(field));
        Put("ime.focused", UnityEngine.Input.imeCompositionMode);
        Put("selection.afterFocus", field.cursorIndex + " " + field.selectIndex);
        // Which commands a focused field validates.
        foreach (var command in new[] { "Copy", "Cut", "Paste", "Delete", "SelectAll", "UndoRedoPerformed", "OnLostFocus", "Duplicate", "Find" })
            Validate("validate.noSelection." + command, field, command);
        yield return Select(field, 0, 5);
        Put("selection.range", field.cursorIndex + " " + field.selectIndex);
        foreach (var command in new[] { "Copy", "Cut" })
            Validate("validate.selection." + command, field, command);
        GUIUtility.systemCopyBuffer = "";
        Validate("validate.emptyClipboard.Paste", field, "Paste");
        GUIUtility.systemCopyBuffer = "CLIP";

        // What each command does.
        yield return Select(field, 0, 5);
        Execute("execute.copy", field, "Copy");
        yield return Select(field, 6, 11);
        Execute("execute.cut", field, "Cut");
        Execute("execute.paste", field, "Paste");
        Execute("execute.selectAll", field, "SelectAll");
        GUIUtility.systemCopyBuffer = "line1\nline2";
        yield return Select(field, 0, 0);
        Execute("execute.paste.newline", field, "Paste");
        yield return Select(field, 0, 5);
        Execute("execute.delete", field, "Delete");
        // Without a selection Delete removes the character after the cursor; Cut changes nothing.
        yield return Select(field, 1, 1);
        Execute("execute.delete.noSelection", field, "Delete");
        Execute("execute.cut.noSelection", field, "Cut");
        Execute("execute.onLostFocus", field, "OnLostFocus");
        Execute("execute.duplicate", field, "Duplicate");
        Execute("execute.copy.noSelection", field, "Copy");
        GUIUtility.systemCopyBuffer = "";
        Execute("execute.paste.emptyClipboard", field, "Paste");
        GUIUtility.systemCopyBuffer = "CLIP";

        // Sent to the panel without a target: dispatched to the focused element.
        yield return Select(field, 0, 0);
        Execute("panel.execute.selectAll", field, "SelectAll", toPanel: true);
        yield return Select(field, 2, 4);
        Execute("panel.execute.cut", field, "Cut", toPanel: true);

        // Pooled events are reset when reused; a command read from an IMGUI event.
        var first = ValidateCommandEvent.GetPooled("SelectAll");
        Send(first, Input(field));
        Put("reuse.firstStopped", first.isPropagationStopped);
        first.Dispose();
        var second = ValidateCommandEvent.GetPooled();
        Put("reuse.same", ReferenceEquals(first, second));
        Put("reuse.state", (second.commandName ?? "<null>") + " " + second.isPropagationStopped + " " + (second.target == null));
        second.Dispose();
        Reset();
        var imgui = new Event { type = EventType.ExecuteCommand, commandName = "SelectAll" };
        yield return Select(field, 0, 0);
        using (var e = ExecuteCommandEvent.GetPooled(imgui))
        {
            Put("imgui.commandName", e.commandName);
            Send(e, Input(field));
        }
        Put("imgui.execute.selectAll", Counts() + State(field));

        // A multiline field keeps the line breaks it pastes.
        multi.Focus();
        yield return Settle();
        yield return Select(multi, 2, 2);
        GUIUtility.systemCopyBuffer = "x\ny";
        Execute("multi.execute.paste", multi, "Paste");

        // A read-only field selects and copies but does not cut or paste; focusing it leaves the
        // IME mode as it was (set with nothing focused).
        multi.Blur();
        yield return Settle();
        UnityEngine.Input.imeCompositionMode = IMECompositionMode.Off;
        locked.Focus();
        yield return Settle();
        Put("ime.lockedFocused", UnityEngine.Input.imeCompositionMode);
        yield return Select(locked, 0, 6);
        GUIUtility.systemCopyBuffer = "CLIP";
        foreach (var command in new[] { "Copy", "Cut", "Paste", "SelectAll", "Find" })
            Validate("locked.validate." + command, locked, command);
        Execute("locked.execute.copy", locked, "Copy");
        Execute("locked.execute.cut", locked, "Cut");
        Execute("locked.execute.paste", locked, "Paste");
        Execute("locked.execute.selectAll", locked, "SelectAll");

        // Another field focused: commands sent to the first are not taken.
        other.Focus();
        yield return Settle();
        yield return Select(field, 0, 3);
        Execute("otherFocused.execute.cut", field, "Cut");

        // The focused field disabled.
        yield return Select(other, 0, 3);
        other.SetEnabled(false);
        yield return Settle();
        Put("disabled.focused", Focused(other));
        Put("disabled.ime", UnityEngine.Input.imeCompositionMode);
        Validate("disabled.validate.Copy", other, "Copy");
        Validate("disabled.validate.Find", other, "Find");
        Execute("disabled.execute.selectAll", other, "SelectAll");
        Execute("disabled.execute.cut", other, "Cut");
        other.SetEnabled(true);
        yield return Settle();

        // The IME event: no composition is active here, so the text stays.
        field.Focus();
        yield return Settle();
        yield return Select(field, 1, 3);
        var imeType = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.IMEEvent");
        Put("imeEvent.type", imeType == null ? "<missing>" : imeType.IsPublic + " " + imeType.BaseType.Name);
        var getPooled = imeType.GetMethod("GetPooled", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        var composition = imeType.GetProperty("compositionString");
        Reset();
        var ime = (EventBase)getPooled.Invoke(null, new object[] { "ka" });
        Put("imeEvent.composition", composition.GetValue(ime));
        Send(ime, Input(field));
        Put("imeEvent.focused", Counts() + State(field) + " stopped " + ime.isPropagationStopped);
        ime.Dispose();
        var reused = (EventBase)imeType.BaseType.GetMethod("GetPooled", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null).Invoke(null, null);
        Put("imeEvent.reuse", ReferenceEquals(ime, reused) + " " + (composition.GetValue(reused) ?? "<null>"));
        reused.Dispose();
        other.SetEnabled(false);
        yield return Settle();
        Reset();
        using (var e = (EventBase)getPooled.Invoke(null, new object[] { "ka" })) Send(e, Input(other));
        Put("imeEvent.disabledTarget", Counts());
        other.SetEnabled(true);
        Reset();
        using (var e = (EventBase)getPooled.Invoke(null, new object[] { "ka" })) Send(e, null);
        Put("imeEvent.panel", Counts() + State(field));

        field.Blur();
        yield return Settle();
        Put("ime.blurred", UnityEngine.Input.imeCompositionMode);
        Put("blurred.selection", field.cursorIndex + " " + field.selectIndex);
    }
}
