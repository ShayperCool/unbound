using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

public class ValuePayloadProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("ValuePayloadProbe").AddComponent<ValuePayloadProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    [Serializable] class Holder { public LocationInfo location; public int tail = 11; }
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    static readonly string[] Names = { "m_Timestamp", "m_Latitude", "m_Longitude", "m_Altitude", "m_HorizontalAccuracy", "m_VerticalAccuracy" };
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    void Try(string key, Func<object> value) { try { Put(key, value()); } catch (Exception ex) { Put(key, ex.GetType().FullName); } }
    static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString("x8");
    static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString("x16");
    static string Bits(object value) => value is double d ? Bits(d) : Bits((float)value);
    static LocationInfo Sample(params object[] values)
    {
        object boxed = default(LocationInfo);
        for (int i = 0; i < Names.Length; i++) typeof(LocationInfo).GetField(Names[i], Fields).SetValue(boxed, values[i]);
        return (LocationInfo)boxed;
    }
    void Values(string key, LocationInfo value)
    {
        Put(key+".timestamp", Bits(value.timestamp)); Put(key+".latitude", Bits(value.latitude)); Put(key+".longitude", Bits(value.longitude));
        Put(key+".altitude", Bits(value.altitude)); Put(key+".horizontalAccuracy", Bits(value.horizontalAccuracy)); Put(key+".verticalAccuracy", Bits(value.verticalAccuracy));
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("VALUE_PAYLOAD_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    void Record()
    {
        var type = typeof(LocationInfo); Put("location.layout", type.StructLayoutAttribute.Value);
        Put("location.size", Marshal.SizeOf(type)); Put("location.fields", type.GetFields(Fields).Length);
        foreach (string name in Names) Put("location.offset."+name, Marshal.OffsetOf(type, name).ToInt64());
        Values("location.default", default(LocationInfo)); Values("location.desktop", Input.location.lastData);
        LocationInfo[] samples = {
            Sample(1234567890.125, -33.5f, 151.25f, 12.75f, .125f, 4.25f),
            Sample(-0d, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue),
            Sample(double.PositiveInfinity, -999f, 999f, -.25f, -7.5f, 1e-30f),
        };
        for (int i = 0; i < samples.Length; i++)
        {
            var sample = samples[i]; Values("location.sample."+i, sample);
            IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(type));
            try
            {
                Marshal.StructureToPtr(sample, buffer, false);
                foreach (string name in Names)
                {
                    var field = type.GetField(name, Fields); int offset = (int)Marshal.OffsetOf(type, name);
                    Put("location.bytes."+i+"."+name, field.FieldType == typeof(double) ? Marshal.ReadInt64(buffer, offset).ToString("x16") : Marshal.ReadInt32(buffer, offset).ToString("x8"));
                }
                Values("location.restore."+i, (LocationInfo)Marshal.PtrToStructure(buffer, type));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        var original = samples[0]; var copy = original; object modified = copy;
        type.GetField("m_Latitude", Fields).SetValue(modified, -17f);
        type.GetField("m_Timestamp", Fields).SetValue(modified, 999d);
        Values("location.copy.original", original); Values("location.copy.changed", (LocationInfo)modified);
        Try("location.json.root", () => JsonUtility.ToJson(original));
        var holder = new Holder { location = original }; Try("location.json.nested", () => JsonUtility.ToJson(holder));
        object overwritten = original; JsonUtility.FromJsonOverwrite("{\"m_Timestamp\":99.125,\"m_Latitude\":-55}", overwritten);
        Values("location.json.root-overwrite", (LocationInfo)overwritten);
        JsonUtility.FromJsonOverwrite("{\"location\":{\"m_Timestamp\":99.125,\"m_Latitude\":-55},\"tail\":21}", holder);
        Values("location.json.nested-overwrite", holder.location); Put("location.json.tail", holder.tail);
        var retired = typeof(Animator).Assembly.GetType("UnityEngine.AnimationInfo", true);
        Put("animation.value-type", retired.IsValueType); Put("animation.layout", retired.StructLayoutAttribute.Value);
        Put("animation.declared-size", retired.StructLayoutAttribute.Size); Put("animation.size", Marshal.SizeOf(retired));
        Put("animation.fields", retired.GetFields(Fields).Length);
        object instance = Activator.CreateInstance(retired);
        Put("animation.clip-null", retired.GetProperty("clip").GetValue(instance) == null);
        Put("animation.weight", Bits((float)retired.GetProperty("weight").GetValue(instance)));
        Try("animation.json", () => JsonUtility.ToJson(instance));
        IntPtr empty = Marshal.AllocHGlobal(Marshal.SizeOf(retired));
        try
        {
            Marshal.StructureToPtr(instance, empty, false);
            object restored = Marshal.PtrToStructure(empty, retired);
            Put("animation.restore.clip-null", retired.GetProperty("clip").GetValue(restored) == null);
            Put("animation.restore.weight", Bits((float)retired.GetProperty("weight").GetValue(restored)));
        }
        finally { Marshal.FreeHGlobal(empty); }
    }
}
