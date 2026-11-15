using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class EditorTuplesProbe : MonoBehaviour
{
    public static Action<int> finished;
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static StreamWriter writer;
    [Serializable] class Row { public string key; public string value; }
    static void Value(string key, object value) => writer.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : value.ToString() }));
    static void Check(string key, Func<object> action)
    {
        try { Value(key, action()); }
        catch (Exception e) { while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            if (e is ArgumentNullException nullError) Value(key, "ArgumentNullException:" + nullError.ParamName);
            else if (e is ArgumentException argument && argument.ParamName != null)
            {
                string message = argument.Message.Replace("\r", "");
                int end = message.IndexOf("\nParameter name:", StringComparison.Ordinal);
                if (end < 0) end = message.IndexOf(" (Parameter '", StringComparison.Ordinal);
                if (end >= 0) message = message.Substring(0, end);
                Value(key, "ArgumentException:" + message + ":" + argument.ParamName);
            }
            else if (e is NullReferenceException) Value(key, "NullReferenceException");
            else Value(key, e.GetType().Name + ":" + e.Message.Replace("\r", "")); }
    }
    static Type TypeOf(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        string output = Environment.GetEnvironmentVariable("EDITORTUPLES_PROBE_OUT");
        if (output == null) return;
        Directory.CreateDirectory(output);
        writer = new StreamWriter(Path.Combine(output, "values.jsonl")) { AutoFlush = true };
        new GameObject("EditorTuplesProbe").AddComponent<EditorTuplesProbe>();
    }
    IEnumerator Start()
    {
        yield return null;
        int code = 0;
        try { TupleControls(); NestedTuples(); BoundaryControls(); }
        catch (Exception e) { Value("fatal", e.GetType().Name + ":" + e.Message); code = 1; }
        Value("complete", code == 0); writer.Dispose();
        if (finished != null) finished(code); else EditorApplication.Exit(code);
    }
    static void TupleControls()
    {
        Type factory = TypeOf("UnityEditor.Tuple");
        for (int length = 1; length <= 8; length++)
        {
            int n = length;
            Check("tuple." + n + ".values", () => {
                MethodInfo create = factory.GetMethods(All).Single(m => m.Name == "Create" && m.GetGenericArguments().Length == n);
                Type[] types = Enumerable.Repeat(typeof(int), n).ToArray();
                object[] values = Enumerable.Range(1, n).Select(i => (object)i).ToArray();
                object a = create.MakeGenericMethod(types).Invoke(null, values);
                object b = create.MakeGenericMethod(types).Invoke(null, values);
                values[n-1] = 100;
                object c = create.MakeGenericMethod(types).Invoke(null, values);
                Value("tuple." + n + ".hash", a.GetHashCode());
                Type structural = a.GetType().GetInterfaces().Single(t => t.FullName == "UnityEditor.IStructuralEquatable");
                Type comparable = a.GetType().GetInterfaces().Single(t => t.FullName == "UnityEditor.IStructuralComparable");
                Type tuple = a.GetType().GetInterfaces().Single(t => t.FullName == "UnityEditor.ITuple");
                var comparer = new AllEqualComparer();
                Value("tuple." + n + ".customEquality", structural.GetMethod("Equals").Invoke(a, new object[] { c, comparer }));
                Value("tuple." + n + ".customHash", structural.GetMethod("GetHashCode").Invoke(a, new object[] { comparer }));
                Value("tuple." + n + ".customCompare", comparable.GetMethod("CompareTo").Invoke(a, new object[] { c, comparer }));
                Value("tuple." + n + ".length", tuple.GetProperty("Length").GetValue(a));
                Check("tuple." + n + ".wrongType", () => ((IComparable)a).CompareTo("wrong"));
                return a + ":" + a.Equals(b) + ":" + a.Equals(c) + ":" + (a.GetHashCode() == b.GetHashCode()) + ":" + ((IComparable)a).CompareTo(c);
            });
        }
    }
    sealed class AllEqualComparer : System.Collections.IEqualityComparer, System.Collections.IComparer
    {
        public new bool Equals(object a, object b) => true;
        public int GetHashCode(object a) => 7;
        public int Compare(object a, object b) => 0;
    }
    static void NestedTuples()
    {
        Type factory = TypeOf("UnityEditor.Tuple");
        Type open = TypeOf("UnityEditor.Tuple`8");
        foreach (int count in new[] { 2, 3, 8 })
        {
            int length = count;
            Check("tuple.rest." + length, () => {
                MethodInfo create = factory.GetMethods(All).Single(m => m.Name == "Create" && m.GetGenericArguments().Length == length);
                object rest = create.MakeGenericMethod(Enumerable.Repeat(typeof(int), length).ToArray()).Invoke(null, Enumerable.Range(8, length).Select(i => (object)i).ToArray());
                Type type = open.MakeGenericType(Enumerable.Repeat(typeof(int), 7).Concat(new[] { rest.GetType() }).ToArray());
                object value = Activator.CreateInstance(type, Enumerable.Range(1, 7).Select(i => (object)i).Concat(new[] { rest }).ToArray());
                Type tuple = type.GetInterfaces().Single(t => t.FullName == "UnityEditor.ITuple");
                return value + ":" + value.GetHashCode() + ":" + tuple.GetProperty("Length").GetValue(value) + ":" + tuple.GetProperty("Item").GetValue(value, new object[] { length + 6 });
            });
        }
        Check("tuple.rest.invalid", () => Activator.CreateInstance(open.MakeGenericType(Enumerable.Repeat(typeof(int), 7).Concat(new[] { typeof(string) }).ToArray()), new object[] { 1, 2, 3, 4, 5, 6, 7, "invalid" }));
    }
    static void BoundaryControls()
    {
        Type factory = TypeOf("UnityEditor.Tuple");
        object tuple = factory.GetMethods(All).Single(m => m.Name == "Create" && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(typeof(string), typeof(int)).Invoke(null, new object[] { null, -42 });
        Type access = TypeOf("UnityEditor.ITuple");
        Check("tuple.boundary.nullItem", () => tuple.ToString());
        Check("tuple.boundary.nullCompare", () => ((IComparable)tuple).CompareTo(null));
        Check("tuple.boundary.wrongEquality", () => tuple.Equals(System.Tuple.Create<string, int>(null, -42)));
        Check("tuple.boundary.nullEquality", () => tuple.Equals(null));
        foreach (int index in new[] { -1, 0, 1, 2 })
        {
            int i = index;
            Check("tuple.boundary.index." + i, () => access.GetProperty("Item").GetValue(tuple, new object[] { i }));
        }
        Type open = TypeOf("UnityEditor.Tuple`8");
        Check("tuple.rest.null", () => Activator.CreateInstance(open.MakeGenericType(Enumerable.Repeat(typeof(int), 7).Concat(new[] { tuple.GetType() }).ToArray()), new object[] { 1, 2, 3, 4, 5, 6, 7, null }));
        object rest = tuple;
        for (int level = 0; level < 4; level++)
        {
            Type type = open.MakeGenericType(Enumerable.Repeat(typeof(int), 7).Concat(new[] { rest.GetType() }).ToArray());
            rest = Activator.CreateInstance(type, new object[] { 1, 2, 3, 4, 5, 6, 7, rest });
        }
        object deep = rest;
        Check("tuple.rest.deep", () => deep + ":" + access.GetProperty("Length").GetValue(deep) + ":" + access.GetProperty("Item").GetValue(deep, new object[] { 29 }));
        Check("tuple.rest.deepHash", () => deep.GetHashCode());
    }
}
