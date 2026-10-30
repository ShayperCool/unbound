using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public partial class ForcesProbe : MonoBehaviour
{
    readonly StringBuilder output=new StringBuilder();
    static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
    static string V(Vector3 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
    static string V2(Vector2 v)=>"["+F(v.x)+","+F(v.y)+"]";
    static string Q(Quaternion v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+","+F(v.w)+"]";
    static string B(bool b)=>b?"true":"false";
    static string S(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    void Value(string k,string v)=>output.Append("{\"k\":").Append(S(k)).Append(",\"v\":").Append(v).Append("}\n");
    static string Properties(ConstantForce f)=>"{\"force\":"+V(f.force)+",\"relative\":"+V(f.relativeForce)+",\"torque\":"+V(f.torque)+",\"relative_torque\":"+V(f.relativeTorque)+",\"enabled\":"+B(f.enabled)+"}";
    static string Properties(ConstantForce2D f)=>"{\"force\":"+V2(f.force)+",\"relative\":"+V2(f.relativeForce)+",\"torque\":"+F(f.torque)+",\"enabled\":"+B(f.enabled)+"}";
    class Case
    {
        public string key;public GameObject root;public Rigidbody body;public Rigidbody2D body2;public ConstantForce force;public ConstantForce2D force2;public Vector3 origin;
        public string Snapshot()=>body!=null?"{\"p\":"+V(body.position-origin)+",\"r\":"+Q(body.rotation)+",\"v\":"+V(body.linearVelocity)+",\"av\":"+V(body.angularVelocity)+",\"sleep\":"+B(body.IsSleeping())+"}":"{\"p\":"+V2(body2.position-(Vector2)origin)+",\"r\":"+F(body2.rotation)+",\"v\":"+V2(body2.linearVelocity)+",\"av\":"+F(body2.angularVelocity)+",\"sleep\":"+B(body2.IsSleeping())+"}";
        public void Enable(bool enabled){if(force!=null)force.enabled=enabled;else force2.enabled=enabled;}
    }
    Case Make(int dimension,int parented,bool kinematic,int count,bool zero,float offset)
    {
        var root=new GameObject("Parent");root.transform.position=new Vector3(offset,0,0);
        if(parented>0){root.transform.rotation=dimension==3?Quaternion.Euler(20,30,10):Quaternion.Euler(0,0,30);root.transform.localScale=new Vector3(parented==2?-2:2,3,1.5f);}
        var go=new GameObject("Body");go.transform.SetParent(root.transform,false);go.transform.localRotation=dimension==3?Quaternion.Euler(10,40,20):Quaternion.Euler(0,0,40);
        var c=new Case{root=root,key=dimension+"."+parented+"."+B(kinematic)+"."+count+"."+B(zero)};
        for(int i=0;i<count;i++)
        {
            if(dimension==3)
            {
                var f=go.AddComponent<ConstantForce>();if(f==null)continue;
                if(!zero){f.force=new Vector3(4,2,-3);f.relativeForce=new Vector3(1,2,3);f.torque=new Vector3(.5f,1,.25f);f.relativeTorque=new Vector3(.3f,.2f,.1f);}
                if(i==0)c.force=f;
            }
            else
            {
                var f=go.AddComponent<ConstantForce2D>();if(f==null)continue;
                if(!zero){f.force=new Vector2(4,2);f.relativeForce=new Vector2(1,2);f.torque=.5f;}
                if(parented>0)f.torque=0;
                if(i==0)c.force2=f;
            }
        }
        if(dimension==3)
        {
            c.body=go.GetComponent<Rigidbody>();c.body.mass=2;c.body.useGravity=false;c.body.linearDamping=0;c.body.angularDamping=0;c.body.isKinematic=kinematic;go.AddComponent<BoxCollider>().size=new Vector3(1,2,3);c.body.inertiaTensor=new Vector3(1.3f,2.1f,3.4f);c.body.inertiaTensorRotation=Quaternion.identity;Physics.SyncTransforms();c.origin=c.body.position;if(zero)c.body.Sleep();
        }
        else
        {
            c.body2=go.GetComponent<Rigidbody2D>();c.body2.mass=2;c.body2.gravityScale=0;c.body2.linearDamping=0;c.body2.angularDamping=0;c.body2.bodyType=kinematic?RigidbodyType2D.Kinematic:RigidbodyType2D.Dynamic;go.AddComponent<BoxCollider2D>().size=new Vector2(1,2);Physics2D.SyncTransforms();c.origin=c.body2.position;if(zero)c.body2.Sleep();
        }
        return c;
    }
    readonly List<Case> updateCases=new List<Case>();int updateFrames;bool recordingUpdate;
    void LateUpdate()
    {
        if(!recordingUpdate||updateFrames>=4)return;
        foreach(var c in updateCases)Value("update."+c.key+"."+updateFrames,c.Snapshot());updateFrames++;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Run()=>new GameObject("Probe").AddComponent<ForcesProbe>();
    IEnumerator Start()
    {
        var sim3=Physics.simulationMode;var sim2=Physics2D.simulationMode;float capture=Time.captureDeltaTime,fixedDelta=Time.fixedDeltaTime;
        Physics.simulationMode=SimulationMode.Script;Physics2D.simulationMode=SimulationMode2D.Script;
        var defaults=new GameObject("Defaults");var defaults2=new GameObject("Defaults2");var cf=defaults.AddComponent<ConstantForce>();var cf2=defaults2.AddComponent<ConstantForce2D>();Value("defaults.3",Properties(cf));Value("defaults.2",Properties(cf2));Value("defaults.required3",B(defaults.GetComponent<Rigidbody>()!=null));Value("defaults.required2",B(defaults2.GetComponent<Rigidbody2D>()!=null));DestroyImmediate(defaults);DestroyImmediate(defaults2);
        var asset=Resources.Load<GameObject>("Forces");var loaded=Instantiate(asset);var cloned=Instantiate(loaded);
        foreach(var go in new[]{loaded,cloned}){Value("serialized."+(go==loaded?"loaded":"cloned")+".3",Properties(go.GetComponentInChildren<ConstantForce>()));Value("serialized."+(go==loaded?"loaded":"cloned")+".2",Properties(go.GetComponentInChildren<ConstantForce2D>()));}DestroyImmediate(loaded);DestroyImmediate(cloned);
        foreach(int dimension in new[]{3,2})
        for(int parent=0;parent<3;parent++)
        foreach(bool kinematic in new[]{false,true})
        foreach(int count in new[]{1,2})
        foreach(bool zero in new[]{false,true})
        {
            var c=Make(dimension,parent,kinematic,count,zero,0);
            Value("created."+c.key+".count",(dimension==3?c.body.GetComponents<ConstantForce>().Length:c.body2.GetComponents<ConstantForce2D>().Length).ToString());
            Value("manual."+c.key+".initial",c.Snapshot());
            for(int step=0;step<7;step++)
            {
                if(step==2)c.Enable(false);if(step==3)c.Enable(true);
                if(step==4)c.root.SetActive(false);if(step==5)c.root.SetActive(true);
                if(dimension==3)Physics.Simulate(.1f);else Physics2D.Simulate(.1f);
                Value("manual."+c.key+"."+step,c.Snapshot());
                if(dimension==2&&(step==4||step==5))Value("manual."+c.key+".awake"+step,B(c.body2.IsAwake()));
            }
            DestroyImmediate(c.root);
        }
        {
            var control=new GameObject("Torque control");var rb=control.AddComponent<Rigidbody2D>();rb.mass=2;rb.gravityScale=0;rb.angularDamping=0;control.AddComponent<BoxCollider2D>().size=new Vector2(1,2);Physics2D.SyncTransforms();
            Value("torque_control.inertia",F(rb.inertia));rb.AddTorque(.5f);Physics2D.Simulate(.02f);Value("torque_control.velocity",F(rb.angularVelocity));DestroyImmediate(control);
        }
        Time.captureDeltaTime=.02f;Time.fixedDeltaTime=.02f;
        var fixedCases=new List<Case>();
        foreach(int dimension in new[]{3,2})foreach(int parent in new[]{0,2})foreach(bool zero in new[]{false,true})fixedCases.Add(Make(dimension,parent,false,1,zero,100+fixedCases.Count*100));
        Physics.simulationMode=SimulationMode.FixedUpdate;Physics2D.simulationMode=SimulationMode2D.FixedUpdate;
        for(int step=0;step<4;step++)
        {
            if(step==2)foreach(var c in fixedCases)c.Enable(false);
            if(step==3)foreach(var c in fixedCases)c.Enable(true);
            yield return new WaitForFixedUpdate();foreach(var c in fixedCases)Value("fixed."+c.key+"."+step,c.Snapshot());
        }
        Physics.simulationMode=SimulationMode.Script;Physics2D.simulationMode=SimulationMode2D.Script;
        for(int i=0;i<3;i++)yield return new WaitForFixedUpdate();
        foreach(var c in fixedCases)Value("suspended."+c.key+".before",c.Snapshot());
        Physics.Simulate(.1f);Physics2D.Simulate(.1f);
        foreach(var c in fixedCases)Value("suspended."+c.key+".after",c.Snapshot());
        foreach(var c in fixedCases)DestroyImmediate(c.root);
        foreach(int dimension in new[]{3,2})foreach(int parent in new[]{0,2})foreach(bool zero in new[]{false,true})updateCases.Add(Make(dimension,parent,false,1,zero,100+updateCases.Count*100));
        Physics.simulationMode=SimulationMode.Update;Physics2D.simulationMode=SimulationMode2D.Update;recordingUpdate=true;
        while(updateFrames<4)yield return null;
        recordingUpdate=false;foreach(var c in updateCases)DestroyImmediate(c.root);
        yield return PhaseProbe();
        Physics.simulationMode=sim3;Physics2D.simulationMode=sim2;Time.captureDeltaTime=capture;Time.fixedDeltaTime=fixedDelta;
        string path=Environment.GetEnvironmentVariable("FORCES_PROBE_OUT")??"/tmp/forces";Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
