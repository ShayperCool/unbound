using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Preserve raw quaternion components and compare transform-derived body angles
// with both public rotation setters before any contact can perturb the pose.
public static class RotationInputsProbe
{
    public static void Run(string output)
    {
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero;
        int step = 0;
        foreach (float z in new[] { -179f, -90f, -7f, 0f, 7f, 45f, 89.9f, 90f, 135f, 179f, 180f, 181f, 270f })
        foreach (var tilt in new[] { Vector2.zero, new Vector2(23, 0), new Vector2(0, 35), new Vector2(23, 35) })
        {
            ProbeLog.Begin("input/" + step);
            ProbeLog.Value("input", new Vector3(tilt.x, tilt.y, z));
            var q = Quaternion.Euler(tilt.x, tilt.y, z);
            ProbeLog.Value("q.x", q.x); ProbeLog.Value("q.y", q.y);
            ProbeLog.Value("q.z", q.z); ProbeLog.Value("q.w", q.w);
            ProbeLog.Value("euler", q.eulerAngles);
            var right = q * Vector3.right;
            ProbeLog.Value("right", right);
            ProbeLog.Value("right-angle", Mathf.Atan2(right.y, right.x));
            ProbeLog.Value("twist-angle", 2 * Mathf.Atan2(q.z, q.w));
            var o = new GameObject("body"); o.transform.rotation = q;
            var rb = o.AddComponent<Rigidbody2D>();
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            o.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            ProbeLog.Value("transform-angle", rb.rotation);
            Physics2D.Simulate(.02f);
            ProbeLog.Value("after-angle", rb.rotation);
            ManagedTrace.RecordState(++step, "transform", new List<Rigidbody2D> { rb });
            rb.SetRotation(q);
            ProbeLog.Value("quaternion-setter", rb.rotation);
            rb.SetRotation(z);
            ProbeLog.Value("float-setter", rb.rotation);
            UnityEngine.Object.DestroyImmediate(o);
        }
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
