using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records what imported cubemaps give a game: size, mip count, format, graphics format,
/// readability, each level's GetPixelData length per face, and texels of every face at two
/// places of the top level and at the corner of the last level (GetPixel, floats to 3
/// decimals). Then writes to a compressed cube and to an uncompressed one (SetPixel, SetPixels,
/// Apply without and with mips, a level copied through GetPixelData views then Apply): what each
/// throws and logs, and the texels, format and level bytes afterwards. Writes CUBE_COMPRESSION_PROBE_OUT (one {"k","v"} JSON line per value) and
/// quits.
/// </summary>
public class CubeCompressionProbe : MonoBehaviour
{
    public Cubemap[] cubes;

    readonly StringBuilder m_Out = new StringBuilder();

    void Add(string key, string value) =>
        m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");

    static string Try(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
    }


    readonly List<string> m_Logs = new List<string>();

    void OnLog(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message.Split('\n')[0]);

    /// <summary>Runs a write: "ok" or what it threw, then each message it logged.</summary>
    string Write(Action write)
    {
        m_Logs.Clear();
        string result;
        try { write(); result = "ok"; }
        catch (Exception e) { result = "throws " + e.GetType().Name + ": " + e.Message.Split('\n')[0]; }
        foreach (var log in m_Logs) result += " | " + log;
        return result;
    }

    void Writes(Cubemap cube)
    {
        string k = "write." + cube.name;
        int size = cube.width;
        var red = new Color(1f, 0f, 0f, 1f);
        var colors = new Color[size * size];
        for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0f, 0f, 1f, 1f);
        Add(k + ".setpixel", Write(() => cube.SetPixel(CubemapFace.PositiveX, 3, 5, red)));
        Add(k + ".setpixel.read", Try(() => Texel(cube.GetPixel(CubemapFace.PositiveX, 3, 5))));
        Add(k + ".setpixels", Write(() => cube.SetPixels(colors, CubemapFace.NegativeX)));
        Add(k + ".setpixels.read", Try(() => Texel(cube.GetPixel(CubemapFace.NegativeX, 3, 5))));
        Add(k + ".apply", Write(() => cube.Apply(false)));
        Add(k + ".apply.read", Try(() => Texel(cube.GetPixel(CubemapFace.PositiveX, 3, 5))));
        Add(k + ".applymips", Write(() => cube.Apply(true)));
        Add(k + ".applymips.last", Try(() => Texel(cube.GetPixel(CubemapFace.NegativeX, 0, 0, cube.mipmapCount - 1))));
        Add(k + ".format", cube.format.ToString());
        Add(k + ".bytes.0", Try(() => cube.GetPixelData<byte>(0, CubemapFace.PositiveX).Length.ToString()));
        // A write through GetPixelData views: -Y's top level copied into +Y's, read before and
        // after Apply; -Y is the unchanged control.
        Add(k + ".view", Write(() => cube.GetPixelData<byte>(0, CubemapFace.PositiveY).CopyFrom(cube.GetPixelData<byte>(0, CubemapFace.NegativeY))));
        Add(k + ".view.read", Try(() => Texel(cube.GetPixel(CubemapFace.PositiveY, 3, 5))));
        Add(k + ".view.apply", Write(() => cube.Apply(false)));
        Add(k + ".view.apply.read", Try(() => Texel(cube.GetPixel(CubemapFace.PositiveY, 3, 5))));
        Add(k + ".view.apply.other", Try(() => Texel(cube.GetPixel(CubemapFace.NegativeY, 3, 5))));
        Add(k + ".view.apply.mip1", Try(() => Texel(cube.GetPixel(CubemapFace.PositiveY, 1, 2, 1))));
    }

    static string Texel(Color c) => string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},{3:F3}", c.r, c.g, c.b, c.a);

    void Start()
    {
        foreach (var cube in cubes)
        {
            string k = cube.name;
            Add(k + ".size", cube.width + "x" + cube.height);
            Add(k + ".mips", cube.mipmapCount.ToString());
            Add(k + ".format", cube.format.ToString());
            Add(k + ".graphicsFormat", cube.graphicsFormat.ToString());
            Add(k + ".readable", cube.isReadable.ToString());
            for (int mip = 0; mip < cube.mipmapCount; mip++)
            {
                int m = mip;
                Add(k + ".bytes." + mip, Try(() =>
                {
                    var lengths = new List<string>();
                    for (int face = 0; face < 6; face++) lengths.Add(cube.GetPixelData<byte>(m, (CubemapFace)face).Length.ToString());
                    return string.Join(",", lengths);
                }));
            }
            int last = cube.mipmapCount - 1;
            for (int face = 0; face < 6; face++)
            {
                var f = (CubemapFace)face;
                Add(k + ".face" + face + ".a", Try(() => Texel(cube.GetPixel(f, 3, 5, 0))));
                Add(k + ".face" + face + ".b", Try(() => Texel(cube.GetPixel(f, 20, 27, 0))));
                Add(k + ".face" + face + ".last", Try(() => Texel(cube.GetPixel(f, 0, 0, last))));
            }
        }
        Application.logMessageReceived += OnLog;
        foreach (var cube in cubes)
            if (cube.name == "LdrOpaque" || cube.name == "HdrNormal" || cube.name == "LdrUncompressed") Writes(cube);
        Application.logMessageReceived -= OnLog;
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        var path = Environment.GetEnvironmentVariable("CUBE_COMPRESSION_PROBE_OUT");
        if (string.IsNullOrEmpty(path)) Debug.Log(m_Out.ToString());
        else File.WriteAllText(path, m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
