using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Appends one JSON line per record to <c>SCENE_ACTIVATION_OUT</c>. Every record opens,
/// appends and closes the file, so records survive the domain reload on entering Play mode and
/// the file is closed between records. After <see cref="Complete"/> nothing more is written.
/// The record kinds and their exact keys are fixed in contract.py.</summary>
public static class WakeLog
{
    static readonly string s_Path = Environment.GetEnvironmentVariable("SCENE_ACTIVATION_OUT");
    static bool s_Closed;

    public static bool Enabled => !string.IsNullOrEmpty(s_Path) && !s_Closed;

    /// <summary>A marker's Awake/OnEnable/Start in Play mode, with the raw instance ids of the
    /// component, its GameObject and its Transform.</summary>
    public static void Callback(MonoBehaviour marker, string name)
    {
        if (!Enabled || !Application.isPlaying) return;
        var go = marker.gameObject;
        new Record("cb").Add("ev", name).Add("scene", go.scene.name).Add("obj", PathOf(go))
            .Add("type", marker.GetType().Name).Add("frame", Time.frameCount)
            .Add("cid", marker.GetInstanceID()).Add("gid", go.GetInstanceID())
            .Add("tid", go.transform.GetInstanceID()).Write();
    }

    /// <summary>The scene's GetRootGameObjects order, then every object in pre-order (each
    /// root, then its children by sibling index).</summary>
    public static void Hierarchy(string at, Scene scene, bool frame)
    {
        var roots = new List<string>();
        var nodes = new List<string>();
        foreach (var root in scene.GetRootGameObjects())
        {
            roots.Add(root.name);
            Walk(root.transform, nodes);
        }
        var r = new Record("roots").Add("at", at).Add("scene", scene.name);
        if (frame) r.Add("frame", Time.frameCount);
        r.Add("roots", roots).Write();
        var t = new Record("tree").Add("at", at).Add("scene", scene.name);
        if (frame) t.Add("frame", Time.frameCount);
        t.Add("nodes", nodes).Write();
    }

    static void Walk(Transform t, List<string> nodes)
    {
        nodes.Add(PathOf(t.gameObject));
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), nodes);
    }

    /// <summary>The single terminal record; the log is closed after it.</summary>
    public static void Complete()
    {
        if (!Enabled) return;
        new Record("complete").Add("v", true).Write();
        s_Closed = true;
    }

    public static string PathOf(GameObject go)
    {
        var path = go.name;
        for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
        return path;
    }

    public sealed class Record
    {
        readonly StringBuilder m_Text = new StringBuilder("{");
        bool m_First = true;

        public Record(string kind) => Add("k", kind);

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

        public Record Add(string key, int value)
        {
            Key(key).m_Text.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public Record Add(string key, bool value)
        {
            Key(key).m_Text.Append(value ? "true" : "false");
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
}
