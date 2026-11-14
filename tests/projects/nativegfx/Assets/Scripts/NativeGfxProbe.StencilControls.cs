using System;
using UnityEngine;
public partial class NativeGfxProbe {
 void StencilControls() {
 foreach(int samples in new[]{1,2,4,8}) {
 var rt=new RenderTexture(8,4,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear){antiAliasing=samples};rt.Create();
 var previous=RenderTexture.active;RenderTexture.active=rt;GL.Clear(true,true,Color.black,.75f);RenderTexture.active=previous;
 for(int i=0;i<32;i++)NativeGfx_SetShape(i,"",IntPtr.Zero);NativeGfx_SetShape(1,"depth",rt.GetNativeDepthBufferPtr());
 string key="device.stencilResolve."+samples;
 Logged(key+".initialize",()=>Value(key+".initialize",L(TextureEvent(7))));
 Logged(key+".initialized",()=>Value(key+".initialized",L(TextureEvent(6))));
 RenderTexture.active=rt;GL.Clear(true,true,Color.black,.625f);RenderTexture.active=previous;
 Logged(key+".clear",()=>Value(key+".clear",L(TextureEvent(6))));
 var material=new Material(Shader.Find("Probe/NativeGfx/MsaaStencil"));Graphics.Blit(Texture2D.whiteTexture,rt,material);
 Logged(key+".draw",()=>Value(key+".draw",L(TextureEvent(6))));
 rt.ResolveAntiAliasedSurface();Logged(key+".self",()=>Value(key+".self",L(TextureEvent(6))));
 material.SetInt("_Ref",91);Graphics.Blit(Texture2D.whiteTexture,rt,material);
 Logged(key+".redraw",()=>Value(key+".redraw",L(TextureEvent(6))));
 rt.ResolveAntiAliasedSurface();Logged(key+".again",()=>Value(key+".again",L(TextureEvent(6))));
 RenderTexture.active=previous;
 DestroyImmediate(material);DestroyImmediate(rt);
 }
 }
}
