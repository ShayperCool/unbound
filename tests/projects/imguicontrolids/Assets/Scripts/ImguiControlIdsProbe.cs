using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in a game's OnGUI in Play mode, the IMGUI control ids: permanent ids (outside OnGUI,
/// between controls and after the run), the ids a fixed sequence of controls gets in the Layout and
/// Repaint passes of several frames, the ids when a control is appended, inserted before the
/// others or left out, and the ids a second component's OnGUI gets in the same frames. The raw
/// numbers depend on what else the Editor drew (its other views take ids from the same counter);
/// the canon/* keys give each id as "o&lt;n&gt;" (the n-th id first seen earlier in the run) or
/// "n&lt;d&gt;" (new, d after a permanent id taken at the start of the pass), and "order" lists the
/// OnGUI calls by component and event.
/// </summary>
public sealed class ImguiControlIdsProbe : MonoBehaviour
{
    internal static StringBuilder Output;
    internal static int Frame = -1;
    string m_Directory;
    bool m_Done;

    static readonly MethodInfo s_Permanent = typeof(GUIUtility).GetMethod("GetPermanentControlID", BindingFlags.Static | BindingFlags.NonPublic);
    internal static int Permanent() => (int)s_Permanent.Invoke(null, null);

    static readonly Dictionary<int, int> s_Seen = new Dictionary<int, int>();
    internal static readonly List<object> Order = new List<object>();

    /// <summary>The pass's ids in canonical form, from the permanent id taken at its start.</summary>
    internal static object[] Canon(int start, object[] ids)
    {
        var canon = new object[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            bool permanent = ids[i] is string;
            int id = permanent ? int.Parse(((string)ids[i]).Substring(1)) : (int)ids[i];
            string form = s_Seen.TryGetValue(id, out int n) ? "o" + n : "n" + (id - start);
            if (!s_Seen.ContainsKey(id)) s_Seen[id] = s_Seen.Count;
            canon[i] = permanent ? "p" + form : form;
        }
        return canon;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("IMGUI_CONTROL_IDS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Output = new StringBuilder();
        var go = new GameObject("ImguiControlIdsProbe");
        var probe = go.AddComponent<ImguiControlIdsProbe>();
        go.AddComponent<ImguiControlIdsSecond>();
        probe.m_Directory = directory;
        int a = Permanent(), b = Permanent();
        object outside;
        try { outside = GUIUtility.GetControlID(FocusType.Passive); }
        catch (Exception ex) { outside = "throws " + ex.GetType().Name + ": " + ex.Message; }
        Put("outside", a, b - a, outside);
    }

    internal static void Put(string key, params object[] values) =>
        Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    static int P() => GUIUtility.GetControlID(FocusType.Passive);

    // The frames' control sequences: 0, 1 the same; 2 one appended; 3 one inserted first;
    // 4 the keyboard control left out; 5 the first sequence again.
    static object[] Sequence(int frame)
    {
        var ids = new List<object>();
        if (frame == 3) ids.Add(GUIUtility.GetControlID(789, FocusType.Passive));
        ids.Add(P());
        if (frame != 4) ids.Add(GUIUtility.GetControlID(FocusType.Keyboard));
        ids.Add(GUIUtility.GetControlID(123, FocusType.Passive));
        ids.Add("p" + Permanent());
        ids.Add(P());
        if (frame >= 2 && frame <= 4) ids.Add(GUIUtility.GetControlID(456, FocusType.Passive));
        ids.Add(GUIUtility.GetControlID(123, FocusType.Keyboard));
        return ids.ToArray();
    }

    void OnGUI()
    {
        if (m_Done) return;
        var type = Event.current.type;
        if (type == EventType.Layout) Frame++;
        if (type != EventType.Layout && type != EventType.Repaint) return;
        if (Frame > 5)
        {
            m_Done = true;
            Put("after", Permanent());
            Put("order", Order.ToArray());
            Output.Append("{\"k\":\"complete\",\"v\":true}\n");
            Directory.CreateDirectory(m_Directory);
            File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), Output.ToString());
            EditorApplication.Exit(0);
            return;
        }
        string pass = $"f{Frame}/{(type == EventType.Layout ? "layout" : "repaint")}";
        Order.Add(pass + "/main");
        int start = Permanent();
        var ids = Sequence(Frame);
        Put(pass + "/main", ids);
        Put("canon/" + pass + "/main", Canon(start, ids));
    }

    internal static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        bool flag => flag ? "true" : "false",
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
