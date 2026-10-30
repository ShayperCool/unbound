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
        ps.SetParticles(new[]{particle},1);
        SetAndRecord("no_uv.normal",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal},renderer,camera);
        SetAndRecord("no_uv.color",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color},renderer,camera);
        SetAndRecord("no_uv.tangent",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Tangent},renderer,camera);
        SetAndRecord("uv_only",new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV},renderer,camera);
        var linearBytes=new List<string>();
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV});
        for(int c=0;c<256;c++)
        {
            var p=particle;p.startColor=new Color32((byte)c,(byte)c,(byte)c,(byte)c);ps.SetParticles(new[]{p},1);var mesh=new Mesh();renderer.BakeMesh(mesh,camera,ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale);
            var color=mesh.colors32[0];linearBytes.Add("["+color.r+","+color.g+","+color.b+","+color.a+"]");DestroyImmediate(mesh);
        }
        Value("linear_byte_table","["+string.Join(",",linearBytes)+"]");
        for(int n=0;n<5;n++)
        {
            var p=particle;p.startLifetime=n==4?7:4;p.remainingLifetime=n==0?4:n==1?2:n==2?1:n==3?3.2f:5.3f;ps.SetParticles(new[]{p},1);
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.AgePercent,ParticleSystemVertexStream.VaryingRandomXYZW});
            var mesh=new Mesh();renderer.BakeMesh(mesh,camera,ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale);var a=new List<Vector4>();var b=new List<Vector4>();mesh.GetUVs(0,a);mesh.GetUVs(1,b);Value("age_random."+n,"["+V(a[0])+","+V(b[0])+"]");DestroyImmediate(mesh);
        }
        var loaded=Instantiate(Resources.Load<GameObject>("Streams"));var cloned=Instantiate(loaded);
        foreach(var item in new[]{loaded,cloned}){var r=item.GetComponent<ParticleSystemRenderer>();var a=new List<ParticleSystemVertexStream>();var b=new List<ParticleSystemVertexStream>();r.GetActiveVertexStreams(a);r.GetActiveTrailVertexStreams(b);Value(item==loaded?"prefab.loaded":"prefab.cloned","{\"main\":"+A(a,x=>((int)x).ToString())+",\"trail\":"+A(b,x=>((int)x).ToString())+"}");}DestroyImmediate(loaded);DestroyImmediate(cloned);
        ps.SetParticles(new[]{particle},1);renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.Custom1XYZW});
        var reused=new Mesh();reused.SetVertexBufferParams(1,new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,1));
        Record("reused.custom",renderer,camera,ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale,reused);
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Tangent,ParticleSystemVertexStream.UV});Record("reused.tangent",renderer,camera,ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale,reused);DestroyImmediate(reused);
        var other=particle;other.position=new Vector3(1,0,1);other.velocity=-particle.velocity;other.remainingLifetime=1;other.randomSeed=4321;particle.position=new Vector3(-1,0,0);
        ps.SetParticles(new[]{particle,other},2);ps.SetCustomParticleData(new List<Vector4>{new Vector4(.1f,.2f,.3f,.4f),new Vector4(.6f,.7f,.8f,.9f)},ParticleSystemCustomData.Custom1);
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.Custom1XY,ParticleSystemVertexStream.ParticleIndex,ParticleSystemVertexStream.StableRandomXYZW,ParticleSystemVertexStream.AgePercent,ParticleSystemVertexStream.Center,ParticleSystemVertexStream.Velocity});
        foreach(var mode in new[]{ParticleSystemSortMode.None,ParticleSystemSortMode.Distance,ParticleSystemSortMode.OldestInFront,ParticleSystemSortMode.YoungestInFront}){renderer.sortMode=mode;Record("multiple."+mode,renderer,camera);}renderer.sortMode=ParticleSystemSortMode.None;
        particle.position=new Vector3(.25f,.5f,0);var custom=new GameObject("CustomSpace");custom.transform.position=new Vector3(-3,.5f,1);custom.transform.rotation=Quaternion.Euler(0,0,30);custom.transform.localScale=new Vector3(.75f,2,.5f);
        go.transform.position=new Vector3(2,-1,3);go.transform.rotation=Quaternion.Euler(0,0,20);go.transform.localScale=new Vector3(-2,3,.5f);
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Tangent,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.Center,ParticleSystemVertexStream.Velocity,ParticleSystemVertexStream.SizeXYZ,ParticleSystemVertexStream.Rotation3D});
        foreach(var space in new[]{ParticleSystemSimulationSpace.World,ParticleSystemSimulationSpace.Local,ParticleSystemSimulationSpace.Custom})
            foreach(var scaling in new[]{ParticleSystemScalingMode.Hierarchy,ParticleSystemScalingMode.Shape})
            {
                main.simulationSpace=space;main.customSimulationSpace=custom.transform;main.scalingMode=scaling;ps.SetParticles(new[]{particle},1);
                for(int flags=0;flags<4;flags++)Record("transform."+space+"."+scaling+"."+flags,renderer,camera,(ParticleSystemBakeMeshOptions)flags);
            }
        main.simulationSpace=ParticleSystemSimulationSpace.World;main.scalingMode=ParticleSystemScalingMode.Hierarchy;go.transform.position=Vector3.zero;go.transform.rotation=Quaternion.identity;go.transform.localScale=Vector3.one;DestroyImmediate(custom);
        TrailCases(camera);
        var imageParticle=particle;imageParticle.position=Vector3.zero;imageParticle.rotation3D=Vector3.zero;imageParticle.startSize3D=new Vector3(4,4,1);ps.SetParticles(new[]{imageParticle},1);
        ps.SetCustomParticleData(new List<Vector4>{new Vector4(.1f,.2f,.3f,.4f)},ParticleSystemCustomData.Custom1);ps.SetCustomParticleData(new List<Vector4>{new Vector4(.5f,.6f,.7f,.8f)},ParticleSystemCustomData.Custom2);
        renderer.sharedMaterial=new Material(Resources.Load<Shader>("VertexStreams"));renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.Custom1XYZW,ParticleSystemVertexStream.Custom2XYZW});
        var target=new RenderTexture(64,64,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();RenderTexture.active=target;var image=new Texture2D(64,64,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();RenderTexture.active=null;camera.targetTexture=null;
        var imageDir=Environment.GetEnvironmentVariable("PARTICLE_STREAMS_OUT")??"/tmp/particle-streams";Directory.CreateDirectory(imageDir);File.WriteAllBytes(Path.Combine(imageDir,"custom_streams.png"),image.EncodeToPNG());
        var pixel=(Color32)image.GetPixel(32,32);Value("custom_streams_pixel","["+pixel.r+","+pixel.g+","+pixel.b+","+pixel.a+"]");
        var path=Environment.GetEnvironmentVariable("PARTICLE_STREAMS_OUT")??"/tmp/particle-streams";Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    void SetAndRecord(string key,ParticleSystemVertexStream[] streams,ParticleSystemRenderer renderer,Camera camera)
    {renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>(streams));Record(key,renderer,camera);}
    void Record(string key,ParticleSystemRenderer renderer,Camera camera,ParticleSystemBakeMeshOptions options=ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale,Mesh destination=null)
    {
        var streams=new List<ParticleSystemVertexStream>();renderer.GetActiveVertexStreams(streams);
        Value(key+".streams",A(streams,s=>((int)s).ToString(CultureInfo.InvariantCulture)));
        var mesh=destination??new Mesh();renderer.BakeMesh(mesh,camera,options);
        DescribeMesh(key,streams,mesh);
        if(destination==null)DestroyImmediate(mesh);
    }
    void DescribeMesh(string key,List<ParticleSystemVertexStream> streams,Mesh mesh)
    {
        Value(key+".attributes",A(mesh.GetVertexAttributes(),a=>"{\"attribute\":"+S(a.attribute.ToString())+",\"format\":"+S(a.format.ToString())+",\"dimension\":"+a.dimension+"}"));
        // Unity leaves uninitialized BakeMesh buffers when no packed UV attribute exists.
        // Only the declared layout and stream API are stable for those inputs.
        if(streams.All(stream=>(int)stream<4))return;
        Value(key+".vertices",A(mesh.vertices,V3));Value(key+".normals",A(mesh.normals,V3));Value(key+".tangents",A(mesh.tangents,V));Value(key+".colors",A(mesh.colors32,c=>"["+c.r+","+c.g+","+c.b+","+c.a+"]"));
        for(int channel=0;channel<8;channel++){var values=new List<Vector4>();mesh.GetUVs(channel,values);Value(key+".uv"+channel,A(values,V));}
        Value(key+".indices",A(mesh.triangles,i=>i.ToString(CultureInfo.InvariantCulture)));
    }
    void TrailCases(Camera camera)
    {
        var extra=new[]{ParticleSystemVertexStream.Position,ParticleSystemVertexStream.Normal,ParticleSystemVertexStream.Tangent,ParticleSystemVertexStream.Color,ParticleSystemVertexStream.UV,ParticleSystemVertexStream.NextTrailCenter,ParticleSystemVertexStream.PreviousTrailCenter,ParticleSystemVertexStream.PercentageAlongTrail,ParticleSystemVertexStream.TrailWidth};
        for(int scenario=0;scenario<18;scenario++)
        {
            var go=new GameObject("Trail");var ps=go.AddComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=ps.main;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.startLifetime=3;main.startSize=.5f;main.startSpeed=0;main.maxParticles=8;
            var emission=ps.emission;emission.enabled=false;var shape=ps.shape;shape.enabled=false;
            var trail=ps.trails;trail.enabled=true;trail.ratio=1;trail.lifetime=1;trail.minVertexDistance=scenario==6?.3f:.05f;trail.worldSpace=scenario==11;trail.dieWithParticles=scenario!=8;trail.generateLightingData=scenario==1||scenario==17;trail.sizeAffectsWidth=scenario!=5;trail.sizeAffectsLifetime=scenario==9;
            if(scenario==3)trail.textureMode=ParticleSystemTrailTextureMode.Tile;
            if(scenario==14)trail.textureMode=ParticleSystemTrailTextureMode.DistributePerSegment;
            if(scenario==15)trail.textureMode=ParticleSystemTrailTextureMode.RepeatPerSegment;
            if(scenario==7)trail.lifetime=.1f;if(scenario==9)main.startSize=.25f;
            if(scenario==4){trail.widthOverTrail=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,0));var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.blue,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.2f,1)});trail.colorOverTrail=gradient;}
            if(scenario==16){var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.red,0),new GradientColorKey(Color.green,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});trail.colorOverLifetime=gradient;}
            if(scenario==10||scenario==11){main.simulationSpace=ParticleSystemSimulationSpace.Local;go.transform.position=new Vector3(2,1,3);go.transform.rotation=Quaternion.Euler(0,0,20);go.transform.localScale=new Vector3(-2,3,.5f);}
            if(scenario==12||scenario==13){trail.mode=ParticleSystemTrailMode.Ribbon;trail.ribbonCount=scenario==13?2:1;}
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.None;renderer.trailMaterial=new Material(Shader.Find("Particles/Standard Unlit"));
            if(scenario>=2)renderer.SetActiveTrailVertexStreams(new List<ParticleSystemVertexStream>(extra));
            var emit=new ParticleSystem.EmitParams{position=Vector3.zero,velocity=new Vector3(1,.5f,0),startLifetime=scenario==8?.5f:3,startSize=scenario==9?.25f:.5f,startColor=new Color32(51,102,153,204),randomSeed=123};ps.Emit(emit,1);
            if(scenario==12||scenario==13){emit.position=new Vector3(1,1,0);emit.velocity=new Vector3(-.3f,.2f,0);ps.Emit(emit,1);if(scenario==13){emit.position=new Vector3(-1,1,0);ps.Emit(emit,1);emit.position=new Vector3(1,-1,0);ps.Emit(emit,1);}}
            for(int step=0;step<12;step++)
            {
                ps.Simulate(.1f,false,false,false);
                if(step!=0&&step!=3&&step!=7&&step!=11)continue;
                for(int flags=0;flags<(scenario==10||scenario==11?4:1);flags++)
                {
                    string key="trail."+scenario+"."+step+"."+flags;var streams=new List<ParticleSystemVertexStream>();renderer.GetActiveTrailVertexStreams(streams);Value(key+".streams",A(streams,s=>((int)s).ToString(CultureInfo.InvariantCulture)));
                    var mesh=new Mesh();renderer.BakeTrailsMesh(mesh,camera,scenario==10||scenario==11?(ParticleSystemBakeMeshOptions)flags:ParticleSystemBakeMeshOptions.BakePosition|ParticleSystemBakeMeshOptions.BakeRotationAndScale);DescribeMesh(key,streams,mesh);DestroyImmediate(mesh);
                }
            }
            DestroyImmediate(go);
        }
    }
}
