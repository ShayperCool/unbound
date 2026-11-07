using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static partial class ProbeEntry
{
    static readonly List<string> Lines = new List<string>();
    static readonly Vector3 Half = Vector3.one * .3f;
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    static void Set<T>(NativeArray<T> array, int index, T value) where T : struct { array[index] = value; }
    static void Row(string key, string value) => Lines.Add(key + "\t" + value);
    static void Hit(string key, RaycastHit h) => Row(key, (h.collider == null ? "none" : h.collider.name) + "," + F(h.distance) + "," + V(h.point) + "," + V(h.normal) + "," + h.triangleIndex);
    static BoxCollider Box(string name, Vector3 at, bool trigger = false, int layer = 0)
    {
        var go = new GameObject(name); go.transform.position = at; go.layer = layer;
        var c = go.AddComponent<BoxCollider>(); c.isTrigger = trigger; return c;
    }
    struct Writer : IJob
    {
        public NativeArray<RaycastCommand> Commands;
        public void Execute() { Commands[0] = new RaycastCommand(Vector3.zero, Vector3.forward, QueryParameters.Default, 20); }
    }
    static void Attempt(string key, Action action)
    {
        try { action(); Row(key, "ok"); }
        catch (Exception e) { Row(key, e.GetType().Name); }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        try
        {
            var near = Box("near", new Vector3(0,0,4));
            Box("far", new Vector3(0,0,8), false, 8);
            Box("trigger", new Vector3(0,0,6), true);
            Box("overlapA", new Vector3(20,0,0));
            Box("overlapB", new Vector3(20.4f,0,0), false, 8);
            Box("overlapTrigger", new Vector3(20,0,.3f), true);
            var meshGo = new GameObject("faces"); meshGo.transform.position = new Vector3(40,0,0);
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-2,-2,3), new Vector3(0,2,3), new Vector3(2,-2,3), new Vector3(-2,-2,6), new Vector3(0,2,6), new Vector3(2,-2,6) };
            mesh.triangles = new[] {0,1,2,3,4,5}; meshGo.AddComponent<MeshCollider>().sharedMesh = mesh;
            Physics.SyncTransforms();
            foreach (bool global in new[] {false,true})
            {
                Physics.queriesHitTriggers = global; Physics.queriesHitBackfaces = global;
                foreach (QueryTriggerInteraction triggers in new[] {QueryTriggerInteraction.Ignore,QueryTriggerInteraction.Collide,QueryTriggerInteraction.UseGlobal})
                foreach (int mask in new[] {-1, 1 << 8, 0})
                foreach (int max in new[] {1,4})
                {
                    var qp = new QueryParameters(mask, false, triggers, false);
                    string key = "g" + global + "/t" + (int)triggers + "/l" + mask + "/m" + max;
                    Casts(key, Vector3.zero, Vector3.forward, 20, qp, max);
                    Overlaps(key, new Vector3(20,0,0), qp, max);
                }
            }
            foreach (int max in new[] {1,4})
            foreach (float distance in new[] {-1f,0f,2f,20f})
            foreach (Vector3 origin in new[] {Vector3.zero,new Vector3(0,0,4),new Vector3(0,10,0)})
                Casts("edge/"+max+"/"+F(distance)+"/"+V(origin),origin,Vector3.forward,distance,new QueryParameters(-1),max);
            foreach (int max in new[] {1,4})
            foreach (bool back in new[] {false,true})
            foreach (bool multiple in new[] {false,true})
            foreach (bool reverse in new[] {false,true})
                Casts("mesh/"+max+"/"+back+"/"+multiple+"/"+reverse, new Vector3(40,0,reverse ? 10 : 0),reverse ? Vector3.back : Vector3.forward,20,new QueryParameters(-1,multiple,QueryTriggerInteraction.Ignore,back),max);
            foreach(bool global in new[] {false,true})
            foreach(bool back in new[] {false,true})
            {
                Physics.queriesHitBackfaces=global;
                Casts("globalBackfaces/"+global+"/"+back,new Vector3(40,0,10),Vector3.back,20,new QueryParameters(-1,false,QueryTriggerInteraction.Ignore,back),4);
            }
            Casts("zeroDirection", Vector3.zero,Vector3.zero,20,QueryParameters.Default,4);
            Overlaps("empty",new Vector3(20,20,0),QueryParameters.Default,4);
            var tr = near.transform;
            foreach (Vector3 scale in new[] {Vector3.one,Vector3.one*2,new Vector3(2,3,.5f),Vector3.zero,-Vector3.one})
            {
                using (var commands = new NativeArray<ClosestPointCommand>(3,Allocator.TempJob))
                using (var results = new NativeArray<Vector3>(4,Allocator.TempJob))
                {
                    for(int i=0;i<3;i++) Set(commands,i,new ClosestPointCommand(new Vector3(3+i,1,4),near,tr.position,Quaternion.identity,scale));
                    Attempt("closest/"+V(scale),()=> { ClosestPointCommand.ScheduleBatch(commands,results,1).Complete(); for(int i=0;i<4;i++)Row("closest/"+V(scale)+"/"+i,V(results[i])); });
                }
            }
            Row("closest/single",V(Physics.ClosestPoint(new Vector3(3,1,4),near,tr.position,tr.rotation)));
            using(var commands=new NativeArray<RaycastCommand>(1,Allocator.TempJob))
            using(var results=new NativeArray<RaycastHit>(2,Allocator.TempJob))
            {
                var writer=new Writer { Commands=commands }.Schedule();
                RaycastCommand.ScheduleBatch(commands,results,1,2,writer).Complete(); Hit("dependency",results[0]);
                foreach(int min in new[] {-1,0,1,64}) Attempt("min/"+min,()=>RaycastCommand.ScheduleBatch(commands,results,min,1).Complete());
                Attempt("invalid/max0",()=>RaycastCommand.ScheduleBatch(commands,results,1,0).Complete());
                Attempt("invalid/maxNegative",()=>RaycastCommand.ScheduleBatch(commands,results,1,-1).Complete());
                Attempt("invalid/short",()=>RaycastCommand.ScheduleBatch(commands,results,1,3).Complete());
                Attempt("invalid/default",()=>RaycastCommand.ScheduleBatch(commands,default(NativeArray<RaycastHit>),1,1).Complete());
            }
            Limits(near,meshGo.GetComponent<MeshCollider>());
            Stress();
            Followups(near);
            Row("complete","yes");
        }
        catch(Exception e) { Row("fatal",e.ToString().Replace('\n',' ').Replace('\r',' ')); }
        File.WriteAllLines(output,Lines);
        Debug.Log("physicsbatch complete");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Limits(Collider box,Collider mesh)
    {
        Action<string,ClosestPointCommand> point=(key,command)=>
        {
            using(var commands=new NativeArray<ClosestPointCommand>(1,Allocator.TempJob))
            using(var results=new NativeArray<Vector3>(1,Allocator.TempJob))
            {
                Set(commands,0,command);Set(results,0,new Vector3(99,99,99));
                Attempt("limits/"+key,()=>ClosestPointCommand.ScheduleBatch(commands,results,1).Complete());
                Row("limits/"+key+"/point",V(results[0]));
            }
        };
        point("posed",new ClosestPointCommand(new Vector3(5,6,7),box,new Vector3(1,2,3),Quaternion.Euler(0,0,40),Vector3.one));
        point("mesh",new ClosestPointCommand(new Vector3(41,2,3),mesh,mesh.transform.position,Quaternion.identity,Vector3.one));
        point("missing",new ClosestPointCommand(Vector3.one,0,Vector3.zero,Quaternion.identity,Vector3.one));
        point("unknown",new ClosestPointCommand(Vector3.one,int.MaxValue,Vector3.zero,Quaternion.identity,Vector3.one));
        box.enabled=false;
        point("disabled",new ClosestPointCommand(new Vector3(5,6,7),box,box.transform.position,box.transform.rotation,Vector3.one));
        box.enabled=true;
        Attempt("limits/null",()=>new ClosestPointCommand(Vector3.one,(Collider)null,Vector3.zero,Quaternion.identity,Vector3.one));
        using(var commands=new NativeArray<RaycastCommand>(1,Allocator.TempJob))
        using(var results=new NativeArray<RaycastHit>(1,Allocator.TempJob))
        {
            Attempt("limits/unassigned",()=>RaycastCommand.ScheduleBatch(default(NativeArray<RaycastCommand>),results,1,1).Complete());
            var disposed=new NativeArray<RaycastCommand>(1,Allocator.TempJob);disposed.Dispose();
            Attempt("limits/disposed",()=>RaycastCommand.ScheduleBatch(disposed,results,1,1).Complete());
            using(var temp=new NativeArray<RaycastCommand>(1,Allocator.Temp))
                Attempt("limits/temp",()=>RaycastCommand.ScheduleBatch(temp,results,1,1).Complete());
            using(var empty=new NativeArray<RaycastCommand>(0,Allocator.TempJob))
                Attempt("limits/empty",()=>RaycastCommand.ScheduleBatch(empty,results,1,1).Complete());
        }
    }
    static void Stress()
    {
        var go=new GameObject("manyFaces");go.transform.position=new Vector3(60,0,0);
        var vertices=new Vector3[80*3];var triangles=new int[vertices.Length];
        for(int i=0;i<80;i++)
        {
            float z=1+i*.1f;
            vertices[i*3]=new Vector3(-2,-2,z);vertices[i*3+1]=new Vector3(0,2,z);vertices[i*3+2]=new Vector3(2,-2,z);
            triangles[i*3]=i*3;triangles[i*3+1]=i*3+1;triangles[i*3+2]=i*3+2;
        }
        var mesh=new Mesh();mesh.vertices=vertices;mesh.triangles=triangles;go.AddComponent<MeshCollider>().sharedMesh=mesh;
        Physics.SyncTransforms();
        using(var commands=new NativeArray<RaycastCommand>(1,Allocator.TempJob))
        using(var results=new NativeArray<RaycastHit>(96,Allocator.TempJob))
        {
            Set(commands,0,new RaycastCommand(go.transform.position,Vector3.forward,new QueryParameters(-1,true,QueryTriggerInteraction.Ignore),20));
            RaycastCommand.ScheduleBatch(commands,results,1,96).Complete();
            int count=0;for(int i=0;i<results.Length;i++) {Hit("stress/faces/"+i,results[i]);if(results[i].collider!=null)count++;}
            Row("stress/faces/count",count.ToString());
            for(int i=0;i<63;i++)
            {
                var containing=new GameObject("containing"+i);containing.transform.position=go.transform.position;
                containing.AddComponent<SphereCollider>().radius=.5f;
            }
            Physics.SyncTransforms();
            RaycastCommand.ScheduleBatch(commands,results,1,96).Complete();
            count=0;for(int i=0;i<results.Length;i++) {Hit("stress/mixed/"+i,results[i]);if(results[i].collider!=null)count++;}
            Row("stress/mixed/count",count.ToString());
        }
    }
    static void Casts(string key,Vector3 origin,Vector3 direction,float distance,QueryParameters qp,int max)
    {
        bool originalBackfaces=Physics.queriesHitBackfaces;
        for(int kind=0;kind<4;kind++)
        {
            Physics.queriesHitBackfaces=originalBackfaces;
            string prefix=key+"/cast"+kind;
            using(var results=new NativeArray<RaycastHit>(3*max+1,Allocator.TempJob))
            {
                // Seed every slot, including space outside the command range, with a valid hit.
                Physics.Raycast(Vector3.zero,Vector3.forward,out var seed,20,-1,QueryTriggerInteraction.Ignore);
                for(int i=0;i<results.Length;i++)Set(results,i,seed);
                Attempt(prefix+"/schedule",()=>
                {
                    if(kind==0) { using(var commands=new NativeArray<RaycastCommand>(3,Allocator.TempJob)) { for(int i=0;i<3;i++)Set(commands,i,new RaycastCommand(origin+(i==1?Vector3.up*30:Vector3.zero),direction,qp,distance)); RaycastCommand.ScheduleBatch(commands,results,2,max).Complete(); } }
                    if(kind==1) { using(var commands=new NativeArray<SpherecastCommand>(3,Allocator.TempJob)) { for(int i=0;i<3;i++)Set(commands,i,new SpherecastCommand(origin+(i==1?Vector3.up*30:Vector3.zero),.3f,direction,qp,distance)); SpherecastCommand.ScheduleBatch(commands,results,2,max).Complete(); } }
                    if(kind==2) { using(var commands=new NativeArray<BoxcastCommand>(3,Allocator.TempJob)) { for(int i=0;i<3;i++)Set(commands,i,new BoxcastCommand(origin+(i==1?Vector3.up*30:Vector3.zero),Half,Quaternion.identity,direction,qp,distance)); BoxcastCommand.ScheduleBatch(commands,results,2,max).Complete(); } }
                    if(kind==3) { using(var commands=new NativeArray<CapsulecastCommand>(3,Allocator.TempJob)) { for(int i=0;i<3;i++) { var at=origin+(i==1?Vector3.up*30:Vector3.zero); Set(commands,i,new CapsulecastCommand(at-Vector3.up*.2f,at+Vector3.up*.2f,.3f,direction,qp,distance)); } CapsulecastCommand.ScheduleBatch(commands,results,2,max).Complete(); } }
                });
                for(int i=0;i<results.Length;i++)Hit(prefix+"/slot"+i,results[i]);
                Physics.queriesHitBackfaces=qp.hitBackfaces;
                var found=new RaycastHit[max]; int count=0;
                if(max==1)
                {
                    bool hit=false;
                    if(kind==0)hit=Physics.Raycast(origin,direction,out found[0],distance,qp.layerMask,qp.hitTriggers);
                    if(kind==1)hit=Physics.SphereCast(origin,.3f,direction,out found[0],distance,qp.layerMask,qp.hitTriggers);
                    if(kind==2)hit=Physics.BoxCast(origin,Half,direction,out found[0],Quaternion.identity,distance,qp.layerMask,qp.hitTriggers);
                    if(kind==3)hit=Physics.CapsuleCast(origin-Vector3.up*.2f,origin+Vector3.up*.2f,.3f,direction,out found[0],distance,qp.layerMask,qp.hitTriggers);
                    count=hit?1:0;
                }
                else
                {
                    if(kind==0)count=Physics.RaycastNonAlloc(origin,direction,found,distance,qp.layerMask,qp.hitTriggers);
                    if(kind==1)count=Physics.SphereCastNonAlloc(origin,.3f,direction,found,distance,qp.layerMask,qp.hitTriggers);
                    if(kind==2)count=Physics.BoxCastNonAlloc(origin,Half,direction,found,Quaternion.identity,distance,qp.layerMask,qp.hitTriggers);
                    if(kind==3)count=Physics.CapsuleCastNonAlloc(origin-Vector3.up*.2f,origin+Vector3.up*.2f,.3f,direction,found,distance,qp.layerMask,qp.hitTriggers);
                }
                Row(prefix+"/singleCount",count.ToString()); for(int i=0;i<count;i++)Hit(prefix+"/single"+i,found[i]);
            }
        }
    }
    static void Overlaps(string key,Vector3 at,QueryParameters qp,int max)
    {
        for(int kind=0;kind<3;kind++)
        using(var results=new NativeArray<ColliderHit>(3*max+1,Allocator.TempJob))
        {
            string prefix=key+"/overlap"+kind;
            // Reuse valid results to observe exactly which trailing slots are overwritten.
            using(var seed=new NativeArray<OverlapSphereCommand>(results.Length,Allocator.TempJob))
            { for(int i=0;i<seed.Length;i++)Set(seed,i,new OverlapSphereCommand(new Vector3(20,0,0),1,new QueryParameters(-1))); OverlapSphereCommand.ScheduleBatch(seed,results,1,1).Complete(); }
            Attempt(prefix+"/schedule",()=>
            {
                if(kind==0) { using(var c=new NativeArray<OverlapSphereCommand>(3,Allocator.TempJob)) { for(int i=0;i<3;i++)Set(c,i,new OverlapSphereCommand(at+(i==1?Vector3.up*30:Vector3.zero),1,qp)); OverlapSphereCommand.ScheduleBatch(c,results,2,max).Complete(); } }
                if(kind==1) { using(var c=new NativeArray<OverlapBoxCommand>(3,Allocator.TempJob)) { for(int i=0;i<3;i++)Set(c,i,new OverlapBoxCommand(at+(i==1?Vector3.up*30:Vector3.zero),Vector3.one,Quaternion.identity,qp)); OverlapBoxCommand.ScheduleBatch(c,results,2,max).Complete(); } }
                if(kind==2) { using(var c=new NativeArray<OverlapCapsuleCommand>(3,Allocator.TempJob)) { for(int i=0;i<3;i++) {var p=at+(i==1?Vector3.up*30:Vector3.zero);Set(c,i,new OverlapCapsuleCommand(p-Vector3.up*.5f,p+Vector3.up*.5f,1,qp));} OverlapCapsuleCommand.ScheduleBatch(c,results,2,max).Complete(); } }
            });
            for(int i=0;i<results.Length;i++)Row(prefix+"/slot"+i,results[i].instanceID==0?"none":results[i].collider.name);
            var found=new Collider[max]; int count=0;
            if(kind==0)count=Physics.OverlapSphereNonAlloc(at,1,found,qp.layerMask,qp.hitTriggers);
            if(kind==1)count=Physics.OverlapBoxNonAlloc(at,Vector3.one,found,Quaternion.identity,qp.layerMask,qp.hitTriggers);
            if(kind==2)count=Physics.OverlapCapsuleNonAlloc(at-Vector3.up*.5f,at+Vector3.up*.5f,1,found,qp.layerMask,qp.hitTriggers);
            Row(prefix+"/singleCount",count.ToString());for(int i=0;i<count;i++)Row(prefix+"/single"+i,found[i].name);
        }
    }
}
