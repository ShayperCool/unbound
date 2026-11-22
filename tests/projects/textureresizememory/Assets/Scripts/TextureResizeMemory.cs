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

    static void Compress(string key, TextureFormat format)
    {
        var texture = new Texture2D(8, 8, format, false, true);
        texture.Reinitialize(16, 24);
        Size(key + ".resized", texture);
        texture.Compress(false);
        Put(key + ".format", texture.format);
        Size(key + ".compressed", texture);
        texture.Apply(false, false);
        Size(key + ".applied", texture);
        UnityEngine.Object.DestroyImmediate(texture);
    }

    static void ResizeUnreadable(string key, Action<Texture2D> resize)
    {
        var texture = new Texture2D(16, 24, TextureFormat.RGBA32, false, true);
        texture.Apply(false, true);
        Size(key + ".before", texture);
        // Match the existing image dimensions and format to isolate profiler accounting
        // from the separate difference in how the runtimes reject unreadable input.
        try { resize(texture); }
        catch (UnityException) { }
        Size(key + ".after", texture);
        UnityEngine.Object.DestroyImmediate(texture);
    }

    static void RejectUnreadable(string key, Func<Texture2D, bool> resize)
    {
        var texture = new Texture2D(16, 24, TextureFormat.RGBA32, false, true) { name = "Unreadable texture" };
        texture.Apply(false, true);
        Size(key + ".before", texture);
        var logs = new List<string>();
        void Capture(string message, string stack, LogType kind) => logs.Add(kind + ": " + message);
        Application.logMessageReceived += Capture;
        try { Put(key + ".result", resize(texture).ToString()); }
        catch (Exception error) { Put(key + ".result", error.GetType().Name + ": " + error.Message); }
        finally { Application.logMessageReceived -= Capture; }
        Put(key + ".logs", string.Join("\n", logs));
        Size(key + ".after", texture);
        Put(key + ".width", texture.width);
        Put(key + ".height", texture.height);
        Put(key + ".format", texture.format);
        Put(key + ".mips", texture.mipmapCount);
        Put(key + ".readable", texture.isReadable);
        UnityEngine.Object.DestroyImmediate(texture);
    }

    static void ReplaceUnreadableImage()
    {
        var source = new Texture2D(8, 12);
        byte[] png = source.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(source);
        var destination = new Texture2D(16, 24);
        destination.Apply(false, true);
        Put("loadImage.unreadable.result", destination.LoadImage(png, false));
        Put("loadImage.unreadable.width", destination.width);
        Put("loadImage.unreadable.height", destination.height);
        Put("loadImage.unreadable.readable", destination.isReadable);
        UnityEngine.Object.DestroyImmediate(destination);
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
        Compress("compress.rgba", TextureFormat.RGBA32);
        Compress("compress.dxt1", TextureFormat.DXT1);
        ResizeUnreadable("unreadable.textureFormat", t => t.Reinitialize(t.width, t.height, t.format, false));
        ResizeUnreadable("unreadable.graphicsFormat", t => t.Reinitialize(t.width, t.height, t.graphicsFormat, false));
        RejectUnreadable("rejected.default", t => t.Reinitialize(32, 12));
        RejectUnreadable("rejected.textureFormat", t => t.Reinitialize(32, 16, TextureFormat.R8, true));
        RejectUnreadable("rejected.graphicsFormat", t => t.Reinitialize(32, 16, GraphicsFormat.R8_UNorm, true));
        RejectUnreadable("rejected.invalidTextureFormat", t => t.Reinitialize(0, 16, TextureFormat.R8, true));
        RejectUnreadable("rejected.invalidGraphicsFormat", t => t.Reinitialize(0, 16, GraphicsFormat.R8_UNorm, true));
        ReplaceUnreadableImage();
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
