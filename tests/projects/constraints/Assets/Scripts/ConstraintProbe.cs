using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;

public class ConstraintProbe : MonoBehaviour
{
    public static readonly Type[] Types={typeof(PositionConstraint),typeof(RotationConstraint),typeof(ScaleConstraint),typeof(ParentConstraint),typeof(AimConstraint),typeof(LookAtConstraint)};
    readonly StringBuilder output=new StringBuilder();
    static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
    static string V(Vector3 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
    static string Q(Quaternion q)=>"["+F(q.x)+","+F(q.y)+","+F(q.z)+","+F(q.w)+"]";
    static string B(bool b)=>b?"true":"false";
    static string S(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    void Value(string k,string v)=>output.Append("{\"k\":").Append(S(k)).Append(",\"v\":").Append(v).Append("}\n");
    void Try(string k,Action a){try{a();Value(k,S("ok"));}catch(Exception e){Value(k,S(e.GetType().Name));}}
    public static void Configure(IConstraint c,int axes,bool offset)
    {
        var rest=new Vector3(1,2,3);var off=offset?new Vector3(0.3f,-0.4f,0.7f):Vector3.zero;
        var rrest=new Vector3(10,20,30);var roff=offset?new Vector3(5,-10,15):Vector3.zero;
        if(c is PositionConstraint p){p.translationAtRest=rest;p.translationOffset=off;p.translationAxis=(Axis)axes;}
        if(c is RotationConstraint r){r.rotationAtRest=rrest;r.rotationOffset=roff;r.rotationAxis=(Axis)axes;}
        if(c is ScaleConstraint s){s.scaleAtRest=rest;s.scaleOffset=off;s.scalingAxis=(Axis)axes;}
        if(c is ParentConstraint pa){pa.translationAtRest=rest;pa.rotationAtRest=rrest;pa.translationAxis=(Axis)axes;pa.rotationAxis=(Axis)axes;}
        if(c is AimConstraint ai){ai.rotationAtRest=rrest;ai.rotationOffset=roff;ai.rotationAxis=(Axis)axes;}
        if(c is LookAtConstraint l){l.rotationAtRest=rrest;l.rotationOffset=roff;}
    }
    static string Properties(IConstraint c)
    {
        string extra="";
        if(c is PositionConstraint p)extra=",\"rest\":"+V(p.translationAtRest)+",\"offset\":"+V(p.translationOffset)+",\"axis\":"+(int)p.translationAxis;
        if(c is RotationConstraint r)extra=",\"rest\":"+V(r.rotationAtRest)+",\"offset\":"+V(r.rotationOffset)+",\"axis\":"+(int)r.rotationAxis;
        if(c is ScaleConstraint s)extra=",\"rest\":"+V(s.scaleAtRest)+",\"offset\":"+V(s.scaleOffset)+",\"axis\":"+(int)s.scalingAxis;
        if(c is ParentConstraint pa)extra=",\"rest\":"+V(pa.translationAtRest)+",\"rrest\":"+V(pa.rotationAtRest)+",\"offsets\":["+string.Join(",",pa.translationOffsets.Select(V))+"],\"roffsets\":["+string.Join(",",pa.rotationOffsets.Select(V))+"],\"axis\":"+(int)pa.translationAxis+",\"raxis\":"+(int)pa.rotationAxis;
        if(c is AimConstraint ai)extra=",\"rest\":"+V(ai.rotationAtRest)+",\"offset\":"+V(ai.rotationOffset)+",\"axis\":"+(int)ai.rotationAxis+",\"aim\":"+V(ai.aimVector)+",\"up\":"+V(ai.upVector)+",\"worldup\":"+V(ai.worldUpVector)+",\"worldup_type\":"+(int)ai.worldUpType+",\"worldup_object\":"+S(ai.worldUpObject?ai.worldUpObject.name:"");
        if(c is LookAtConstraint l)extra=",\"rest\":"+V(l.rotationAtRest)+",\"offset\":"+V(l.rotationOffset)+",\"roll\":"+F(l.roll)+",\"useup\":"+B(l.useUpObject)+",\"worldup_object\":"+S(l.worldUpObject?l.worldUpObject.name:"");
        var list=new List<ConstraintSource>();c.GetSources(list);
        return "{\"weight\":"+F(c.weight)+",\"active\":"+B(c.constraintActive)+",\"locked\":"+B(c.locked)+",\"count\":"+c.sourceCount+",\"sources\":["+string.Join(",",list.Select(s=>"["+S(s.sourceTransform?s.sourceTransform.name:"")+","+F(s.weight)+"]"))+ "]"+extra+"}";
    }
    static string Pose(Transform t)=>"{\"p\":"+V(t.localPosition)+",\"r\":"+Q(t.localRotation)+",\"s\":"+V(t.localScale)+"}";
    class Case
    {
        public string key;public GameObject parent;public Transform target,a,b;public IConstraint c;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Run()=>new GameObject("Probe").AddComponent<ConstraintProbe>();
    IEnumerator Start()
    {
        for(int kind=0;kind<Types.Length;kind++)
        {
            var go=new GameObject("API");var c=(IConstraint)go.AddComponent(Types[kind]);string k="api."+kind;
            Value(k+".defaults",Properties(c));
            c.weight=-2;Value(k+".negative_weight",F(c.weight));c.weight=2;Value(k+".high_weight",F(c.weight));
            Try(k+".get_negative",()=>c.GetSource(-1));Try(k+".get_empty",()=>c.GetSource(0));Try(k+".remove_empty",()=>c.RemoveSource(0));
            Try(k+".set_empty",()=>c.SetSource(0,default));Try(k+".get_null",()=>c.GetSources(null));Try(k+".set_null",()=>c.SetSources(null));
            c.AddSource(new ConstraintSource{sourceTransform=go.transform,weight=-2});c.AddSource(new ConstraintSource{weight=2});
            Value(k+".after_add",Properties(c));
            Try(k+".get_negative_nonempty",()=>c.GetSource(-1));Try(k+".get_past_nonempty",()=>c.GetSource(2));
            Try(k+".set_past_nonempty",()=>c.SetSource(2,default));Try(k+".remove_past_nonempty",()=>c.RemoveSource(2));
            var l=new List<ConstraintSource>{default};c.GetSources(l);Value(k+".list_count",l.Count.ToString());
            l[0]=default;Value(k+".list_copy",F(c.GetSource(0).weight));
            c.SetSources(new List<ConstraintSource>{new ConstraintSource{weight=.3f}});Value(k+".after_set",Properties(c));
            if(c is ParentConstraint p)
            {
                Try(k+".offset_negative",()=>p.GetTranslationOffset(-1));Try(k+".offset_past",()=>p.SetRotationOffset(1,Vector3.one));
                Try(k+".offsets_null",()=>p.translationOffsets=null);Try(k+".offsets_empty",()=>p.rotationOffsets=new Vector3[0]);
                Try(k+".offsets_extra",()=>p.translationOffsets=new Vector3[2]);
                p.translationOffsets=new[]{new Vector3(2,3,4)};var copy=p.translationOffsets;copy[0]=Vector3.zero;
                Value(k+".offset_copy",V(p.GetTranslationOffset(0)));
                p.AddSource(default);Value(k+".offset_after_add",Properties(c));p.RemoveSource(0);Value(k+".offset_after_remove",Properties(c));
            }
            DestroyImmediate(go);
        }
        var cases=new List<Case>();
        for(int kind=0;kind<Types.Length;kind++)
        for(int parented=0;parented<3;parented++)
        for(int sources=0;sources<6;sources++)
        foreach(float weight in new[]{0f,.35f,1f})
        {
            var parent=new GameObject("Parent");
            if(parented>0){parent.transform.position=new Vector3(2,-1,3);parent.transform.rotation=Quaternion.Euler(20,30,10);parent.transform.localScale=new Vector3(parented==2?-2:2,3,1.5f);}
            var target=new GameObject("Target").transform;target.SetParent(parent.transform,false);Reset(target);
            var a=new GameObject("A").transform;var b=new GameObject("B").transform;
            a.position=new Vector3(4,5,6);a.rotation=Quaternion.Euler(20,70,40);a.localScale=new Vector3(2,3,4);
            b.position=new Vector3(-2,3,1);b.rotation=Quaternion.Euler(-30,10,100);b.localScale=new Vector3(5,2,1);
            var c=(IConstraint)target.gameObject.AddComponent(Types[kind]);
            Configure(c,sources%3==0?7:sources%3==1?5:0,weight!=0);
            float[,] weights={{1,0},{.25f,.5f},{2,1},{-.5f,1},{0,0},{.25f,.5f}};
            c.AddSource(new ConstraintSource{sourceTransform=a,weight=weights[sources,0]});
            c.AddSource(new ConstraintSource{sourceTransform=sources==5?null:b,weight=weights[sources,1]});
            if(c is ParentConstraint pa){pa.SetTranslationOffset(0,new Vector3(.2f,.4f,.6f));pa.SetTranslationOffset(1,new Vector3(-.3f,.2f,.5f));pa.SetRotationOffset(0,new Vector3(10,20,30));pa.SetRotationOffset(1,new Vector3(0,-20,10));}
            c.weight=weight;c.locked=true;c.constraintActive=true;
            cases.Add(new Case{key="solve."+kind+"."+parented+"."+sources+"."+F(weight),parent=parent,target=target,a=a,b=b,c=c});
        }
        foreach(float value in new[]{0f,.3f,.5f,1f,-1f,2f})
        foreach(float sourceWeight in new[]{.25f,1f,2f})
        {
            var parent=new GameObject("Scale diagnostic"); var target=new GameObject("Scale target").transform;target.SetParent(parent.transform,false);
            var a=new GameObject("A").transform;a.localScale=new Vector3(2,3,4);
            var b=new GameObject("B").transform;
            var c=target.gameObject.AddComponent<ScaleConstraint>();c.scaleAtRest=new Vector3(1,2,3);c.scaleOffset=Vector3.one*value;
            c.AddSource(new ConstraintSource{sourceTransform=a,weight=sourceWeight});c.constraintActive=true;
            cases.Add(new Case{key="scale.diagnostic."+F(value)+"."+F(sourceWeight),parent=parent,target=target,a=a,b=b,c=c});
        }
        for(int i=1;i<=32;i++)
        {
            var parent=new GameObject("Scale exp");var target=new GameObject("Scale target").transform;target.SetParent(parent.transform,false);
            var a=new GameObject("A").transform;var b=new GameObject("B").transform;
            var c=target.gameObject.AddComponent<ScaleConstraint>();c.scaleOffset=new Vector3(2,3,4);
            c.AddSource(new ConstraintSource{sourceTransform=a,weight=32f/i});c.constraintActive=true;
            cases.Add(new Case{key="scale.exp."+i,parent=parent,target=target,a=a,b=b,c=c});
        }
        for(int i=0;i<=32;i++)
        {
            var parent=new GameObject("Scale log");var target=new GameObject("Scale target").transform;target.SetParent(parent.transform,false);
            var a=new GameObject("A").transform;var b=new GameObject("B").transform;
            var c=target.gameObject.AddComponent<ScaleConstraint>();float v=1f+i/32f;c.scaleOffset=new Vector3(v,-v,v*.125f);
            c.AddSource(new ConstraintSource{sourceTransform=a,weight=1f});c.constraintActive=true;
            cases.Add(new Case{key="scale.log."+i,parent=parent,target=target,a=a,b=b,c=c});
        }
        foreach(var x in cases)Value(x.key+".properties",Properties(x.c));
        var prefab=Resources.Load<GameObject>("Constraints");var loaded=Instantiate(prefab);loaded.name="Loaded";
        var cloned=Instantiate(loaded);cloned.name="Cloned";
        foreach(var g in new[]{loaded,cloned})foreach(var c in g.GetComponentsInChildren<Component>())if(c is IConstraint constraint)Value("serialized."+g.name+"."+c.name+".properties",Properties(constraint));
        for(int step=0;step<8;step++)
        {
            if(step==2)foreach(var x in cases)Reset(x.target);
            if(step==3)foreach(var x in cases){x.a.position+=new Vector3(1,2,-3);x.a.rotation=Quaternion.Euler(50,-20,10);x.a.localScale=new Vector3(3,1,2);}
            if(step==4)foreach(var x in cases)x.c.locked=false;
            if(step==5)foreach(var x in cases){x.c.constraintActive=false;Reset(x.target);}
            if(step==6)foreach(var x in cases){x.c.constraintActive=true;((Behaviour)x.c).enabled=false;}
            if(step==7)foreach(var x in cases)((Behaviour)x.c).enabled=true;
            yield return null;
            foreach(var x in cases)Value(x.key+".step"+step,Pose(x.target));
            foreach(var g in new[]{loaded,cloned})foreach(var c in g.GetComponentsInChildren<Component>())if(c is IConstraint)Value("serialized."+g.name+"."+c.name+".step"+step,Pose(c.transform));
        }
        foreach(var x in cases){DestroyImmediate(x.parent);DestroyImmediate(x.a.gameObject);DestroyImmediate(x.b.gameObject);}
        string path=Environment.GetEnvironmentVariable("CONSTRAINT_PROBE_OUT")??"/tmp/constraints";Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Reset(Transform t){t.localPosition=new Vector3(7,8,9);t.localRotation=Quaternion.Euler(15,25,35);t.localScale=new Vector3(1.1f,1.2f,1.3f);}
}
