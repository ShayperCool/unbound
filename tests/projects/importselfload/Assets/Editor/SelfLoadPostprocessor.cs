using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What loading the asset being imported gives from its own import callbacks (and from
/// OnPostprocessAllAssets after them): written as lines into imports.txt in $IMPORTSELFLOAD_OUT,
/// which ImportSelfLoadProbe reads back into rows.
/// </summary>
public class SelfLoadPostprocessor : AssetPostprocessor
{
    internal static void Note(string line)
    {
        var dir = Environment.GetEnvironmentVariable("IMPORTSELFLOAD_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "imports.txt"), line + "\n");
    }

    /// <summary>How many lines noted so far start with <paramref name="prefix"/>.</summary>
    internal static int Count(string prefix)
    {
        var dir = Environment.GetEnvironmentVariable("IMPORTSELFLOAD_OUT");
        var file = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "imports.txt");
        return file != null && File.Exists(file) ? File.ReadAllLines(file).Count(l => l.StartsWith(prefix, StringComparison.Ordinal)) : 0;
    }

    /// <summary>What the three loads give, and how many errors they log.</summary>
    internal static string Loads(string assetPath)
    {
        int errors = 0;
        Application.LogCallback count = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception) errors++; };
        Application.logMessageReceived += count;
        try
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            return "texture " + (texture == null ? "null" : texture.name) + " main " + (main == null ? "null" : main.GetType().Name) + " all " + all.Length
                + " errors " + errors;
        }
        finally { Application.logMessageReceived -= count; }
    }

    static bool Probed(string path) => path.StartsWith("Assets/Textures/", StringComparison.Ordinal) || path.StartsWith("Assets/Data/", StringComparison.Ordinal);

    /// <summary>Assets in Assets/Quiet/ are noted without loading them.</summary>
    internal static bool Quiet(string path) => path.StartsWith("Assets/Quiet/", StringComparison.Ordinal);

    /// <summary>What the loads give, or "quiet" for an asset noted without loading it.</summary>
    internal static string Observe(string path) => Quiet(path) ? "quiet" : Loads(path);

    void OnPreprocessTexture() { if (Probed(assetPath) || Quiet(assetPath)) Note("pre " + assetPath + " | " + Observe(assetPath)); }
    void OnPostprocessTexture(Texture2D texture) { if (Probed(assetPath) || Quiet(assetPath)) Note("post " + assetPath + " | " + Observe(assetPath)); }
    void OnPostprocessSprites(Texture2D texture, Sprite[] sprites) { if (Probed(assetPath) || Quiet(assetPath)) Note("sprites " + assetPath + " | " + Observe(assetPath)); }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] from)
    {
        foreach (var path in imported.Where(p => Probed(p) || Quiet(p)).OrderBy(p => p, StringComparer.Ordinal))
            Note("all " + path + " | " + Observe(path));
    }
}
