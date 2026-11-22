using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// The Undo records SerializedObject.ApplyModifiedProperties makes: their group and name, what an
// undo and a redo restore, and how they join other records of the group.
public class SerializedUndoProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();
    readonly List<string> m_Events = new List<string>();
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() => new GameObject("SerializedUndoProbe").AddComponent<SerializedUndoProbe>();

    void Update()
    {
        if (m_Done) return;
        m_Done = true;
        var dir = Environment.GetEnvironmentVariable("SERIALIZED_UNDO_OUT");
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

#if UNITY_EDITOR
    int m_Base;

    // The group relative to the probe's first one, its name, and the undo events since the last call.
    string Group()
    {
        string events = string.Join(";", m_Events);
        m_Events.Clear();
        return $"group={Undo.GetCurrentGroup() - m_Base}|name={Undo.GetCurrentGroupName()}|events={events}";
    }

    static string Values(UndoTarget t) => $"count={t.count}|title={t.title}|numbers={string.Join(",", t.numbers)}|f={t.nested.f}";

    void Run()
    {
        Application.logMessageReceived += (msg, _, type) =>
        {
            if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception)
                m_Logs.Add($"{type}:{msg}");
        };
        Undo.undoRedoEvent += (in UndoRedoInfo info) => m_Events.Add((info.isRedo ? "redo:" : "undo:") + info.undoName);
        Undo.IncrementCurrentGroup();
        m_Base = Undo.GetCurrentGroup();

        // 1. One property applied, then undone and redone.
        var a = ScriptableObject.CreateInstance<UndoTarget>();
        a.name = "Alpha";
        var so = new SerializedObject(a);
        so.FindProperty("count").intValue = 10;
        Put("one.before", Group());
        bool applied = so.ApplyModifiedProperties();
        Put("one.applied", $"{applied}|{Values(a)}|{Group()}");
        Undo.FlushUndoRecordObjects();
        Put("one.flushed", Group());
        Undo.PerformUndo();
        Put("one.undone", $"{Values(a)}|{Group()}");
        Undo.PerformRedo();
        Put("one.redone", $"{Values(a)}|{Group()}");

        // 2. Nothing changed: an apply records nothing.
        Undo.IncrementCurrentGroup();
        so.Update();
        Put("none.applied", $"{so.ApplyModifiedProperties()}|{Group()}");

        // 3. Several properties in one apply, of several kinds.
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("title").stringValue = "u";
        so.FindProperty("numbers.Array.data[1]").intValue = 60;
        so.FindProperty("nested.f").floatValue = 1.5f;
        so.ApplyModifiedProperties();
        Put("many.applied", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("many.undone", $"{Values(a)}|{Group()}");

        // 4. An array's size and a nested element.
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("numbers.Array.size").intValue = 3;
        so.ApplyModifiedProperties();
        Put("size.applied", $"{Values(a)}|{Group()}");
        so.Update();
        so.FindProperty("nested.s").stringValue = "m";
        so.ApplyModifiedProperties();
        Put("size.second", $"{Values(a)}|s={a.nested.s}|{Group()}");
        Undo.PerformUndo();
        Put("size.undone", $"{Values(a)}|s={a.nested.s}|{Group()}");

        // An array element alone, then an array's size alone.
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("numbers.Array.data[0]").intValue = 50;
        so.ApplyModifiedProperties();
        Undo.FlushUndoRecordObjects();
        Put("element.flushed", $"{Values(a)}|{Group()}");
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("numbers.Array.size").intValue = 1;
        so.ApplyModifiedProperties();
        Undo.FlushUndoRecordObjects();
        Put("sizeOnly.flushed", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Undo.PerformUndo();
        Put("element.undone", $"{Values(a)}|{Group()}");

        // Names of a camel-case field and of an m_ field; an array growing alone.
        foreach (var path in new[] { "maxSpeed", "m_Level" })
        {
            Undo.IncrementCurrentGroup();
            so.Update();
            so.FindProperty(path).intValue = 9;
            so.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Put("name." + path, $"maxSpeed={a.maxSpeed}|level={a.level}|{Group()}");
        }
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("numbers.Array.size").intValue = 4;
        so.ApplyModifiedProperties();
        Undo.FlushUndoRecordObjects();
        Put("grow.flushed", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("grow.undone", $"{Values(a)}|{Group()}");

        // 5. Two applies in one group, then in two groups.
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("count").intValue = 20;
        so.ApplyModifiedProperties();
        so.FindProperty("count").intValue = 21;
        so.ApplyModifiedProperties();
        Put("twice.applied", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("twice.undone", $"{Values(a)}|{Group()}");
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("count").intValue = 30;
        so.ApplyModifiedProperties();
        Undo.IncrementCurrentGroup();
        so.FindProperty("count").intValue = 31;
        so.ApplyModifiedProperties();
        Put("split.applied", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("split.undone", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("split.undone2", $"{Values(a)}|{Group()}");

        // 6. Without Undo: nothing recorded, and an undo takes the earlier record back.
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("count").intValue = 40;
        so.ApplyModifiedProperties();
        Undo.IncrementCurrentGroup();
        so.FindProperty("title").stringValue = "w";
        so.ApplyModifiedPropertiesWithoutUndo();
        Put("without.applied", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("without.undone", $"{Values(a)}|{Group()}");

        // 7. A record of the object, then an apply, in one group; a named group.
        Undo.IncrementCurrentGroup();
        so.Update();
        Undo.RecordObject(a, "Record Alpha");
        a.title = "r";
        so.Update();
        so.FindProperty("count").intValue = 50;
        so.ApplyModifiedProperties();
        Put("mixed.applied", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("mixed.undone", $"{Values(a)}|{Group()}");
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Named Group");
        so.Update();
        so.FindProperty("count").intValue = 55;
        so.ApplyModifiedProperties();
        Put("named.applied", $"{Values(a)}|{Group()}");
        Undo.PerformUndo();
        Put("named.undone", $"{Values(a)}|{Group()}");

        // Two records of objects in one group, and a record's name before a flush.
        Undo.IncrementCurrentGroup();
        Undo.RecordObject(a, "First Record");
        a.count = 80;
        Put("records.beforeFlush", Group());
        var c = ScriptableObject.CreateInstance<UndoTarget>();
        c.name = "Gamma";
        Undo.RecordObject(c, "Second Record");
        c.count = 81;
        Undo.FlushUndoRecordObjects();
        Put("records.flushed", Group());
        Undo.PerformUndo();
        Put("records.undone", $"a={a.count}|c={c.count}|{Group()}");
        UnityEngine.Object.DestroyImmediate(c);

        // Structural edits of an array: delete the last element, delete the first, insert, move.
        void Structural(string key, Action<SerializedProperty> edit)
        {
            Undo.IncrementCurrentGroup();
            a.numbers = new List<int> { 5, 6, 7 };
            so.Update();
            edit(so.FindProperty("numbers"));
            so.ApplyModifiedProperties();
            Undo.FlushUndoRecordObjects();
            Put("structural." + key, $"{Values(a)}|{Group()}");
            Undo.PerformUndo();
            Put("structural." + key + ".undone", $"{Values(a)}|{Group()}");
        }
        Structural("deleteLast", p => p.DeleteArrayElementAtIndex(2));
        Structural("deleteFirst", p => p.DeleteArrayElementAtIndex(0));
        Structural("insert", p => p.InsertArrayElementAtIndex(1));
        Structural("move", p => p.MoveArrayElement(0, 2));
        Structural("boxed", p => p.GetArrayElementAtIndex(1).boxedValue = 60);
        a.numbers = new List<int> { 5, 6 };
        so.Update();

        // Which record names a group: records of objects, created objects, added components and
        // an apply, in both orders.
        var host = new GameObject("Host");
        void Named(string key, Action first, Action second)
        {
            Undo.IncrementCurrentGroup();
            first();
            second();
            Undo.FlushUndoRecordObjects();
            Put("priority." + key, Group());
        }
        void RecordA(string name) { Undo.RecordObject(a, name); a.count++; }
        void Create(string name) => Undo.RegisterCreatedObjectUndo(new GameObject(name), name);
        void AddComponent() => Undo.AddComponent<UndoBehaviour>(host);
        void ApplyA() { so.Update(); so.FindProperty("count").intValue += 1; so.ApplyModifiedProperties(); }
        Named("recordThenAdd", () => RecordA("R1"), AddComponent);
        Named("addThenRecord", AddComponent, () => RecordA("R2"));
        Named("createThenRecord", () => Create("C3"), () => RecordA("R3"));
        Named("recordThenCreate", () => RecordA("R4"), () => Create("C4"));
        Named("createThenApply", () => Create("C5"), ApplyA);
        Named("applyThenCreate", ApplyA, () => Create("C6"));
        Named("createThenAdd", () => Create("C7"), AddComponent);
        Named("addThenCreate", AddComponent, () => Create("C8"));
        Named("createThenCreate", () => Create("C9"), () => Create("C10"));

        // 8. Several targets.
        Undo.IncrementCurrentGroup();
        var b = ScriptableObject.CreateInstance<UndoTarget>();
        b.name = "Beta";
        b.count = 4;
        var multi = new SerializedObject(new UnityEngine.Object[] { a, b });
        multi.FindProperty("count").intValue = 60;
        multi.ApplyModifiedProperties();
        Put("multi.applied", $"a={a.count}|b={b.count}|{Group()}");
        Undo.PerformUndo();
        Put("multi.undone", $"a={a.count}|b={b.count}|{Group()}");

        // 9. A component of a GameObject.
        Undo.IncrementCurrentGroup();
        var go = new GameObject("Holder");
        var behaviour = go.AddComponent<UndoBehaviour>();
        var bso = new SerializedObject(behaviour);
        bso.FindProperty("offset.y").floatValue = 9f;
        bso.ApplyModifiedProperties();
        Put("component.applied", $"offset={behaviour.offset}|{Group()}");
        Undo.PerformUndo();
        Put("component.undone", $"offset={behaviour.offset}|{Group()}");
        Undo.PerformRedo();
        Put("component.redone", $"offset={behaviour.offset}|{Group()}");

        // 10. A SerializedObject read before an undo keeps its own values until updated.
        Undo.IncrementCurrentGroup();
        so.Update();
        so.FindProperty("count").intValue = 70;
        so.ApplyModifiedProperties();
        Undo.PerformUndo();
        Put("stale.afterUndo", $"so={so.FindProperty("count").intValue}|a={a.count}");
        so.Update();
        Put("stale.updated", $"so={so.FindProperty("count").intValue}");

        UnityEngine.Object.DestroyImmediate(go);
        UnityEngine.Object.DestroyImmediate(a);
        UnityEngine.Object.DestroyImmediate(b);
    }
#else
    void Run() { }
#endif
}
