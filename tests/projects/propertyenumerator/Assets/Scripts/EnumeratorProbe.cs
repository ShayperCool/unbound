using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Unity.Properties;
using UnityEditor;
using UnityEngine;

public class EnumeratorProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("EnumeratorProbe").AddComponent<EnumeratorProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    sealed class Container { }
    sealed class Named<T> : Property<T, int>
    {
        readonly string name;
        public Named(string name) { this.name = name; }
        public override string Name => name;
        public override bool IsReadOnly => false;
        public override int GetValue(ref T container) => 0;
        public override void SetValue(ref T container, int value) { }
    }
    sealed class Observable : IEnumerable<IProperty<Container>>, IEnumerator<IProperty<Container>>
    {
        public int reads, resets, disposals, position = -1;
        public bool throwCurrent;
        readonly IProperty<Container>[] values = { new Named<Container>("A"), new Named<Container>("B") };
        readonly IProperty<Container> before = new Named<Container>("before"), end = new Named<Container>("end");
        public IProperty<Container> Current { get { reads++; if (throwCurrent) throw new InvalidOperationException("current"); return position < 0 ? before : position < values.Length ? values[position] : end; } }
        object IEnumerator.Current => Current;
        public bool MoveNext() { position++; return position < values.Length; }
        public void Reset() { resets++; position = -1; }
        public void Dispose() { disposals++; }
        public IEnumerator<IProperty<Container>> GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    void Read<T>(string prefix, PropertyCollection<T>.Enumerator value)
    {
        Put(prefix+".first", value.Current == null ? "null" : value.Current.Name);
        Put(prefix+".second", value.Current == null ? "null" : value.Current.Name);
        Put(prefix+".object", ReferenceEquals(value.Current, ((IEnumerator)value).Current));
    }
    static void Inject<T>(ref PropertyCollection<T>.Enumerator value)
    {
        object boxed = value;
        typeof(PropertyCollection<T>.Enumerator).GetProperty("Current").SetValue(boxed, new Named<T>("injected"));
        value = (PropertyCollection<T>.Enumerator)boxed;
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("ENUMERATOR_OUT"); if (string.IsNullOrEmpty(path)) return;
        try { using (output = new StreamWriter(path)) { Record(); Put("complete", 1); } EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); output?.Dispose(); EditorApplication.Exit(1); }
    }
    void Exercise<T>(string prefix, PropertyCollection<T>.Enumerator value)
    {
        Read(prefix+".before", value);
        Put(prefix+".move1", value.MoveNext()); Read(prefix+".one", value);
        var copy = value;
        Inject(ref value); Read(prefix+".injected", value); Read(prefix+".copy", copy);
        value.Reset(); Read(prefix+".reset", value);
        Put(prefix+".move2", value.MoveNext()); Read(prefix+".two", value);
        Put(prefix+".move3", value.MoveNext()); Read(prefix+".three", value);
        Put(prefix+".move4", value.MoveNext()); Read(prefix+".four", value);
        Inject(ref value); value.Dispose(); Read(prefix+".disposed", value);
    }
    void Record()
    {
        var setter = typeof(PropertyCollection<Container>.Enumerator).GetProperty("Current").GetSetMethod(true);
        Put("setter.exists", setter != null); Put("setter.private", setter != null && setter.IsPrivate);
        var underlying = new Observable();
        var iterator = new PropertyCollection<Container>((IEnumerable<IProperty<Container>>)underlying).GetEnumerator();
        Read("observable.before", iterator); Put("observable.reads.before", underlying.reads);
        Put("observable.move1", iterator.MoveNext()); Read("observable.one", iterator); Put("observable.reads.one", underlying.reads);
        underlying.position = 1; Read("observable.changed-source", iterator); Put("observable.reads.changed-source", underlying.reads);
        underlying.throwCurrent = true;
        try { Put("observable.throw.move", iterator.MoveNext()); } catch (Exception ex) { Put("observable.throw.type", ex.GetType().Name); }
        Read("observable.throw.retained", iterator); Put("observable.reads.throw", underlying.reads);
        underlying.throwCurrent = false;
        Put("observable.end.move", iterator.MoveNext()); Read("observable.end", iterator); Put("observable.reads.end", underlying.reads);
        Inject(ref iterator); iterator.Reset(); Read("observable.reset", iterator); Put("observable.resets", underlying.resets);
        Put("observable.restart.move", iterator.MoveNext()); Read("observable.restart", iterator); Put("observable.reads.restart", underlying.reads);
        iterator.Dispose(); Read("observable.dispose", iterator); Put("observable.disposals", underlying.disposals);
        Exercise("enumerable", new PropertyCollection<Container>((IEnumerable<IProperty<Container>>)new Observable()).GetEnumerator());
        Exercise("list", new PropertyCollection<Container>(new List<IProperty<Container>> { new Named<Container>("A"), new Named<Container>("B") }).GetEnumerator());
        var list = new List<int> { 3, 5 };
        Exercise("indexed", PropertyBag.GetPropertyBag<List<int>>().GetProperties(ref list).GetEnumerator());
        Exercise("none", default(PropertyCollection<Container>.Enumerator));
        Exercise("empty-list", new PropertyCollection<Container>(new List<IProperty<Container>>()).GetEnumerator());
    }
}
