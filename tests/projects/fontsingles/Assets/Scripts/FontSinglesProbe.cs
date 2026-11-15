using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
public sealed class FontSinglesProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install(){new GameObject("Adjustments").AddComponent<FontSinglesProbe>();}
    [Serializable] sealed class Row{public string key;public string value;}
    StreamWriter output;
    void Write(string k,string v){output.WriteLine(JsonUtility.ToJson(new Row{key=k,value=v}));output.Flush();}
    static object Call(string n,Type[] types,params object[] args)=>typeof(FontEngine).GetMethod(n,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public,null,types,null).Invoke(null,args);
    static string Bits(float x)=>BitConverter.ToInt32(BitConverter.GetBytes(x),0).ToString("X8");
    static string Glyph(GlyphAdjustmentRecord r){var v=r.glyphValueRecord;return r.glyphIndex+":"+Bits(v.xPlacement)+","+Bits(v.yPlacement)+","+Bits(v.xAdvance)+","+Bits(v.yAdvance);}
    void Capture(string k,object o){if(o==null){Write(k,"null");return;}var records=(GlyphAdjustmentRecord[])o;Write(k+"/capacity",records.Length.ToString());int count=0;foreach(var r in records){if(r.glyphIndex==0)break;Write(k+"/"+count++,Glyph(r));}Write(k+"/count",count.ToString());}
    void Start(){try{output=new StreamWriter(Environment.GetEnvironmentVariable("FONT_FEATURES_OUT"));FontEngine.InitializeFontEngine();foreach(string font in new[]{"Singles","Liberation","DejaVu"}) foreach(int size in new[]{13,37}){
        string p="single/"+font+"/"+size;Write(p+"/load",FontEngine.LoadFontFace(Path.Combine(Application.dataPath,"Fonts",font+".ttf"),size).ToString());FontEngine.TryGetGlyphIndex('A',out uint a);FontEngine.TryGetGlyphIndex('f',out uint f);FontEngine.TryGetGlyphIndex('i',out uint i);FontEngine.TryGetGlyphIndex('l',out uint l);FontEngine.TryGetGlyphIndex('t',out uint t);
        foreach(string method in new[]{"GetSingleAdjustmentRecords"})for(int lookup=0;lookup<(font=="Singles"?4:font=="Liberation"?5:3);lookup++){
            string q=p+"/"+method+"/"+lookup;foreach(uint glyph in new[]{a,f,i,l,t,0u,65536u,f+65536u})Capture(q+"/one/"+glyph,Call(method,new[]{typeof(int),typeof(uint)},lookup,glyph));
            var lists=new[]{new List<uint>{a,f,i,l,t},new List<uint>{t,l,i,f,a},new List<uint>{a,a,f,i,i,l,t},new List<uint>{0,a},new List<uint>(),new List<uint>{65536,a}};for(int j=0;j<lists.Length;j++)Capture(q+"/list/"+j,Call(method,new[]{typeof(int),typeof(List<uint>)},lookup,lists[j]));
        }
    FontEngine.UnloadFontFace();Capture(p+"/unloaded",Call("GetSingleAdjustmentRecords",new[]{typeof(int),typeof(uint)},0,a));
    }try {Call("GetSingleAdjustmentRecords",new[]{typeof(int),typeof(List<uint>)},new object[]{0,null});Write("null-list","returned");}catch(TargetInvocationException e){Write("null-list",e.InnerException.GetType().Name);}Write("complete","1");}catch(Exception e){Write("error",e.ToString());Debug.LogException(e);}finally{output.Dispose();}
#if UNITY_EDITOR
    UnityEditor.EditorApplication.Exit(0);
#else
    Application.Quit();
#endif
    }
}
