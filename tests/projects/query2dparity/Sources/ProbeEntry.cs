using System;
using System.IO;
using System.Reflection;
using UnityEngine;

public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        // Invoke unchanged broad scenarios, including their own cleanup and exact inputs.
        var scenario = typeof(Probe2D).GetMethod("Scenario", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var method in new[] { "Queries", "QueryOptions", "CastsFromInside" })
        {
            var body = (Action)Delegate.CreateDelegate(typeof(Action), typeof(Probe2D).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic));
            scenario.Invoke(null, new object[] { "original." + method, body });
        }
        foreach (string kind in new[] { "circle", "box", "capsuleV", "capsuleH" })
        foreach (float angle in new[] { 0f, 23f, 90f })
        foreach (float scale in new[] { 0.5f, 1f, 2f })
        {
            ProbeLog.Begin(kind + "." + angle + "." + scale);
            var go = new GameObject("target");
            go.layer = 8;
            go.transform.position = new Vector3(0.3f, -0.2f, 2f);
            go.transform.rotation = Quaternion.Euler(0, 0, angle);
            go.transform.localScale = new Vector3(scale, scale * 0.8f, 1);
            if (kind == "circle") go.AddComponent<CircleCollider2D>().radius = 0.75f;
            else if (kind == "box") go.AddComponent<BoxCollider2D>().size = new Vector2(2, 1);
            else
            {
                var c = go.AddComponent<CapsuleCollider2D>();
                c.direction = kind == "capsuleH" ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
                c.size = kind == "capsuleH" ? new Vector2(3, 1) : new Vector2(1, 3);
            }
            Physics2D.SyncTransforms();
            var hits = new RaycastHit2D[8];
            var filter = new ContactFilter2D();
            filter.SetLayerMask(1 << 8);
            filter.SetDepth(1, 3);
            for (int d = 0; d < 8; d++)
            for (int offset = 0; offset < 3; offset++)
            {
                float a = d * 45 * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var origin = new Vector2(0.3f, -0.2f) - dir * 5 + new Vector2(-dir.y, dir.x) * (offset - 1) * scale;
                foreach (bool inside in new[] { true, false })
                {
                    Physics2D.queriesStartInColliders = inside;
                    var start = inside ? new Vector2(0.3f, -0.2f) : origin;
                    string k = d + "." + offset + "." + inside;
                    Hit(k + ".circle", Physics2D.CircleCast(start, 0.3f, dir, 10));
                    Hit(k + ".box", Physics2D.BoxCast(start, new Vector2(0.5f, 0.8f), 10, dir, 10));
                    int n = Physics2D.CapsuleCast(start, new Vector2(1.6f, 0.6f), CapsuleDirection2D.Horizontal, 30, dir, filter, hits, 10);
                    ProbeLog.Value(k + ".capsule.count", n);
                    for (int i = 0; i < n; i++) Hit(k + ".capsule." + i, hits[i]);
                }
            }
            // Tangency, near-touch, short ranges, direction normalization and filter rejection.
            foreach (float gap in new[] { -0.001f, 0f, 0.001f, 0.01f })
            foreach (float range in new[] { 0f, 0.01f, 3f })
            {
                var bounds = go.GetComponent<Collider2D>().bounds;
                var origin = new Vector2(bounds.min.x - 0.3f - gap, bounds.center.y);
                Hit("touch." + gap + "." + range, Physics2D.CircleCast(origin, 0.3f, Vector2.right * 3, range));
            }
            filter.SetNormalAngle(80, 100);
            ProbeLog.Value("normalFiltered", Physics2D.CircleCast(new Vector2(-5, 0), 0.3f, Vector2.right, filter, hits, 10));
            filter.SetDepth(-3, -1);
            ProbeLog.Value("depthFiltered", Physics2D.CircleCast(new Vector2(-5, 0), 0.3f, Vector2.right, filter, hits, 10));
            ProbeLog.Value("maskFiltered", Physics2D.CircleCast(new Vector2(-5, 0), 0.3f, Vector2.right, 10, 1 << 9).collider != null);
            UnityEngine.Object.DestroyImmediate(go);
        }
        Physics2D.queriesStartInColliders = true;
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Hit(string k, RaycastHit2D h)
    {
        ProbeLog.Value(k + ".collider", h.collider ? h.collider.name : "none");
        if (!h.collider) return;
        ProbeLog.Value(k + ".distance", h.distance);
        ProbeLog.Value(k + ".fraction", h.fraction);
        ProbeLog.Value(k + ".normal", h.normal);
        ProbeLog.Value(k + ".point", h.point);
        ProbeLog.Value(k + ".centroid", h.centroid);
    }
}
