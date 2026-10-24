using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Properties;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Unity.Properties and com.unity.serialization, recorded the same way in the Editor and in
/// Unbound when Play mode is entered (<c>PROPERTIES_PROBE_OUT</c> names the JSON file).
/// <c>Unity -batchmode -projectPath tests/projects/properties -executeMethod PropertiesProbe.Record</c>.
/// </summary>
[InitializeOnLoad]
public static class PropertiesProbe
{
    static readonly List<(string Key, string Value)> s_Records = new List<(string, string)>();

    static string Out => Environment.GetEnvironmentVariable("PROPERTIES_PROBE_OUT");

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
    }

    static PropertiesProbe()
    {
        if (string.IsNullOrEmpty(Out)) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            try { Run(); }
            catch (Exception e) { Add("exception", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
            Write();
            EditorApplication.Exit(0);
        };
    }

    static void Run()
    {
        foreach (var (name, section) in new (string, Action)[]
        {
            ("bags", Bags), ("paths", Paths), ("values", Values), ("visitors", Visitors),
            ("conversions", Conversions), ("utilities", Utilities), ("serialization", Serialization),
        })
        {
            try { section(); }
            catch (Exception e) { Add("exception." + name, e.GetType().Name + ": " + e.Message); }
        }
    }

    static void Bags()
    {
        Add("bag.reflected", Names(PropertyBag.GetPropertyBag<ReflectedThing>()));
        Add("bag.base", Names(PropertyBag.GetPropertyBag<ReflectedBase>()));
        Add("bag.value", Names(PropertyBag.GetPropertyBag<ValueThing>()));
        Add("bag.nested", Names(PropertyBag.GetPropertyBag<Nested>()));
        Add("bag.generated", Names(PropertyBag.GetPropertyBag<GeneratedThing>()));
        Add("bag.vector3", Names(PropertyBag.GetPropertyBag<Vector3>()));
        Add("bag.color", Names(PropertyBag.GetPropertyBag<Color>()));
        Add("bag.version", Names(PropertyBag.GetPropertyBag<Version>()));
        Add("bag.string", (PropertyBag.GetPropertyBag(typeof(string)) == null).ToString());
        Add("bag.int", (PropertyBag.GetPropertyBag(typeof(int)) == null).ToString());
        Add("bag.interface", (PropertyBag.GetPropertyBag(typeof(IDisposable)) == null).ToString());
        Add("bag.exists.reflected", PropertyBag.Exists<ReflectedThing>().ToString());
        Add("bag.exists.unseen", PropertyBag.Exists(typeof(KindOfThing)).ToString());
        var thing = new ReflectedThing();
        var bag = PropertyBag.GetPropertyBag<ReflectedThing>();
        Add("bag.readOnly", string.Join(",", bag.GetProperties(ref thing).Select(p => p.Name + "=" + p.IsReadOnly)));
        Add("bag.valueTypes", string.Join(",", bag.GetProperties(ref thing).Select(p => p.Name + ":" + TypeUtility.GetTypeDisplayName(p.DeclaredValueType()))));
        var list = new List<int> { 4, 5 };
        Add("bag.list.properties", string.Join(",", PropertyBag.GetPropertyBag<List<int>>().GetProperties(ref list).Select(p => p.Name)));
        var dict = new Dictionary<string, int> { { "k", 1 } };
        Add("bag.dict.properties", string.Join(",", PropertyBag.GetPropertyBag<Dictionary<string, int>>().GetProperties(ref dict).Select(p => p.Name)));
        Add("bag.list.instance", PropertyBag.CreateInstance<List<int>>().Count.ToString());
        Add("bag.array.instance", TypeUtility.InstantiateArray<int[]>(3).Length.ToString());
    }

    static string Names<T>(IPropertyBag<T> bag) => bag == null ? "null" : string.Join(",", bag.GetProperties().Select(p => p.Name));

    static void Paths()
    {
        foreach (var text in new[] { "a", "a.b", "a[1]", "a[\"key\"].b", "[0][1]", " a.b", "list[2].value", "a.b.c.d.e.f" })
        {
            var path = new PropertyPath(text);
            Add("path." + text, $"{path.Length}:{path}:{string.Join("|", Enumerable.Range(0, path.Length).Select(i => path[i].Kind + "=" + path[i]))}");
        }
        foreach (var bad in new[] { "a..b", ".a", "a[x]", "a[-1]", "a[\"k]", "a\"b" })
        {
            try { Add("path.bad." + bad, new PropertyPath(bad).ToString()); }
            catch (Exception e) { Add("path.bad." + bad, e.GetType().Name + ": " + e.Message); }
        }
        var combined = PropertyPath.Combine(new PropertyPath("a.b"), new PropertyPath("[3].c"));
        Add("path.combine", combined.ToString());
        Add("path.pop", PropertyPath.Pop(combined).ToString());
        Add("path.sub", PropertyPath.SubPath(combined, 1, 2).ToString());
        Add("path.appendKey", PropertyPath.AppendKey(new PropertyPath("d"), 5).ToString());
        Add("path.equals", (new PropertyPath("a.b[1]") == PropertyPath.AppendIndex(new PropertyPath("a.b"), 1)).ToString());
        Add("path.empty", new PropertyPath("").IsEmpty + " " + new PropertyPath("   ").Length);
    }

    static void Values()
    {
        var thing = new ReflectedThing();
        Add("value.speed", PropertyContainer.GetValue<ReflectedThing, float>(thing, "speed").ToString(CultureInfo.InvariantCulture));
        Add("value.nested", PropertyContainer.GetValue<ReflectedThing, int>(thing, new PropertyPath("nested.value")).ToString());
        Add("value.list", PropertyContainer.GetValue<ReflectedThing, int>(thing, new PropertyPath("list[1]")).ToString());
        Add("value.array", PropertyContainer.GetValue<ReflectedThing, int>(thing, new PropertyPath("array[1]")).ToString());
        Add("value.dict", PropertyContainer.GetValue<ReflectedThing, KeyValuePair<string, int>>(thing, new PropertyPath("dict[\"b\"]")).ToString());
        Add("value.dictValue", PropertyContainer.GetValue<ReflectedThing, int>(thing, new PropertyPath("dict[\"b\"].Value")).ToString());
        Add("value.vector", PropertyContainer.GetValue<ReflectedThing, float>(thing, new PropertyPath("position.y")).ToString(CultureInfo.InvariantCulture));
        Add("value.convert", PropertyContainer.GetValue<ReflectedThing, string>(thing, "speed"));
        Add("value.enumAsInt", PropertyContainer.TryGetValue<ReflectedThing, int>(ref thing, new PropertyPath("kind"), out var asInt, out var asIntCode) + " " + asInt + " " + asIntCode);
        Add("value.computed", PropertyContainer.GetValue<ReflectedThing, int>(thing, "Computed").ToString());
        PropertyContainer.SetValue(thing, "speed", 7);
        Add("value.setInt", thing.speed.ToString(CultureInfo.InvariantCulture));
        PropertyContainer.SetValue(thing, new PropertyPath("nested.text"), "changed");
        Add("value.setNested", thing.nested.text);
        PropertyContainer.SetValue(thing, new PropertyPath("list[0]"), "9");
        Add("value.setList", string.Join(",", thing.list));
        PropertyContainer.SetValue(thing, new PropertyPath("position.x"), 5.5);
        Add("value.setStruct", thing.position.x.ToString(CultureInfo.InvariantCulture));
        var value = new ValueThing { x = 1, y = 2 };
        PropertyContainer.SetValue(ref value, "x", 4);
        Add("value.setValueType", value.x + " " + PropertyContainer.GetValue<ValueThing, int>(ref value, "Sum"));
        Add("value.isValid", string.Join(",", new[] { "speed", "nested.value", "list[9]", "missing.value", "nope", "dict[\"a\"]", "dict[\"z\"]" }.Select(p => p + "=" + PropertyContainer.IsPathValid(ref thing, p))));
        Add("value.try.invalidPath", PropertyContainer.TryGetValue<ReflectedThing, int>(ref thing, new PropertyPath("nope"), out _, out var code1) + " " + code1);
        Add("value.try.nullPath", PropertyContainer.TryGetValue<ReflectedThing, int>(ref thing, new PropertyPath("missing.value"), out _, out var code2) + " " + code2);
        Add("value.try.cast", PropertyContainer.TryGetValue<ReflectedThing, Nested>(ref thing, new PropertyPath("speed"), out _, out var code3) + " " + code3);
        Add("value.try.readOnly", PropertyContainer.TrySetValue(ref thing, new PropertyPath("Computed"), 3, out var code4) + " " + code4);
        foreach (var (name, action) in new (string, Action)[]
        {
            ("getMissing", () => PropertyContainer.GetValue<ReflectedThing, int>(thing, "nope")),
            ("getCast", () => PropertyContainer.GetValue<ReflectedThing, Nested>(thing, "speed")),
            ("setReadOnly", () => PropertyContainer.SetValue(thing, "Computed", 1)),
            ("getNull", () => PropertyContainer.GetValue<ReflectedThing, int>((ReflectedThing)null, "speed")),
            ("visitNull", () => PropertyContainer.Accept(new RecordingVisitor(), (ReflectedThing)null)),
            ("getEmpty", () => PropertyContainer.GetValue<ReflectedThing, int>(thing, new PropertyPath(""))),
            ("property", () => PropertyContainer.GetProperty(thing, new PropertyPath("nope"))),
        })
        {
            try { action(); Add("error." + name, "none"); }
            catch (Exception e) { Add("error." + name, e.GetType().Name + ": " + e.Message); }
        }
        var property = PropertyContainer.GetProperty(thing, new PropertyPath("list[1]"));
        Add("value.getProperty", property.Name + " " + property.GetType().Name.Split('`')[0] + " " + (property is IListElementProperty));
    }

    sealed class RecordingVisitor : PropertyVisitor
    {
        public readonly List<string> Visited = new List<string>();
        PropertyPath m_Path;

        protected override void VisitProperty<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
        {
            var previous = m_Path;
            m_Path = PropertyPath.AppendProperty(m_Path, property);
            Visited.Add(m_Path + ":" + TypeUtility.GetTypeDisplayName(typeof(TValue)) + (value == null ? "=null" : TypeTraits<TValue>.IsContainer ? "" : "=" + value));
            if (value != null) PropertyContainer.TryAccept(this, ref value);
            m_Path = previous;
        }

        protected override void VisitList<TContainer, TList, TElement>(Property<TContainer, TList> property, ref TContainer container, ref TList value)
        {
            Visited.Add("list " + property.Name + " of " + TypeUtility.GetTypeDisplayName(typeof(TElement)) + " count " + value.Count);
            base.VisitList<TContainer, TList, TElement>(property, ref container, ref value);
        }

        protected override void VisitDictionary<TContainer, TDictionary, TKey, TValue>(Property<TContainer, TDictionary> property, ref TContainer container, ref TDictionary value)
        {
            Visited.Add("dictionary " + property.Name + " count " + value.Count);
            base.VisitDictionary<TContainer, TDictionary, TKey, TValue>(property, ref container, ref value);
        }

        protected override bool IsExcluded<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value) =>
            property.Name == "color";
    }

    sealed class DoubleInts : IVisitPropertyAdapter<int>
    {
        public void Visit<TContainer>(in VisitContext<TContainer, int> context, ref TContainer container, ref int value) => value *= 2;
    }

    static void Visitors()
    {
        var thing = new ReflectedThing();
        var visitor = new RecordingVisitor();
        PropertyContainer.Accept(visitor, thing);
        Add("visit.reflected", string.Join("\n", visitor.Visited));
        var doubled = new ReflectedThing();
        var adapted = new RecordingVisitor();
        adapted.AddAdapter(new DoubleInts());
        PropertyContainer.Accept(adapted, ref doubled);
        Add("visit.adapter", $"{doubled.baseValue} {doubled.nested.value} {string.Join(",", doubled.list)} {doubled.locked}");
        var generated = new GeneratedThing();
        var genVisitor = new RecordingVisitor();
        PropertyContainer.Accept(genVisitor, generated);
        Add("visit.generated", string.Join("\n", genVisitor.Visited));
        Add("visit.try.int", PropertyContainer.TryAccept(new RecordingVisitor(), ref doubled.baseValue, out var code) + " " + code);
    }

    static void Conversions()
    {
        string Try<TFrom, TTo>(TFrom from)
        {
            var ok = TypeConversion.TryConvert<TFrom, TTo>(ref from, out var to);
            return ok + ":" + (to is float f ? f.ToString("R", CultureInfo.InvariantCulture) : to is double d ? d.ToString("R", CultureInfo.InvariantCulture) : to?.ToString() ?? "null");
        }
        Add("convert.intToFloat", Try<int, float>(3));
        Add("convert.floatToInt", Try<float, int>(3.9f));
        Add("convert.negFloatToUInt", Try<float, uint>(-1.5f));
        Add("convert.doubleToBool", Try<double, bool>(0.0));
        Add("convert.stringToInt", Try<string, int>("42"));
        Add("convert.stringFloatToInt", Try<string, int>("4.7"));
        Add("convert.badStringToInt", Try<string, int>("x"));
        Add("convert.stringToBool", Try<string, bool>("2"));
        Add("convert.floatToString", Try<float, string>(0.1f));
        Add("convert.intToString", Try<int, string>(-5));
        Add("convert.stringToEnum", Try<string, KindOfThing>("Third"));
        Add("convert.badStringToEnum", Try<string, KindOfThing>("Fourth"));
        Add("convert.intToEnum", Try<int, KindOfThing>(1));
        Add("convert.objectToInt", Try<object, int>(5));
        Add("convert.boxedLongToInt", Try<object, int>(5L));
        Add("convert.intToObject", Try<int, object>(5));
        Add("convert.charToInt", Try<char, int>('A'));
        Add("convert.stringToChar", Try<string, char>(""));
        Add("convert.nullable", Try<int, int?>(3));
        Add("convert.fromNullable", Try<int?, int>(null));
        Add("convert.derived", Try<ReflectedThing, ReflectedBase>(new ReflectedThing()) .Split(':')[0]);
        Add("convert.unrelated", Try<Nested, ReflectedBase>(new Nested()));
        Add("convert.vector", Try<Vector3, Vector2>(Vector3.one));
        TypeConversion.Register((ref Vector3 v) => new Vector2(v.x, v.z));
        Add("convert.registered", Try<Vector3, Vector2>(new Vector3(1, 2, 3)));
        try { var s = "x"; TypeConversion.Convert<string, Nested>(ref s); Add("convert.throw", "none"); }
        catch (Exception e) { Add("convert.throw", e.GetType().Name + ": " + e.Message); }
    }

    static void Utilities()
    {
        foreach (var type in new[] { typeof(int), typeof(List<int>), typeof(Dictionary<string, List<float>>), typeof(Nested), typeof(KeyValuePair<int, string>), typeof(int[]), typeof(PropertyCollection<int>.Enumerator) })
            Add("display." + type.Name, TypeUtility.GetTypeDisplayName(type));
        Add("instantiate.nested", TypeUtility.Instantiate<Nested>().value.ToString());
        Add("instantiate.value", TypeUtility.Instantiate<ValueThing>().x.ToString());
        Add("instantiate.string", "'" + TypeUtility.Instantiate<string>() + "'");
        Add("instantiate.can", string.Join(",", new[] { typeof(Nested), typeof(IDisposable), typeof(string), typeof(Version), typeof(ScriptableObject) }.Select(t => t.Name + "=" + TypeUtility.CanBeInstantiated(t))));
        try { TypeUtility.Instantiate<IDisposable>(); Add("instantiate.interface", "none"); }
        catch (Exception e) { Add("instantiate.interface", e.GetType().Name + ": " + e.Message); }
        Add("instantiate.derived", TypeUtility.Instantiate<ReflectedBase>(typeof(ReflectedThing)).GetType().Name);
        Add("traits", $"{TypeTraits<int?>.CanBeNull} {TypeTraits<KindOfThing>.IsContainer} {TypeTraits<string>.IsContainer} {TypeTraits<int[,]>.IsMultidimensionalArray} {TypeTraits<GameObject>.IsUnityObject}");
        Add("rootType", typeof(ReflectedThing).GetRootType().Name);
    }

    static void Serialization()
    {
        var thing = new SerializedThing();
        var json = JsonSerialization.ToJson(thing);
        Add("json.write", json);
        var back = JsonSerialization.FromJson<SerializedThing>(json);
        Add("json.read", JsonSerialization.ToJson(back));
        Add("json.minified", JsonSerialization.ToJson(thing, new JsonSerializationParameters { Minified = true, Simplified = true }));
        var reflected = JsonSerialization.ToJson(new ReflectedThing());
        Add("json.reflected", reflected);
        var overwrite = new SerializedThing { number = 0, numbers = new List<int>() };
        JsonSerialization.FromJsonOverride("{\"number\": 77, \"numbers\": [1]}", ref overwrite);
        Add("json.override", overwrite.number + " " + string.Join(",", overwrite.numbers) + " " + overwrite.text);
        try { JsonSerialization.FromJson<SerializedThing>("{\"number\": }"); Add("json.bad", "none"); }
        catch (Exception e) { Add("json.bad", e.GetType().Name); }
    }

    static void Add(string key, string value) => s_Records.Add((key, value));

    static void Write()
    {
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < s_Records.Count; i++)
        {
            sb.Append("  ").Append(Quote(s_Records[i].Key)).Append(": ").Append(Quote(s_Records[i].Value));
            sb.Append(i + 1 < s_Records.Count ? ",\n" : "\n");
        }
        File.WriteAllText(Out, sb.Append("}\n").ToString());
    }

    static string Quote(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
