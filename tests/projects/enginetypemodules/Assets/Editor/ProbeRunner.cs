using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    // Authoring: the probe's input, every public type (nested ones too) of the engine's module
    // assemblies (UnityEngine.*Module, UnityEditor.*Module), each with the umbrella assembly that
    // names it (UnityEngine or UnityEditor), sorted.
    public static void List()
    {
        var lines = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .Where(n => (n.StartsWith("UnityEngine.") || n.StartsWith("UnityEditor.")) && n.EndsWith("Module"))
            .Distinct()
            .SelectMany(n => AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == n).GetExportedTypes()
                .Select(t => (n.StartsWith("UnityEngine.") ? "UnityEngine" : "UnityEditor") + "\t" + t.FullName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal);
        File.WriteAllText(Environment.GetEnvironmentVariable("ENGINE_TYPE_MODULES_LIST"), string.Join("\n", lines) + "\n");
        EditorApplication.Exit(0);
    }
}
