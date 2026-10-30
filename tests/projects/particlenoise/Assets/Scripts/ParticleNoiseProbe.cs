using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class ParticleNoiseProbe : MonoBehaviour
{
    readonly StringBuilder output = new StringBuilder();
    static string F(float x) => float.IsNaN(x) ? "\"NaN\"" : float.IsInfinity(x) ? "\""+x+"\"" : x.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
    void Value(string k,string v) => output.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(v).Append("}\n");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Run() => new GameObject("Probe").AddComponent<ParticleNoiseProbe>();
    void Start()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.position=new Vector3(0,0,-10);
        for(int quality=0;quality<3;quality++)
        for(int variant=0;variant<41;variant++) Case(camera,quality,variant);
        var path=Environment.GetEnvironmentVariable("PARTICLE_NOISE_OUT")??"/tmp/particle-noise";
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    void Case(Camera camera,int quality,int variant)
    {
        string key="q"+quality+".v"+variant;
        var go=new GameObject(key); var ps=go.AddComponent<ParticleSystem>(); ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed=false; ps.randomSeed=variant==1?1u:variant==2?0xabcdef01u:12345u;
        var main=ps.main;main.playOnAwake=false;main.duration=1;main.startSpeed=0;main.startLifetime=4;main.maxParticles=8;
        main.startSize3D=variant>=19;main.startRotation3D=variant>=19;
        var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
        var noise=ps.noise;noise.enabled=true;noise.quality=(ParticleSystemNoiseQuality)quality;noise.strength=1;noise.frequency=.5f;noise.damping=true;
        noise.positionAmount=1;noise.rotationAmount=0;noise.sizeAmount=0;
        if(variant==3)noise.frequency=.25f;
        if(variant==4)noise.frequency=2;
        if(variant==5)noise.damping=false;
        if(variant>=6&&variant<=9){noise.octaveCount=variant<8?2:4;noise.octaveMultiplier=variant%2==0?.25f:.7f;noise.octaveScale=variant%2==0?2:3;}
        if(variant>=10&&variant<=14){noise.remapEnabled=true;noise.positionAmount=0;noise.sizeAmount=1;noise.rotationAmount=360;}
        if(variant==11)noise.remap=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,-.8f,0,0),new Keyframe(.6f,.4f,2,2),new Keyframe(1,.9f,0,0)));
        if(variant==12)noise.remap=new ParticleSystem.MinMaxCurve(-.7f,.3f);
        if(variant==13){noise.separateAxes=true;noise.remapX=new ParticleSystem.MinMaxCurve(-.5f,.4f);noise.remapY=.2f;noise.remapZ=-.3f;noise.strengthX=.6f;noise.strengthY=1.2f;noise.strengthZ=.8f;}
        if(variant==14)noise.remap=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,-.9f,1,-.3f),AnimationCurve.Linear(0,-.2f,1,.7f));
        if(variant==15)noise.scrollSpeed=.7f;
        if(variant==16||variant>=25)noise.scrollSpeed=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.2f,1,2));
        if(variant==17){noise.positionAmount=2;noise.rotationAmount=360;noise.sizeAmount=1;}
        if(variant==18||variant==19){noise.positionAmount=0;noise.rotationAmount=360;noise.sizeAmount=1;}
        if(variant==20||variant==21){noise.separateAxes=true;noise.strengthX=new ParticleSystem.MinMaxCurve(.2f,1.3f);noise.strengthY=new ParticleSystem.MinMaxCurve(.5f,2);noise.strengthZ=new ParticleSystem.MinMaxCurve(-.5f,.8f);}
        if(variant>=21&&variant<=23){noise.positionAmount=new ParticleSystem.MinMaxCurve(.3f,1.7f);noise.rotationAmount=new ParticleSystem.MinMaxCurve(40,400);noise.sizeAmount=new ParticleSystem.MinMaxCurve(.4f,1.4f);}
        if(variant==23){noise.strength=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.3f,1,2));var size=ps.sizeOverLifetime;size.enabled=true;size.separateAxes=true;size.x=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,.3f));size.y=.8f;size.z=1.2f;var speed=ps.sizeBySpeed;speed.enabled=true;speed.size=.7f;var rot=ps.rotationOverLifetime;rot.enabled=true;rot.separateAxes=true;rot.x=.2f;rot.y=.3f;rot.z=.4f;var vel=ps.velocityOverLifetime;vel.enabled=true;vel.x=.1f;vel.y=.2f;vel.z=.3f;}
        if(variant==24){noise.frequency=0;noise.damping=false;}
        if(variant>=28&&variant<=31){main.simulationSpace=(ParticleSystemSimulationSpace)(variant-28);if(variant==31)main.simulationSpace=ParticleSystemSimulationSpace.Local;go.transform.position=new Vector3(3,-2,1);go.transform.rotation=Quaternion.Euler(20,35,50);go.transform.localScale=new Vector3(-1.2f,.8f,1.7f);var custom=new GameObject("Custom");custom.transform.position=new Vector3(-2,1,3);custom.transform.rotation=Quaternion.Euler(10,60,20);main.customSimulationSpace=custom.transform;}
        if(variant==32||variant==33){noise.scrollSpeed=0;noise.strength=new ParticleSystem.MinMaxCurve(.2f,1.3f);}
        if(variant==34){noise.scrollSpeed=new ParticleSystem.MinMaxCurve(-.2f,.6f);noise.positionAmount=0;noise.sizeAmount=1;}
        if(variant==35){noise.scrollSpeed=0;noise.positionAmount=0;noise.sizeAmount=1;noise.rotationAmount=0;}
        if(variant==36){noise.scrollSpeed=0;noise.positionAmount=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.3f,1,1.4f));noise.sizeAmount=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.2f,1,1));noise.rotationAmount=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,20,1,200));}
        if(variant==37){noise.scrollSpeed=0;noise.positionAmount=0;noise.sizeAmount=10;}
        var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=new Material(Shader.Find("Particles/Standard Unlit"));renderer.minParticleSize=0;renderer.maxParticleSize=1;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.NoiseSumXYZ,ParticleSystemVertexStream.NoiseImpulseXYZ,ParticleSystemVertexStream.SizeXYZ});
        if(variant==38)renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV});
        ps.Simulate(0,true,true,false);
        var ep=new ParticleSystem.EmitParams{position=new Vector3(.125f,-.25f,.5f),velocity=variant==23?new Vector3(.2f,.1f,.3f):Vector3.zero,startLifetime=4,randomSeed=variant==2||variant==21||variant==33?654321u:123456u,startSize=2,rotation=15,angularVelocity=7};
        if(variant>=19){ep.startSize3D=new Vector3(1.5f,2,3);ep.rotation3D=new Vector3(10,20,30);ep.angularVelocity3D=new Vector3(3,5,7);}
        ps.Emit(ep,1);ps.Pause();Record(key+".birth",ps,renderer,camera);
        for(int step=0;step<6;step++)
        {
            if(variant==25&&step==3){ps.Simulate(0,true,true,false);ps.Emit(ep,1);WarmStreams(renderer,camera);}
            if(variant==26&&step==3){ps.Pause();ps.Play();ps.Pause();}
            if(variant==27&&step==3){noise.scrollSpeed=-.4f;}
            if(variant==38&&step==2||variant==39&&step==4){renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.NoiseSumXYZ,ParticleSystemVertexStream.NoiseImpulseXYZ,ParticleSystemVertexStream.SizeXYZ});WarmStreams(renderer,camera);}
            if(variant==39&&step==2){renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV});WarmStreams(renderer,camera);}
            if(variant==40&&step==3){var old=new ParticleSystem.Particle[1];ps.GetParticles(old);old[0].position=new Vector3(.125f,-.25f,.5f);ps.SetParticles(old,1);}
            ps.Simulate(step==4?.07f:.1f,true,false,false);Record(key+".s"+step,ps,renderer,camera);
        }
        for(int stream=0;stream<6;stream++)
        {
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,(ParticleSystemVertexStream)((int)ParticleSystemVertexStream.NoiseSumX+stream)});
            Record(key+".stream"+stream,ps,renderer,camera);
        }
        DestroyImmediate(renderer.sharedMaterial); if(main.customSimulationSpace!=null)DestroyImmediate(main.customSimulationSpace.gameObject);DestroyImmediate(go);
    }
    static void WarmStreams(ParticleSystemRenderer renderer,Camera camera)
    {var mesh=new Mesh();renderer.BakeMesh(mesh,camera,true);DestroyImmediate(mesh);}
    void Record(string key,ParticleSystem ps,ParticleSystemRenderer renderer,Camera camera)
    {
        var particles=new ParticleSystem.Particle[8];int count=ps.GetParticles(particles);Value(key+".count",count.ToString());Value(key+".time",F(ps.time));
        if(count==0)return;var p=particles[0];
        Value(key+".pos",V(p.position));Value(key+".vel",V(p.velocity));Value(key+".anim",V(p.animatedVelocity));Value(key+".total",V(p.totalVelocity));Value(key+".rot",V(p.rotation3D));Value(key+".angular",V(p.angularVelocity3D));Value(key+".size",V(p.GetCurrentSize3D(ps)));Value(key+".life",F(p.remainingLifetime));
        // Native optional impulse storage is uninitialized before the first simulation update.
        // Record the defined public birth state above, and bake only after simulation.
        if(key.EndsWith(".birth")){WarmStreams(renderer,camera);return;}
        var mesh=new Mesh();renderer.BakeMesh(mesh,camera,true);
        for(int channel=0;channel<3;channel++){var uv=new List<Vector4>();mesh.GetUVs(channel,uv);var v=uv.Count>0?uv[0]:Vector4.zero;Value(key+".uv"+channel,"["+F(v.x)+","+F(v.y)+","+F(v.z)+","+F(v.w)+"]");}
        DestroyImmediate(mesh);
    }
}
