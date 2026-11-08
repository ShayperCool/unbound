using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Collections;

// The extended capture has its own output switch so the original Q06 oracle stays intact.
public class GeometryEntry : MonoBehaviour
{
    const float Dt = .02f;
    static readonly List<string> Lines = new List<string>();
    static readonly List<UnityEngine.Object> Made = new List<UnityEngine.Object>();
    static readonly CollisionDetectionMode[] Modes = {
        CollisionDetectionMode.Discrete, CollisionDetectionMode.Continuous,
        CollisionDetectionMode.ContinuousDynamic, CollisionDetectionMode.ContinuousSpeculative };
    static string Scenario;
    static int Step;
    static PhysicsScene Space;
    static Mesh Hull;
    static string Output;
    static readonly Dictionary<int,string> ContactNames = new Dictionary<int,string>();
    static int NativeEvents;
    string Role;
    int Events;
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x)+","+F(v.y)+","+F(v.z);
    static string Q(Quaternion q) => F(q.x)+","+F(q.y)+","+F(q.z)+","+F(q.w);
    static void Row(string key, string value) => Lines.Add(key+"\t"+value);
    static string Key => Scenario+"/"+Step;
    static GameObject Make(string name, Vector3 at)
    {
        var go = new GameObject(name); go.transform.position = at; Made.Add(go); return go;
    }
    static Mesh BoxMesh(Vector3 half)
    {
        var m = new Mesh(); Made.Add(m);
        m.vertices = new[] {
            new Vector3(-half.x,-half.y,-half.z), new Vector3(half.x,-half.y,-half.z),
            new Vector3(half.x,half.y,-half.z), new Vector3(-half.x,half.y,-half.z),
            new Vector3(-half.x,-half.y,half.z), new Vector3(half.x,-half.y,half.z),
            new Vector3(half.x,half.y,half.z), new Vector3(-half.x,half.y,half.z) };
        m.triangles = new[] { 0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,
            3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5 };
        return m;
    }
    // sphere, hull, two spheres, two hulls, hull with a trailing sphere.
    static void Shape(GameObject go, int kind)
    {
        if(kind==0) go.AddComponent<SphereCollider>().radius=.1f;
        else if(kind==1) { var c=go.AddComponent<MeshCollider>(); c.sharedMesh=Hull; c.convex=true; }
        else for(int i=0;i<2;i++)
        {
            var child=Make(i==0?"front":"rear",go.transform.position);
            child.transform.SetParent(go.transform,false);
            child.transform.localPosition=kind==4 ? new Vector3(i==0?.15f:-.15f,0,0) : new Vector3(0,i==0?.25f:-.25f,0);
            Shape(child,kind==2 || kind==4 && i==1 ? 0:1);
        }
    }
    static Rigidbody Body(string role, Vector3 at, int kind, CollisionDetectionMode mode, bool kinematic=false)
    {
        var go=Make(role,at); Shape(go,kind);
        var b=go.AddComponent<Rigidbody>(); b.useGravity=false; b.linearDamping=0; b.angularDamping=0;
        b.sleepThreshold=0; b.constraints=RigidbodyConstraints.FreezeRotation;
        b.isKinematic=kinematic; b.collisionDetectionMode=mode;
        var receiver=go.AddComponent<GeometryEntry>(); receiver.Role=role;
        return b;
    }
    static void Begin(string name)
    {
        Scenario=name; Step=-1;
        Hull=BoxMesh(new Vector3(.1f,.15f,.2f));
    }
    static void Clear(int idleSteps=1)
    {
        // Children are tracked too; destroying the root first makes their entries null.
        foreach(var o in Made) if(o is GameObject && o!=null) DestroyImmediate(o);
        foreach(var o in Made) if(!(o is GameObject) && o!=null) DestroyImmediate(o);
        Made.Clear(); Physics.SyncTransforms();
        for(int idle=0;idle<idleSteps;idle++)Space.Simulate(Dt);
    }
    static void ResetEvents(Rigidbody b) => b.GetComponent<GeometryEntry>().Events=0;
    static void State(Rigidbody b)
    {
        Row(Key+"/"+b.name+"/body",V(b.position)+","+Q(b.rotation)+","+V(b.linearVelocity)+","+V(b.angularVelocity));
        Row(Key+"/"+b.name+"/events",b.GetComponent<GeometryEntry>().Events.ToString());
    }
    static void Tick(Rigidbody a, Rigidbody b=null)
    {
        ResetEvents(a); if(b!=null)ResetEvents(b);
        Space.Simulate(Dt); State(a); if(b!=null)State(b);
    }
    void Collision(string phase, Collision c)
    {
        string key=Key+"/"+Role+"/event/"+(Events++);
        Row(key,phase+","+c.collider.name+","+c.contactCount+","+V(c.impulse)+","+V(c.relativeVelocity));
        for(int i=0;i<c.contactCount;i++)
        {
            var p=c.GetContact(i);
            Row(key+"/"+i,V(p.point)+","+V(p.normal)+","+F(p.separation)+","+p.thisCollider.name+","+p.otherCollider.name);
        }
    }
    void OnCollisionEnter(Collision c) => Collision("enter",c);
    void OnCollisionStay(Collision c) => Collision("stay",c);
    void OnCollisionExit(Collision c) => Collision("exit",c);
    // Static box, kinematic box, static triangle mesh, terrain, kinematic triangle mesh.
    static Collider Obstacle(int kind)
    {
        var go=Make("wall",Vector3.zero);
        Collider c;
        if(kind==3)
        {
            go.transform.position=new Vector3(-2,0,-2);
            var data=new TerrainData(); Made.Add(data); data.heightmapResolution=33;
            data.size=new Vector3(4,1,4); data.SetHeights(0,0,new float[33,33]);
            var terrain=go.AddComponent<TerrainCollider>(); terrain.terrainData=data; c=terrain;
        }
        else if(kind==2 || kind==4)
        {
            var mesh=go.AddComponent<MeshCollider>(); mesh.sharedMesh=BoxMesh(new Vector3(.025f,2,2)); c=mesh;
        }
        else { var box=go.AddComponent<BoxCollider>(); box.size=new Vector3(.05f,4,4); c=box; }
        if(kind==1 || kind==4)go.AddComponent<Rigidbody>().isKinematic=true;
        return c;
    }
    static void StaticMatrix()
    {
        for(int shape=0;shape<5;shape++)
        for(int target=0;target<5;target++)
        foreach(var mode in Modes)
        {
            Begin("geometry/static/"+shape+"/"+target+"/"+(int)mode);
            Obstacle(target);
            var direction=target==3?Vector3.down:Vector3.right;
            var b=Body("bullet",-direction,shape,mode);
            Physics.SyncTransforms(); Space.Simulate(Dt);
            b.linearVelocity=direction*100;
            for(Step=0;Step<5;Step++)Tick(b);
            Clear();
        }
    }
    static void DynamicMatrix()
    {
        int[] left={0,1,0,2,4}, right={0,0,1,1,2};
        for(int pair=0;pair<left.Length;pair++)
        foreach(var first in Modes)
        foreach(var second in Modes)
        {
            Begin("geometry/dynamic/"+pair+"/"+(int)first+"/"+(int)second);
            var a=Body("a",Vector3.left,left[pair],first);
            var b=Body("b",Vector3.right,right[pair],second);
            Physics.SyncTransforms(); Space.Simulate(Dt);
            a.linearVelocity=Vector3.right*100; b.linearVelocity=Vector3.left*100;
            for(Step=0;Step<5;Step++)Tick(a,b);
            Clear();
        }
    }
    static void Tuning()
    {
        foreach(int shape in new[]{0,1,2,4})
        foreach(var mode in Modes)
        foreach(float offset in new[]{.001f,.05f})
        foreach(float maximum in new[]{.5f,10f})
        foreach(bool overlap in new[]{false,true})
        {
            Begin("geometry/tuning/"+shape+"/"+(int)mode+"/"+F(offset)+"/"+F(maximum)+"/"+overlap);
            Physics.defaultMaxDepenetrationVelocity=maximum;
            var wall=Obstacle(0); wall.contactOffset=offset;
            var b=Body("bullet",new Vector3(overlap?-.06f:-1,0,0),shape,mode);
            foreach(var c in b.GetComponentsInChildren<Collider>())c.contactOffset=offset;
            Row(Scenario+"/maximum",F(b.maxDepenetrationVelocity));
            if(!overlap) { Physics.SyncTransforms(); Space.Simulate(Dt); b.linearVelocity=Vector3.right*100; }
            for(Step=0;Step<5;Step++)Tick(b);
            Clear();
        }
        Begin("geometry/tuning/live-default");
        Physics.defaultMaxDepenetrationVelocity=.5f;
        var existing=Body("old",Vector3.zero,0,CollisionDetectionMode.ContinuousSpeculative);
        Physics.defaultMaxDepenetrationVelocity=10;
        var fresh=Body("new",Vector3.up,1,CollisionDetectionMode.ContinuousSpeculative);
        Row(Scenario+"/values",F(existing.maxDepenetrationVelocity)+","+F(fresh.maxDepenetrationVelocity));
        existing.maxDepenetrationVelocity=.25f; Row(Scenario+"/override",F(existing.maxDepenetrationVelocity));
        Clear();
    }
    static void Kinematics()
    {
        foreach(int shape in new[]{1,2,3,4})
        foreach(int target in new[]{0,1})
        foreach(var mode in new[]{CollisionDetectionMode.Discrete,CollisionDetectionMode.ContinuousSpeculative})
        {
            Begin("geometry/kinematic/"+shape+"/"+target+"/"+(int)mode);
            var a=Body("mover",Vector3.left,shape,CollisionDetectionMode.Discrete,true);
            var b=Body("target",Vector3.zero,target,CollisionDetectionMode.Discrete);
            Physics.SyncTransforms(); Space.Simulate(Dt);
            a.collisionDetectionMode=mode; Space.Simulate(Dt);
            for(Step=0;Step<4;Step++) { a.MovePosition(Vector3.right*(1+2*Step)); Tick(a,b); }
            Clear();
        }
    }
    static void KinematicTargets()
    {
        foreach(int target in new[]{1,2,3,4})
        foreach(int shape in new[]{0,1,2,4})
        foreach(var mode in Modes)
        {
            Begin("geometry/kinematic-target/"+shape+"/"+target+"/"+(int)mode);
            var wall=Body("wall",Vector3.zero,target,CollisionDetectionMode.Discrete,true);
            var b=Body("bullet",Vector3.left,shape,mode);
            Physics.SyncTransforms(); Space.Simulate(Dt); b.linearVelocity=Vector3.right*100;
            for(Step=0;Step<5;Step++)Tick(b,wall);
            Clear();
        }
    }
    static void Controls()
    {
        foreach(int target in new[]{0,1})
        foreach(float mass in new[]{1f,1000f})
        foreach(float velocity in new[]{0f,-1f,-100f})
        foreach(var mode in Modes)
        {
            Begin("control/dynamic/"+target+"/"+F(mass)+"/"+F(velocity)+"/"+(int)mode);
            var a=Body("bullet",Vector3.left,0,mode);
            var b=Body("target",Vector3.zero,target,CollisionDetectionMode.Discrete); b.mass=mass;
            Physics.SyncTransforms(); Space.Simulate(Dt);
            a.linearVelocity=Vector3.right*100; b.linearVelocity=Vector3.right*velocity;
            for(Step=0;Step<4;Step++)Tick(a,b);
            Clear();
        }
        foreach(bool child in new[]{false,true})
        {
            Begin("control/mesh-transition/"+child);
            var root=Make("wall",Vector3.zero);
            var owner=child?Make("mesh-child",Vector3.zero):root;
            if(child)owner.transform.SetParent(root.transform,false);
            var c=owner.AddComponent<MeshCollider>(); c.sharedMesh=BoxMesh(new Vector3(.025f,2,2));
            Physics.SyncTransforms();
            var wall=root.AddComponent<Rigidbody>(); wall.useGravity=false;
            var receiver=root.AddComponent<GeometryEntry>(); receiver.Role="wall";
            for(int phase=0;phase<3;phase++)
            {
                wall.isKinematic=phase!=1;
                Physics.SyncTransforms();
                Row(Scenario+"/"+phase+"/ray",Physics.Raycast(Vector3.left,Vector3.right,out var hit,2)?hit.collider.name:"none");
            }
            var a=Body("bullet",Vector3.left,0,CollisionDetectionMode.ContinuousDynamic);
            Physics.SyncTransforms(); Space.Simulate(Dt); a.linearVelocity=Vector3.right*100;
            for(Step=0;Step<4;Step++)Tick(a,wall);
            Clear();
        }
        Begin("control/mesh-convex-transition");
        var go=Make("body",Vector3.zero);
        var collider=go.AddComponent<MeshCollider>(); collider.sharedMesh=BoxMesh(new Vector3(.1f,.2f,.3f));
        Physics.SyncTransforms();
        var moving=go.AddComponent<Rigidbody>(); moving.useGravity=false; moving.linearDamping=0;
        go.AddComponent<GeometryEntry>().Role="body";
        moving.isKinematic=true; Physics.SyncTransforms();
        collider.convex=true; moving.isKinematic=false;
        moving.linearVelocity=Vector3.right;
        for(Step=0;Step<3;Step++)Tick(moving);
        collider.convex=false; moving.isKinematic=true;
        moving.MovePosition(Vector3.right); Step=3; Tick(moving);
        Clear();
    }
    static void NativeContacts(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
    {
        for(int h=0;h<headers.Length;h++)
        for(int i=0;i<headers[h].pairCount;i++)
        {
            ref readonly var pair=ref headers[h].GetContactPair(i);
            string first,second;
            if(!ContactNames.TryGetValue(pair.colliderInstanceID,out first) ||
               !ContactNames.TryGetValue(pair.otherColliderInstanceID,out second))continue;
            string key=Key+"/native/"+(NativeEvents++);
            Row(key,first+","+second+","+pair.isCollisionEnter+","+pair.isCollisionStay+","+pair.isCollisionExit+","+pair.contactCount+","+V(pair.impulseSum));
            for(int j=0;j<pair.contactCount;j++)
            {
                ref readonly var point=ref pair.GetContactPoint(j);
                Row(key+"/"+j,V(point.position)+","+V(point.normal)+","+F(point.separation)+","+V(point.impulse));
            }
        }
    }
    static Rigidbody OrderedBody(string role, Vector3 at, int kind, bool earlyBody, bool reverseChildren, bool kinematic)
    {
        var go=Make(role,at);
        var b=earlyBody?go.AddComponent<Rigidbody>():null;
        if(kind<2)Shape(go,kind);
        else for(int index=0;index<2;index++)
        {
            int i=reverseChildren?1-index:index;
            var child=Make(i==0?"front":"rear",at);
            child.transform.SetParent(go.transform,false);
            child.transform.localPosition=new Vector3(0,i==0?.25f:-.25f,0);
            Shape(child,kind==2?0:1);
        }
        if(b==null)b=go.AddComponent<Rigidbody>();
        b.useGravity=false; b.linearDamping=0; b.angularDamping=0;
        b.sleepThreshold=0; b.constraints=RigidbodyConstraints.FreezeRotation;
        b.isKinematic=kinematic; b.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        go.AddComponent<GeometryEntry>().Role=role;
        foreach(var c in go.GetComponentsInChildren<Collider>())ContactNames[c.GetInstanceID()]=role+"/"+c.name;
        return b;
    }
    static void Ordering()
    {
        Physics.ContactEvent+=NativeContacts;
        try
        {
            foreach(int shape in new[]{0,1,2,3})
            foreach(bool early in new[]{false,true})
            foreach(bool reverseChildren in new[]{false,true})
            foreach(bool reverseActors in new[]{false,true})
            foreach(bool kinematic in new[]{false,true})
            {
                Begin("ordering/"+shape+"/"+early+"/"+reverseChildren+"/"+reverseActors+"/"+kinematic);
                Rigidbody a,b;
                if(reverseActors)
                {
                    b=OrderedBody("b",Vector3.zero,1,early,false,kinematic);
                    a=OrderedBody("a",Vector3.left,shape,early,reverseChildren,false);
                }
                else
                {
                    a=OrderedBody("a",Vector3.left,shape,early,reverseChildren,false);
                    b=OrderedBody("b",Vector3.zero,1,early,false,kinematic);
                }
                Physics.SyncTransforms(); Space.Simulate(Dt);
                a.linearVelocity=Vector3.right*100;
                for(Step=0;Step<4;Step++) { NativeEvents=0; Tick(a,b); }
                Clear(); ContactNames.Clear();
            }
        }
        finally { Physics.ContactEvent-=NativeContacts; }
    }
    static void FrameControls()
    {
        foreach(int frame in new[]{0,1,2,3,4,5})
        {
            Begin("frames/"+frame);
            Obstacle(0);
            var root=Make("bullet",Vector3.left);
            var b=root.AddComponent<Rigidbody>();
            b.useGravity=false; b.linearDamping=0; b.angularDamping=0; b.sleepThreshold=0;
            b.constraints=RigidbodyConstraints.FreezeRotation;
            b.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            root.AddComponent<GeometryEntry>().Role="bullet";
            var child=Make("hull",Vector3.zero); child.transform.SetParent(root.transform,false);
            child.transform.localPosition=new Vector3(.15f,.25f,0);
            if(frame==1)child.transform.localScale=new Vector3(2,.5f,1.5f);
            if(frame==2)child.transform.localRotation=Quaternion.Euler(0,0,20);
            if(frame==3)child.transform.localScale=new Vector3(-1,1,1);
            if(frame==4)
            {
                root.transform.localScale=new Vector3(2,1,.5f);
                child.transform.localRotation=Quaternion.Euler(0,0,20);
            }
            if(frame==5)
            {
                var vertices=Hull.vertices;
                for(int i=0;i<vertices.Length;i++)vertices[i]+=Vector3.right*10000;
                Hull.vertices=vertices;
                child.transform.localPosition=new Vector3(.15f-10000,.25f,0);
            }
            Shape(child,1);
            Physics.SyncTransforms(); Space.Simulate(Dt);
            Row(Scenario+"/bounds",V(child.GetComponent<Collider>().bounds.center)+","+V(child.GetComponent<Collider>().bounds.size));
            b.linearVelocity=Vector3.right*100;
            for(Step=0;Step<4;Step++)Tick(b);
            Clear();
        }
    }
    static void IdleControls()
    {
        Physics.ContactEvent+=NativeContacts;
        try
        {
            foreach(int idleSteps in new[]{0,1,3})
            {
                Begin("ordering/warm/"+idleSteps);
                Body("warm",Vector3.left*10,1,CollisionDetectionMode.Discrete);
                Physics.SyncTransforms(); Space.Simulate(Dt);
                Clear(idleSteps);
                ContactNames.Clear();
                Begin("ordering/idle/"+idleSteps);
                var a=OrderedBody("a",Vector3.left,2,false,false,false);
                var b=OrderedBody("b",Vector3.zero,1,false,false,true);
                Physics.SyncTransforms(); Space.Simulate(Dt);
                a.linearVelocity=Vector3.right*100;
                for(Step=0;Step<3;Step++) { NativeEvents=0; Tick(a,b); }
                Clear(); ContactNames.Clear();
            }
        }
        finally { Physics.ContactEvent-=NativeContacts; }
    }
    static void PendingFrames()
    {
        bool previous=Physics.autoSyncTransforms;
        Physics.autoSyncTransforms=false;
        try
        {
            foreach(bool kinematic in new[]{false,true})
            foreach(int property in new[]{0,1,2})
            {
                Begin("frames/pending/"+kinematic+"/"+property);
                var root=Make("body",Vector3.zero);
                var body=root.AddComponent<Rigidbody>(); body.useGravity=false; body.isKinematic=kinematic;
                var child=Make("hull",Vector3.zero); child.transform.SetParent(root.transform,false);
                Shape(child,1); var collider=child.GetComponent<MeshCollider>();
                Physics.SyncTransforms();
                RecordPending("before",collider);
                child.transform.localRotation=Quaternion.Euler(0,0,90);
                if(property==0)collider.isTrigger=true;
                else if(property==1)collider.contactOffset=.02f;
                else
                {
                    var material=new PhysicsMaterial(); Made.Add(material);
                    material.dynamicFriction=.7f; collider.sharedMaterial=material;
                }
                RecordPending("pending",collider);
                Physics.SyncTransforms();
                RecordPending("synced",collider);
                Clear();
            }
        }
        finally { Physics.autoSyncTransforms=previous; }
    }
    static void ConstraintControls()
    {
        var constraints=new[]{RigidbodyConstraints.None, RigidbodyConstraints.FreezePositionY,
            RigidbodyConstraints.FreezeRotationX, RigidbodyConstraints.FreezeRotationY,
            RigidbodyConstraints.FreezeRotationZ, RigidbodyConstraints.FreezeRotation, RigidbodyConstraints.FreezeAll};
        foreach(var constraint in constraints)
        foreach(bool speculative in new[]{false,true})
        {
            Begin("constraints/"+(int)constraint+"/"+speculative);
            Obstacle(0);
            var body=Body("bullet",Vector3.left*(speculative?1:.06f),1,
                speculative?CollisionDetectionMode.ContinuousSpeculative:CollisionDetectionMode.Discrete);
            body.constraints=constraint;
            Physics.SyncTransforms();
            if(speculative) { Space.Simulate(Dt); body.linearVelocity=Vector3.right*100; }
            Row(Scenario+"/assigned-velocity",V(body.linearVelocity));
            for(Step=0;Step<5;Step++)Tick(body);
            body.Sleep();
            body.PublishTransform();
            Row(Scenario+"/published-sleeping",body.IsSleeping().ToString());
            Clear();
        }
        foreach(var constraint in new[]{RigidbodyConstraints.None, RigidbodyConstraints.FreezePositionX,
            RigidbodyConstraints.FreezePositionY, RigidbodyConstraints.FreezePositionZ,
            RigidbodyConstraints.FreezePosition, RigidbodyConstraints.FreezeRotationX,
            RigidbodyConstraints.FreezeRotationY, RigidbodyConstraints.FreezeRotationZ, RigidbodyConstraints.FreezeRotation})
        foreach(bool rotated in new[]{false,true})
        {
            Begin("constraints/velocity/"+(int)constraint+"/"+rotated);
            var body=Body("bullet",Vector3.left*10,0,CollisionDetectionMode.Discrete);
            if(rotated)body.rotation=Quaternion.Euler(20,30,40);
            body.inertiaTensorRotation=rotated?Quaternion.Euler(15,25,35):Quaternion.identity;
            body.constraints=constraint;
            body.linearVelocity=new Vector3(1,2,3); body.angularVelocity=new Vector3(4,5,6);
            Row(Scenario+"/linear",V(body.linearVelocity)); Row(Scenario+"/angular",V(body.angularVelocity));
            body.constraints=RigidbodyConstraints.None;
            Row(Scenario+"/released-linear",V(body.linearVelocity)); Row(Scenario+"/released-angular",V(body.angularVelocity));
            Clear();
        }
    }
    static void RecordPending(string phase, Collider collider)
    {
        var bounds=collider.bounds;
        Row(Scenario+"/"+phase+"/bounds",V(bounds.center)+","+V(bounds.size));
        var hits=Physics.OverlapSphere(new Vector3(.08f,0,0),.005f,~0,QueryTriggerInteraction.Collide);
        Row(Scenario+"/"+phase+"/hit",(Array.IndexOf(hits,collider)>=0).ToString());
    }
    IEnumerator FixedSteps()
    {
        Space=Physics.defaultPhysicsScene;
        foreach(int shape in new[]{1,2,4})
        foreach(var mode in Modes)
        {
            Begin("geometry/fixed/"+shape+"/"+(int)mode); Obstacle(0);
            var b=Body("bullet",Vector3.left,shape,mode);
            Physics.SyncTransforms(); Space.Simulate(Dt); b.linearVelocity=Vector3.right*100;
            Physics.simulationMode=SimulationMode.FixedUpdate;
            for(Step=0;Step<5;Step++) { ResetEvents(b); yield return new WaitForFixedUpdate(); State(b); }
            Physics.simulationMode=SimulationMode.Script; Clear();
        }
        Row("complete","yes"); Save(0);
    }
    static void Save(int code)
    {
        File.WriteAllLines(Output,Lines);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        Output=Environment.GetEnvironmentVariable("PHYSICS_GEOMETRY_OUT");
        string controls=Environment.GetEnvironmentVariable("PHYSICS_GEOMETRY_CONTROL_OUT");
        string ordering=Environment.GetEnvironmentVariable("PHYSICS_GEOMETRY_ORDER_OUT");
        string constraints=Environment.GetEnvironmentVariable("PHYSICS_GEOMETRY_CONSTRAINT_OUT");
        if(!string.IsNullOrEmpty(controls))Output=controls;
        if(!string.IsNullOrEmpty(ordering))Output=ordering;
        if(!string.IsNullOrEmpty(constraints))Output=constraints;
        if(string.IsNullOrEmpty(Output))return;
        Time.fixedDeltaTime=Dt; Physics.simulationMode=SimulationMode.Script; Space=Physics.defaultPhysicsScene;
        try
        {
            if(!string.IsNullOrEmpty(constraints)) { ConstraintControls(); Row("complete","yes"); Save(0); return; }
            if(!string.IsNullOrEmpty(ordering)) { Ordering(); FrameControls(); PendingFrames(); IdleControls(); Row("complete","yes"); Save(0); return; }
            if(!string.IsNullOrEmpty(controls)) { Controls(); Row("complete","yes"); Save(0); return; }
            StaticMatrix(); DynamicMatrix(); Tuning();
            var previous=SceneManager.GetActiveScene();
            var scene=SceneManager.CreateScene("geometry-kinematics",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(scene); Space=scene.GetPhysicsScene(); Kinematics(); KinematicTargets();
            SceneManager.SetActiveScene(previous); SceneManager.UnloadSceneAsync(scene);
            var driver=new GameObject("geometry-driver").AddComponent<GeometryEntry>();
            driver.StartCoroutine(driver.FixedSteps());
        }
        catch(Exception e) { Row("fatal",e.ToString().Replace('\n',' ')); Save(1); }
    }
}
