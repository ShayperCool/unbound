using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Writes the probe's records as JSON lines {"k": key, "v": value}.
public static class ProbeLog
{
    static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
    static string Out => System.Environment.GetEnvironmentVariable("MISSING_PREFAB_OUT");

    public static int Next(string key)
    {
        counters.TryGetValue(key, out int n);
        counters[key] = n + 1;
        return n;
    }

    public static void Put(string key, string value)
    {
        if (Out == null) return;
        var line = new StringBuilder("{\"k\": ");
        Quote(line, key);
        line.Append(", \"v\": ");
        Quote(line, value);
        line.Append("}\n");
        File.AppendAllText(Out, line.ToString());
    }

    static void Quote(StringBuilder b, string s)
    {
        b.Append('"');
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        b.Append('"');
    }

    public static string Path(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Insert(0, t.GetSiblingIndex() + ":" + t.name);
        return string.Join("/", parts);
    }

    public static string Describe(Object o)
    {
        if (ReferenceEquals(o, null)) return "null";
        if (o == null) return "destroyed-or-missing";
        if (o is GameObject go) return "GameObject " + Path(go.transform);
        if (o is Component c) return c.GetType().Name + " " + Path(c.transform);
        return o.GetType().Name + " " + o.name;
    }

    static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Node(GameObject go)
    {
        var b = new StringBuilder();
        b.Append(go.name).Append(" active=").Append(go.activeSelf ? 1 : 0);
        var p = go.transform.localPosition;
        b.Append(" pos=").Append(F(p.x)).Append(',').Append(F(p.y)).Append(',').Append(F(p.z));
        b.Append(" components=");
        var components = go.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (i > 0) b.Append(',');
            b.Append(components[i] == null ? "Missing" : components[i].GetType().Name);
        }
        var marker = go.GetComponent<Marker>();
        if (marker != null) b.Append(" marker=").Append(marker.value);
        return b.ToString();
    }

    public static void Tree(string context, GameObject root)
    {
        if (root == null) { Put(context, "null"); return; }
        Walk(context, root.transform);
    }

    static void Walk(string context, Transform t)
    {
        Put(context + "/" + Path(t), Node(t.gameObject));
        for (int i = 0; i < t.childCount; i++) Walk(context, t.GetChild(i));
    }

    public static void Refs(string context, Holder h)
    {
        if (h == null) { Put(context + "/holder", "null"); return; }
        var objects = h.objects;
        Put(context + "/objects.Length", objects == null ? "null" : objects.Length.ToString());
        if (objects != null)
            for (int i = 0; i < objects.Length; i++) Put(context + "/objects[" + i + "]", Describe(objects[i]));
        Put(context + "/childTransform", Describe(h.childTransform));
        Put(context + "/marker", Describe(h.marker));
        Put(context + "/box", Describe(h.box));
        Put(context + "/sphere", Describe(h.sphere));
        var parts = h.parts;
        Put(context + "/parts.Length", parts == null ? "null" : parts.Length.ToString());
        if (parts != null)
            for (int i = 0; i < parts.Length; i++) Put(context + "/parts[" + i + "]", Describe(parts[i]));
        Put(context + "/spawn", h.spawn == null ? Describe(h.spawn) : "GameObject " + h.spawn.name);
    }
}
