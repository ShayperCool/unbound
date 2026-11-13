using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Records how texture and graphics formats map onto each other: GraphicsFormatUtility.
/// GetTextureFormat for every GraphicsFormat, GetGraphicsFormat for every TextureFormat (linear and
/// sRGB), and the format and graphicsFormat of 2D, 3D and array textures created from the
/// single- and two-channel 8-bit GraphicsFormats. Writes values.jsonl into FORMAT_MAPPING_OUT and exits.
/// </summary>
public sealed class FormatMappingProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("FormatMappingProbe").AddComponent<FormatMappingProbe>();

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");

    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        try
        {
            // By name: some values have two names.
            foreach (var name in Enum.GetNames(typeof(GraphicsFormat)))
            {
                var g = (GraphicsFormat)Enum.Parse(typeof(GraphicsFormat), name);
                Rec("texture/" + name, Try(() => ((int)GraphicsFormatUtility.GetTextureFormat(g)).ToString()));
            }
            foreach (var name in Enum.GetNames(typeof(TextureFormat)))
            {
                var t = (TextureFormat)Enum.Parse(typeof(TextureFormat), name);
                Rec("graphics/" + name + "/linear", Try(() => ((int)GraphicsFormatUtility.GetGraphicsFormat(t, false)).ToString()));
                Rec("graphics/" + name + "/srgb", Try(() => ((int)GraphicsFormatUtility.GetGraphicsFormat(t, true)).ToString()));
            }
            foreach (var g in new[] { GraphicsFormat.R8_UNorm, GraphicsFormat.R8_SRGB, GraphicsFormat.R8G8_UNorm, GraphicsFormat.R16_UNorm })
            {
                Rec("create2d/" + g, Try(() => { var x = new Texture2D(4, 4, g, TextureCreationFlags.None); return (int)x.format + " " + (int)x.graphicsFormat; }));
                Rec("create3d/" + g, Try(() => { var x = new Texture3D(4, 4, 2, g, TextureCreationFlags.None, 1); return (int)x.format + " " + (int)x.graphicsFormat; }));
                Rec("createArray/" + g, Try(() => { var x = new Texture2DArray(4, 4, 2, g, TextureCreationFlags.None); return (int)x.format + " " + (int)x.graphicsFormat; }));
            }
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            var dir = Environment.GetEnvironmentVariable("FORMAT_MAPPING_OUT");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString()); }
        }
        catch (Exception e) { Debug.LogError("format mapping probe failed: " + e); code = 1; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
