using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class RayHitProbe : MonoBehaviour
{
    readonly StringBuilder output=new StringBuilder();
    static string F(float f)=>f.ToString("R",CultureInfo.InvariantCulture);
    static string V(Vector3 v)=>"["+F(v.x)+","+F(v.y)+","+F(v.z)+"]";
    static string V2(Vector2 v)=>"["+F(v.x)+","+F(v.y)+"]";
    static string S(string s)=>"\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
    void Value(string k,string v)=>output.Append("{\"k\":").Append(S(k)).Append(",\"v\":").Append(v).Append("}\n");
    static string Read(Func<Vector2> get){try{return V2(get());}catch(Exception e){return S(e.GetType().Name);}}
    static readonly List<string> messages=new List<string>();
    static string Hit(RaycastHit h)
    {
        messages.Clear();string uv=Read(()=>h.textureCoord),uv2=Read(()=>h.textureCoord2),lightmap=Read(()=>h.lightmapCoord);
        return "{\"point\":"+V(h.point)+",\"normal\":"+V(h.normal)+",\"distance\":"+F(h.distance)+",\"triangle\":"+h.triangleIndex+",\"bary\":"+V(h.barycentricCoordinate)+",\"uv\":"+uv+",\"uv2\":"+uv2+",\"lightmap\":"+lightmap+",\"messages\":["+string.Join(",",messages.Select(S))+"]}";
    }
    public static Mesh MakeMesh(int mode)
    {
        var mesh=new Mesh{name="UV mesh"};
        mesh.vertices=new[]{new Vector3(0,0,0),new Vector3(0,0,2),new Vector3(2,0,0),new Vector3(3,0,0),new Vector3(3,0,2),new Vector3(5,0,0)};
        mesh.subMeshCount=2;mesh.SetTriangles(new[]{0,1,2},0);mesh.SetTriangles(new[]{0,1,2},1,true,3);
        if(mode==1||mode==2)mesh.uv=new[]{new Vector2(.1f,.2f),new Vector2(.4f,.7f),new Vector2(.9f,.3f),new Vector2(2,3),new Vector2(-1,4),new Vector2(5,-2)};
        if(mode==2||mode==3)mesh.uv2=new[]{new Vector2(2,-1),new Vector2(-1,4),new Vector2(3,5),new Vector2(.1f,.2f),new Vector2(.4f,.7f),new Vector2(.9f,.3f)};
        mesh.RecalculateBounds();return mesh;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Run()=>new GameObject("Probe").AddComponent<RayHitProbe>();
    void Start()
    {
#if UNITY_EDITOR
        Application.SetStackTraceLogType(LogType.Error,StackTraceLogType.None);
#endif
        Application.logMessageReceived+=(message,stack,type)=>{if(type==LogType.Error||type==LogType.Warning||type==LogType.Assert)messages.Add(type+": "+message);};
        var previous=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
        for(int mode=0;mode<5;mode++)
        for(int parented=0;parented<3;parented++)
        foreach(bool render in new[]{false,true})
        foreach(int lightmap in new[]{-1,0})
        {
            string key="mesh."+mode+"."+parented+"."+render+"."+lightmap;
            var parent=new GameObject("Parent");
            if(parented>0){parent.transform.position=new Vector3(2,3,4);parent.transform.rotation=Quaternion.Euler(20,30,10);parent.transform.localScale=new Vector3(2,1.5f,3);}
            if(parented==2)parent.AddComponent<Rigidbody>().isKinematic=true;
            var go=new GameObject("Mesh");go.transform.SetParent(parent.transform,false);
            var mesh=mode==4?Resources.Load<Mesh>("Mesh"):MakeMesh(mode);
            if(render){go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.lightmapIndex=lightmap;r.lightmapScaleOffset=new Vector4(2,3,.1f,.2f);r.realtimeLightmapScaleOffset=new Vector4(4,5,.3f,.4f);}
            var c=go.AddComponent<MeshCollider>();c.sharedMesh=mesh;Physics.SyncTransforms();
            RaycastHit saved=default;
            for(int tri=0;tri<2;tri++)
            {
                int sample=0;
                foreach(var bary in new[]{new Vector3(.2f,.3f,.5f),new Vector3(.8f,.1f,.1f),new Vector3(.1f,.7f,.2f)})
                {
                    var vertices=mesh.vertices;var point=vertices[tri*3]*bary.x+vertices[tri*3+1]*bary.y+vertices[tri*3+2]*bary.z;
                    var origin=go.transform.TransformPoint(point)+go.transform.up*5;var ray=new Ray(origin,-go.transform.up);
                    string k=key+"."+tri+"."+sample++;
                    bool hit=Physics.Raycast(ray,out var h,20);Value(k+".hit",hit?"true":"false");if(hit){Value(k+".ray",Hit(h));saved=h;}
                    if(c.Raycast(ray,out h,20))Value(k+".collider",Hit(h));
                    var all=Physics.RaycastAll(ray,20);Value(k+".all_count",all.Length.ToString());if(all.Length>0)Value(k+".all",Hit(all[0]));
                    var buf=new RaycastHit[2];if(Physics.RaycastNonAlloc(ray,buf,20)>0)Value(k+".nonalloc",Hit(buf[0]));
                    if(Physics.SphereCast(ray,.05f,out h,20))Value(k+".sphere",Hit(h));
                    if(Physics.BoxCast(origin,Vector3.one*.03f,ray.direction,out h,Quaternion.identity,20))Value(k+".box",Hit(h));
                }
            }
            saved.barycentricCoordinate=new Vector3(.2f,.3f,.5f);Value(key+".set_bary",Hit(saved));
            saved.barycentricCoordinate=new Vector3(2,3,4);Value(key+".set_bary_invalid",Hit(saved));
            saved.point=new Vector3(100,200,300);saved.normal=Vector3.forward;Value(key+".set_point",Hit(saved));
            if(mode==2)
            {
                mesh.uv=mesh.uv.Select(v=>v+Vector2.one).ToArray();Value(key+".changed_uv",Hit(saved));
                c.sharedMesh=MakeMesh(1);Physics.SyncTransforms();Value(key+".changed_mesh",Hit(saved));
                DestroyImmediate(c.sharedMesh);
            }
            DestroyImmediate(parent);if(mode!=4)DestroyImmediate(mesh);
        }
        foreach(var type in new[]{PrimitiveType.Cube,PrimitiveType.Sphere,PrimitiveType.Capsule})
        {
            var go=GameObject.CreatePrimitive(type);Physics.SyncTransforms();if(Physics.Raycast(new Vector3(.1f,5,.1f),Vector3.down,out var h,20))Value("primitive."+type,Hit(h));DestroyImmediate(go);
        }
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);DestroyImmediate(go.GetComponent<Collider>());var mesh=go.GetComponent<MeshFilter>().sharedMesh;
            var c=go.AddComponent<MeshCollider>();c.convex=true;c.sharedMesh=mesh;Physics.SyncTransforms();if(Physics.Raycast(new Vector3(.1f,5,.1f),Vector3.down,out var h,20))Value("convex",Hit(h));DestroyImmediate(go);
        }
        Value("default",Hit(default));Physics.simulationMode=previous;
        string path=Environment.GetEnvironmentVariable("RAYHITS_PROBE_OUT")??"/tmp/rayhits";Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"values.jsonl"),output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
