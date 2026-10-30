using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class Joint2DProbe : MonoBehaviour
{
    readonly StringBuilder output=new StringBuilder();
    static string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    static string V(Vector2 v)=>"["+F(v.x)+","+F(v.y)+"]";
    static string B(bool b)=>b?"true":"false";
    static string S(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    void Value(string k,string v)=>output.Append("{\"k\":").Append(S(k)).Append(",\"v\":").Append(v).Append("}\n");
    static readonly List<string> events=new List<string>();
    public static void Event(string s)=>events.Add(s);
    static string Properties(AnchoredJoint2D j)
    {
        string extra=j is DistanceJoint2D d?",\"auto_distance\":"+B(d.autoConfigureDistance)+",\"distance\":"+F(d.distance)+",\"max_only\":"+B(d.maxDistanceOnly)
            :j is SpringJoint2D s?",\"auto_distance\":"+B(s.autoConfigureDistance)+",\"distance\":"+F(s.distance)+",\"frequency\":"+F(s.frequency)+",\"damping\":"+F(s.dampingRatio):"";
        return "{\"enabled\":"+B(j.enabled)+",\"connected\":"+S(j.connectedBody?j.connectedBody.name:"")+",\"attached\":"+S(j.attachedRigidbody?j.attachedRigidbody.name:"")+",\"anchor\":"+V(j.anchor)+",\"connected_anchor\":"+V(j.connectedAnchor)+",\"auto_anchor\":"+B(j.autoConfigureConnectedAnchor)+",\"collision\":"+B(j.enableCollision)+",\"break_force\":"+F(j.breakForce)+",\"break_torque\":"+F(j.breakTorque)+",\"break_action\":"+(int)j.breakAction+extra+"}";
    }
    static string Body(Rigidbody2D b)=>"{\"p\":"+V(b.position)+",\"r\":"+F(b.rotation)+",\"v\":"+V(b.linearVelocity)+",\"av\":"+F(b.angularVelocity)+"}";
    static string State(AnchoredJoint2D j,Rigidbody2D a,Rigidbody2D b)
    {
        string joint=j?",\"force\":"+V(j.reactionForce)+",\"torque\":"+F(j.reactionTorque)+",\"force_04\":"+V(j.GetReactionForce(.04f))+",\"force_zero\":"+V(j.GetReactionForce(0))+",\"force_negative\":"+V(j.GetReactionForce(-.02f))+",\"properties\":"+Properties(j):"";
        return "{\"body\":"+Body(a)+",\"other\":"+(b?Body(b):"null")+",\"joint\":"+B(j!=null)+joint+",\"events\":["+string.Join(",",events.Select(S))+"]}";
    }
    static void Distance(AnchoredJoint2D joint,float length,bool auto)
    {
        if(joint is DistanceJoint2D d){d.autoConfigureDistance=auto;d.distance=length;}
        if(joint is SpringJoint2D s){s.autoConfigureDistance=auto;s.distance=length;}
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Run()=>new GameObject("Probe").AddComponent<Joint2DProbe>();
    void Start()
    {
        var mode=Physics2D.simulationMode;var gravity=Physics2D.gravity;Physics2D.simulationMode=SimulationMode2D.Script;Physics2D.gravity=new Vector2(0,-9.81f);
        foreach(bool spring in new[]{false,true})
        {
            string kind=spring?"spring":"distance";
            var api=new GameObject("API");api.transform.position=new Vector3(2,-3,0);api.transform.rotation=Quaternion.Euler(0,0,30);api.transform.localScale=new Vector3(2,3,1);
            var j=(AnchoredJoint2D)api.AddComponent(spring?typeof(SpringJoint2D):typeof(DistanceJoint2D));Value(kind+".defaults",Properties(j));
            j.anchor=new Vector2(.3f,.4f);Value(kind+".set_anchor",Properties(j));j.connectedAnchor=new Vector2(1,2);Value(kind+".set_connected_anchor",Properties(j));
            j.autoConfigureConnectedAnchor=false;Value(kind+".manual_anchor",Properties(j));j.autoConfigureConnectedAnchor=true;Value(kind+".auto_anchor",Properties(j));
            Distance(j,5,false);Value(kind+".distance",Properties(j));Distance(j,-2,false);Value(kind+".negative_distance",Properties(j));Distance(j,5,true);Value(kind+".auto_distance",Properties(j));
            if(j is SpringJoint2D sp){sp.frequency=-2;sp.dampingRatio=2;Value(kind+".limits_high",Properties(j));sp.frequency=2000000;sp.dampingRatio=-2;Value(kind+".limits_low",Properties(j));}
            DestroyImmediate(api);
            var prefab=Resources.Load<GameObject>(spring?"Spring":"Distance");var loaded=Instantiate(prefab);var cloned=Instantiate(loaded);
            Value(kind+".loaded",Properties(loaded.GetComponentInChildren<AnchoredJoint2D>()));Value(kind+".cloned",Properties(cloned.GetComponentInChildren<AnchoredJoint2D>()));DestroyImmediate(loaded);DestroyImmediate(cloned);
            for(int scenario=0;scenario<12;scenario++)
            {
                var root=new GameObject("Root");
                if(scenario==5){root.transform.position=new Vector3(2,1,0);root.transform.rotation=Quaternion.Euler(0,0,20);root.transform.localScale=new Vector3(-2,3,1);}
                var go=new GameObject("Body");go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(1,-3,0);
                var a=go.AddComponent<Rigidbody2D>();a.mass=2;a.linearDamping=0;a.angularDamping=0;go.AddComponent<CircleCollider2D>().radius=.25f;
                Rigidbody2D b=null;
                if(scenario==2||scenario==3||scenario==8||scenario==11){var other=new GameObject("Other");other.transform.SetParent(root.transform,false);other.transform.position=new Vector3(-1,1,0);b=other.AddComponent<Rigidbody2D>();b.mass=3;b.bodyType=scenario==2?RigidbodyType2D.Kinematic:RigidbodyType2D.Dynamic;b.linearDamping=0;b.angularDamping=0;other.AddComponent<CircleCollider2D>().radius=.25f;}
                var joint=(AnchoredJoint2D)go.AddComponent(spring?typeof(SpringJoint2D):typeof(DistanceJoint2D));joint.connectedBody=b;joint.autoConfigureConnectedAnchor=scenario==4;joint.anchor=scenario==5?new Vector2(.2f,.3f):Vector2.zero;joint.connectedAnchor=Vector2.zero;Distance(joint,2,scenario==4);
                if(joint is DistanceJoint2D dist)dist.maxDistanceOnly=scenario==1;
                if(joint is SpringJoint2D spr){spr.frequency=scenario==1?0:2;spr.dampingRatio=.7f;}
                if(scenario>=6&&scenario<=9){joint.breakForce=.1f;joint.breakAction=(JointBreakAction2D)(scenario-6);}
                go.AddComponent<JointBreakObserver>();
                Physics2D.SyncTransforms();string key=kind+".case"+scenario;events.Clear();Value(key+".initial",State(joint,a,b));
                for(int step=0;step<36;step++)
                {
                    events.Clear();
                    if(scenario==10&&step==5){joint.enabled=false;}
                    if(scenario==10&&step==10){joint.enabled=true;}
                    if(scenario==11&&step==10){joint.connectedBody=null;}
                    Physics2D.Simulate(scenario==9?.05f:.02f);
                    if(step<3||step==5||step==10||step==20||step==35)Value(key+"."+step,State(joint,a,b));
                }
                DestroyImmediate(root);
            }
        }
        Physics2D.simulationMode=mode;Physics2D.gravity=gravity;
        string path=Environment.GetEnvironmentVariable("JOINTS2D_PROBE_OUT")??"/tmp/joints2d";Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
