using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class CanvasLifecycleProbe : MonoBehaviour
{
    static readonly StringBuilder records = new StringBuilder();
    public static readonly List<string> events = new List<string>();
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector2 v) => "[" + F(v.x) + "," + F(v.y) + "]";
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string R(Rect r) => "[" + F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height) + "]";
    static void Value(string k, string v) => records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(v).Append("}\n");
    public static void Event(Component c, string name) { if(c.name.StartsWith("L")) events.Add(c.name + "." + name); }
    static void Snap(string k, RectTransform t)
    {
        var c = t.GetComponent<Canvas>();
        Value(k+".localPosition", V(t.localPosition)); Value(k+".position", V(t.position));
        Value(k+".localRotation", Q(t.localRotation)); Value(k+".rotation", Q(t.rotation));
        Value(k+".localScale", V(t.localScale)); Value(k+".lossyScale", V(t.lossyScale));
        Value(k+".anchorMin", V(t.anchorMin)); Value(k+".anchorMax", V(t.anchorMax));
        Value(k+".pivot", V(t.pivot)); Value(k+".sizeDelta", V(t.sizeDelta));
        Value(k+".anchoredPosition3D", V(t.anchoredPosition3D)); Value(k+".rect", R(t.rect));
        Value(k+".offsetMin", V(t.offsetMin)); Value(k+".offsetMax", V(t.offsetMax));
        Value(k+".driver", "\""+(t.drivenByObject ? t.drivenByObject.GetType().Name : "null")+"\"");
        var corners = new Vector3[4]; t.GetLocalCorners(corners);
        for(int i=0;i<4;i++) Value(k+".localCorner"+i,V(corners[i]));
        t.GetWorldCorners(corners); for(int i=0;i<4;i++) Value(k+".worldCorner"+i,V(corners[i]));
        if(c) {
            Value(k+".mode", "\""+c.renderMode+"\""); Value(k+".root",c.isRootCanvas?"true":"false");
            Value(k+".enabled",c.isActiveAndEnabled?"true":"false"); Value(k+".scaleFactor",F(c.scaleFactor));
            Value(k+".planeDistance",F(c.planeDistance)); Value(k+".pixelRect",R(c.pixelRect));
        }
        Value(k+".events", "["+string.Join(",",events.ConvertAll(e=>"\""+e+"\""))+ "]"); events.Clear();
    }
    static RectTransform New(string name, bool active, bool ctor)
    {
        var go = ctor ? new GameObject(name,typeof(RectTransform),typeof(CanvasLifecycleEvents),typeof(Canvas)) : new GameObject(name,typeof(RectTransform));
        if(!active) go.SetActive(false);
        if(!ctor) go.AddComponent<CanvasLifecycleEvents>();
        return (RectTransform)go.transform;
    }
    static void Author(RectTransform t)
    {
        t.anchorMin=new Vector2(.2f,.3f); t.anchorMax=new Vector2(.7f,.8f);
        t.pivot=new Vector2(.25f,.75f); t.sizeDelta=new Vector2(173,91);
        t.localPosition=new Vector3(13,27,4); t.localRotation=Quaternion.Euler(0,0,30);
        t.localScale=new Vector3(2,3,1.5f);
    }
    static void Step(string k, RectTransform t, Action a) { a(); Snap(k,t); }
    IEnumerator Start()
    {
        yield return null;
        RectTransform.reapplyDrivenProperties += Driven;
        Value("screen",V(new Vector2(Screen.width,Screen.height)));
        // Constructors are always initially active; inactive AddComponent tests set the mode before activation.
        for(int ctor=0;ctor<2;ctor++) for(int active=0;active<2;active++) for(int mode=0;mode<3;mode++)
        {
            string k="creation."+ctor+"."+active+"."+mode;
            events.Clear(); var t=New("LRoot",active!=0,ctor!=0); Snap(k+".new",t);
            Author(t); Snap(k+".authored",t);
            var c=t.GetComponent<Canvas>(); if(!c) c=t.gameObject.AddComponent<Canvas>(); Snap(k+".add",t);
            Step(k+".mode",t,()=>c.renderMode=(RenderMode)mode);
            Step(k+".forceInactive",t,Canvas.ForceUpdateCanvases);
            Step(k+".activate",t,()=>t.gameObject.SetActive(true));
            Step(k+".force",t,Canvas.ForceUpdateCanvases);
            Step(k+".forceAgain",t,Canvas.ForceUpdateCanvases);
            Step(k+".disable",t,()=>c.enabled=false);
            Step(k+".authorDisabled",t,()=>Author(t));
            Step(k+".forceDisabled",t,Canvas.ForceUpdateCanvases);
            Step(k+".enable",t,()=>c.enabled=true);
            Step(k+".forceEnabled",t,Canvas.ForceUpdateCanvases);
            DestroyImmediate(t.gameObject);
        }
        for(int mode=0;mode<3;mode++)
        {
            events.Clear(); string k="transition."+mode;
            var t=New("LTransition",true,false); Author(t); var c=t.gameObject.AddComponent<Canvas>(); c.renderMode=(RenderMode)mode;
            Canvas.ForceUpdateCanvases(); Snap(k+".initial",t);
            Step(k+".author",t,()=>Author(t));
            Step(k+".rectForce",t,t.ForceUpdateRectTransforms);
            Step(k+".force",t,Canvas.ForceUpdateCanvases);
            for(int next=0;next<3;next++) {
                Step(k+".to"+next,t,()=>c.renderMode=(RenderMode)next);
                Step(k+".force"+next,t,Canvas.ForceUpdateCanvases);
            }
            DestroyImmediate(t.gameObject);
        }
        for(int stays=0;stays<2;stays++)
        {
            events.Clear(); string k="nested."+stays;
            var root=New("LParent",true,true); root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace; Author(root);
            var t=New("LChild",true,true); Author(t); Snap(k+".new",t);
            Step(k+".parent",t,()=>t.SetParent(root,stays!=0));
            Step(k+".force",t,Canvas.ForceUpdateCanvases);
            Step(k+".overlayRoot",root,()=>root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay);
            Snap(k+".childImmediate",t); Step(k+".forceOverlay",t,Canvas.ForceUpdateCanvases);
            Step(k+".disableRoot",t,()=>root.GetComponent<Canvas>().enabled=false);
            Step(k+".forceRootDisabled",t,Canvas.ForceUpdateCanvases);
            Step(k+".enableRoot",t,()=>root.GetComponent<Canvas>().enabled=true);
            Step(k+".detach",t,()=>t.SetParent(null,stays!=0));
            Step(k+".forceDetached",t,Canvas.ForceUpdateCanvases);
            Step(k+".reparent",t,()=>t.SetParent(root,stays!=0));
            Step(k+".forceReparented",t,Canvas.ForceUpdateCanvases);
            DestroyImmediate(root.gameObject);
        }
        var camera=new GameObject("Camera").AddComponent<Camera>(); camera.transform.position=new Vector3(5,7,-10); camera.transform.rotation=Quaternion.Euler(0,0,15);
        camera.orthographic=true; camera.orthographicSize=12; camera.rect=new Rect(.1f,.2f,.6f,.5f);
        var rt=new RenderTexture(320,240,24); camera.targetTexture=rt;
        var screen=New("LCamera",true,true); Author(screen); var cv=screen.GetComponent<Canvas>();
        events.Clear(); Snap("camera.created",screen);
        Step("camera.mode",screen,()=>cv.renderMode=RenderMode.ScreenSpaceCamera);
        Step("camera.camera",screen,()=>cv.worldCamera=camera);
        Step("camera.plane",screen,()=>cv.planeDistance=7);
        Step("camera.scale",screen,()=>cv.scaleFactor=2);
        Step("camera.force",screen,Canvas.ForceUpdateCanvases);
        Step("camera.resize",screen,()=>{rt.Release();rt.width=640;rt.height=360;rt.Create();});
        Step("camera.forceResize",screen,Canvas.ForceUpdateCanvases);
        Step("camera.perspective",screen,()=>camera.orthographic=false);
        Step("camera.forcePerspective",screen,Canvas.ForceUpdateCanvases);
        Step("camera.null",screen,()=>cv.worldCamera=null);
        Step("camera.forceNull",screen,Canvas.ForceUpdateCanvases);
        DestroyImmediate(screen.gameObject); DestroyImmediate(camera.gameObject); DestroyImmediate(rt);
        // Observe ordinary frame progression without ForceUpdateCanvases.
        events.Clear(); var frame=New("LFrame",true,true); Author(frame); Snap("frame.created",frame);
        yield return null; Snap("frame.nextUpdate",frame);
        yield return null; Snap("frame.secondUpdate",frame);
        Step("frame.world",frame,()=>frame.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace);
        Step("frame.worldAuthor",frame,()=>Author(frame));
        yield return null; Snap("frame.worldNextUpdate",frame);
        Step("frame.overlay",frame,()=>frame.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay);
        yield return null; Snap("frame.overlayNextUpdate",frame);
        DestroyImmediate(frame.gameObject);
        foreach(var name in new[]{"SerializedWorld","SerializedOverlay","SerializedInactive"}) {
            events.Clear(); var go=GameObject.Find(name); if(go) Snap("scene."+name,(RectTransform)go.transform);
        }
        var prefab=Resources.Load<GameObject>("Lifecycle"); events.Clear(); var clone=Instantiate(prefab); clone.name="LClone";
        Snap("prefab.clone",(RectTransform)clone.transform); Step("prefab.force",(RectTransform)clone.transform,Canvas.ForceUpdateCanvases);
        var copy=Instantiate(clone); copy.name="LCopy"; Snap("clone.clone",(RectTransform)copy.transform);
        DestroyImmediate(clone);DestroyImmediate(copy);
        RectTransform.reapplyDrivenProperties -= Driven;
        string output=Environment.GetEnvironmentVariable("CANVAS_LIFECYCLE_OUT")??"/tmp/canvas-lifecycle";
        Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"values.jsonl"),records.ToString());
        Debug.Log("Canvas lifecycle recorded to "+output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Driven(RectTransform t) => Event(t,"reapply");
}
