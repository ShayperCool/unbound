using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

public class LinkedPoolProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("LinkedPoolProbe").AddComponent<LinkedPoolProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    sealed class Item
    {
        public readonly int id;
        public Item(int id) { this.id = id; }
        public override bool Equals(object obj) => obj is Item;
        public override int GetHashCode() => 1;
    }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    static int Id(Item item) => item == null ? -1 : item.id;
    static void Set(LinkedPool<Item> pool, int value) => typeof(LinkedPool<Item>).GetProperty("CountInactive").SetValue(pool, value);
    void Attempt(string key, Action action)
    {
        try { action(); Put(key, "ok"); }
        catch (Exception ex) { Put(key, ex.GetType().Name); if (ex is ArgumentException arg) Put(key+".parameter", arg.ParamName ?? "null"); }
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("LINKED_POOL_OUT"); if (string.IsNullOrEmpty(path)) return;
        try { using (output = new StreamWriter(path)) { Record(); Put("complete", 1); } EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); output?.Dispose(); EditorApplication.Exit(1); }
    }
    void Record()
    {
        var setter = typeof(LinkedPool<Item>).GetProperty("CountInactive").GetSetMethod(true);
        Put("setter.exists", setter != null); Put("setter.private", setter != null && setter.IsPrivate);
        foreach (int seed in new[] { 0, -3, 2, 9, int.MinValue, int.MaxValue }) Diverge(seed);
        int created = 0;
        var events = new List<string>();
        LinkedPool<Item> pool = null;
        pool = new LinkedPool<Item>(() => new Item(++created),
            item => events.Add("get:"+Id(item)+":"+pool.CountInactive),
            item => events.Add("release:"+Id(item)+":"+pool.CountInactive),
            item => events.Add("destroy:"+Id(item)+":"+pool.CountInactive), true, 2);
        var a = pool.Get(); var b = pool.Get(); var c = pool.Get();
        pool.Release(a); pool.Release(b); pool.Release(c);
        Put("normal.full.count", pool.CountInactive); Put("normal.events", string.Join("|", events)); events.Clear();
        Put("normal.take1", Id(pool.Get())); Put("normal.take2", Id(pool.Get())); Put("normal.take3", Id(pool.Get()));
        Put("normal.taken.count", pool.CountInactive); Put("normal.taken.events", string.Join("|", events)); events.Clear();
        pool.Release(a); pool.Release(b);
        Attempt("normal.duplicate", () => pool.Release(a)); Put("normal.duplicate.count", pool.CountInactive);
        pool.Clear(); Put("normal.clear.count", pool.CountInactive); Put("normal.clear.events", string.Join("|", events)); events.Clear();
        pool.Release(c); ((IDisposable)pool).Dispose(); Put("normal.dispose.count", pool.CountInactive); Put("normal.dispose.events", string.Join("|", events));
        using (pool.Get(out var scoped)) Put("scope.item", Id(scoped));
        Put("scope.returned", pool.CountInactive);
        var uncheckedPool = new LinkedPool<Item>(() => new Item(17), collectionCheck: false);
        uncheckedPool.Release(a); uncheckedPool.Release(a); Put("unchecked.count", uncheckedPool.CountInactive);
        Put("unchecked.same", ReferenceEquals(uncheckedPool.Get(), uncheckedPool.Get()));
        var nullPool = new LinkedPool<Item>(() => null);
        Put("null.created", Id(nullPool.Get())); nullPool.Release(null); Put("null.released", nullPool.CountInactive);
        Attempt("null.duplicate", () => nullPool.Release(null)); Put("null.taken", Id(nullPool.Get())); Put("null.after", nullPool.CountInactive);
        var nullCreate = new LinkedPool<Item>(() => null);
        Put("null.factory1", Id(nullCreate.Get())); Put("null.factory2", Id(nullCreate.Get())); Put("null.factory.count", nullCreate.CountInactive);
        Attempt("ctor.null", () => new LinkedPool<Item>(null));
        Attempt("ctor.zero", () => new LinkedPool<Item>(() => a, maxSize: 0));
        Attempt("ctor.negative", () => new LinkedPool<Item>(() => a, maxSize: -1));
        Throws();
        bool reenter = true;
        var same = new Item(83);
        LinkedPool<Item> recursive = null;
        recursive = new LinkedPool<Item>(() => new Item(97), actionOnRelease: item => { if (reenter) { reenter = false; recursive.Release(item); } });
        recursive.Release(same); Put("reentrant.count", recursive.CountInactive);
        Put("reentrant.first", Id(recursive.Get()));
        Attempt("reentrant.stillDuplicate", () => recursive.Release(same));
        Put("reentrant.second", Id(recursive.Get())); Put("reentrant.empty", recursive.CountInactive);
        recursive.Release(same); Put("reentrant.return", recursive.CountInactive);

        var control = new ObjectPool<Item>(() => new Item(29), maxSize: 4);
        control.Release(a); control.Release(b); control.Release(c);
        Put("object.control.count", control.CountInactive); Put("object.control.first", Id(control.Get()));
        Put("object.control.second", Id(control.Get())); Put("object.control.third", Id(control.Get()));
        control.Release(null); Put("object.null.emptyCount", control.CountInactive);
        control.Release(a); control.Release(b); control.Release(null); Put("object.null.count", control.CountInactive);
        Put("object.null.fresh", Id(control.Get()));
        Attempt("object.null.duplicate", () => control.Release(null));
        Put("object.null.list", Id(control.Get())); Put("object.null.last", Id(control.Get()));
        bool objectReenter = true;
        ObjectPool<Item> objectRecursive = null;
        objectRecursive = new ObjectPool<Item>(() => same, actionOnRelease: item => { if (objectReenter) { objectReenter = false; objectRecursive.Release(item); } });
        objectRecursive.Release(same); Put("object.reentrant.count", objectRecursive.CountInactive);
        Put("object.reentrant.first", Id(objectRecursive.Get()));
        Attempt("object.reentrant.duplicate", () => objectRecursive.Release(same));
        Put("object.reentrant.second", Id(objectRecursive.Get()));

    }
    void Diverge(int seed)
    {
        string key = "seed."+seed;
        int creates = 0;
        var events = new List<string>();
        LinkedPool<Item> pool = null;
        pool = new LinkedPool<Item>(() => new Item(100 + ++creates),
            item => events.Add("get:"+Id(item)+":"+pool.CountInactive),
            item => events.Add("release:"+Id(item)+":"+pool.CountInactive),
            item => events.Add("destroy:"+Id(item)+":"+pool.CountInactive), true, 2);
        pool.Release(new Item(17)); pool.Release(new Item(29)); events.Clear();
        Set(pool, seed); Put(key+".written", pool.CountInactive);
        Put(key+".take1", Id(pool.Get())); Put(key+".count1", pool.CountInactive);
        Put(key+".take2", Id(pool.Get())); Put(key+".count2", pool.CountInactive);
        Put(key+".empty", Id(pool.Get())); Put(key+".emptyCount", pool.CountInactive);
        pool.Release(new Item(41)); Put(key+".released", pool.CountInactive);
        Put(key+".events", string.Join("|", events)); events.Clear();
        pool.Clear(); Put(key+".clear", pool.CountInactive); Put(key+".clearEvents", string.Join("|", events));
        pool.Release(new Item(53)); Set(pool, seed); pool.Clear(); Put(key+".nonemptyClear", pool.CountInactive);
        Put(key+".clearStorage", Id(pool.Get()));
    }
    void Throws()
    {
        foreach (string stage in new[] { "create", "get", "release", "destroy", "clear" })
        {
            bool fail = false;
            var events = new List<string>();
            LinkedPool<Item> pool = null;
            Action<string, Item> callback = (phase, item) => { events.Add(phase+":"+Id(item)+":"+pool.CountInactive); if (fail && phase == stage) throw new InvalidOperationException(phase); };
            pool = new LinkedPool<Item>(() => { if (fail && stage == "create") throw new InvalidOperationException("create"); return new Item(71); },
                item => callback("get", item), item => callback("release", item), item => callback(stage == "clear" ? "clear" : "destroy", item), true, 1);
            if (stage != "create") pool.Release(new Item(17));
            events.Clear(); fail = true;
            if (stage == "get" || stage == "create") Attempt("throw."+stage, () => pool.Get());
            else if (stage == "clear") Attempt("throw."+stage, pool.Clear);
            else Attempt("throw."+stage, () => pool.Release(new Item(29)));
            Put("throw."+stage+".count", pool.CountInactive); Put("throw."+stage+".events", string.Join("|", events));
            fail = false; Put("throw."+stage+".next", Id(pool.Get())); Put("throw."+stage+".after", pool.CountInactive);
            pool.Clear();
        }
        LinkedPool<Item> changed = null;
        changed = new LinkedPool<Item>(() => new Item(91), actionOnRelease: item => Set(changed, 3), maxSize: 2);
        changed.Release(new Item(17)); Put("callback.counter.release", changed.CountInactive); Put("callback.counter.created", Id(changed.Get()));
        changed = new LinkedPool<Item>(() => new Item(91), actionOnGet: item => Set(changed, 7));
        changed.Release(new Item(17)); Put("callback.counter.get", Id(changed.Get())); Put("callback.counter.after", changed.CountInactive);
    }
}
