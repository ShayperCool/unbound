using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Looks up the assets of the bundles ProbeRunner.Create built (Assets/StreamingAssets) by many
/// spellings of their names: the full name as stored, lower and upper case, backslashes, a partial
/// path, the file name with and without its extension. For each bundle and query it records
/// Contains, LoadAsset (untyped, as BundleNameData and as Sprite), LoadAssetAsync and
/// LoadAssetWithSubAssets, and the bundle's GetAllAssetNames. Writes values.jsonl into
/// BUNDLE_NAMES_OUT and exits.
/// </summary>
public sealed class BundleNameProbe : MonoBehaviour
{
    static readonly (string Key, string File)[] Bundles =
    {
        ("sbp", "sbp/mixed"), ("legacy", "legacy/names"), ("nofile", "nofile/names"), ("noext", "noext/names"),
    };

    static readonly string[] Queries =
    {
        // Mixed-case names as the Scriptable Build Pipeline stores them.
        "Assets/Mixed/RoomsData.asset", "assets/mixed/roomsdata.asset", "ASSETS/MIXED/ROOMSDATA.ASSET",
        "Assets\\Mixed\\RoomsData.asset", "Mixed/RoomsData.asset", "RoomsData.asset", "roomsdata.asset", "RoomsData",
        "roomsdata", "Notes/MixedNotes.TXT", "notes/mixednotes.txt", "MixedNotes.TXT", "MixedNotes.txt", "MixedNotes",
        "mixednotes", "Sprites/Checker.png", "Checker",
        // Asset paths as BuildPipeline stores them (lower case).
        "assets/bundled/data.asset", "Assets/Bundled/Data.asset", "ASSETS/BUNDLED/DATA.ASSET", "Assets\\Bundled\\Data.asset",
        "bundled/data.asset", "/data.asset", "Data.asset", "data.asset", "Data", "data", "DATA", "assets/bundled/notes.txt",
        "Notes", "notes.txt", "assets/bundled/checker.png", "Checker.png", "checker.png", "checker",
        // A name beyond ASCII.
        "Notes/Ünïcode.TXT", "notes/ünïcode.txt", "assets/bundled/Ünïcode.txt", "assets/bundled/ünïcode.txt",
        "ASSETS/BUNDLED/ÜNÏCODE.TXT", "Ünïcode", "ünïcode",
    };

    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("BundleNameProbe").AddComponent<BundleNameProbe>();

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    void Rec(string key, string value) => m_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":\"").Append(Escape(value)).Append("\"}\n");

    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }

    static string Describe(UnityEngine.Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        try
        {
            foreach (var (key, file) in Bundles)
            {
                var bundle = AssetBundle.LoadFromFile(Path.Combine(Application.streamingAssetsPath, file));
                Rec(key + "/loaded", bundle != null ? "true" : "false");
                if (bundle == null) continue;
                Rec(key + "/names", string.Join("|", bundle.GetAllAssetNames()));
                foreach (var query in Queries)
                {
                    var k = key + "/" + query;
                    Rec(k + "/contains", Try(() => bundle.Contains(query) ? "true" : "false"));
                    Rec(k + "/load", Try(() => Describe(bundle.LoadAsset(query))));
                    Rec(k + "/data", Try(() => Describe(bundle.LoadAsset<BundleNameData>(query))));
                    Rec(k + "/sprite", Try(() => Describe(bundle.LoadAsset<Sprite>(query))));
                    Rec(k + "/async", Try(() => Describe(bundle.LoadAssetAsync(query).asset)));
                    Rec(k + "/sub", Try(() => string.Join(",", bundle.LoadAssetWithSubAssets(query).Select(Describe).OrderBy(s => s, StringComparer.Ordinal))));
                }
                bundle.Unload(true);
            }
            m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        var dir = Environment.GetEnvironmentVariable("BUNDLE_NAMES_OUT");
        if (string.IsNullOrEmpty(dir)) Debug.Log(m_Out.ToString());
        else { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString()); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
