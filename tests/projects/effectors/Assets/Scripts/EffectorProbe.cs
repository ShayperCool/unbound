using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

public class EffectorProbe : MonoBehaviour
{
    readonly StringBuilder output=new StringBuilder();
    static readonly List<string> events=new List<string>();
    public static void Event(string e)=>events.Add(e);
    public static readonly Type[] Types={typeof(PlatformEffector2D),typeof(SurfaceEffector2D)};
    static readonly string[] Common={"useColliderMask","colliderMask","requiresCollider","designedForTrigger","designedForNonTrigger"};
    static readonly string[] Platform={"useOneWay","useOneWayGrouping","useSideFriction","useSideBounce","surfaceArc","sideArc","rotationalOffset"};
    static readonly string[] Surface={"speed","speedVariation","forceScale","useContactForce","useFriction","useBounce"};
    static string S(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    static string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    static string V(Vector2 v)=>"["+F(v.x)+","+F(v.y)+"]";
    static string B(bool b)=>b?"true":"false";
    static string Json(object value)=>value is bool b?B(b):value is float f?F(f):value is int i?i.ToString(CultureInfo.InvariantCulture):value==null?"null":S(value.ToString());
    void Value(string k,string v)=>output.Append("{\"k\":").Append(S(k)).Append(",\"v\":").Append(v).Append("}\n");
    static string Properties(Effector2D eff)
    {
        var names=Common.Concat(eff is PlatformEffector2D?Platform:Surface);
        var data=new List<string>();foreach(string name in names){var prop=eff.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);data.Add(S(name)+":"+Json(prop?.GetValue(eff)));}
        data.Add("\"enabled\":"+B(eff.enabled));data.Add("\"colliders\":"+eff.GetComponents<Collider2D>().Length);return "{"+string.Join(",",data)+"}";
    }
    static string State(Effector2D eff,Rigidbody2D rb,Collider2D platform)
    {
        var own=rb.GetComponent<Collider2D>();
        var contacts=new ContactPoint2D[16];int count=rb.GetContacts(contacts);
        string normals="["+string.Join(",",contacts.Take(count).Select(c=>V(c.normal)))+"]";
        return "{\"p\":"+V(rb.position)+",\"r\":"+F(rb.rotation)+",\"v\":"+V(rb.linearVelocity)+",\"av\":"+F(rb.angularVelocity)+",\"touching\":"+B(own.IsTouching(platform))+",\"contacts\":"+count+",\"normals\":"+normals+",\"events\":["+string.Join(",",events.Select(S))+"],\"properties\":"+Properties(eff)+"}";
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Run()=>new GameObject("Probe").AddComponent<EffectorProbe>();
    void Start()
    {
        var mode=Physics2D.simulationMode;var gravity=Physics2D.gravity;Physics2D.simulationMode=SimulationMode2D.Script;
        var materialObject=new GameObject("MaterialAPI");var materialCollider=materialObject.AddComponent<BoxCollider2D>();
        var shared=new PhysicsMaterial2D("Shared"){friction=.2f,bounciness=.3f,frictionCombine=PhysicsMaterialCombine2D.Minimum,bounceCombine=PhysicsMaterialCombine2D.Mean};
        string MaterialState()=>"{\"friction\":"+F(materialCollider.friction)+",\"bounce\":"+F(materialCollider.bounciness)+",\"friction_combine\":"+(int)materialCollider.frictionCombine+",\"bounce_combine\":"+(int)materialCollider.bounceCombine+",\"material_friction\":"+F(shared.friction)+",\"material_bounce\":"+F(shared.bounciness)+"}";
        materialCollider.sharedMaterial=shared;Value("material.assigned",MaterialState());
        shared.friction=.7f;shared.bounciness=.8f;shared.frictionCombine=PhysicsMaterialCombine2D.Maximum;shared.bounceCombine=PhysicsMaterialCombine2D.Multiply;Value("material.mutated",MaterialState());
        Physics2D.SyncTransforms();Value("material.synced",MaterialState());materialCollider.offset=Vector2.one;Physics2D.SyncTransforms();Value("material.geometry_changed",MaterialState());
        materialCollider.enabled=false;materialCollider.enabled=true;Physics2D.SyncTransforms();Value("material.reenabled",MaterialState());
        materialCollider.sharedMaterial=shared;Value("material.reassigned",MaterialState());
        var materialBody=materialObject.AddComponent<Rigidbody2D>();materialCollider.sharedMaterial=null;materialBody.sharedMaterial=shared;Value("material.body_assigned",MaterialState());
        shared.friction=.1f;shared.bounciness=.2f;Value("material.body_mutated",MaterialState());Physics2D.SyncTransforms();Value("material.body_synced",MaterialState());
        materialBody.sharedMaterial=shared;Value("material.body_reassigned",MaterialState());
        materialObject.SetActive(false);shared.friction=.9f;shared.bounciness=.1f;materialBody.sharedMaterial=shared;Value("material.inactive_assigned",MaterialState());
        materialObject.SetActive(true);Physics2D.SyncTransforms();Value("material.reactivated",MaterialState());
        DestroyImmediate(materialObject);DestroyImmediate(shared);
        foreach(var type in Types)
        {
            string kind=type==typeof(PlatformEffector2D)?"platform":"surface";var api=new GameObject("API");var eff=(Effector2D)api.AddComponent(type);Value(kind+".defaults",Properties(eff));
            eff.useColliderMask=false;eff.colliderMask=0x12345678;
            if(eff is PlatformEffector2D p){p.surfaceArc=-20;p.sideArc=500;p.rotationalOffset=-2000000;p.useOneWay=false;p.useOneWayGrouping=true;p.useSideFriction=true;p.useSideBounce=true;Value(kind+".limits_low",Properties(eff));p.surfaceArc=500;p.sideArc=-20;p.rotationalOffset=2000000;Value(kind+".limits_high",Properties(eff));}
            if(eff is SurfaceEffector2D s){s.speed=-2000000;s.speedVariation=-2;s.forceScale=-2;s.useContactForce=false;s.useFriction=false;s.useBounce=false;Value(kind+".limits_low",Properties(eff));s.speed=2000000;s.speedVariation=2000000;s.forceScale=2;Value(kind+".limits_high",Properties(eff));}
            var second=api.AddComponent(type);Value(kind+".duplicates","{\"added\":"+B(second!=null)+",\"count\":"+api.GetComponents(type).Length+",\"disallow_attribute\":"+B(type.IsDefined(typeof(DisallowMultipleComponent),true))+"}");var cross=api.AddComponent(type==typeof(PlatformEffector2D)?typeof(SurfaceEffector2D):typeof(PlatformEffector2D));Value(kind+".cross_type","{\"added\":"+B(cross!=null)+",\"count\":"+api.GetComponents<Effector2D>().Length+"}");DestroyImmediate(api);
            var prefab=Resources.Load<GameObject>(kind);var loaded=Instantiate(prefab);var cloned=Instantiate(loaded);Value(kind+".loaded",Properties(loaded.GetComponent<Effector2D>()));Value(kind+".cloned",Properties(cloned.GetComponent<Effector2D>()));DestroyImmediate(loaded);DestroyImmediate(cloned);
            for(int scenario=0;scenario<(kind=="platform"?31:26);scenario++)
            {
                UnityEngine.Random.InitState(1234567);var randomBefore=UnityEngine.Random.state;
                var root=new GameObject("Root");var floor=new GameObject("Floor");floor.transform.SetParent(root.transform,false);
                Collider2D collider=floor.AddComponent<BoxCollider2D>();((BoxCollider2D)collider).size=new Vector2(kind=="surface"?40:8,.4f);collider.usedByEffector=scenario!=7;
                var material=new PhysicsMaterial2D("Material"){friction=.4f,bounciness=.25f};collider.sharedMaterial=material;
                eff=(Effector2D)floor.AddComponent(type);eff.useColliderMask=true;eff.colliderMask=((scenario==14||scenario==15)&&kind=="platform"||(scenario==12||scenario==13)&&kind=="surface")?0:-1;
                var body=new GameObject("Body");body.transform.SetParent(root.transform,false);body.transform.position=new Vector3(0,2,0);var rb=body.AddComponent<Rigidbody2D>();rb.mass=2;rb.linearDamping=0;rb.angularDamping=0;rb.sleepMode=RigidbodySleepMode2D.NeverSleep;
                var circle=body.AddComponent<CircleCollider2D>();circle.radius=.25f;circle.sharedMaterial=material;body.AddComponent<EffectorBodyObserver>();
                Physics2D.gravity=new Vector2(0,-9.81f);
                if(eff is PlatformEffector2D plat)
                {
                    bool up=scenario==1||scenario==3||scenario==6||scenario==7||scenario==8||scenario==11||scenario==14||scenario==15||scenario==16||scenario==17||scenario==18||scenario==19||scenario==21;
                    if(up){body.transform.position=new Vector3(0,scenario==3?0:-2,0);rb.linearVelocity=new Vector2(0,scenario==3?6:10);}
                    if(scenario==2){body.transform.position=new Vector3(-5,0,0);rb.linearVelocity=new Vector2(6,0);Physics2D.gravity=Vector2.zero;}
                    if(scenario==3)Physics2D.gravity=Vector2.zero;
                    if(scenario==4){rb.linearVelocity=new Vector2(0,-6);Physics2D.gravity=Vector2.zero;}
                    if(scenario==5){floor.transform.rotation=Quaternion.Euler(0,0,30);floor.transform.localScale=new Vector3(-1,1,1);}
                    if(scenario==6||scenario==19)eff.enabled=false;
                    if(scenario==8)plat.useOneWay=false;if(scenario==9)plat.surfaceArc=90;if(scenario==10)plat.surfaceArc=0;if(scenario==11)plat.surfaceArc=360;
                    if(scenario==12)plat.rotationalOffset=180;if(scenario==13)plat.rotationalOffset=90;
                    if(scenario==15)eff.useColliderMask=false;
                    if(scenario==16||scenario==17){plat.useOneWayGrouping=scenario==16;circle.offset=new Vector2(-.3f,0);var extra=body.AddComponent<CircleCollider2D>();extra.radius=.25f;extra.offset=new Vector2(.3f,.2f);}
                    if(scenario==22||scenario==23){body.transform.position=new Vector3(0,-1.5f,0);rb.linearVelocity=new Vector2(0,2);Physics2D.gravity=Vector2.zero;plat.useOneWayGrouping=scenario==22;circle.offset=new Vector2(-.3f,-.8f);var upper=body.AddComponent<CircleCollider2D>();upper.radius=.25f;upper.offset=new Vector2(.3f,.8f);}
                    if(scenario>=24&&scenario<=28){((BoxCollider2D)collider).size=new Vector2(8,2);body.transform.position=new Vector3(-5,-.5f,0);rb.linearVelocity=new Vector2(6,2);Physics2D.gravity=Vector2.zero;plat.useSideFriction=scenario!=26;plat.useSideBounce=scenario==25;}
                    if(scenario==29||scenario==30){body.transform.position=Vector3.zero;rb.linearVelocity=Vector2.zero;Physics2D.gravity=Vector2.zero;plat.useOneWayGrouping=scenario==29;circle.offset=new Vector2(-.3f,-.25f);var upper=body.AddComponent<CircleCollider2D>();upper.radius=.25f;upper.offset=new Vector2(.3f,.25f);}
                    if(scenario==20)collider.isTrigger=true;if(scenario==21)floor.transform.localScale=new Vector3(1,-1,1);
                }
                if(eff is SurfaceEffector2D surf)
                {
                    surf.speed=scenario==1?-3:scenario==11?0:3;surf.speedVariation=0;surf.useContactForce=scenario!=4;surf.forceScale=scenario==2?0:scenario==3?.5f:scenario==10?2:1;
                    if(scenario==4)surf.useContactForce=false;if(scenario==5)floor.transform.rotation=Quaternion.Euler(0,0,30);if(scenario==6)eff.enabled=false;
                    if(scenario==8)surf.useFriction=false;if(scenario==9)surf.useBounce=false;if(scenario==13)eff.useColliderMask=false;
                    if(scenario==14){body.transform.position=new Vector3(0,.5f,0);rb.linearVelocity=new Vector2(-3,0);rb.mass=5;}
                    if(scenario==15)material.bounciness=1;
                    if(scenario==18||scenario==20||scenario==24)surf.speedVariation=1;if(scenario==19)surf.speedVariation=-1;
                    if(scenario==21)floor.transform.localScale=new Vector3(-1,1,1);if(scenario==22)floor.transform.rotation=Quaternion.Euler(0,0,180);
                    if(scenario==23){DestroyImmediate(collider);var round=floor.AddComponent<CircleCollider2D>();round.radius=2;round.usedByEffector=true;round.sharedMaterial=material;floor.transform.position=new Vector3(0,-2,0);collider=round;}
                }
                bool matrixCase=kind=="platform"?scenario==27||scenario==28:scenario==16||scenario==17;
                bool oldIgnore=Physics2D.GetIgnoreLayerCollision(8,9);
                if(matrixCase){floor.layer=8;body.layer=9;Physics2D.IgnoreLayerCollision(8,9,true);eff.colliderMask=1<<9;eff.useColliderMask=kind=="platform"?scenario==27:scenario==16;}
                Physics2D.SyncTransforms();string key=kind+".case"+scenario;events.Clear();Value(key+".initial",State(eff,rb,collider));
                for(int step=0;step<150;step++)
                {
                    events.Clear();if(kind=="platform"&&scenario==18&&step==25)eff.enabled=false;if(kind=="platform"&&scenario==19&&step==25)eff.enabled=true;
                    if(kind=="surface"&&scenario==24&&step==60)eff.enabled=false;if(kind=="surface"&&scenario==24&&step==65)eff.enabled=true;
                    if(kind=="surface"&&scenario==25&&step==60)((SurfaceEffector2D)eff).speedVariation=1;
                    Physics2D.Simulate(.02f);
                    if(step==0||step==1||step==5||step==10||step==20||step==30||step==50||step==75||step==100||step==149)Value(key+"."+step,State(eff,rb,collider));
                }
                if(kind=="surface")Value(key+".random","{\"unchanged\":"+B(UnityEngine.Random.state.Equals(randomBefore))+",\"next\":"+F(UnityEngine.Random.value)+"}");
                DestroyImmediate(root);DestroyImmediate(material);if(matrixCase)Physics2D.IgnoreLayerCollision(8,9,oldIgnore);
            }
        }
        Physics2D.gravity=new Vector2(0,-9.81f);
        var randomFloor=new GameObject("RandomFloor");var randomCollider=randomFloor.AddComponent<BoxCollider2D>();randomCollider.size=new Vector2(40,.4f);randomCollider.usedByEffector=true;
        var randomEffector=randomFloor.AddComponent<SurfaceEffector2D>();randomEffector.speed=0;randomEffector.speedVariation=1;randomEffector.forceScale=1;randomEffector.useContactForce=false;randomEffector.useFriction=false;randomEffector.useBounce=false;
        var randomBody=new GameObject("RandomBody");randomBody.transform.position=new Vector3(0,.45f,0);var randomRb=randomBody.AddComponent<Rigidbody2D>();randomRb.sleepMode=RigidbodySleepMode2D.NeverSleep;randomRb.linearDamping=0;randomBody.AddComponent<CircleCollider2D>().radius=.25f;
        Physics2D.SyncTransforms();
        string Samples(int count){var values=new List<string>();for(int n=0;n<count;n++){Physics2D.Simulate(.02f);values.Add(F(randomRb.linearVelocity.x));}return "["+string.Join(",",values)+"]";}
        Value("surface.random_sequence",Samples(64));randomEffector.enabled=false;Physics2D.Simulate(.02f);randomEffector.enabled=true;Value("surface.random_reenabled",Samples(4));
        var randomClone=Instantiate(randomFloor);DestroyImmediate(randomFloor);randomFloor=randomClone;randomEffector=randomFloor.GetComponent<SurfaceEffector2D>();Physics2D.SyncTransforms();Value("surface.random_cloned",Samples(4));
        randomEffector.speedVariation=0;Value("surface.random_zero",Samples(4));randomEffector.speedVariation=1;Value("surface.random_resumed",Samples(4));
        DestroyImmediate(randomFloor);DestroyImmediate(randomBody);
        Physics2D.simulationMode=mode;Physics2D.gravity=gravity;
        string path=Environment.GetEnvironmentVariable("EFFECTORS_PROBE_OUT")??"/tmp/effectors";Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
