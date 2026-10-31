using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        typeof(Probe2D).GetMethod("Layout", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        foreach (string name in new[] { "box", "circle", "capsuleV", "capsuleH", "polygon", "edge", "scaledBox" })
        {
            ProbeLog.Begin(name);
            var group = new PhysicsShapeGroup2D();
            var c = GameObject.Find(name).GetComponent<Collider2D>();
            ProbeLog.Value("count", c.GetShapes(group));
            for (int i = 0; i < group.shapeCount; i++)
            {
                var shape=group.GetShape(i);
                ProbeLog.Value(i+".type",(int)shape.shapeType);
                ProbeLog.Value(i+".radius",shape.radius);
                var vertices=new List<Vector2>(); group.GetShapeVertices(i,vertices);
                ProbeLog.Value(i+".count",vertices.Count);
                for(int j=0;j<vertices.Count;j++) ProbeLog.Value(i+".v"+j,vertices[j]);
            }
        }
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
