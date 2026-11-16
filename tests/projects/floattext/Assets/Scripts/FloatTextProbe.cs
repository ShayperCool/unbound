using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the text of float and double values into values.jsonl in $FLOAT_TEXT_OUT
/// (tests/value_probe.py): values m * 2^e whose exact decimal expansion is short, so that many of
/// them fall exactly halfway between two texts at the printed precision, and their neighbours,
/// in the default, "R", "G", "E", "F", "N" and "P" formats, through ToString, string.Format,
/// concatenation and Vector3.ToString.
/// </summary>
public class FloatTextProbe : MonoBehaviour
{
    static StreamWriter output;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}\n");

    static readonly string[] FloatFormats = { "", "R", "G", "G6", "G7", "G8", "G9", "G12", "E", "E6", "E7", "E8", "E12", "F0", "F2", "F4", "N3", "P1", "r", "g8", "e7" };
    static readonly string[] DoubleFormats = { "", "R", "G", "G14", "G15", "G16", "G17", "G20", "E", "E14", "E15", "E16", "E20", "F2", "F6" };

    // Odd multipliers: small ones, ones ending in 5 and 25, and ones near the top of each mantissa.
    static readonly long[] FloatMultipliers =
        { 1, 3, 5, 7, 9, 13, 25, 61, 63, 125, 245, 255, 625, 1023, 3125, 4097, 12345, 65535, 78125, 1048575, 1234567, 8388607, 12345665, 16777215 };
    static readonly long[] DoubleMultipliers =
        { 1, 3, 5, 7, 25, 61, 245, 625, 3125, 78125, 1953125, 48828125, 1220703125, 30517578125, 4503599627370497, 9007199254740991 };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var dir = Environment.GetEnvironmentVariable("FLOAT_TEXT_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            using (output = new StreamWriter(Path.Combine(dir, "values.jsonl")))
            {
                Write();
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static void Float(float value)
    {
        var key = "f " + BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("x8");
        foreach (var format in FloatFormats)
            Row(key + " " + (format == "" ? "default" : format), format == "" ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(format, CultureInfo.InvariantCulture));
    }

    static void Double(double value)
    {
        var key = "d " + BitConverter.DoubleToInt64Bits(value).ToString("x16");
        foreach (var format in DoubleFormats)
            Row(key + " " + (format == "" ? "default" : format), format == "" ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(format, CultureInfo.InvariantCulture));
    }

    static void Write()
    {
        foreach (var m in FloatMultipliers)
            for (int e = -30; e <= 2; e++)
            {
                float value = (float)(m * Math.Pow(2, e));
                if ((double)value != m * Math.Pow(2, e)) continue; // not a float
                Float(value);
                if (m == 245 || m == 12345665) Float(-value);
            }
        // Very small and very large magnitudes (beyond decimal's range), subnormals among them.
        foreach (var m in new long[] { 1, 3, 245, 12345665 })
            foreach (var e in new[] { -149, -140, -130, -126, -120, -110, -100, -96, 90, 96, 100, 103 })
            {
                double exact = m * Math.Pow(2, e);
                float value = (float)exact;
                if ((double)value == exact && !float.IsInfinity(value)) Float(value);
            }
        foreach (var m in new long[] { 1, 3, 245, 4503599627370497 })
            foreach (var e in new[] { -1074, -1060, -1030, -1022, -200, -100, 100, 200, 960 })
            {
                double value = m * Math.Pow(2, e);
                if (!double.IsInfinity(value) && value != 0) Double(value);
            }
        // Integers whose digits end in 5 before a trailing zero: ties at 15 digits (and neighbours).
        foreach (var value in new[] { 10000000000000050.0, 12345678901234550.0, -12345678901234550.0, 10000000000000040.0, 10000000000000060.0 })
            Double(value);
        // Neighbours of a tie: the floats on either side of 245 / 1024.
        float tie = 245f / 1024f;
        Float(BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(tie), 0) - 1), 0));
        Float(BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(tie), 0) + 1), 0));
        foreach (var m in DoubleMultipliers)
            for (int e = -60; e <= 2; e++)
            {
                double value = m * Math.Pow(2, e);
                Double(value);
                if (m == 1 && (e == -22 || e == -25)) Double(-value);
            }
        // The same values through the calls that format their arguments.
        float f = 245f / 1024f, g = 12345665f;
        double d = Math.Pow(2, -22);
        Row("format float", string.Format(CultureInfo.InvariantCulture, "{0} {0:R} {0:E3} {1}", f, g));
        Row("format double", string.Format(CultureInfo.InvariantCulture, "{0} {0:R} {0:G16}", d));
        Row("concat", "x" + f + " " + g + " " + d + " " + (0.00048828125f));
        Row("vector3", new Vector3(f, 0.00048828125f, 0.125f).ToString() + " " + new Vector3(f, 0.00048828125f, 0.125f).ToString("F5") + " " + new Vector3(f, g, 0.125f).ToString("G9"));
    }
}
