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
            using (output = new StreamWriter(path)) { Record(); RecordCurveGradients(); RecordNumbers(); Put("complete", 1); }
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

    static string State(CurveGradientHolder h)
    {
        var g = h.gradient;
        var c = h.curve;
        return g.mode + " " + g.colorSpace + "|" + string.Join(" ", g.colorKeys.Select(k => F(k.color.r) + "," + F(k.color.g) + "," + F(k.color.b) + "@" + F(k.time)))
            + "|" + string.Join(" ", g.alphaKeys.Select(k => F(k.alpha) + "@" + F(k.time)))
            + "|" + (c == null ? "null" : c.preWrapMode + " " + c.postWrapMode + " " + string.Join(" ", c.keys.Select(k =>
                F(k.time) + "," + F(k.value) + "," + F(k.inTangent) + "," + F(k.outTangent) + "," + F(k.inWeight) + "," + F(k.outWeight) + "," + k.weightedMode)))
            + "|" + h.after;
    }

    static CurveGradientHolder MakeCurveGradient()
    {
        var g = new Gradient { mode = GradientMode.Fixed, colorSpace = ColorSpace.Linear };
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.5f, 0.25f), 0.25f), new GradientColorKey(Color.black, 0.75f), new GradientColorKey(new Color(0.2f, 0.4f, 0.6f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
        var c = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1f), new Keyframe(1f, 2f, 0.5f, 0.5f, 0.25f, 0.75f) { weightedMode = WeightedMode.Both })
        {
            preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.PingPong,
        };
        return new CurveGradientHolder { gradient = g, curve = c, after = 4 };
    }

    // Gradient and AnimationCurve fields in JsonUtility text: written, read back, read from the
    // text a Shader Graph gradient property is saved as.
    void RecordCurveGradients()
    {
        var holder = MakeCurveGradient();
        string json = JsonUtility.ToJson(holder);
        Put("json.curveGradient", json);
        Try("json.curveGradient.fromJson", () => State(JsonUtility.FromJson<CurveGradientHolder>(json)));
        Try("json.curveGradient.saved", () => State(JsonUtility.FromJson<CurveGradientHolder>(
            "{\"gradient\":{\"serializedVersion\":\"2\",\"key0\":{\"r\":1.0,\"g\":1.0,\"b\":1.0,\"a\":1.0},\"key1\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":1.0},"
            + "\"key2\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"key3\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"key4\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},"
            + "\"key5\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"key6\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},\"key7\":{\"r\":0.0,\"g\":0.0,\"b\":0.0,\"a\":0.0},"
            + "\"ctime0\":20352,\"ctime1\":51327,\"ctime2\":65535,\"ctime3\":0,\"ctime4\":0,\"ctime5\":0,\"ctime6\":0,\"ctime7\":0,"
            + "\"atime0\":0,\"atime1\":65535,\"atime2\":0,\"atime3\":0,\"atime4\":0,\"atime5\":0,\"atime6\":0,\"atime7\":0,"
            + "\"m_Mode\":0,\"m_ColorSpace\":1,\"m_NumColorKeys\":3,\"m_NumAlphaKeys\":2},\"after\":2}")));
        Try("json.curveGradient.direct", () => JsonUtility.ToJson(holder.gradient) + " " + JsonUtility.ToJson(holder.curve));
        // Text that names only some of a gradient's data is not read the same way twice (the
        // Editor leaves keys it does not name undefined): not recorded.
    }

    // The digits JsonUtility writes for floats and doubles (fractions with many digits, very
    // small and very large numbers), and the same text read back, as bit patterns.
    void RecordNumbers()
    {
        var holder = new NumberHolder { floats = Enumerable.Range(1, 64).Select(i => i / 7f).ToArray() };
        var doubles = new List<double>();
        for (int i = 1; i <= 64; i++) { doubles.Add(i / 7.0); doubles.Add(1 / (i * 997.0)); doubles.Add(i / 3e-5); }
        doubles.AddRange(new[]
        {
            6.8474229291646805e-84, 6.7744064557649905e-196, 6.8304626092319105e+276, 7.1207531506153835e-28,
            0.0, -0.0, 1.0, -2.5, 100.0, 0.1, 123456789012345678901.0, 1e21, 1.5e22, 0.0000015, 1e-7, 1.25e-10,
            double.MaxValue, double.Epsilon, 2.2250738585072014e-308,
        });
        holder.doubles = doubles.ToArray();
        string json = JsonUtility.ToJson(holder);
        Put("json.numbers", json);
        Try("json.numbers.fromJson", () => Bits(JsonUtility.FromJson<NumberHolder>(json)));
        // Number text as other writers give it: long integers, more digits than a double holds,
        // exponents, numbers below the smallest normal double and beyond a float's range.
        Try("json.numbers.read.integers", () => Bits(JsonUtility.FromJson<NumberHolder>("{\"doubles\":[123456789012345678901,123456789012345680000.0,"
            + "18446744073709551615,18446744073709551616,18446744073709551617.5,99999999999999999999,429496729.5,4294967295,4294967296,"
            + "-2147483648,-2147483649,-9223372036854775808,-9223372036854775809,9007199254740993,9007199254740993.0,"
            + "123456789012345678901234567890,12345678901234567890.123,-0,-0.0,7]}")));
        Try("json.numbers.read.fractions", () => Bits(JsonUtility.FromJson<NumberHolder>("{\"doubles\":[0.1234567890123456789012345,0.30000000000000004,"
            + "0.000000000000000000000000000001234567890123456789,9007199254740993.5,90071992547409935.25,1.00000000000000000000000001,"
            + "1E5,1e+5,1.5E-5,1e308,1.7976931348623157e308,123456789.123456789e-300,0.00001e-300]}")));
        Try("json.numbers.read.long", () => Bits(JsonUtility.FromJson<NumberHolder>("{\"doubles\":[99.9999999999999999e-304,1.1529215046068469760951e-294,"
            + "0.24714652281731693407e-291,9.29058107097009616897993,0.10973160311660499853,1.76419634386536593167118e-296,1.8446744073709551615,"
            + "1.8446744073709551616,1.84467440737095516159,0.92233720368547758079,1.84467440737095516150001]}")));
        Try("json.numbers.read.small", () => Bits(JsonUtility.FromJson<NumberHolder>("{\"doubles\":[2.2250738585072014e-308,3e-308,2e-308,1e-310,"
            + "2.5e-320,4.9e-324,5e-324,22250738585072014e-324,0.00000000000000000000000000000000000001e-280]}")));
        Try("json.numbers.read.tiny", () => Bits(JsonUtility.FromJson<NumberHolder>("{\"doubles\":[1e-400,1]}")));
        Try("json.numbers.read.huge", () => Bits(JsonUtility.FromJson<NumberHolder>("{\"doubles\":[1e400,1]}")));
        Try("json.numbers.read.floats", () => Bits(JsonUtility.FromJson<NumberHolder>("{\"floats\":[0.1,16777217,3.4028235e38,3.4028236e38,1e39,1e-46,"
            + "1.401298464324817e-45,0.20000000298023225,123456789012345678901,-0.0]}")));
    }

    static string Bits(NumberHolder h) =>
        string.Join(" ", h.floats.Select(f => BitConverter.DoubleToInt64Bits(f).ToString("x16")))
        + "|" + string.Join(" ", h.doubles.Select(d => BitConverter.DoubleToInt64Bits(d).ToString("x16")));

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
