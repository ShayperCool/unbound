using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

// Numeric fields bound to one property of two objects, with typed expressions: those with
// per-object variables give each object its own value.
public class MultiExprProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    VisualElement m_Root;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("MultiExprProbe");
        var doc = go.AddComponent<UIDocument>();
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1f;
        doc.panelSettings = settings;
        go.AddComponent<MultiExprProbe>();
    }

    IEnumerator Start()
    {
        yield return null;
        var dir = Environment.GetEnvironmentVariable("MULTI_EXPR_OUT");
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };
        m_Root = GetComponent<UIDocument>().rootVisualElement;
        m_Root.style.width = 640f;
#if UNITY_EDITOR
        var run = Run();
        while (true)
        {
            object current;
            try
            {
                if (!run.MoveNext()) break;
                current = run.Current;
            }
            catch (Exception e)
            {
                Put("error", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                break;
            }
            yield return current;
        }
#endif
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
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static string V(object v) => v is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : v?.ToString();

    static void Send(VisualElement target, EventBase evt)
    {
        using (evt)
        {
            evt.target = target;
            target.SendEvent(evt);
        }
    }

    static TextElement Input<T>(TextValueField<T> field) => field.Q<TextElement>(className: TextElement.ussClassName + "--inner-input-field-component") ?? field.Q(className: "unity-base-text-field__input").Q<TextElement>();

    static IEnumerator Settle() { yield return null; yield return null; }

#if UNITY_EDITOR
    ExprTarget m_A, m_B;

    string Values() => $"a={m_A.count},{V(m_A.ratio)},{m_A.big},{V(m_A.precise)},{m_A.unsigned}|b={m_B.count},{V(m_B.ratio)},{m_B.big},{V(m_B.precise)},{m_B.unsigned}";

    void Reset()
    {
        m_A.count = 3; m_A.ratio = 1.5f; m_A.big = 100; m_A.precise = 0.25; m_A.unsigned = 4;
        m_B.count = 7; m_B.ratio = 2.5f; m_B.big = 200; m_B.precise = 0.75; m_B.unsigned = 9;
    }

    // The field's text set as typed, then committed as Return does (the field is not focused: a
    // focused field of several objects commits the shown value when it loses focus).
    static readonly System.Reflection.MethodInfo s_UpdateText =
        typeof(ITextEdition).GetMethod("UpdateText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

    static void Commit<T>(TextValueField<T> field, string typed)
    {
        s_UpdateText.Invoke(field.textEdition, new object[] { typed });
        var commit = field.GetType().GetMethod("UpdateValueFromText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        commit.Invoke(field, null);
    }

    IEnumerator Type<T>(string key, TextValueField<T> field, string typed)
    {
        Reset();
        // The bindings read the objects again (they poll at most every 100 ms).
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Settle();
        Put(key + ".before", $"{Values()}|text={field.text}|mixed={field.showMixedValue}");
        Commit(field, typed);
        yield return Settle();
        Undo.FlushUndoRecordObjects();
        Put(key, $"{Values()}|text={field.text}|mixed={field.showMixedValue}|undo={Undo.GetCurrentGroupName()}");
        Undo.IncrementCurrentGroup();
    }

    IEnumerator Run()
    {
        m_A = ScriptableObject.CreateInstance<ExprTarget>();
        m_A.name = "A";
        m_B = ScriptableObject.CreateInstance<ExprTarget>();
        m_B.name = "B";
        Reset();
        var so = new SerializedObject(new UnityEngine.Object[] { m_A, m_B });
        var count = new IntegerField("Count");
        var ratio = new FloatField("Ratio");
        var big = new LongField("Big");
        var precise = new DoubleField("Precise");
        var unsigned = new UnsignedIntegerField("Unsigned");
        foreach (var (field, path) in new (VisualElement, string)[] { (count, "count"), (ratio, "ratio"), (big, "big"), (precise, "precise"), (unsigned, "unsigned") })
        {
            ((IBindable)field).bindingPath = path;
            m_Root.Add(field);
        }
        m_Root.Bind(so);
        yield return new WaitForSecondsRealtime(0.3f);
        yield return Settle();
        Put("bound", $"{Values()}|count={count.text}|{count.showMixedValue}|ratio={ratio.text}|{ratio.showMixedValue}");

        foreach (var typed in new[] { "L(0,10)", "+=5", "*=2", "R(5,5)", "2+3", "L(1,1)" })
        {
            yield return Type("count." + typed, count, typed);
            yield return Type("ratio." + typed, ratio, typed);
        }
        foreach (var typed in new[] { "L(0,10)", "+=5" })
        {
            yield return Type("big." + typed, big, typed);
            yield return Type("precise." + typed, precise, typed);
            yield return Type("unsigned." + typed, unsigned, typed);
        }
        // Three objects: L() spreads over all of them.
        var c = ScriptableObject.CreateInstance<ExprTarget>();
        c.name = "C";
        var three = new SerializedObject(new UnityEngine.Object[] { m_A, m_B, c });
        var threeField = new IntegerField("Three") { bindingPath = "count" };
        var holder = new VisualElement();
        holder.Add(threeField);
        m_Root.Add(holder);
        holder.Bind(three);
        yield return Settle();
        c.count = 1;
        yield return Type("three.L(0,10)", threeField, "L(0,10)");
        Put("three.c", $"c={c.count}");
    }
#endif
}
