using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SearchObjectField = UnityEditor.Search.ObjectField;

/// <summary>
/// UnityEditor.Search.ObjectField, the search picker's UI Toolkit object field, outside any panel:
/// what its constructors build (classes, label, input and its display and selector children),
/// its defaults (objectType, searchViewFlags, searchContext, value, singleLineHeight), what its
/// display shows for no value and for values of several types, how a GameObject value is read
/// for a component objectType, the change events it sends, and its UXML data. Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class SearchObjectFieldProbe
{
    const string Variable = "SEARCH_OBJECT_FIELD_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();

    static SearchObjectFieldProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) => s_Logs.Add(type + ": " + message.Split('\n')[0]);
            EditorApplication.delayCall += Run;
        };
    }

    public static void Record() => EditorApplication.EnterPlaymode();

    static string Classes(VisualElement e) => string.Join(" ", e.GetClasses());

    static string Tree(VisualElement e)
    {
        var parts = new List<string>();
        foreach (var child in e.Children())
            parts.Add(child.GetType().Name + "[" + Classes(child) + "]" + (child.childCount > 0 ? "{" + Tree(child) + "}" : ""));
        return string.Join(",", parts);
    }

    static string Display(SearchObjectField field)
    {
        var display = field.Q(className: "unity-object-field-display");
        if (display == null) return "no display";
        var label = display.Q<Label>();
        var image = display.Q<Image>();
        return "label=" + (label?.text ?? "null") + "|image=" + (image?.image != null ? image.image.name : "null");
    }

    static void Run()
    {
        try
        {
            var field = new SearchObjectField();
            Put("default.classes", Classes(field));
            Put("default.tree", Tree(field));
            Put("default.label", field.label ?? "null");
            Put("default.labelClasses", Classes(field.labelElement));
            Put("default.objectType", field.objectType?.FullName ?? "null");
            Put("default.searchViewFlags", field.searchViewFlags.ToString());
            Put("default.searchContext", field.searchContext == null ? "null" : "set");
            Put("default.value", field.value == null ? "null" : field.value.name);
            Put("default.display", Display(field));
            Put("default.focusable", field.focusable + "|" + field.labelElement.focusable + "|" + field.Q(className: "unity-object-field__input").focusable + "|" + field.Q(className: "unity-object-field__object").focusable);
            Put("singleLineHeight", SearchObjectField.singleLineHeight.ToString("R", CultureInfo.InvariantCulture));

            var labelled = new SearchObjectField("Pick");
            Put("labelled.label", labelled.label);
            Put("labelled.tree", Tree(labelled));

            // The display for no value of several object types, and for values.
            foreach (var type in new[] { typeof(Texture2D), typeof(GameObject), typeof(Transform), typeof(ScriptableObject), typeof(Material) })
            {
                field.objectType = type;
                Put("none." + type.Name, Display(field));
            }
            var go = new GameObject("Probe Object");
            var events = new List<string>();
            field.RegisterValueChangedCallback(e => events.Add((e.previousValue == null ? "null" : e.previousValue.GetType().Name) + "->" + (e.newValue == null ? "null" : e.newValue.GetType().Name)));
            field.objectType = typeof(GameObject);
            field.value = go;
            Put("go.value", field.value == null ? "null" : field.value.GetType().Name + ":" + field.value.name);
            Put("go.display", Display(field));
            field.objectType = typeof(Transform);
            Put("go.asTransform.display", Display(field));
            field.SetValueWithoutNotify(go);
            Put("set.go.forTransform", field.value == null ? "null" : field.value.GetType().Name);
            field.objectType = typeof(Rigidbody);
            field.SetValueWithoutNotify(go);
            Put("set.go.forMissingComponent", field.value == null ? "null" : field.value.GetType().Name);
            field.objectType = typeof(UnityEngine.Object);
            var texture = new Texture2D(2, 2) { name = "Probe Texture" };
            field.value = texture;
            Put("texture.display", Display(field));
            field.value = null;
            Put("null.display", Display(field));
            Put("events", string.Join(" ", events));
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(texture);

            field.searchViewFlags = (UnityEngine.Search.SearchViewFlags)0;
            Put("flags.cleared", field.searchViewFlags.ToString());

            // UXML data: a new one makes a field; a type attribute set (with its flag) is read.
            var data = new SearchObjectField.UxmlSerializedData();
            var made = data.CreateInstance();
            Put("uxml.created", made?.GetType().FullName ?? "null");
            var dataType = typeof(SearchObjectField.UxmlSerializedData);
            var typeField = dataType.GetField("type", BindingFlags.Instance | BindingFlags.NonPublic);
            var flagField = dataType.GetField("type_UxmlAttributeFlags", BindingFlags.Instance | BindingFlags.NonPublic);
            Put("uxml.fields", (typeField != null) + "|" + (flagField != null));
            if (typeField != null && flagField != null)
            {
                var target = (SearchObjectField)data.CreateInstance();
                data.Deserialize(target);
                Put("uxml.unset.objectType", target.objectType.FullName);
                typeField.SetValue(data, typeof(Texture2D).AssemblyQualifiedName);
                flagField.SetValue(data, Enum.ToObject(flagField.FieldType, 1));
                data.Deserialize(target);
                Put("uxml.set.objectType", target.objectType.FullName);
            }
        }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); }
        Put("logs", string.Join(" | ", s_Logs));
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
