using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using UnityEngine;

public static partial class ProbeEntry
{
    struct RayReader : IJob
    {
        [ReadOnly] public NativeArray<RaycastHit> Hits;
        public NativeArray<float> Distances;
        public void Execute() { Distances[0] = Hits[0].distance; }
    }
    static void Followups(Collider box)
    {
        Scheduling(box);
        TransformPreparation();
        ClosestMutation();
        TerrainQueriesAndMaterials();
        ControllerFilters();
    }
    static void Scheduling(Collider box)
    {
        using(var commands=new NativeArray<RaycastCommand>(17,Allocator.TempJob))
        using(var results=new NativeArray<RaycastHit>(17,Allocator.TempJob))
        using(var distances=new NativeArray<float>(1,Allocator.TempJob))
        {
            for(int i=0;i<commands.Length;i++)Set(commands,i,new RaycastCommand(Vector3.zero,Vector3.forward,QueryParameters.Default,20));
            foreach(int min in new[]{-1,0,1,3,64,int.MaxValue})
            {
                string key="jobs/"+min;
                var handle=RaycastCommand.ScheduleBatch(commands,results,min,1);
                Attempt(key+"/readResults",()=>{var value=results[0];});
                Attempt(key+"/readCommands",()=>{var value=commands[0];});
                Attempt(key+"/writeCommands",()=>Set(commands,0,default(RaycastCommand)));
                var alias=results;
                Attempt(key+"/disposeAlias",()=>alias.Dispose());
                Attempt(key+"/competing",()=>RaycastCommand.ScheduleBatch(commands,results,min,1).Complete());
                Physics.SyncTransforms();
                Attempt(key+"/readAfterSync",()=>{var value=results[0];});
                var reader=new RayReader{Hits=results,Distances=distances}.Schedule(handle);
                reader.Complete();
                Row(key+"/dependent",F(distances[0]));
                for(int i=0;i<results.Length;i++)Hit(key+"/result/"+i,results[i]);
            }
            var writer=new Writer{Commands=commands}.Schedule();
            var batch=RaycastCommand.ScheduleBatch(commands,results,3,1,writer);
            Attempt("jobs/writerStillOwned",()=>{var value=commands[0];});
            batch.Complete();Hit("jobs/writerCompleted",results[0]);
            using(var small=new NativeArray<RaycastHit>(1,Allocator.TempJob))
            {
                var pending=new Writer{Commands=commands}.Schedule();
                RaycastCommand.ScheduleBatch(commands,small,1,1,pending).Complete();
                Attempt("jobs/invalidDoesNotComplete",()=>{var value=commands[0];});
                pending.Complete();
            }
        }
        // Every command family must own its buffers until completion, including a dependent reader.
        using(var rays=new NativeArray<RaycastHit>(1,Allocator.TempJob))
        using(var overlaps=new NativeArray<ColliderHit>(1,Allocator.TempJob))
        using(var points=new NativeArray<Vector3>(1,Allocator.TempJob))
        {
            using(var c=new NativeArray<SpherecastCommand>(1,Allocator.TempJob))
            {Set(c,0,new SpherecastCommand(Vector3.zero,.2f,Vector3.forward,QueryParameters.Default,20));var h=SpherecastCommand.ScheduleBatch(c,rays,1);Attempt("jobs/sphere/read",()=>{var v=rays[0];});h.Complete();Hit("jobs/sphere/hit",rays[0]);}
            using(var c=new NativeArray<BoxcastCommand>(1,Allocator.TempJob))
            {Set(c,0,new BoxcastCommand(Vector3.zero,Half,Quaternion.identity,Vector3.forward,QueryParameters.Default,20));var h=BoxcastCommand.ScheduleBatch(c,rays,1);Attempt("jobs/box/read",()=>{var v=rays[0];});h.Complete();Hit("jobs/box/hit",rays[0]);}
            using(var c=new NativeArray<CapsulecastCommand>(1,Allocator.TempJob))
            {Set(c,0,new CapsulecastCommand(Vector3.down*.2f,Vector3.up*.2f,.2f,Vector3.forward,QueryParameters.Default,20));var h=CapsulecastCommand.ScheduleBatch(c,rays,1);Attempt("jobs/capsule/read",()=>{var v=rays[0];});h.Complete();Hit("jobs/capsule/hit",rays[0]);}
            using(var c=new NativeArray<OverlapSphereCommand>(1,Allocator.TempJob))
            {Set(c,0,new OverlapSphereCommand(box.transform.position,1,QueryParameters.Default));var h=OverlapSphereCommand.ScheduleBatch(c,overlaps,1,1);Attempt("jobs/overlapSphere/read",()=>{var v=overlaps[0];});h.Complete();Row("jobs/overlapSphere/hit",overlaps[0].collider.name);}
            using(var c=new NativeArray<OverlapBoxCommand>(1,Allocator.TempJob))
            {Set(c,0,new OverlapBoxCommand(box.transform.position,Vector3.one,Quaternion.identity,QueryParameters.Default));var h=OverlapBoxCommand.ScheduleBatch(c,overlaps,1,1);Attempt("jobs/overlapBox/read",()=>{var v=overlaps[0];});h.Complete();Row("jobs/overlapBox/hit",overlaps[0].collider.name);}
            using(var c=new NativeArray<OverlapCapsuleCommand>(1,Allocator.TempJob))
            {Set(c,0,new OverlapCapsuleCommand(box.transform.position-Vector3.up*.2f,box.transform.position+Vector3.up*.2f,1,QueryParameters.Default));var h=OverlapCapsuleCommand.ScheduleBatch(c,overlaps,1,1);Attempt("jobs/overlapCapsule/read",()=>{var v=overlaps[0];});h.Complete();Row("jobs/overlapCapsule/hit",overlaps[0].collider.name);}
            using(var c=new NativeArray<ClosestPointCommand>(1,Allocator.TempJob))
            {Set(c,0,new ClosestPointCommand(Vector3.zero,box,box.transform.position,box.transform.rotation,Vector3.one));var h=ClosestPointCommand.ScheduleBatch(c,points,1);Attempt("jobs/closest/read",()=>{var v=points[0];});h.Complete();Row("jobs/closest/point",V(points[0]));}
        }
    }
    struct TransformWriter : IJobParallelForTransform
    {
        public Vector3 Position;
        public void Execute(int index,TransformAccess transform) { transform.position=Position; }
    }
    static void TransformPreparation()
    {
        bool previous=Physics.autoSyncTransforms;Physics.autoSyncTransforms=true;
        var at=new Vector3(600,0,0);var target=Box("transformTarget",at+Vector3.forward*4);
        Physics.SyncTransforms();
        using(var transforms=new TransformAccessArray(new[]{target.transform}))
        using(var commands=new NativeArray<RaycastCommand>(1,Allocator.TempJob))
        using(var results=new NativeArray<RaycastHit>(1,Allocator.TempJob))
        {
            Set(commands,0,new RaycastCommand(at,Vector3.forward,QueryParameters.Default,20));
            target.transform.position=at+Vector3.forward*5;
            RaycastCommand.ScheduleBatch(commands,results,1).Complete();Hit("jobs/transform/manual",results[0]);
            using(var points=new NativeArray<Vector3>(1,Allocator.TempJob))
            using(var closest=new NativeArray<ClosestPointCommand>(1,Allocator.TempJob))
            {
                Set(closest,0,new ClosestPointCommand(at+Vector3.forward*9,target,at+Vector3.forward*5,Quaternion.identity,Vector3.one));
                ClosestPointCommand.ScheduleBatch(closest,points,1).Complete();Row("jobs/transform/closestMoved",V(points[0]));
            }
            target.transform.position=at+Vector3.forward*4;Physics.SyncTransforms();
            var writer=new TransformWriter{Position=at+Vector3.forward*6}.Schedule(transforms);
            RaycastCommand.ScheduleBatch(commands,results,1).Complete();Hit("jobs/transform/pending",results[0]);
            writer.Complete();
            Physics.Raycast(at,Vector3.forward,out var single,20);Hit("jobs/transform/single",single);
            Physics.SyncTransforms();RaycastCommand.ScheduleBatch(commands,results,1).Complete();Hit("jobs/transform/synced",results[0]);
        }
        Physics.autoSyncTransforms=previous;UnityEngine.Object.DestroyImmediate(target.gameObject);
    }
    static void ClosestMutation()
    {
        foreach(string mutation in new[]{"convex","disable","destroy"})
        {
            var go=new GameObject("closestMutation");var at=new Vector3(700,0,0);go.transform.position=at;
            var mesh=new Mesh();mesh.vertices=new[]{new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(0,0,1),new Vector3(-1,-1,-1)};
            mesh.triangles=new[]{0,2,1,0,1,3,1,2,3,2,0,3};
            var collider=go.AddComponent<MeshCollider>();collider.sharedMesh=mesh;collider.convex=true;
            Physics.SyncTransforms();
            using(var commands=new NativeArray<ClosestPointCommand>(1,Allocator.TempJob))
            using(var results=new NativeArray<Vector3>(1,Allocator.TempJob))
            {
                Set(commands,0,new ClosestPointCommand(at+Vector3.right*3,collider,at,Quaternion.identity,Vector3.one));
                var handle=ClosestPointCommand.ScheduleBatch(commands,results,1);
                if(mutation=="convex")collider.convex=false;
                if(mutation=="disable")collider.enabled=false;
                if(mutation=="destroy")UnityEngine.Object.DestroyImmediate(go);
                handle.Complete();Row("jobs/closestMutation/"+mutation,V(results[0]));
            }
            if(go!=null)UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
    static TerrainCollider NewTerrain(string name,Vector3 origin,PhysicsMaterial initial)
    {
        var data=new TerrainData{heightmapResolution=33,size=new Vector3(20,10,20)};
        data.SetHeights(0,0,new float[33,33]);
        data.treePrototypes=new[]{new TreePrototype{prefab=Resources.Load<GameObject>("Trees/Tree")}};
        data.SetTreeInstances(new[]{new TreeInstance{position=new Vector3(.5f,0,.5f),widthScale=1,heightScale=1,color=Color.white,lightmapColor=Color.white}},false);
        var go=new GameObject(name);go.SetActive(false);go.transform.position=origin;
        var c=go.AddComponent<TerrainCollider>();c.sharedMaterial=initial;c.terrainData=data;go.SetActive(true);
        return c;
    }
    static PhysicsMaterial BounceMaterial(float bounce)=>new PhysicsMaterial{bounciness=bounce,bounceCombine=PhysicsMaterialCombine.Maximum,dynamicFriction=0,staticFriction=0};
    static void TerrainQueriesAndMaterials()
    {
        var terrain=NewTerrain("batchTerrain",new Vector3(200,0,0),null);
        Physics.SyncTransforms();
        foreach(int max in new[]{1,4})
        {
            Casts("terrain/height/"+max,new Vector3(205,8,5),Vector3.down,20,new QueryParameters(-1),max);
            Casts("terrain/tree/"+max,new Vector3(207,1,10),Vector3.right,8,new QueryParameters(-1),max);
            Overlaps("terrain/height/"+max,new Vector3(205,.1f,5),new QueryParameters(-1),max);
            Overlaps("terrain/tree/"+max,new Vector3(210,1,10),new QueryParameters(-1),max);
        }
        Physics.simulationMode=SimulationMode.Script;
        foreach(string mode in new[]{"initialLow","initialHigh","sharedAssign","materialAssign","sharedMutate","materialMutate","highAssignLow","highMutateLow"})
        {
            var low=BounceMaterial(0);var high=BounceMaterial(1);
            var c=NewTerrain("materialTerrain",new Vector3(300,0,0),mode=="initialHigh"||mode=="highAssignLow"||mode=="highMutateLow"?high:low);
            Physics.SyncTransforms();
            if(mode=="highAssignLow")c.sharedMaterial=low;
            if(mode=="highMutateLow")high.bounciness=0;
            if(mode=="sharedAssign")c.sharedMaterial=high;
            if(mode=="materialAssign")c.material=high;
            if(mode=="sharedMutate")low.bounciness=1;
            if(mode=="materialMutate")c.material.bounciness=1;
            foreach(bool tree in new[]{false,true})
            {
                var ball=new GameObject("materialBall");ball.transform.position=tree?new Vector3(308.5f,1,10):new Vector3(305,1.5f,5);
                var sphere=ball.AddComponent<SphereCollider>();sphere.radius=.2f;sphere.sharedMaterial=BounceMaterial(0);
                var rb=ball.AddComponent<Rigidbody>();rb.useGravity=false;rb.linearDamping=0;rb.angularDamping=0;rb.linearVelocity=tree?Vector3.right*10:Vector3.down*10;
                Physics.SyncTransforms();for(int i=0;i<12;i++)Physics.Simulate(.02f);
                Row("terrain/material/"+mode+"/"+tree,V(rb.linearVelocity));
                UnityEngine.Object.DestroyImmediate(ball);
            }
            UnityEngine.Object.DestroyImmediate(c.gameObject);
        }
    }
    static void ControllerFilters()
    {
        foreach(string mode in new[]{"matrix","pair","wallLayer","controllerLayer","unrelated","noopMatrix","noopPair"})
        {
            var go=new GameObject("filterController");go.layer=9;go.transform.position=new Vector3(400,2,0);
            var c=go.AddComponent<CharacterController>();c.minMoveDistance=0;var hits=go.AddComponent<FilterHits>();
            var wall=Box("filterWall",new Vector3(402,2,0),false,10);wall.size=new Vector3(1,6,6);
            var second=new GameObject("secondController");second.layer=9;second.transform.position=new Vector3(400,2,10);var c2=second.AddComponent<CharacterController>();c2.minMoveDistance=0;var hits2=second.AddComponent<FilterHits>();
            var wall2=Box("secondWall",new Vector3(402,2,10),false,10);wall2.size=new Vector3(1,6,6);
            Physics.IgnoreLayerCollision(9,10,false);Physics.SyncTransforms();
            c.Move(Vector3.right*.1f);c2.Move(Vector3.right*.1f);
            for(int i=0;i<8;i++)
            {
                hits.Count=0;hits2.Count=0;
                bool ignore=(i%2)==0;
                if(mode=="noopMatrix")Physics.IgnoreLayerCollision(9,10,false);
                if(mode=="noopPair")Physics.IgnoreCollision(c,wall,false);
                if(mode=="matrix")Physics.IgnoreLayerCollision(9,10,ignore);
                if(mode=="pair")Physics.IgnoreCollision(c,wall,ignore);
                if(mode=="wallLayer")wall.gameObject.layer=ignore?11:10;
                if(mode=="controllerLayer")go.layer=ignore?11:9;
                if(mode=="unrelated")Physics.IgnoreLayerCollision(20,21,ignore);
                if(mode=="wallLayer"||mode=="controllerLayer")Physics.IgnoreLayerCollision(mode=="controllerLayer"?10:9,11,true);
                var f=c.Move(Vector3.right*(i<2?.1f:1));var f2=c2.Move(Vector3.right*(i<2?.1f:1));
                Row("controller/"+mode+"/hits/"+i,hits.Count+","+hits2.Count);
                Row("controller/"+mode+"/"+i,F(go.transform.position.x-400)+","+(int)f+","+F(second.transform.position.x-400)+","+(int)f2);
            }
            Physics.IgnoreLayerCollision(9,10,false);Physics.IgnoreLayerCollision(9,11,false);Physics.IgnoreLayerCollision(10,11,false);Physics.IgnoreLayerCollision(20,21,false);
            UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(second);UnityEngine.Object.DestroyImmediate(wall.gameObject);UnityEngine.Object.DestroyImmediate(wall2.gameObject);
        }
    }
}

public sealed class FilterHits : MonoBehaviour
{
    public int Count;
    void OnControllerColliderHit(ControllerColliderHit hit) { Count++; }
}
