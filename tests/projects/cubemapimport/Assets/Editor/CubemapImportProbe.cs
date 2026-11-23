using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Cubemaps imported from images (textureShape Cube) by generateCubemap mode and image aspect:
/// what each source imports as (main asset type, size, format, mips) and a 4x4 grid of texels of
/// every face (as 0-255 integers). The sources (make_textures.py) hold their own texel position
/// in R and G, so a face texel tells where in the image it came from. Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class CubemapImportProbe
{
    const string Variable = "CUBEMAP_IMPORT_OUT";
    const int Grid = 4;
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();

    static CubemapImportProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.delayCall += Run;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static void Run()
    {
        try
        {
            var cases = File.ReadAllLines("Assets/Editor/CubemapCases.txt");
            foreach (var name in cases)
            {
                if (name.Length == 0) continue;
                try { Case(name); }
                catch (Exception e) { Put(name + ".threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); }
            }
        }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); }
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Case(string name)
    {
        var path = "Assets/Textures/" + name + ".png";
        var main = AssetDatabase.GetMainAssetTypeAtPath(path);
        Put(name + ".mainType", main == null ? "null" : main.Name);
        var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
        Put(name + ".loaded", texture == null ? "null" : texture.GetType().Name);
        if (!(texture is Cubemap cube)) return;
        Put(name + ".size", cube.width + "x" + cube.height);
        Put(name + ".format", cube.format.ToString());
        Put(name + ".mipmapCount", cube.mipmapCount);
        Put(name + ".isReadable", cube.isReadable);
        if (!cube.isReadable) return;
        int size = cube.width;
        for (int face = 0; face < 6; face++)
            for (int j = 0; j < Grid; j++)
                for (int i = 0; i < Grid; i++)
                {
                    int x = (2 * i + 1) * size / (2 * Grid), y = (2 * j + 1) * size / (2 * Grid);
                    var c = (Color32)cube.GetPixel((CubemapFace)face, x, y);
                    var key = $"{name}.face{face}.{x},{y}";
                    Put(key + ".r", (int)c.r);
                    Put(key + ".g", (int)c.g);
                    Put(key + ".ba", c.b + "," + c.a);
                }
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
