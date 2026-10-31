using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class SceneSettingsProbe
{
    static readonly StringBuilder rows = new StringBuilder();
    static readonly List<GameObject> made = new List<GameObject>();
    public static readonly List<string> order = new List<string>();
    static int nativeContacts;
    static bool NativeEditor => typeof(Physics).Assembly.GetName().Name == "UnityEngine.PhysicsModule";
    static void Record(string key, params float[] values)
    {
        rows.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i != 0) rows.Append(',');
            rows.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }
        rows.Append("]}\n");
    }
    static void RecordOrder(string key)
    {
        rows.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < order.Count; i++)
        {
            if (i != 0) rows.Append(',');
            rows.Append('"').Append(order[i]).Append('"');
        }
        rows.Append("]}\n");
    }
    static GameObject Box(string name, int type, Vector3 position)
    {
        var go = new GameObject(name); made.Add(go); go.transform.position = position;
        go.AddComponent<BoxCollider>();
        if (type != 0)
        {
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = type == 1;
            rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezeAll;
            rb.sleepThreshold = 0;
        }
        go.AddComponent<TriggerFlagEvents>();
        return go;
    }
    static int Sleep(GameObject go)
    {
        var rb = go.GetComponent<Rigidbody>();
        return rb != null ? (rb.IsSleeping() ? 1 : 0) : -1;
    }
    static void Hold(GameObject go)
    {
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null && rb.isKinematic) rb.MovePosition(go.transform.position);
    }
#if UNITY_EDITOR
    static void ContactEvent(PhysicsScene scene, Unity.Collections.NativeArray<ContactPairHeader>.ReadOnly headers)
    {
        for (int i = 0; i < headers.Length; i++)
        for (int j = 0; j < headers[i].pairCount; j++)
            nativeContacts += headers[i].GetContactPair(j).contactCount;
    }
#endif
    static int Contacts(Collider c)
    {
        if (NativeEditor) return nativeContacts;
        var type = typeof(Physics).Assembly.GetType("Unbound.Runtime.PhysicsWorld");
        var list = new List<ContactPoint>();
        type.GetMethod("CollectContacts",System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null,new object[] { c,null,list });
        return list.Count;
    }
    static void RunCases()
    {
        Physics.simulationMode = SimulationMode.Script; Physics.gravity = Vector3.zero;
        foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            rb.gameObject.SetActive(false);
#if UNITY_EDITOR
        if (NativeEditor) Physics.ContactEvent += ContactEvent;
#endif
        foreach (string kind in new[] { "dd", "dd-sleep", "kd", "kk", "ks", "filtered-dd" })
        {
            int aType = kind == "kd" || kind == "kk" || kind == "ks" ? 1 : 2;
            int bType = kind == "kk" ? 1 : kind == "ks" ? 0 : 2;
            var a = Box("a",aType,Vector3.zero);
            var b = Box("b",bType,new Vector3(.8f,0,0));
            var ac = a.GetComponent<Collider>(); var bc = b.GetComponent<Collider>();
            var ae = a.GetComponent<TriggerFlagEvents>(); var be = b.GetComponent<TriggerFlagEvents>();
            Physics.SyncTransforms();
            if (kind == "dd-sleep") { a.GetComponent<Rigidbody>().Sleep(); b.GetComponent<Rigidbody>().Sleep(); }
            if (kind == "filtered-dd") Physics.IgnoreCollision(ac,bc,true);
            foreach (string phase in new[] { "ss", "ts", "tt", "st", "ss-again", "a-roundtrip",
                "both-roundtrip", "ts-again", "st-swap", "ts-swap", "disable-enable", "disabled",
                "enabled", "ignore", "restore", "final-ss", "tt-new", "tt-out", "tt-in", "from-tt" })
            {
                switch (phase)
                {
                    case "ts": ac.isTrigger=true; break;
                    case "tt": bc.isTrigger=true; break;
                    case "st": ac.isTrigger=false; break;
                    case "ss-again": bc.isTrigger=false; break;
                    case "a-roundtrip": ac.isTrigger=true; ac.isTrigger=false; break;
                    case "both-roundtrip": ac.isTrigger=true; bc.isTrigger=true; ac.isTrigger=false; bc.isTrigger=false; break;
                    case "ts-again": ac.isTrigger=true; break;
                    case "st-swap": ac.isTrigger=false; bc.isTrigger=true; break;
                    case "ts-swap": ac.isTrigger=true; bc.isTrigger=false; break;
                    case "disable-enable": ac.enabled=false; ac.enabled=true; break;
                    case "disabled": ac.enabled=false; break;
                    case "enabled": ac.enabled=true; break;
                    case "ignore": Physics.IgnoreCollision(ac,bc,true); break;
                    case "restore": Physics.IgnoreCollision(ac,bc,false); break;
                    case "final-ss": ac.isTrigger=false; bc.isTrigger=false; break;
                    case "tt-new": ac.isTrigger=true; bc.isTrigger=true; break;
                    case "tt-out": b.transform.position=new Vector3(3,0,0); Physics.SyncTransforms(); break;
                    case "tt-in": b.transform.position=new Vector3(.8f,0,0); Physics.SyncTransforms(); break;
                    case "from-tt": ac.isTrigger=false; bc.isTrigger=false; break;
                }
                for (int step = 0; step < 2; step++)
                {
                    string key = kind + "/" + phase + "/" + step;
                    ae.Clear(); be.Clear(); order.Clear(); nativeContacts=0;
                    if (kind != "dd-sleep") { Hold(a); Hold(b); }
                    Record(key + "/before",ac.isTrigger ? 1:0,bc.isTrigger ? 1:0,ac.enabled ? 1:0,Sleep(a),Sleep(b));
                    Physics.Simulate(.02f);
                    Record(key + "/events",ae.enter,ae.stay,ae.exit,ae.te,ae.ts,ae.tx,
                        be.enter,be.stay,be.exit,be.te,be.ts,be.tx,ae.contacts,be.contacts);
                    RecordOrder(key + "/order");
                    Record(key + "/raw-contacts",Contacts(ac));
                    Record(key + "/after",Sleep(a),Sleep(b));
                    var ap=a.transform.position; var bp=b.transform.position;
                    Record(key + "/motion",ap.x,ap.y,ap.z,bp.x,bp.y,bp.z);
                }
            }
            foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            made.Clear(); Physics.SyncTransforms();
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output=Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        int exit=0;
        try { RunCases(); }
        catch (Exception error) { Debug.LogException(error); exit=1; }
        File.WriteAllText(output,rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(exit);
#else
        Application.Quit(exit);
#endif
    }
}

public sealed class TriggerFlagEvents : MonoBehaviour
{
    public int enter,stay,exit,te,ts,tx,contacts;
    public void Clear() { enter=stay=exit=te=ts=tx=contacts=0; }
    void OnCollisionEnter(Collision c) { enter++; contacts+=c.contactCount; SceneSettingsProbe.order.Add(name+".CE"); }
    void OnCollisionStay(Collision c) { stay++; contacts+=c.contactCount; SceneSettingsProbe.order.Add(name+".CS"); }
    void OnCollisionExit(Collision c) { exit++; SceneSettingsProbe.order.Add(name+".CX"); }
    void OnTriggerEnter(Collider c) { te++; SceneSettingsProbe.order.Add(name+".TE"); }
    void OnTriggerStay(Collider c) { ts++; SceneSettingsProbe.order.Add(name+".TS"); }
    void OnTriggerExit(Collider c) { tx++; SceneSettingsProbe.order.Add(name+".TX"); }
}
