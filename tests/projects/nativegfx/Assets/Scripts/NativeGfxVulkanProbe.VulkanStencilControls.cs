using System;
using System.Runtime.InteropServices;
using UnityEngine;
public partial class NativeGfxVulkanProbe
{
    [DllImport(Plugin)] static extern void NativeGfxVk_StencilPrepare(IntPtr pointer,int initialize);
    [DllImport(Plugin)] static extern IntPtr NativeGfxVk_StencilEvent();
    [DllImport(Plugin)] static extern int NativeGfxVk_StencilValue();
    [DllImport(Plugin)] static extern void NativeGfxVk_StencilDestroy();
    void VulkanStencilControls()
    {
        var previousLogs=logs.ToArray(); logs.Clear();
        var previous=RenderTexture.active;
        var fence=new RenderTexture(1,1,0);fence.Create();
        Action<RenderTexture,string> read=(rt,key)=>
        {
            NativeGfxVk_StencilPrepare(rt.GetNativeDepthBufferPtr(),0);
            GL.IssuePluginEvent(NativeGfxVk_StencilEvent(),eventBase+Outside);
            Pixels(fence,1,1);
            Value(key,NativeGfxVk_StencilValue().ToString());
        };
        foreach(int samples in new[]{1,2,4,8})
        {
            var rt=new RenderTexture(8,4,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=samples};rt.Create();
            RenderTexture.active=rt;GL.Clear(true,true,Color.black,.75f);RenderTexture.active=previous;
            NativeGfxVk_StencilPrepare(rt.GetNativeDepthBufferPtr(),1);
            GL.IssuePluginEvent(NativeGfxVk_StencilEvent(),eventBase+Outside);
            Pixels(fence,1,1);
            string key="vulkanStencil."+samples;
            read(rt,key+".initialized");
            RenderTexture.active=rt;GL.Clear(true,true,Color.black,.625f);RenderTexture.active=previous;
            read(rt,key+".clear");
            var material=new Material(Shader.Find("Probe/NativeGfx/MsaaStencil"));
            Graphics.Blit(Texture2D.whiteTexture,rt,material);RenderTexture.active=previous;
            read(rt,key+".draw");
            rt.ResolveAntiAliasedSurface();read(rt,key+".self");
            material.SetInt("_Ref",91);Graphics.Blit(Texture2D.whiteTexture,rt,material);RenderTexture.active=previous;
            read(rt,key+".redraw");
            rt.ResolveAntiAliasedSurface();read(rt,key+".again");
            RenderTexture.active=previous;DestroyImmediate(material);DestroyImmediate(rt);
        }
        NativeGfxVk_StencilDestroy();RenderTexture.active=previous;DestroyImmediate(fence);
        Value("vulkanStencil.logs",L(logs));logs.Clear();logs.AddRange(previousLogs);
    }
}
