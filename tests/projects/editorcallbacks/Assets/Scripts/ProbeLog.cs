using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Appends one JSON line per record to <c>EDITOR_CALLBACKS_OUT</c>, immediately, so
/// records survive the Editor's domain reload on entering Play mode.</summary>
public static class ProbeLog
{
    static readonly string s_Path = Environment.GetEnvironmentVariable("EDITOR_CALLBACKS_OUT");

    static int s_AsyncFrame = -1, s_AsyncBase = -1;

    public static bool Enabled => !string.IsNullOrEmpty(s_Path);

    /// <summary>An asynchronous load was requested in this frame.</summary>
    public static void AsyncRequested() => s_AsyncFrame = Time.frameCount;

    public static Record Event(string name) => new Record().Add("event", name);

    public sealed class Record
    {
        readonly StringBuilder m_Text = new StringBuilder("{");
        bool m_First = true;

        Record Key(string key)
        {
            if (!m_First) m_Text.Append(',');
            m_First = false;
            Quote(m_Text, key);
            m_Text.Append(':');
            return this;
        }

        public Record Add(string key, string value)
        {
            Key(key);
            if (value == null) m_Text.Append("null");
            else Quote(m_Text, value);
            return this;
        }

        public Record Add(string key, bool value)
        {
            Key(key).m_Text.Append(value ? "true" : "false");
            return this;
        }

        public Record Add(string key, int value)
        {
            Key(key).m_Text.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public Record Add(string key, IEnumerable<string> values)
        {
            Key(key).m_Text.Append('[');
            bool first = true;
            foreach (var value in values)
            {
                if (!first) m_Text.Append(',');
                first = false;
                Quote(m_Text, value);
            }
            m_Text.Append(']');
            return this;
        }

        /// <summary>The play state and frame the record was made in. After an asynchronous load
        /// was requested, frames count from the first later frame instead (when it completes
        /// depends on the loading time).</summary>
        public Record Frame()
        {
#if UNITY_EDITOR
            Add("playing", UnityEditor.EditorApplication.isPlaying);
#endif
            if (s_AsyncFrame < 0 || Time.frameCount <= s_AsyncFrame) return Add("frame", Time.frameCount);
            if (s_AsyncBase < 0) s_AsyncBase = Time.frameCount;
            return Add("asyncFrame", Time.frameCount - s_AsyncBase);
        }

        public void Write()
        {
            if (!Enabled) return;
            File.AppendAllText(s_Path, m_Text.ToString() + "}\n");
        }
    }

    static void Quote(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4"));
                    else text.Append(c);
                    break;
            }
        }
        text.Append('"');
    }

    /// <summary>A GameObject or component as "scene:/Root/Child[:Type]".</summary>
    public static string PathOf(UnityEngine.Object o)
    {
        if (o == null) return null;
        var go = o as GameObject ?? (o as Component)?.gameObject;
        if (go == null) return o.GetType().Name + ":" + o.name;
        var path = go.name;
        for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
        var owner = go.scene.IsValid() ? go.scene.name : "-";
        return owner + ":/" + path + (o is Component ? ":" + o.GetType().Name : "");
    }
}
