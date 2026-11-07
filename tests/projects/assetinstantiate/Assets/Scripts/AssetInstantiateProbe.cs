using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.Collections;
using Object = UnityEngine.Object;

public class AssetInstantiateProbe : MonoBehaviour
{
    readonly StringBuilder output = new StringBuilder();
    readonly List<KeyValuePair<string,Object>> deferred = new List<KeyValuePair<string,Object>>();
    string context;
    int logs;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init() { new GameObject("AssetInstantiateProbe").AddComponent<AssetInstantiateProbe>(); }
    static string Escape(string s) => s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t");
    void V(string k, object value) { output.Append("{\"k\":\"").Append(Escape(k)).Append("\",\"v\":\"").Append(Escape(Convert.ToString(value,CultureInfo.InvariantCulture)??"")).Append("\"}\n"); }
    void Log(string condition,string stack,LogType type) { if(context!=null) V(context+".log."+(logs++),type+":"+condition); }
    void Try(string k,Action action)
    {
        var previousContext=context; int previousLogs=logs; context=k; logs=0;
        try { action(); V(k+".result","ok"); }
        catch(Exception e) { V(k+".result",e.GetType().Name+":"+e.Message); }
        finally { V(k+".logs",logs); context=previousContext; logs=previousLogs; }
    }
    void Case(string k,Action action) { Try(k+".case",action); }
    T Clone<T>(string k,T a) where T:Object
    {
        T b=null;
        Try(k+".instantiate",()=>b=Object.Instantiate(a));
        if(!b) { V(k+".null",true); return null; }
        V(k+".same_type",a.GetType()==b.GetType()); V(k+".name",b.name);
        V(k+".distinct_id",a.GetInstanceID()!=b.GetInstanceID()); V(k+".same_object",ReferenceEquals(a,b));
        V(k+".flags",(int)b.hideFlags); V(k+".source_name",a.name); V(k+".source_flags",(int)a.hideFlags);
        V(k+".id_nonzero",b.GetInstanceID()!=0);
        Object c=null; Try(k+".again",()=>c=Object.Instantiate(b));
        if(c) { V(k+".again.name",c.name); Object.DestroyImmediate(c); }
        var parent=new GameObject("parent");
        Try(k+".parent",()=>Object.DestroyImmediate(Object.Instantiate(a,parent.transform)));
        Try(k+".parent_world",()=>Object.DestroyImmediate(Object.Instantiate(a,parent.transform,true)));
        Try(k+".null_parent",()=>Object.DestroyImmediate(Object.Instantiate(a,(Transform)null)));
        Try(k+".position",()=>Object.DestroyImmediate(Object.Instantiate(a,Vector3.one,Quaternion.identity)));
        Try(k+".position_parent",()=>Object.DestroyImmediate(Object.Instantiate(a,Vector3.one,Quaternion.identity,parent.transform)));
        Object.DestroyImmediate(parent);
        var d=Object.Instantiate(a); Object.Destroy(d); V(k+".destroy.immediate_live",d!=null);
        deferred.Add(new KeyValuePair<string,Object>(k,d));
        return b;
    }
    void Finish(string k,Object a,Object b)
    {
        int id=b.GetInstanceID(); Object.DestroyImmediate(b);
        V(k+".destroyed",b==null); V(k+".source_live",a!=null); V(k+".destroyed_id",b.GetInstanceID()==id);
    }
    void ColorValue(string k,Color c) { for(int i=0;i<4;i++) V(k+"."+i,c[i]); }
    void AudioInfo(string k,AudioClip a)
    {
        V(k+".samples",a.samples); V(k+".channels",a.channels); V(k+".frequency",a.frequency);
        V(k+".state",a.loadState); V(k+".type",a.loadType); V(k+".preload",a.preloadAudioData); V(k+".background",a.loadInBackground);
        var data=new float[8]; Try(k+".read",()=>V(k+".get",a.GetData(data,0)));
        for(int i=0;i<data.Length;i++) V(k+".data."+i,data[i]);
    }
    void AudioCase(string k,AudioClip a)
    {
        a.hideFlags=HideFlags.HideAndDontSave; AudioInfo(k+".source",a);
        var b=Clone(k,a); if(!b)return; AudioInfo(k+".clone",b);
        Try(k+".write",()=>V(k+".set",b.SetData(new[]{0.75f,0.5f,0.25f,0f,-0.25f,-0.5f,-0.75f,0f},0)));
        AudioInfo(k+".after_source",a); AudioInfo(k+".after_clone",b);
        Try(k+".load",()=>V(k+".load.value",b.LoadAudioData())); V(k+".loaded_state",b.loadState);
        Try(k+".unload",()=>V(k+".unload.value",b.UnloadAudioData())); V(k+".unloaded_state",b.loadState); V(k+".source_state_after_unload",a.loadState);
        AudioInfo(k+".source_after_unload",a);
        b.LoadAudioData();V(k+".source_state_after_clone_load",a.loadState);
        Finish(k,a,b);
        AudioInfo(k+".source_after_destroy",a);
        if(a.samples>0 && a.loadType==AudioClipLoadType.DecompressOnLoad && k.Contains("Pcm")) {
            var survivor=Object.Instantiate(a);Object.DestroyImmediate(a,true);AudioInfo(k+".clone_after_source_destroy",survivor);Object.DestroyImmediate(survivor);
        }
    }
    void FontCase(string k,Font a)
    {
        a.hideFlags=HideFlags.HideAndDontSave;
        if(a.dynamic) a.RequestCharactersInTexture("AB",16,FontStyle.Normal);
        var b=Clone(k,a); if(!b)return;
        V(k+".dynamic",b.dynamic); V(k+".size",b.fontSize); V(k+".ascent",b.ascent); V(k+".line",b.lineHeight);
        V(k+".material_shared",a.material==b.material);
        V(k+".texture_shared",a.material && b.material && a.material.mainTexture==b.material.mainTexture);
        V(k+".characters",b.characterInfo.Length);
        if(b.dynamic) { b.RequestCharactersInTexture("ABC",16,FontStyle.Normal); V(k+".has_A",b.GetCharacterInfo('A',out var ci,16,FontStyle.Normal)); V(k+".advance_A",ci.advance); }
        else { V(k+".advance_A",b.characterInfo[0].advance); var chars=b.characterInfo; chars[0].advance=33; b.characterInfo=chars; V(k+".source_advance",a.characterInfo[0].advance); }
        Finish(k,a,b);
    }
    void CubeCase(string k,bool readable)
    {
        var a=new Cubemap(4,TextureFormat.RGBA32,true) { name="cube",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point };
        for(int f=0;f<6;f++) for(int m=0;m<3;m++) { var colors=new Color[Math.Max(1,16>>(m*2))]; for(int i=0;i<colors.Length;i++)colors[i]=new Color32((byte)(f*40),(byte)(m*60),200,255); a.SetPixels(colors,(CubemapFace)f,m); }
        a.Apply(false,!readable);
        var b=Clone(k,a); if(!b)return;
        V(k+".readable",b.isReadable); V(k+".mips",b.mipmapCount); V(k+".format",b.format); V(k+".filter",b.filterMode);
        Try(k+".pixels",()=>{for(int f=0;f<6;f++)for(int m=0;m<3;m++)ColorValue(k+".face."+f+".mip."+m,b.GetPixels((CubemapFace)f,m)[0]);});
        if(readable) { b.SetPixel(CubemapFace.PositiveX,0,0,Color.red); ColorValue(k+".source_after_write",a.GetPixel(CubemapFace.PositiveX,0,0)); }
        Finish(k,a,b); Object.DestroyImmediate(a);
    }
    void MeshCase(string k,bool readable)
    {
        var a=new Mesh { name="mesh",vertices=new[]{Vector3.zero,Vector3.right,Vector3.up},triangles=new[]{0,1,2},hideFlags=HideFlags.HideAndDontSave };
        a.UploadMeshData(!readable); var b=Clone(k,a); if(!b)return;
        V(k+".readable",b.isReadable); V(k+".vertices",b.vertexCount);
        Try(k+".data",()=>V(k+".x",b.vertices[1].x));
        if(b.isReadable) { var v=b.vertices; v[1]=new Vector3(9,0,0); b.vertices=v; V(k+".source_x",a.vertices[1].x); }
        Finish(k,a,b); Object.DestroyImmediate(a);
    }
    void OtherCases()
    {
        Case("texture",()=>{ var a=new Texture2D(2,2){name="tex",hideFlags=HideFlags.HideAndDontSave};a.SetPixel(0,0,Color.red);a.Apply();var b=Clone("texture",a);if(!b)return;V("texture.readable",b.isReadable);ColorValue("texture.pixel",b.GetPixel(0,0));b.SetPixel(0,0,Color.green);ColorValue("texture.source",a.GetPixel(0,0));Finish("texture",a,b); });
        Case("rt",()=>{var a=new RenderTexture(4,4,0){name="rt",hideFlags=HideFlags.HideAndDontSave};a.Create();var b=Clone("rt",a);if(!b)return;V("rt.created",b.IsCreated());V("rt.width",b.width);Finish("rt",a,b);});
        Case("material",()=>{var a=new Material(Shader.Find("Unlit/Color")){name="mat",color=Color.red,hideFlags=HideFlags.HideAndDontSave};var b=Clone("material",a);if(!b)return;V("material.shader_shared",a.shader==b.shader);ColorValue("material.color",b.color);b.color=Color.green;ColorValue("material.source",a.color);Finish("material",a,b);});
        Case("animation",()=>{var a=new AnimationClip{name="clip",legacy=true,hideFlags=HideFlags.HideAndDontSave};a.SetCurve("",typeof(Transform),"localPosition.x",AnimationCurve.Linear(0,0,1,4));var b=Clone("animation",a);if(!b)return;var go=new GameObject("sample");b.SampleAnimation(go,0.5f);V("animation.x",go.transform.localPosition.x);b.ClearCurves();a.SampleAnimation(go,0.75f);V("animation.source_x",go.transform.localPosition.x);Finish("animation",a,b);Object.DestroyImmediate(go);});
        Case("script",()=>{var a=ScriptableObject.CreateInstance<CloneAsset>();a.name="script";a.hideFlags=HideFlags.HideAndDontSave;a.data=new CloneData{value=7,reference=a};a.self=a;a.values=new[]{1,2};var b=Clone("script",a);if(!b)return;V("script.self",b.self==b);V("script.data_reference",b.data.reference==b);V("script.curve_created",b.curve!=null);b.data.value=10;b.values[0]=9;V("script.source_value",a.data.value);V("script.source_array",a.values[0]);Finish("script",a,b);});
        Case("sprite",()=>{var t=new Texture2D(4,4);var a=Sprite.Create(t,new Rect(0,0,4,4),Vector2.one*0.5f,1,0,SpriteMeshType.FullRect);a.name="sprite";a.hideFlags=HideFlags.HideAndDontSave;var b=Clone("sprite",a);if(!b)return;V("sprite.texture_shared",b.texture==a.texture);V("sprite.vertices",b.vertices.Length);var v=b.vertices;v[0]=Vector2.zero;b.OverrideGeometry(v,b.triangles);V("sprite.source_vertex",a.vertices[0].x);Finish("sprite",a,b);});
        Case("text",()=>{var a=new TextAsset("abc"){name="text",hideFlags=HideFlags.HideAndDontSave};var b=Clone("text",a);if(!b)return;V("text.text",b.text);var bytes=b.bytes;bytes[0]=100;V("text.source",a.text);V("text.clone",b.text);Finish("text",a,b);});
        Case("physics",()=>{var a=new PhysicsMaterial("physics"){bounciness=0.25f,hideFlags=HideFlags.HideAndDontSave};var b=Clone("physics",a);if(!b)return;V("physics.bounce",b.bounciness);b.bounciness=0.75f;V("physics.source",a.bounciness);Finish("physics",a,b);});
        Case("controller",()=>{var a=Resources.Load<RuntimeAnimatorController>("Control");var b=Clone("controller",a);if(!b)return;V("controller.clips",b.animationClips.Length);Finish("controller",a,b);});
        Case("override",()=>{var a=new AnimatorOverrideController(Resources.Load<RuntimeAnimatorController>("Control")){name="override",hideFlags=HideFlags.HideAndDontSave};var b=Clone("override",a);if(!b)return;V("override.controller_shared",a.runtimeAnimatorController==b.runtimeAnimatorController);Finish("override",a,b);});
        Case("compute",()=>{var a=Resources.Load<ComputeShader>("Control");a.SetInt("Value",5);var b=Clone("compute",a);if(!b)return;V("compute.kernel",b.FindKernel("Main"));var buffer=new ComputeBuffer(1,4);a.SetBuffer(0,"Output",buffer);a.SetInt("Value",7);b.SetBuffer(0,"Output",buffer);b.Dispatch(0,1,1,1);var values=new int[1];buffer.GetData(values);V("compute.initial",values[0]);b.SetInt("Value",9);b.Dispatch(0,1,1,1);buffer.GetData(values);V("compute.clone",values[0]);a.Dispatch(0,1,1,1);buffer.GetData(values);V("compute.source",values[0]);buffer.Release();Finish("compute",a,b);});
        Case("shader",()=>{var a=Shader.Find("Unlit/Color");var b=Clone("shader",a);if(!b)return;V("shader.supported",b.isSupported);V("shader.queue",b.renderQueue);Finish("shader",a,b);});
    }
    byte[] ReadTarget(RenderTexture rt)
    {
        var old=RenderTexture.active; RenderTexture.active=rt;
        var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
        var result=image.GetRawTextureData();
        Object.DestroyImmediate(image);RenderTexture.active=old;return result;
    }
    void EqualImage(string k,byte[] a,byte[] b)
    {
        bool same=a.Length==b.Length,nonblack=false;
        for(int i=0;i<a.Length;i++) { if(a[i]!=b[i])same=false;if(i%4!=3 && a[i]>8)nonblack=true; }
        V(k+".same_image",same);V(k+".nonblack",nonblack);
    }
    void UseControls()
    {
        Case("use.font",()=>{
            var a=new Font { name="bitmap",material=new Material(Shader.Find("GUI/Text Shader")) };
            a.material.mainTexture=Texture2D.whiteTexture;
            a.characterInfo=new[]{ new CharacterInfo{index=65,advance=8,minX=0,maxX=8,minY=0,maxY=8,uvBottomLeft=Vector2.zero,uvBottomRight=Vector2.right,uvTopLeft=Vector2.up,uvTopRight=Vector2.one}, new CharacterInfo{index=66,advance=8,minX=0,maxX=8,minY=0,maxY=8,uvBottomLeft=Vector2.zero,uvBottomRight=Vector2.right,uvTopLeft=Vector2.up,uvTopRight=Vector2.one} };
            var b=Object.Instantiate(a);
            var go=new GameObject("text");var text=go.AddComponent<TextMesh>();text.text="AB";text.fontSize=16;text.characterSize=0.2f;text.anchor=TextAnchor.MiddleCenter;
            var cameraGo=new GameObject("camera");var camera=cameraGo.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=1;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.transform.position=new Vector3(0,0,-10);
            var rt=new RenderTexture(64,64,16);camera.targetTexture=rt;
            text.font=a;go.GetComponent<MeshRenderer>().sharedMaterial=a.material;camera.Render();var original=ReadTarget(rt);
            text.font=b;go.GetComponent<MeshRenderer>().sharedMaterial=b.material;camera.Render();EqualImage("use.font",original,ReadTarget(rt));
            Object.DestroyImmediate(cameraGo);Object.DestroyImmediate(go);Object.DestroyImmediate(rt);Object.DestroyImmediate(b);
        });
        Case("use.cube",()=>{
            var a=new Cubemap(2,TextureFormat.RGBA32,false);for(int f=0;f<6;f++)a.SetPixels(new[]{Color.red,Color.red,Color.red,Color.red},(CubemapFace)f);a.Apply();var b=Object.Instantiate(a);
            var go=new GameObject("sky");var camera=go.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.Skybox;var sky=go.AddComponent<Skybox>();var mat=new Material(Shader.Find("Skybox/Cubemap"));sky.material=mat;var rt=new RenderTexture(16,16,16);camera.targetTexture=rt;
            mat.SetTexture("_Tex",a);camera.Render();var original=ReadTarget(rt);mat.SetTexture("_Tex",b);camera.Render();EqualImage("use.cube",original,ReadTarget(rt));
            Object.DestroyImmediate(go);Object.DestroyImmediate(rt);Object.DestroyImmediate(mat);Object.DestroyImmediate(a);Object.DestroyImmediate(b);
        });
        Case("use.material",()=>{
            var a=new Material(Shader.Find("Unlit/Color"));a.color=Color.red;var b=Object.Instantiate(a);var rt=new RenderTexture(8,8,0);Graphics.Blit(Texture2D.whiteTexture,rt,a);var original=ReadTarget(rt);Graphics.Blit(Texture2D.whiteTexture,rt,b);EqualImage("use.material",original,ReadTarget(rt));RenderTexture.active=null;Object.DestroyImmediate(rt);Object.DestroyImmediate(a);Object.DestroyImmediate(b);
        });
    }
    IEnumerator OwnershipControls()
    {
        var a=Resources.Load<AudioClip>("Lifetime");var b=Object.Instantiate(a);
        b.UnloadAudioData();V("ownership.pair.source_unload",a.loadState);V("ownership.pair.clone_unload",b.loadState);
        yield return null;
        V("ownership.pair.source_next",a.loadState);V("ownership.pair.clone_next",b.loadState);
        b.LoadAudioData();Object.DestroyImmediate(a,true);
        V("ownership.survivor.before_read",b.loadState);var data=new float[8];V("ownership.survivor.read",b.GetData(data,0));
        V("ownership.survivor.after_read",b.loadState);b.UnloadAudioData();V("ownership.survivor.after_unload",b.loadState);
        yield return null;
        V("ownership.survivor.next_frame",b.loadState);Object.DestroyImmediate(b);
        var runtime=AudioClip.Create("stereo",37,2,11025,false);var copy=Clone("audio.stereo",runtime);
        if(copy) {AudioInfo("audio.stereo.clone",copy);Finish("audio.stereo",runtime,copy);}Object.DestroyImmediate(runtime);
        Case("sprite.valid",()=>{
            var t=new Texture2D(4,4);var original=Sprite.Create(t,new Rect(0,0,4,4),Vector2.one*0.5f,1,0,SpriteMeshType.FullRect);var clone=Object.Instantiate(original);
            clone.OverrideGeometry(new[]{Vector2.zero,new Vector2(4,0),new Vector2(0,4)},new ushort[]{0,1,2});
            V("sprite.valid.original_count",original.vertices.Length);V("sprite.valid.clone_count",clone.vertices.Length);
            V("sprite.valid.original_vertex",original.vertices[0].x);V("sprite.valid.clone_vertex",clone.vertices[0].x);
            Object.DestroyImmediate(original);V("sprite.valid.survivor_count",clone.vertices.Length);Object.DestroyImmediate(clone);Object.DestroyImmediate(t);
        });
        Case("font.overrides",()=>{
            var go=new GameObject("customText");var text=go.AddComponent<TextMesh>();var font=new Font();
            Try("font.overrides.assign_default",()=>text.font=font);
            Try("font.overrides.size",()=>text.fontSize=12);
            Try("font.overrides.style",()=>text.fontStyle=FontStyle.Bold);
            Try("font.overrides.clear_size",()=>text.fontSize=0);
            Try("font.overrides.clear_style",()=>text.fontStyle=FontStyle.Normal);
            Try("font.overrides.assign_again",()=>text.font=font);
            Object.DestroyImmediate(go);Object.DestroyImmediate(font);
        });
    }
    IEnumerator Start()
    {
        Application.logMessageReceived+=Log;
        Case("audio.runtime",()=>{var a=AudioClip.Create("tone",22050,1,22050,false);var data=new float[22050];for(int i=0;i<data.Length;i++)data[i]=0.25f;a.SetData(data,0);AudioCase("audio.runtime",a);});
        Case("audio.callback",()=>AudioCase("audio.callback",AudioClip.Create("callback",22050,1,22050,false,data=>{for(int i=0;i<data.Length;i++)data[i]=0.5f;})));
        Case("audio.stream",()=>AudioCase("audio.stream",AudioClip.Create("stream",22050,1,22050,true,data=>{for(int i=0;i<data.Length;i++)data[i]=0.5f;})));
        foreach(var n in new[]{"Pcm","Compressed","Streaming","Unloaded"})Case("audio."+n,()=>AudioCase("audio."+n,Resources.Load<AudioClip>(n)));
        Case("font.custom",()=>FontCase("font.custom",new Font{name="custom",characterInfo=new[]{new CharacterInfo{index=65,advance=7}}}));
        Case("font.dynamic",()=>FontCase("font.dynamic",Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
        Case("cube.readable",()=>CubeCase("cube.readable",true)); Case("cube.unreadable",()=>CubeCase("cube.unreadable",false));
        Case("mesh.readable",()=>MeshCase("mesh.readable",true)); Case("mesh.unreadable",()=>MeshCase("mesh.unreadable",false));
        OtherCases();
        var controls=OwnershipControls();while(controls.MoveNext())yield return controls.Current;
        UseControls();
        var config=AudioSettings.GetConfiguration();config.sampleRate=48000;config.speakerMode=AudioSpeakerMode.Stereo;config.dspBufferSize=1024;AudioSettings.Reset(config);Time.captureDeltaTime=1024f/48000;
        new GameObject("listener").AddComponent<AudioListener>();AudioListener.volume=1;
        V("use.audio.start",AudioRenderer.Start());
        var playing=new GameObject("playing").AddComponent<AudioSource>();playing.loop=true;playing.spatialBlend=0;
        // The fresh resource is separate from the load/unload controls above.
        var imported=Resources.Load<AudioClip>("Audible");imported.LoadAudioData();
        var importedCopy=Object.Instantiate(imported);playing.clip=importedCopy;playing.Play();
        bool audible=false;
        for(int i=0;i<8;i++) {
            yield return null;
            var samples=new NativeArray<float>(2048,Allocator.Temp);V("use.audio.render."+i,AudioRenderer.Render(samples));
            for(int j=0;j<samples.Length;j++)if(Math.Abs(samples[j])>0.001f)audible=true;samples.Dispose();
        }
        V("use.audio.audible",audible);playing.Stop();AudioRenderer.Stop();Object.DestroyImmediate(playing.gameObject);Object.DestroyImmediate(importedCopy);
        foreach(var n in new[]{"", "already(Clone)"," spaced "})Case("names."+n,()=>{var a=new TextAsset("a"){name=n};var b=Clone("names."+n,a);if(b)Finish("names."+n,a,b);});
        yield return null;
        foreach(var pair in deferred)V(pair.Key+".destroy.next_frame",pair.Value==null);
        Application.logMessageReceived-=Log; V("done","ok");
        var path=Environment.GetEnvironmentVariable("ASSET_INSTANTIATE_OUT");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
