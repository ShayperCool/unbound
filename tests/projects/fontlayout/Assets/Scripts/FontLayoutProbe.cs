using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
public sealed class FontLayoutProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("FontLayoutProbe").AddComponent<FontLayoutProbe>(); }
    [Serializable] sealed class Row { public string key; public string value; }
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    StreamWriter output;
    static object Get(object o,string name) { var t=o.GetType();var f=t.GetField(name,Any);return f!=null?f.GetValue(o):t.GetProperty(name,Any).GetValue(o); }
    static void Set(object o,string name,object value) { var t=o.GetType();var f=t.GetField(name,Any);if(f!=null)f.SetValue(o,value);else t.GetProperty(name,Any).SetValue(o,value); }
    static object Call(object o,string name,params object[] args) {
        Type type=o as Type??o.GetType();
        foreach(var m in type.GetMethods(Any)) { if(m.Name!=name)continue;var p=m.GetParameters();if(p.Length!=args.Length)continue;bool fits=true;for(int i=0;i<p.Length;i++)if(args[i]!=null&&!p[i].ParameterType.IsInstanceOfType(args[i]))fits=false;if(fits)return m.Invoke(o is Type?null:o,args); }
        throw new MissingMethodException(type.FullName,name);
    }
    static string Bits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value),0).ToString("X8");
    static string Value(object value) {
        if(value==null)return "null";
        if(value is float f)return Bits(f);
        if(value is Vector2 v2)return Bits(v2.x)+","+Bits(v2.y);
        if(value is Vector3 v3)return Bits(v3.x)+","+Bits(v3.y)+","+Bits(v3.z);
        if(value is Color32 c)return c.r+","+c.g+","+c.b+","+c.a;
        if(value is uint[] ids)return string.Join(",",ids);
        return Convert.ToString(value,CultureInfo.InvariantCulture);
    }
    void Write(string key,object value) { output.WriteLine(JsonUtility.ToJson(new Row { key=key,value=Value(value) })); output.Flush(); }
    void Fields(string key,object value,params string[] fields) { foreach(var field in fields)Write(key+"/"+field,Get(value,field)); }
    static FontAsset Asset(string font,int sampling=24,int atlas=1024) => FontAsset.CreateFontAsset(Path.Combine(Application.dataPath,"Fonts",font+".ttf"),0,sampling,4,GlyphRenderMode.SDFAA,atlas,atlas);
    void Tables(string key,FontAsset asset) {
        var table=Get(asset,"fontFeatureTable");
        var ligatures=(IList)Get(table,"ligatureRecords");Write(key+"/ligatures/count",ligatures.Count);
        for(int i=0;i<ligatures.Count;i++)Fields(key+"/ligatures/"+i,ligatures[i],"componentGlyphIDs","ligatureGlyphID");
        foreach(bool mark in new[]{false,true}) {
            string name=mark?"MarkToMarkAdjustmentRecords":"MarkToBaseAdjustmentRecords",p=key+"/"+name;
            var records=(IList)Get(table,name);Write(p+"/count",records.Count);
            for(int i=0;i<records.Count;i++) {
                object r=records[i];string q=p+"/"+i;
                Fields(q,r,mark?"baseMarkGlyphID":"baseGlyphID",mark?"combiningMarkGlyphID":"markGlyphID");
                Fields(q+"/anchor",Get(r,mark?"baseMarkGlyphAnchorPoint":"baseGlyphAnchorPoint"),"xCoordinate","yCoordinate");
                Fields(q+"/adjustment",Get(r,mark?"combiningMarkPositionAdjustment":"markPositionAdjustment"),"xPositionAdjustment","yPositionAdjustment");
            }
        }
    }
    void Case(string key,object generator,object info,TextSettings settings,FontAsset font,string text,int size,bool wrap,string style=null) {
        var type=typeof(FontAsset).Assembly.GetType("UnityEngine.TextCore.Text.TextGenerationSettings");
        var s=Activator.CreateInstance(type,true);Set(s,"text",text);Set(s,"fontAsset",font);Set(s,"textSettings",settings);Set(s,"fontSize",(float)size);Set(s,"screenRect",new Rect(0,0,wrap?20:120,150));Set(s,"richText",true);Set(s,"color",Color.white);
        var field=type.GetField("textWrappingMode",Any);Set(s,"textWrappingMode",Enum.Parse(field.FieldType,wrap?"Normal":"NoWrap"));
        if(style!=null)Set(s,"fontStyle",Enum.Parse(type.GetField("fontStyle",Any).FieldType,style));
        Write(key+"/text",text);Write(key+"/cold-preferred",Call(generator,"GetPreferredValues",s,info));Call(typeof(FontAsset),"UpdateFontAssetsInUpdateQueue");Write(key+"/preferred",Call(generator,"GetPreferredValues",s,info));Call(generator,"GenerateText",s,info);
        Fields(key,info,"characterCount","lineCount","materialCount");
        var chars=(Array)Get(info,"textElementInfo");int count=(int)Get(info,"characterCount");
        for(int i=0;i<count;i++)Fields(key+"/char/"+i,chars.GetValue(i),"character","index","stringLength","lineNumber","isVisible","origin","xAdvance","ascender","descender","baseLine","bottomLeft","topRight","scale","pointSize");
        int materials=(int)Get(info,"materialCount");var meshes=(Array)Get(info,"meshInfo");
        for(int i=0;i<materials;i++) {
            object mesh=meshes.GetValue(i);int n=(int)Get(mesh,"vertexCount");Write(key+"/mesh/"+i+"/count",n);var vertices=(Array)Get(mesh,"vertexData");
            for(int j=0;j<n;j++)Fields(key+"/mesh/"+i+"/"+j,vertices.GetValue(j),"position","uv0","uv2","color");
        }
    }
    void Start() {
        try {
            output=new StreamWriter(Environment.GetEnvironmentVariable("FONT_FEATURES_OUT"));var assembly=typeof(FontAsset).Assembly;
            var settings=ScriptableObject.CreateInstance<TextSettings>();object generator=Activator.CreateInstance(assembly.GetType("UnityEngine.TextCore.Text.TextGenerator"),true),info=Activator.CreateInstance(assembly.GetType("UnityEngine.TextCore.Text.TextInfo"),true);
            var featureField=assembly.GetType("UnityEngine.TextCore.Text.TextGenerationSettings").GetField("fontFeatures",Any);var features=(IList)featureField.GetValue(null);var featureType=features.GetType().GetGenericArguments()[0];
            foreach(string font in new[]{"Controlled","DejaVu","Marks"}) {
                var all=Asset(font);Call(all,"ImportFontFeatures");Tables(font+"/import-all",all);
                var partial=Asset(font);partial.TryAddCharacters("A",true);Tables(font+"/seed-A",partial);Call(partial,"ImportFontFeatures");Tables(font+"/seed-A-then-import",partial);
                var disabled=Asset(font);disabled.TryAddCharacters("Aafilst \u0300\u0301\u0327",false);Tables(font+"/without-features",disabled);
                var reset=Asset(font);reset.TryAddCharacters("Aafilst \u0300\u0301\u0327",true);reset.ReadFontAssetDefinition();Tables(font+"/rebuilt-lookups",reset);reset.ClearFontAssetData();Tables(font+"/cleared",reset);reset.TryAddCharacters("Aafilst \u0300\u0301\u0327",true);Tables(font+"/repopulated",reset);
                var asset=Asset(font);Write(font+"/added",asset.TryAddCharacters("Aafilst \u0300\u0301\u0327",true));Fields(font+"/face",asset.faceInfo,"familyName","pointSize","unitsPerEM");Tables(font+"/dynamic",asset);
                var table=Get(asset,"fontFeatureTable");Call(table,"SortMarkToBaseAdjustmentRecords");Call(table,"SortMarkToMarkAdjustmentRecords");Tables(font+"/sorted",asset);
                int mode=0;
                foreach(string[] tags in new[]{new string[0],new[]{"liga"},new[]{"mark"},new[]{"mkmk"},new[]{"liga","mark","mkmk"}}) {
                    features.Clear();foreach(string tag in tags)features.Add(Enum.Parse(featureType,tag));Write(font+"/mode/"+mode,string.Join(",",tags));int id=0;
                    foreach(int size in new[]{18,17})foreach(string text in new[]{"fi","ffi","fl","AfiA","aA\u0301","A\u0301\u0300","f\u0327","A\u0327","f\u0301\u0300","f<i>i</i>","fi\nfl","<size=130%>A\u0301\u0300</size>"})Case(font+"/mode/"+mode+"/"+id++,generator,info,settings,asset,text,size,false);
                    Case(font+"/mode/"+mode+"/wrap",generator,info,settings,asset,"A\u0301\u0300 A\u0301\u0300 A\u0301\u0300",18,true);
                    Case(font+"/mode/"+mode+"/chain",generator,info,settings,asset,"A"+new string('\u0327',32)+"\u0301",18,false);mode++;
                }
            }
            generator=Activator.CreateInstance(assembly.GetType("UnityEngine.TextCore.Text.TextGenerator"),true);info=Activator.CreateInstance(assembly.GetType("UnityEngine.TextCore.Text.TextInfo"),true);
            foreach(string font in new[]{"Controlled","DejaVu"}) {
                var small=Asset(font,24,64);small.TryAddCharacters("fi",true);small.TryAddCharacters(" AafilstAVTo\u0300\u0301\u0302\u0308\u0323\u0327",true);
                features.Clear();features.Add(Enum.Parse(featureType,"liga"));
                Write(font+"/small/f-atlas",small.characterLookupTable['f'].glyph.atlasIndex);Write(font+"/small/atlas-count",small.atlasTextureCount);
                Case(font+"/small/fi",generator,info,settings,small,"fi",18,false);
                var chars=(Array)Get(info,"textElementInfo");object first=chars.GetValue(0),alt=Get(first,"alternativeGlyph");
                Write(font+"/small/alternative-atlas",alt==null?-1:(int)Get(alt,"atlasIndex"));Write(font+"/small/alternative-id",alt==null?0:(uint)Get(alt,"index"));
                Write(font+"/small/material-texture",((Material)Get(first,"material")).mainTexture.name);Write(font+"/small/atlas0",small.atlasTextures[0].name);
                var normal=Asset(font);normal.TryAddCharacters(" AafilstAVTo\u0300\u0301\u0302\u0308\u0323\u0327\ufb01",true);
                Case(font+"/lowercase",generator,info,settings,normal,"FI",18,false,"LowerCase");
                features.Add(Enum.Parse(featureType,"kern"));
                foreach(string text in new[]{"fiV","Tfi","fiA","fi\u0301","\ufb01V"})Case(font+"/kern/"+text,generator,info,settings,normal,text,18,false);
                features.Add(Enum.Parse(featureType,"mark"));features.Add(Enum.Parse(featureType,"mkmk"));
                Case(font+"/ligature-mark",generator,info,settings,normal,"fi\u0301\u0300",18,false);
            }
            Write("complete","1");
        } catch(Exception e) {if(output!=null)Write("error",e.ToString());Debug.LogException(e);}finally {if(output!=null)output.Dispose();}
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
