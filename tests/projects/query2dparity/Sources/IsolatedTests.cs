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
        var scales=new[]{.5f,1f,1f,1f,1f,2f};
        var angles=new[]{.001f,.0007f,.0008f,.0011f,.0012f,.001f};
        for(int i=0;i<scales.Length;i++)
        {
            Physics2D.defaultContactOffset=.01f;
            var go=new GameObject("box");
            go.AddComponent<BoxCollider2D>().size=new Vector2(4,1)*scales[i];
            Physics2D.SyncTransforms();
            var origin=new Vector2(0,4)*scales[i];var size=new Vector2(2,.8f)*scales[i];
            var h=i<3?Physics2D.BoxCast(origin,size,angles[i],Vector2.down,5*scales[i])
                :Physics2D.CapsuleCast(origin,size,CapsuleDirection2D.Horizontal,angles[i],Vector2.down,5*scales[i]);
            ProbeLog.Begin(i.ToString());
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
