using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public class ParticleStreamsProbe : MonoBehaviour
{
    readonly StringBuilder output=new StringBuilder();
    static string F(float f)=>float.IsNaN(f)?"\"NaN\"":float.IsPositiveInfinity(f)?"\"Infinity\"":float.IsNegativeInfinity(f)?"\"-Infinity\"":f.ToString("R",CultureInfo.InvariantCulture);
    static string S(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    static string V(Vector4 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+","+F(v.w)+"]";
    static string V3(Vector3 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
    static string A<T>(IEnumerable<T> values,Func<T,string> json)=>"["+string.Join(",",values.Select(json))+"]";
    void Value(string key,string value)=>output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Run()=>new GameObject("Probe").AddComponent<ParticleStreamsProbe>();
    IEnumerator Start()
    {
        Time.captureDeltaTime=.05f;
        var cameraObject=new GameObject("Camera");var camera=cameraObject.AddComponent<Camera>();cameraObject.tag="MainCamera";
        camera.transform.position=new Vector3(0,0,-10);camera.orthographic=true;camera.orthographicSize=4;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
        var go=new GameObject("System");var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=8;main.startSpeed=0;main.startSize3D=true;
        var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
        var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=new Material(Shader.Find("Particles/Standard Unlit"));renderer.minParticleSize=0;renderer.maxParticleSize=1;
        var particle=new ParticleSystem.Particle{position=new Vector3(.25f,.5f,0),velocity=new Vector3(.5f,1,2),startLifetime=4,remainingLifetime=3,startSize3D=new Vector3(.5f,.7f,.9f),rotation3D=new Vector3(10,20,30),angularVelocity3D=new Vector3(3,7,11),startColor=new Color32(51,102,153,204),randomSeed=0x12345678,axisOfRotation=new Vector3(.2f,.5f,.3f)};
        ps.SetParticles(new[]{particle},1);ps.Pause();
        ps.SetCustomParticleData(new List<Vector4>{new Vector4(.1f,.2f,.3f,.4f)},ParticleSystemCustomData.Custom1);
        ps.SetCustomParticleData(new List<Vector4>{new Vector4(2,3,4,5)},ParticleSystemCustomData.Custom2);
        yield return null;
        Record("default",renderer,camera);
        var basic=new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV};
        SetAndRecord("position",new[]{ParticleSystemVertexStream.Position},renderer,camera);
        SetAndRecord("tangent",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Tangent,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV},renderer,camera);
        foreach(var stream in (ParticleSystemVertexStream[])Enum.GetValues(typeof(ParticleSystemVertexStream)))
        {
            if((int)stream<5||(int)stream>48)continue;
            SetAndRecord("stream."+stream,basic.Concat(new[]{stream}).ToArray(),renderer,camera);
        }
        SetAndRecord("packed",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.AnimBlend,ParticleSystemVertexStream.AgePercent,ParticleSystemVertexStream.Custom1XYZW,ParticleSystemVertexStream.Custom2XY,ParticleSystemVertexStream.Velocity,ParticleSystemVertexStream.Speed},renderer,camera);
        SetAndRecord("packed_small",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Custom1XYZ,ParticleSystemVertexStream.AgePercent,ParticleSystemVertexStream.Custom2X,ParticleSystemVertexStream.InvStartLifetime},renderer,camera);
        var sheet=ps.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=2;sheet.numTilesY=2;sheet.frameOverTime=.35f;sheet.cycleCount=1;
        SetAndRecord("sheet",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.UV2,ParticleSystemVertexStream.AnimBlend,ParticleSystemVertexStream.AnimFrame},renderer,camera);
        sheet.enabled=false;
        var sourceMesh=new Mesh();sourceMesh.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(-.5f,.5f,0),new Vector3(.5f,.5f,0),new Vector3(.5f,-.5f,0)};
        sourceMesh.normals=Enumerable.Repeat(Vector3.back,4).ToArray();sourceMesh.tangents=Enumerable.Repeat(new Vector4(1,0,0,-1),4).ToArray();sourceMesh.uv=new[]{new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0)};
        sourceMesh.uv2=new[]{new Vector2(.1f,.2f),new Vector2(.3f,.4f),new Vector2(.5f,.6f),new Vector2(.7f,.8f)};sourceMesh.triangles=new[]{0,1,2,2,3,0};sourceMesh.RecalculateBounds();
        renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=sourceMesh;
        foreach(var stream in new[]{ParticleSystemVertexStream.Custom1XYZW,ParticleSystemVertexStream.Center,ParticleSystemVertexStream.MeshAxisOfRotation,ParticleSystemVertexStream.VertexID,ParticleSystemVertexStream.UV2,ParticleSystemVertexStream.ParticleIndex})
            SetAndRecord("mesh."+stream,basic.Concat(new[]{ParticleSystemVertexStream.Tangent,stream}).ToArray(),renderer,camera);
        renderer.renderMode=ParticleSystemRenderMode.Billboard;
        for(int n=0;n<6;n++)
        {
            var p=particle;if(n==0||n==1)p.rotation3D=Vector3.zero;if(n==2||n==3)p.startSize3D=Vector3.one;
            renderer.normalDirection=n==1||n==3?0:n==4?.5f:1;
            if(n==5)p.axisOfRotation=Vector3.forward;
            ps.SetParticles(new[]{p},1);
            SetAndRecord("normal."+n,new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Tangent,ParticleSystemVertexStream.UV},renderer,camera);
        }
        renderer.normalDirection=1;
        foreach(uint seed in new uint[]{0,1,2,3,4,5,6,7,8,9,15,16,31,32,127,128,255,256,65535,65536,0x12345678,0x89abcdef,0xffffffff})
        {
            var p=particle;p.randomSeed=seed;ps.SetParticles(new[]{p},1);
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.StableRandomXYZW,ParticleSystemVertexStream.VaryingRandomXYZW});
            var mesh=new Mesh();renderer.BakeMesh(mesh,camera,ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale);
            var a=new List<Vector4>();var b=new List<Vector4>();mesh.GetUVs(0,a);mesh.GetUVs(1,b);Value("random."+seed,"{\"stable\":"+V(a[0])+",\"varying\":"+V(b[0])+"}");DestroyImmediate(mesh);
        }
        var path=Environment.GetEnvironmentVariable("PARTICLE_STREAMS_OUT")??"/tmp/particle-streams";Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    void SetAndRecord(string key,ParticleSystemVertexStream[] streams,ParticleSystemRenderer renderer,Camera camera)
    {renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>(streams));Record(key,renderer,camera);}
    void Record(string key,ParticleSystemRenderer renderer,Camera camera)
    {
        var streams=new List<ParticleSystemVertexStream>();renderer.GetActiveVertexStreams(streams);
        Value(key+".streams",A(streams,s=>((int)s).ToString(CultureInfo.InvariantCulture)));
        var mesh=new Mesh();renderer.BakeMesh(mesh,camera,ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale);
        Value(key+".attributes",A(mesh.GetVertexAttributes(),a=>"{\"attribute\":"+S(a.attribute.ToString())+",\"format\":"+S(a.format.ToString())+",\"dimension\":"+a.dimension+"}"));
        Value(key+".vertices",A(mesh.vertices,V3));Value(key+".normals",A(mesh.normals,V3));Value(key+".tangents",A(mesh.tangents,V));Value(key+".colors",A(mesh.colors32,c=>"["+c.r+","+c.g+","+c.b+","+c.a+"]"));
        for(int channel=0;channel<8;channel++){var values=new List<Vector4>();mesh.GetUVs(channel,values);Value(key+".uv"+channel,A(values,V));}
        Value(key+".indices",A(mesh.triangles,i=>i.ToString(CultureInfo.InvariantCulture)));DestroyImmediate(mesh);
    }
}
