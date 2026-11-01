using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output=Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT"); if(string.IsNullOrEmpty(output))return;
        Physics2D.simulationMode=SimulationMode2D.Script; Physics2D.gravity=Vector2.zero;
        Physics2D.queriesStartInColliders=true; Physics2D.defaultContactOffset=.01f;
        int index=0;
        foreach(bool horizontal in new[] {false,true})
        foreach(float angle in new[] {7.3f,10f,23f,30f})
        foreach(float delta in new[] {-.1f,0f,.1f})
        foreach(float difference in new[] {-.001f,0f,.001f})
        foreach(var center in new[] {Vector2.zero,new Vector2(.3f,-.2f),new Vector2(-.3f,.2f)})
        {
            ProbeLog.Begin("manifold/"+index++);
            ProbeLog.Value("horizontal",horizontal); ProbeLog.Value("angle",angle); ProbeLog.Value("delta",delta); ProbeLog.Value("difference",difference);
            var q=Quaternion.Euler(0,0,angle); Q("factory",q);
            var axis=q*(horizontal?Vector3.right:Vector3.up); var origin=center+delta*new Vector2(axis.x,axis.y);
            ProbeLog.Value("origin",origin); ProbeLog.Value("center",center);
            var go=new GameObject("target");go.layer=8;go.transform.position=new Vector3(center.x,center.y,2);go.transform.rotation=q;go.transform.localScale=new Vector3(1,.8f,1);
            var target=go.AddComponent<CapsuleCollider2D>();target.direction=horizontal?CapsuleDirection2D.Horizontal:CapsuleDirection2D.Vertical;target.size=horizontal?new Vector2(3,1):new Vector2(1,3);
            Physics2D.SyncTransforms(); Shape("target",target); Q("target.rotation",go.transform.rotation);
            var side=q*(horizontal?Vector3.up:Vector3.right);var outward=new Vector2(side.x,side.y);
            Hit("sideRay",Physics2D.Raycast(center+outward*3,-outward,10,1<<8));
            float queryAngle=angle+difference;
            Hit("box",Physics2D.BoxCast(origin,new Vector2(.5f,.8f),queryAngle,Vector2.right,1,1<<8));
            Hit("capsule",Physics2D.CapsuleCast(origin,new Vector2(1.6f,.6f),CapsuleDirection2D.Horizontal,queryAngle,Vector2.right,1,1<<8));
            foreach(bool capsule in new[] {false,true})
            {
                string key=capsule?"physicalCapsule":"physicalBox";
                var query=new GameObject("query");query.layer=9;query.transform.position=new Vector3(origin.x,origin.y,2);query.transform.rotation=Quaternion.Euler(0,0,queryAngle);
                var body=query.AddComponent<Rigidbody2D>();body.bodyType=RigidbodyType2D.Kinematic;body.useFullKinematicContacts=true;body.constraints=RigidbodyConstraints2D.FreezeAll;
                Collider2D collider;
                if(capsule){var c=query.AddComponent<CapsuleCollider2D>();c.direction=CapsuleDirection2D.Horizontal;c.size=new Vector2(1.6f,.6f);collider=c;}
                else {var c=query.AddComponent<BoxCollider2D>();c.size=new Vector2(.5f,.8f);collider=c;}
                Physics2D.SyncTransforms(); Shape(key,collider); Q(key+".rotation",query.transform.rotation); ProbeLog.Value(key+".bodyAngle",body.rotation);
                ProbeLog.Value(key+".relativeRight",body.GetRelativeVector(Vector2.right));
                ProbeLog.Value(key+".relativeUp",body.GetRelativeVector(Vector2.up));
                ProbeLog.Value(key+".pointRight",body.GetRelativePoint(Vector2.right));
                Physics2D.Simulate(.02f);
                var contacts=new ContactPoint2D[8];int count=body.GetContacts(contacts);ProbeLog.Value(key+".count",count);
                for(int i=0;i<count;i++)
                {
                    var c=contacts[i];string k=key+".contact/"+i;
                    ProbeLog.Value(k+".point",c.point);ProbeLog.Value(k+".normal",c.normal);ProbeLog.Value(k+".separation",c.separation);ProbeLog.Value(k+".enabled",c.enabled);
                }
                UnityEngine.Object.DestroyImmediate(query);
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
    static void Q(string key,Quaternion q)
    {
        ProbeLog.Value(key+".x",q.x);ProbeLog.Value(key+".y",q.y);ProbeLog.Value(key+".z",q.z);ProbeLog.Value(key+".w",q.w);
    }
    static void Shape(string key,Collider2D collider)
    {
        var group=new PhysicsShapeGroup2D();collider.GetShapes(group);var shapes=new List<PhysicsShape2D>();var vertices=new List<Vector2>();group.GetShapeData(shapes,vertices);
        ProbeLog.Value(key+".shapes",shapes.Count);ProbeLog.Value(key+".vertices",vertices.Count);
        for(int i=0;i<shapes.Count;i++){ProbeLog.Value(key+".shape/"+i+".radius",shapes[i].radius);ProbeLog.Value(key+".shape/"+i+".type",shapes[i].shapeType.ToString());}
        for(int i=0;i<vertices.Count;i++)
        {
            ProbeLog.Value(key+".local/"+i,vertices[i]);var world=group.localToWorldMatrix.MultiplyPoint3x4(vertices[i]);ProbeLog.Value(key+".world/"+i,new Vector2(world.x,world.y));
        }
    }
    static void Hit(string key,RaycastHit2D hit)
    {
        ProbeLog.Value(key+".collider",hit.collider?hit.collider.name:"none");ProbeLog.Value(key+".point",hit.point);ProbeLog.Value(key+".normal",hit.normal);ProbeLog.Value(key+".distance",hit.distance);ProbeLog.Value(key+".centroid",hit.centroid);
    }
}
