using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records the value a shader samples from each texture property's default (white, black, gray,
/// grey, bump, red, linearGray), from a sampler without a property, from a default after the
/// material set the property to null, and from the built-in textures of <see cref="Texture2D"/>
/// (whiteTexture, blackTexture, grayTexture, linearGrayTexture, normalTexture, redTexture) set on
/// a property: one texel drawn into a float render target, per channel to four decimals. The
/// project is in the linear colour space. Writes values.jsonl into DEFAULT_TEXTURES_OUT and exits.
/// </summary>
public sealed class DefaultTexturesProbe : MonoBehaviour
{
    static readonly string[] Defaults = { "white", "black", "gray", "grey", "bump", "red", "linearGray", "unassigned" };
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("DefaultTexturesProbe").AddComponent<DefaultTexturesProbe>();

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        string dir = Environment.GetEnvironmentVariable("DEFAULT_TEXTURES_OUT");
        try
        {
            Record();
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
            if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
            else
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogError("default textures probe failed: " + e);
            code = 1;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    void Record()
    {
        m_Out.Append("{\"k\":\"colorSpace\",\"v\":\"").Append(QualitySettings.activeColorSpace).Append("\"}\n");
        var shader = Shader.Find("Probe/DefaultTexels");
        for (int i = 0; i < Defaults.Length; i++)
        {
            var material = new Material(shader);
            Rec("default." + Defaults[i], Sample(material, i));
            Destroy(material);
        }
        // A property set to null samples its default again.
        for (int i = 0; i < 7; i++)
        {
            var material = new Material(shader);
            material.SetTexture("_T" + i, null);
            Rec("null." + Defaults[i], Sample(material, i));
            Destroy(material);
        }
        // A property whose texture was destroyed: the material still holds it (it compares equal
        // to null), and what the shader samples then.
        for (int i = 0; i < 7; i++)
        {
            var material = new Material(shader);
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var texels = new Color32[16];
            for (int t = 0; t < texels.Length; t++) texels[t] = new Color32(64, 128, 192, 255);
            texture.SetPixels32(texels);
            texture.Apply();
            material.SetTexture("_T" + i, texture);
            DestroyImmediate(texture);
            var held = material.GetTexture("_T" + i);
            m_Out.Append("{\"k\":\"destroyed.").Append(Defaults[i]).Append(".held\",\"v\":\"")
                .Append(ReferenceEquals(held, null) ? "none" : held == null ? "destroyed" : "alive").Append("\"}\n");
            Rec("destroyed." + Defaults[i], Sample(material, i));
            Destroy(material);
        }
        var builtins = new (string, Texture2D)[]
        {
            ("whiteTexture", Texture2D.whiteTexture), ("blackTexture", Texture2D.blackTexture),
            ("grayTexture", Texture2D.grayTexture), ("linearGrayTexture", Texture2D.linearGrayTexture),
            ("normalTexture", Texture2D.normalTexture), ("redTexture", Texture2D.redTexture),
        };
        foreach (var (name, texture) in builtins)
        {
            var material = new Material(shader);
            material.SetTexture("_T0", texture);
            Rec("builtin." + name, Sample(material, 0));
            m_Out.Append("{\"k\":\"builtin.").Append(name).Append(".format\",\"v\":\"").Append(texture.graphicsFormat).Append("\"}\n");
            Destroy(material);
        }
    }

    void Rec(string key, Color c)
    {
        foreach (var (channel, value) in new[] { ("r", c.r), ("g", c.g), ("b", c.b), ("a", c.a) })
            m_Out.Append("{\"k\":\"").Append(key).Append('.').Append(channel).Append("\",\"v\":")
                .Append(Math.Round(value, 4).ToString("0.0###", CultureInfo.InvariantCulture)).Append("}\n");
    }

    static Color Sample(Material material, int pick)
    {
        material.SetFloat("_Pick", pick);
        var target = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        Graphics.Blit(null, target, material);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        var c = read.GetPixel(0, 0);
        Destroy(read);
        target.Release();
        Destroy(target);
        return c;
    }
}
