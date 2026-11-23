using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Jobs;

/// <summary>
/// TransformAccessArray's capacity and length: a capacity set below, at and above the length
/// (what the array then holds, what a transform job over it reaches), SetTransforms and Add after
/// a shrink, the capacity Add grows to, and invalid capacities. Recorded in Play mode.
/// </summary>
[InitializeOnLoad]
public static class TransformAccessProbe
{
    const string Variable = "TRANSFORM_ACCESS_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();

    static TransformAccessProbe()
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

    struct Mark : IJobParallelForTransform
    {
        public NativeArray<int> visited;
        public void Execute(int index, TransformAccess transform)
        {
            visited[index] = 1;
            transform.localPosition = new Vector3(index + 1, 0, 0);
        }
    }

    static Transform[] s_All;

    static void Run()
    {
        s_All = Enumerable.Range(0, 6).Select(i => new GameObject("T" + i).transform).ToArray();
        Step("shrink", a => { a.capacity = 3; }, 5);
        Step("shrinkToZero", a => { a.capacity = 0; }, 5);
        Step("equal", a => { a.capacity = 5; }, 5);
        Step("grow", a => { a.capacity = 9; }, 5);
        Step("negative", a => { a.capacity = -1; }, 5);
        Step("shrinkThenSet", a => { a.capacity = 3; a.SetTransforms(s_All.Take(3).ToArray()); }, 5);
        Step("shrinkThenSetMore", a => { a.capacity = 3; a.SetTransforms(s_All.Take(4).ToArray()); }, 5);
        Step("shrinkThenAdd", a => { a.capacity = 3; a.Add(s_All[5]); }, 5);
        Step("shrinkThenRemove", a => { a.capacity = 3; a.RemoveAtSwapBack(0); }, 5);
        Step("setFewer", a => { a.SetTransforms(s_All.Take(2).ToArray()); }, 5);
        // A shrink below the length after a growth: kept, or the length?
        Step("grownThenShrink", a => { a.capacity = 9; a.capacity = 3; }, 5);
        Step("grownThenLower", a => { a.capacity = 9; a.capacity = 7; }, 5);
        Step("grownThenToLength", a => { a.capacity = 9; a.capacity = 5; }, 5);
        Step("negativeThenAdd", a => { a.capacity = -1; a.Add(s_All[5]); }, 5);
        Step("negativeThenSet", a => { a.capacity = -1; a.SetTransforms(s_All.Take(2).ToArray()); }, 5);
        // An empty array: any capacity at or above its length of 0.
        Step("emptyCapacity", a => { a.capacity = 1; }, 0);
        Step("emptyThenSetMore", a => { a.capacity = 2; a.SetTransforms(s_All.Take(5).ToArray()); }, 0);
        Step("emptyThenAddPastCapacity", a => { a.capacity = 3; for (int i = 0; i < 4; i++) a.Add(s_All[i]); }, 0);
        // The capacity Add grows to, from an allocation of 2.
        var grown = new TransformAccessArray(2);
        var sizes = new List<string>();
        for (int i = 0; i < 6; i++)
        {
            grown.Add(s_All[i]);
            sizes.Add(grown.length + "/" + grown.capacity);
        }
        Put("add.growth", string.Join(" ", sizes));
        grown.Dispose();
        var empty = new TransformAccessArray(0);
        Put("zero.capacity", empty.capacity);
        empty.Add(s_All[0]);
        Put("zero.afterAdd", empty.length + "/" + empty.capacity);
        empty.Dispose();
        Put("logs", string.Join(" | ", s_Logs));
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    /// <summary>An array of the first <paramref name="count"/> transforms (capacity = count), the
    /// action, then its length, capacity, elements and what a transform job over it reaches.</summary>
    static void Step(string name, Action<TransformAccessArray> action, int count)
    {
        foreach (var t in s_All) t.localPosition = Vector3.zero;
        var array = new TransformAccessArray(s_All.Take(count).ToArray());
        Put(name + ".before", array.length + "/" + array.capacity);
        try
        {
            action(array);
            Put(name + ".threw", "no");
        }
        catch (Exception e) { Put(name + ".threw", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
        Put(name + ".actionLogs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
        Put(name + ".after", array.length + "/" + array.capacity);
        var elements = new List<string>();
        for (int i = 0; i < array.length; i++)
        {
            try { elements.Add(array[i] == null ? "null" : array[i].name); }
            catch (Exception e) { elements.Add(e.GetType().Name); }
        }
        Put(name + ".elements", string.Join(",", elements));
        try { var beyond = array[array.length]; Put(name + ".beyond", beyond == null ? "null" : beyond.name); }
        catch (Exception e) { Put(name + ".beyond", e.GetType().Name); }
        var visited = new NativeArray<int>(8, Allocator.TempJob);
        try
        {
            new Mark { visited = visited }.Schedule(array).Complete();
            Put(name + ".job", string.Join("", visited.Select(v => v.ToString())));
            Put(name + ".moved", string.Join(",", s_All.Select(t => t.localPosition.x.ToString(CultureInfo.InvariantCulture))));
        }
        catch (Exception e) { Put(name + ".job", e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
        visited.Dispose();
        array.Dispose();
        Put(name + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
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
