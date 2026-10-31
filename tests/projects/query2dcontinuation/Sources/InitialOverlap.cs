using System;
using System.IO;
using UnityEngine;

public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.defaultContactOffset = 0.01f;
        foreach (string target in new[] { "box", "circle", "capsuleV", "capsuleH" })
        {
            var go = new GameObject("target");
            if (target == "box") go.AddComponent<BoxCollider2D>().size = new Vector2(2, 1);
            else if (target == "circle") go.AddComponent<CircleCollider2D>().radius = 0.75f;
            else
            {
                var c = go.AddComponent<CapsuleCollider2D>();
                c.direction = target == "capsuleH" ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
                c.size = target == "capsuleH" ? new Vector2(3, 1) : new Vector2(1, 3);
            }
            Physics2D.SyncTransforms();
            float left = go.GetComponent<Collider2D>().bounds.min.x;
            foreach (string query in new[] { "circle", "box", "capsule" })
            foreach (float gap in new[] { -.03f, -.02f, -.016f, -.015f, -.014f, -.012f, -.01f, -.006f, -.005f, -.004f, -.001f, 0f, .001f, .01f })
            foreach (bool include in new[] { false, true })
            {
                Physics2D.queriesStartInColliders = include;
                ProbeLog.Begin(target + "." + query + "." + gap + "." + include);
                var origin = new Vector2(left - .3f - gap, 0);
                Hit("zero", Cast(query, origin, Vector2.right, 0));
                Hit("short", Cast(query, origin, Vector2.right, .01f));
                Hit("toward", Cast(query, origin, Vector2.right, 3));
                Hit("away", Cast(query, origin, Vector2.left, 3));
            }
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
    static RaycastHit2D Cast(string kind, Vector2 origin, Vector2 direction, float distance)
    {
        if (kind == "circle") return Physics2D.CircleCast(origin, .3f, direction, distance);
        if (kind == "box") return Physics2D.BoxCast(origin, new Vector2(.6f, .8f), 0, direction, distance);
        return Physics2D.CapsuleCast(origin, new Vector2(.6f, .8f), CapsuleDirection2D.Vertical, 0, direction, distance);
    }
    static void Hit(string key, RaycastHit2D hit)
    {
        ProbeLog.Value(key + ".collider", hit.collider ? hit.collider.name : "none");
        if (!hit.collider) return;
        ProbeLog.Value(key + ".distance", hit.distance);
        ProbeLog.Value(key + ".fraction", hit.fraction);
        ProbeLog.Value(key + ".point", hit.point);
        ProbeLog.Value(key + ".normal", hit.normal);
        ProbeLog.Value(key + ".centroid", hit.centroid);
    }
}
