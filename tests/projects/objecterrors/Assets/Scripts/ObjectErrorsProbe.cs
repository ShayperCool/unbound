using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes exception types and messages into values.jsonl in $OBJECTERRORS_OUT (tests/value_probe.py
/// rows): the scene's unassigned serialized references, missing components from the GetComponent
/// family, destroyed objects (members and arguments), and argument errors of Instantiate, Destroy,
/// AddComponent, GetComponent, tags and SendMessage. Each action also records the log messages it
/// writes. Values never contain instance ids.
/// </summary>
public class ObjectErrorsProbe : MonoBehaviour
{
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Row(string key, bool value) => Row(key, value ? "true" : "false");

    static string Thrown(Exception e) => "throws " + e.GetType().FullName + ": " + e.Message;

    /// <summary>What an action returns or throws, and the logs it writes.</summary>
    static void Try(string key, Func<object> action)
    {
        logs.Clear();
        string result;
        try
        {
            var value = action();
            result = ReferenceEquals(value, null) ? "returns null" : "returns " + Describe(value);
        }
        catch (Exception e) { result = Thrown(e); }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    static void Do(string key, Action action) => Try(key, () => { action(); return "done"; });

    static string Describe(object value)
    {
        if (value is UnityEngine.Object o)
            return o == null ? (ReferenceEquals(o, null) ? "null" : "Unity-null " + o.GetType().FullName) : o.GetType().FullName + " '" + o.name + "'";
        if (value is Array array) return value.GetType().Name + "[" + array.Length + "]";
        return value.ToString();
    }

    /// <summary>The facts of an object reference: identity, Unity's null checks, its type and what its
    /// members throw.</summary>
    static void Facts(string key, UnityEngine.Object o)
    {
        Row(key + ".reference_null", ReferenceEquals(o, null));
        Row(key + ".equals_null", o == null);
        Row(key + ".bool", (bool)o);
        Row(key + ".is_null", o is null);
        Row(key + ".type", ReferenceEquals(o, null) ? "none" : o.GetType().FullName);
        if (ReferenceEquals(o, null)) return;
        Try(key + ".to_string", () => ((object)o).ToString());
        Try(key + ".name", () => o.name);
        Try(key + ".id_zero", () => o.GetInstanceID() == 0);
        Try(key + ".hide_flags", () => o.hideFlags);
        Try(key + ".equals_self", () => o.Equals(o));
        if (o is Component c)
        {
            Try(key + ".transform", () => c.transform);
            Try(key + ".game_object", () => c.gameObject);
            Try(key + ".get_component", () => c.GetComponent<Transform>());
        }
        if (o is Behaviour b) Try(key + ".enabled", () => b.enabled);
        if (o is GameObject g)
        {
            Try(key + ".transform", () => g.transform);
            Try(key + ".active_self", () => g.activeSelf);
            Do(key + ".set_active", () => g.SetActive(true));
            Try(key + ".add_component", () => g.AddComponent<BoxCollider>());
        }
        if (o is Rigidbody r) Try(key + ".mass", () => r.mass);
        if (o is Texture2D t) Try(key + ".width", () => t.width);
        if (o is Material m) Try(key + ".color", () => m.color);
        Try(key + ".instantiate", () => Instantiate(o));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OBJECTERRORS_OUT"))) return;
        new GameObject("ObjectErrorsProbe").AddComponent<ObjectErrorsProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable("OBJECTERRORS_OUT");
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try
        {
            Unassigned();
            Missing();
            Arguments();
        }
        catch (Exception e) { Row("aborted", Thrown(e)); }
        yield return Destroyed();
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static void Unassigned()
    {
        var holder = GameObject.Find("Holder").GetComponent<Holder>();
        Facts("unassigned.game_object", holder.gameObjectField);
        Facts("unassigned.transform", holder.transformField);
        Facts("unassigned.rigidbody", holder.rigidbodyField);
        Facts("unassigned.collider", holder.colliderField);
        Facts("unassigned.marker", holder.markerField);
        Facts("unassigned.data", holder.dataField);
        Facts("unassigned.material", holder.materialField);
        Facts("unassigned.texture", holder.textureField);
        Facts("unassigned.object", holder.objectField);
        Facts("unassigned.private", holder.PrivateField);
        Row("unassigned.array.length", holder.arrayField.Length.ToString());
        Facts("unassigned.array.0", holder.arrayField[0]);
        Row("unassigned.list.count", holder.listField.Count.ToString());
        Facts("unassigned.list.0", holder.listField[0]);
        Facts("unassigned.nested", holder.nestedField.target);
        Facts("unassigned.non_serialized", holder.nonSerializedField);
        Facts("unassigned.assigned", holder.assignedField);
        Row("unassigned.coalesce", ReferenceEquals(holder.gameObjectField ?? holder.gameObject, holder.gameObject));
        Facts("unassigned.behaviour_base", holder.behaviourBaseField);
        Facts("unassigned.scriptable_base", holder.scriptableBaseField);
        Facts("unassigned.component", holder.componentField);
        Facts("unassigned.behaviour", holder.behaviourField);
        Facts("unassigned.namespaced", GameObject.Find("NsHolder").GetComponent<Probes.NsHolder>().target);
        var asset = Resources.Load<DataHolder>("DataHolder");
        Facts("asset.target", asset.target);
        Row("asset.items.length", asset.items.Length.ToString());
        Facts("asset.items.0", asset.items[0]);
        Facts("asset.nested", asset.nested.target);
        var made = ScriptableObject.CreateInstance<DataHolder>();
        Facts("created.target", made.target);

        // A component added at runtime and a scriptable object created at runtime.
        var added = new GameObject("Added").AddComponent<Holder>();
        Facts("added.game_object", added.gameObjectField);
        Facts("added.array_null", null);
        Row("added.array", added.arrayField == null ? "null" : "length " + added.arrayField.Length);
        var copy = Instantiate(holder);
        Facts("instantiated.game_object", copy.gameObjectField);
        Facts("instantiated.array.0", copy.arrayField[0]);
    }

    static void Missing()
    {
        var holder = GameObject.Find("Holder");
        Facts("missing.generic", holder.GetComponent<Rigidbody>());
        Facts("missing.type", holder.GetComponent(typeof(Rigidbody)));
        Facts("missing.string", holder.GetComponent("Rigidbody"));
        Facts("missing.behaviour", holder.GetComponent<Marker>());
        Facts("missing.abstract", holder.GetComponent<Collider>());
        Facts("missing.renderer_type", holder.GetComponent(typeof(Renderer)));
        Try("missing.interface", () => holder.GetComponent<IMarker>());
        Facts("missing.children", holder.GetComponentInChildren<Rigidbody>());
        Facts("missing.parent", holder.GetComponentInParent<Rigidbody>());
        Facts("missing.from_component", holder.transform.GetComponent<Rigidbody>());
        Row("missing.try", holder.TryGetComponent<Rigidbody>(out var tried) + " " + ReferenceEquals(tried, null));
        Row("missing.components", holder.GetComponents<Rigidbody>().Length.ToString());
        GameObject sleeper = null;
        foreach (var root in holder.scene.GetRootGameObjects()) if (root.name == "Sleeper") sleeper = root;
        Facts("missing.inactive", sleeper.GetComponent<Rigidbody>());
        Row("missing.coalesce", ReferenceEquals(holder.GetComponent<Rigidbody>() ?? (Component)holder.transform, holder.transform));
    }

    static void Arguments()
    {
        var holder = GameObject.Find("Holder");
        Try("argument.instantiate_null", () => Instantiate((UnityEngine.Object)null));
        Try("argument.instantiate_generic_null", () => Instantiate<GameObject>(null));
        Try("argument.instantiate_null_parent", () => Instantiate((GameObject)null, holder.transform));
        Do("argument.destroy_null", () => Destroy(null));
        Do("argument.destroy_immediate_null", () => DestroyImmediate(null));
        Try("argument.add_null", () => holder.AddComponent((Type)null));
        Try("argument.add_string", () => holder.AddComponent(typeof(string)));
        Try("argument.add_transform", () => holder.AddComponent<Transform>());
        Try("argument.add_abstract", () => holder.AddComponent(typeof(Collider)));
        Try("argument.add_monobehaviour", () => holder.AddComponent(typeof(MonoBehaviour)));
        Try("argument.get_string_type", () => holder.GetComponent(typeof(string)));
        Try("argument.get_null_type", () => holder.GetComponent((Type)null));
        Try("argument.get_null_name", () => holder.GetComponent((string)null));
        Try("argument.get_unknown_name", () => holder.GetComponent("NoSuchComponent"));
        Try("argument.get_components_null_list", () => { holder.GetComponents<Transform>(null); return "done"; });
        Try("argument.get_components_null_type", () => holder.GetComponents((Type)null));
        Try("argument.children_null_type", () => holder.GetComponentInChildren(null));
        Try("argument.find_null", () => GameObject.Find(null));
        Try("argument.find_tag_null", () => GameObject.FindWithTag(null));
        Try("argument.find_tag_unknown", () => GameObject.FindWithTag("NoSuchTag"));
        Try("argument.compare_tag_unknown", () => holder.CompareTag("NoSuchTag"));
        Do("argument.set_tag_unknown", () => holder.tag = "NoSuchTag");
        Do("argument.send_message_required", () => holder.SendMessage("NoSuchMessage", SendMessageOptions.RequireReceiver));
        Do("argument.send_message_null", () => holder.SendMessage(null));
        Try("argument.find_objects_null", () => FindObjectsByType(null, FindObjectsSortMode.None));
        Try("argument.create_instance_null", () => ScriptableObject.CreateInstance((Type)null));
        Try("argument.create_instance_unknown", () => ScriptableObject.CreateInstance("NoSuchType"));
        Try("argument.create_instance_component", () => ScriptableObject.CreateInstance(typeof(Marker)));
        Try("argument.new_component", () => new Marker());
        Try("argument.set_parent_self", () => { holder.transform.SetParent(holder.transform); return "done"; });
        Try("argument.child_out_of_range", () => holder.transform.GetChild(3));
        Try("argument.child_negative", () => holder.transform.GetChild(-1));
        Try("argument.add_game_object_type", () => holder.AddComponent(typeof(GameObject)));
        Try("argument.add_scriptable_type", () => holder.AddComponent(typeof(Data)));
        Try("argument.add_component_type", () => holder.AddComponent(typeof(Component)));
        Try("argument.add_behaviour_type", () => holder.AddComponent(typeof(Behaviour)));
        Try("argument.get_game_object_type", () => holder.GetComponent(typeof(GameObject)));
        Try("argument.get_scriptable_type", () => holder.GetComponent(typeof(Data)));
        Try("argument.get_generic_string", () => holder.GetComponent<string>());
        Try("argument.get_generic_interface", () => holder.GetComponent<IMarker>());
        Try("argument.parent_null_type", () => holder.GetComponentInParent(null));
        Try("argument.children_all_null_type", () => holder.GetComponentsInChildren(null));
        Try("argument.parent_all_null_type", () => holder.GetComponentsInParent(null));
        Try("argument.try_null_type", () => holder.TryGetComponent(null, out var found) + " " + ReferenceEquals(found, null));
        Try("argument.children_string_type", () => holder.GetComponentInChildren(typeof(string)));
        Try("argument.components_string_type", () => holder.GetComponents(typeof(string)));
        Try("argument.find_objects_string", () => FindObjectsByType(typeof(string), FindObjectsSortMode.None));
        Try("argument.find_object_null", () => FindFirstObjectByType(null));
        Try("argument.find_all_tag_null", () => GameObject.FindGameObjectsWithTag(null));
        Try("argument.find_all_tag_unknown", () => GameObject.FindGameObjectsWithTag("NoSuchTag"));
        Try("argument.find_tag_empty", () => GameObject.FindWithTag(""));
        Try("argument.compare_tag_null", () => holder.CompareTag((string)null));
        Try("argument.compare_tag_empty", () => holder.CompareTag(""));
        Try("argument.tag_after_unknown", () => holder.tag);
        Do("argument.set_tag_null", () => holder.tag = null);
        Try("argument.create_instance_string", () => ScriptableObject.CreateInstance(typeof(string)));
        Try("argument.create_instance_abstract", () => ScriptableObject.CreateInstance(typeof(AbstractData)));
        Try("argument.create_instance_component_name", () => ScriptableObject.CreateInstance("Marker"));
        Try("argument.sibling_index_out_of_range", () => { holder.transform.SetSiblingIndex(99); return holder.transform.GetSiblingIndex(); });
    }

    static IEnumerator Destroyed()
    {
        var doomed = new GameObject("Doomed");
        var box = doomed.AddComponent<BoxCollider>();
        var marker = doomed.AddComponent<Marker>();
        var transform = doomed.transform;
        Destroy(doomed);
        yield return null;
        Facts("destroyed.game_object", doomed);
        Facts("destroyed.transform", transform);
        Facts("destroyed.collider", box);
        Facts("destroyed.marker", marker);

        var texture = new Texture2D(2, 2);
        DestroyImmediate(texture);
        Facts("destroyed.texture", texture);
        var data = ScriptableObject.CreateInstance<Data>();
        DestroyImmediate(data);
        Facts("destroyed.data", data);

        var parent = new GameObject("Parent");
        Do("destroyed.set_parent", () => parent.transform.SetParent(transform));
        Do("destroyed.destroy_again", () => Destroy(doomed));
        Do("destroyed.destroy_immediate_again", () => DestroyImmediate(doomed));
        Try("destroyed.dont_destroy", () => { DontDestroyOnLoad(doomed); return "done"; });
        Try("destroyed.instantiate_with_parent", () => Instantiate(parent, transform));
        Try("destroyed.get_component_generic", () => doomed.GetComponent<BoxCollider>());
        Try("destroyed.compare_tag", () => doomed.CompareTag("Untagged"));
        Destroy(parent);
    }
}
