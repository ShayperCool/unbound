using System;
using System.IO;
using UnityEngine;
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output=Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if(string.IsNullOrEmpty(output))return;
        Physics2D.simulationMode=SimulationMode2D.Script;
        Physics2D.OverlapPoint(Vector2.zero);
        foreach(string mode in new[]{"before","after","after-sync","after-edit"})
        foreach(float offset in new[]{.001f,.1f})
        {
            ProbeLog.Begin(mode+"."+offset);
            Physics2D.defaultContactOffset=mode=="before"?offset:.01f;
            var go=new GameObject("box");
            var collider=go.AddComponent<BoxCollider2D>();
            collider.size=new Vector2(4,1);
            if(mode=="after-sync")Physics2D.SyncTransforms();
            Physics2D.defaultContactOffset=offset;
            if(mode=="after-edit")collider.size=new Vector2(4.2f,1);
            Physics2D.SyncTransforms();
            var h=Physics2D.BoxCast(new Vector2(0,4),new Vector2(2,.8f),0,Vector2.down,5);
            ProbeLog.Value("collider",h.collider?h.collider.name:"none");
            ProbeLog.Value("point",h.point);ProbeLog.Value("normal",h.normal);
            ProbeLog.Value("centroid",h.centroid);ProbeLog.Value("distance",h.distance);
            UnityEngine.Object.DestroyImmediate(go);
        }
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
