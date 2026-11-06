using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Writes the release's native class table, one JSON line per class sorted by class id:
/// id, name, base class, namespace, module and the abstract, sealed and editor-only flags. The
/// table is UnityEditor's own list of classes (the class ids of the Manual's YAML Class ID
/// Reference), read through reflection because it is not public API.</summary>
public static class ClassTable
{
    public static void Record()
    {
        var output = Environment.GetEnvironmentVariable("CLASS_TABLE_OUT");
        int code = 1;
        try
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.UnityType", true);
            var all = (IEnumerable)type.GetMethod("GetTypes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            string Str(object o, string name) => type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o) as string ?? "";
            object Get(object o, string name) => type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
            var lines = all.Cast<object>()
                .Select(t => (Id: Convert.ToInt32(Get(t, "persistentTypeID")), Type: t))
                .OrderBy(t => t.Id)
                .Select(t =>
                {
                    var baseClass = Get(t.Type, "baseClass");
                    return "{\"id\":" + t.Id
                        + ",\"name\":\"" + Str(t.Type, "name") + "\""
                        + ",\"base\":\"" + (baseClass != null ? Str(baseClass, "name") : "") + "\""
                        + ",\"namespace\":\"" + Str(t.Type, "nativeNamespace") + "\""
                        + ",\"module\":\"" + Str(t.Type, "module") + "\""
                        + ",\"abstract\":" + Flag(Get(t.Type, "isAbstract"))
                        + ",\"sealed\":" + Flag(Get(t.Type, "isSealed"))
                        + ",\"editorOnly\":" + Flag(Get(t.Type, "isEditorOnly"))
                        + "}";
                })
                .ToList();
            lines.Add("{\"k\":\"complete\",\"v\":true}");
            File.WriteAllText(output, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            code = 0;
        }
        catch (Exception e)
        {
            Debug.LogError("ClassTable: " + e);
        }
        EditorApplication.Exit(code);
    }

    static string Flag(object value) => value is bool b && b ? "true" : "false";
}
