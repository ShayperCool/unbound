using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;

public partial class ConstraintProbe
{
    void AddAimCases(List<Case> cases)
    {
        for(int kind=4;kind<6;kind++)
        for(int parented=0;parented<3;parented++)
        for(int mode=0;mode<(kind==4?5:2);mode++)
        for(int variant=0;variant<3;variant++)
        foreach(bool upObject in new[]{false,true})
        foreach(float weight in new[]{.35f,1f})
        {
            var parent=new GameObject("Parent");
            if(parented>0){parent.transform.position=new Vector3(2,-1,3);parent.transform.rotation=Quaternion.Euler(20,30,10);parent.transform.localScale=new Vector3(parented==2?-2:2,3,1.5f);}
            var target=new GameObject("Target").transform;target.SetParent(parent.transform,false);Reset(target);
            var a=new GameObject("A").transform;var b=new GameObject("B").transform;a.position=new Vector3(4,5,6);b.position=new Vector3(-2,3,1);
            var up=new GameObject("Up").transform;up.SetParent(parent.transform,false);up.localPosition=new Vector3(3,4,1);up.localRotation=Quaternion.Euler(40,10,30);
            var c=(IConstraint)target.gameObject.AddComponent(Types[kind]);Configure(c,7,true);c.AddSource(new ConstraintSource{sourceTransform=a,weight=1f});
            if(c is AimConstraint aim)
            {
                aim.worldUpType=(AimConstraint.WorldUpType)mode;aim.worldUpVector=new Vector3(1,2,3);aim.worldUpObject=upObject?up:null;
                if(variant==1){aim.aimVector=Vector3.right;aim.upVector=Vector3.forward;}
                if(variant==2){aim.aimVector=new Vector3(1,2,3);aim.upVector=new Vector3(2,1,-1);}
            }
            else if(c is LookAtConstraint look){look.useUpObject=mode==1;look.worldUpObject=upObject?up:null;look.roll=(variant-1)*30f;}
            c.weight=weight;c.constraintActive=true;
            cases.Add(new Case{key="aim."+kind+"."+parented+"."+mode+"."+variant+"."+B(upObject)+"."+F(weight),parent=parent,target=target,a=a,b=b,c=c});
        }
    }

    void AddSourceCases(List<Case> cases)
    {
        for(int kind=0;kind<6;kind++)
        for(int parented=0;parented<3;parented++)
        {
            var root=new GameObject("Source hierarchy");
            var parent=new GameObject("Source parent").transform;parent.SetParent(root.transform,false);
            parent.localPosition=new Vector3(2,-1,3);parent.localRotation=Quaternion.Euler(20,30,10);parent.localScale=new Vector3(parented==0?2:-2,3,1.5f);
            if(parented==2)
            {
                var grandparent=new GameObject("Grandparent").transform;grandparent.SetParent(root.transform,false);
                grandparent.localRotation=Quaternion.Euler(10,40,20);grandparent.localScale=new Vector3(2,1,3);parent.SetParent(grandparent,false);
            }
            var a=new GameObject("A").transform;a.SetParent(parent,false);a.localPosition=new Vector3(4,5,6);a.localRotation=Quaternion.Euler(20,70,40);a.localScale=new Vector3(2,3,4);
            var b=new GameObject("B").transform;b.SetParent(root.transform,false);b.position=new Vector3(-2,3,1);b.rotation=Quaternion.Euler(-30,10,100);b.localScale=new Vector3(5,2,1);
            var t=new GameObject("Target").transform;t.SetParent(root.transform,false);Reset(t);
            var c=(IConstraint)t.gameObject.AddComponent(Types[kind]);Configure(c,7,true);c.weight=.65f;
            c.AddSource(new ConstraintSource{sourceTransform=a,weight=.25f});c.AddSource(new ConstraintSource{sourceTransform=b,weight=.5f});
            if(c is ParentConstraint pa){pa.SetTranslationOffset(0,new Vector3(.2f,.4f,.6f));pa.SetRotationOffset(0,new Vector3(10,20,30));}
            c.constraintActive=true;cases.Add(new Case{key="source."+kind+"."+parented,parent=root,target=t,a=a,b=b,c=c});
        }
    }

    IEnumerator ActivationCases()
    {
        var cases=new List<Case>();
        for(int kind=0;kind<Types.Length;kind++)
        for(int parented=0;parented<3;parented++)
        for(int action=0;action<3;action++)
        {
            var parent=new GameObject("Parent");
            if(parented>0){parent.transform.position=new Vector3(2,-1,3);parent.transform.rotation=Quaternion.Euler(20,30,10);parent.transform.localScale=new Vector3(parented==2?-2:2,3,1.5f);}
            var t=new GameObject("Target").transform;t.SetParent(parent.transform,false);Reset(t);
            var a=new GameObject("A").transform;var b=new GameObject("B").transform;
            a.position=new Vector3(4,5,6);a.rotation=Quaternion.Euler(20,70,40);a.localScale=new Vector3(2,3,4);
            b.position=new Vector3(-2,3,1);b.rotation=Quaternion.Euler(-30,10,100);b.localScale=new Vector3(5,2,1);
            var c=(IConstraint)t.gameObject.AddComponent(Types[kind]);var k="activate."+kind+"."+parented+"."+action;
            Value(k+".initial",Properties(c));
            Configure(c,7,true);c.weight=.65f;c.locked=false;
            c.AddSource(new ConstraintSource{sourceTransform=a,weight=.5f});c.AddSource(new ConstraintSource{sourceTransform=b,weight=.25f});
            var iface=typeof(IConstraint).Assembly.GetType("UnityEngine.Animations.IConstraintInternal");
            var method=new[]{"ActivateAndPreserveOffset","ActivateWithZeroOffset","UserUpdateOffset"}[action];
            Try(k+".call",()=>iface.GetMethod(method).Invoke(c,null));
            Value(k+".properties",Properties(c));Value(k+".immediate",Pose(t));
            cases.Add(new Case{key=k,parent=parent,target=t,a=a,b=b,c=c});
        }
        for(int frame=0;frame<3;frame++)
        {
            if(frame==1)foreach(var x in cases)x.c.weight=1f;
            if(frame==2)foreach(var x in cases){x.a.position+=new Vector3(1,2,-3);x.a.rotation=Quaternion.Euler(50,-20,10);}
            yield return null;
            foreach(var x in cases){Value(x.key+".step"+frame,Pose(x.target));Value(x.key+".properties"+frame,Properties(x.c));}
        }
        foreach(var x in cases){DestroyImmediate(x.parent);DestroyImmediate(x.a.gameObject);DestroyImmediate(x.b.gameObject);}
    }

    IEnumerator DependencyCases()
    {
        var root=new GameObject("Dependency");var source=new GameObject("Source").transform;source.SetParent(root.transform,false);
        var a=new GameObject("A").transform;a.SetParent(root.transform,false);
        var b=new GameObject("B").transform;b.SetParent(root.transform,false);
        var ca=a.gameObject.AddComponent<PositionConstraint>();ca.AddSource(new ConstraintSource{sourceTransform=b,weight=1});ca.constraintActive=true;
        var cb=b.gameObject.AddComponent<PositionConstraint>();cb.AddSource(new ConstraintSource{sourceTransform=source,weight=1});cb.constraintActive=true;
        var driver=root.AddComponent<ConstraintPhaseObserver>();
        driver.Step=frame=>{source.position=new Vector3(frame,2*frame,3*frame);if(frame<=2)Value("phase."+frame+".update","["+V(a.position)+","+V(b.position)+"]");};
        driver.Late=frame=>{if(frame<=2)Value("phase."+frame+".late","["+V(a.position)+","+V(b.position)+"]");};
        while(driver.Frame<3)yield return null;
        DestroyImmediate(root);
    }
}
