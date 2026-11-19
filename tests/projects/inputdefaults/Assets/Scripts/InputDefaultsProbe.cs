using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Writes values.jsonl in $INPUT_DEFAULTS_OUT (tests/value_probe.py rows) in a project without
/// ProjectSettings/InputManager.asset: whether the file exists in Play mode, the Input Manager's
/// axes (the Editor's settings object) and what Input answers for each name.
/// </summary>
public class InputDefaultsProbe : MonoBehaviour
{
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<object> action)
    {
        string result;
        try
        {
            var value = action();
            result = value == null ? "null" : value.ToString();
        }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INPUT_DEFAULTS_OUT"))) return;
        new GameObject("InputDefaultsProbe").AddComponent<InputDefaultsProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("INPUT_DEFAULTS_OUT");
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"));
        yield return null;
        Row("input.file", File.Exists("ProjectSettings/InputManager.asset") ? "exists" : "missing");
#if UNITY_EDITOR
        var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
        Row("input.assets", string.Join(",", asset.Select(a => a.GetType().Name)));
        if (asset.Length > 0)
        {
            var axes = new SerializedObject(asset[0]).FindProperty("m_Axes");
            Row("input.axes", axes.arraySize.ToString());
            for (int i = 0; i < axes.arraySize; i++)
            {
                var axis = axes.GetArrayElementAtIndex(i);
                var fields = new[] { "m_Name", "descriptiveName", "descriptiveNegativeName", "negativeButton", "positiveButton", "altNegativeButton", "altPositiveButton" }
                    .Select(f => f + "=" + axis.FindPropertyRelative(f).stringValue)
                    .Concat(new[] { "gravity", "dead", "sensitivity" }.Select(f => f + "=" + axis.FindPropertyRelative(f).floatValue.ToString("R")))
                    .Concat(new[] { "snap", "invert" }.Select(f => f + "=" + axis.FindPropertyRelative(f).boolValue))
                    .Concat(new[] { "type", "axis", "joyNum" }.Select(f => f + "=" + axis.FindPropertyRelative(f).intValue));
                Row("input.axis." + i, string.Join(";", fields));
            }
        }
#endif
        foreach (var name in new[] { "Horizontal", "Vertical", "Fire1", "Fire2", "Fire3", "Jump", "Mouse X", "Mouse Y", "Mouse ScrollWheel", "Submit", "Cancel" })
        {
            Try("input.axis_raw." + name, () => Input.GetAxisRaw(name));
            Try("input.button." + name, () => Input.GetButton(name));
        }
        Try("input.axis_raw.Missing", () => Input.GetAxisRaw("Probe Missing Axis"));
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Close();
#if UNITY_EDITOR
        EditorApplication.ExitPlaymode();
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
