using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>The probe's record: one JSON object per line, in the order things happen. Frames are
/// the probe's own Update count (-1 before its first Update).</summary>
public static class ProbeLog
{
    static readonly List<string> s_Lines = new List<string>();

    public static int frame = -1;

    public static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static string B(bool v) => v ? "true" : "false";

    public static string Q(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        return b.Append('"').ToString();
    }

    /// <summary>Adds a record: <paramref name="fields"/> are pairs of name and already-encoded value.</summary>
    public static void Add(string kind, params string[] fields)
    {
        var b = new StringBuilder();
        b.Append("{\"f\":").Append(frame).Append(",\"k\":").Append(Q(kind));
        for (int i = 0; i + 1 < fields.Length; i += 2)
            b.Append(',').Append(Q(fields[i])).Append(':').Append(fields[i + 1]);
        b.Append('}');
        s_Lines.Add(b.ToString());
    }

    public static void Write()
    {
        var path = Environment.GetEnvironmentVariable("CULLINGGROUP_PROBE_OUT");
        if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.temporaryCachePath, "cullinggroup-probe.jsonl");
        File.WriteAllText(path, string.Join("\n", s_Lines) + "\n");
        Debug.Log($"[probe] wrote {s_Lines.Count} records to {path}");
    }
}
