using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProbeRunner
{
    static ProbeRunner()
    {
        var output=Environment.GetEnvironmentVariable("RENDER_API_OUT");
        if(output==null || typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder")!=null)return;
        if(SessionState.GetBool("RenderApi.finished",false))
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall+=()=>EditorApplication.Exit(SessionState.GetInt("RenderApi.code",5));
            return;
        }
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode)BindRuntime(output);
        };
        if(EditorApplication.isPlaying)BindRuntime(output);
    }
    static void BindRuntime(string output)
    {
        double start=EditorApplication.timeSinceStartup;
        EditorApplication.update+=()=>
        {
            if(EditorApplication.timeSinceStartup-start<300)return;
            File.WriteAllText(Path.Combine(output,"watchdog.txt"),"Play mode did not finish\n");
            EditorApplication.Exit(4);
        };
        RenderApiProbe.setGameGizmos=enabled=>
        {
            var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            SetGizmos(EditorWindow.GetWindow(type),enabled);
        };
        RenderApiProbe.finished=code=>
        {
            SessionState.SetBool("RenderApi.finished",true);
            SessionState.SetInt("RenderApi.code",code);
            EditorApplication.playModeStateChanged+=state=>
            {
                if(state==PlayModeStateChange.EnteredEditMode)EditorApplication.Exit(code);
            };
            EditorApplication.ExitPlaymode();
        };
    }
    static void SetGizmos(EditorWindow view,bool enabled)
    {
        var flags=BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type=view.GetType();
        var field=type.GetField("m_Gizmos",flags);
        if(field==null)throw new InvalidOperationException("Game view gizmo toggle was not found");
        field.SetValue(view,enabled);
        PropertyInfo property=null;
        while(type!=null && property==null) { property=type.GetProperty("showGizmos",flags);type=type.BaseType; }
        if(property==null)throw new InvalidOperationException("Game view gizmo rendering property was not found");
        property.SetValue(view,enabled);
        if((bool)field.GetValue(view)!=enabled || (bool)property.GetValue(view)!=enabled)
            throw new InvalidOperationException("Game view gizmo toggle did not change");
        view.Repaint();
    }
    public static void Record()
    {
        int ticks=0;
        EditorApplication.CallbackFunction wait=null;
        wait=()=>
        {
            if(++ticks<30)return;
            EditorApplication.update-=wait;
            try
            {
                EditorSettings.asyncShaderCompilation=false;
                EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                PlayModeWindow.SetCustomRenderingResolution(96,64,"RenderApi");
                PlayModeWindow.SetPlayModeFocused(true);
                SetGizmos(EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")),false);
                EditorApplication.EnterPlaymode();
            }
            catch(Exception e) { Debug.LogException(e);EditorApplication.Exit(6); }
        };
        EditorApplication.update+=wait;
    }
}
