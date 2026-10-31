using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class EnumerationProbe
{
    static readonly List<GameObject> Objects = new List<GameObject>();
    static Rigidbody2D body;
    static BoxCollider2D self, child;
    static readonly List<BoxCollider2D> Others = new List<BoxCollider2D>();
    static BoxCollider2D Box(string name, Vector3 position, bool dynamic = false)
    {
        var o = new GameObject(name); Objects.Add(o); o.transform.position = position;
        var b = o.AddComponent<Rigidbody2D>(); b.bodyType = dynamic ? RigidbodyType2D.Dynamic : RigidbodyType2D.Static;
        b.gravityScale = 0; b.constraints = RigidbodyConstraints2D.FreezeAll;
        b.sleepMode = RigidbodySleepMode2D.NeverSleep;
        return o.AddComponent<BoxCollider2D>();
    }
    public static void Run(string output)
    {
        Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
        foreach (string history in new[] { "remove-first", "remove-middle", "remove-last", "recreate-first", "compound", "compound-overlap", "delayed-touch", "unrelated" })
            Case(history);
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Points(string k, int n, IList<ContactPoint2D> points)
    {
        ProbeLog.Value(k+"/count", n);
        for (int i=0;i<n;i++)
        {
            var c=points[i]; string q=k+"/"+i;
            ProbeLog.Value(q+"/collider",c.collider.name); ProbeLog.Value(q+"/other",c.otherCollider.name);
            ProbeLog.Value(q+"/point",c.point); ProbeLog.Value(q+"/normal",c.normal);
            ProbeLog.Value(q+"/separation",c.separation); ProbeLog.Value(q+"/normalImpulse",c.normalImpulse);
            ProbeLog.Value(q+"/tangentImpulse",c.tangentImpulse);
        }
    }
    static void Colliders(string k,int n,IList<Collider2D> colliders)
    {
        ProbeLog.Value(k+"/count",n);
        for(int i=0;i<n;i++) ProbeLog.Value(k+"/"+i,colliders[i].name);
    }
    static void View(string k, Func<ContactPoint2D[],int> pointsArray, Func<List<ContactPoint2D>,int> pointsList,
        Func<Collider2D[],int> collidersArray, Func<List<Collider2D>,int> collidersList)
    {
        foreach(int capacity in new[]{0,1,2,3,5,16})
        {
            var p=new ContactPoint2D[capacity]; Points(k+"/array"+capacity,pointsArray(p),p);
            if(collidersArray!=null){var c=new Collider2D[capacity];Colliders(k+"/colliders"+capacity,collidersArray(c),c);}
        }
        var pl=new List<ContactPoint2D>{default(ContactPoint2D)};
        Points(k+"/list",pointsList(pl),pl);ProbeLog.Value(k+"/list-size",pl.Count);
        if(collidersList!=null){var cl=new List<Collider2D>{self};Colliders(k+"/collider-list",collidersList(cl),cl);ProbeLog.Value(k+"/collider-list-size",cl.Count);}
    }
    static void Snapshot(string phase)
    {
        foreach(string mode in new[]{"all","layer","depth","normal","no-triggers"})
        {
            var f=new ContactFilter2D().NoFilter();
            if(mode=="layer")f.SetLayerMask(1<<1);
            if(mode=="depth")f.SetDepth(-.5f,.5f);
            if(mode=="normal")f.SetNormalAngle(45,135);
            if(mode=="no-triggers")f.useTriggers=false;
            string k=phase+"/"+mode;
            View(k+"/body",a=>body.GetContacts(f,a),l=>body.GetContacts(f,l),a=>body.GetContacts(f,a),l=>body.GetContacts(f,l));
            View(k+"/static-body",a=>Physics2D.GetContacts(body,f,a),l=>Physics2D.GetContacts(body,f,l),a=>Physics2D.GetContacts(body,f,a),l=>Physics2D.GetContacts(body,f,l));
            foreach(var c in child ? new[]{self,child,Others[0]}:new[]{self,Others[0]})
            {
                string q=k+"/"+c.name;
                View(q,a=>c.GetContacts(f,a),l=>c.GetContacts(f,l),a=>c.GetContacts(f,a),l=>c.GetContacts(f,l));
            }
            View(k+"/pair",a=>Physics2D.GetContacts(self,Others[0],f,a),l=>Physics2D.GetContacts(self,Others[0],f,l),null,null);
        }
    }
    static void Step(string phase){Physics2D.SyncTransforms();Physics2D.Simulate(.02f);Snapshot(phase);}
    static void Case(string history)
    {
        ProbeLog.Begin(history); self=Box("seed",new Vector3(100,0,0),true);body=self.GetComponent<Rigidbody2D>();child=null;
        if(history.StartsWith("compound"))
        {
            var o=new GameObject("child");Objects.Add(o);o.transform.parent=self.transform;o.transform.localPosition=new Vector3(3,0,0);child=o.AddComponent<BoxCollider2D>();
        }
        var directions=new[]{new Vector3(-.9f,0,0),new Vector3(.9f,0,2),new Vector3(0,.9f,0),new Vector3(0,-.9f,-2)};
        var names=new[]{"Z","A","Q","B"};
        for(int i=0;i<4;i++)
        {
            var direction=directions[i];
            if(history=="delayed-touch"&&i<2)direction.x=direction.x<0?-1.05f:1.05f;
            var c=Box(names[i],new Vector3(100,0,0)+direction);c.gameObject.layer=i%2+1;Others.Add(c);
            if(i==1&&child)c.transform.position+=new Vector3(3,0,0);
            Physics2D.SyncTransforms();Physics2D.Simulate(.02f);
        }
        var trigger=Box("trigger",new Vector3(100,0,0));trigger.isTrigger=true;trigger.gameObject.layer=1;
        if(history=="compound-overlap")
        {
            var wide=Box("wide",new Vector3(101.5f,0,0));wide.size=new Vector2(3.5f,1);wide.gameObject.layer=1;
        }
        if(history=="delayed-touch")
        {
            Others[1].GetComponent<Rigidbody2D>().position=new Vector2(100.9f,0);Step("first-touch");
            Others[0].GetComponent<Rigidbody2D>().position=new Vector2(99.1f,0);
        }
        if(history=="unrelated")
        {
            Box("peer",new Vector3(120,0,0),true);Box("peer-contact",new Vector3(120.9f,0,0));
        }
        Step("initial");
        int removed=history=="remove-middle"?1:history=="remove-last"?3:0;
        if(history=="recreate-first")
        {
            var old=Others[0];var pos=old.transform.position;UnityEngine.Object.DestroyImmediate(old.gameObject);
            Others[0]=Box("Z2",pos);Others[0].gameObject.layer=1;
        }
        else Physics2D.IgnoreCollision(self,Others[removed],true);
        Step("removed");
        if(history!="recreate-first")Physics2D.IgnoreCollision(self,Others[removed],false);
        Step("restored");
        foreach(var o in Objects)if(o)UnityEngine.Object.DestroyImmediate(o);Objects.Clear();Others.Clear();
    }
}
