using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class LoadingProbe
{
    static readonly StringBuilder rows = new StringBuilder();
    public static string Name(string kind, bool active, bool kinematic, bool parented, int sequence) =>
        kind + "-" + (active ? "active" : "inactive") + "-" + (kinematic ? "kinematic" : "dynamic") + "-" + (parented ? "child" : "root") + "-" + sequence;
    public static void Row(string key, params float[] values)
    {
        rows.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) rows.Append(',');
            rows.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }
        rows.Append("]}\n");
    }
    // This intentionally touches only Transform and GameObject, never Rigidbody state.
    static void Authored(string key, GameObject go)
    {
        var t = go.transform; var p = t.position; var q = t.rotation;
        var l = t.localPosition; var r = t.localRotation;
        Row(key + "/authored", p.x,p.y,p.z,q.x,q.y,q.z,q.w,l.x,l.y,l.z,r.x,r.y,r.z,r.w,
            go.activeSelf ? 1 : 0, go.activeInHierarchy ? 1 : 0);
    }
    static void State(string key, Rigidbody rb)
    {
        var p = rb.position; var q = rb.rotation; var v = rb.linearVelocity; var a = rb.angularVelocity;
        var t = rb.transform.position; var r = rb.transform.rotation;
        Row(key + "/pose",p.x,p.y,p.z,q.x,q.y,q.z,q.w,t.x,t.y,t.z,r.x,r.y,r.z,r.w);
        Row(key + "/motion",v.x,v.y,v.z,a.x,a.y,a.z,rb.IsSleeping() ? 1 : 0);
        Row(key + "/settings",rb.isKinematic ? 1 : 0,rb.useGravity ? 1 : 0,rb.mass,
            rb.linearDamping,rb.angularDamping,(int)rb.constraints,(int)rb.interpolation,
            (int)rb.collisionDetectionMode,rb.detectCollisions ? 1 : 0,rb.solverIterations,
            rb.solverVelocityIterations,rb.sleepThreshold,rb.maxAngularVelocity,rb.maxDepenetrationVelocity);
    }
    static void Edit(GameObject go) => go.transform.SetPositionAndRotation(new Vector3(31,32,33),new Quaternion(0,0,1,0));
    static void SetPose(Rigidbody rb) { rb.position = new Vector3(41,42,43); rb.rotation = new Quaternion(0,1,0,0); }
    static GameObject Body(GameObject root) => root.GetComponent<Rigidbody>() != null ? root : root.transform.GetChild(0).gameObject;
    static void First(string key, GameObject go, int sequence)
    {
        var rb = go.GetComponent<Rigidbody>();
        if (sequence == 2) Physics.SyncTransforms();
        if (sequence == 3 || sequence == 4) SetPose(rb);
        if (sequence == 5) { go.SetActive(true); Physics.SyncTransforms(); }
        State(key + "/first", rb);
    }
    static void Follow(string key, GameObject go)
    {
        var rb = go.GetComponent<Rigidbody>();
        SetPose(rb); State(key + "/set",rb);
        Edit(go); State(key + "/transform",rb);
        Physics.SyncTransforms(); State(key + "/sync",rb);
        rb.MovePosition(new Vector3(51,52,53)); rb.MoveRotation(new Quaternion(1,0,0,0));
        rb.PublishTransform(); State(key + "/move-publish",rb);
        go.SetActive(true); Physics.SyncTransforms(); State(key + "/activated",rb);
        rb.useGravity = false;
        Physics.Simulate(.02f); State(key + "/step",rb);
        if (!go.scene.IsValid()) return;
        rb.linearVelocity = new Vector3(1,2,3); rb.angularVelocity = Vector3.up;
        go.SetActive(false); Edit(go); Physics.SyncTransforms(); State(key + "/deactivated",rb);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("LOADING_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics.simulationMode = SimulationMode.Script;
        Physics.autoSyncTransforms = false;
        Physics.gravity = Vector3.zero;
        var scene = new Dictionary<string,GameObject>();
        foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            scene.Add(rb.gameObject.name,rb.gameObject);
        // All original Transform snapshots and pre-read edits precede any Rigidbody property.
        foreach (string kind in new[] { "scene", "nested" })
        foreach (bool active in new[] { false,true })
        foreach (bool kinematic in new[] { false,true })
        foreach (bool parented in new[] { false,true })
        for (int sequence = 0; sequence < 6; sequence++)
        {
            string key = Name(kind,active,kinematic,parented,sequence); var go = scene[key];
            Authored(key,go);
            if (sequence == 1 || sequence == 2 || sequence == 4 || sequence == 5) Edit(go);
        }
        // The no-sync boundaries are observed before the explicitly synchronized cases.
        foreach (int sequence in new[] { 0,1,3,4,2,5 })
        foreach (string kind in new[] { "scene", "nested" })
        foreach (bool active in new[] { false,true })
        foreach (bool kinematic in new[] { false,true })
        foreach (bool parented in new[] { false,true })
        {
            string key = Name(kind,active,kinematic,parented,sequence);
            First(key,scene[key],sequence);
        }
        // No simulation or deactivation until every scene body's first-read boundary was recorded.
        foreach (string kind in new[] { "scene", "nested" })
        foreach (bool active in new[] { false,true })
        foreach (bool kinematic in new[] { false,true })
        foreach (bool parented in new[] { false,true })
        for (int sequence = 0; sequence < 6; sequence++)
        {
            string key = Name(kind,active,kinematic,parented,sequence); Follow(key,scene[key]);
        }
        foreach (var go in scene.Values) UnityEngine.Object.DestroyImmediate(go);
        foreach (string kind in new[] { "asset", "instance" })
        foreach (bool active in new[] { false,true })
        foreach (bool kinematic in new[] { false,true })
        foreach (bool parented in new[] { false,true })
        for (int sequence = 0; sequence < 6; sequence++)
        {
            string key = Name(kind,active,kinematic,parented,sequence);
            var asset = Resources.Load<GameObject>(key);
            var root = kind == "asset" ? asset : UnityEngine.Object.Instantiate(asset);
            var go = Body(root); Authored(key,go);
            if (sequence == 1 || sequence == 2 || sequence == 4 || sequence == 5) Edit(go);
            First(key,go,sequence); Follow(key,go);
            if (kind == "instance") UnityEngine.Object.DestroyImmediate(root);
        }
        File.WriteAllText(output,rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
