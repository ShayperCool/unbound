using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.OverlapPoint(Vector2.zero);
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.autoSyncTransforms = false;
        Physics2D.gravity = Vector2.zero;
        Physics2D.queriesHitTriggers = true;
        foreach (float next in new[] { .001f, .1f })
        foreach (string action in new[] { "none", "sync-before", "sync-after", "enable", "activate",
            "trigger", "material", "density", "size-same", "size-edit", "offset-same", "offset-edit",
            "scale-same", "scale-edit", "rotation", "position", "body-static", "body-kinematic",
            "clone", "clone-inactive", "disabled-geometry", "inactive-geometry", "initial-inactive" })
        {
            ProbeLog.Begin(next.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "/" + action);
            Physics2D.defaultContactOffset = .01f;
            var source = new GameObject("box");
            if (action == "initial-inactive") source.SetActive(false);
            var collider = source.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(4, 1);
            if (action == "sync-before") Physics2D.SyncTransforms();
            // No physics property/query read occurs before the tested transition.
            Physics2D.defaultContactOffset = next;
            GameObject clone = null;
            PhysicsMaterial2D material = null;
            switch (action)
            {
                case "sync-after": Physics2D.SyncTransforms(); break;
                case "enable": collider.enabled = false; collider.enabled = true; break;
                case "activate": source.SetActive(false); source.SetActive(true); break;
                case "trigger": collider.isTrigger = true; break;
                case "material": material = new PhysicsMaterial2D("skin"); material.friction = .61f; collider.sharedMaterial = material; break;
                case "density": collider.density = 2.5f; break;
                case "size-same": collider.size = collider.size; break;
                case "size-edit": collider.size = new Vector2(4.2f, 1); break;
                case "offset-same": collider.offset = collider.offset; break;
                case "offset-edit": collider.offset = new Vector2(0, .1f); break;
                case "scale-same": source.transform.localScale = Vector3.one; break;
                case "scale-edit": source.transform.localScale = new Vector3(1.1f, 1, 1); break;
                case "rotation": source.transform.rotation = Quaternion.Euler(0, 0, 5); break;
                case "position": source.transform.position = new Vector3(0, .1f, 0); break;
                case "body-static": source.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static; break;
                case "body-kinematic": source.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic; break;
                case "clone":
                case "clone-inactive":
                    if (action == "clone-inactive") source.SetActive(false);
                    clone = UnityEngine.Object.Instantiate(source); clone.name = "box";
                    source.SetActive(false); clone.SetActive(true); collider = clone.GetComponent<BoxCollider2D>();
                    break;
                case "disabled-geometry": collider.enabled = false; collider.size = new Vector2(4.2f, 1); collider.enabled = true; break;
                case "inactive-geometry": source.SetActive(false); collider.size = new Vector2(4.2f, 1); source.SetActive(true); break;
                case "initial-inactive": source.SetActive(true); break;
            }
            Physics2D.SyncTransforms();
            var hit = Physics2D.BoxCast(new Vector2(0, 4), new Vector2(2, .8f), 0, Vector2.down, 5);
            ProbeLog.Value("collider", hit.collider ? hit.collider.name : "none");
            ProbeLog.Value("point", hit.point); ProbeLog.Value("normal", hit.normal);
            ProbeLog.Value("centroid", hit.centroid); ProbeLog.Value("distance", hit.distance);
            ProbeLog.Value("bounds.center", collider.bounds.center);
            ProbeLog.Value("bounds.size", collider.bounds.size);
            if (clone) UnityEngine.Object.DestroyImmediate(clone);
            UnityEngine.Object.DestroyImmediate(source);
            if (material) UnityEngine.Object.DestroyImmediate(material);
        }
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
