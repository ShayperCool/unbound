using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what MonoImporter.GetExecutionOrder answers for scripts ordered by their
/// .meta file, by DefaultExecutionOrder, by both and by neither, for a script of an engine class and
/// for null.
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
        }
        Put("order.null", Try(() => MonoImporter.GetExecutionOrder(null).ToString()));
        var go = new GameObject("Probe");
        var engineScript = MonoScript.FromMonoBehaviour(go.AddComponent<MetaOrdered>());
        Put("order.fromBehaviour", Try(() => MonoImporter.GetExecutionOrder(engineScript).ToString()));
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
