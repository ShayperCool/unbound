using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, which engine component types a GameObject holds only one of: for every
/// public, concrete component class of the engine's modules (scripts aside), a new GameObject gets
/// it twice through AddComponent, and the row tells whether the second came back, with what the
/// second call logged (the GameObject's name replaced by NAME).
/// </summary>
static class UniqueComponentsProbe
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("UNIQUE_COMPONENTS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name.StartsWith("UnityEngine.", StringComparison.Ordinal))
            .SelectMany(a => { try { return a.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); } })
            .Where(t => t.IsPublic && !t.IsAbstract && !t.ContainsGenericParameters && typeof(Component).IsAssignableFrom(t)
                && !typeof(MonoBehaviour).IsAssignableFrom(t) && !typeof(Transform).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();
        var output = new StringBuilder();
        var logs = new List<string>();
        Application.LogCallback handler = (message, stack, type) => logs.Add(type + ":" + message.Replace("\n", "\\n"));
        foreach (var type in types)
        {
            var go = new GameObject("Probe" + type.Name);
            string value;
            try
            {
                var first = go.AddComponent(type);
                logs.Clear();
                Application.logMessageReceived += handler;
                Component second;
                try { second = go.AddComponent(type); }
                finally { Application.logMessageReceived -= handler; }
                value = first == null ? "first:null" : "second:" + (second == null ? "null" : "added") + "|log:" + string.Join("||", logs).Replace(go.name, "NAME");
            }
            catch (Exception e) { value = "throws " + e.GetType().Name; }
            UnityEngine.Object.DestroyImmediate(go);
            output.Append("{\"k\":\"").Append(type.FullName).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
        }
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }
}
