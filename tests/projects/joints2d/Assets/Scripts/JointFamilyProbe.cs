using System;
using System.Globalization;
using UnityEngine;

public static class JointFamilyProbe
{
    static string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    static string B(bool b)=>b?"true":"false";
    static string V(Vector2 v)=>"["+F(v.x)+","+F(v.y)+"]";
    static string Properties(AnchoredJoint2D j)
    {
        string extra="";
        if(j is HingeJoint2D h)extra=",\"reference\":"+F(h.referenceAngle)+",\"angle\":"+F(h.jointAngle)+",\"speed\":"+F(h.jointSpeed)+",\"state\":"+(int)h.limitState+",\"motor\":"+B(h.useMotor)+",\"limits\":"+B(h.useLimits)+",\"use_anchor\":"+B(h.useConnectedAnchor)+",\"motor_speed\":"+F(h.motor.motorSpeed)+",\"max_torque\":"+F(h.motor.maxMotorTorque)+",\"min\":"+F(h.limits.min)+",\"max\":"+F(h.limits.max)+",\"motor_torque\":"+F(h.GetMotorTorque(.02f));
        if(j is FixedJoint2D x)extra=",\"reference\":"+F(x.referenceAngle)+",\"frequency\":"+F(x.frequency)+",\"damping\":"+F(x.dampingRatio);
        if(j is FrictionJoint2D f)extra=",\"max_force\":"+F(f.maxForce)+",\"max_torque\":"+F(f.maxTorque);
        return "{\"enabled\":"+B(j.enabled)+",\"connected\":\""+(j.connectedBody?j.connectedBody.name:"")+"\",\"anchor\":"+V(j.anchor)+",\"connected_anchor\":"+V(j.connectedAnchor)+",\"auto_anchor\":"+B(j.autoConfigureConnectedAnchor)+",\"collision\":"+B(j.enableCollision)+",\"break_force\":"+F(j.breakForce)+",\"break_torque\":"+F(j.breakTorque)+",\"break_action\":"+(int)j.breakAction+extra+"}";
    }
    static string Body(Rigidbody2D b)=>b?"{\"p\":"+V(b.position)+",\"r\":"+F(b.rotation)+",\"v\":"+V(b.linearVelocity)+",\"av\":"+F(b.angularVelocity)+",\"inertia\":"+F(b.inertia)+"}":"null";
    static string State(AnchoredJoint2D j,Rigidbody2D a,Rigidbody2D b)
    {
        string joint=j?",\"properties\":"+Properties(j)+",\"force\":"+V(j.reactionForce)+",\"torque\":"+F(j.reactionTorque)+",\"force_04\":"+V(j.GetReactionForce(.04f))+",\"torque_04\":"+F(j.GetReactionTorque(.04f)):"";
        return "{\"body\":"+Body(a)+",\"other\":"+Body(b)+",\"joint\":"+B(j!=null)+joint+",\"events\":"+Joint2DProbe.EventsJson()+"}";
    }
    public static readonly Type[] Types={typeof(HingeJoint2D),typeof(FixedJoint2D),typeof(FrictionJoint2D)};
    public static string Kind(Type t)=>t.Name.Replace("Joint2D","").ToLowerInvariant();
    public static void Run(Action<string,string> value)
    {
        foreach(var type in Types)
        {
            string kind=Kind(type);
            var api=new GameObject("API");api.transform.position=new Vector3(2,-3,0);api.transform.rotation=Quaternion.Euler(0,0,30);api.transform.localScale=new Vector3(2,3,1);
            var j=(AnchoredJoint2D)api.AddComponent(type);value(kind+".defaults",Properties(j));
            var other=new GameObject("Other");other.transform.position=new Vector3(-1,2,0);other.transform.rotation=Quaternion.Euler(0,0,-25);var b=other.AddComponent<Rigidbody2D>();j.connectedBody=b;value(kind+".connected",Properties(j));
            api.transform.rotation=Quaternion.Euler(0,0,60);other.transform.rotation=Quaternion.Euler(0,0,-15);value(kind+".rotated",Properties(j));
            j.autoConfigureConnectedAnchor=false;j.anchor=new Vector2(.2f,.4f);j.connectedAnchor=new Vector2(1,2);value(kind+".manual",Properties(j));
            j.autoConfigureConnectedAnchor=true;value(kind+".auto",Properties(j));
            j.breakForce=-2;j.breakTorque=-3;j.breakAction=(JointBreakAction2D)99;
            if(j is HingeJoint2D h){h.motor=new JointMotor2D{motorSpeed=-90,maxMotorTorque=-2};h.limits=new JointAngleLimits2D{min=30,max=-15};h.useMotor=true;h.useLimits=true;h.useConnectedAnchor=false;}
            if(j is FixedJoint2D x){x.frequency=-2;x.dampingRatio=2;}
            if(j is FrictionJoint2D f){f.maxForce=-2;f.maxTorque=-3;}
            value(kind+".limits_low",Properties(j));
            if(j is HingeJoint2D h2){h2.motor=new JointMotor2D{motorSpeed=2000000,maxMotorTorque=2000000};h2.limits=new JointAngleLimits2D{min=-2000000,max=2000000};}
            if(j is FixedJoint2D x2){x2.frequency=2000000;x2.dampingRatio=-2;}
            if(j is FrictionJoint2D f2){f2.maxForce=2000000;f2.maxTorque=2000000;}
            value(kind+".limits_high",Properties(j));
            UnityEngine.Object.DestroyImmediate(api);UnityEngine.Object.DestroyImmediate(other);
            var prefab=Resources.Load<GameObject>(type.Name.Replace("Joint2D",""));var loaded=UnityEngine.Object.Instantiate(prefab);var cloned=UnityEngine.Object.Instantiate(loaded);
            value(kind+".loaded",Properties(loaded.GetComponentInChildren<AnchoredJoint2D>()));value(kind+".cloned",Properties(cloned.GetComponentInChildren<AnchoredJoint2D>()));UnityEngine.Object.DestroyImmediate(loaded);UnityEngine.Object.DestroyImmediate(cloned);
            for(int scenario=0;scenario<15;scenario++)
            {
                var root=new GameObject("Root");if(scenario==5){root.transform.position=new Vector3(2,1,0);root.transform.rotation=Quaternion.Euler(0,0,20);root.transform.localScale=new Vector3(-2,3,1);}
                var go=new GameObject("Body");go.transform.SetParent(root.transform,false);go.transform.localPosition=new Vector3(1,-3,0);go.transform.localRotation=Quaternion.Euler(0,0,20);
                var a=go.AddComponent<Rigidbody2D>();a.mass=2;a.linearDamping=0;a.angularDamping=0;go.AddComponent<BoxCollider2D>().size=new Vector2(1,.5f);a.angularVelocity=30;
                if(type==typeof(FrictionJoint2D))a.linearVelocity=new Vector2(3,1);
                b=null;if(scenario==2||scenario==3||scenario==8||scenario==11||scenario==14){other=new GameObject("Other");other.transform.SetParent(root.transform,false);other.transform.position=new Vector3(-1,1,0);other.transform.rotation=Quaternion.Euler(0,0,-15);b=other.AddComponent<Rigidbody2D>();b.mass=3;b.bodyType=scenario==2?RigidbodyType2D.Kinematic:RigidbodyType2D.Dynamic;b.linearDamping=0;b.angularDamping=0;other.AddComponent<BoxCollider2D>().size=new Vector2(1,.5f);}
                j=(AnchoredJoint2D)go.AddComponent(type);j.connectedBody=b;j.autoConfigureConnectedAnchor=scenario!=4;j.anchor=new Vector2(.25f,.1f);j.connectedAnchor=Vector2.zero;j.enableCollision=scenario==14;
                if(j is HingeJoint2D hinge){hinge.useMotor=scenario==1||scenario>=6&&scenario<=8;hinge.motor=new JointMotor2D{motorSpeed=45,maxMotorTorque=5};hinge.useLimits=scenario==9||scenario==10;hinge.limits=new JointAngleLimits2D{min=-10,max=15};hinge.useConnectedAnchor=scenario!=12;}
                if(j is FixedJoint2D fix){fix.frequency=scenario==1?0:2;fix.dampingRatio=.7f;}
                if(j is FrictionJoint2D friction){friction.maxForce=scenario==1?0:5;friction.maxTorque=.5f;}
                if(scenario>=6&&scenario<=8){j.breakForce=.1f;j.breakTorque=.001f;j.breakAction=(JointBreakAction2D)(scenario-5);}
                go.AddComponent<JointBreakObserver>();Physics2D.SyncTransforms();Joint2DProbe.ClearEvents();string key=kind+".case"+scenario;value(key+".initial",State(j,a,b));
                for(int step=0;step<36;step++)
                {
                    Joint2DProbe.ClearEvents();
                    if(scenario==10&&step==5)j.enabled=false;if(scenario==10&&step==10)j.enabled=true;
                    if(scenario==11&&step==10)j.connectedBody=null;
                    if(scenario==13&&step==10){a.simulated=false;}if(scenario==13&&step==15){a.simulated=true;}
                    Physics2D.Simulate(scenario==14?.05f:.02f);
                    if(step<3||step==5||step==10||step==15||step==20||step==35)value(key+"."+step,State(j,a,b));
                }
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
