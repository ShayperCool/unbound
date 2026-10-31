using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class RectClippingProbe : MonoBehaviour
{
    readonly StringBuilder records = new StringBuilder();
    string directory;
    Camera camera;
    RenderTexture target;
    RectTransform overlayRoot;
    void LateUpdate()
    {
        if(overlayRoot==null)return;
        overlayRoot.position=new Vector3(64,64,0);overlayRoot.localScale=Vector3.one;overlayRoot.sizeDelta=new Vector2(128,128);
    }
    static string F(float x) => float.IsNaN(x) ? "\"NaN\"" : float.IsPositiveInfinity(x) ? "\"Infinity\"" : float.IsNegativeInfinity(x) ? "\"-Infinity\"" : x.ToString("R", CultureInfo.InvariantCulture);
    void Value(string k, string v) => records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(v).Append("}\n");
    void Vector(string k, Vector2 v) => Value(k, "[" + F(v.x) + "," + F(v.y) + "]");
    void Readback(string key, CanvasRenderer r, Mesh source, Material material)
    {
        Value(key+".enabled",r.hasRectClipping ? "true" : "false");
        Vector(key+".softness",r.clippingSoftness);
        Value(key+".materialIdentity", r.GetMaterial()==material ? "true" : "false");
        Value(key+".keyword",material.IsKeywordEnabled("UNITY_UI_CLIP_RECT") ? "true" : "false");
        var rect=material.GetVector("_ClipRect");
        Value(key+".materialRect","["+F(rect.x)+","+F(rect.y)+","+F(rect.z)+","+F(rect.w)+"]");
        Value(key+".materialSoftX",F(material.GetFloat("_UIMaskSoftnessX")));
        if(source!=null) { var v=source.vertices[0]; Value(key+".sourceVertex","["+F(v.x)+","+F(v.y)+","+F(v.z)+"]"); }
    }
    void Capture(string key)
    {
        Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
        var image=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);
        image.ReadPixels(new Rect(0,0,128,128),0,0); image.Apply(); RenderTexture.active=null;
        SaveImage(key,image);
    }
    void SaveImage(string key, Texture2D image)
    {
        var pixels=image.GetPixels();
        using(var writer=new BinaryWriter(File.Create(Path.Combine(directory,key+".rgba"))))
            foreach(var c in pixels) { writer.Write(c.r);writer.Write(c.g);writer.Write(c.b);writer.Write(c.a); }
        // PNGs aid visual inspection. Float buffers above are the numeric oracle, including alpha.
        File.WriteAllBytes(Path.Combine(directory,key+".png"),image.EncodeToPNG());
        Value(key+".pixels",pixels.Length.ToString());
        DestroyImmediate(image);
    }
    static RectTransform RectObject(string name, Transform parent, Vector2 size)
    {
        var rt=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        if(parent!=null) rt.SetParent(parent,false);
        rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);rt.sizeDelta=size;rt.localPosition=Vector3.zero;
        return rt;
    }
    static Mesh Quad()
    {
        var m=new Mesh();m.vertices=new[]{new Vector3(-55,-49),new Vector3(55,-49),new Vector3(55,49),new Vector3(-55,49)};
        m.colors32=new[]{new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255)};
        m.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};m.triangles=new[]{0,1,2,0,2,3};return m;
    }
    IEnumerator Start()
    {
        yield return null;
        directory=Environment.GetEnvironmentVariable("RECT_CLIP_OUT")??"/tmp/rectclip"; Directory.CreateDirectory(directory);
        Value("colorSpace","\""+QualitySettings.activeColorSpace+"\"");
        camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-10);
        bool qualityMSAA=Environment.GetEnvironmentVariable("RECT_CLIP_MSAA")=="1";camera.allowMSAA=qualityMSAA;QualitySettings.antiAliasing=qualityMSAA?2:0;camera.orthographic=true;camera.orthographicSize=64;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
        target=new RenderTexture(128,128,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);camera.targetTexture=target;
        var standard=new Material(Shader.Find("UI/Default"));
        var diagnostic=new Material(Shader.Find("Probe/ClipCoordinates"));
        var noVariant=new Material(Shader.Find("Probe/ClipNoVariant"));
        for(int pose=0;pose<7;pose++)
        {
            var root=RectObject("Root",null,new Vector2(128,128));var canvas=root.gameObject.AddComponent<Canvas>();
            canvas.renderMode=pose==6?RenderMode.ScreenSpaceCamera:RenderMode.WorldSpace;
            if(pose==6){canvas.worldCamera=camera;canvas.planeDistance=1;root.position=new Vector3(0,0,-9);}
            else {root.position=new Vector3(3,-5,0);if(pose==1)root.localEulerAngles=new Vector3(0,0,27);if(pose==2)root.localScale=new Vector3(.8f,1.1f,1);}
            Transform parent=root;
            if(pose==4||pose==5){var nested=RectObject("Nested",root,new Vector2(128,128));nested.localPosition=new Vector3(9,4,0);nested.localEulerAngles=new Vector3(0,0,-17);var nc=nested.gameObject.AddComponent<Canvas>();nc.overrideSorting=pose==5;parent=nested;}
            var rt=RectObject("Renderer",parent,new Vector2(110,98));if(pose==3){rt.localPosition=new Vector3(12,7,0);rt.localEulerAngles=new Vector3(0,0,19);rt.localScale=new Vector3(.8f,1.2f,1);}
            var r=rt.gameObject.AddComponent<CanvasRenderer>();var mesh=Quad();r.SetMesh(mesh);r.SetMaterial(standard,Texture2D.whiteTexture);
            for(int state=0;state<10;state++)
            {
                var clip=new Rect(-31,-23,59,48);var soft=Vector2.zero;
                if(state==1)soft=new Vector2(8,0);if(state==2)soft=new Vector2(0,12);if(state==3)soft=new Vector2(8,12);
                if(state==4)clip=new Rect(-3,-23,0,48);if(state==5)clip=new Rect(20,-23,-40,48);if(state==6)clip=Rect.zero;
                if(state==7)soft=new Vector2(-4,-8);if(state==8)soft=new Vector2(.5f,2.75f);
                r.EnableRectClipping(clip);r.clippingSoftness=soft;if(state==9)r.DisableRectClipping();
                string k="p"+pose+".s"+state;
                Readback(k,r,mesh,standard);Capture(k);
            }
            r.EnableRectClipping(new Rect(-31,-23,59,48));r.clippingSoftness=new Vector2(8,12);
            for(int mode=0;mode<5;mode++){diagnostic.SetFloat("_ProbeMode",mode);r.SetMaterial(diagnostic,0);Capture("p"+pose+".diag"+mode);}
            for(int state=0;state<4;state++)
            {
                diagnostic.SetFloat("_ProbeMode",state%2==0?2:3);
                diagnostic.EnableKeyword("UNITY_UI_CLIP_RECT");diagnostic.SetVector("_ClipRect",new Vector4(-9,-10,11,12));diagnostic.SetFloat("_UIMaskSoftnessX",21);
                if(state>=2)r.DisableRectClipping();
                Readback("p"+pose+".override"+state,r,mesh,diagnostic);Capture("p"+pose+".override"+state);
            }
            diagnostic.DisableKeyword("UNITY_UI_CLIP_RECT");r.DisableRectClipping();diagnostic.SetFloat("_ProbeMode",3);Capture("p"+pose+".off");
            r.EnableRectClipping(new Rect(-31,-23,59,48));noVariant.SetFloat("_ProbeMode",4);r.SetMaterial(noVariant,0);Capture("p"+pose+".noVariant");
            // Mutating the authored source must not change the CanvasRenderer snapshot.
            mesh.vertices=new[]{new Vector3(0,0),new Vector3(1,0),new Vector3(1,1),new Vector3(0,1)};
            Capture("p"+pose+".sourceChanged");r.SetMesh(mesh);Capture("p"+pose+".reassigned");
            r.SetMesh(Quad());r.SetMaterial(standard,0);r.SetPopMaterial(diagnostic,0);r.popMaterialCount=1;r.hasPopInstruction=true;diagnostic.SetFloat("_ProbeMode",3);Capture("p"+pose+".pop");
            r.popMaterialCount=1;r.SetPopMaterial(diagnostic,0);Capture("p"+pose+".validPop");
            r.popMaterialCount=0;r.hasPopInstruction=false;r.SetMaterial(diagnostic,0);diagnostic.SetFloat("_ProbeMode",3);
            r.clippingSoftness=new Vector2(-4,-8);Capture("p"+pose+".negativeUniform");
            r.clippingSoftness=new Vector2(8,12);diagnostic.SetFloat("_ProbeMode",4);
            rt.localPosition+=new Vector3(7,-4);Capture("p"+pose+".moved");
            rt.localEulerAngles+=new Vector3(0,0,13);Capture("p"+pose+".rotated");
            r.clippingSoftness=new Vector2(float.NaN,float.PositiveInfinity);Vector("p"+pose+".nonfiniteSoftReadback",r.clippingSoftness);
            r.EnableRectClipping(new Rect(float.NaN,0,float.PositiveInfinity,10));Value("p"+pose+".nonfiniteEnabled",r.hasRectClipping?"true":"false");
            r.clippingSoftness=new Vector2(8,12);r.EnableRectClipping(new Rect(-31,-23,59,48));
            Shader.EnableKeyword("UNITY_UI_CLIP_RECT");r.DisableRectClipping();diagnostic.SetFloat("_ProbeMode",3);Capture("p"+pose+".globalOff");
            r.EnableRectClipping(new Rect(-31,-23,59,48));Capture("p"+pose+".globalOn");Value("p"+pose+".globalPreserved",Shader.IsKeywordEnabled("UNITY_UI_CLIP_RECT")?"true":"false");Shader.DisableKeyword("UNITY_UI_CLIP_RECT");
            r.Clear();Readback("p"+pose+".clear",r,mesh,standard);
            DestroyImmediate(root.gameObject);DestroyImmediate(mesh);
        }
        for(int sorted=0;sorted<4;sorted++)
        {
            var root=RectObject("UGUI",null,new Vector2(128,128));var canvas=root.gameObject.AddComponent<Canvas>();canvas.renderMode=sorted>=2?RenderMode.ScreenSpaceCamera:RenderMode.WorldSpace;
            if(sorted>=2){canvas.worldCamera=camera;canvas.planeDistance=1;root.position=new Vector3(0,0,-9);}else root.position=Vector3.zero;
            var maskRect=RectObject("Mask",root,new Vector2(72,60));maskRect.localEulerAngles=new Vector3(0,0,19);var mask=maskRect.gameObject.AddComponent<RectMask2D>();
            var nested=RectObject("Nested",maskRect,new Vector2(128,128));var nestedCanvas=nested.gameObject.AddComponent<Canvas>();nestedCanvas.overrideSorting=sorted%2!=0;
            var secondRect=RectObject("Intersection",nested,new Vector2(60,80));secondRect.localPosition=new Vector3(10,0);var second=secondRect.gameObject.AddComponent<RectMask2D>();
            var graphic=RectObject("Image",secondRect,new Vector2(110,98));var image=graphic.gameObject.AddComponent<Image>();image.color=Color.white;
            for(int state=0;state<12;state++)
            {
                if(state==1)mask.softness=new Vector2Int(8,12);
                if(state==2)second.softness=new Vector2Int(12,4);
                if(state==3)second.enabled=false;
                if(state==4)mask.enabled=false;
                if(state==5){mask.enabled=true;second.enabled=true;mask.padding=new Vector4(3,4,5,6);}
                if(state==6)maskRect.sizeDelta=new Vector2(48,40);
                if(state==7)graphic.localPosition=new Vector3(19,-11);
                if(state==8)secondRect.gameObject.SetActive(false);
                if(state==9){secondRect.gameObject.SetActive(true);maskRect.gameObject.AddComponent<Image>();maskRect.gameObject.AddComponent<Mask>().showMaskGraphic=false;}
                if(state==10)maskRect.localScale=new Vector3(.8f,1.1f,1);
                if(state==11){maskRect.gameObject.GetComponent<Mask>().enabled=false;maskRect.gameObject.SetActive(false);maskRect.gameObject.SetActive(true);}
                Canvas.ForceUpdateCanvases();string k="ugui"+sorted+".s"+state;Vector(k+".softness",image.canvasRenderer.clippingSoftness);Value(k+".clip",image.canvasRenderer.hasRectClipping?"true":"false");Value(k+".cull",image.canvasRenderer.cull?"true":"false");Capture(k);
            }
            DestroyImmediate(root.gameObject);
        }
        camera.targetTexture=null;DestroyImmediate(target);target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default);
        overlayRoot=RectObject("Overlay",null,new Vector2(128,128));var overlay=overlayRoot.gameObject.AddComponent<Canvas>();overlay.renderMode=RenderMode.ScreenSpaceOverlay;
        var ort=RectObject("OverlayRenderer",overlayRoot,new Vector2(110,98));var or=ort.gameObject.AddComponent<CanvasRenderer>();var om=Quad();or.SetMesh(om);or.SetMaterial(standard,Texture2D.whiteTexture);
        RectTransform overlayMaskRect=null;RectMask2D overlayMask=null;Image overlayImage=null;
        for(int state=0;state<8;state++)
        {
            or.EnableRectClipping(new Rect(-31,-23,59,48));or.clippingSoftness=state==1?new Vector2(8,12):Vector2.zero;
            if(state==2)or.DisableRectClipping();
            if(state>=3&&state<5){or.SetMaterial(diagnostic,0);diagnostic.SetFloat("_ProbeMode",state==3?0:3);}
            if(state==5){ort.gameObject.SetActive(false);overlayMaskRect=RectObject("OverlayMask",overlayRoot,new Vector2(72,60));overlayMask=overlayMaskRect.gameObject.AddComponent<RectMask2D>();overlayMask.softness=new Vector2Int(8,12);overlayImage=RectObject("OverlayImage",overlayMaskRect,new Vector2(110,98)).gameObject.AddComponent<Image>();}
            if(state==6){overlayMaskRect.localEulerAngles=new Vector3(0,0,19);overlayMaskRect.sizeDelta=new Vector2(48,40);}
            if(state==7)overlayMask.enabled=false;
            Canvas.ForceUpdateCanvases();
            yield return null;
            var frameBuilder=typeof(Canvas).Assembly.GetType("Unbound.Runtime.FrameBuilder");
            if(frameBuilder!=null)
            {
                RenderTexture.active=target;GL.Clear(true,true,Color.clear);
                frameBuilder.GetMethod("DrawOverlayCanvases",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{camera});
            }
            else
            {
#if UNITY_EDITOR
                var gvType=typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
                var gv=UnityEditor.EditorWindow.GetWindow(gvType);
                var pmv=gvType.BaseType;var bf=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
                pmv.GetProperty("targetSize",bf).SetValue(gv,new Vector2(128,128));
                var view=(RenderTexture)pmv.GetMethod("RenderView",bf).Invoke(gv,new object[]{Vector2.zero,false});
                Debug.Log("OVERLAY_VIEW "+view.width+"x"+view.height+" "+view.format+" srgb="+view.sRGB+" root="+overlayRoot.position+" size="+Screen.width+"x"+Screen.height);
                RenderTexture.active=view;
#endif
            }
            var screen=new Texture2D(128,128,TextureFormat.RGBAFloat,false,true);screen.ReadPixels(new Rect(0,0,128,128),0,0);screen.Apply();RenderTexture.active=null;SaveImage("overlay"+state,screen);
            if(state<5)Readback("overlay"+state,or,om,state>=3?diagnostic:standard);
            else{var ocr=overlayImage.canvasRenderer;Value("overlay"+state+".clip",ocr.hasRectClipping?"true":"false");Vector("overlay"+state+".softness",ocr.clippingSoftness);Value("overlay"+state+".cull",ocr.cull?"true":"false");}
        }
        DestroyImmediate(overlayRoot.gameObject);overlayRoot=null;DestroyImmediate(om);
        DestroyImmediate(standard);DestroyImmediate(diagnostic);DestroyImmediate(noVariant);DestroyImmediate(camera.gameObject);DestroyImmediate(target);
        File.WriteAllText(Path.Combine(directory,"values.jsonl"),records.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
