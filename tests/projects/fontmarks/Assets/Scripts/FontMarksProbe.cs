using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
public sealed class FontMarksProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("MarkFeaturesProbe").AddComponent<FontMarksProbe>(); }
    [Serializable] sealed class Row { public string key; public string value; }
    StreamWriter output;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static object Call(string name, Type[] signature, params object[] args) => typeof(FontEngine).GetMethod(name, Static, null, signature, null).Invoke(null, args);
    void Write(string key, string value) { output.WriteLine(JsonUtility.ToJson(new Row { key=key, value=value })); output.Flush(); }
    static object Prop(object record, string name) => record.GetType().GetProperty(name).GetValue(record);
    static string Float(object record,string name) => BitConverter.ToInt32(BitConverter.GetBytes((float)Prop(record,name)), 0).ToString("X8");
    static uint Base(object record,bool mark) => (uint)Prop(record,mark?"baseMarkGlyphID":"baseGlyphID");
    static string Record(object record,bool mark)
    {
        object anchor=Prop(record,mark?"baseMarkGlyphAnchorPoint":"baseGlyphAnchorPoint"), adjustment=Prop(record,mark?"combiningMarkPositionAdjustment":"markPositionAdjustment");
        return Base(record,mark)+":"+Float(anchor,"xCoordinate")+","+Float(anchor,"yCoordinate")+">"+Prop(record,mark?"combiningMarkGlyphID":"markGlyphID")+":"+Float(adjustment,"xPositionAdjustment")+","+Float(adjustment,"yPositionAdjustment");
    }
    void Capture(string key,object value,bool mark,bool terminated)
    {
        if(value==null){Write(key,"null");return;}
        var records=(Array)value;Write(key+"/capacity",records.Length.ToString());int count=0;
        foreach(object record in records){if(terminated&&Base(record,mark)==0)break;Write(key+"/"+count++,Record(record,mark));}
        Write(key+"/count",count.ToString());
    }
    void Start()
    {
        try{
        output=new StreamWriter(Environment.GetEnvironmentVariable("FONT_FEATURES_OUT"));FontEngine.InitializeFontEngine();
        foreach(string font in new[]{"Marks","Liberation","DejaVu"}) foreach(int size in new[]{13,37}){
            string p="marks/"+font+"/"+size;
            Write(p+"/load",FontEngine.LoadFontFace(Path.Combine(Application.dataPath,"Fonts",font+".ttf"),size).ToString());
            FontEngine.TryGetGlyphIndex('A',out uint a);FontEngine.TryGetGlyphIndex('f',out uint f);FontEngine.TryGetGlyphIndex(0x301,out uint acute);FontEngine.TryGetGlyphIndex(0x300,out uint grave);
            int lookupCount=font=="Marks"?3:font=="Liberation"?5:3;
            foreach(bool mark in new[]{false,true}){
                string query=mark?"MarkToMarkAdjustment":"MarkToBaseAdjustment",q=p+"/"+query;
                Capture(q+"/all",Call("GetAll"+query+"Records",Type.EmptyTypes),mark,false);
                foreach(uint glyph in new[]{a,f,acute,grave,0u,65535u,65536u,f+65536u}){
                    Capture(q+"/one/"+glyph,Call("Get"+query+"Records",new[]{typeof(uint)},glyph),mark,false);
                    foreach(uint second in new[]{acute,grave,0u,65536u})Write(q+"/pair/"+glyph+"/"+second,Record(Call("Get"+query+"Record",new[]{typeof(uint),typeof(uint)},glyph,second),mark));
                }
                var lists=new[]{new List<uint>{a,f,acute,grave},new List<uint>{grave,acute,f,a},new List<uint>{a,a,acute},new List<uint>{acute},new List<uint>{0,a},new List<uint>(),new List<uint>{65536,a}};
                for(int j=0;j<lists.Length;j++){
                    Capture(q+"/list/"+j,Call("Get"+query+"Records",new[]{typeof(List<uint>)},lists[j]),mark,true);
                    for(int lookup=0;lookup<lookupCount;lookup++)Capture(q+"/lookup/"+lookup+"/list/"+j,Call("Get"+query+"Records",new[]{typeof(int),typeof(List<uint>)},lookup,lists[j]),mark,true);
                }
            }
            FontEngine.UnloadFontFace();
            Capture(p+"/unloaded-base",Call("GetAllMarkToBaseAdjustmentRecords",Type.EmptyTypes),false,false);
            Capture(p+"/unloaded-mark",Call("GetAllMarkToMarkAdjustmentRecords",Type.EmptyTypes),true,false);
        }
        Write("complete","1");
        }catch(Exception e){if(output!=null)Write("error",e.ToString());Debug.LogException(e);}finally{if(output!=null)output.Dispose();}
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
