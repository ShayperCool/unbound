using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        foreach (string kind in new[] { "capsuleV", "capsuleH", "circle", "box" })
        foreach (float angle in new[] { 0f, 23f, 90f })
        foreach (float scale in new[] { .5f, 1f, 2f })
        foreach (string frame in new[] { "static", "body", "child", "rotated-child" })
        foreach (var center in new[] { Vector2.zero, new Vector2(.3f, -.2f) })
        {
            var go = new GameObject("target"); GameObject parent = null;
            go.transform.position = new Vector3(center.x, center.y, 2);
            go.transform.rotation = Quaternion.Euler(0, 0, angle);
            go.transform.localScale = new Vector3(scale, .8f * scale, 1);
            if (frame == "body") go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            if (frame == "child" || frame == "rotated-child")
            {
                parent = new GameObject("body");
                parent.transform.position = new Vector3(center.x, center.y, 2);
                parent.transform.rotation = Quaternion.Euler(0, 0, frame == "child" ? 0 : 11);
                parent.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.transform.SetParent(parent.transform, true);
            }
            Collider2D c;
            if (kind == "circle")
            {
                var circle = go.AddComponent<CircleCollider2D>(); circle.radius = .75f; c = circle;
            }
            else if (kind == "box")
            {
                var box = go.AddComponent<BoxCollider2D>(); box.size = new Vector2(2, 1); c = box;
            }
            else
            {
                var capsule = go.AddComponent<CapsuleCollider2D>();
                capsule.direction = kind == "capsuleH" ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
                capsule.size = kind == "capsuleH" ? new Vector2(3, 1) : new Vector2(1, 3); c = capsule;
            }
            Physics2D.SyncTransforms();
            string key = kind + "/" + N(angle) + "/" + N(scale) + "/" + frame + "/" + N(center.x) + "/" + N(center.y) + "/0";
            ProbeLog.Begin("frame/" + key); Record(go.transform);
            var rb = c.attachedRigidbody;
            if (rb) { Q("bodyRotation",rb.transform.rotation); M("bodyMatrix",rb.localToWorldMatrix); ProbeLog.Value("bodyAngle",rb.rotation); }
            else Q("bodyRotation",Quaternion.identity);
            var group = new PhysicsShapeGroup2D(); c.GetShapes(group); M("groupMatrix",group.localToWorldMatrix);
            ProbeLog.Value("radius",group.GetShape(0).radius);
            var points = new List<Vector2>(); group.GetShapeVertices(0,points);
            for(int i=0;i<points.Count;i++) ProbeLog.Value("vertices/"+i,points[i]);
            if(parent) UnityEngine.Object.DestroyImmediate(parent); else UnityEngine.Object.DestroyImmediate(go);
        }
        var rotations = new[] { Vector3.zero,new Vector3(0,0,11),new Vector3(0,0,23),new Vector3(0,0,90),
            new Vector3(0,0,13.37f),new Vector3(0,0,-47.125f),new Vector3(0,0,137.6f),new Vector3(23,35,7) };
        var scales = new[] { Vector3.one,new Vector3(2,2,2),new Vector3(1.3f,.7f,1.1f),new Vector3(-1.3f,.7f,1.1f),new Vector3(-1.3f,-.7f,1.1f) };
        int index = 0;
        foreach(var rotation in rotations)
        foreach(var scale in scales)
        foreach(bool nested in new[] {false,true})
        foreach(bool stays in new[] {false,true})
        {
            string key="parent/"+index++;
            var grand = new GameObject("grand"); var parent = new GameObject("parent"); var child = new GameObject("child");
            grand.transform.position=new Vector3(2.3f,-1.7f,.9f);
            grand.transform.rotation=Quaternion.Euler(17,-29,13);
            grand.transform.localScale=new Vector3(-.9f,1.7f,1.1f);
            if(nested) parent.transform.SetParent(grand.transform,false);
            parent.transform.localPosition=new Vector3(.7f,-.4f,1.2f);
            parent.transform.localRotation=Quaternion.Euler(rotation);
            parent.transform.localScale=scale;
            child.transform.position=new Vector3(-.3f,1.7f,-2.1f);
            child.transform.rotation=Quaternion.Euler(rotation+new Vector3(0,0,12));
            child.transform.localScale=new Vector3(.5f,-.8f,1.3f);
            Snapshot(key+"/parent",parent.transform); Snapshot(key+"/before",child.transform);
            child.transform.SetParent(parent.transform,stays);
            Snapshot(key+"/attached",child.transform); Snapshot(key+"/cached",child.transform);
            parent.transform.localScale=Vector3.Scale(scale,new Vector3(1.17f,.73f,-.8f));
            Snapshot(key+"/edited",child.transform);
            child.transform.SetParent(null,true); Snapshot(key+"/detached",child.transform);
            UnityEngine.Object.DestroyImmediate(child); UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(grand);
        }
        var raw = new[] { new Quaternion(.2488692105f,.1409349144f,-.9571540356f,.0453629345f),
            Quaternion.identity, new Quaternion(0,0,.7071068f,.7071068f), new Quaternion(1,2,3,4),
            new Quaternion(1e-16f,-1e-16f,1e-16f,-1e-16f), new Quaternion(1e-14f,-2e-14f,3e-14f,-4e-14f),
            new Quaternion(.23f,-.17f,.31f,.9f), new Quaternion(-1.3f,.4f,.27f,-2.1f) };
        for(int i=0;i<raw.Length;i++)
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            parent.transform.rotation=Quaternion.Euler(17,-29,13); parent.transform.localScale=new Vector3(.9f,-1.2f,1.4f);
            child.transform.localRotation=raw[i]; Snapshot("raw/"+i+"/local-root",child.transform);
            child.transform.rotation=raw[i]; Snapshot("raw/"+i+"/world-root",child.transform);
            child.transform.SetParent(parent.transform,false); Snapshot("raw/"+i+"/attached",child.transform);
            child.transform.localRotation=raw[i]; Snapshot("raw/"+i+"/local-child",child.transform);
            child.transform.rotation=raw[i]; Snapshot("raw/"+i+"/world-child",child.transform);
            child.transform.SetParent(null,true); Snapshot("raw/"+i+"/detached",child.transform);
            UnityEngine.Object.DestroyImmediate(child); UnityEngine.Object.DestroyImmediate(parent);
        }
        var collapsed = new[] { Vector3.zero,new Vector3(0,1,1),new Vector3(1,0,1),new Vector3(1,1,0),new Vector3(1e-6f,1e-7f,1e-8f),new Vector3(-1,0,2),new Vector3(1e-10f,1e-9f,1.0001e-9f),new Vector3(9.999999e-10f,1e-9f,1.0000001e-9f) };
        for(int i=0;i<collapsed.Length;i++)
        {
            var parent = new GameObject("parent"); var child = new GameObject("child");
            parent.transform.position=new Vector3(.7f,-.4f,1.2f); parent.transform.rotation=Quaternion.Euler(23,35,7); parent.transform.localScale=collapsed[i];
            child.transform.position=new Vector3(-.3f,1.7f,-2.1f); child.transform.rotation=Quaternion.Euler(0,0,13.37f); child.transform.localScale=new Vector3(.5f,.8f,1.3f);
            Snapshot("collapsed/"+i+"/parent",parent.transform);
            child.transform.SetParent(parent.transform,false); Snapshot("collapsed/"+i+"/attached",child.transform);
            child.transform.SetParent(null,true); Snapshot("collapsed/"+i+"/detached",child.transform);
            UnityEngine.Object.DestroyImmediate(child); UnityEngine.Object.DestroyImmediate(parent);
        }
        var circleRotations = new[] { Vector3.zero,new Vector3(0,0,90),new Vector3(0,0,13.37f),
            new Vector3(23,35,7),new Vector3(17,-29,13),new Vector3(0,0,-90),new Vector3(720,-1080,1440),new Vector3(45,22,-17) };
        var circleScales = new[] { new Vector3(1,.8f,1),new Vector3(1.3f,-.7f,1.1f),new Vector3(-.9f,1.7f,-1.1f) };
        index=0;
        foreach(var rotation in circleRotations)
        foreach(var scale in circleScales)
        foreach(bool child in new[] {false,true})
        {
            string key="circle-scale/"+index++;
            var parent=new GameObject("parent"); var go=new GameObject("circle");
            parent.transform.position=new Vector3(.7f,-.4f,1.2f);
            parent.transform.localRotation=Quaternion.Euler(17,-29,13); parent.transform.localScale=new Vector3(-.9f,1.7f,1.1f);
            if(child) { parent.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic; go.transform.SetParent(parent.transform,false); }
            else go.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;
            go.transform.localPosition=new Vector3(.23f,-.17f,2); go.transform.localRotation=Quaternion.Euler(rotation); go.transform.localScale=scale;
            var circle=go.AddComponent<CircleCollider2D>(); circle.radius=.75f; circle.offset=new Vector2(.17f,-.23f);
            CircleSnapshot(key+"/initial",circle);
            go.transform.localRotation=Quaternion.Euler(rotation+new Vector3(7,-19,31)); CircleSnapshot(key+"/rotated",circle);
            go.transform.localScale=Vector3.Scale(scale,new Vector3(1.13f,.79f,-.91f)); CircleSnapshot(key+"/scaled",circle);
            go.transform.SetParent(null,true); CircleSnapshot(key+"/detached",circle);
            UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(parent);
        }
        index=0;
        foreach(var scale in circleScales)
        foreach(string frame in new[] { "static", "body", "child", "rotated-child" })
        {
            string key="circle-planar/"+index++;
            var parent=new GameObject("parent"); var go=new GameObject("circle");
            go.transform.position=new Vector3(.3f,-.2f,2); go.transform.localScale=scale;
            if(frame=="body") go.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;
            if(frame=="child" || frame=="rotated-child")
            {
                parent.transform.position=go.transform.position;
                parent.transform.rotation=Quaternion.Euler(0,0,frame=="child"?0:11);
                parent.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic; go.transform.SetParent(parent.transform,true);
            }
            var circle=go.AddComponent<CircleCollider2D>(); circle.radius=.75f; circle.offset=new Vector2(.17f,-.23f);
            CircleSnapshot(key+"/initial",circle);
            go.transform.rotation=Quaternion.Euler(0,0,90); CircleSnapshot(key+"/rotated",circle);
            go.transform.rotation=Quaternion.identity; CircleSnapshot(key+"/restored",circle);
            UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(parent);
        }
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void CircleSnapshot(string key,CircleCollider2D circle)
    {
        Physics2D.SyncTransforms(); ProbeLog.Begin(key); Record(circle.transform);
        var group=new PhysicsShapeGroup2D(); circle.GetShapes(group); M("groupMatrix",group.localToWorldMatrix);
        ProbeLog.Value("radius",group.GetShape(0).radius); var vertices=new List<Vector2>(); group.GetShapeVertices(0,vertices);
        for(int i=0;i<vertices.Count;i++) ProbeLog.Value("vertices/"+i,vertices[i]);
    }
    static string N(float x) => x.ToString("R",CultureInfo.InvariantCulture);
    static void Snapshot(string key,Transform t) { ProbeLog.Begin(key); Record(t); }
    static void Record(Transform t)
    {
        Q("rotation",t.rotation); Q("localRotation",t.localRotation);
        ProbeLog.Value("position",t.position); ProbeLog.Value("localPosition",t.localPosition);
        ProbeLog.Value("localScale",t.localScale); ProbeLog.Value("lossyScale",t.lossyScale);
        M("matrix",t.localToWorldMatrix); M("inverse",t.worldToLocalMatrix);
        ProbeLog.Value("point/zero",t.TransformPoint(Vector3.zero));
        ProbeLog.Value("point/mixed",t.TransformPoint(new Vector3(.23f,-1.7f,2.1f)));
        ProbeLog.Value("inversePoint/zero",t.InverseTransformPoint(Vector3.zero));
        ProbeLog.Value("inversePoint/mixed",t.InverseTransformPoint(new Vector3(.31f,2.7f,-.9f)));

        if(t.parent)
        {
            Q("parentRotation",t.parent.rotation); M("parentMatrix",t.parent.localToWorldMatrix);
            ProbeLog.Value("parentLocalScale",t.parent.localScale); ProbeLog.Value("parentLossyScale",t.parent.lossyScale);
        }
    }
    static void Q(string key,Quaternion q)
    {
        ProbeLog.Value(key+".x",q.x); ProbeLog.Value(key+".y",q.y); ProbeLog.Value(key+".z",q.z); ProbeLog.Value(key+".w",q.w);
    }
    static void M(string key,Matrix4x4 m) { for(int i=0;i<16;i++) ProbeLog.Value(key+"/"+i,m[i]); }
}
