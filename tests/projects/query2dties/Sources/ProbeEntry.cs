using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.OverlapPoint(Vector2.zero);
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.queriesStartInColliders = true;
        Physics2D.gravity = Vector2.zero;
        Physics2D.defaultContactOffset = .01f;
        foreach (string kind in new[] { "circle", "box", "capsuleV", "capsuleH" })
        foreach (float angle in new[] { 0f, 23f, 90f })
        foreach (float scale in new[] { .5f, 1f, 2f })
        foreach (string frame in new[] { "static", "body", "child", "rotated-child" })
        foreach (var center in new[] { Vector2.zero, new Vector2(.3f, -.2f) })
        {
            var go = new GameObject("target");
            GameObject parent = null;
            go.transform.position = new Vector3(center.x, center.y, 2);
            go.transform.rotation = Quaternion.Euler(0, 0, angle);
            go.transform.localScale = new Vector3(scale, .8f * scale, 1);
            if (frame == "body") go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            if (frame == "child" || frame == "rotated-child")
            {
                parent = new GameObject("body");
                parent.transform.position = new Vector3(center.x, center.y, 2);
                parent.transform.rotation = Quaternion.Euler(0, 0, frame == "child" ? 0 : 11);
                parent.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                go.transform.SetParent(parent.transform, true);
            }
            switch (kind)
            {
                case "circle": go.AddComponent<CircleCollider2D>().radius = .75f; break;
                case "box": go.AddComponent<BoxCollider2D>().size = new Vector2(2, 1); break;
                default:
                    var capsule = go.AddComponent<CapsuleCollider2D>();
                    capsule.direction = kind == "capsuleH" ? CapsuleDirection2D.Horizontal : CapsuleDirection2D.Vertical;
                    capsule.size = kind == "capsuleH" ? new Vector2(3, 1) : new Vector2(1, 3);
                    break;
            }
            Physics2D.SyncTransforms();
            foreach (float delta in new[] { -.1f, -.001f, -.00001f, -.0000001f, 0f, .0000001f, .00001f, .001f, .1f })
            {
                string name = kind + "/" + Number(angle) + "/" + Number(scale) + "/" + frame
                    + "/" + Number(center.x) + "/" + Number(center.y) + "/" + Number(delta);
                ProbeLog.Begin(name);
                var axis = Quaternion.Euler(0, 0, angle) * Vector3.up;
                var origin = center + delta * new Vector2(axis.x, axis.y);
                ProbeLog.Value("origin", origin);
                ProbeLog.Value("target", go.transform.position);
                Hit("circle", Physics2D.CircleCast(origin, .3f, Vector2.right, 1));
                Hit("box", Physics2D.BoxCast(origin, new Vector2(.5f, .8f), 10, Vector2.right, 1));
                Hit("capsule", Physics2D.CapsuleCast(origin, new Vector2(1.6f, .6f), CapsuleDirection2D.Horizontal, 30, Vector2.right, 1));
            }
            if (parent) UnityEngine.Object.DestroyImmediate(parent);
            else UnityEngine.Object.DestroyImmediate(go);
        }
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    static void Hit(string key, RaycastHit2D hit)
    {
        ProbeLog.Value(key + ".collider", hit.collider ? hit.collider.name : "none");
        ProbeLog.Value(key + ".distance", hit.distance);
        ProbeLog.Value(key + ".normal", hit.normal);
        ProbeLog.Value(key + ".point", hit.point);
        ProbeLog.Value(key + ".centroid", hit.centroid);
    }
}
