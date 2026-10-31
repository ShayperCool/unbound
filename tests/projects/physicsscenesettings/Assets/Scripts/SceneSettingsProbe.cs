using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

public static class SceneSettingsProbe
{
    static readonly StringBuilder rows = new StringBuilder();
    static readonly List<GameObject> made = new List<GameObject>();
    static PhysicsMaterial mat;
    public static void Record(string key, params float[] values)
    {
        rows.Append("{\"key\":\"").Append(key).Append("\",\"value\":[");
        for (int i = 0; i < values.Length; i++)
        {
            if (i != 0) rows.Append(',');
            rows.Append(values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        rows.Append("]}\n");
    }
    static GameObject Box(string name, Vector3 position, Vector3 size, int type, bool active = true)
    {
        var go = new GameObject(name); made.Add(go); go.SetActive(active);
        go.transform.position = position;
        go.AddComponent<BoxCollider>().size = size;
        go.GetComponent<Collider>().sharedMaterial = mat;
        if (type != 0)
        {
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = type == 1;
            rb.sleepThreshold = 0; rb.linearDamping = rb.angularDamping = 0;
        }
        go.AddComponent<SceneSettingsEvents>();
        return go;
    }
    static void Clean()
    {
        foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        made.Clear(); Physics.SyncTransforms();
    }
    static readonly Dictionary<int,int> nativeContacts = new Dictionary<int,int>();
#if UNITY_EDITOR
    static void ContactEvent(PhysicsScene scene, Unity.Collections.NativeArray<ContactPairHeader> headers)
    {
        for (int i = 0; i < headers.Length; i++)
        for (int j = 0; j < headers[i].pairCount; j++)
        {
            var pair = headers[i].GetContactPair(j);
            foreach (int id in new[] { pair.colliderInstanceID,pair.otherColliderInstanceID })
            {
                nativeContacts.TryGetValue(id,out int count); nativeContacts[id] = count + pair.contactCount;
            }
        }
    }
#endif
    static int GeneratedContacts(Collider collider)
    {
#if UNITY_EDITOR
        return nativeContacts.TryGetValue(collider.GetInstanceID(),out int count) ? count : 0;
#else
        // The public ContactEvent API is currently a stub. Inspect the engine's real PhysX
        // contact stream for this supplemental backend measurement, without faking that API.
        var type = typeof(Physics).Assembly.GetType("Unbound.Runtime.PhysicsWorld");
        var list = new List<ContactPoint>();
        type.GetMethod("CollectContacts",System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null,new object[] { collider,null,list });
        return list.Count;
#endif
    }
    static void State(string key, Rigidbody rb)
    {
        var p = rb.position; var q = rb.rotation; var v = rb.linearVelocity; var a = rb.angularVelocity;
        Record(key + "/motion", p.x,p.y,p.z,q.x,q.y,q.z,q.w,v.x,v.y,v.z,a.x,a.y,a.z);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output = Environment.GetEnvironmentVariable("SCENE_SETTINGS_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics.simulationMode = SimulationMode.Script;
#if UNITY_EDITOR
        Physics.ContactEvent += ContactEvent;
#endif
        Record("globals", Physics.defaultSolverIterations, Physics.defaultSolverVelocityIterations,
            Physics.defaultContactOffset, Physics.defaultMaxAngularSpeed, Physics.improvedPatchFriction ? 1 : 0);
        // Imported bodies, activation and clones keep creation defaults independent of allocation.
        foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            made.Add(rb.gameObject);
            Record("loaded/" + rb.name, rb.solverIterations,rb.solverVelocityIterations,rb.maxAngularVelocity,rb.GetComponent<Collider>().contactOffset);
            rb.gameObject.SetActive(true);
            var clone = UnityEngine.Object.Instantiate(rb.gameObject); made.Add(clone);
            var crb = clone.GetComponent<Rigidbody>();
            Record("clone/" + rb.name, crb.solverIterations,crb.solverVelocityIterations,crb.maxAngularVelocity,clone.GetComponent<Collider>().contactOffset);
        }
        Clean();
        mat = new PhysicsMaterial("scene-settings") { staticFriction = .7f, dynamicFriction = .35f, bounciness = 0 };
        foreach (string kind in new[] { "slide", "tilt", "stack", "joint" })
        {
            Physics.gravity = new Vector3(0,-9.81f,0);
            Box("support",new Vector3(0,-.5f,0),new Vector3(40,1,40),0);
            var body = Box("body",new Vector3(0,.5f,0),Vector3.one,2).GetComponent<Rigidbody>();
            if (kind == "slide") body.linearVelocity = new Vector3(3,0,1.7f);
            if (kind == "tilt") body.rotation = Quaternion.Euler(9,13,7);
            Rigidbody top = null;
            if (kind == "stack") top = Box("top",new Vector3(.2f,1.48f,.1f),Vector3.one,2).GetComponent<Rigidbody>();
            if (kind == "joint")
            {
                body.position = new Vector3(0,3,0);
                var joint = body.gameObject.AddComponent<FixedJoint>(); joint.autoConfigureConnectedAnchor = false;
                joint.anchor = Vector3.zero; joint.connectedAnchor = new Vector3(.2f,3,0);
            }
            Physics.SyncTransforms();
            for (int step = 1; step <= 12; step++)
            {
                Physics.Simulate(.02f); State(kind + "/" + step,body);
                if (top != null) State(kind + "/top/" + step,top);
            }
            Clean();
        }
        Physics.gravity = Vector3.zero;
        foreach (string pair in new[] { "kk", "ks", "kd", "ss" })
        foreach (bool trigger in new[] { false, true })
        foreach (string lifetime in new[] { "fresh", "activate", "clone" })
        {
            int aType = pair == "ss" ? 0 : 1, bType = pair == "kk" ? 1 : pair == "kd" ? 2 : 0;
            var a = Box("a",Vector3.zero,Vector3.one,aType,lifetime != "activate");
            var b = Box("b",new Vector3(.8f,0,0),Vector3.one,bType);
            a.GetComponent<Collider>().isTrigger = trigger;
            if (lifetime == "activate") a.SetActive(true);
            if (lifetime == "clone")
            {
                var c = UnityEngine.Object.Instantiate(a); made.Add(c); a.SetActive(false); a = c;
            }
            Physics.SyncTransforms();
            string key = "pair/" + pair + "/" + trigger + "/" + lifetime;
            for (int step = 0; step < 3; step++)
            {
                if (step == 2) { a.transform.position = new Vector3(-3,0,0); Physics.SyncTransforms(); }
                if (Environment.GetEnvironmentVariable("SCENE_SETTINGS_QUERY_CONTACTS") == "1" &&
                    !trigger && (pair == "kk" || pair == "ks") && step < 2)
                    a.GetComponent<Rigidbody>().MovePosition(new Vector3(.01f * (step + 1),0,0));
                var ae = a.GetComponent<SceneSettingsEvents>(); var be = b.GetComponent<SceneSettingsEvents>();
                ae.Clear(); be.Clear(); nativeContacts.Clear(); Physics.Simulate(.02f);
                Record(key + "/" + step + "/events",ae.enter,ae.stay,ae.exit,ae.triggerEnter,ae.triggerStay,ae.triggerExit,
                    be.enter,be.stay,be.exit,be.triggerEnter,be.triggerStay,be.triggerExit);
                // Contact point order and normal direction are deliberately not sorted into a fake correspondence.
                Record(key + "/" + step + "/contacts",ae.contacts,be.contacts);
                if (Environment.GetEnvironmentVariable("SCENE_SETTINGS_QUERY_CONTACTS") == "1")
                {
                    int ac = GeneratedContacts(a.GetComponent<Collider>());
                    int bc = GeneratedContacts(b.GetComponent<Collider>());
                    Record(key + "/" + step + "/query-contacts",ac,bc);
                }
                if (bType == 2) State(key + "/" + step,b.GetComponent<Rigidbody>());
            }
            Clean();
        }
        UnityEngine.Object.DestroyImmediate(mat);
        File.WriteAllText(output,rows.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
public sealed class SceneSettingsEvents : MonoBehaviour
{
    public int enter,stay,exit,triggerEnter,triggerStay,triggerExit,contacts;
    public void Clear() { enter=stay=exit=triggerEnter=triggerStay=triggerExit=contacts=0; }
    void OnCollisionEnter(Collision c) { enter++; contacts += c.contactCount; }
    void OnCollisionStay(Collision c) { stay++; contacts += c.contactCount; }
    void OnCollisionExit(Collision c) { exit++; contacts += c.contactCount; }
    void OnTriggerEnter(Collider c) { triggerEnter++; }
    void OnTriggerStay(Collider c) { triggerStay++; }
    void OnTriggerExit(Collider c) { triggerExit++; }
}
