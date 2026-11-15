using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class EnabledResizeProbe : MonoBehaviour
{
    readonly StringBuilder rows = new StringBuilder();
    readonly List<int> renderLatches = new List<int>();
    int draws;
    void Row(string key, params float[] values) => rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":[").Append(string.Join(",",Array.ConvertAll(values,v=>v.ToString("R",CultureInfo.InvariantCulture)))).Append("]}\n");
    void Latches(string key) => rows.Append("{\"k\":\"").Append(key).Append("\",\"v\":[").Append(string.Join(",",renderLatches)).Append("]}\n");
    IEnumerator Start()
    {
        string output=Environment.GetEnvironmentVariable("SCREENREAD_PROBE_OUT");
        bool engine=typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder")!=null;
        var camera=new GameObject("EnabledResizeCamera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.red;
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        EditorWindow window=null;
        if(!engine)
        {
            window=(EditorWindow)EditorUtility.InstanceIDToObject(SessionState.GetInt("GameViewProbe.window",0));
            type.GetProperty("showToolbar",flags).SetValue(window,false);window.minSize=Vector2.one;
            window.position=new Rect(20,20,640,360);window.Focus();
        }
        PlayModeWindow.SetCustomRenderingResolution(320,320,"EnabledResizeProbe");
        for(int i=0;i<15;i++){if(window!=null)window.Repaint();yield return null;}
        Row("enabled.before.screen",Screen.width,Screen.height);
        Camera.CameraCallback count=drawn=>{if(drawn==camera){draws++;renderLatches.Add(OnDemandRendering.willCurrentFrameRender?1:0);}};
        Camera.onPostRender+=count;
        OnDemandRendering.renderFrameInterval=100000;
        yield return null;
        camera.backgroundColor=Color.blue;
        draws=0;renderLatches.Clear();
        for(int i=0;i<15;i++){if(window!=null)window.Repaint();yield return null;}
        Row("enabled.noResize.draws",draws);Latches("enabled.noResize.renderLatches");
        Row("enabled.noResize.willRender",OnDemandRendering.willCurrentFrameRender?1:0);
        draws=0;renderLatches.Clear();
        if(engine)
        {
            File.WriteAllText(Path.Combine(output,"ready"),"ready");
            while(!File.Exists(Path.Combine(output,"resize")))yield return null;
        }
        else window.position=new Rect(20,20,800,600);
        for(int i=0;i<15;i++){if(window!=null)window.Repaint();yield return null;}
        Row("enabled.after.draws",draws);Latches("enabled.after.renderLatches");
        Row("enabled.after.willRender",OnDemandRendering.willCurrentFrameRender?1:0);
        Row("enabled.after.screen",Screen.width,Screen.height);
        var rect=camera.pixelRect;Row("enabled.after.camera",rect.x,rect.y,rect.width,rect.height);
        Rect target;
        if(engine)target=(Rect)typeof(Screen).GetProperty("PresentationRect",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        else target=(Rect)type.GetProperty("targetInView",flags).GetValue(window);
        Row("enabled.after.target",target.x,target.y,target.width,target.height);
        Row("enabled.after.cameraEnabled",camera.enabled?1:0);
        if(engine)
        {
            File.WriteAllText(Path.Combine(output,"resized"),"resized");
            while(!File.Exists(Path.Combine(output,"finish")))yield return null;
        }
        int beforeResume=draws;
        OnDemandRendering.renderFrameInterval=1;
        for(int i=0;i<15;i++){if(window!=null)window.Repaint();yield return null;}
        Row("enabled.resumed.didRender",draws>beforeResume?1:0);
        Row("enabled.resumed.willRender",OnDemandRendering.willCurrentFrameRender?1:0);
        Camera.onPostRender-=count;
        rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(output,"values.jsonl"),rows.ToString());
        if(engine)EditorApplication.Exit(0);else GameViewProbe.finished?.Invoke(0);
    }
}
