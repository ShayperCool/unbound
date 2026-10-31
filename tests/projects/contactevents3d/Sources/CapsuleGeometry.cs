using System;
using System.IO;
using System.Globalization;
using UnityEngine;
using Unity.Collections;
public sealed class CapsuleDestroyReceiver : MonoBehaviour
{
    public static bool requested;
    void OnCollisionEnter(Collision c)
    {
        if (!requested) return;
        UnityEngine.Object.DestroyImmediate(gameObject);
        CapsuleGeometry.Row("legacyAlive", gameObject != null);
    }
}
public static class CapsuleGeometry
{
    static StreamWriter file;static string phase;static int serial;
    static GameObject target;static bool destroy;
    public static void Row(string key,object value)
    {
        string v=value is bool ? ((bool)value ? "true":"false") : Convert.ToString(value,CultureInfo.InvariantCulture);
        file.WriteLine("{\"key\":\""+phase+"/"+(serial++)+"/"+key+"\",\"value\":"+v+"}");
    }
    static void Vec(string key,Vector3 v){Row(key+"/x",v.x);Row(key+"/y",v.y);Row(key+"/z",v.z);}
    static void Event(PhysicsScene scene,NativeArray<ContactPairHeader>.ReadOnly headers)
    {
        Row("headers",headers.Length);
        for(int i=0;i<headers.Length;i++)for(int j=0;j<headers[i].pairCount;j++)
        {
            var p=headers[i].GetContactPair(j);Row("points",p.contactCount);
            for(int k=0;k<p.contactCount;k++)
            {
                var q=p.GetContactPoint(k);Vec("position",q.position);Vec("normal",q.normal);Row("separation",q.separation);
            }
        }
        if(destroy){UnityEngine.Object.DestroyImmediate(target);Row("publicAlive",target!=null);}
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output=Environment.GetEnvironmentVariable("CAPSULE_GEOMETRY_OUT");if(string.IsNullOrEmpty(output))return;
        file=new StreamWriter(output);file.AutoFlush=true;Physics.simulationMode=SimulationMode.Script;Physics.gravity=Vector3.zero;
        phase="defaults";var a=new GameObject("add");var c=a.AddComponent<CapsuleCollider>();Row("addHeight",c.height);Row("addRadius",c.radius);
        UnityEngine.Object.DestroyImmediate(a);a=GameObject.CreatePrimitive(PrimitiveType.Capsule);c=a.GetComponent<CapsuleCollider>();Row("primitiveHeight",c.height);Row("primitiveRadius",c.radius);UnityEngine.Object.DestroyImmediate(a);
        Physics.ContactEvent+=Event;
        foreach(int direction in new[]{0,1,2})foreach(float height in new[]{0f,1f,1.00002f,2f})
        {
            phase="axis"+direction+"-height"+height.ToString("R",CultureInfo.InvariantCulture);serial=0;
            a=new GameObject("A");c=a.AddComponent<CapsuleCollider>();c.direction=direction;c.height=height;c.providesContacts=true;
            Physics.SyncTransforms();Vec("boundsSize",c.bounds.size);
            var axis=direction==0 ? Vector3.right : direction==1 ? Vector3.up : Vector3.forward;
            foreach(float sign in new[]{-1f,1f})
            {
                RaycastHit hit;bool got=c.Raycast(new Ray(axis*sign*3,-axis*sign),out hit,10);Row("hit",got);if(got)Vec("endpoint",hit.point);
            }
            var b=new GameObject("B");b.transform.position=(direction==0 ? Vector3.forward : Vector3.right)*.75f;
            var rb=b.AddComponent<Rigidbody>();rb.useGravity=false;var cb=b.AddComponent<CapsuleCollider>();cb.direction=direction;cb.height=height;cb.providesContacts=true;
            Physics.SyncTransforms();Physics.Simulate(.02f);UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);Physics.Simulate(.02f);
        }
        foreach(bool publicCallback in new[]{true,false})
        {
            phase=publicCallback?"destroyPublic":"destroyLegacy";serial=0;
            a=new GameObject("support");a.AddComponent<BoxCollider>().providesContacts=true;
            target=new GameObject("target");target.transform.position=Vector3.right*.75f;target.AddComponent<Rigidbody>().useGravity=false;target.AddComponent<BoxCollider>().providesContacts=true;target.AddComponent<CapsuleDestroyReceiver>();
            destroy=publicCallback;CapsuleDestroyReceiver.requested=!publicCallback;
            Physics.SyncTransforms();Physics.Simulate(.02f);Row("afterAlive",target!=null);
            destroy=false;CapsuleDestroyReceiver.requested=false;UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(target);Physics.Simulate(.02f);
        }
        Physics.ContactEvent-=Event;file.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
