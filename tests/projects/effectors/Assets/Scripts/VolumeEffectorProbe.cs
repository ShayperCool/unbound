using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class VolumeEffectorProbe
{
    public static readonly Type[] Types={typeof(AreaEffector2D),typeof(PointEffector2D),typeof(BuoyancyEffector2D)};
    static readonly string[] Common={"useColliderMask","colliderMask","requiresCollider","designedForTrigger","designedForNonTrigger"};
    static readonly string[] Area={"forceAngle","useGlobalAngle","forceMagnitude","forceVariation","drag","angularDrag","forceTarget"};
    static readonly string[] Point={"forceMagnitude","forceVariation","distanceScale","drag","angularDrag","forceSource","forceTarget","forceMode"};
    static readonly string[] Buoyancy={"surfaceLevel","density","linearDrag","angularDrag","flowAngle","flowMagnitude","flowVariation"};
    static string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    static string B(bool b)=>b?"true":"false";
    static string V(Vector2 v)=>"["+F(v.x)+","+F(v.y)+"]";
    static string Json(object o)=>o is bool b?B(b):o is float f?F(f):o is int i?i.ToString(CultureInfo.InvariantCulture):o is Enum e?Convert.ToInt32(e).ToString(CultureInfo.InvariantCulture):"null";
    public static string Kind(Type type)=>type.Name.Replace("Effector2D","").ToLowerInvariant();
    static string[] Fields(Type type)=>type==typeof(AreaEffector2D)?Area:type==typeof(PointEffector2D)?Point:Buoyancy;
    static string Properties(Effector2D e)
    {
        var values=new List<string>();foreach(var field in Common.Concat(Fields(e.GetType()))){var p=e.GetType().GetProperty(field,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);values.Add("\""+field+"\":"+Json(p?.GetValue(e)));}
        values.Add("\"enabled\":"+B(e.enabled));values.Add("\"colliders\":"+e.GetComponents<Collider2D>().Length);return "{"+string.Join(",",values)+"}";
    }
    static string State(Effector2D e,Rigidbody2D body)=>"{\"p\":"+V(body.position)+",\"r\":"+F(body.rotation)+",\"v\":"+V(body.linearVelocity)+",\"av\":"+F(body.angularVelocity)+",\"inertia\":"+F(body.inertia)+",\"com\":"+V(body.worldCenterOfMass)+",\"damping\":"+F(body.linearDamping)+",\"angular_damping\":"+F(body.angularDamping)+",\"force\":"+V(body.totalForce)+",\"torque\":"+F(body.totalTorque)+",\"events\":"+EffectorProbe.EventsJson()+",\"properties\":"+Properties(e)+"}";
    public static void Configure(Effector2D e)
    {
        if(e is AreaEffector2D a){a.forceAngle=25;a.useGlobalAngle=true;a.forceMagnitude=4;a.forceVariation=0;a.drag=.2f;a.angularDrag=.3f;a.forceTarget=EffectorSelection2D.Collider;}
        if(e is PointEffector2D p){p.forceMagnitude=4;p.forceVariation=0;p.distanceScale=2;p.drag=.2f;p.angularDrag=.3f;p.forceSource=EffectorSelection2D.Rigidbody;p.forceTarget=EffectorSelection2D.Collider;p.forceMode=EffectorForceMode2D.InverseLinear;}
        if(e is BuoyancyEffector2D b){b.surfaceLevel=.4f;b.density=2;b.linearDrag=.2f;b.angularDrag=.3f;b.flowAngle=25;b.flowMagnitude=4;b.flowVariation=0;}
    }
    static void SetVariation(Effector2D e,float value)
    {if(e is AreaEffector2D a)a.forceVariation=value;if(e is PointEffector2D p)p.forceVariation=value;if(e is BuoyancyEffector2D b)b.flowVariation=value;}
    public static void Run(Action<string,string> value)
    {
        foreach(var type in Types)
        {
            string kind=Kind(type);var api=new GameObject("API");var e=(Effector2D)api.AddComponent(type);value(kind+".defaults",Properties(e));
            foreach(var field in Fields(type)){var p=type.GetProperty(field);if(p.PropertyType==typeof(float))p.SetValue(e,-2f);else if(p.PropertyType==typeof(bool))p.SetValue(e,true);else if(p.PropertyType.IsEnum)p.SetValue(e,Enum.ToObject(p.PropertyType,-2));}value(kind+".limits_low",Properties(e));
            foreach(var field in Fields(type)){var p=type.GetProperty(field);if(p.PropertyType==typeof(float))p.SetValue(e,2000000f);else if(p.PropertyType.IsEnum)p.SetValue(e,Enum.ToObject(p.PropertyType,99));}value(kind+".limits_high",Properties(e));UnityEngine.Object.DestroyImmediate(api);
            var prefab=Resources.Load<GameObject>(kind);var loaded=UnityEngine.Object.Instantiate(prefab);var cloned=UnityEngine.Object.Instantiate(loaded);value(kind+".loaded",Properties(loaded.GetComponent<Effector2D>()));value(kind+".cloned",Properties(cloned.GetComponent<Effector2D>()));UnityEngine.Object.DestroyImmediate(loaded);UnityEngine.Object.DestroyImmediate(cloned);
            for(int scenario=0;scenario<32;scenario++)
            {
                UnityEngine.Random.InitState(1234567);var randomBefore=UnityEngine.Random.state;
                var root=new GameObject("Root");if(scenario==5){root.transform.position=new Vector3(2,1,0);root.transform.rotation=Quaternion.Euler(0,0,20);root.transform.localScale=new Vector3(-2,3,1);}
                var field=new GameObject("Field");field.transform.SetParent(root.transform,false);var volume=field.AddComponent<BoxCollider2D>();volume.size=new Vector2(10,10);volume.isTrigger=scenario!=11;volume.usedByEffector=scenario!=7;
                if(scenario==2||scenario==3)field.transform.localRotation=Quaternion.Euler(0,0,30);
                if(scenario==4){volume.offset=new Vector2(.5f,.25f);var source=field.AddComponent<Rigidbody2D>();source.mass=10;source.gravityScale=0;source.constraints=RigidbodyConstraints2D.FreezeAll;source.centerOfMass=new Vector2(.2f,.3f);}
                e=(Effector2D)field.AddComponent(type);e.useColliderMask=scenario!=10;e.colliderMask=scenario==9||scenario==10||scenario==19?0:-1;if(scenario==8)e.enabled=false;
                var target=new GameObject("Body");target.transform.SetParent(root.transform,false);target.transform.localPosition=new Vector3(scenario==6?20:scenario==11?5.1f:1,scenario==28?-1:0,0);
                var rb=target.AddComponent<Rigidbody2D>();rb.mass=2;rb.gravityScale=type==typeof(BuoyancyEffector2D)?1:0;rb.linearDamping=0;rb.angularDamping=0;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;if(scenario==17)rb.bodyType=RigidbodyType2D.Kinematic;
                Collider2D shape;
                if(scenario==16){var circle=target.AddComponent<CircleCollider2D>();circle.radius=.5f;shape=circle;}else{var box=target.AddComponent<BoxCollider2D>();box.size=new Vector2(.5f,.5f);shape=box;}
                if(scenario==4){shape.offset=new Vector2(.5f,.3f);rb.centerOfMass=new Vector2(-.2f,.1f);}
                if(scenario==18)shape.isTrigger=true;if(scenario==29)rb.gravityScale=0;if(scenario==30)rb.mass=4;if(scenario==31)rb.mass=1;
                if(scenario==14||scenario==15){shape.offset=new Vector2(-.3f,0);var extra=target.AddComponent<BoxCollider2D>();extra.size=new Vector2(.5f,.5f);extra.offset=new Vector2(.3f,.2f);}
                if(scenario==1||scenario==26||scenario==27){rb.linearVelocity=new Vector2(3,1);rb.angularVelocity=30;}
                Physics2D.gravity=new Vector2(0,-9.81f);
                if(e is AreaEffector2D a){a.forceAngle=scenario==2||scenario==3?25:0;a.useGlobalAngle=scenario==3;a.forceMagnitude=scenario==1?0:4;a.forceVariation=scenario==22||scenario==24||scenario==25?2:scenario==23?-2:0;a.drag=scenario==1?2:0;a.angularDrag=scenario==1?3:0;a.forceTarget=scenario==4||scenario==15?EffectorSelection2D.Collider:EffectorSelection2D.Rigidbody;}
                if(e is PointEffector2D p){p.forceMagnitude=scenario==3?-4:scenario==1?0:4;p.forceVariation=scenario==22||scenario==24||scenario==25?2:scenario==23?-2:0;p.distanceScale=scenario==16?2:1;p.drag=scenario==1?2:0;p.angularDrag=scenario==1?3:0;p.forceMode=scenario==2?EffectorForceMode2D.InverseSquared:scenario==16?EffectorForceMode2D.InverseLinear:EffectorForceMode2D.Constant;p.forceSource=scenario==4?EffectorSelection2D.Rigidbody:EffectorSelection2D.Collider;p.forceTarget=scenario==4||scenario==15?EffectorSelection2D.Collider:EffectorSelection2D.Rigidbody;}
                if(e is BuoyancyEffector2D b){b.surfaceLevel=scenario==16?.2f:0;b.density=2;b.linearDrag=scenario==1||scenario==27?2:0;b.angularDrag=scenario==1||scenario==26?3:0;b.flowMagnitude=scenario==3?2:0;b.flowAngle=25;b.flowVariation=scenario==22||scenario==24||scenario==25?2:scenario==23?-2:0;}
                target.AddComponent<EffectorBodyObserver>();Physics2D.SyncTransforms();EffectorProbe.ClearEvents();string key=kind+".case"+scenario;value(key+".initial",State(e,rb));
                for(int step=0;step<32;step++)
                {
                    EffectorProbe.ClearEvents();if((scenario==13||scenario==24)&&step==5)e.enabled=false;if((scenario==13||scenario==24)&&step==10)e.enabled=true;
                    if(scenario==19&&step==10)e.colliderMask=-1;if(scenario==20&&step==5)volume.enabled=false;
                    if(scenario==21&&step==5)rb.simulated=false;if(scenario==21&&step==10)rb.simulated=true;
                    if(scenario==25&&step==5)SetVariation(e,0);if(scenario==25&&step==10)SetVariation(e,2);
                    Physics2D.Simulate(scenario==12?.05f:.02f);
                    if(step<3||step==5||step==10||step==15||step==20||step==31)value(key+"."+step,State(e,rb));
                }
                value(key+".random","{\"unchanged\":"+B(UnityEngine.Random.state.Equals(randomBefore))+",\"next\":"+F(UnityEngine.Random.value)+"}");
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
