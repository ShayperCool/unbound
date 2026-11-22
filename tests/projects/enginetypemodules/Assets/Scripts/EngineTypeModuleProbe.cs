using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

// The assembly each engine type names: its assembly-qualified name, reached by name through the
// umbrella assembly (UnityEngine or UnityEditor), for every public type of the engine's module
// assemblies (ENGINE_TYPE_MODULES_LIST, written by ProbeRunner.List); how Type.GetType resolves a
// type named with its module, the umbrella, the core module or no assembly; and the type name a
// UnityEvent stores for an object argument of an engine type.
public class EngineTypeModuleProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("EngineTypeModuleProbe").AddComponent<EngineTypeModuleProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    public UnityEvent<UnityEngine.Object> onObject = new UnityEvent<UnityEngine.Object>();
    public void Take(UnityEngine.Object value) { }

    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>" }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("ENGINE_TYPE_MODULES_OUT"); if (string.IsNullOrEmpty(path)) return;
        if (name != "EngineTypeModuleProbe") return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static string Name(Type type) => type == null ? "<null>" : type.AssemblyQualifiedName;

    void Record()
    {
        foreach (var line in File.ReadAllLines(Environment.GetEnvironmentVariable("ENGINE_TYPE_MODULES_LIST")))
        {
            if (line.Length == 0) continue;
            var parts = line.Split('\t');
            Put("type." + parts[1], Name(Type.GetType(parts[1] + ", " + parts[0])));
        }

        // Resolution by name: the module, the umbrella, the core module, no assembly.
        foreach (var (type, module) in new[] { ("UnityEngine.Rigidbody", "UnityEngine.PhysicsModule"), ("UnityEngine.AudioSource", "UnityEngine.AudioModule"),
                                               ("UnityEngine.Transform", "UnityEngine.CoreModule"), ("UnityEditor.EditorWindow", "UnityEditor.CoreModule") })
        {
            Put("getType." + type + ".module", Name(Type.GetType(type + ", " + module)));
            Put("getType." + type + ".umbrella", Name(Type.GetType(type + ", " + module.Substring(0, module.IndexOf('.')))));
            Put("getType." + type + ".core", Name(Type.GetType(type + ", UnityEngine.CoreModule")));
            Put("getType." + type + ".bare", Name(Type.GetType(type)));
        }
        Put("assembly.rigidbody.fullName", typeof(Rigidbody).Assembly.FullName);
        Put("assembly.rigidbody.sameAsTransform", typeof(Rigidbody).Assembly == typeof(Transform).Assembly);
        Put("assembly.rigidbody.definesRigidbody", typeof(Rigidbody).Assembly.GetType("UnityEngine.Rigidbody") != null);
        Put("assembly.transform.definesRigidbody", typeof(Transform).Assembly.GetType("UnityEngine.Rigidbody") != null);

        // A UnityEvent's object argument stores its argument's type name.
        // Each component on its own object (a 2D collider cannot join a Rigidbody).
        var arguments = new UnityEngine.Object[] { transform, Holder().AddComponent<Rigidbody>(), Holder().AddComponent<AudioSource>(), Holder().AddComponent<Animator>(),
                                                   Holder().AddComponent<BoxCollider2D>(), Holder().AddComponent<UnityEngine.AI.NavMeshAgent>(), ScriptableObject.CreateInstance<EngineTypeModuleArgument>() };
        foreach (var argument in arguments)
        {
            var e = new UnityEvent<UnityEngine.Object>();
            UnityEventTools.AddObjectPersistentListener<UnityEngine.Object>(e, Take, argument);
            var json = JsonUtility.ToJson(new EventHolder { e = e });
            int at = json.IndexOf("\"m_ObjectArgumentAssemblyTypeName\":\"", StringComparison.Ordinal);
            string stored = at < 0 ? "<none>" : json.Substring(at + 36, json.IndexOf('"', at + 36) - at - 36);
            Put("event.argument." + argument.GetType().Name, stored);
        }
    }

    static GameObject Holder() => new GameObject("Argument");

    [Serializable] class EventHolder { public UnityEvent<UnityEngine.Object> e; }
}
