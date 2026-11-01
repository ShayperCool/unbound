using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class VelocityProbe
{
    static readonly List<Rigidbody2D> Bodies = new List<Rigidbody2D>();
    static string Scenario;
    public static int Step, Event;
    static Rigidbody2D Box(string name, float x, float y, bool dynamic=true, float width=1)
    {
        var o=new GameObject(name);o.transform.position=new Vector3(x,y,0);
        var b=o.AddComponent<Rigidbody2D>();b.bodyType=dynamic?RigidbodyType2D.Dynamic:RigidbodyType2D.Static;
        b.sleepMode=RigidbodySleepMode2D.NeverSleep;
        o.AddComponent<BoxCollider2D>().size=new Vector2(width,1);o.AddComponent<VelocityObserver>();
        Bodies.Add(b);return b;
    }
    public static void Contact(string key,ContactPoint2D c)
    {
        ProbeLog.Value(key+"/collider",c.collider.name);ProbeLog.Value(key+"/other",c.otherCollider.name);
        ProbeLog.Value(key+"/point",c.point);ProbeLog.Value(key+"/normal",c.normal);
        ProbeLog.Value(key+"/relativeVelocity",c.relativeVelocity);ProbeLog.Value(key+"/separation",c.separation);
        ProbeLog.Value(key+"/normalImpulse",c.normalImpulse);ProbeLog.Value(key+"/tangentImpulse",c.tangentImpulse);
    }
    static void Snapshot(string phase)
    {
        foreach(var b in Bodies)
        {
            string key="s"+Step+"/"+phase+"/"+b.name;
            ProbeLog.Value(key+"/position",b.position);ProbeLog.Value(key+"/velocity",b.linearVelocity);
            ProbeLog.Value(key+"/angularVelocity",b.angularVelocity);
            var array=new ContactPoint2D[16];int n=b.GetContacts(array);ProbeLog.Value(key+"/body-count",n);
            for(int i=0;i<n;i++)Contact(key+"/body/"+i,array[i]);
            var list=new List<ContactPoint2D>();b.GetComponent<Collider2D>().GetContacts(list);
            ProbeLog.Value(key+"/collider-count",list.Count);
            for(int i=0;i<list.Count;i++)Contact(key+"/collider/"+i,list[i]);
        }
        ManagedTrace.Record(Step,Scenario+"/"+phase,Bodies);
    }
    public static void Run(string output)
    {
        Physics2D.simulationMode=SimulationMode2D.Script;
        foreach(string kind in new[]{"head-on","stack","mixed-continuous","continuity-toggle"})
        {
            ProbeLog.Begin(kind);Step=Event=0;
            Scenario=kind;
            bool traceCase=Environment.GetEnvironmentVariable("SOLVER_PAIR_QUERY_CASE")==kind;
            // Debugger-only recording barriers; no simulation runs at these
            // temporary iteration values. Normal settings are restored below.
            if(traceCase)Physics2D.velocityIterations=63;
            if(kind=="head-on")
            {
                var a=Box("A",-.45f,0);var b=Box("B",.45f,0);
                a.linearVelocity=new Vector2(2,.3f);b.linearVelocity=new Vector2(-1,-.1f);
                a.angularVelocity=33;b.angularVelocity=-17;
            }
            else if(kind=="stack")
            {
                Box("ground",0,-.5f,false,30);
                for(int i=0;i<4;i++)Box("box"+i,.1f*i,.5f+1.01f*i);
            }
            else
            {
                Box("ordinary-a",100,0).linearVelocity=new Vector2(1,0);Box("ordinary-b",100.9f,0);
                var a=Box("continuous-a",110,0);var b=Box("continuous-b",110.9f,0);
                a.collisionDetectionMode=b.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
                a.linearVelocity=new Vector2(1,0);
                Box("sensor",100,0,false).GetComponent<Collider2D>().isTrigger=true;
            }
            Physics2D.gravity=kind=="stack"?new Vector2(0,-9.81f):Vector2.zero;
            Physics2D.velocityIterations=8;Physics2D.positionIterations=3;
            Snapshot("created");
            for(Step=1;Step<=8;Step++)
            {
                Snapshot("before");Physics2D.Simulate(.02f);Snapshot("after");
                if(kind=="head-on"&&Step==2)
                {
                    Bodies[0].linearVelocity=new Vector2(5,-3);Bodies[1].linearVelocity=new Vector2(-7,2);
                    Snapshot("edited");
                }
                if(kind=="continuity-toggle")
                {
                    if(Step==2)Physics2D.IgnoreCollision(Bodies[0].GetComponent<Collider2D>(),Bodies[1].GetComponent<Collider2D>(),true);
                    if(Step==3)Physics2D.IgnoreCollision(Bodies[0].GetComponent<Collider2D>(),Bodies[1].GetComponent<Collider2D>(),false);
                    if(Step==4||Step==6)
                    {
                        var mode=Step==4?CollisionDetectionMode2D.Discrete:CollisionDetectionMode2D.Continuous;
                        Bodies[2].collisionDetectionMode=Bodies[3].collisionDetectionMode=mode;
                        Snapshot("mode-edited");
                    }
                }
            }
            if(traceCase)Physics2D.velocityIterations=62;
            foreach(var b in Bodies)UnityEngine.Object.DestroyImmediate(b.gameObject);Bodies.Clear();
        }
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
public class VelocityObserver:MonoBehaviour
{
    void Record(string kind,Collision2D c)
    {
        string key="e"+VelocityProbe.Event++;
        ProbeLog.Value(key+"/step",VelocityProbe.Step);ProbeLog.Value(key+"/kind",kind);
        ProbeLog.Value(key+"/self",name);ProbeLog.Value(key+"/other",c.collider.name);
        ProbeLog.Value(key+"/relativeVelocity",c.relativeVelocity);ProbeLog.Value(key+"/count",c.contactCount);
        for(int i=0;i<c.contactCount;i++)VelocityProbe.Contact(key+"/"+i,c.GetContact(i));
    }
    void OnCollisionEnter2D(Collision2D c)=>Record("enter",c);
    void OnCollisionStay2D(Collision2D c)=>Record("stay",c);
    void OnCollisionExit2D(Collision2D c)=>Record("exit",c);
}
