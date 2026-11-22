using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

public static class AstcMemory
{
    [Serializable]
    sealed class Row
    {
        public string k, v;
    }

    static readonly List<string> Rows = new List<string>();

    static void Put(string key, object value)
        => Rows.Add(JsonUtility.ToJson(new Row { k = key, v = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    static void Size(string key, Object instance) => Put(key, Profiler.GetRuntimeMemorySizeLong(instance));

    static Texture2D Create(string key, TextureFormat format, int width, int height, bool mips, bool linear)
    {
        var texture = new Texture2D(width, height, format, mips, linear);
        Put(key + ".format", (int)texture.format);
        Put(key + ".samples", SystemInfo.IsFormatSupported(texture.graphicsFormat, GraphicsFormatUsage.Sample));
        Size(key + ".new", texture);
        texture.Apply(false, false);
        Size(key + ".applied", texture);
        return texture;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        string output = Environment.GetEnvironmentVariable("ASTC_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        string profile = Environment.GetEnvironmentVariable("ASTC_MEMORY_PROFILE");
        Put("unity", Application.unityVersion);
        Put("device", SystemInfo.graphicsDeviceType);
        Put("profile", profile);
        if (profile == "CubeFirst")
        {
            var cube = new Cubemap(8, TextureFormat.ASTC_4x4, false);
            Size("prelude.cube.new", cube);
            cube.Apply(false, false);
            Size("prelude.cube.applied", cube);
            Object.DestroyImmediate(cube);
        }
        var format = profile == "Wide" ? TextureFormat.ASTC_8x8 : TextureFormat.ASTC_4x4;
        int width = profile == "Wide" ? 77 : 13;
        var first = Create("first", format, width, 17, true, true);
        var same = Create("same", format, width, 17, true, true);
        var color = Create("color", format, width, 17, true, false);
        var secondFormat = Create("secondFormat", TextureFormat.ASTC_6x6, 16, 24, false, true);
        Size("first.afterOthers", first);
        Put("first.reinitialize.result", first.Reinitialize(16, 24, TextureFormat.RGBA32, false));
        Size("first.reinitialize", first);
        first.Apply(false, false);
        Size("first.reinitialize.applied", first);
        Object.DestroyImmediate(first);
        var after = Create("after", format, width, 17, true, true);
        foreach (var item in new[] { same, color, secondFormat, after }) Object.DestroyImmediate(item);
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
