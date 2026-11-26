using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $SMALLSTUBS_OUT (tests/value_probe.py rows): API members of smaller
/// systems Unbound had as stubs (Q820): Texture2D.LoadRawTextureData from a NativeArray (pixels
/// after a load, and its errors) and ShaderUtil's pass compilation queries.
/// </summary>
public class SmallStubsProbe : MonoBehaviour
{
    const string Variable = "SMALLSTUBS_OUT";
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        string value;
        try { value = action(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ": " + e.Message; }
        Row(key, value);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("SmallStubsProbe").AddComponent<SmallStubsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        Application.logMessageReceived += (message, stack, type) => Row("log", type + ": " + message);
        try
        {
            RawTextureData();
            ShaderQueries();
            Palettes();
        }
        catch (Exception e) { Row("aborted", e.GetType().Name + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
        EditorApplication.Exit(0);
    }

    static string Pixels(Texture2D texture) => string.Join(" ", texture.GetPixels32().Select(c => $"{c.r},{c.g},{c.b},{c.a}"));

    static void RawTextureData()
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        using (var colors = new NativeArray<Color32>(new[] { new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8), new Color32(9, 10, 11, 12), new Color32(13, 14, 15, 16) }, Allocator.Temp))
            Try("raw.color32", () => { texture.LoadRawTextureData(colors); return Pixels(texture); });
        using (var bytes = new NativeArray<byte>(Enumerable.Range(100, 16).Select(i => (byte)i).ToArray(), Allocator.Temp))
            Try("raw.bytes", () => { texture.LoadRawTextureData(bytes); return Pixels(texture); });
        using (var longer = new NativeArray<byte>(Enumerable.Range(0, 24).Select(i => (byte)(200 + i)).ToArray(), Allocator.Temp))
            Try("raw.longer", () => { texture.LoadRawTextureData(longer); return Pixels(texture); });
        using (var shorter = new NativeArray<byte>(new byte[] { 1, 2, 3 }, Allocator.Temp))
            Try("raw.shorter", () => { texture.LoadRawTextureData(shorter); return Pixels(texture); });
        using (var empty = new NativeArray<byte>(0, Allocator.Temp))
            Try("raw.empty", () => { texture.LoadRawTextureData(empty); return Pixels(texture); });
        Try("raw.default", () => { texture.LoadRawTextureData(default(NativeArray<byte>)); return Pixels(texture); });
        var unreadable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        unreadable.Apply(false, true);
        using (var bytes = new NativeArray<byte>(16, Allocator.Temp))
            Try("raw.unreadable", () => { unreadable.LoadRawTextureData(bytes); return "loaded"; });
        using (var empty = new NativeArray<byte>(0, Allocator.Temp))
            Try("raw.unreadable_empty", () => { unreadable.LoadRawTextureData(empty); return "loaded"; });
    }

    static void ShaderQueries()
    {
        var unlit = Shader.Find("Unlit/Color");
        var standard = Shader.Find("Standard");
        Try("shader.hasProceduralInstancing.unlit", () => ShaderUtil.HasProceduralInstancing(unlit).ToString());
        Try("shader.hasProceduralInstancing.standard", () => ShaderUtil.HasProceduralInstancing(standard).ToString());
        Try("shader.hasProceduralInstancing.procedural", () => ShaderUtil.HasProceduralInstancing(Shader.Find("SmallStubs/Procedural")).ToString());
        Try("shader.hasProceduralInstancing.instanced", () => ShaderUtil.HasProceduralInstancing(Shader.Find("SmallStubs/Instanced")).ToString());
        Try("shader.hasProceduralInstancing.null", () => ShaderUtil.HasProceduralInstancing(null).ToString());
        var material = new Material(unlit);
        Try("shader.isPassCompiled.before", () => ShaderUtil.IsPassCompiled(material, 0).ToString());
        Try("shader.compilePass", () => { ShaderUtil.CompilePass(material, 0, true); return "done"; });
        Try("shader.isPassCompiled.after", () => ShaderUtil.IsPassCompiled(material, 0).ToString());
        Try("shader.isPassCompiled.otherMaterial", () => ShaderUtil.IsPassCompiled(new Material(unlit), 0).ToString());
        var keyed = new Material(standard);
        keyed.EnableKeyword("_EMISSION");
        Try("shader.isPassCompiled.standard.before", () => ShaderUtil.IsPassCompiled(keyed, 0).ToString());
        // The same pass compiled without the keyword: is the keyword's variant compiled?
        var plain = new Material(standard);
        Try("shader.compilePass.standard.plain", () => { ShaderUtil.CompilePass(plain, 0, true); return "done"; });
        Try("shader.isPassCompiled.standard.plain", () => ShaderUtil.IsPassCompiled(plain, 0).ToString());
        Try("shader.isPassCompiled.standard.keyedAfterPlain", () => ShaderUtil.IsPassCompiled(keyed, 0).ToString());
        Try("shader.compilePass.standard.async", () => { ShaderUtil.CompilePass(keyed, 0); return "done"; });
        Try("shader.isPassCompiled.standard.afterAsync", () => ShaderUtil.IsPassCompiled(keyed, 0).ToString());
        Try("shader.isPassCompiled.standard.otherPass", () => ShaderUtil.IsPassCompiled(keyed, 1).ToString());
        var other = new Material(standard);
        other.EnableKeyword("_NORMALMAP");
        Try("shader.isPassCompiled.standard.otherKeyword", () => ShaderUtil.IsPassCompiled(other, 0).ToString());
        Try("shader.isPassCompiled.badPass", () => ShaderUtil.IsPassCompiled(material, 7).ToString());
        Try("shader.isPassCompiled.null", () => ShaderUtil.IsPassCompiled(null, 0).ToString());
    }

    static string Colors(Color[] colors, int count) =>
        count + ": " + string.Join(" ", colors.Select(c => $"{c.r:R},{c.g:R},{c.b:R},{c.a:R}"));

    /// <summary>VisionUtility.GetColorBlindSafePalette: the palette whole (a long array, every
    /// luminance), cycled when longer than it, filtered by luminance ranges, and its errors; then
    /// ShaderUtil's hardware answers and ClearCachedData.</summary>
    static void Palettes()
    {
        foreach (var (key, length, min, max) in new[] { ("all", 40, 0f, 1f), ("short", 3, 0f, 1f), ("mid", 12, 0.3f, 0.7f),
                     ("bright", 12, 0.8f, 1f), ("dark", 12, 0f, 0.2f), ("none", 4, 2f, 3f), ("inverted", 4, 0.9f, 0.1f), ("empty", 0, 0f, 1f) })
            Try("palette." + key, () => { var colors = new Color[length]; int n = UnityEngine.Accessibility.VisionUtility.GetColorBlindSafePalette(colors, min, max); return Colors(colors, n); });
        Try("palette.null", () => UnityEngine.Accessibility.VisionUtility.GetColorBlindSafePalette(null, 0f, 1f).ToString());
        Try("shader.hardwareSupportsRectRenderTexture", () => ShaderUtil.hardwareSupportsRectRenderTexture.ToString());
        Try("shader.clearCachedData", () => { ShaderUtil.ClearCachedData(Shader.Find("Unlit/Color")); return "done"; });
        Try("shader.clearCachedData.null", () => { ShaderUtil.ClearCachedData(null); return "done"; });
    }
}
