using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>
/// Records, in Play mode, what the Editor's sprite atlas extensions answer for each sprite atlas of
/// the project (in asset path order): its packables (type, asset path and name of each, in order;
/// a packable whose asset is gone answers null), its master atlas and whether builds include it,
/// beside the atlas's variant flag and sprite count. Also the answers for a null atlas.
/// </summary>
static class SpriteAtlasEditorProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("SPRITE_ATLAS_EDITOR_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":").Append(Quote(key))
            .Append(",\"v\":").Append(Quote(value)).Append("}\n");
        void Check(string key, Func<string> read)
        {
            try { Put(key, read()); }
            catch (Exception e) { Put(key, "throws " + e.GetType().Name); }
        }

        var paths = AssetDatabase.FindAssets("t:SpriteAtlas").Select(AssetDatabase.GUIDToAssetPath)
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Put("atlases", string.Join("|", paths));
        foreach (var path in paths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            string key = Path.GetFileName(path);
            Put(key + ".loaded", (atlas != null).ToString());
            if (atlas == null) continue;
            Check(key + ".isVariant", () => atlas.isVariant.ToString());
            Check(key + ".spriteCount", () => atlas.spriteCount.ToString());
            Check(key + ".packables", () => string.Join("|", atlas.GetPackables().Select(Describe)));
            Check(key + ".master", () => Describe(atlas.GetMasterAtlas()));
            Check(key + ".includeInBuild", () => atlas.IsIncludeInBuild().ToString());
        }
        SpriteAtlas none = null;
        Check("null.packables", () => string.Join("|", none.GetPackables().Select(Describe)));
        Check("null.master", () => Describe(none.GetMasterAtlas()));
        Check("null.includeInBuild", () => none.IsIncludeInBuild().ToString());

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string Describe(UnityEngine.Object o) =>
        o == null ? "<null>" : o.GetType().Name + ":" + AssetDatabase.GetAssetPath(o) + ":" + o.name;

    static string Quote(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s ?? "<null>")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
