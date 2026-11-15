using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// RenderingLayerMask and SphericalHarmonicsL2 as serialized fields: their CLR serializable flag,
// JsonUtility and EditorJsonUtility text at the root and nested, reading that text back (FromJson
// and FromJsonOverwrite over diverged values), Instantiate of a script object holding them, and
// values loaded from an authored asset.
public class BuiltinSerialProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("BuiltinSerialProbe").AddComponent<BuiltinSerialProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("BUILTIN_SERIAL_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string Sh(SphericalHarmonicsL2 sh) => string.Join(" ", Enumerable.Range(0, 27).Select(i => F(sh[i / 9, i % 9])));

    // Distinct coefficients: seed + index / 4.
    static SphericalHarmonicsL2 MakeSh(float seed)
    {
        var sh = new SphericalHarmonicsL2();
        for (int i = 0; i < 27; i++) sh[i / 9, i % 9] = seed + i * 0.25f;
        return sh;
    }

    static string State(BuiltinHolder h) =>
        h.mask.value + "|" + Sh(h.sh) + "|" + h.nested.mask.value + "|" + Sh(h.nested.sh) + "|" + h.nested.after + "|" +
        string.Join(",", h.masks.Select(m => m.value)) + "|" + string.Join(";", h.shs.Select(Sh)) + "|" + h.after;

    static string State(BuiltinWrapper w) =>
        w.mask.value + "|" + Sh(w.sh) + "|" + w.nested.mask.value + "|" + Sh(w.nested.sh) + "|" + w.nested.after + "|" +
        string.Join(",", w.masks.Select(m => m.value)) + "|" + string.Join(";", w.shs.Select(Sh));

    static void Fill(BuiltinHolder h, uint bits, float seed)
    {
        h.mask = bits;
        h.sh = MakeSh(seed);
        h.nested = new BuiltinNested { mask = bits << 1, sh = MakeSh(seed + 100), after = (int)bits + 1 };
        h.masks = new RenderingLayerMask[] { bits << 2, 0xFFFFFFFFu };
        h.shs = new List<SphericalHarmonicsL2> { MakeSh(seed + 200) };
        h.after = 77;
    }

    void Try(string key, Func<object> action)
    {
        try { Put(key, action()); }
        catch (Exception e) { Put(key, "threw " + e.GetType().Name + ": " + e.Message); }
    }

    // One row per top-level field of the holder's own (the script object's header is not compared).
    void Flush(List<string> tree, HashSet<string> fields)
    {
        if (tree.Count == 0) return;
        string path = tree[0].Split(' ')[0];
        if (fields.Contains(path.Split('.')[0])) Put("tree." + path, string.Join(" / ", tree));
    }

    // An array element's properties, with depths relative to the element.
    void Subtree(SerializedProperty element)
    {
        var parts = new List<string>();
        var it = element.Copy();
        var end = element.GetEndProperty();
        int depth = element.depth;
        do
        {
            parts.Add(it.propertyPath + " " + it.propertyType + " " + it.type + " d" + (it.depth - depth) + (it.hasChildren ? " c" : "") + (it.hasVisibleChildren ? " v" : ""));
        }
        while (it.Next(true) && !SerializedProperty.EqualContents(it, end));
        Put("tree." + element.propertyPath, string.Join(" / ", parts));
    }

    void Record()
    {
        Put("type.mask.serializable", typeof(RenderingLayerMask).IsSerializable);
        Put("type.sh.serializable", typeof(SphericalHarmonicsL2).IsSerializable);

        var holder = ScriptableObject.CreateInstance<BuiltinHolder>();
        Fill(holder, 5, 1.5f);
        string json = JsonUtility.ToJson(holder);
        Put("json.holder", json);
        Put("editorJson.holder", EditorJsonUtility.ToJson(holder));

        var wrapper = new BuiltinWrapper
        {
            mask = 9, sh = MakeSh(-3), nested = new BuiltinNested { mask = 0x80000000u, sh = MakeSh(7), after = 3 },
            masks = new List<RenderingLayerMask> { 1, 2 }, shs = new[] { MakeSh(11), MakeSh(12) },
        };
        string wrapperJson = JsonUtility.ToJson(wrapper);
        Put("json.wrapper", wrapperJson);
        Try("json.wrapper.fromJson", () => State(JsonUtility.FromJson<BuiltinWrapper>(wrapperJson)));
        Try("json.mask.direct", () => JsonUtility.ToJson(holder.mask));
        Try("json.sh.direct", () => JsonUtility.ToJson(holder.sh));
        Try("json.mask.fromDirect", () => JsonUtility.FromJson<RenderingLayerMask>("{\"m_Bits\":6}").value);

        // Reading text over diverged values: the fields the text names are replaced.
        var target = ScriptableObject.CreateInstance<BuiltinHolder>();
        Fill(target, 1u << 20, 50);
        JsonUtility.FromJsonOverwrite(json, target);
        Put("json.overwrite", State(target));
        Fill(target, 1u << 20, 50);
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(holder), target);
        Put("editorJson.overwrite", State(target));
        // Text that names only some of the struct's data.
        Fill(target, 1u << 20, 50);
        JsonUtility.FromJsonOverwrite("{\"mask\":{},\"sh\":{\"sh[ 3]\":9,\"sh[26]\":-9},\"nested\":{\"mask\":{\"m_Bits\":12}}}", target);
        Put("json.partial", State(target));

        var copy = Instantiate(holder);
        Put("clone", State(copy));

        // The Editor's property tree over the same data, and writes through it.
        var so = new SerializedObject(holder);
        var it = so.GetIterator();
        var tree = new List<string>();
        var fields = new HashSet<string> { "mask", "sh", "nested", "after" };
        while (it.Next(true))
        {
            if (it.depth == 0 && tree.Count > 0) { Flush(tree, fields); tree.Clear(); }
            tree.Add(it.propertyPath + " " + it.propertyType + " " + it.type + " d" + it.depth + (it.hasChildren ? " c" : "") + (it.hasVisibleChildren ? " v" : "") + (it.isArray ? " a" : ""));
        }
        Flush(tree, fields);
        Subtree(so.FindProperty("masks.Array.data[1]"));
        Subtree(so.FindProperty("shs.Array.data[0]"));
        var maskProperty = so.FindProperty("mask");
        Put("property.mask.values", maskProperty.intValue + "|" + maskProperty.uintValue + "|" + maskProperty.longValue);
        var fullMask = so.FindProperty("masks.Array.data[1]");
        Put("property.fullMask.values", fullMask.intValue + "|" + fullMask.uintValue + "|" + fullMask.longValue);
        so.FindProperty("mask.m_Bits").uintValue = 3;
        so.FindProperty("nested.mask").uintValue = 0x40000000u;
        so.FindProperty("sh.sh[ 4]").floatValue = -1.25f;
        so.FindProperty("shs.Array.data[0].sh[26]").floatValue = 99;
        so.ApplyModifiedPropertiesWithoutUndo();
        Put("property.applied", State(holder));

        var authored = Resources.Load<BuiltinHolder>("Authored");
        Put("authored", authored == null ? "missing" : State(authored));

        // Written in the text serialization and read back (a file in the run's copy of the project).
        var saved = ScriptableObject.CreateInstance<BuiltinHolder>();
        Fill(saved, 0x80000001u, -0.125f);
        UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new UnityEngine.Object[] { saved }, "Library/BuiltinSaved.asset", true);
        var loaded = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget("Library/BuiltinSaved.asset");
        Put("text.roundTrip", loaded.Length == 1 && loaded[0] is BuiltinHolder back ? State(back) : "loaded " + loaded.Length);
    }
}
