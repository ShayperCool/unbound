using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        var sizes = new[] { new Vector2(1,1), new Vector2(1,1.004f), new Vector2(1,1.005f),
            new Vector2(1,1.006f), new Vector2(1,2), new Vector2(2,1),
            new Vector2(.005f,.02f), new Vector2(.0049f,.02f), new Vector2(.0051f,.02f) };
        int index = 0;
        foreach (var direction in new[] { CapsuleDirection2D.Vertical, CapsuleDirection2D.Horizontal })
        foreach (var size in sizes)
        foreach (bool translated in new[] { false, true })
        {
            ProbeLog.Begin("capsule/" + index++);
            var go = new GameObject("capsule");
            if (translated) { go.transform.position = new Vector3(.3f,-.2f,0); go.transform.rotation = Quaternion.Euler(0,0,23); }
            var c = go.AddComponent<CapsuleCollider2D>(); c.size = size; c.direction = direction;
            Physics2D.SyncTransforms();
            var group = new PhysicsShapeGroup2D();
            ProbeLog.Value("count",c.GetShapes(group)); ProbeLog.Value("error",c.errorState.ToString());
            for (int i = 0; i < group.shapeCount; i++)
            {
                var shape = group.GetShape(i); var points = new List<Vector2>(); group.GetShapeVertices(i,points);
                ProbeLog.Value("shape/"+i+"/radius",shape.radius); ProbeLog.Value("shape/"+i+"/count",points.Count);
                for (int j = 0; j < points.Count; j++) ProbeLog.Value("shape/"+i+"/vertex/"+j,points[j]);
            }
            UnityEngine.Object.DestroyImmediate(go);
        }
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
