using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class PopupContentProbe : MonoBehaviour
{
    Action m_RecordGui;
    Action m_Finish;
    bool m_AttemptedGui;
    double m_ReadyAt;

    void OnGUI()
    {
        if (m_AttemptedGui || Event.current.type != EventType.Repaint) return;
        m_AttemptedGui = true;
        m_RecordGui();
    }

    void Update()
    {
        if (m_AttemptedGui && EditorApplication.timeSinceStartup >= m_ReadyAt) m_Finish();
    }

    sealed class DefaultContent : PopupWindowContent { }

    class MeasuredContent : PopupWindowContent
    {
        public readonly List<string> Calls = new List<string>();
        public EditorWindow OpenWindow { get; private set; }

        public override void OnOpen()
        {
            OpenWindow = editorWindow;
            Calls.Add(editorWindow == null ? "open without window" : "open with window");
        }

        public override void OnClose() => Calls.Add(ReferenceEquals(editorWindow, OpenWindow) ? "close with same window" : "close with different window");

        public override Vector2 GetWindowSize()
        {
            Calls.Add("size");
            return new Vector2(130, 90);
        }

        public override VisualElement CreateGUI()
        {
            Calls.Add("create");
            return null;
        }
    }

    sealed class RetainedContent : MeasuredContent
    {
        public readonly VisualElement Element = new VisualElement { name = "popup-content-probe" };

        public override VisualElement CreateGUI()
        {
            Calls.Add("create");
            return Element;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("POPUP_CONTENT_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, params object[] values) => output.Append("{\"k\":").Append(Json(key))
            .Append(",\"v\":").Append(Json(values)).Append("}\n");

        var defaults = new DefaultContent();
        var defaultSize = defaults.GetWindowSize();
        Put("base/defaults", defaults.editorWindow == null, defaultSize.x, defaultSize.y, defaults.CreateGUI() == null);
        Put("base/hooks", ExceptionFrom(() => { defaults.OnOpen(); defaults.OnGUI(Rect.zero); defaults.OnClose(); }),
            defaults.editorWindow == null);

        void Show(string key, Rect anchor, MeasuredContent content, bool recordGeometry = true)
        {
            string error = ExceptionFrom(() => UnityEditor.PopupWindow.Show(anchor, content));
            var window = content.editorWindow;
            Put(key, error, window != null, window?.GetType().Name, content.Calls);
            if (recordGeometry && window != null)
                Put("geometry/" + key, window.position.width, window.position.height);
        }

        var firstAnchor = new Rect(10, 20, 40, 16);
        var otherAnchor = new Rect(30, 50, 50, 18);
        var first = new MeasuredContent();
        Show("show/first", firstAnchor, first);
        var replacement = new MeasuredContent();
        Show("show/replacement", otherAnchor, replacement);
        Put("show/replaced", first.editorWindow == null, ReferenceEquals(first.editorWindow, first.OpenWindow), first.Calls);
        var repeated = new MeasuredContent();
        Show("show/sameAnchor", firstAnchor, repeated);
        var reopened = new MeasuredContent();
        Show("show/otherAnchor", otherAnchor, reopened);
        Put("show/previousAfterRepeat", replacement.editorWindow == null, replacement.Calls);
        foreach (var content in new[] { first, replacement, repeated, reopened })
            if (content.editorWindow != null) content.editorWindow.Close();

        var retained = new RetainedContent();
        Show("show/retained", firstAnchor, retained, recordGeometry: false);
        var retainedSize = retained.editorWindow != null ? retained.editorWindow.position.size : default;
        Put("geometry/retained", retainedSize.x, retainedSize.y);
        Put("show/retainedAttachment", retained.Element.parent != null);
        if (retained.editorWindow != null) retained.editorWindow.Close();
        Put("show/retainedClosed", retained.editorWindow == null, ReferenceEquals(retained.editorWindow, retained.OpenWindow),
            retained.Element.parent != null, retained.Calls);

        Put("show/null", ExceptionFrom(() => UnityEditor.PopupWindow.Show(new Rect(48, 76, 32, 20), null)));

        var guiAnchor = new Rect(16, 76, 32, 20);
        var probe = new GameObject("Popup content probe").AddComponent<PopupContentProbe>();
        probe.m_RecordGui = () =>
        {
            var content = new MeasuredContent();
            Show("show/onGUI", guiAnchor, content, recordGeometry: false);
            if (content.editorWindow != null) content.editorWindow.Close();
            Put("show/onGUIClosed", content.editorWindow == null, content.Calls);
            probe.m_ReadyAt = EditorApplication.timeSinceStartup + 0.25;
        };
        probe.m_Finish = () =>
        {
            var reopenedAfterDelay = new MeasuredContent();
            Show("show/sameAnchorAfterDelay", guiAnchor, reopenedAfterDelay, recordGeometry: false);
            if (reopenedAfterDelay.editorWindow != null) reopenedAfterDelay.editorWindow.Close();
            output.Append("{\"k\":\"complete\",\"v\":true}\n");
            File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
            EditorApplication.Exit(0);
        };
    }

    static string ExceptionFrom(Action action)
    {
        try { action(); return null; }
        catch (Exception error) { return error.GetType().Name + ": " + error.Message; }
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool flag => flag ? "true" : "false",
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
