using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

public static class DrivenJointProbe
{
    static string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    static string B(bool b)=>b?"true":"false";
    static string V(Vector2 v)=>"["+F(v.x)+","+F(v.y)+"]";
    public static readonly Type[] Types={typeof(RelativeJoint2D),typeof(TargetJoint2D)};
    static string Properties(Joint2D j)
    {
        string extra="";
        if(j is RelativeJoint2D r)extra=",\"max_force\":"+F(r.maxForce)+",\"max_torque\":"+F(r.maxTorque)+",\"correction\":"+F(r.correctionScale)+",\"auto_offset\":"+B(r.autoConfigureOffset)+",\"linear_offset\":"+V(r.linearOffset)+",\"angular_offset\":"+F(r.angularOffset)+",\"target\":"+V(r.target);
        if(j is TargetJoint2D t)extra=",\"max_force\":"+F(t.maxForce)+",\"frequency\":"+F(t.frequency)+",\"damping\":"+F(t.dampingRatio)+",\"auto_target\":"+B(t.autoConfigureTarget)+",\"anchor\":"+V(t.anchor)+",\"target\":"+V(t.target);
        return "{\"enabled\":"+B(j.enabled)+",\"connected\":\""+(j.connectedBody?j.connectedBody.name:"")+"\",\"collision\":"+B(j.enableCollision)+",\"break_force\":"+F(j.breakForce)+",\"break_torque\":"+F(j.breakTorque)+",\"break_action\":"+(int)j.breakAction+extra+"}";
    }
    static string Body(Rigidbody2D b)=>b?"{\"p\":"+V(b.position)+",\"r\":"+F(b.rotation)+",\"v\":"+V(b.linearVelocity)+",\"av\":"+F(b.angularVelocity)+",\"inertia\":"+F(b.inertia)+"}":"null";
    static string State(Joint2D j,Rigidbody2D a,Rigidbody2D b)
    {
        string joint=j?",\"properties\":"+Properties(j)+",\"force\":"+V(j.reactionForce)+",\"torque\":"+F(j.reactionTorque)+",\"force_04\":"+V(j.GetReactionForce(.04f))+",\"torque_04\":"+F(j.GetReactionTorque(.04f)):"";
        return "{\"body\":"+Body(a)+",\"other\":"+Body(b)+",\"joint\":"+B(j!=null)+joint+",\"events\":"+Joint2DProbe.EventsJson()+"}";
    }
    public static void Run(Action<string,string> value)
    {
        var diagnostics=new List<int>();
        void OnLog(string condition,string stack,LogType type){if(condition.Contains("TargetJoint2D")&&condition.Contains("cannot"))diagnostics.Add((int)type);}
        Application.logMessageReceived+=OnLog;
        foreach(var type in Types)
        {
            string kind=type.Name.Replace("Joint2D","").ToLowerInvariant();
            var api=new GameObject("API");api.transform.position=new Vector3(2,-3,0);api.transform.rotation=Quaternion.Euler(0,0,30);api.transform.localScale=new Vector3(2,3,1);
            var j=(Joint2D)api.AddComponent(type);value(kind+".defaults",Properties(j));
            var other=new GameObject("Other");other.transform.position=new Vector3(-1,2,0);other.transform.rotation=Quaternion.Euler(0,0,-25);var b=other.AddComponent<Rigidbody2D>();j.connectedBody=b;value(kind+".connected",Properties(j));
            api.transform.rotation=Quaternion.Euler(0,0,60);other.transform.rotation=Quaternion.Euler(0,0,-15);value(kind+".rotated",Properties(j));
            if(j is RelativeJoint2D r){r.linearOffset=new Vector2(.2f,.4f);value(kind+".set_linear",Properties(j));r.angularOffset=35;value(kind+".set_angular",Properties(j));r.autoConfigureOffset=false;r.linearOffset=new Vector2(.2f,.4f);r.angularOffset=35;value(kind+".manual",Properties(j));r.autoConfigureOffset=true;value(kind+".auto",Properties(j));r.autoConfigureOffset=false;r.maxForce=-2;r.maxTorque=-3;r.correctionScale=-2;r.linearOffset=new Vector2(-2000000,2000000);r.angularOffset=-2000000;value(kind+".limits_low",Properties(j));r.maxForce=2000000;r.maxTorque=2000000;r.correctionScale=2;r.angularOffset=2000000;value(kind+".limits_high",Properties(j));}
            if(j is TargetJoint2D t){t.anchor=new Vector2(.2f,.4f);value(kind+".set_anchor",Properties(j));t.target=new Vector2(1,2);value(kind+".set_target",Properties(j));t.autoConfigureTarget=false;t.target=new Vector2(1,2);value(kind+".manual",Properties(j));t.autoConfigureTarget=true;value(kind+".auto",Properties(j));t.maxForce=-2;t.frequency=-2;t.dampingRatio=2;t.anchor=new Vector2(-2000000,2000000);value(kind+".limits_low",Properties(j));t.maxForce=2000000;t.frequency=2000000;t.dampingRatio=-2;t.target=new Vector2(-2000000,2000000);value(kind+".limits_high",Properties(j));}
            UnityEngine.Object.DestroyImmediate(api);UnityEngine.Object.DestroyImmediate(other);
            var prefab=Resources.Load<GameObject>(type.Name.Replace("Joint2D",""));var loaded=UnityEngine.Object.Instantiate(prefab);var clone=UnityEngine.Object.Instantiate(loaded);
            value(kind+".loaded",Properties(loaded.GetComponentInChildren<Joint2D>()));value(kind+".cloned",Properties(clone.GetComponentInChildren<Joint2D>()));UnityEngine.Object.DestroyImmediate(loaded);UnityEngine.Object.DestroyImmediate(clone);
            for(int scenario=0;scenario<18;scenario++)
            {
                var root=new GameObject("Root");if(scenario==5){root.transform.position=new Vector3(2,1,0);root.transform.rotation=Quaternion.Euler(0,0,20);root.transform.localScale=new Vector3(-2,3,1);}
                var go=new GameObject("Body");go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(1,-3,0);go.transform.localRotation=Quaternion.Euler(0,0,20);
                var a=go.AddComponent<Rigidbody2D>();a.mass=2;a.linearDamping=0;a.angularDamping=0;if(scenario==17)a.bodyType=RigidbodyType2D.Kinematic;go.AddComponent<BoxCollider2D>().size=new Vector2(1,.5f);a.angularVelocity=30;a.linearVelocity=new Vector2(3,1);
                b=null;if(scenario==2||scenario==3||scenario==8||scenario==11||scenario==14||scenario==16){other=new GameObject("Other");other.transform.SetParent(root.transform,false);other.transform.position=new Vector3(-1,1,0);other.transform.rotation=Quaternion.Euler(0,0,-15);b=other.AddComponent<Rigidbody2D>();b.mass=3;b.bodyType=scenario==2?RigidbodyType2D.Kinematic:RigidbodyType2D.Dynamic;b.linearDamping=0;b.angularDamping=0;other.AddComponent<BoxCollider2D>().size=new Vector2(1,.5f);}
                j=(Joint2D)go.AddComponent(type);j.connectedBody=b;j.enableCollision=scenario==14;
                if(j is RelativeJoint2D rel){rel.autoConfigureOffset=scenario!=4;rel.linearOffset=new Vector2(.3f,.4f);rel.angularOffset=15;rel.maxForce=scenario==1?0:scenario==16?10000:20;rel.maxTorque=scenario==1?0:scenario==16?1000:2;rel.correctionScale=scenario==15?0:scenario==16?1:.3f;}
                if(j is TargetJoint2D tar){tar.autoConfigureTarget=scenario!=4;tar.anchor=new Vector2(.25f,.1f);tar.target=Vector2.zero;tar.maxForce=scenario==1?0:scenario==16?10000:20;tar.frequency=scenario==15?0:3;tar.dampingRatio=.7f;}
                if(scenario>=6&&scenario<=8){j.breakForce=.1f;j.breakTorque=.001f;j.breakAction=(JointBreakAction2D)(scenario-5);}
                go.AddComponent<JointBreakObserver>();Physics2D.SyncTransforms();Joint2DProbe.ClearEvents();string key=kind+".case"+scenario;value(key+".initial",State(j,a,b));
                for(int step=0;step<36;step++)
                {
                    Joint2DProbe.ClearEvents();if(scenario==10&&step==5)j.enabled=false;if(scenario==10&&step==10)j.enabled=true;
                    if(scenario==11&&step==10)j.connectedBody=null;
                    if(scenario==12&&step==10){if(j is RelativeJoint2D rr){rr.autoConfigureOffset=false;rr.linearOffset=new Vector2(-1,2);rr.angularOffset=45;}if(j is TargetJoint2D tt){tt.autoConfigureTarget=false;tt.target=new Vector2(2,-1);}}
                    if(scenario==13&&step==10)a.simulated=false;if(scenario==13&&step==15)a.simulated=true;
                    Physics2D.Simulate(scenario==14?.05f:.02f);
                    if(step<3||step==5||step==10||step==15||step==20||step==35)value(key+"."+step,State(j,a,b));
                }
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        Application.logMessageReceived-=OnLog;
        value("target.diagnostics","["+string.Join(",",diagnostics)+"]");
    }
}
