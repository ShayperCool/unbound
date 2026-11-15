using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
public sealed class FontPairsProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("Pairs").AddComponent<FontPairsProbe>(); }
    [Serializable] sealed class Row { public string key; public string value; }
    StreamWriter output;
    bool gposProjection;
    string DescribePair(GlyphPairAdjustmentRecord r) => gposProjection ? Glyph(r.firstAdjustmentRecord)+"/"+Glyph(r.secondAdjustmentRecord) : Pair(r);
    void Write(string key, string value) { output.WriteLine(JsonUtility.ToJson(new Row { key=key, value=value })); output.Flush(); }
    static object Call(string name, Type[] types, params object[] args) => typeof(FontEngine).GetMethod(name, BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public, null, types, null).Invoke(null,args);
    static string Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value),0).ToString("X8");
    static string Glyph(GlyphAdjustmentRecord record) { var v=record.glyphValueRecord; return record.glyphIndex+":"+Bits(v.xPlacement)+","+Bits(v.yPlacement)+","+Bits(v.xAdvance)+","+Bits(v.yAdvance); }
    static string Pair(GlyphPairAdjustmentRecord r) => Glyph(r.firstAdjustmentRecord)+"/"+Glyph(r.secondAdjustmentRecord)+"/flags="+(int)r.featureLookupFlags;
    void Capture(string key, object value)
    {
        if (value==null) { Write(key,"null"); return; }
        if (value is GlyphPairAdjustmentRecord scalar) { Write(key,DescribePair(scalar)); return; }
        var records=(GlyphPairAdjustmentRecord[])value;
        Write(key+"/capacity", records.Length.ToString());
        int count=0;
        foreach (var record in records) { if (record.firstAdjustmentRecord.glyphIndex==0) break; Write(key+"/"+count++,DescribePair(record)); }
        Write(key+"/count",count.ToString());
    }
    void Gpos()
    {
        gposProjection=true;
            foreach (string font in new[] { "PairOnly", "KernOnly", "Both", "Neither", "ClassZero", "ClassRange", "Fallback", "KernWithSingle", "Reverse" })
            foreach (int size in new[] { 13,37 })
            {
                string prefix="gpos/"+font+"/"+size;
                Write(prefix+"/load",FontEngine.LoadFontFace(Path.Combine(Application.dataPath,"Fonts",font+".ttf"),size).ToString());
                var glyphs=new List<uint>();
                foreach (char c in "AVaTilft") { FontEngine.TryGetGlyphIndex(c,out uint glyph); glyphs.Add(glyph); Write(prefix+"/glyph/"+c,glyph.ToString()); }
                foreach (string method in new[] { "GetAllPairAdjustmentRecords" }) Capture(prefix+"/"+method,Call(method,Type.EmptyTypes));
                foreach (string method in new[] { "GetPairAdjustmentRecords" })
                {
                    foreach (uint glyph in new[] { glyphs[0],glyphs[1],glyphs[3],0u,65536u,glyphs[0]+65536u })
                    {
                        if (method=="GetPairAdjustmentRecords") Capture(prefix+"/"+method+"/one/"+glyph,Call(method,new[]{typeof(uint)},glyph));
                        else { var args=new object[]{glyph,0}; Capture(prefix+"/"+method+"/one/"+glyph,Call(method,new[]{typeof(uint),typeof(int).MakeByRefType()},args)); Write(prefix+"/"+method+"/one/"+glyph+"/out",args[1].ToString()); }
                    }
                    var lists=new[] { glyphs,new List<uint>{glyphs[3],glyphs[0],glyphs[0]},new List<uint>{0,glyphs[0]},new List<uint>(),new List<uint>{65536,glyphs[0]},new List<uint>{glyphs[1]},new List<uint>{glyphs[1],glyphs[0]} };
                    for (int i=0;i<lists.Length;i++)
                    {
                        if (method=="GetPairAdjustmentRecords") Capture(prefix+"/"+method+"/list/"+i,Call(method,new[]{typeof(List<uint>)},lists[i]));
                        else { var args=new object[]{lists[i],0};Capture(prefix+"/"+method+"/list/"+i,Call(method,new[]{typeof(List<uint>),typeof(int).MakeByRefType()},args));Write(prefix+"/"+method+"/list/"+i+"/out",args[1].ToString()); }
                    }
                }
                for (int lookup=0;lookup<(font=="KernOnly"||font=="Neither"||font=="Reverse"?0:font=="KernWithSingle"?1:4);lookup++)
                {
                    Capture(prefix+"/indexed/"+lookup+"/one",Call("GetPairAdjustmentRecords",new[]{typeof(int),typeof(uint)},lookup,glyphs[0]));
                    Capture(prefix+"/indexed/"+lookup+"/list",Call("GetPairAdjustmentRecords",new[]{typeof(int),typeof(List<uint>)},lookup,new List<uint>{glyphs[3],glyphs[0],glyphs[0]}));
                }
                Capture(prefix+"/scalar-advance-control",Call("GetPairAdjustmentRecord",new[]{typeof(uint),typeof(uint)},glyphs[3],glyphs[1]));
                FontEngine.UnloadFontFace();
            }
    }
    void FontAssets()
    {
        gposProjection=true;
        foreach (string font in new[]{"PairOnly","KernOnly","Both","Neither"})
        foreach (int size in new[]{13,37})
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/"+font+".ttf");
            var asset=UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(source,size,0,GlyphRenderMode.SMOOTH,64,64,UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic,false);
            typeof(UnityEngine.TextCore.Text.FontAsset).GetMethod("ImportFontFeatures",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).Invoke(asset,null);
            object table=asset.fontFeatureTable;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var property=table.GetType().GetProperty("glyphPairAdjustmentRecords",flags);
            var pairs=property!=null?property.GetValue(table):table.GetType().GetField("glyphPairAdjustmentRecords",flags).GetValue(table);
            var records=new List<GlyphPairAdjustmentRecord>();
            foreach(var record in (System.Collections.IEnumerable)pairs)records.Add((GlyphPairAdjustmentRecord)record);
            Capture("asset/"+font+"/"+size+"/pairs",records.ToArray());
            FontEngine.TryGetGlyphIndex('A',out uint a);FontEngine.TryGetGlyphIndex('V',out uint v);
            Capture("asset/"+font+"/"+size+"/legacy",Call("GetGlyphPairAdjustmentRecord",new[]{typeof(uint),typeof(uint)},a,v));
            UnityEngine.Object.DestroyImmediate(asset);
            FontEngine.UnloadFontFace();
        }
    }

    void LegacyFormats()
    {
        foreach (string font in new[]{"KernClass2", "KernClass3"})
        foreach (int size in new[]{13,37})
        {
            string prefix="formats/"+font+"/"+size;
            Write(prefix+"/load",FontEngine.LoadFontFace(Path.Combine(Application.dataPath,"Fonts",font+".ttf"),size).ToString());
            FontEngine.TryGetGlyphIndex('A',out uint a);FontEngine.TryGetGlyphIndex('V',out uint v);
            gposProjection=false;
            Capture(prefix+"/scalar",Call("GetGlyphPairAdjustmentRecord",new[]{typeof(uint),typeof(uint)},a,v));
            Capture(prefix+"/table",Call("GetGlyphPairAdjustmentTable",new[]{typeof(uint[])},new[]{a,v}));
            var args=new object[]{a,0};
            Capture(prefix+"/one",Call("GetGlyphPairAdjustmentRecords",new[]{typeof(uint),typeof(int).MakeByRefType()},args));
            Write(prefix+"/one/out",args[1].ToString());
            gposProjection=true;
            Capture(prefix+"/fallback",Call("GetPairAdjustmentRecords",new[]{typeof(List<uint>)},new List<uint>{a,v}));
            FontEngine.UnloadFontFace();
        }
    }

    void Start()
    {
        try
        {
            output=new StreamWriter(Environment.GetEnvironmentVariable("FONT_FEATURES_OUT"));
            FontEngine.InitializeFontEngine();
            foreach (string font in new[] { "KernOnly", "PairOnly", "Both", "Neither", "Reverse" })
            foreach (int size in new[] { 13,37 })
            {
                string prefix=font+"/"+size;
                Write(prefix+"/load",FontEngine.LoadFontFace(Path.Combine(Application.dataPath,"Fonts",font+".ttf"),size).ToString());
                var glyphs=new List<uint>();
                foreach (char c in "AVaTilft") { FontEngine.TryGetGlyphIndex(c,out uint glyph); glyphs.Add(glyph); Write(prefix+"/glyph/"+c,glyph.ToString()); }
                foreach (uint glyph in new[] { glyphs[0],glyphs[1],glyphs[3],0u,65536u,glyphs[0]+65536u })
                {
                    var args=new object[]{glyph,0};
                    Capture(prefix+"/one/"+glyph,Call("GetGlyphPairAdjustmentRecords",new[]{typeof(uint),typeof(int).MakeByRefType()},args));
                    Write(prefix+"/one/"+glyph+"/out",args[1].ToString());
                }
                var lists=new[] { glyphs,new List<uint>{glyphs[1],glyphs[3],glyphs[0]},new List<uint>{glyphs[0],glyphs[1],glyphs[0],glyphs[1],glyphs[3]},new List<uint>{glyphs[3],glyphs[0]},new List<uint>{0,glyphs[0],glyphs[1]},new List<uint>(),new List<uint>{65536,glyphs[0],glyphs[1]} };
                for (int i=0;i<lists.Length;i++)
                {
                    var args=new object[]{lists[i],0};
                    Capture(prefix+"/list/"+i,Call("GetGlyphPairAdjustmentRecords",new[]{typeof(List<uint>),typeof(int).MakeByRefType()},args));
                    Write(prefix+"/list/"+i+"/out",args[1].ToString());
                    Capture(prefix+"/table/"+i,Call("GetGlyphPairAdjustmentTable",new[]{typeof(uint[])},lists[i].ToArray()));
                    Capture(prefix+"/new/"+i,Call("GetGlyphPairAdjustmentRecords",new[]{typeof(List<uint>),typeof(List<uint>)},lists[i],glyphs));
                }
                foreach (string method in new[] { "GetGlyphPairAdjustmentRecord" })
                foreach (var pair in new[] { new[]{glyphs[0],glyphs[1]},new[]{glyphs[3],glyphs[1]},new[]{glyphs[0],glyphs[2]},new[]{glyphs[0],glyphs[0]} })
                    Capture(prefix+"/"+method+"/"+pair[0]+"/"+pair[1],Call(method,new[]{typeof(uint),typeof(uint)},pair[0],pair[1]));
                Capture(prefix+"/legacy-table",Call("GetGlyphPairAdjustmentTable",new[]{typeof(uint[])},glyphs.ToArray()));
                Capture(prefix+"/legacy-new",Call("GetGlyphPairAdjustmentRecords",new[]{typeof(List<uint>),typeof(List<uint>)},new List<uint>{glyphs[0],glyphs[3]},glyphs));
                if (font=="PairOnly"||font=="Both")
                {
                    var prime=new List<uint>(); for(int j=0;j<20;j++)prime.Add(glyphs[0]);
                    var gpos=Call("GetPairAdjustmentRecords",new[]{typeof(List<uint>)},prime);
                    var legacy=Call("GetGlyphPairAdjustmentTable",new[]{typeof(uint[])},glyphs.ToArray());
                    Write(prefix+"/shared-buffer",ReferenceEquals(gpos,legacy).ToString());
                    Capture(prefix+"/after-gpos",legacy);
                }
                FontEngine.UnloadFontFace();
                Capture(prefix+"/unloaded",Call("GetGlyphPairAdjustmentTable",new[]{typeof(uint[])},glyphs.ToArray()));
            }
            foreach (string kind in new[]{"table", "list", "new", "all"})
            {
                string method=kind=="table"?"GetGlyphPairAdjustmentTable":"GetGlyphPairAdjustmentRecords";
                var types=kind=="table"?new[]{typeof(uint[])}:kind=="list"?new[]{typeof(List<uint>),typeof(int).MakeByRefType()}:new[]{typeof(List<uint>),typeof(List<uint>)};
                object[] args=kind=="table"?new object[]{null}:kind=="list"?new object[]{null,0}:kind=="new"?new object[]{null,new List<uint>{2,4}}:new object[]{new List<uint>{2,4},null};
                try { Capture("null/"+kind,Call(method,types,args)); } catch(TargetInvocationException e) { Write("null/"+kind,e.InnerException.GetType().Name); }
            }
            Gpos();
            FontAssets();
            LegacyFormats();
            Write("complete","1");
        }
        catch (Exception e) { Write("error",e.ToString());Debug.LogException(e); }
        finally { output.Dispose(); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
