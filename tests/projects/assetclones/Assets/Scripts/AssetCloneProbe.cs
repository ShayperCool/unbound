using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

public class CloneControl : ScriptableObject { public int value; }

public class AssetCloneProbe : MonoBehaviour
{
    readonly StringBuilder output = new StringBuilder();
    Material sample;
    RenderTexture pixel;
    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\0", "\\u0000");
    void V(string k, object v) => output.Append("{\"k\":\"").Append(Escape(k)).Append("\",\"v\":\"").Append(Escape(Convert.ToString(v, CultureInfo.InvariantCulture))).Append("\"}\n");
    void Try(string k, Action f) { try { f(); V(k, "ok"); } catch (Exception e) { V(k, e.GetType().Name); } }
    void ColorValue(string k, Color c) { for (int i=0;i<4;i++) V(k+"."+i,c[i].ToString("R",CultureInfo.InvariantCulture)); }
    void Identity(string k, Object a, Object b)
    {
        V(k+".type",b.GetType().FullName); V(k+".name",b.name); V(k+".distinct",a!=b && !ReferenceEquals(a,b) && a.GetInstanceID()!=b.GetInstanceID());
        V(k+".flags",(int)b.hideFlags); V(k+".persistent",EditorUtility.IsPersistent(b)); V(k+".path",AssetDatabase.GetAssetPath(b));
        V(k+".source_persistent",EditorUtility.IsPersistent(a)); V(k+".source_path",AssetDatabase.GetAssetPath(a));
    }
    void TextureInfo(string k, Texture2D t)
    {
        V(k+".width",t.width); V(k+".height",t.height); V(k+".format",t.format); V(k+".graphics",t.graphicsFormat); V(k+".mips",t.mipmapCount);
        V(k+".readable",t.isReadable); V(k+".data_color",t.isDataSRGB); V(k+".alpha",t.alphaIsTransparency);
        Sampler(k,t);
        Try(k+".read",()=>{ for(int m=0;m<t.mipmapCount;m++) { var c=t.GetPixels(m); for(int i=0;i<c.Length;i++) ColorValue(k+".cpu.m"+m+".p"+i,c[i]); } });
        Try(k+".raw",()=>V(k+".bytes",Convert.ToBase64String(t.GetRawTextureData())));
    }
    void Sampler(string k, Texture t) { V(k+".filter",t.filterMode); V(k+".u",t.wrapModeU); V(k+".v",t.wrapModeV); V(k+".w",t.wrapModeW); V(k+".aniso",t.anisoLevel); V(k+".bias",t.mipMapBias); }
    Color Read(RenderTexture rt)
    {
        var old=RenderTexture.active; RenderTexture.active=rt;
        var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false); var c=t.GetPixel(0,0); Object.DestroyImmediate(t); RenderTexture.active=old; return c;
    }
    void Gpu(string k, Texture t, int mips)
    {
        for(int m=0;m<mips;m++) { sample.SetFloat("_Level",m); Graphics.Blit(t,pixel,sample); ColorValue(k+".gpu.m"+m,Read(pixel)); }
    }
    void TextureCase(string k, Texture2D a)
    {
        V(k+".source_flags",(int)a.hideFlags); TextureInfo(k+".source",a); Gpu(k+".source",a,a.mipmapCount);
        Texture2D b=null;
        Try(k+".instantiate",()=>b=Object.Instantiate(a));
        if(b==null)return;
        Identity(k,a,b); TextureInfo(k+".clone",b); Gpu(k+".clone",b,b.mipmapCount);
        b.name="independent"; b.filterMode=FilterMode.Trilinear; b.wrapModeU=TextureWrapMode.Mirror; b.anisoLevel=7; b.mipMapBias=-0.5f;
        Sampler(k+".source_after_sampler",a); Sampler(k+".clone_after_sampler",b);
        if(b.isReadable) { b.SetPixel(0,0,Color.magenta); b.Apply(false); ColorValue(k+".source_after_write",a.GetPixel(0,0)); ColorValue(k+".clone_after_write",b.GetPixel(0,0)); }
        Gpu(k+".source_after_write",a,a.mipmapCount); Gpu(k+".clone_after_write",b,b.mipmapCount);
        Object.DestroyImmediate(b); V(k+".destroyed",b==null); V(k+".source_live",a!=null); Gpu(k+".source_after_destroy",a,a.mipmapCount);
    }
    void RtInfo(string k, RenderTexture t)
    {
        var d=t.descriptor;
        V(k+".width",d.width); V(k+".height",d.height); V(k+".graphics",d.graphicsFormat); V(k+".depth",d.depthStencilFormat); V(k+".stencil",d.stencilFormat);
        V(k+".mipcount",d.mipCount); V(k+".mips",d.useMipMap); V(k+".auto",d.autoGenerateMips); V(k+".msaa",d.msaaSamples); V(k+".bind",d.bindMS);
        V(k+".dimension",d.dimension); V(k+".volume",d.volumeDepth); V(k+".random",d.enableRandomWrite); V(k+".dynamic",d.useDynamicScale); V(k+".explicit",d.useDynamicScaleExplicit);
        V(k+".memoryless",d.memoryless); V(k+".vr",d.vrUsage); V(k+".shadow",d.shadowSamplingMode); V(k+".srgb",t.sRGB); V(k+".created",t.IsCreated()); Sampler(k,t);
    }
    void Clear(RenderTexture rt, Color c) { RenderTexture.active=rt; GL.Clear(true,true,c); }
    void RtCase(string k, RenderTexture a, bool initialized)
    {
        var old=RenderTexture.active;
        if(initialized) Clear(a,new Color(0.25f,0.5f,0.75f,1));
        RtInfo(k+".source",a); var active=RenderTexture.active;
        RenderTexture b=null; Try(k+".instantiate",()=>b=Object.Instantiate(a));
        V(k+".active_preserved",RenderTexture.active==active);
        if(b==null){RenderTexture.active=old;return;}
        Identity(k,a,b); RtInfo(k+".clone",b);
        // A clone's initial GPU storage is undefined. Record creation, then initialize before reading.
        Clear(b,Color.magenta); ColorValue(k+".clone_initialized.gpu",Read(b));
        if(initialized)ColorValue(k+".source_preserved.gpu",Read(a));
        b.Release(); V(k+".released",b.IsCreated()); V(k+".active_after_release",RenderTexture.active==b);
        Clear(b,Color.green); ColorValue(k+".recreated.gpu",Read(b));
        RenderTexture.active=old; Object.DestroyImmediate(b); V(k+".destroyed",b==null); V(k+".source_live",a!=null); V(k+".source_created",a.IsCreated());
        if(initialized)ColorValue(k+".source_after_destroy.gpu",Read(a));
    }
    void TextCase(string k, TextAsset a)
    {
        V(k+".source_bytes",Convert.ToBase64String(a.bytes)); V(k+".source_text",a.text);
        TextAsset b=null; Try(k+".instantiate",()=>b=Object.Instantiate(a)); if(b==null)return;
        Identity(k,a,b); V(k+".bytes",Convert.ToBase64String(b.bytes)); V(k+".text",b.text); V(k+".size",b.dataSize);
        var bytes=b.bytes; if(bytes.Length>0)bytes[0]^=255; V(k+".bytes_after_array_write",Convert.ToBase64String(b.bytes)); V(k+".source_after_array_write",Convert.ToBase64String(a.bytes));
        var data=b.GetData<byte>(); V(k+".view_length",data.Length); b.name="independent"; V(k+".source_name",a.name);
        Object.DestroyImmediate(b); V(k+".destroyed",b==null); Try(k+".destroyed_view",()=>V(k+".destroyed_view_byte",data[0]));
        V(k+".source_live",a!=null); V(k+".source_after_destroy",Convert.ToBase64String(a.bytes));
    }
    void Overloads(string k, Object a)
    {
        var parent=new GameObject("parent");
        Try(k+".plain",()=>Object.DestroyImmediate(Object.Instantiate(a)));
        Try(k+".null_parent",()=>Object.DestroyImmediate(Object.Instantiate(a,(Transform)null)));
        Try(k+".parent",()=>Object.DestroyImmediate(Object.Instantiate(a,parent.transform)));
        Try(k+".parent_world",()=>Object.DestroyImmediate(Object.Instantiate(a,parent.transform,true)));
        Try(k+".position",()=>Object.DestroyImmediate(Object.Instantiate(a,Vector3.one,Quaternion.identity)));
        Try(k+".position_parent",()=>Object.DestroyImmediate(Object.Instantiate(a,Vector3.one,Quaternion.identity,parent.transform)));
        Object.DestroyImmediate(parent);
    }
    void Start()
    {
        sample=new Material(Shader.Find("Probe/MipLevels")); pixel=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
        foreach(var fmt in new[]{TextureFormat.RGBA32,TextureFormat.RGB24,TextureFormat.RGBAHalf,TextureFormat.RFloat})
        foreach(bool readable in new[]{true,false})
        {
            var a=new Texture2D(4,2,fmt,true,true){name="runtime",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapModeU=TextureWrapMode.Clamp,wrapModeV=TextureWrapMode.Mirror,anisoLevel=3,mipMapBias=0.25f};
            for(int m=0;m<a.mipmapCount;m++){var c=new Color[Math.Max(1,4>>m)*Math.Max(1,2>>m)]; for(int i=0;i<c.Length;i++)c[i]=new Color(0.25f*(m+1),0.5f,0.75f,1); a.SetPixels(c,m);}
            a.Apply(false,!readable); TextureCase("texture."+fmt+"."+readable,a); Object.DestroyImmediate(a);
        }
        var dirty=new Texture2D(2,2,TextureFormat.RGBA32,false,true){name="dirty"}; dirty.SetPixels(new[]{Color.red,Color.red,Color.red,Color.red}); dirty.Apply(); dirty.SetPixel(0,0,Color.green);
        TextureCase("texture.dirty",dirty); Overloads("overload.texture",dirty); Object.DestroyImmediate(dirty);
        // GPU-only changes distinguish a real storage copy from an upload of stale CPU bytes.
        foreach(bool readable in new[]{true,false})
        {
            var src=new Texture2D(2,2,TextureFormat.RGBA32,false,true); src.SetPixels(new[]{Color.green,Color.green,Color.green,Color.green}); src.Apply(false,true);
            var dst=new Texture2D(2,2,TextureFormat.RGBA32,false,true){name="gpu-write"}; dst.SetPixels(new[]{Color.red,Color.red,Color.red,Color.red}); dst.Apply(false,!readable);
            Graphics.CopyTexture(src,dst); TextureCase("texture.gpu_write."+readable,dst); Object.DestroyImmediate(src); Object.DestroyImmediate(dst);
        }
        foreach(var name in new[]{"Readable","Unreadable"}) TextureCase("import."+name,Resources.Load<Texture2D>(name));
        var rt=new RenderTexture(4,2,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){name="target",hideFlags=HideFlags.HideAndDontSave,useMipMap=true,autoGenerateMips=false,filterMode=FilterMode.Point,wrapModeU=TextureWrapMode.Repeat,anisoLevel=2,mipMapBias=0.25f};
        RtCase("rt.uncreated",rt,false); RtCase("rt.created",rt,true); Overloads("overload.rt",rt); rt.Release(); Object.DestroyImmediate(rt);
        RtCase("rt.import",Resources.Load<RenderTexture>("Target"),false);
        var text=new TextAsset("text ☃\0end"){name="runtime-text",hideFlags=HideFlags.HideAndDontSave}; TextCase("text.runtime",text); Overloads("overload.text",text); Object.DestroyImmediate(text);
        TextCase("text.import",Resources.Load<TextAsset>("Message"));
        TextCase("text.bom_text",Resources.Load<TextAsset>("Bom"));
        var bom=new TextAsset("\uFEFFbom"); TextCase("text.bom_runtime",bom); Object.DestroyImmediate(bom);
        foreach(var encoding in new[]{"Utf8","Utf8Bom","Utf16Le","Utf16LeBom","Utf16Be","Utf16BeBom","Utf32LeBom","Utf32BeBom","Invalid"})
        foreach(var suffix in new[]{"Txt","Bytes"})
        {
            var asset=Resources.Load<TextAsset>(encoding+suffix); TextCase("encoding.import."+encoding+suffix,asset);
            var bytes=new TextAsset(new ReadOnlySpan<byte>(asset.bytes)); TextCase("encoding.bytes."+encoding+suffix,bytes); Object.DestroyImmediate(bytes);
        }
        var bomString=new TextAsset("\uFEFFalpha\0Ω\n"); TextCase("encoding.string.bom",bomString); Object.DestroyImmediate(bomString);
        Overloads("overload.null",null);
        var dead=new Texture2D(2,2,TextureFormat.RGBA32,false,true); dead.SetPixels(new[]{Color.cyan,Color.cyan,Color.cyan,Color.cyan}); dead.Apply();
        Try("lifetime.texture.instantiate",()=>{ var c=Object.Instantiate(dead); Object.DestroyImmediate(dead); Gpu("lifetime.texture.clone",c,1); ColorValue("lifetime.texture.cpu",c.GetPixel(0,0)); Object.DestroyImmediate(c); });
        var deadText=new TextAsset("survives"); Try("lifetime.text.instantiate",()=>{var c=Object.Instantiate(deadText);Object.DestroyImmediate(deadText);V("lifetime.text.clone",c.text);Object.DestroyImmediate(c);});
        var deadRt=new RenderTexture(2,2,0); Clear(deadRt,Color.red);
        Try("lifetime.rt.instantiate",()=>{var c=Object.Instantiate(deadRt);RenderTexture.active=null;Object.DestroyImmediate(deadRt);Clear(c,Color.cyan);ColorValue("lifetime.rt.clone.gpu",Read(c));RenderTexture.active=null;Object.DestroyImmediate(c);});
        var so=ScriptableObject.CreateInstance<CloneControl>(); so.value=73; so.name="control"; var soc=Object.Instantiate(so); Identity("control.scriptable",so,soc); V("control.scriptable.value",soc.value); Object.DestroyImmediate(so); Object.DestroyImmediate(soc);
        var mat=new Material(Shader.Find("Unlit/Color")){name="material"}; mat.color=Color.cyan; var mc=Object.Instantiate(mat); Identity("control.material",mat,mc); ColorValue("control.material.color",mc.color); Object.DestroyImmediate(mat); Object.DestroyImmediate(mc);
        var go=new GameObject("control"); go.transform.position=Vector3.one; var gc=Object.Instantiate(go); V("control.go.name",gc.name); V("control.go.position",gc.transform.position==Vector3.one); Object.DestroyImmediate(go); Object.DestroyImmediate(gc);
        RenderTexture.active=null; pixel.Release(); Object.DestroyImmediate(pixel); Object.DestroyImmediate(sample);
        var path=Environment.GetEnvironmentVariable("ASSET_CLONE_OUT"); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path,output.ToString());
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
