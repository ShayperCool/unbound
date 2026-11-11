using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Loads the probe document from Resources, clones it and records every named element's class and
/// probe properties, plus the warnings and errors of the import (written by the Editor runner) and
/// of the load and clone. Writes values.jsonl to $UXML_ATTRIBUTES_OUT.
/// </summary>
public static class UxmlAttributesProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var dir = Environment.GetEnvironmentVariable("UXML_ATTRIBUTES_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        var output = new StringBuilder();
        Record(dir, "UxmlAttributesProbe", "", "import-logs.txt", output);
        Record(dir, "UxmlIconsProbe", "icons/", "import-logs-icons.txt", output);
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    // One document: its named elements' classes and probe properties under `prefix`, then the
    // warnings and errors of its import, load and clone. The icons document skips the elements a
    // control builds for itself (named unity-*).
    static void Record(string dir, string resource, string prefix, string importLogs, StringBuilder output)
    {
        var logs = new List<string>();
        var imported = Path.Combine(dir, importLogs);
        if (File.Exists(imported)) logs.AddRange(File.ReadAllLines(imported).Where(l => l.Length > 0));
        Application.LogCallback capture = (message, stack, type) =>
        {
            if (type != LogType.Log) logs.Add(type + ": " + message);
        };
        Application.logMessageReceived += capture;
        var asset = Resources.Load<VisualTreeAsset>(resource);
        var root = new VisualElement();
        asset?.CloneTree(root);
        Application.logMessageReceived -= capture;

        void Put(string key, string value) => output.Append("{\"k\":").Append(Json(prefix + key)).Append(",\"v\":").Append(Json(value)).Append("}\n");
        Put("asset", asset == null ? "null" : asset.name);
        Put("rootChildren", root.childCount.ToString(Inv));
        foreach (var element in root.Query<VisualElement>().ToList())
        {
            if (string.IsNullOrEmpty(element.name) || element == root) continue;
            if (element.GetFirstAncestorOfType<BaseVerticalCollectionView>() != null) continue;
            if (prefix.Length > 0 && element.name.StartsWith("unity-", StringComparison.Ordinal)) continue;
            var key = "element/" + element.name + "/";
            Put(key + "class", element.GetType().FullName);
            if (element is ListView list)
            {
                Put(key + "itemTemplate", Format(list.itemTemplate));
                continue;
            }
            if (element is TreeView tree)
            {
                Put(key + "itemTemplate", Format(tree.itemTemplate));
                continue;
            }
            if (element is Button button)
            {
                var image = button.iconImage;
                Put(key + "iconImage", $"texture={Name(image.texture)} sprite={Name(image.sprite)} renderTexture={Name(image.renderTexture)} vectorImage={Name(image.vectorImage)}");
                continue;
            }
            if (element is Tab tab)
            {
                var image = tab.iconImage;
                Put(key + "iconImage", $"texture={Name(image.texture)} sprite={Name(image.sprite)} renderTexture={Name(image.renderTexture)} vectorImage={Name(image.vectorImage)}");
                continue;
            }
            if (element.GetType().Namespace != "UxmlAttributesProbes") continue;
            foreach (var property in element.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Concat(element.GetType().BaseType.Namespace == "UxmlAttributesProbes"
                    ? element.GetType().BaseType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    : Array.Empty<PropertyInfo>())
                .OrderBy(p => p.Name, StringComparer.Ordinal))
                Put(key + property.Name, Format(property.GetValue(element)));
        }
        logs.Sort(StringComparer.Ordinal);
        output.Append("{\"k\":").Append(Json(prefix + "logs")).Append(",\"v\":[").Append(string.Join(",", logs.Select(Json))).Append("]}\n");
    }

    static string Name(UnityEngine.Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    static string Format(object value)
    {
        switch (value)
        {
            case null: return "null";
            case UnityEngine.Object o: return o ? o.GetType().Name + ":" + o.name : "null";
            case float f: return f.ToString("R", Inv);
            case double d: return d.ToString("R", Inv);
            case Color c: return string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(x => x.ToString("R", Inv)));
            case Vector2 v: return v.x.ToString("R", Inv) + "," + v.y.ToString("R", Inv);
            case Vector3 v: return v.x.ToString("R", Inv) + "," + v.y.ToString("R", Inv) + "," + v.z.ToString("R", Inv);
            case Rect r: return string.Join(",", new[] { r.x, r.y, r.width, r.height }.Select(x => x.ToString("R", Inv)));
            case Type t: return t.FullName;
            case Background b: return $"texture={Name(b.texture)} sprite={Name(b.sprite)} renderTexture={Name(b.renderTexture)} vectorImage={Name(b.vectorImage)}";
            case string s: return "\"" + s + "\"";
            case System.Collections.IList list:
                return "[" + string.Join("|", list.Cast<object>().Select(Format)) + "]";
            case IFormattable f: return f.ToString(null, Inv);
            default: return value.ToString();
        }
    }

    static string Json(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 0x20 || c > 0x7e) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
