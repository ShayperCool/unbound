using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class FramePayloadProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("FramePayloadProbe").AddComponent<FramePayloadProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    [Serializable] public class Holder<T> { public T value; public int tail = 11; }
    public struct Unmarked { [SerializeField] int m_Value; public Unmarked(int value) { m_Value = value; } public int value => m_Value; }
    [Serializable] public struct Marked { [SerializeField] int m_Value; public Marked(int value) { m_Value = value; } public int value => m_Value; }
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    StreamWriter output;
    Type rateType;
    FieldInfo scalar;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    object Rate(int value) { object result = Activator.CreateInstance(rateType); scalar.SetValue(result, value); return result; }
    void Value(string key, object value)
    {
        Put(key+".raw", scalar.GetValue(value)); Put(key+".drop", rateType.GetProperty("dropFrame").GetValue(value));
        Put(key+".valid", rateType.GetMethod("IsValid").Invoke(value, null));
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("FRAME_PAYLOAD_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    void Record()
    {
        rateType = typeof(UnityEngine.Object).Assembly.GetType("UnityEngine.Playables.FrameRate", true);
        scalar = rateType.GetField("m_Rate", Fields);
        Put("type.serializable", rateType.IsDefined(typeof(SerializableAttribute), false));
        Put("field.marker", scalar.IsDefined(typeof(SerializeField), false)); Put("field.type", scalar.FieldType.FullName);
        int[] rates = { 0, 1, 24, -24, 60, -60, int.MinValue, int.MaxValue };
        for (int i = 0; i < rates.Length; i++)
        {
            object value = Rate(rates[i]); string json = JsonUtility.ToJson(value);
            Put("root."+i+".json", json); Value("root."+i, value);
            Value("restore."+i, JsonUtility.FromJson(json, rateType));
            object target = Rate(17); JsonUtility.FromJsonOverwrite(json, target); Value("overwrite."+i, target);
            JsonUtility.FromJsonOverwrite("{\"unknown\":41}", target); Value("omitted."+i, target);
            JsonUtility.FromJsonOverwrite("{}", target); Value("empty."+i, target);
        }
        foreach (bool drop in new[] { false, true })
        {
            object value = Activator.CreateInstance(rateType, Fields, null, new object[] { 30U, drop }, CultureInfo.InvariantCulture);
            Put("constructor."+drop+".json", JsonUtility.ToJson(value)); Value("constructor."+drop, value);
        }
        var holderType = typeof(Holder<>).MakeGenericType(rateType); object holder = Activator.CreateInstance(holderType);
        holderType.GetField("value").SetValue(holder, Rate(17));
        Put("nested.json", JsonUtility.ToJson(holder));
        JsonUtility.FromJsonOverwrite("{\"value\":{\"m_Rate\":-29},\"tail\":21}", holder);
        Value("nested.overwrite", holderType.GetField("value").GetValue(holder)); Put("nested.tail", holderType.GetField("tail").GetValue(holder));
        JsonUtility.FromJsonOverwrite("{}", holder); Value("nested.omitted", holderType.GetField("value").GetValue(holder));
        var unmarked = new Holder<Unmarked> { value = new Unmarked(37) };
        Put("control.unmarked.root", JsonUtility.ToJson(unmarked.value)); Put("control.unmarked.nested", JsonUtility.ToJson(unmarked));
        JsonUtility.FromJsonOverwrite("{\"value\":{\"m_Value\":53},\"tail\":23}", unmarked);
        Put("control.unmarked.value", unmarked.value.value); Put("control.unmarked.tail", unmarked.tail);
        var marked = new Holder<Marked> { value = new Marked(37) };
        Put("control.marked.root", JsonUtility.ToJson(marked.value)); Put("control.marked.nested", JsonUtility.ToJson(marked));
        JsonUtility.FromJsonOverwrite("{\"value\":{\"m_Value\":53},\"tail\":23}", marked);
        Put("control.marked.value", marked.value.value); Put("control.marked.tail", marked.tail);
    }
}
