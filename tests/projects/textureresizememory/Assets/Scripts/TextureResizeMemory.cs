using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;

public static class TextureResizeMemory
{
    static readonly List<string> Rows = new List<string>();

    [Serializable]
    sealed class Row
    {
        public string k, v;
    }

    static void Put(string key, object value)
        => Rows.Add(JsonUtility.ToJson(new Row { k = key, v = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    static void Size(string key, Texture2D texture)
        => Put(key, Profiler.GetRuntimeMemorySizeLong(texture));

    static void Resize(string key, TextureFormat format, bool mips, bool warm, int width, int height)
    {
        var texture = new Texture2D(64, 64, format, mips, true);
        Size(key + ".created", texture);
        if (warm) texture.Apply(false, false);
        Size(key + ".before", texture);
        Put(key + ".result", texture.Reinitialize(width, height));
        Size(key + ".resized", texture);
        if (format != TextureFormat.DXT1) texture.SetPixel(0, 0, Color.green);
        Size(key + ".cpu", texture);
        texture.Apply(false, false);
        Size(key + ".uploaded", texture);
        Put(key + ".again.result", texture.Reinitialize(8, 12));
        Size(key + ".again", texture);
        texture.Apply(false, true);
        Size(key + ".unreadable", texture);
        UnityEngine.Object.DestroyImmediate(texture);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        string output = Environment.GetEnvironmentVariable("TEXTURE_RESIZE_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Put("unity", Application.unityVersion);
        Put("device", SystemInfo.graphicsDeviceType);
        Resize("rgba.cold", TextureFormat.RGBA32, false, false, 128, 32);
        Resize("rgba.warm", TextureFormat.RGBA32, false, true, 16, 24);
        Resize("r8.mips", TextureFormat.R8, true, true, 32, 16);
        Resize("dxt1", TextureFormat.DXT1, false, true, 32, 16);
        var explicitFormat = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
        Put("explicit.result", explicitFormat.Reinitialize(12, 20, GraphicsFormat.R8_UNorm, false));
        Size("explicit.resized", explicitFormat);
        explicitFormat.Apply(false, false);
        Size("explicit.uploaded", explicitFormat);
        UnityEngine.Object.DestroyImmediate(explicitFormat);
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
