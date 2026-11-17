using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Enums whose values have several names, declared in different orders and sizes.
public enum SortedAliases { A = 1, B = 2, C = 2, D = 3, E = 4 }
public enum ThreeAliases { X = 0, P = 5, Q = 5, R = 5, Z = 9 }
public enum UnsortedAliases { B = 2, A = 1, C = 2, D = 0 }
public enum ThreeUnsorted { M = 2, N = 2, L = 1 }
public enum ThreeUnsortedLast { M = 2, L = 1, N = 2 }
public enum ManyUnsorted
{
    V17 = 17, V3 = 3, V9 = 9, V3b = 3, V12 = 12, V1 = 1, V9b = 9, V20 = 20, V5 = 5, V3c = 3,
    V14 = 14, V0 = 0, V9c = 9, V7 = 7, V18 = 18, V2 = 2, V12b = 12, V11 = 11, V9d = 9, V6 = 6,
    V3d = 3, V15 = 15, V12c = 12, V4 = 4,
}
public enum ManySorted
{
    S0 = 0, S1 = 1, S1b = 1, S1c = 1, S2 = 2, S3 = 3, S3b = 3, S4 = 4, S5 = 5, S5b = 5, S5c = 5, S5d = 5,
    S6 = 6, S7 = 7, S8 = 8, S8b = 8, S9 = 9, S10 = 10, S11 = 11, S11b = 11, S12 = 12, S13 = 13,
}
[Flags] public enum FlagAliases { None = 0, A = 1, Alias = 1, B = 2, AB = 3, C = 4, Both = 2 }
[Flags] public enum FlagsNoZero { One = 1, Two = 2, Uno = 1 }
public enum Signed { Neg = -1, Zero = 0, One = 1, MinusOne = -1, Min = int.MinValue }
public enum Bytes : byte { Low = 1, High = 200, Top = 200 }
public enum Longs : ulong { Small = 1, Big = ulong.MaxValue, Biggest = ulong.MaxValue, Mid = 1UL << 40 }
public enum Shorts : short { Neg = -5, Pos = 5, NegAlias = -5 }

/// <summary>
/// Writes the names that enum values print with into values.jsonl in $ENUM_TEXT_OUT
/// (tests/value_probe.py): every value of the enums above, through each call that turns an enum
/// into text, the order of their names and values, and every value with more than one name among
/// the public enums in UnityEngine namespaces.
/// </summary>
public class EnumTextProbe : MonoBehaviour
{
    static StreamWriter output;

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + key + "\",\"v\":\"" + (value ?? "<null>").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        var dir = Environment.GetEnvironmentVariable("ENUM_TEXT_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            using (output = new StreamWriter(Path.Combine(dir, "values.jsonl")))
            {
                Write();
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static void Write()
    {
        // Each way of printing, on one value with aliases (2 of SortedAliases).
        var c = SortedAliases.C;
        object boxed = c;
        Enum asEnum = c;
        var builder = new StringBuilder();
        builder.Append(boxed).Append(' ').Append((object)ThreeAliases.Q);
        string logged = null;
        Application.LogCallback capture = (text, _, _) => logged = text;
        Application.logMessageReceived += capture;
        Debug.Log(c);
        Application.logMessageReceived -= capture;
        Row("call.ToString", c.ToString());
        Row("call.ToString.G", c.ToString("G"));
        Row("call.ToString.g", c.ToString("g"));
        Row("call.ToString.F", c.ToString("F"));
        Row("call.ToString.D", c.ToString("D"));
        Row("call.ToString.X", c.ToString("X"));
        Row("call.boxed", boxed.ToString());
        Row("call.enum", asEnum.ToString());
        Row("call.enum.G", asEnum.ToString("G"));
        Row("call.concat", "value " + c);
        Row("call.concat.object", string.Concat(boxed, "|", (object)ThreeAliases.Q));
        Row("call.interpolated", $"{c} {ThreeAliases.R}");
        Row("call.format", string.Format("{0} {0:G} {0:D} {1:F} {2}", c, FlagAliases.AB, ThreeAliases.P));
        Row("call.format.invariant", string.Format(CultureInfo.InvariantCulture, "{0}", c));
        Row("call.builder", builder.ToString());
        Row("call.builder.format", new StringBuilder().AppendFormat("{0}", c).ToString());
        Row("call.convert", Convert.ToString(boxed));
        Row("call.getname", Enum.GetName(typeof(SortedAliases), 2));
        Row("call.getname.boxed", Enum.GetName(typeof(SortedAliases), boxed));
        Row("call.type.getenumname", typeof(SortedAliases).GetEnumName(2));
        Row("call.enum.format", Enum.Format(typeof(SortedAliases), 2, "G"));
        Row("call.enum.format.F", Enum.Format(typeof(SortedAliases), c, "F"));
        Row("call.debug.log", logged);
        Row("call.generic", Generic(c));
        Row("call.direct.others", ThreeUnsorted.N.ToString() + " " + ThreeUnsortedLast.N.ToString() + " " + ManyUnsorted.V9.ToString() + " " + ManyUnsorted.V3.ToString()
            + " " + ManySorted.S5.ToString() + " " + FlagAliases.A.ToString() + " " + (FlagAliases.A | FlagAliases.C).ToString() + " " + Signed.Neg.ToString()
            + " " + Bytes.Top.ToString() + " " + Longs.Big.ToString() + " " + Shorts.Neg.ToString() + " " + CameraClearFlags.SolidColor.ToString()
            + " " + KeyCode.LeftApple.ToString() + " " + (CollisionFlags.Sides | CollisionFlags.Above).ToString());
        Row("call.nullable", ((SortedAliases?)c).ToString());
        // The same calls on a float (the class library's float text): through a type parameter and
        // a nullable.
        Row("call.generic.float", Generic(1849.9999f));
        Row("call.nullable.float", ((float?)1849.9999f).ToString());
        Row("call.join.float", string.Join(",", new[] { 1849.9999f, 0.1f }));
        // A format provider whose custom formatter takes one enum value and leaves the rest.
        Row("call.customformatter", string.Format(new Localizer(), "{0} {1} {2:F} {3} {4} {5}", SortedAliases.C, SortedAliases.B, FlagAliases.AB, 1849.9999f, 1849.9999, new Money()));
        // IFormattable's and Enum's ToString through type parameters.
        Row("call.generic.formattable", Formattable(c, "G") + " " + Formattable(1849.9999f, null) + " " + Formattable<IFormattable>(c, null));
        Row("call.generic.enumoverloads", EnumOverloads(c) + "|" + EnumOverloads(FlagAliases.A | FlagAliases.C));
        Row("call.array", string.Join(",", new[] { SortedAliases.B, SortedAliases.C }));
        Row("call.list", string.Join(",", new List<ThreeAliases> { ThreeAliases.P, ThreeAliases.X }));
        Row("call.parse", Enum.Parse(typeof(SortedAliases), "C").ToString());
        Row("call.isdefined", Enum.IsDefined(typeof(SortedAliases), "C").ToString());

        // Every value of each enum, the order of names and values, and some values without a name.
        Values<SortedAliases>(0, 5, 6);
        Values<ThreeAliases>(1, 6);
        Values<UnsortedAliases>(3);
        Values<ThreeUnsorted>();
        Values<ThreeUnsortedLast>();
        Values<ManyUnsorted>(8, 21);
        Values<ManySorted>(14);
        Values<FlagAliases>(5, 6, 7, 8, 9);
        Values<FlagsNoZero>(0, 3, 4);
        Values<Signed>(-2, 2);
        Values<Bytes>(0, 201);
        Values<Longs>(2, (1UL << 40) + 1);
        Values<Shorts>(-6, 0);

        // Every value with more than one name among the UnityEngine enums.
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
        {
            var name = assembly.GetName().Name;
            if (name != "UnityEngine" && !name.StartsWith("UnityEngine.", StringComparison.Ordinal)) continue;
            if (name.Contains("Test")) continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (var type in types.Where(t => t.IsEnum && (t.IsPublic || t.IsNestedPublic) && IsEngineNamespace(t.Namespace)).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                var duplicated = fields.GroupBy(f => Convert.ToUInt64(Unsigned(f.GetRawConstantValue()))).Where(g => g.Count() > 1);
                foreach (var group in duplicated.OrderBy(g => g.Key))
                {
                    var value = Enum.ToObject(type, group.Key);
                    Row("unity." + type.FullName + "." + group.Key, value.ToString() + " | " + Enum.GetName(type, value) + " | " + string.Join(",", group.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal)));
                }
            }
        }
    }

    static bool IsEngineNamespace(string name) => name == "UnityEngine" || name != null && name.StartsWith("UnityEngine.", StringComparison.Ordinal);

    static object Unsigned(object raw) => raw switch
    {
        sbyte v => (ulong)(long)v,
        short v => (ulong)(long)v,
        int v => (ulong)(long)v,
        long v => (ulong)v,
        _ => raw,
    };

    static string Generic<T>(T value) => value.ToString();

    static string Formattable<T>(T value, string format) where T : IFormattable => value.ToString(format, null);

#pragma warning disable 618
    static string EnumOverloads<T>(T value) where T : Enum =>
        value.ToString("G") + " " + value.ToString("F") + " " + value.ToString("D") + " " + value.ToString((IFormatProvider)null) + " " + value.ToString();
#pragma warning restore 618

    sealed class Localizer : IFormatProvider, ICustomFormatter
    {
        public object GetFormat(Type type) => type == typeof(ICustomFormatter) ? this : null;
        public string Format(string format, object arg, IFormatProvider provider) => arg is SortedAliases value && value == SortedAliases.A ? "first" : null;
    }

    struct Money : IFormattable
    {
        public string ToString(string format, IFormatProvider provider) => provider is Localizer ? "localized" : "plain";
    }

    static void Values<T>(params object[] unnamed) where T : struct
    {
        var type = typeof(T);
        var prefix = "enum." + type.Name + ".";
        Row(prefix + "names", string.Join(",", Enum.GetNames(type)));
        var values = Enum.GetValues(type);
        var texts = new List<string>();
        foreach (var value in values) texts.Add(((Enum)value).ToString("D"));
        Row(prefix + "values", string.Join(",", texts));
        texts.Clear();
        foreach (var value in values) texts.Add(value.ToString()); // boxed: a virtual ToString
        Row(prefix + "valueNames", string.Join(",", texts));
        foreach (var raw in unnamed)
        {
            var value = (T)Enum.ToObject(type, raw);
            Row(prefix + "unnamed." + raw, ((object)value).ToString() + " | " + ((Enum)(object)value).ToString("F") + " | " + (Enum.GetName(type, value) ?? "<null>"));
        }
        // Each distinct value boxed, in the "F" format and by Enum.GetName.
        foreach (var value in values.Cast<T>().Distinct())
        {
            var text = ((Enum)(object)value).ToString("D");
            Row(prefix + text, ((object)value).ToString() + " | " + ((Enum)(object)value).ToString("F") + " | " + Enum.GetName(type, value));
        }
    }
}
