using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public class CameraMsaaEffect : MonoBehaviour
{
    void OnRenderImage(RenderTexture source, RenderTexture destination) { CameraMsaaProbe.InspectEffect(source,destination); Graphics.Blit(source,destination); }
}
public class CameraMsaaScreenRead : MonoBehaviour
{
    void OnPostRender() { CameraMsaaProbe.InspectRendering(); CameraMsaaProbe.ReadScreenInFrame(); }
}
public class CameraMsaaProbe : MonoBehaviour
{
    public static CameraMsaaProbe Current;
    public static void InspectRendering()
    {
        var p=Current;if(p==null || p.captureKey==null)return;
        string key=p.captureKey;
        p.Describe(key+".renderAttachment",RenderTexture.active);
        p.Value(key+".pixelRect","["+p.camera.pixelRect.x+","+p.camera.pixelRect.y+","+p.camera.pixelWidth+","+p.camera.pixelHeight+"]");
    }
    string screenKey;
    public static void ReadScreenInFrame()
    {
        var p=Current;if(p==null || p.screenKey==null)return;
        string key=p.screenKey;p.screenKey=null;
        p.Describe(key+".active",RenderTexture.active);
        var saved=p.target;var active=RenderTexture.active;
        if(active!=null){p.target=active;p.ReadVariants(key+".attachment");}
        p.target=null;p.ReadVariants(key);p.target=saved;RenderTexture.active=active;
    }
    string captureKey;
    int effectIndex;
    public static void InspectEffect(RenderTexture source, RenderTexture destination)
    {
        if(Current==null || Current.captureKey==null)return;
        var p=Current;string key=p.captureKey+".fx"+(p.effectIndex++);
        p.Value(key+".destinationIsTarget",destination==p.camera.targetTexture?1:0);
        p.Describe(key+".cameraTarget",p.camera.targetTexture);
        p.Describe(key+".source",source);p.Describe(key+".destination",destination);
        p.Value(key+".active",RenderTexture.active==source?1:RenderTexture.active==destination?2:RenderTexture.active==null?0:3);
        var saved=RenderTexture.active;var savedTarget=p.target;
        p.target=source;p.ReadImage(key+".source");p.target=savedTarget;RenderTexture.active=saved;
    }
    void Describe(string key,RenderTexture rt)
    {
        if(rt==null){Value(key,"null");return;}
        State(key,rt);Value(key+".size","["+rt.width+","+rt.height+","+rt.depth+"]");
        Value(key+".filter",(int)rt.filterMode);
    }
    readonly StringBuilder records = new StringBuilder();
    string directory;
    Camera camera;
    RenderTexture target;
    static readonly RenderTextureFormat[] formats = {RenderTextureFormat.ARGB32,RenderTextureFormat.Default,RenderTextureFormat.ARGBHalf,RenderTextureFormat.DefaultHDR,RenderTextureFormat.ARGBFloat,RenderTextureFormat.RFloat,RenderTextureFormat.RGFloat,RenderTextureFormat.RHalf,RenderTextureFormat.RGHalf};
    void Value(string k, object v) { records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(v).Append("}\n"); }
    void State(string key, RenderTexture rt)
    {
        var d=rt.descriptor;
        Value(key, "["+(int)rt.format+","+(int)rt.graphicsFormat+","+rt.antiAliasing+","+d.msaaSamples+","+(rt.IsCreated()?1:0)+","+(rt.sRGB?1:0)+","+SystemInfo.GetRenderTextureSupportedMSAASampleCount(d)+"]");
    }
    void Capture(string key)
    {
        captureKey=key;effectIndex=0;camera.Render();captureKey=null;ReadImage(key);
    }
    void ReadImage(string key)
    {
        RenderTexture.active=target;
        int width=target!=null?target.width:128,height=target!=null?target.height:128;
        var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
        image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=null;
        var pixels=image.GetPixels();
        using(var w=new BinaryWriter(File.Create(Path.Combine(directory,key+".rgba"))))
            foreach(var c in pixels) { w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a); }
        File.WriteAllBytes(Path.Combine(directory,key+".png"),image.EncodeToPNG());
        Value(key+".pixels",pixels.Length);DestroyImmediate(image);
    }
    void Case(string key, RenderTextureFormat format, int samples, int quality, bool allow=true, bool hdr=false, int mode=0)
    {
        QualitySettings.antiAliasing=quality;camera.allowMSAA=allow;camera.allowHDR=hdr;
        camera.rect=mode==2?new Rect(.125f,.125f,.75f,.75f):new Rect(0,0,1,1);
        target=new RenderTexture(128,128,24,format,RenderTextureReadWrite.Linear);target.antiAliasing=samples;
        State(key+".before",target);Value(key+".create",target.Create()?"true":"false");State(key+".after",target);
        camera.targetTexture=target;
        if(mode==1) { camera.targetTexture=null;camera.SetTargetBuffers(target.colorBuffer,target.depthBuffer); }
        // Partial viewports leave pixels outside the viewport untouched. Initialize them.
        RenderTexture.active=target;GL.Clear(true,true,Color.clear);RenderTexture.active=null;
        CameraMsaaEffect effect=null;if(mode==3)effect=camera.gameObject.AddComponent<CameraMsaaEffect>();
        Capture(key);State(key+".rendered",target);
        if(effect!=null)DestroyImmediate(effect);
        camera.targetTexture=null;target.Release();DestroyImmediate(target);
    }
    void ReadVariants(string key)
    {
        Describe(key+".target",target);
        foreach(var format in new[]{TextureFormat.RGBAFloat,TextureFormat.RGBA32})foreach(bool linear in new[]{false,true})
        {
            string k=key+"."+format+".linear"+linear;
            RenderTexture.active=target;var t=new Texture2D(128,128,format,false,linear);
            t.ReadPixels(new Rect(0,0,128,128),0,0);t.Apply();
            using(var w=new BinaryWriter(File.Create(Path.Combine(directory,k+".rgba"))))
                foreach(var c in t.GetPixels()){w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a);}
            File.WriteAllBytes(Path.Combine(directory,k+".raw"),t.GetRawTextureData());
            File.WriteAllBytes(Path.Combine(directory,k+".png"),t.EncodeToPNG());Value(k+".pixels",128*128);
            var c0=t.GetPixel(64,64);Value(k+".center","["+c0.r.ToString("R")+","+c0.g.ToString("R")+","+c0.b.ToString("R")+","+c0.a.ToString("R")+"]");
            DestroyImmediate(t);RenderTexture.active=null;
        }
    }
    IEnumerator Boundaries(GameObject triangle,Mesh mesh)
    {
        var original=triangle.GetComponent<MeshRenderer>().sharedMaterial;
        var a=new Material(Shader.Find("Probe/CameraLayers"));a.SetVector("_Color",new Vector4(2.34567f,.456789f,.123456f,.37123f));
        var b=new Material(Shader.Find("Probe/CameraLayers"));b.SetVector("_Color",new Vector4(.173219f,.812345f,1.56789f,.612345f));
        triangle.GetComponent<MeshRenderer>().sharedMaterial=a;
        var overlay=new GameObject("Overlap",typeof(MeshFilter),typeof(MeshRenderer));overlay.GetComponent<MeshFilter>().sharedMesh=mesh;overlay.GetComponent<MeshRenderer>().sharedMaterial=b;b.renderQueue=3001;
        foreach(var f in new[]{RenderTextureFormat.ARGB32,RenderTextureFormat.ARGBHalf,RenderTextureFormat.ARGBFloat})
        foreach(bool srgb in new[]{false,true})foreach(bool hdr in new[]{false,true})foreach(int q in new[]{0,2,4})
        {
            string key="boundary."+f+".srgb"+srgb+".hdr"+hdr+".q"+q;
            QualitySettings.antiAliasing=q;camera.allowHDR=hdr;camera.allowMSAA=true;camera.rect=new Rect(0,0,1,1);
            target=new RenderTexture(128,128,24,f,srgb?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear);target.Create();camera.targetTexture=target;
            Capture(key);Describe(key+".target",target);
            var effect=camera.gameObject.AddComponent<CameraMsaaEffect>();Capture(key+".effect");DestroyImmediate(effect);
            if(f==RenderTextureFormat.ARGB32 && hdr)
            {
                foreach(int mode in new[]{0,1,2})
                {
                    string vk=key+".viewport"+mode;
                    camera.rect=new Rect(.125f,.25f,.625f,.5f);
                    camera.backgroundColor=new Color(.13791f,.27813f,.39127f,.43129f);
                    RenderTexture.active=target;GL.Clear(true,true,new Color(.21739f,.34719f,.61973f,.71329f));RenderTexture.active=null;
                    if(mode==1)effect=camera.gameObject.AddComponent<CameraMsaaEffect>();
                    if(mode==2){camera.targetTexture=null;camera.SetTargetBuffers(target.colorBuffer,target.depthBuffer);}
                    Capture(vk);if(mode==1)DestroyImmediate(effect);camera.targetTexture=target;
                }
                camera.backgroundColor=Color.clear;
            }
            camera.targetTexture=null;target.Release();DestroyImmediate(target);
        }
        foreach(var f in new[]{RenderTextureFormat.ARGB32,RenderTextureFormat.ARGBFloat}) {
            var source=new RenderTexture(128,128,24,f,RenderTextureReadWrite.Linear);source.antiAliasing=4;camera.targetTexture=source;camera.allowHDR=false;camera.rect=new Rect(0,0,1,1);camera.Render();
            var destination=new RenderTexture(128,128,0,f,RenderTextureReadWrite.Linear);destination.Create();
            target=destination;RenderTexture.active=destination;GL.Clear(true,true,new Color(.21739f,.34719f,.61973f,.71329f));RenderTexture.active=null;
            ReadImage("boundary."+f+".resolveSentinelBefore");
            RenderTexture.active=null;Value("boundary."+f+".resolveInactive.bound",RenderTexture.active==source?1:0);source.ResolveAntiAliasedSurface(destination);
            ReadImage("boundary."+f+".resolveSentinelInactive");
            RenderTexture.active=source;Value("boundary."+f+".resolveActive.bound",RenderTexture.active==source?1:0);source.ResolveAntiAliasedSurface(destination);
            ReadImage("boundary."+f+".resolveSentinelActive");RenderTexture.active=null;
            camera.targetTexture=null;source.Release();destination.Release();DestroyImmediate(source);DestroyImmediate(destination);
        }
        DestroyImmediate(overlay);triangle.GetComponent<MeshRenderer>().sharedMaterial=original;
        // Read an explicit sRGB attachment with exactly the screen's measured byte format.
        foreach(bool srgb in new[]{false,true}) {
            target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32,srgb?RenderTextureReadWrite.sRGB:RenderTextureReadWrite.Linear);
            camera.targetTexture=target;camera.rect=new Rect(0,0,1,1);camera.allowHDR=false;camera.allowMSAA=false;camera.Render();
            ReadVariants("boundary.readback.srgb"+srgb);camera.targetTexture=null;target.Release();DestroyImmediate(target);
        }
        var old=camera;camera=new GameObject("BoundaryScreen").AddComponent<Camera>();camera.CopyFrom(old);camera.transform.position=old.transform.position;camera.targetTexture=null;camera.gameObject.AddComponent<CameraMsaaScreenRead>();
        foreach(bool hdr in new[]{false,true})foreach(int q in new[]{0,2,4}) {
            QualitySettings.antiAliasing=q;camera.allowHDR=hdr;camera.allowMSAA=true;camera.enabled=true;
            yield return null;yield return null;
            target=null;
#if UNITY_EDITOR
            if(typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder")==null) {
                var bf=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
                var ass=typeof(UnityEditor.Editor).Assembly;var gv=UnityEditor.EditorWindow.GetWindow(ass.GetType("UnityEditor.GameView"));
                target=(RenderTexture)ass.GetType("UnityEditor.PlayModeView").GetMethod("RenderView",bf).Invoke(gv,new object[]{Vector2.zero,false});
            }
#endif
            ReadVariants("boundary.screen.h"+hdr+".q"+q);
            // Public API read from the screen, without the Editor's RenderView attachment.
            screenKey="boundary.screenPublic.h"+hdr+".q"+q;camera.Render();camera.enabled=false;
        }
        DestroyImmediate(camera.gameObject);camera=old;Current=null;
    }
    IEnumerator Start()
    {
        yield return null;
        Current=this;directory=Environment.GetEnvironmentVariable("CAMERA_MSAA_OUT")??"/tmp/cameramsaa";Directory.CreateDirectory(directory);
        Value("colorSpace","\""+QualitySettings.activeColorSpace+"\"");
        Debug.Log("MSAA_DEVICE "+SystemInfo.graphicsDeviceType+" "+SystemInfo.graphicsDeviceName+" "+SystemInfo.graphicsDeviceVersion);
        camera=new GameObject("Camera").AddComponent<Camera>();camera.gameObject.AddComponent<CameraMsaaScreenRead>();camera.enabled=false;camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=64;camera.nearClipPlane=.1f;camera.farClipPlane=100;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
        var go=new GameObject("Coverage",typeof(MeshFilter),typeof(MeshRenderer));var m=new Mesh();
        // Several slopes and subpixel coordinates distinguish 1/2/4-sample coverage.
        m.vertices=new[]{new Vector3(-47.1875f,-38.3125f),new Vector3(48.4375f,-23.1875f),new Vector3(19.3125f,51.4375f)};m.triangles=new[]{0,1,2};m.RecalculateBounds();go.GetComponent<MeshFilter>().sharedMesh=m;go.GetComponent<MeshRenderer>().sharedMaterial=new Material(Shader.Find("Probe/Coverage"));
        foreach(var f in formats)
        {
            foreach(int s in new[]{0,1,2,3,4,8}) {
                var d=new RenderTextureDescriptor(128,128,f,24);d.msaaSamples=s;
                Value(f+".supported"+s,SystemInfo.GetRenderTextureSupportedMSAASampleCount(d));
            }
            foreach(int s in new[]{1,2,4})foreach(int q in new[]{0,2,4})Case(f+".s"+s+".q"+q,f,s,q);
            Case(f+".off",f,1,4,false);Case(f+".hdr",f,1,4,true,true);
            Case(f+".buffers",f,1,4,true,false,1);Case(f+".viewport",f,1,4,true,false,2);
            Case(f+".effect",f,1,4,true,false,3);Case(f+".effectHDR",f,1,4,true,true,3);
            if(f==RenderTextureFormat.ARGB32 || f==RenderTextureFormat.ARGBHalf || f==RenderTextureFormat.ARGBFloat) {
                Case(f+".offS2",f,2,4,false);
                Case(f+".hdrQ0",f,1,0,true,true);Case(f+".hdrQ2",f,1,2,true,true);
                Case(f+".hdrQ0S2",f,2,0,true,true);Case(f+".hdrOffS2",f,2,4,false,true);Case(f+".hdrQ2S4",f,4,2,true,true);
                Case(f+".hdrOff",f,1,4,false,true);Case(f+".hdrS2",f,2,4,true,true);
                Case(f+".hdrBuffers",f,1,4,true,true,1);Case(f+".hdrViewport",f,1,4,true,true,2);
                Case(f+".effectS2",f,2,4,true,false,3);Case(f+".effectHDRS2",f,2,4,true,true,3);
            }
        }
        // A fresh camera avoids retaining explicit buffer bindings from earlier cases.
        var oldCamera=camera;camera=new GameObject("ScreenCamera").AddComponent<Camera>();camera.CopyFrom(oldCamera);camera.transform.position=oldCamera.transform.position;camera.targetTexture=null;
        foreach(bool hdr in new[]{false,true})foreach(int q in new[]{0,2,4}) {
            QualitySettings.antiAliasing=q;camera.allowHDR=hdr;camera.allowMSAA=true;camera.rect=new Rect(0,0,1,1);camera.enabled=true;
            yield return null;yield return null;
            target=null;
#if UNITY_EDITOR
            if(typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder")==null) {
                var bf=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static;
                var a=typeof(UnityEditor.Editor).Assembly;var gv=UnityEditor.EditorWindow.GetWindow(a.GetType("UnityEditor.GameView"));
                var pmv=a.GetType("UnityEditor.PlayModeView");pmv.GetProperty("targetSize",bf).SetValue(gv,new Vector2(128,128));
                target=(RenderTexture)pmv.GetMethod("RenderView",bf).Invoke(gv,new object[]{Vector2.zero,false});
                Debug.Log("MSAA_SCREEN "+hdr+" "+q+" "+target.width+"x"+target.height+" "+target.format+" "+target.antiAliasing);
            }
#endif
            ReadImage("screen.h"+hdr+".q"+q);camera.enabled=false;
        }
        DestroyImmediate(camera.gameObject);camera=oldCamera;
        // Re-render one created target while quality, camera flags and explicit samples change.
        foreach(var f in new[]{RenderTextureFormat.ARGB32,RenderTextureFormat.ARGBFloat}) {
            target=new RenderTexture(128,128,24,f,RenderTextureReadWrite.Linear);camera.targetTexture=target;camera.allowHDR=false;
            for(int i=0;i<6;i++) {QualitySettings.antiAliasing=i%3==0?0:i%3==1?2:4;camera.allowMSAA=i<4;Capture(f+".live"+i);State(f+".live"+i+".state",target);}
            camera.allowMSAA=true;target.Release();target.antiAliasing=4;Capture(f+".recreate");State(f+".recreate.state",target);
            var resolved=new RenderTexture(128,128,0,f,RenderTextureReadWrite.Linear);resolved.Create();target.ResolveAntiAliasedSurface(resolved);RenderTexture.active=resolved;
            // Read resolved separately without another camera render.
            var copy=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);copy.ReadPixels(new Rect(0,0,128,128),0,0);copy.Apply();
            using(var w=new BinaryWriter(File.Create(Path.Combine(directory,f+".resolve.rgba"))))foreach(var c in copy.GetPixels()){w.Write(c.r);w.Write(c.g);w.Write(c.b);w.Write(c.a);}
            File.WriteAllBytes(Path.Combine(directory,f+".resolve.png"),copy.EncodeToPNG());Value(f+".resolve.pixels",128*128);DestroyImmediate(copy);
            // The preceding inactive-source call asserts in Native. Retain its output as invalid
            // input evidence, then exercise the valid path with the source bound explicitly.
            RenderTexture.active=target;target.ResolveAntiAliasedSurface(resolved);
            var source=target;target=resolved;ReadImage(f+".resolveActive");target=source;
            RenderTexture.active=null;resolved.Release();DestroyImmediate(resolved);camera.targetTexture=null;target.Release();DestroyImmediate(target);
        }
        yield return Boundaries(go,m);
        File.WriteAllText(Path.Combine(directory,"values.jsonl"),records.ToString());Debug.Log("CAMERA_MSAA_DONE "+directory);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
