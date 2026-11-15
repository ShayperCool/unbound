using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

// A persistent call's stored target type name: tidied by its serialization callbacks and filled
// from the target when empty. Fields are reached by their serialized names.
public class EventTypeNameProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("EventTypeNameProbe").AddComponent<EventTypeNameProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    [Serializable] public class Nested { public UnityEvent inner = new UnityEvent(); public int tail = 7; }
    public UnityEvent onFire = new UnityEvent();
    public Nested nested = new Nested();
    public int fired;
    public void Fire() { fired++; }

    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>" }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("EVENT_TYPE_NAME_OUT"); if (string.IsNullOrEmpty(path)) return;
        if (name != "EventTypeNameProbe") return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    // Private fields are declared on base classes too (UnityEventBase).
    static FieldInfo Declared(object owner, string name)
    {
        for (var type = owner.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Fields | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(owner.GetType().Name, name);
    }
    static object Field(object owner, string name) => Declared(owner, name).GetValue(owner);
    static void SetField(object owner, string name, object value) => Declared(owner, name).SetValue(owner, value);
    static IList Calls(UnityEventBase e) => (IList)Field(Field(e, "m_PersistentCalls"), "m_Calls");
    static object Call(UnityEventBase e, int i) => Calls(e)[i];
    static string Stored(object call) => (string)Field(call, "m_TargetAssemblyTypeName");
    static string Getter(object call) => (string)call.GetType().GetProperty("targetAssemblyTypeName", Fields).GetValue(call);

    static readonly string[] Names =
    {
        "EventTypeNameProbe, Assembly-CSharp, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null",
        "UnityEngine.Rigidbody, UnityEngine.PhysicsModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
        "UnityEngine.Rigidbody, UnityEngine.PhysicsModule",
        "A.B, Lib, Culture=en, Version=2.0.0.0",
        "A.B, Lib, PublicKeyToken=0123456789abcdef",
        "A.B, UnityEngine.Thing, Version=1.0.0.0",
        "A.B, MyUnityEngine.Module",
        "A.B, Other, UnityEngine.XModule",
        "Plain",
        "",
    };

    // The names contain no quotes or backslashes, so they are written as JSON strings directly.
    static string Json(string typeName) =>
        "{\"onFire\":{\"m_PersistentCalls\":{\"m_Calls\":[{\"m_TargetAssemblyTypeName\":\"" + typeName +
        "\",\"m_MethodName\":\"Fire\",\"m_Mode\":1,\"m_CallState\":2}]}}}";

    void Record()
    {
        Put("interface", typeof(ISerializationCallbackReceiver).IsAssignableFrom(typeof(UnityEventBase).Assembly.GetType("UnityEngine.Events.PersistentCall")));
        for (int i = 0; i < Names.Length; i++)
        {
            // Deserialization: the stored name of a call read from JSON.
            onFire = new UnityEvent();
            JsonUtility.FromJsonOverwrite(Json(Names[i]), this);
            Put("read." + i + ".count", Calls(onFire).Count);
            Put("read." + i, Stored(Call(onFire, 0)));
            // Serialization of a live call whose stored name is untidy: the output and the live field.
            SetField(Call(onFire, 0), "m_TargetAssemblyTypeName", Names[i]);
            string json = JsonUtility.ToJson(this);
            Put("write." + i + ".json", json.Contains(Names[i] + "\"") ? "untidy" : json.Contains("\"m_TargetAssemblyTypeName\"") ? "tidy" : "absent");
            Put("write." + i + ".live", Stored(Call(onFire, 0)));
        }

        // A nested event inside a serializable class, written then read back into a copy.
        nested = new Nested();
        JsonUtility.FromJsonOverwrite("{\"nested\":{\"inner\":{\"m_PersistentCalls\":{\"m_Calls\":[{\"m_TargetAssemblyTypeName\":\"X.Y, Z, Version=3.0.0.0\",\"m_MethodName\":\"Fire\"}]}},\"tail\":9}}", this);
        Put("nested.read", Stored(Call(nested.inner, 0)));
        Put("nested.tail", nested.tail);

        // The getter fills an empty stored name from the target, and only then.
        onFire = new UnityEvent();
        JsonUtility.FromJsonOverwrite(Json("Placeholder, Lib"), this);
        var call = Call(onFire, 0);
        SetField(call, "m_Target", this);
        Put("getter.nonEmpty", Getter(call));
        Put("getter.nonEmpty.stored", Stored(call));
        SetField(call, "m_TargetAssemblyTypeName", "");
        Put("getter.empty", Getter(call));
        Put("getter.empty.stored", Stored(call));
        SetField(call, "m_TargetAssemblyTypeName", null);
        Put("getter.null", Getter(call));
        SetField(call, "m_Target", GetComponent<Transform>());
        SetField(call, "m_TargetAssemblyTypeName", null);
        Put("getter.engineType", Getter(call));
        SetField(call, "m_Target", null);
        SetField(call, "m_TargetAssemblyTypeName", null);
        Put("getter.noTarget", Getter(call) ?? "<null>");
        Put("getter.noTarget.stored", Stored(call) ?? "<null>");

        // A clone copies the call through serialization; the source's live name is tidied too.
        onFire = new UnityEvent();
        JsonUtility.FromJsonOverwrite(Json("Placeholder, Lib"), this);
        SetField(Call(onFire, 0), "m_Target", this);
        SetField(Call(onFire, 0), "m_TargetAssemblyTypeName", Names[0]);
        var clone = Instantiate(this);
        clone.name = "clone";
        Put("clone.stored", Stored(Call(clone.onFire, 0)));
        Put("clone.source", Stored(Call(onFire, 0)));
        // The target name does not decide the call: an untidy or foreign name still invokes.
        SetField(Call(onFire, 0), "m_TargetAssemblyTypeName", "Unknown.Type, Nowhere, Version=9.9.9.9");
        fired = 0;
        onFire.Invoke();
        Put("invoke.foreignName.fired", fired);
        Put("invoke.foreignName.stored", Stored(Call(onFire, 0)));
        DestroyImmediate(clone.gameObject);
    }
}
