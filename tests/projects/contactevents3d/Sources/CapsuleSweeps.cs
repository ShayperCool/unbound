using System;
using System.IO;
using System.Globalization;
using UnityEngine;
public static class CapsuleSweeps
{
    static StreamWriter file;static string phase;
    static void Row(string key,object value)
    {
        string v=value is bool ? ((bool)value?"true":"false") : Convert.ToString(value,CultureInfo.InvariantCulture);
        file.WriteLine("{\"key\":\""+phase+"/"+key+"\",\"value\":"+v+"}");
    }
    static void Vec(string key,Vector3 v){Row(key+"/x",v.x);Row(key+"/y",v.y);Row(key+"/z",v.z);}
    static Vector3 Axis(int a)=>a==0?Vector3.right:a==1?Vector3.up:Vector3.forward;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output=Environment.GetEnvironmentVariable("CAPSULE_SWEEPS_OUT");if(string.IsNullOrEmpty(output))return;
        file=new StreamWriter(output);file.AutoFlush=true;Physics.simulationMode=SimulationMode.Script;
        foreach(string geometry in new[]{"capsule","sphere","box"})foreach(int axis in new[]{0,1,2})foreach(float height in new[]{1f,2f,3f})
        {
            var go=new GameObject("target");Collider target;
            if(geometry=="capsule"){var c=go.AddComponent<CapsuleCollider>();c.direction=axis;c.height=height;target=c;}
            else if(geometry=="sphere")target=go.AddComponent<SphereCollider>();
            else target=go.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            var forward=Axis((axis+2)%3);var side=Axis((axis+1)%3);
            foreach(int castAxis in new[]{0,1,2})foreach(bool reverse in new[]{false,true})foreach(float offset in new[]{-1f,-.5f,0f,.5f,1f})
            {
                phase=geometry+"/axis"+axis+"/height"+height+"/cast"+castAxis+"/reverse"+reverse+"/offset"+offset;
                var a=-forward*10+Axis(axis)*offset+side*.2f;var b=a+Axis(castAxis)*.8f;
                RaycastHit h;bool hit=Physics.CapsuleCast(reverse?b:a,reverse?a:b,.25f,forward,out h,30);
                Row("hit",hit);if(hit){Vec("position",h.point);Vec("normal",h.normal);Row("distance",h.distance);Row("identity",h.collider==target);}
            }
            UnityEngine.Object.DestroyImmediate(go);
        }
        file.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
