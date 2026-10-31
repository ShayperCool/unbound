using System;
using System.IO;
using UnityEngine;
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script; Physics2D.gravity = Vector2.zero;
        foreach (var dt in new[] { .01f, .02f, .04f })
        foreach (var initial in new[] { 0f, 7f, 179f, -179f, 359f, 721f })
        foreach (var delta in new[] { 1f, -1f, 190f, -190f, 360f })
        {
            ProbeLog.Begin($"rotate/{dt}/{initial}/{delta}");
            var o = new GameObject("paddle"); var b=o.AddComponent<Rigidbody2D>(); b.bodyType=RigidbodyType2D.Kinematic;
            b.rotation=initial;
            for(int i=0;i<100;i++)
            {
                float target=b.rotation+delta;
                ProbeLog.Value(i+"/before",b.rotation); ProbeLog.Value(i+"/target",target);
                b.MoveRotation(target); Physics2D.Simulate(dt);
                ProbeLog.Value(i+"/after",b.rotation); ProbeLog.Value(i+"/velocity",b.angularVelocity);
            }
            UnityEngine.Object.DestroyImmediate(o);
        }
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
