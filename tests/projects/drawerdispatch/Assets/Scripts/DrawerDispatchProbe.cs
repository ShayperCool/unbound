using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

public class DrawerDispatchProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("DrawerDispatchProbe");
        var doc = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        doc.panelSettings = settings;
        go.AddComponent<DrawerDispatchProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("DRAWER_DISPATCH_OUT");
        try
        {
            Run();
        }
        catch (Exception e)
        {
            Put("error", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
        m_Logs.Sort(StringComparer.Ordinal);
        m_Out.Append("{\"k\":\"logs\",\"v\":[").Append(string.Join(",", m_Logs.Select(Json))).Append("]}\n");
        m_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

    static Type RuntimeUtil => typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");

    static void Frames(int count = 4)
    {
        var update = RuntimeUtil.GetMethod("UpdatePanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var repaint = RuntimeUtil.GetMethod("RepaintPanels", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < count; i++)
        {
            update.Invoke(null, null);
            repaint.Invoke(null, new object[] { false });
            System.Threading.Thread.Sleep(20);
        }
    }

    void Put(string key, string value) => m_Out.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(value)).Append("}\n");

    static string Json(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string B(bool v) => v ? "1" : "0";
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static string Len(StyleLength l) => l.keyword == StyleKeyword.Undefined ? F(l.value.value) : l.keyword.ToString();

    // An element and its hierarchy children: type, name, classes, text of text elements and an
    // inline height when one is set.
    static string Tree(VisualElement e)
    {
        var sb = new StringBuilder();
        sb.Append(e.GetType().Name).Append('#').Append(e.name).Append('{').Append(string.Join(" ", e.GetClasses())).Append('}');
        if (e is TextElement t && !(e is TextField)) sb.Append("'").Append(t.text).Append("'");
        if (e.style.height.keyword != StyleKeyword.Null) sb.Append("[h=").Append(Len(e.style.height)).Append(']');
        if (e.hierarchy.childCount > 0)
        {
            sb.Append('(');
            for (int i = 0; i < e.hierarchy.childCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Tree(e.hierarchy[i]));
            }
            sb.Append(')');
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    SerializedObject m_So;

    PropertyField Field(string path, string label = null)
    {
        var property = m_So.FindProperty(path);
        var field = label == null ? new PropertyField(property) : new PropertyField(property, label);
        m_Root.Add(field);
        field.Bind(m_So);
        Frames();
        return field;
    }

    static int StaticInt(string type, string field) =>
        (int)AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(type)).First(t => t != null)
            .GetField(field, BindingFlags.Static | BindingFlags.Public).GetValue(null);

    void Run()
    {
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 640f;
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };
        var target = ScriptableObject.CreateInstance<DrawerDispatchTarget>();
        m_So = new SerializedObject(target);

        // 1. Lookup: by type, by base type without and with useForChildren, by interface, by
        // generic type definition, by attribute.
        foreach (var path in new[] { "shape", "derivedNo", "derivedYes", "marked", "wrapped", "tagged" })
            Put("lookup." + path, Tree(Field(path)));

        // 2. Two drawer attributes, each drawing the property again inside: the order.
        Put("nesting.ordered", Tree(Field("ordered")));

        // 3. A drawer with only IMGUI.
        Put("imgui", Tree(Field("imgui")));

        // 4. The label a drawer gets.
        Put("label.given", Tree(Field("shape", "Given")));
        Put("label.empty", Tree(Field("shape", "")));

        // 5. Collections: an attribute's drawer on each element, or on the list itself.
        Put("collection.array", Tree(Field("taggedArray")));
        Put("collection.element", Tree(Field("taggedArray.Array.data[1]")));
        Put("collection.whole", Tree(Field("wholeList")));
        Put("collection.wholeElement", Tree(Field("wholeList.Array.data[0]")));

        // 6. Decorators: built-in, custom with UI Toolkit, custom with IMGUI only, on an array.
        Put("decorator.builtin", Tree(Field("decorated")));
        Put("decorator.custom", Tree(Field("customDecorated")));
        Put("decorator.array", Tree(Field("headedArray")));
        Put("decorator.arrayElement", Tree(Field("headedArray.Array.data[0]")));

        // 7. The built-in drawers.
        var rangeFloat = Field("rangeFloat");
        var slider = rangeFloat.Q<Slider>();
        Put("builtin.rangeFloat", Tree(rangeFloat) + (slider == null ? "" : $"|low={F(slider.lowValue)}|high={F(slider.highValue)}|input={B(slider.showInputField)}|value={F(slider.value)}"));
        var rangeInt = Field("rangeInt");
        var sliderInt = rangeInt.Q<SliderInt>();
        Put("builtin.rangeInt", Tree(rangeInt) + (sliderInt == null ? "" : $"|low={sliderInt.lowValue}|high={sliderInt.highValue}|input={B(sliderInt.showInputField)}|value={sliderInt.value}"));
        Put("builtin.rangeString", Tree(Field("rangeString")));
        var minFloat = Field("minFloat");
        var minField = minFloat.Q<FloatField>();
        if (minField != null) minField.value = 1f;
        Frames();
        Put("builtin.min", Tree(minFloat) + $"|after1={F(target.minFloat)}");
        var delayed = Field("delayedString");
        Put("builtin.delayed", Tree(delayed) + $"|delayed={B(delayed.Q<TextField>()?.isDelayed ?? false)}");
        var multiline = Field("multiline");
        Put("builtin.multiline", Tree(multiline) + $"|multi={B(multiline.Q<TextField>()?.multiline ?? false)}");
        var textArea = Field("textArea");
        var areaField = textArea.Q<TextField>();
        Put("builtin.textArea", Tree(textArea) + (areaField == null ? "" : $"|multi={B(areaField.multiline)}|min={Len(areaField.style.minHeight)}|max={Len(areaField.style.maxHeight)}|dir={areaField.style.flexDirection.value}|ws={areaField.style.whiteSpace.value}"));

        // 8. Drawer instances: two fields of one property.
        int createdBefore = StaticInt("CountedDrawer", "created"), guisBefore = StaticInt("CountedDrawer", "guis");
        Field("counted");
        Field("counted");
        Put("instances", $"created={StaticInt("CountedDrawer", "created") - createdBefore}|guis={StaticInt("CountedDrawer", "guis") - guisBefore}");

        // 9. A drawer that builds its children's PropertyFields.
        Put("composite", Tree(Field("composite")));
        m_So.Dispose();
    }
#else
    void Run() { }
#endif
}
