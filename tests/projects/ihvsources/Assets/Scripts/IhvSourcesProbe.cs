using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $IHVSOURCES_OUT (tests/value_probe.py rows): for each DDS, KTX and ASTC
/// source file in Assets/Sources (made by make_sources.py, imported without a .meta), the .meta the
/// Editor writes, its importer and settings, the texture it imports (type, size, format, mips,
/// shape, sampling settings), and, after the importer is made readable and the asset imported
/// again, the .meta and the texture's raw data (length and SHA-256). Values never contain instance
/// ids; GUID lines read "guid: (guid)".
/// </summary>
public class IhvSourcesProbe : MonoBehaviour
{
    const string Variable = "IHVSOURCES_OUT";

    static StreamWriter output;
    static readonly List<string> logs = new List<string>();
    static readonly Regex s_Guid = new Regex("(?m)^guid: [0-9a-f]{32}$");

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static string Meta(string path) => File.Exists(path + ".meta") ? s_Guid.Replace(File.ReadAllText(path + ".meta"), "guid: (guid)") : "<none>";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("IhvSourcesProbe").AddComponent<IhvSourcesProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    /// <summary>The importer's public instance properties of simple types, by name.</summary>
    static string Settings(AssetImporter importer)
    {
        if (importer == null) return "null";
        var parts = new List<string> { "type " + importer.GetType().FullName };
        foreach (var p in importer.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
            if (!(p.PropertyType.IsPrimitive || p.PropertyType.IsEnum || p.PropertyType == typeof(string))) continue;
            object value;
            try { value = p.GetValue(importer); }
            catch (Exception e) { value = "throws " + (e.InnerException ?? e).GetType().Name; }
            parts.Add(p.Name + " " + value);
        }
        return string.Join("; ", parts);
    }

    static string Describe(UnityEngine.Object main)
    {
        if (main == null) return "null";
        var parts = new List<string> { "type " + main.GetType().FullName, "name " + main.name };
        if (main is Texture texture)
        {
            parts.Add("size " + texture.width + "x" + texture.height);
            parts.Add("dimension " + texture.dimension);
            parts.Add("graphicsFormat " + texture.graphicsFormat);
            parts.Add("mipmapCount " + texture.mipmapCount);
            parts.Add("isReadable " + texture.isReadable);
            parts.Add("filterMode " + texture.filterMode + " wrap " + texture.wrapModeU + "," + texture.wrapModeV + "," + texture.wrapModeW);
            parts.Add("aniso " + texture.anisoLevel + " mipMapBias " + texture.mipMapBias);
        }
        if (main is Texture2D t2) parts.Add("format " + t2.format);
        if (main is Cubemap cube) parts.Add("format " + cube.format);
        return string.Join("; ", parts);
    }

    static string Raw(UnityEngine.Object main)
    {
        if (main is Texture2D t2)
        {
            var bytes = t2.GetRawTextureData();
            using var sha = System.Security.Cryptography.SHA256.Create();
            return "length " + bytes.Length + " sha256 " + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        return main == null ? "null" : "not a Texture2D";
    }

    static void Run()
    {
        var paths = Directory.GetFiles("Assets/Sources").Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
            .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Row("sources", string.Join(", ", paths.Select(Path.GetFileName)));
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            Try("meta/" + name, () => Meta(path));
            Try("importer/" + name, () => Settings(AssetImporter.GetAtPath(path)));
            Try("asset/" + name, () => Describe(AssetDatabase.LoadMainAssetAtPath(path)));
            Try("subassets/" + name, () => string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(path).Select(o => o.GetType().Name + " " + o.name)));
            Try("raw/" + name, () => Raw(AssetDatabase.LoadMainAssetAtPath(path)));
        }
        // Each made readable through its importer and imported again.
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            Try("readable/" + name, () =>
            {
                var importer = AssetImporter.GetAtPath(path);
                var property = importer?.GetType().GetProperty("isReadable");
                if (property == null) return "no isReadable";
                property.SetValue(importer, true);
                importer.SaveAndReimport();
                return "done";
            });
            Try("readable_meta/" + name, () => Meta(path));
            Try("readable_asset/" + name, () => Describe(AssetDatabase.LoadMainAssetAtPath(path)));
            Try("readable_raw/" + name, () => Raw(AssetDatabase.LoadMainAssetAtPath(path)));
        }
    }
}
