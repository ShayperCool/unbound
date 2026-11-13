using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes what AssetDatabase.IsMainAssetAtPathLoaded answers for an asset the scene references and
/// one nothing references, before and after loading and unloading them, and what the scene's
/// reference reads after its asset is unloaded, into values.jsonl in $ASSET_LOADED_OUT
/// (tests/value_probe.py).
/// </summary>
public class AssetLoadedProbe : MonoBehaviour
{
    const string Referenced = "Assets/Data/Referenced.asset";
    const string Unreferenced = "Assets/Data/Unreferenced.asset";
    static string s_Directory;
    static StreamWriter output;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        s_Directory = Environment.GetEnvironmentVariable("ASSET_LOADED_OUT");
        if (!string.IsNullOrEmpty(s_Directory)) new GameObject("probe").AddComponent<AssetLoadedProbe>();
    }

    static string Loaded(string path) => AssetDatabase.IsMainAssetAtPathLoaded(path).ToString();

    void Update()
    {
        if (Time.frameCount < 3) return;
        enabled = false;
        try
        {
            Directory.CreateDirectory(s_Directory);
            using (output = new StreamWriter(Path.Combine(s_Directory, "values.jsonl")))
            {
                var holder = FindAnyObjectByType<LoadedHolder>();
                Row("referenced.loaded", Loaded(Referenced));
                Row("unreferenced.loaded", Loaded(Unreferenced));
                Row("missing.loaded", Loaded("Assets/Data/Missing.asset"));
                var unreferenced = AssetDatabase.LoadAssetAtPath<LoadedData>(Unreferenced);
                Row("unreferenced.afterLoad", Loaded(Unreferenced));
                Resources.UnloadAsset(unreferenced);
                Row("unreferenced.afterUnload", Loaded(Unreferenced));
                Row("unreferenced.reference", unreferenced == null ? "null" : unreferenced.value.ToString());
                Resources.UnloadAsset(holder.data);
                Row("referenced.afterUnload", Loaded(Referenced));
                Row("referenced.sceneReference", holder.data == null ? "null" : holder.data.value.ToString());
                Row("referenced.afterAccess", Loaded(Referenced));
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
