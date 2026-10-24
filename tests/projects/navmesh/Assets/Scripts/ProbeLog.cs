using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// The probe's results: per scenario, an ordered list of named values (numbers written with
/// round-trip precision, or text), serialized as JSON. Engines are compared value by value.
/// </summary>
public static class ProbeLog
{
    static readonly List<(string Scenario, List<(string Key, string Json)> Values)> s_Scenarios =
        new List<(string, List<(string, string)>)>();
    static List<(string Key, string Json)> s_Current;

    public static void Begin(string scenario)
    {
        s_Current = new List<(string, string)>();
        s_Scenarios.Add((scenario, s_Current));
    }

    static string Number(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? "\"" + v.ToString(CultureInfo.InvariantCulture) + "\"" : v.ToString("R", CultureInfo.InvariantCulture);

    static string Text(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "null")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    public static void Value(string key, float v) => s_Current.Add((key, Number(v)));
    public static void Value(string key, int v) => s_Current.Add((key, v.ToString(CultureInfo.InvariantCulture)));
    public static void Value(string key, bool v) => s_Current.Add((key, v ? "true" : "false"));
    public static void Value(string key, string v) => s_Current.Add((key, Text(v)));

    public static void Value(string key, Vector2 v)
    {
        Value(key + ".x", v.x);
        Value(key + ".y", v.y);
    }

    public static void Value(string key, Vector3 v)
    {
        Value(key + ".x", v.x);
        Value(key + ".y", v.y);
        Value(key + ".z", v.z);
    }

    public static void Value(string key, Quaternion q) => Value(key, q.eulerAngles);

    public static void Error(System.Exception e) => Value("error", e.GetType().Name + ": " + e.Message);

    public static string Json()
    {
        var b = new StringBuilder("{\n");
        for (int i = 0; i < s_Scenarios.Count; i++)
        {
            var (scenario, values) = s_Scenarios[i];
            b.Append("  ").Append(Text(scenario)).Append(": {");
            for (int j = 0; j < values.Count; j++)
            {
                b.Append(j == 0 ? "\n" : ",\n").Append("    ").Append(Text(values[j].Key)).Append(": ").Append(values[j].Json);
            }
            b.Append("\n  }").Append(i + 1 < s_Scenarios.Count ? ",\n" : "\n");
        }
        return b.Append("}\n").ToString();
    }
}
