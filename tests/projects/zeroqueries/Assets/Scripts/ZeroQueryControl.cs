using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public sealed class ZeroQueryControl : MonoBehaviour
{
    const int Mask = 1 << 27;
    static readonly List<string> Rows = new List<string>();
    static readonly Collider[] Colliders = new Collider[16];
    static readonly RaycastHit[] Hits = new RaycastHit[16];
    static void Put(string key, object value) => Rows.Add("{\"k\":\"" + key + "\",\"v\":\"" + Convert.ToString(value, CultureInfo.InvariantCulture) + "\"}");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (Environment.GetEnvironmentVariable("ZERO_QUERY_OUT") != null)
            new GameObject("Zero query controls").AddComponent<ZeroQueryControl>();
    }
    void Start()
    {
        try
        {
            // Isolated target shapes keep membership and count answers independent of scene contents.
            foreach (string target in new[] { "sphere", "box", "capsule" })
            {
                var go = new GameObject(target); go.layer = 27;
                if (target == "sphere") go.AddComponent<SphereCollider>().radius = 1;
                else if (target == "box") go.AddComponent<BoxCollider>().size = Vector3.one * 2;
                else { var c = go.AddComponent<CapsuleCollider>(); c.radius = 1; c.height = 4; }
                foreach (bool trigger in new[] { false, true })
                {
                    go.GetComponent<Collider>().isTrigger = trigger;
                    Physics.SyncTransforms();
                    foreach (float x in new[] { 0f, 0.99999988f, 1f, 1.00000012f, 1.000005f, 1.001f, 2f })
                    foreach (float r in new[] { 0f, 0.000001f, 0.1f })
                    foreach (int kind in new[] { 0, 1, 2, 3, 4, 5 })
                    {
                        string k = target + "/" + trigger + "/" + Array.IndexOf(new[] { 0f, 0.99999988f, 1f, 1.00000012f, 1.000005f, 1.001f, 2f }, x).ToString() + "/" + Array.IndexOf(new[] { 0f, 0.000001f, 0.1f }, r).ToString() + "/" + kind;
                        Query(k, new Vector3(x, 0, 0), r, kind);
                    }
                }
                DestroyImmediate(go);
            }
            Put("complete", 1);
        }
        catch (Exception e) { Put("error", e.GetType().Name); Debug.LogException(e); }
        File.WriteAllLines(Environment.GetEnvironmentVariable("ZERO_QUERY_OUT"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void Query(string key, Vector3 p, float r, int kind)
    {
        var a = kind == 1 ? p - Vector3.up * 0.25f : p;
        var b = kind == 1 ? p + Vector3.up * 0.25f : p;
        var h = kind == 3 ? new Vector3(r, 0.25f, 0.25f) : kind == 4 ? new Vector3(0.25f, r, r) : Vector3.one * r;
        const QueryTriggerInteraction triggers = QueryTriggerInteraction.Collide;
        int overlap, nonalloc; bool check, cast; RaycastHit hit;
        if (kind == 0)
        {
            overlap = Physics.OverlapSphere(p, r, Mask, triggers).Length;
            nonalloc = Physics.OverlapSphereNonAlloc(p, r, Colliders, Mask, triggers);
            check = Physics.CheckSphere(p, r, Mask, triggers);
            cast = Physics.SphereCast(p, r, Vector3.left, out hit, 3, Mask, triggers);
            Put(key + "/castAll", Physics.SphereCastAll(p, r, Vector3.left, 3, Mask, triggers).Length);
            Put(key + "/castNonAlloc", Physics.SphereCastNonAlloc(p, r, Vector3.left, Hits, 3, Mask, triggers));
        }
        else if (kind == 1 || kind == 5)
        {
            overlap = Physics.OverlapCapsule(a, b, r, Mask, triggers).Length;
            nonalloc = Physics.OverlapCapsuleNonAlloc(a, b, r, Colliders, Mask, triggers);
            check = Physics.CheckCapsule(a, b, r, Mask, triggers);
            cast = Physics.CapsuleCast(a, b, r, Vector3.left, out hit, 3, Mask, triggers);
            Put(key + "/castAll", Physics.CapsuleCastAll(a, b, r, Vector3.left, 3, Mask, triggers).Length);
            Put(key + "/castNonAlloc", Physics.CapsuleCastNonAlloc(a, b, r, Vector3.left, Hits, 3, Mask, triggers));
        }
        else
        {
            overlap = Physics.OverlapBox(p, h, Quaternion.identity, Mask, triggers).Length;
            nonalloc = Physics.OverlapBoxNonAlloc(p, h, Colliders, Quaternion.identity, Mask, triggers);
            check = Physics.CheckBox(p, h, Quaternion.identity, Mask, triggers);
            cast = Physics.BoxCast(p, h, Vector3.left, out hit, Quaternion.identity, 3, Mask, triggers);
            Put(key + "/castAll", Physics.BoxCastAll(p, h, Vector3.left, Quaternion.identity, 3, Mask, triggers).Length);
            Put(key + "/castNonAlloc", Physics.BoxCastNonAlloc(p, h, Vector3.left, Hits, Quaternion.identity, 3, Mask, triggers));
        }
        Put(key + "/overlap", overlap); Put(key + "/nonalloc", nonalloc); Put(key + "/check", check); Put(key + "/cast", cast);

    }
}
