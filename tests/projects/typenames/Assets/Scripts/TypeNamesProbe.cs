using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>What the runtime names types and assemblies; see README.md.</summary>
public class TypeNamesProbe : MonoBehaviour
{
    public class Nested { }

    static readonly (string Key, Type Type)[] Types =
    {
        ("bool", typeof(bool)),
        ("string", typeof(string)),
        ("intArray", typeof(int[])),
        ("intArray2", typeof(int[,])),
        ("listInt", typeof(List<int>)),
        ("dictStringListInt", typeof(Dictionary<string, List<int>>)),
        ("hashSetInt", typeof(HashSet<int>)),
        ("uri", typeof(Uri)),
        ("xmlDocument", typeof(System.Xml.XmlDocument)),
        ("specialFolder", typeof(Environment.SpecialFolder)),
        ("nullableInt", typeof(int?)),
        ("funcInt", typeof(Func<int>)),
        ("enumerable", typeof(System.Linq.Enumerable)),
        ("binaryFormatter", typeof(System.Runtime.Serialization.Formatters.Binary.BinaryFormatter)),
        ("listOpen", typeof(List<>)),
        ("game", typeof(TypeNamesProbe)),
        ("gameNested", typeof(Nested)),
        ("listGame", typeof(List<TypeNamesProbe>)),
        ("vector3", typeof(Vector3)),
        ("rigidbody", typeof(Rigidbody)),
        ("listVector3", typeof(List<Vector3>)),
    };

    static readonly string[] Lookups =
    {
        "System.Boolean, mscorlib",
        "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
        "System.Boolean, System.Private.CoreLib",
        "System.Boolean",
        "System.Collections.Generic.HashSet`1[[System.Int32, mscorlib]], System.Core",
        "System.Collections.Generic.HashSet`1[[System.Int32, mscorlib]], mscorlib",
        "System.Uri, System",
        "UnityEngine.Rigidbody, UnityEngine.PhysicsModule",
        "UnityEngine.Rigidbody, UnityEngine.CoreModule",
        "TypeNamesProbe, Assembly-CSharp",
    };

    readonly List<string> m_Lines = new List<string>();

    void Add(string key, object value)
    {
        string json = value is bool b ? (b ? "true" : "false")
            : "\"" + Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        m_Lines.Add("{\"k\": \"" + key + "\", \"v\": " + json + "}");
    }

    void Start()
    {
        foreach (var (key, type) in Types)
        {
            Add(key + ".aqn", type.AssemblyQualifiedName ?? "null");
            Add(key + ".fullName", type.FullName ?? "null");
            Add(key + ".toString", type.ToString());
            Add(key + ".assembly", type.Assembly.FullName);
            Add(key + ".assemblyName", type.Assembly.GetName().Name);
            Add(key + ".assemblyNameFull", type.Assembly.GetName().FullName);
            Add(key + ".assemblyToString", type.Assembly.ToString());
            // The name read back (what persisted names are for).
            Add(key + ".roundTrip", type.AssemblyQualifiedName != null && Type.GetType(type.AssemblyQualifiedName, false) == type);
        }
        for (int i = 0; i < Lookups.Length; i++)
        {
            var found = Type.GetType(Lookups[i], false);
            Add("lookup" + i + ".name", Lookups[i]);
            Add("lookup" + i + ".found", found != null ? found.FullName : "null");
        }
        Add("complete", true);
        string dir = Environment.GetEnvironmentVariable("TYPE_NAMES_OUT");
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "values.jsonl"), m_Lines);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
        UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
