using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;

public partial class ForcesProbe
{
    class ForcePhaseMarker { }
    int phaseFrame;
    bool phaseActive;
    Rigidbody phaseThree;
    Rigidbody2D phaseTwo;
    void RecordPhase(string name,bool before)
    {
        if(!phaseActive)return;
        if(name=="ScriptRunBehaviourFixedUpdate"&&before)phaseFrame++;
        if(phaseFrame>2)return;
        string key="phase."+phaseFrame+"."+name+"."+B(before);
        Value(key+".3",V(phaseThree.GetAccumulatedForce()));
        phaseTwo.linearVelocity=Vector2.zero;Physics2D.Simulate(.02f);Value(key+".2",V2(phaseTwo.linearVelocity));
    }
    PlayerLoopSystem Instrument(PlayerLoopSystem node)
    {
        if(node.subSystemList==null)return node;
        var list=new List<PlayerLoopSystem>();
        foreach(var child in node.subSystemList)
        {
            var wrapped=Instrument(child);string name=child.type?.Name;
            bool target=child.type==typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate)
                ||child.type==typeof(UnityEngine.PlayerLoop.FixedUpdate.PhysicsFixedUpdate)
                ||child.type==typeof(UnityEngine.PlayerLoop.FixedUpdate.Physics2DFixedUpdate);
            if(target)list.Add(new PlayerLoopSystem{type=typeof(ForcePhaseMarker),updateDelegate=()=>RecordPhase(name,true)});
            list.Add(wrapped);
            if(target)list.Add(new PlayerLoopSystem{type=typeof(ForcePhaseMarker),updateDelegate=()=>RecordPhase(name,false)});
        }
        node.subSystemList=list.ToArray();return node;
    }
    IEnumerator PhaseProbe()
    {
        Value("metadata.disallow3",B(typeof(ConstantForce).IsDefined(typeof(DisallowMultipleComponent),true)));
        Value("metadata.disallow2",B(typeof(ConstantForce2D).IsDefined(typeof(DisallowMultipleComponent),true)));
        Physics.simulationMode=SimulationMode.Script;Physics2D.simulationMode=SimulationMode2D.Script;
        var three=new GameObject("Phase3");var f=three.AddComponent<ConstantForce>();f.force=new Vector3(1,2,3);phaseThree=three.GetComponent<Rigidbody>();phaseThree.useGravity=false;
        var two=new GameObject("Phase2");var f2=two.AddComponent<ConstantForce2D>();f2.force=new Vector2(1,2);phaseTwo=two.GetComponent<Rigidbody2D>();phaseTwo.gravityScale=0;
        var saved=PlayerLoop.GetCurrentPlayerLoop();phaseActive=true;PlayerLoop.SetPlayerLoop(Instrument(saved));
        while(phaseFrame<2)yield return new WaitForFixedUpdate();
        phaseActive=false;PlayerLoop.SetPlayerLoop(saved);DestroyImmediate(three);DestroyImmediate(two);
    }
}
