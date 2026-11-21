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

public class BindingControlsProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("BindingControlsProbe");
        var doc = go.AddComponent<UIDocument>();
        doc.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        go.AddComponent<BindingControlsProbe>();
    }

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("BINDING_CONTROLS_OUT");
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
            System.Threading.Thread.Sleep(35);
        }
        update.Invoke(null, null);
        repaint.Invoke(null, new object[] { false });
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
    static string V4(Vector4 v) => $"({F(v.x)},{F(v.y)},{F(v.z)},{F(v.w)})";

#if UNITY_EDITOR
    void Run()
    {
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 640f;
        m_Root.style.height = 900f;
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };

        var a = ScriptableObject.CreateInstance<BindingControlsTarget>();
        var b = ScriptableObject.CreateInstance<BindingControlsTarget>();
        b.pos4 = new Vector4(2f, 10f, 200f, 300f);
        var so = new SerializedObject(a);
        var multi = new SerializedObject(new UnityEngine.Object[] { a, b });

        // 1. Enum names, display names and the value's index, for enums whose declaration order
        // is not value order, with aliases, an unsigned value with the high bit and a negative one.
        foreach (var path in new[] { "nonAscending", "alias", "highUnsigned", "signed", "shapeName" })
        {
            var p = so.FindProperty(path);
            Put("enum." + path, $"names=[{string.Join(",", p.enumNames)}]|display=[{string.Join(",", p.enumDisplayNames)}]|index={p.enumValueIndex}|int={p.intValue}");
        }

        // 2. Nicified names with digit-x-digit and digit-letter parts.
        var names = new[] { "matrix4x4", "grid2x3", "size4x", "m_Value4x4Mat", "x2y", "k3D", "_3dModel", "vector3Int", "uv2", "rgba32Bit", "a1x", "box2x2x2" };
        Put("nicify", string.Join("|", names.Select(n => n + "=" + ObjectNames.NicifyVariableName(n))));

        // 3. Multi-object Vector4 edits through the sub-fields: A=(1,10,100,100), B=(2,10,200,300).
        var v4 = new Vector4Field();
        m_Root.Add(v4);
        v4.BindProperty(multi.FindProperty("pos4"));
        Frames();
        var x = v4.Q<FloatField>("unity-x-input");
        var z = v4.Q<FloatField>("unity-z-input");
        var w = v4.Q<FloatField>("unity-w-input");
        var steps = new List<string>();
        z.value = 7f;
        Frames();
        steps.Add($"z7:a={V4(a.pos4)}|b={V4(b.pos4)}");
        x.value = 6f;
        Frames();
        steps.Add($"x6:a={V4(a.pos4)}|b={V4(b.pos4)}");
        w.value = 7f;
        Frames();
        steps.Add($"w7:a={V4(a.pos4)}|b={V4(b.pos4)}|field={V4(v4.value)}");
        Put("vec4.multi", string.Join("###", steps));

        // 4. Usage attributes of a collection on its elements.
        var tint1 = new PropertyField(so.FindProperty("tints.Array.data[1]"));
        var ramp0 = new PropertyField(so.FindProperty("ramps.Array.data[0]"));
        m_Root.Add(tint1);
        m_Root.Add(ramp0);
        tint1.Bind(so);
        ramp0.Bind(so);
        Frames();
        var color = tint1.Q<ColorField>();
        var gradient = ramp0.Q<GradientField>();
        Put("usage.elements", $"color={(color == null ? "null" : $"alpha={B(color.showAlpha)}|hdr={B(color.hdr)}")}"
            + $"|gradient={(gradient == null ? "null" : $"hdr={B(gradient.hdr)}|space={gradient.colorSpace}")}");

        // 5. When a PropertyField reports its field's changes: a usage drawer's field and a plain
        // one, two writes in one frame each.
        var events = new List<string>();
        foreach (var path in new[] { "tint", "plainColor" })
        {
            var pf = new PropertyField(so.FindProperty(path));
            m_Root.Add(pf);
            pf.Bind(so);
            Frames();
            int count = 0;
            pf.RegisterCallback<SerializedPropertyChangeEvent>(_ => count++);
            var field = pf.Q<ColorField>();
            field.value = Color.green;
            field.value = Color.yellow;
            int immediate = count;
            Frames(1);
            int oneFrame = count;
            Frames();
            events.Add($"{path}:now={immediate}|frame={oneFrame}|later={count}");
        }
        Put("usage.events", string.Join("###", events));

        so.Dispose();
        multi.Dispose();
    }
#else
    void Run() { }
#endif
}
