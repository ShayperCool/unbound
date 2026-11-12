using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Writes, for each .probepair asset (its .meta's name table differs per asset), the file ids of the
/// objects its importer made and which object each reference of FileIdRefs resolves to, into
/// values.jsonl in $SCRIPTED_FILE_IDS_OUT (tests/value_probe.py).
/// </summary>
public static class FileIdProbe
{
    static readonly string[] Assets = { "Legacy", "LegacyOtherClass", "Named", "Empty", "NamedOtherClass", "LegacyBoth", "NamedBoth", "OtherName" };
    static readonly long[] Ids = { 2800000L, 11400000L, 1234567L, 7654321L, 3342436360307218768L, -927777440634643073L, 5L };
    static StreamWriter output;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + value + "\"}\n");

    static string Describe(Object o)
    {
        if (o == null) return "null";
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long id);
        return o.GetType().Name + " " + id;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("SCRIPTED_FILE_IDS_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            using (output = new StreamWriter(Path.Combine(dir, "values.jsonl")))
            {
                foreach (var asset in Assets)
                {
                    var assetPath = "Assets/Imports/" + asset + ".probepair";
                    Row(asset + "/main", Describe(AssetDatabase.LoadMainAssetAtPath(assetPath)));
                    Row(asset + "/data", Describe(AssetDatabase.LoadAssetAtPath<ProbePair>(assetPath)));
                }
                var refs = Resources.Load<FileIdRefs>("Refs");
                for (int a = 0, i = 0; a < Assets.Length; a++)
                    for (int k = 0; k < Ids.Length; k++, i++)
                    {
                        var key = Assets[a] + "/" + Ids[k];
                        Row(key + "/any", Describe(refs.any[i]));
                        Row(key + "/texture", Describe(refs.textures[i]));
                        Row(key + "/data", Describe(refs.data[i]));
                    }
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
