using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
static class MsaaControls
{
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string ColorValue(Color c) => "[" + c.r.ToString("G9",CultureInfo.InvariantCulture) + "," + c.g.ToString("G9",CultureInfo.InvariantCulture) + "," + c.b.ToString("G9",CultureInfo.InvariantCulture) + "," + c.a.ToString("G9",CultureInfo.InvariantCulture) + "]";
    static Color Read(RenderTexture source)
    {
        var previous = RenderTexture.active;
        var cpu = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
        RenderTexture.active=source; cpu.ReadPixels(new Rect(0,0,1,1),0,0);
        RenderTexture.active=previous; Color c=cpu.GetPixel(0,0); UnityEngine.Object.DestroyImmediate(cpu); return c;
    }
    static void Sample(Action<string,string> row, string key, RenderTexture source, bool bound)
    {
        var previous=RenderTexture.active;
        var material=new Material(Shader.Find(bound ? "Probe/NativeGfx/MsaaRead" : "Probe/NativeGfx/MsaaSingle"));
        material.SetTexture("_Samples",source);
        var output=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);output.Create();
        Graphics.Blit(Texture2D.whiteTexture,output,material); row(key,ColorValue(Read(output)));
        RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(output);UnityEngine.Object.DestroyImmediate(material);
    }
    static void Compute(Action<string,string> row, string key, RenderTexture source)
    {
        var shader=Resources.Load<ComputeShader>("MsaaCompute");
        var buffer=new ComputeBuffer(2,16);
        shader.SetTexture(0,"_Samples",source);shader.SetTexture(0,"_Depth",source,0,RenderTextureSubElement.Depth);
        shader.SetBuffer(0,"_Out",buffer);shader.Dispatch(0,1,1,1);
        var colors=new Color[2];buffer.GetData(colors);row(key+".color",ColorValue(colors[0]));row(key+".depth",ColorValue(colors[1]));buffer.Release();
    }
    static void Depth(Action<string,string> row,string key,RenderTexture source)
    {
        var previous=RenderTexture.active;
        var material=new Material(Shader.Find("Probe/NativeGfx/MsaaDepth"));material.SetTexture("_Samples",source,RenderTextureSubElement.Depth);
        var output=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);output.Create();
        Graphics.Blit(Texture2D.whiteTexture,output,material);row(key,ColorValue(Read(output)));
        RenderTexture.active=previous;
        UnityEngine.Object.DestroyImmediate(output);UnityEngine.Object.DestroyImmediate(material);
    }
    static void KernelElements(Action<string,string> row, RenderTexture source)
    {
        var shader=Resources.Load<ComputeShader>("MsaaCompute");
        int a=shader.FindKernel("SelectA"), b=shader.FindKernel("SelectB");
        var output=new ComputeBuffer(2,16);shader.SetBuffer(a,"_Out",output);shader.SetBuffer(b,"_Out",output);
        for(int mode=0;mode<3;mode++)
        {
            shader.SetTexture(a,"_Selected",source,0,mode==2 ? RenderTextureSubElement.Depth : RenderTextureSubElement.Color);
            shader.SetTexture(b,"_Selected",source,0,mode==0 ? RenderTextureSubElement.Depth : RenderTextureSubElement.Color);
            shader.Dispatch(a,1,1,1);shader.Dispatch(b,1,1,1);
            var values=new Color[2];output.GetData(values);
            row("device.kernelElements."+mode+".a",ColorValue(values[0]));
            row("device.kernelElements."+mode+".b",ColorValue(values[1]));
        }
        output.Release();
    }
    public static void Run(Action<string,string> row)
    {
        foreach(int samples in new[]{2,4,8}) foreach(bool bound in new[]{false,true})
        {
            string key="device.colorResolve."+samples+"."+bound;
            var rt=new RenderTexture(8,4,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=samples,bindTextureMS=bound};
            row(key+".created",rt.Create()?"true":"false"); if(!rt.IsCreated()){UnityEngine.Object.DestroyImmediate(rt);continue;}
            var previous=RenderTexture.active;
            RenderTexture.active=rt;GL.Clear(true,true,new Color(.75f,.5f,.25f,1),.75f);RenderTexture.active=previous;
            Sample(row,key+".clear",rt,bound);
            var material=new Material(Shader.Find("Probe/NativeGfx/DeviceSamples"));
            Graphics.Blit(Texture2D.whiteTexture,rt,material);
            Sample(row,key+".draw",rt,bound);
            if(bound){Depth(row,key+".depth",rt);Compute(row,key+".compute",rt);}
            if(bound && samples==4 && SystemInfo.graphicsDeviceType==GraphicsDeviceType.Vulkan)KernelElements(row,rt);
            var dest=new RenderTexture(8,4,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);dest.Create();
            RenderTexture.active=dest;GL.Clear(true,true,Color.green,.625f);RenderTexture.active=previous;
            RenderTexture.active=rt;rt.ResolveAntiAliasedSurface(dest);RenderTexture.active=null;Sample(row,key+".destination",dest,false);Sample(row,key+".afterDestination",rt,bound);
            rt.ResolveAntiAliasedSurface();Sample(row,key+".self",rt,bound);
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(dest);UnityEngine.Object.DestroyImmediate(rt);
        }
        foreach(int sourceSamples in new[]{1,4})
        foreach(int destSamples in new[]{1,4}) foreach(int depth in new[]{0,24}) foreach(bool bound in new[]{false,true})
        {
            if(sourceSamples==1 && bound)continue;
            var previous=RenderTexture.active;
            string key=(sourceSamples==1 ? "device.resolveSourceSingle." : "device.resolveDestination.")+destSamples+"."+depth+"."+bound;
            var src=new RenderTexture(8,4,depth,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=sourceSamples,bindTextureMS=bound};src.Create();
            var dst=new RenderTexture(8,4,depth,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=destSamples};dst.Create();
            src.GetNativeTexturePtr();dst.GetNativeTexturePtr();
            RenderTexture.active=src;GL.Clear(true,true,Color.red,.25f);RenderTexture.active=dst;GL.Clear(true,true,Color.green,.75f);RenderTexture.active=null;
            RenderTexture.active=src;src.ResolveAntiAliasedSurface(dst);RenderTexture.active=null;Sample(row,key,dst,false);
            RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(src);UnityEngine.Object.DestroyImmediate(dst);
        }
    }
}
