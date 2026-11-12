using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEditor;
public sealed class SerializedBindingControl : MonoBehaviour
{
    static readonly List<string> Rows = new List<string>();
    static void Put(string k, object v) => Rows.Add("{\"k\":\"" + k + "\",\"v\":\"" + Convert.ToString(v, CultureInfo.InvariantCulture) + "\"}");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin() { if (Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT") != null) new GameObject("Binding driver").AddComponent<SerializedBindingControl>(); }
    void Start()
    {
        try
        {
            foreach (string kind in new[] { "box", "sphere", "capsule" })
            {
                var go = new GameObject(kind); go.layer = 27;
                Collider c = kind == "box" ? (Collider)go.AddComponent<BoxCollider>() : kind == "sphere" ? go.AddComponent<SphereCollider>() : (Collider)go.AddComponent<CapsuleCollider>();
                var material = new PhysicsMaterial("Binding material");
                c.includeLayers = 1 << 3; c.excludeLayers = 1 << 4; c.layerOverridePriority = 3;
                c.providesContacts = false; c.isTrigger = false; c.enabled = true;
                using (var so = new SerializedObject(c))
                {
                    foreach (string name in new[] { "m_Material", "m_IncludeLayers", "m_ExcludeLayers", "m_LayerOverridePriority", "m_IsTrigger", "m_ProvidesContacts", "m_Enabled" })
                    {
                        var p = so.FindProperty(name); Put(kind + "/" + name + "/found", p != null);
                        if (p == null) continue;
                        Put(kind + "/" + name + "/type", p.type); Put(kind + "/" + name + "/propertyType", p.propertyType);
                    }
                    Physics.SyncTransforms(); Put(kind + "/solid.before", Physics.CheckSphere(Vector3.zero, 0.1f, 1 << 27, QueryTriggerInteraction.Ignore));
                    var enabled = so.FindProperty("m_Enabled");
                    if (enabled != null)
                    {
                        so.FindProperty("m_Material").objectReferenceValue = material;
                        so.FindProperty("m_IncludeLayers").intValue = 1 << 6; so.FindProperty("m_ExcludeLayers").intValue = 1 << 7;
                        so.FindProperty("m_LayerOverridePriority").intValue = 9; so.FindProperty("m_IsTrigger").boolValue = true;
                        so.FindProperty("m_ProvidesContacts").boolValue = true; enabled.boolValue = false;
                        Put(kind + "/buffer.beforeApply", c.enabled); Put(kind + "/modified", so.hasModifiedProperties);
                        Put(kind + "/apply", so.ApplyModifiedProperties());
                        Put(kind + "/material", c.sharedMaterial == material); Put(kind + "/include", c.includeLayers.value); Put(kind + "/exclude", c.excludeLayers.value);
                        Put(kind + "/priority", c.layerOverridePriority); Put(kind + "/trigger", c.isTrigger); Put(kind + "/contacts", c.providesContacts); Put(kind + "/enabled", c.enabled);
                        Physics.SyncTransforms(); Put(kind + "/disabled.query", Physics.CheckSphere(Vector3.zero, 0.1f, 1 << 27, QueryTriggerInteraction.Collide));
                        c.enabled = true; Put(kind + "/buffer.stale", enabled.boolValue); so.Update(); Put(kind + "/buffer.refreshed", so.FindProperty("m_Enabled").boolValue);
                        Physics.SyncTransforms(); Put(kind + "/trigger.query.ignore", Physics.CheckSphere(Vector3.zero, 0.1f, 1 << 27, QueryTriggerInteraction.Ignore));
                        Put(kind + "/trigger.query.collide", Physics.CheckSphere(Vector3.zero, 0.1f, 1 << 27, QueryTriggerInteraction.Collide));
                        Put(kind + "/apply.unchanged", so.ApplyModifiedProperties());
                    }
                    if (c is BoxCollider box)
                    {
                        foreach (string name in new[] { "m_Size", "m_Center" })
                        {
                            var p = so.FindProperty(name); Put("box/" + name + "/found", p != null);
                            if (p != null) { Put("box/" + name + "/type", p.type); Put("box/" + name + "/propertyType", p.propertyType); }
                        }
                        var size = so.FindProperty("m_Size"); var center = so.FindProperty("m_Center");
                        if (size != null && center != null)
                        {
                            size.vector3Value = new Vector3(2, 4, 6); center.vector3Value = new Vector3(4, 0, 0);
                            Put("box/geometry.before", box.center.x); Put("box/geometry.apply", so.ApplyModifiedProperties());
                            Put("box/size.x", box.size.x); Put("box/size.y", box.size.y); Put("box/size.z", box.size.z); Put("box/center.x", box.center.x);
                            Physics.SyncTransforms(); Put("box/query.old", Physics.CheckSphere(Vector3.zero, 0.1f, 1 << 27, QueryTriggerInteraction.Collide)); Put("box/query.new", Physics.CheckSphere(new Vector3(4, 0, 0), 0.1f, 1 << 27, QueryTriggerInteraction.Collide));
                        }
                    }
                }
                DestroyImmediate(go); DestroyImmediate(material);
            }
            var first = new GameObject("Multi box"); var second = new GameObject("Multi sphere");
            var boxMulti = first.AddComponent<BoxCollider>(); var sphereMulti = second.AddComponent<SphereCollider>();
            boxMulti.isTrigger = false; sphereMulti.isTrigger = true;
            using (var multi = new SerializedObject(new UnityEngine.Object[] { boxMulti, sphereMulti }))
            {
                var p = multi.FindProperty("m_IsTrigger"); Put("multi/found", p != null);
                if (p != null)
                {
                    Put("multi/mixed.before", p.hasMultipleDifferentValues); Put("multi/first.before", p.boolValue);
                    p.boolValue = false; Put("multi/apply", multi.ApplyModifiedProperties());
                    Put("multi/box", boxMulti.isTrigger); Put("multi/sphere", sphereMulti.isTrigger); Put("multi/mixed.after", multi.FindProperty("m_IsTrigger").hasMultipleDifferentValues);
                    var mask = multi.FindProperty("m_IncludeLayers"); Put("multi/mask.child", mask.FindPropertyRelative("m_Bits") != null);
                    mask.FindPropertyRelative("m_Bits").intValue = 1 << 13; Put("multi/mask.apply", multi.ApplyModifiedProperties()); Put("multi/mask.box", boxMulti.includeLayers.value); Put("multi/mask.sphere", sphereMulti.includeLayers.value);
                }
            }
            DestroyImmediate(first); DestroyImmediate(second);
            var one = ScriptableObject.CreateInstance<SerializedBindingSample>(); var two = ScriptableObject.CreateInstance<SerializedBindingSample>();
            one.number = 0; two.number = 7; one.vector = new Vector3(1, 2, 3); two.vector = new Vector3(9, 8, 7);
            one.bounds = new Bounds(new Vector3(1, 2, 3), Vector3.one * 2); two.bounds = new Bounds(new Vector3(9, 8, 7), Vector3.one * 4);
            using (var multi = new SerializedObject(new UnityEngine.Object[] { one, two }))
            {
                multi.FindProperty("number").intValue = 0; Put("script/equalFirst.apply", multi.ApplyModifiedProperties()); Put("script/equalFirst.second", two.number);
                multi.FindProperty("vector").FindPropertyRelative("x").floatValue = 1; Put("script/vector.apply", multi.ApplyModifiedProperties());
                Put("script/vector.second.x", two.vector.x); Put("script/vector.second.y", two.vector.y); Put("script/vector.second.z", two.vector.z);
                multi.FindProperty("bounds").FindPropertyRelative("m_Center").FindPropertyRelative("x").floatValue = 4; Put("script/nested.apply", multi.ApplyModifiedProperties());
                Put("script/nested.second.x", two.bounds.center.x); Put("script/nested.second.y", two.bounds.center.y); Put("script/nested.second.z", two.bounds.center.z); Put("script/nested.second.size", two.bounds.size.x);
                multi.FindProperty("vector").FindPropertyRelative("y").floatValue = 5; multi.FindProperty("vector").vector3Value = new Vector3(2, 3, 4); multi.FindProperty("vector").FindPropertyRelative("z").floatValue = 6;
                Put("script/whole.apply", multi.ApplyModifiedProperties()); Put("script/whole.second.x", two.vector.x); Put("script/whole.second.y", two.vector.y); Put("script/whole.second.z", two.vector.z);
            }
            DestroyImmediate(one); DestroyImmediate(two);
            Put("complete", 1);
        }
        catch (Exception e) { Put("error", e.GetType().Name); Debug.LogException(e); }
        File.WriteAllLines(Environment.GetEnvironmentVariable("SERIALIZED_BINDING_OUT"), Rows);
        EditorApplication.Exit(0);
    }
}

public sealed class SerializedBindingSample : ScriptableObject
{
    public int number;
    public Vector3 vector;
    public Bounds bounds;
}
