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
        foreach(float margin in new[]{.001f,.01f,.1f})
        foreach(float scale in new[]{.5f,1f,2f})
        {
            var go=new GameObject("box");
            go.AddComponent<BoxCollider2D>().size=new Vector2(4,1)*scale;
            Physics2D.defaultContactOffset=margin;
            Physics2D.SyncTransforms();
            foreach(float angle in new[]{0f,.0005f,.0006f,.0007f,.0008f,.0009f,.001f,.0011f,.0012f,.0013f,.0014f,.0015f})
            {
                ProbeLog.Begin(margin+"."+scale+"."+angle);
                Hit("box",Physics2D.BoxCast(new Vector2(0,4)*scale,new Vector2(2,.8f)*scale,angle,Vector2.down,5*scale));
                Hit("capsule",Physics2D.CapsuleCast(new Vector2(0,4)*scale,new Vector2(2,.8f)*scale,CapsuleDirection2D.Horizontal,angle,Vector2.down,5*scale));
            }
            UnityEngine.Object.DestroyImmediate(go);
        }
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Hit(string k,RaycastHit2D h)
    {
        ProbeLog.Value(k+".collider",h.collider?h.collider.name:"none");
        ProbeLog.Value(k+".point",h.point);ProbeLog.Value(k+".normal",h.normal);
        ProbeLog.Value(k+".centroid",h.centroid);ProbeLog.Value(k+".distance",h.distance);
    }
}
