using System;
using System.IO;
using UnityEngine;
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output=Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if(string.IsNullOrEmpty(output)) return;
        var factoryAngles = new[] { Vector3.zero,new Vector3(0,0,11),new Vector3(0,0,12),new Vector3(0,0,23),
            new Vector3(0,0,90),new Vector3(0,0,13.37f),new Vector3(0,0,-47.125f),new Vector3(0,0,137.6f),
            new Vector3(23,35,7),new Vector3(23,35,19),new Vector3(17,-29,13),new Vector3(1.3f,2.7f,9.1f),
            new Vector3(37.2f,-68.3f,119.7f),new Vector3(-180,90,360),new Vector3(181,-179,540),new Vector3(.0001f,-.001f,.01f),new Vector3(0,0,360),new Vector3(0,0,-360),new Vector3(360,0,0),new Vector3(-360,0,0),
            new Vector3(0,360,0),new Vector3(0,-360,0),new Vector3(.000001f,0,0),new Vector3(-.000001f,0,0),
            new Vector3(359.99997f,0,0),new Vector3(360.00003f,0,0),new Vector3(0,359.99997f,-360.00003f),
            new Vector3(89.99999f,0,0),new Vector3(90.00001f,0,0),new Vector3(0,89.99999f,0),new Vector3(0,90.00001f,0),
            new Vector3(0,0,89.99999f),new Vector3(0,0,90.00001f),new Vector3(-90,0,0),
            new Vector3(179.99998f,0,0),new Vector3(180.00002f,0,0),new Vector3(-180.00002f,0,0),
            new Vector3(0,0,179.99998f),new Vector3(0,0,180.00002f),new Vector3(720,-1080,1440),new Vector3(-720,1080,-1440),
            new Vector3(10000,-20000,30000),new Vector3(1000000,-1000000,500000),new Vector3(12.34567f,-234.567f,45.6789f),
            new Vector3(-.03f,179.9f,-91.1f),new Vector3(.1f,.2f,.3f) };
        for(int i=0;i<factoryAngles.Length;i++)
        {
            var angle=factoryAngles[i];ProbeLog.Begin("factory/"+i);ProbeLog.Value("degrees",angle);
            Q("vector",Quaternion.Euler(angle));Q("components",Quaternion.Euler(angle.x,angle.y,angle.z));
            Q("normalized",Quaternion.Normalize(Quaternion.Euler(angle)));
            var q=Quaternion.Euler(angle);ProbeLog.Value("normSquared",Quaternion.Dot(q,q));
            ProbeLog.Value("right",q*Vector3.right);ProbeLog.Value("up",q*Vector3.up);ProbeLog.Value("forward",q*Vector3.forward);
            ProbeLog.Value("mixed",q*new Vector3(.3f,-.7f,1.1f));

        }
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Q(string key,Quaternion q)
    {
        ProbeLog.Value(key+".x",q.x);ProbeLog.Value(key+".y",q.y);ProbeLog.Value(key+".z",q.z);ProbeLog.Value(key+".w",q.w);
    }
}
