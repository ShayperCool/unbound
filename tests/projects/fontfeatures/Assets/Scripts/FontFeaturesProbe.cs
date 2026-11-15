using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public sealed class FontFeaturesProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("FontFeaturesProbe").AddComponent<FontFeaturesProbe>(); }
    [Serializable] sealed class Row { public string key; public string value; }
    StreamWriter output;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static object Call(string name, Type[] signature, params object[] args) => typeof(FontEngine).GetMethod(name, Static, null, signature, null).Invoke(null, args);
    void Write(string key, string value) { output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value })); output.Flush(); }
    static object Prop(object record, string name) => record.GetType().GetProperty(name).GetValue(record);
    static string Record(object record)
    {
        var ids = (uint[])Prop(record, "componentGlyphIDs");
        return (ids == null ? "null" : string.Join(",", ids)) + ">" + Prop(record, "ligatureGlyphID");
    }
    void Capture(string key, object value, bool terminated)
    {
        if (value == null) { Write(key, "null"); return; }
        var records = (Array)value;
        Write(key + "/capacity", records.Length.ToString());
        int count = 0;
        foreach (object record in records)
        {
            if (terminated && (uint)Prop(record, "ligatureGlyphID") == 0) break;
            Write(key + "/" + count++, Record(record));
        }
        Write(key + "/count", count.ToString());
    }
    void Start()
    {
        try
        {
            output = new StreamWriter(Environment.GetEnvironmentVariable("FONT_FEATURES_OUT"));
            FontEngine.InitializeFontEngine();
            foreach (string font in new[] { "Controlled", "Liberation", "DejaVu" })
            foreach (int size in new[] { 13, 37 })
            {
                string prefix = font + "/" + size;
                Write(prefix + "/load", FontEngine.LoadFontFace(Path.Combine(Application.dataPath, "Fonts", font + ".ttf"), size).ToString());
                Capture(prefix + "/all", Call("GetAllLigatureSubstitutionRecords", Type.EmptyTypes), false);
                FontEngine.TryGetGlyphIndex('f', out uint f); FontEngine.TryGetGlyphIndex('A', out uint a); FontEngine.TryGetGlyphIndex('i', out uint i);
                foreach (uint glyph in new[] { f, a, i, 0u, 65535u, 65536u })
                    Capture(prefix + "/one/" + glyph, Call("GetLigatureSubstitutionRecords", new[] { typeof(uint) }, glyph), true);
                var lists = new[] { new List<uint> { f, a }, new List<uint> { a, f }, new List<uint> { f, f, a }, new List<uint> { f }, new List<uint> { 0, f }, new List<uint>(), new List<uint> { 65536, f } };
                for (int j = 0; j < lists.Length; j++)
                {
                    Capture(prefix + "/list/" + j, Call("GetLigatureSubstitutionRecords", new[] { typeof(List<uint>) }, lists[j]), true);
                    for (int lookup = -2; lookup < 14; lookup++)
                        Capture(prefix + "/lookup/" + lookup + "/list/" + j, Call("GetLigatureSubstitutionRecords", new[] { typeof(int), typeof(List<uint>) }, lookup, lists[j]), true);
                }
                Capture(prefix + "/lookup-one", Call("GetLigatureSubstitutionRecords", new[] { typeof(int), typeof(uint) }, 1, f), true);
                FontEngine.UnloadFontFace();
                Capture(prefix + "/unloaded", Call("GetAllLigatureSubstitutionRecords", Type.EmptyTypes), false);
            }
            var recordType = typeof(FontEngine).Assembly.GetType("UnityEngine.TextCore.LowLevel.LigatureSubstitutionRecord");
            var left = Activator.CreateInstance(recordType);
            var right = Activator.CreateInstance(recordType);
            recordType.GetProperty("componentGlyphIDs").SetValue(left, new uint[] { 4, 5 });
            recordType.GetProperty("componentGlyphIDs").SetValue(right, new uint[] { 4, 5 });
            Write("equality/components", left.Equals(right).ToString());
            recordType.GetProperty("ligatureGlyphID").SetValue(right, 7u);
            Write("equality/replacement", left.Equals(right).ToString());
            Write("equality/default", Activator.CreateInstance(recordType).Equals(Activator.CreateInstance(recordType)).ToString());
            try { Call("GetLigatureSubstitutionRecords", new[] { typeof(List<uint>) }, new object[] { null }); Write("null-list", "returned"); }
            catch (TargetInvocationException e) { Write("null-list", e.InnerException.GetType().Name); }
            Write("complete", "1");
        }
        catch (Exception e) { if (output != null) Write("error", e.ToString()); Debug.LogException(e); }
        finally { if (output != null) output.Dispose(); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
