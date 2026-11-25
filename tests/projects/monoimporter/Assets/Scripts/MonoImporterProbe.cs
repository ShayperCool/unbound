using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what MonoImporter.GetExecutionOrder answers for scripts ordered by their
/// .meta file, by DefaultExecutionOrder, by both and by neither, for a behaviour's script and for
/// null, and the script each script's importer gives (MonoImporter.GetScript).
/// </summary>
static class MonoImporterProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("MONO_IMPORTER_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
        foreach (var name in new[] { "MetaOrdered", "AttributeOrdered", "BothOrdered", "Unordered" })
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/" + name + ".cs");
            Put("order." + name, Try(() => MonoImporter.GetExecutionOrder(script).ToString()));
            // The script its importer imports.
            Put("getScript." + name, Try(() =>
            {
                var imported = ((MonoImporter)AssetImporter.GetAtPath("Assets/Scripts/" + name + ".cs")).GetScript();
                return imported == null ? "null" : imported.name + (ReferenceEquals(imported, script) ? " (the loaded script)" : " (another object)");
            }));
        }
        Put("order.null", Try(() => MonoImporter.GetExecutionOrder(null).ToString()));
        var go = new GameObject("Probe");
        var behaviourScript = MonoScript.FromMonoBehaviour(go.AddComponent<MetaOrdered>());
        Put("order.fromBehaviour", Try(() => MonoImporter.GetExecutionOrder(behaviourScript).ToString()));
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string Try(Func<string> read)
    {
        try { return read(); }
        catch (Exception e) { return "throws " + e.GetType().Name; }
    }
}
