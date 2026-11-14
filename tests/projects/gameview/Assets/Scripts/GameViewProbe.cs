using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public class GameViewProbe : MonoBehaviour
{
    public static Action<int> finished;
    static readonly int[,] Cases = { {640,360,320,320}, {360,640,640,320}, {640,360,160,90}, {640,360,1280,720}, {641,361,320,200}, {360,640,180,640} };
    readonly StringBuilder values = new StringBuilder();
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    void Row(string key, params float[] v) => values.Append("{\"k\":\"").Append(key).Append("\",\"v\":[").Append(string.Join(",", Array.ConvertAll(v, F))).Append("]}\n");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("SCREENREAD_PROBE_OUT") != null)
            new GameObject("GameViewProbe").AddComponent<GameViewProbe>();
    }
    IEnumerator Start()
    {
        if (typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null)
        {
            yield return Engine();
            yield break;
        }
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.red;
        var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        var window = (EditorWindow)EditorUtility.InstanceIDToObject(SessionState.GetInt("GameViewProbe.window",0));
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        type.GetProperty("showToolbar", flags).SetValue(window, false);
        window.minSize = Vector2.one;
        for (int c = 0; c < Cases.GetLength(0); c++)
        {
            window.position = new Rect(20,20,Cases[c,0],Cases[c,1]);
            PlayModeWindow.SetCustomRenderingResolution((uint)Cases[c,2],(uint)Cases[c,3],"GameViewProbe");
            window.Focus();
            for (int i=0;i<15;i++) { window.Repaint(); yield return null; }
            var view = (Rect)type.GetProperty("viewInWindow", flags).GetValue(window);
            var target = (Rect)type.GetProperty("targetInView", flags).GetValue(window);
            Row(c+".view", view.x,view.y,view.width,view.height);
            Row(c+".target",target.x,target.y,target.width,target.height);
            Row(c+".screen", Screen.width,Screen.height);
            var rect=camera.pixelRect;
            Row(c+".camera",rect.x,rect.y,rect.width,rect.height);
            for (int p=0;p<3;p++)
            {
                var mouse = new Vector2(view.width*(p+1)/4,view.height*(p+1)/4)+view.position;
                window.Focus();
                System.Diagnostics.Process.Start("xdotool", "mousemove --sync " + (int)(window.position.x+mouse.x) + " " + (int)(window.position.y+mouse.y)).WaitForExit();
                yield return new WaitForSecondsRealtime(0.5f);
                for(int i=0;i<10;i++) { window.Repaint(); yield return null; }
                Row(c+".pointer"+p,Input.mousePosition.x,Input.mousePosition.y);
            }
        }
        int draws=0;
        Camera.CameraCallback count = drawn => { if (drawn==camera) draws++; };
        Camera.onPostRender += count;
        window.position=new Rect(20,20,640,360);
        PlayModeWindow.SetCustomRenderingResolution(320,320,"GameViewProbe");
        for(int i=0;i<15;i++) { window.Repaint(); yield return null; }
        var before=(Rect)type.GetProperty("targetInView",flags).GetValue(window);
        Row("retained.before.target",before.x,before.y,before.width,before.height);
        Row("retained.before.screen",Screen.width,Screen.height);
        OnDemandRendering.renderFrameInterval=100000;
        yield return null;
        camera.enabled=false;
        int startDraws=draws;
        window.position=new Rect(20,20,800,600);
        for(int i=0;i<15;i++) { window.Repaint(); yield return null; }
        var afterView=(Rect)type.GetProperty("viewInWindow",flags).GetValue(window);
        var after=(Rect)type.GetProperty("targetInView",flags).GetValue(window);
        Row("retained.after.view",afterView.x,afterView.y,afterView.width,afterView.height);
        Row("retained.after.target",after.x,after.y,after.width,after.height);
        Row("retained.after.screen",Screen.width,Screen.height);
        var afterCamera=camera.pixelRect;
        Row("retained.after.camera",afterCamera.x,afterCamera.y,afterCamera.width,afterCamera.height);
        Row("retained.after.draws",draws-startDraws);
        OnDemandRendering.renderFrameInterval=1;
        Camera.onPostRender -= count;
        values.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("SCREENREAD_PROBE_OUT"),"values.jsonl"),values.ToString());
        finished?.Invoke(0);
    }
    IEnumerator Engine()
    {
        string output = Environment.GetEnvironmentVariable("SCREENREAD_PROBE_OUT");
        int c = int.Parse(Environment.GetEnvironmentVariable("GAMEVIEW_CASE"));
        if (c>=6) { yield return RetainedEngine(output,c==6); yield break; }
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.red;
        PlayModeWindow.SetCustomRenderingResolution((uint)Cases[c,2], (uint)Cases[c,3], "GameViewProbe");
        for (int i=0;i<5;i++) yield return null;
        Row(c+".view",0,0,Cases[c,0],Cases[c,1]);
        var target = (Rect)typeof(Screen).GetProperty("PresentationRect",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        Row(c+".target",target.x,target.y,target.width,target.height);
        Row(c+".screen",Screen.width,Screen.height);
        var rect = camera.pixelRect;
        Row(c+".camera",rect.x,rect.y,rect.width,rect.height);
        File.WriteAllText(Path.Combine(output,"ready"),"ready");
        for (int p=0;p<3;p++)
        {
            while (!File.Exists(Path.Combine(output,"move"+p))) yield return null;
            for (int i=0;i<5;i++) yield return null;
            Row(c+".pointer"+p,Input.mousePosition.x,Input.mousePosition.y);
            File.WriteAllText(Path.Combine(output,"read"+p),"read");
        }
        File.WriteAllText(Path.Combine(output,"values.jsonl"),values.ToString());
        EditorApplication.Exit(0);
    }

    IEnumerator RetainedEngine(string output, bool fixedSize)
    {
        var camera=new GameObject("RetainedCamera").AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=Color.red;
        int draws=0;
        Camera.CameraCallback count = drawn => { if (drawn==camera) draws++; };
        Camera.onPostRender += count;
        if (fixedSize) PlayModeWindow.SetCustomRenderingResolution(320,320,"GameViewProbe");
        for(int i=0;i<5;i++) yield return null;
        var property=typeof(Screen).GetProperty("PresentationRect",BindingFlags.Static|BindingFlags.NonPublic);
        var before=(Rect)property.GetValue(null);
        Row("retained.before.target",before.x,before.y,before.width,before.height);
        Row("retained.before.screen",Screen.width,Screen.height);
        OnDemandRendering.renderFrameInterval=100000;
        yield return null;
        camera.enabled=false;
        int startDraws=draws;
        File.WriteAllText(Path.Combine(output,"ready"),"ready");
        while(!File.Exists(Path.Combine(output,"resize"))) yield return null;
        for(int i=0;i<10;i++) yield return null;
        Row("retained.after.view",0,0,800,600);
        var after=(Rect)property.GetValue(null);
        Row("retained.after.target",after.x,after.y,after.width,after.height);
        Row("retained.after.screen",Screen.width,Screen.height);
        var rect=camera.pixelRect;
        Row("retained.after.camera",rect.x,rect.y,rect.width,rect.height);
        Row("retained.after.draws",draws-startDraws);
        File.WriteAllText(Path.Combine(output,"resized"),"resized");
        while(!File.Exists(Path.Combine(output,"finish"))) yield return null;
        Camera.onPostRender -= count;
        OnDemandRendering.renderFrameInterval=1;
        File.WriteAllText(Path.Combine(output,"values.jsonl"),values.ToString());
        EditorApplication.Exit(0);
    }

}
