using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>The probe's rows, one JSON object per line, in the file SCENESAVE_LOG names (run.py
/// sets it; without it nothing is written).</summary>
public static class ProbeLog
{
    public static void Row(string key, object value)
    {
        var path = Environment.GetEnvironmentVariable("SCENESAVE_LOG");
        if (string.IsNullOrEmpty(path)) return;
        var text = value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => Quote(value.ToString()),
        };
        File.AppendAllText(path, "{\"key\": " + Quote(key) + ", \"value\": " + text + "}\n");
    }

    /// <summary>The file's SHA-256 (hex), or "missing".</summary>
    public static string Hash(string projectPath)
    {
        if (!File.Exists(projectPath)) return "missing";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(File.ReadAllBytes(projectPath));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>Keeps a copy of the file as it is now (beside the log, under `snapshots/`), and
    /// logs its hash.</summary>
    public static void Snapshot(string key, string projectPath)
    {
        Row(key, Hash(projectPath));
        var log = Environment.GetEnvironmentVariable("SCENESAVE_LOG");
        if (string.IsNullOrEmpty(log) || !File.Exists(projectPath)) return;
        var target = Path.Combine(Path.GetDirectoryName(log), "snapshots", key);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(projectPath, target, true);
    }

    /// <summary>Runs a step; an exception becomes the row's value.</summary>
    public static void Try(string key, Func<object> step)
    {
        try { Row(key, step()); }
        catch (Exception e) { Row(key, "throws " + e.GetType().Name + ": " + e.Message); }
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
