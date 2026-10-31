using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using N = System.Numerics.Vector2;
public static class ProbeEntry
{
    static readonly BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static object Get(object o,string f)=>(o.GetType().GetField(f,Flags)?.GetValue(o) ?? o.GetType().GetProperty(f,Flags).GetValue(o));
    static Type T(string name)=>typeof(Physics2D).Assembly.GetType(name,true);
    static object Make(string name,params object[] args)=>Activator.CreateInstance(T(name),args);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output=Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if(string.IsNullOrEmpty(output))return;
        typeof(Probe2D).GetMethod("Layout",Flags).Invoke(null,null);
        Case("H", "polygon", new Vector2(2,4.000989f),new Vector2(1.6f,.6f),30,true);
        Case("V", "polygon", new Vector2(5.603674f,4.080582f),new Vector2(.5f,1.2f),0,true);
        Case("boxV", "trigger", new Vector2(-5,5.165f),new Vector2(.5f,.8f),10,false);
        File.WriteAllText(output,ProbeLog.Json());Application.Quit();
    }
    static void Case(string name,string target,Vector2 center,Vector2 size,float angle,bool capsule)
    {
        ProbeLog.Begin(name);
        var collider=GameObject.Find(target).GetComponent<Collider2D>();
        var fixture=((IList)Get(collider,"m_Fixtures"))[0];
        var a=fixture.GetType().GetProperty("Shape",Flags).GetValue(fixture);
        var body=fixture.GetType().GetProperty("Body",Flags).GetValue(fixture);
        var xfA=body.GetType().GetMethod("GetTransform",Flags).Invoke(body,null);
        var b=typeof(Physics2D).GetMethod(capsule?"CapsuleShape":"BoxShape",Flags).Invoke(null,capsule?new object[]{size,CapsuleDirection2D.Horizontal}:new object[]{size});
        if(name=="V")b=typeof(Physics2D).GetMethod("CapsuleShape",Flags).Invoke(null,new object[]{size,CapsuleDirection2D.Vertical});
        var xfB=Make("Unbound.Box2D.Common.Transform",new N(center.x,center.y),angle*Mathf.Deg2Rad);
        var manifold=Make("Unbound.Box2D.Collision.Collider.Manifold");
        var args=new[]{manifold,a,xfA,b,xfB};
        T("Unbound.Runtime.QueryParityManifold").GetMethod("CollidePolygons",Flags).Invoke(null,args);manifold=args[0];
        ProbeLog.Value("type",Convert.ToInt32(Get(manifold,"Type")));
        ProbeLog.Value("count",Convert.ToInt32(Get(manifold,"PointCount")));
        var world=Make("Unbound.Box2D.Collision.Collider.WorldManifold");
        world.GetType().GetMethod("Initialize",Flags).Invoke(world,new[]{manifold,xfA,0.01f,xfB,capsule?(name=="V"?0.25f:0.3f):0.01f});
        foreach(string field in new[]{"Points","Separations"})
        for(int i=0;i<2;i++)
        {
            var v=Get(Get(world,field),"Value"+i);
            if(v is N n)ProbeLog.Value(field+i,new Vector2(n.X,n.Y));else ProbeLog.Value(field+i,(float)v);
        }
    }
}
