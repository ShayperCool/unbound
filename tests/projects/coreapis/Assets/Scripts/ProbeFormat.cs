using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>Fixed, culture-independent text for the record: floats as their IEEE-754 bits.</summary>
public static class P
{
    public static string Bits(float v) => BitConverter.ToInt32(BitConverter.GetBytes(v), 0).ToString("x8");
    public static string F(float v)
    {
        var s = v.ToString("F3", CultureInfo.InvariantCulture);
        return s == "-0.000" ? "0.000" : s;
    }

    public static string B(bool b) => b ? "1" : "0";
    public static string V(Vector3 v) => $"({F(v.x)},{F(v.y)},{F(v.z)})";
    public static string VB(Vector3 v) => $"({Bits(v.x)},{Bits(v.y)},{Bits(v.z)})";
    public static string QB(Quaternion q) => $"({Bits(q.x)},{Bits(q.y)},{Bits(q.z)},{Bits(q.w)})";
    public static string PB(Pose p) => VB(p.position) + QB(p.rotation);

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
    public static string ExType(Exception e) => e.GetType().FullName;
}
