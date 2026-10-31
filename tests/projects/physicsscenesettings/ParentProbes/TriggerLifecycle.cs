using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public static class SceneSettingsProbe
{
    static readonly List<GameObject> made = new List<GameObject>();
    static readonly StringBuilder rows = new StringBuilder();
    static GameObject Box(string name, int type, bool active, Vector3 position)
    {
        var go = new GameObject(name); made.Add(go); go.SetActive(active);
        go.transform.position = position; go.AddComponent<BoxCollider>();
        if (type != 0)
        {
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = type == 1;
            rb.useGravity = false; rb.linearDamping = rb.angularDamping = 0;
        }
        go.AddComponent<TriggerLifecycleEvents>(); return go;
    }
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
    static int Sleep(GameObject go)
    {
        var rb = go.GetComponent<Rigidbody>();
        return rb != null ? (rb.IsSleeping() ? 1 : 0) : -1;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None)) rb.gameObject.SetActive(false);
        Physics.simulationMode = SimulationMode.Script; Physics.gravity = Vector3.zero;
        foreach (string kind in new[] { "kk", "ks", "kd", "ss" })
        foreach (string life in new[] { "fresh", "activate", "clone" })
        foreach (string action in new[] { "still", "sleep", "move", "disable-enter" })
        {
            int at = kind == "ss" ? 0 : 1, bt = kind == "kk" ? 1 : kind == "kd" ? 2 : 0;
            var a = Box("a", at, life != "activate", Vector3.zero);
            var b = Box("b", bt, true, new Vector3(.8f,0,0));
            a.GetComponent<Collider>().isTrigger = true;
            if (life == "activate") a.SetActive(true);
            if (life == "clone")
            {
                var c = UnityEngine.Object.Instantiate(a); made.Add(c); a.SetActive(false); a = c;
            }
            var ae = a.GetComponent<TriggerLifecycleEvents>(); var be = b.GetComponent<TriggerLifecycleEvents>();
            ae.disableOnEnter = action == "disable-enter";
            Physics.SyncTransforms();
            for (int step = 0; step < 12; step++)
            {
                if (action == "sleep" && step == 1 && bt == 2) b.GetComponent<Rigidbody>().Sleep();
                if (action == "sleep" && step == 5 && bt == 2) b.GetComponent<Rigidbody>().WakeUp();
                if (action == "move" && step == 3 && at != 0) a.GetComponent<Rigidbody>().MovePosition(new Vector3(.01f,0,0));
                if (step == 8) { a.transform.position = new Vector3(-3,0,0); Physics.SyncTransforms(); }
                if (step == 10) { a.transform.position = Vector3.zero; Physics.SyncTransforms(); }
                ae.Clear(); be.Clear(); int beforeA = Sleep(a), beforeB = Sleep(b);
                Physics.Simulate(.02f);
                string key = kind + "/" + life + "/" + action + "/" + step;
                Record(key + "/events", ae.enter,ae.stay,ae.exit,be.enter,be.stay,be.exit);
                Record(key + "/order", ae.order,be.order);
                Record(key + "/sleep", beforeA,beforeB,Sleep(a),Sleep(b));
            }
            foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            made.Clear(); Physics.SyncTransforms();
        }
        File.WriteAllText(output, rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}

public sealed class TriggerLifecycleEvents : MonoBehaviour
{
    public int enter,stay,exit,order;
    public bool disableOnEnter;
    public void Clear() { enter=stay=exit=order=0; }
    void OnTriggerEnter(Collider other)
    {
        enter++; order=order*10+1;
        if (disableOnEnter) GetComponent<Collider>().enabled=false;
    }
    void OnTriggerStay(Collider other) { stay++; order=order*10+2; }
    void OnTriggerExit(Collider other) { exit++; order=order*10+3; }
}
