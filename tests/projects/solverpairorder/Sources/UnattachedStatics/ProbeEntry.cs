using System;
using System.IO;
using UnityEngine;
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output=Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");if(string.IsNullOrEmpty(output))return;
        Physics2D.simulationMode=SimulationMode2D.Script;Physics2D.gravity=Vector2.zero;
        var h=new GameObject("H");h.transform.position=new Vector3(100,0,0);var rb=h.AddComponent<Rigidbody2D>();rb.constraints=RigidbodyConstraints2D.FreezeAll;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;var hc=h.AddComponent<BoxCollider2D>();
        var l=new GameObject("L");l.transform.position=new Vector3(99.1f,0,0);var lc=l.AddComponent<BoxCollider2D>();
        var r=new GameObject("R");r.transform.position=new Vector3(100.9f,0,0);var rc=r.AddComponent<BoxCollider2D>();
        Step("initial",rb,lc,rc);
        l.transform.position=new Vector3(98,0,0);Step("move-away",rb,lc,rc);
        l.transform.position=new Vector3(99.1f,0,0);Step("move-back",rb,lc,rc);
        l.transform.rotation=Quaternion.Euler(0,0,23);Step("rotate",rb,lc,rc);
        lc.size=new Vector2(1,1.2f);Step("resize",rb,lc,rc);
        lc.enabled=false;Step("disable",rb,lc,rc);
        lc.enabled=true;Step("enable",rb,lc,rc);
        UnityEngine.Object.DestroyImmediate(lc);Step("destroy",rb,null,rc);
        lc=l.AddComponent<BoxCollider2D>();Step("recreate",rb,lc,rc);
        UnityEngine.Object.DestroyImmediate(l);UnityEngine.Object.DestroyImmediate(r);UnityEngine.Object.DestroyImmediate(h);
        PoseLifetimeControls();
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void PoseLifetimeControls()
    {
        foreach(bool sensor in new[] {false,true})
        foreach(var kind in new[] {RigidbodyType2D.Dynamic,RigidbodyType2D.Kinematic})
        {
            ProbeLog.Begin("static-pose/"+sensor+"/"+kind);
            var moved=new GameObject("moved"); moved.transform.position=new Vector3(100.5f,0,0);
            var target=moved.AddComponent<BoxCollider2D>(); target.size=new Vector2(2,2); target.isTrigger=sensor;
            var go=new GameObject("body"); go.transform.position=new Vector3(100,0,0);
            var body=go.AddComponent<Rigidbody2D>(); body.bodyType=kind; body.useFullKinematicContacts=true; body.constraints=RigidbodyConstraints2D.FreezeAll;
            var collider=go.AddComponent<CircleCollider2D>(); collider.radius=.25f;
            var peerGo=new GameObject("peer"); peerGo.transform.position=new Vector3(120,0,0);
            var peer=peerGo.AddComponent<Rigidbody2D>(); peer.constraints=RigidbodyConstraints2D.FreezeAll;
            peerGo.AddComponent<CircleCollider2D>().radius=.25f;
            var peerStatic=new GameObject("peer-static"); peerStatic.transform.position=new Vector3(120.5f,0,0); peerStatic.AddComponent<BoxCollider2D>().size=new Vector2(2,2);
            Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); Physics2D.Simulate(.02f); body.Sleep(); peer.Sleep();
            PoseSnapshot("before",body,peer,collider,target);
            moved.transform.position=new Vector3(105.5f,0,0); Physics2D.SyncTransforms(); PoseSnapshot("sync-out",body,peer,collider,target);
            Physics2D.Simulate(.02f); PoseSnapshot("step-out",body,peer,collider,target);
            body.Sleep(); peer.Sleep(); moved.transform.position=new Vector3(100.5f,0,0); Physics2D.SyncTransforms(); PoseSnapshot("sync-in",body,peer,collider,target);
            Physics2D.Simulate(.02f); PoseSnapshot("step-in",body,peer,collider,target);
            UnityEngine.Object.DestroyImmediate(moved); UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(peerGo); UnityEngine.Object.DestroyImmediate(peerStatic);
        }
    }
    static void PoseSnapshot(string phase,Rigidbody2D body,Rigidbody2D peer,Collider2D collider,Collider2D target)
    {
        ProbeLog.Value(phase+"/sleep",body.IsSleeping()); ProbeLog.Value(phase+"/peerSleep",peer.IsSleeping());
        ProbeLog.Value(phase+"/touching",collider.IsTouching(target));
        Contacts(phase+"/body",body.GetContacts); Contacts(phase+"/target",target.GetContacts); Contacts(phase+"/peer",peer.GetContacts);
        var colliders=new Collider2D[8]; int count=body.GetContacts(colliders); ProbeLog.Value(phase+"/bodyColliders.count",count);
        for(int i=0;i<count;i++) ProbeLog.Value(phase+"/bodyColliders/"+i,colliders[i].name);
        count=target.GetContacts(colliders); ProbeLog.Value(phase+"/targetColliders.count",count);
        for(int i=0;i<count;i++) ProbeLog.Value(phase+"/targetColliders/"+i,colliders[i].name);
    }
    static void Step(string phase,Rigidbody2D rb,Collider2D l,Collider2D r)
    {
        ProbeLog.Begin(phase);Physics2D.SyncTransforms();Physics2D.Simulate(.02f);
        ProbeLog.Value("position",rb.position);ProbeLog.Value("rotation",rb.rotation);
        Contacts("body",rb.GetContacts);if(l)Contacts("left",l.GetContacts);if(r)Contacts("right",r.GetContacts);
    }
    delegate int Collect(ContactPoint2D[] output);
    static void Contacts(string key,Collect collect)
    {
        var points=new ContactPoint2D[16];int count=collect(points);ProbeLog.Value(key+".count",count);
        for(int i=0;i<count;i++)
        {
            var p=points[i];string k=key+"."+i;ProbeLog.Value(k+".collider",p.collider.name);ProbeLog.Value(k+".other",p.otherCollider.name);ProbeLog.Value(k+".normal",p.normal);ProbeLog.Value(k+".point",p.point);ProbeLog.Value(k+".separation",p.separation);
        }
    }
}
