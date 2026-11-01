using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>Fixed, culture-independent text for the record.</summary>
public static class P
{
    public static string F(float v)
    {
        var s = v.ToString("F3", CultureInfo.InvariantCulture);
        return s == "-0.000" ? "0.000" : s;
    }

    public static string B(bool b) => b ? "1" : "0";
    public static string V(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";

    public static string Q(Quaternion q)
    {
        // q and -q are the same rotation: print the one with w >= 0.
        if (q.w < 0 || (q.w == 0 && (q.x < 0 || (q.x == 0 && (q.y < 0 || (q.y == 0 && q.z < 0)))))) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
        return $"({F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)})";
    }

    public static string Esc(string s)
    {
        var b = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        return b.ToString();
    }

    public static string Ex(Exception e) => e.GetType().FullName + ": " + e.Message;
}
