using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ProbeEntry : MonoBehaviour
{
    static readonly List<string> Lines = new List<string>();
    static readonly List<GameObject> Objects = new List<GameObject>();
    static string Scenario;
    static PhysicsScene Space;
    static void Tick() => Space.Simulate(Dt);
    static int Step, Event;
    const float Dt = .02f;
    static void Set<T>(NativeArray<T> a, int i, T v) where T:struct { a[i]=v; }
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x)+","+F(v.y)+","+F(v.z);
    static string Q(Quaternion q) => F(q.x)+","+F(q.y)+","+F(q.z)+","+F(q.w);
    static void Row(string key, string value) => Lines.Add(key+"\t"+value);
    static string Key => Scenario+"/"+Step;
    static GameObject Make(string name, Vector3 at)
    {
        var go = new GameObject(name); go.transform.position = at; Objects.Add(go); return go;
    }
    static Rigidbody Body(string name, Vector3 at, int shape = 0)
    {
        var go = Make(name,at);
        if(shape==0) go.AddComponent<SphereCollider>().radius=.1f;
        else if(shape==1) go.AddComponent<BoxCollider>().size=new Vector3(.2f,.3f,.4f);
        else { var c=go.AddComponent<CapsuleCollider>(); c.radius=.1f; c.height=.4f; }
        var rb=go.AddComponent<Rigidbody>(); rb.useGravity=false; rb.linearDamping=0; rb.angularDamping=0;
        rb.maxAngularVelocity=100; rb.sleepThreshold=0; return rb;
    }
    static void Clear()
    {
        foreach(var go in Objects) DestroyImmediate(go);
        Objects.Clear(); Physics.SyncTransforms(); Tick();
    }
    static void State(Rigidbody b, string suffix="body") => Row(Key+"/"+suffix,V(b.position)+","+Q(b.rotation)+","+V(b.linearVelocity)+","+V(b.angularVelocity));
    static void Simulate() { Event=0; Tick(); }
    void Collision(string phase, Collision c)
    {
        string key=Key+"/event/"+(Event++);
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
    static void Ccd()
    {
        foreach(int shape in new[]{0,1,2})
        foreach(bool kinematic in new[]{false,true})
        foreach(CollisionDetectionMode mode in new[]{CollisionDetectionMode.Discrete,CollisionDetectionMode.Continuous,CollisionDetectionMode.ContinuousDynamic,CollisionDetectionMode.ContinuousSpeculative})
        {
            Scenario="ccd/thin/"+shape+"/"+kinematic+"/"+(int)mode;
            var wall=Make("wall",Vector3.zero); wall.AddComponent<BoxCollider>().size=new Vector3(.05f,4,4);
            if(kinematic) wall.AddComponent<Rigidbody>().isKinematic=true;
            var b=Body("bullet",new Vector3(-1,0,0),shape); b.collisionDetectionMode=mode;
            Tick(); b.gameObject.AddComponent<ProbeEntry>(); b.linearVelocity=new Vector3(100,0,0);
            for(Step=0;Step<6;Step++) { Simulate(); State(b); }
            Clear();
        }
        foreach(CollisionDetectionMode mode in new[]{CollisionDetectionMode.Discrete,CollisionDetectionMode.Continuous,CollisionDetectionMode.ContinuousDynamic,CollisionDetectionMode.ContinuousSpeculative})
        {
            Scenario="ccd/rotating/"+(int)mode;
            var target=Make("target",new Vector3(1.4f,0,.8f)); target.AddComponent<SphereCollider>().radius=.15f;
            var b=Body("bar",Vector3.zero,1); b.GetComponent<BoxCollider>().size=new Vector3(4,.15f,.15f);
            b.collisionDetectionMode=mode; Tick(); b.gameObject.AddComponent<ProbeEntry>(); b.angularVelocity=new Vector3(0,-50,0);
            for(Step=0;Step<8;Step++) { Simulate(); State(b); }
            Clear();
            Scenario="ccd/ghost/"+(int)mode;
            var box=Make("edge",Vector3.zero); box.AddComponent<BoxCollider>().size=new Vector3(.3f,.3f,4);
            b=Body("grazing",new Vector3(-1,.26f,0)); b.collisionDetectionMode=mode;
            Tick(); b.gameObject.AddComponent<ProbeEntry>(); b.linearVelocity=new Vector3(50,0,0);
            for(Step=0;Step<6;Step++) { Simulate(); State(b); }
            Clear();
            // Unity supports only Discrete and ContinuousSpeculative on kinematic bodies.
            if(mode!=CollisionDetectionMode.Discrete && mode!=CollisionDetectionMode.ContinuousSpeculative) continue;
            Scenario="ccd/kinematic-moving/"+(int)mode;
            var previous=SceneManager.GetActiveScene();
            var scene=SceneManager.CreateScene("kinematic-"+(int)mode,new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(scene); Space=scene.GetPhysicsScene();
            b=Body("target",Vector3.zero); b.gameObject.AddComponent<ProbeEntry>();
            var plate=Body("plate",new Vector3(-1,0,0),1); plate.GetComponent<BoxCollider>().size=new Vector3(.05f,2,2);
            plate.isKinematic=true; Physics.SyncTransforms(); Tick();
            plate.collisionDetectionMode=mode; Tick();
            for(Step=0;Step<4;Step++) { plate.MovePosition(new Vector3(1+2*Step,0,0)); Simulate(); State(b); State(plate,"plate"); }
            Clear(); SceneManager.SetActiveScene(previous); Space=Physics.defaultPhysicsScene;
            SceneManager.UnloadSceneAsync(scene);
        }
    }
    static HingeJoint Hinge(Rigidbody b, bool connected=false)
    {
        var h=b.gameObject.AddComponent<HingeJoint>(); h.anchor=Vector3.zero; h.axis=Vector3.up;
        h.autoConfigureConnectedAnchor=false; h.connectedAnchor=b.position;
        if(connected) { var c=Body("connected",b.position); c.GetComponent<Collider>().enabled=false; h.connectedBody=c; h.connectedAnchor=Vector3.zero; }
        return h;
    }
    static void Reading(HingeJoint h) => Row(Key+"/hinge",F(h.angle)+","+F(h.velocity)+","+V(h.currentForce)+","+V(h.currentTorque));
    static void Hinges()
    {
        foreach(bool free in new[]{false,true})
        foreach(float target in new[]{-90f,0f,90f})
        foreach(float speed in new[]{-360f,0f,360f})
        {
            Scenario="hinge/motor/"+free+"/"+F(target)+"/"+F(speed);
            var b=Body("rotor",new Vector3(0,5,0),1); var h=Hinge(b);
            h.motor=new JointMotor{targetVelocity=target,force=.1f,freeSpin=free}; h.useMotor=true;
            b.angularVelocity=Vector3.up*(speed*Mathf.Deg2Rad);
            for(Step=0;Step<12;Step++) { Simulate(); State(b); Reading(h); }
            Clear();
        }
        foreach(bool connected in new[]{false,true})
        {
            Scenario="hinge/motor-toggle/"+connected;
            var b=Body("rotor",new Vector3(0,5,0),1); var h=Hinge(b,connected);
            h.motor=new JointMotor{targetVelocity=90,force=1,freeSpin=true}; h.useMotor=true;
            for(Step=0;Step<24;Step++)
            {
                if(Step==4)b.angularVelocity=Vector3.up*10;
                if(Step==8)h.motor=new JointMotor{targetVelocity=-90,force=1,freeSpin=true};
                if(Step==12)h.motor=new JointMotor{targetVelocity=0,force=1,freeSpin=true};
                if(Step==16)h.motor=new JointMotor{targetVelocity=90,force=1,freeSpin=false};
                if(Step==20)h.useMotor=false;
                Simulate(); State(b); Reading(h);
                if(connected)State(h.connectedBody,"connected");
            }
            Clear();
        }
        foreach(bool extended in new[]{false,true})
        foreach(float speed in new[]{-360f,360f})
        foreach(int kind in new[]{0,1,2,3})
        {
            Scenario="hinge/limits/"+extended+"/"+F(speed)+"/"+kind;
            var b=Body("rotor",new Vector3(0,5,0),1); var h=Hinge(b); h.extendedLimits=extended;
            if(kind!=0)
            {
                h.limits=new JointLimits{min=kind==1 ? -170:-270,max=kind==1 ? 170:270,bounciness=kind==3?.7f:0,bounceMinVelocity=.2f,contactDistance=kind==3?10:0};
                h.useLimits=true;
            }
            b.angularVelocity=Vector3.up*(speed*Mathf.Deg2Rad);
            for(Step=0;Step<110;Step++) { Simulate(); State(b); Reading(h); }
            Clear();
        }
        foreach(bool extended in new[]{false,true})
        for(int span=0;span<6;span++)
        {
            float[] minimum={170,-190,-180,0,-360,0}, maximum={190,-170,180,360,0,0};
            Scenario="hinge/wrap/"+extended+"/"+span;
            var b=Body("rotor",new Vector3(0,5,0),1); var h=Hinge(b); h.extendedLimits=extended;
            h.limits=new JointLimits{min=minimum[span],max=maximum[span],contactDistance=2}; h.useLimits=true;
            var l=h.limits; Row(Scenario+"/stored",F(l.min)+","+F(l.max)+","+F(l.contactDistance));
            b.angularVelocity=Vector3.up*(span==1||span==4?-6.2831855f:6.2831855f);
            for(Step=0;Step<60;Step++) { Simulate(); State(b); Reading(h); }
            Clear();
        }
        foreach(bool extended in new[]{false,true})
        foreach(float angle in new[]{-359f,-270f,-181f,-180f,-179f,179f,180f,181f,270f,359f})
        {
            Scenario="hinge/pose/"+extended+"/"+F(angle); Step=0;
            var b=Body("rotor",new Vector3(0,5,0),1); var h=Hinge(b); h.extendedLimits=extended;
            Physics.SyncTransforms(); b.rotation=Quaternion.AngleAxis(angle,Vector3.up); Simulate(); Reading(h); State(b); Clear();
        }
    }
    static void ContactMatrix()
    {
        foreach(float bounce in new[]{0f,.7f})
        foreach(float distance in new[]{0f,1f,10f,30f})
        {
            Scenario="hinge/contact/"+F(bounce)+"/"+F(distance);
            var b=Body("rotor",new Vector3(0,5,0),1); var h=Hinge(b);
            h.limits=new JointLimits{min=-90,max=90,bounciness=bounce,bounceMinVelocity=.2f,contactDistance=distance}; h.useLimits=true;
            b.angularVelocity=Vector3.up*6.2831855f;
            for(Step=0;Step<35;Step++) { Simulate(); State(b); Reading(h); }
            Clear();
        }
    }
    static void Hit(string key, RaycastHit h) => Row(key,h.collider==null?"none":h.collider.name+","+F(h.distance)+","+V(h.point)+","+V(h.normal)+","+h.triangleIndex);
    static void Queries()
    {
        foreach(bool convex in new[]{false,true})
        {
            var go=Make("mesh",Vector3.zero); var mesh=new Mesh();
            mesh.vertices=new[]{new Vector3(-2,-2,3),new Vector3(0,2,3),new Vector3(2,-2,3),new Vector3(0,0,4)};
            mesh.triangles=convex?new[]{0,1,2,0,3,1,1,3,2,2,3,0}:new[]{0,1,2};
            var c=go.AddComponent<MeshCollider>(); c.sharedMesh=mesh; c.convex=convex; Physics.SyncTransforms();
            foreach(bool global in new[]{false,true})
            foreach(bool reverse in new[]{false,true})
            {
                string key="query/"+convex+"/"+global+"/"+reverse;
                Physics.queriesHitBackfaces=global;
                var origin=new Vector3(.2f,-.1f,reverse?6:0); var direction=reverse?Vector3.back:Vector3.forward;
                Physics.Raycast(origin,direction,out var hit,10); Hit(key+"/ray",hit);
                var all=Physics.RaycastAll(origin,direction,10); Row(key+"/rayAll/count",all.Length.ToString());
                for(int i=0;i<all.Length;i++)Hit(key+"/rayAll/"+i,all[i]);
                var buffer=new RaycastHit[8]; int n=Physics.RaycastNonAlloc(origin,direction,buffer,10); Row(key+"/rayNonAlloc/count",n.ToString());
                for(int i=0;i<n;i++)Hit(key+"/rayNonAlloc/"+i,buffer[i]);
                Physics.SphereCast(origin,.2f,direction,out hit,10); Hit(key+"/sphere",hit);
                all=Physics.SphereCastAll(origin,.2f,direction,10); Row(key+"/sphereAll/count",all.Length.ToString());
                for(int i=0;i<all.Length;i++)Hit(key+"/sphereAll/"+i,all[i]);
                n=Physics.SphereCastNonAlloc(origin,.2f,direction,buffer,10); Row(key+"/sphereNonAlloc/count",n.ToString());
                for(int i=0;i<n;i++)Hit(key+"/sphereNonAlloc/"+i,buffer[i]);
                c.Raycast(new Ray(origin,direction),out hit,10); Hit(key+"/collider",hit);
                foreach(bool back in new[]{false,true})
                {
                    var qp=new QueryParameters(-1,false,QueryTriggerInteraction.Ignore,back);
                    using(var commands=new NativeArray<RaycastCommand>(1,Allocator.TempJob))
                    using(var hits=new NativeArray<RaycastHit>(1,Allocator.TempJob))
                    { Set(commands,0,new RaycastCommand(origin,direction,qp,10)); RaycastCommand.ScheduleBatch(commands,hits,1,1).Complete(); Hit(key+"/batchRay/"+back,hits[0]); }
                    using(var commands=new NativeArray<SpherecastCommand>(1,Allocator.TempJob))
                    using(var hits=new NativeArray<RaycastHit>(1,Allocator.TempJob))
                    { Set(commands,0,new SpherecastCommand(origin,.2f,direction,qp,10)); SpherecastCommand.ScheduleBatch(commands,hits,1,1).Complete(); Hit(key+"/batchSphere/"+back,hits[0]); }
                    Row(key+"/globalAfter/"+back,Physics.queriesHitBackfaces.ToString());
                }
            }
            Clear(); DestroyImmediate(mesh);
        }
    }
    IEnumerator FixedSteps()
    {
        Scenario="ccd/fixed"; var wall=Make("wall",Vector3.zero); wall.AddComponent<BoxCollider>().size=new Vector3(.05f,2,2);
        var b=Body("bullet",new Vector3(-1,0,0)); b.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        b.gameObject.AddComponent<ProbeEntry>(); b.linearVelocity=Vector3.right*100;
        Physics.simulationMode=SimulationMode.FixedUpdate;
        for(Step=0;Step<6;Step++) { Event=0; yield return new WaitForFixedUpdate(); State(b); }
        Physics.simulationMode=SimulationMode.Script; Clear(); Row("complete","yes"); File.WriteAllLines(Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT"),Lines);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT")))return;
        Time.fixedDeltaTime=Dt; Physics.simulationMode=SimulationMode.Script; Space=Physics.defaultPhysicsScene;
        try { Ccd(); Hinges(); ContactMatrix(); Queries(); var driver=new GameObject("driver").AddComponent<ProbeEntry>(); driver.StartCoroutine(driver.FixedSteps()); }
        catch(Exception e)
        {
            Row("fatal",e.ToString().Replace('\n',' ')); File.WriteAllLines(Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT"),Lines);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(1);
#else
            Application.Quit(1);
#endif
        }
    }
}
