using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, whether Unity's module assemblies are among the loaded assemblies by
/// name (AppDomain.GetAssemblies), and what a lookup through one finds: the way packages reach
/// internal Editor types by reflection.
/// </summary>
static class ModuleAssembliesProbe
{
    static readonly string[] s_Modules =
    {
        "UnityEngine.CoreModule", "UnityEngine.PhysicsModule", "UnityEngine.UIElementsModule", "UnityEngine.TextRenderingModule",
        "UnityEditor.CoreModule", "UnityEditor.UIElementsModule", "UnityEditor.GraphViewModule",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("MODULE_ASSEMBLIES_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value).Append("\"}\n");
        foreach (var name in s_Modules) Put("loaded." + name, Find(name) != null ? "true" : "false");
        var module = Find("UnityEditor.UIElementsModule");
        var backend = module?.GetType("UnityEditor.UIElements.DefaultWindowBackend");
        Put("type.DefaultWindowBackend", backend == null ? "null" : backend.FullName);
        var property = backend?.GetProperty("visualTree", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, typeof(object), new Type[0], null);
        Put("property.visualTree", property == null ? "null" : property.PropertyType.FullName);
        var physics = Find("UnityEngine.PhysicsModule")?.GetType("UnityEngine.Rigidbody");
        Put("type.Rigidbody", physics == null ? "null" : (physics == typeof(Rigidbody) ? "the Rigidbody type" : "another type"));
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static Assembly Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == name) return assembly;
        return null;
    }
}
