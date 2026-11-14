using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
public class YieldLayoutProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("YieldLayoutProbe").AddComponent<YieldLayoutProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    public class DerivedYield : YieldInstruction { public int value = 17; }
    StreamWriter output;
    int entered;
    IEnumerator Hold() { entered++; yield return null; entered++; }
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    void Try(string key, Func<object> value) { try { Put(key, value()); } catch (Exception ex) { Put(key, ex.GetType().FullName); } }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("YIELD_LAYOUT_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            output = new StreamWriter(path); Record(); StartCoroutine(Finish());
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    IEnumerator Finish()
    {
        yield return null; yield return null;
        try { Put("coroutine.after-two-updates", entered); Put("complete", 1); output.Dispose(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
    void Record()
    {
        foreach (var type in new[] { typeof(YieldInstruction), typeof(WaitForSeconds) })
        {
            Put("layout." + type.Name, type.StructLayoutAttribute.Value);
            Put("pack." + type.Name, type.StructLayoutAttribute.Pack);
            Put("declared-size." + type.Name, type.StructLayoutAttribute.Size);
            Try("size." + type.Name, () => Marshal.SizeOf(type));
        }
        Try("seconds.offset", () => Marshal.OffsetOf(typeof(WaitForSeconds), "m_Seconds").ToInt64());
        var field = typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.Instance | BindingFlags.NonPublic);
        float[] values = { .25f, -.5f, -0f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };
        for (int i = 0; i < values.Length; i++)
        {
            var wait = new WaitForSeconds(values[i]);
            Put("field." + i, BitConverter.SingleToInt32Bits((float)field.GetValue(wait)).ToString("x8"));
            IntPtr buffer = Marshal.AllocHGlobal(32);
            try
            {
                for (int b = 0; b < 32; b++) Marshal.WriteByte(buffer, b, 0xcc);
                Try("marshal." + i, () => { Marshal.StructureToPtr(wait, buffer, false); return Marshal.ReadInt32(buffer).ToString("x8"); });
                Try("restore." + i, () => { var restored = new WaitForSeconds(17); Marshal.PtrToStructure(buffer, restored); return BitConverter.SingleToInt32Bits((float)field.GetValue(restored)).ToString("x8"); });
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        Put("derived.value", new DerivedYield().value);
        Put("derived.layout", typeof(DerivedYield).StructLayoutAttribute.Value);
        Put("fixed.assignable", new WaitForFixedUpdate() is YieldInstruction);
        Put("eof.assignable", new WaitForEndOfFrame() is YieldInstruction);
        var coroutine = StartCoroutine(Hold());
        Put("coroutine.created", coroutine != null); Put("coroutine.assignable", coroutine is YieldInstruction);
        Put("coroutine.entered", entered); StopCoroutine(coroutine); Put("coroutine.stopped", entered);
    }
}
