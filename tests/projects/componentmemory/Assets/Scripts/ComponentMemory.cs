using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

public sealed class ComponentMemory : MonoBehaviour
{
    [Serializable]
    sealed class Row
    {
        public string k, v;
    }

    [SerializeField] int[] managed = new int[16384];
    [SerializeField] Object reference;

    static readonly List<string> Rows = new();

    static void Measure(string key, Object value)
    {
        Rows.Add(JsonUtility.ToJson(new Row
        {
            k = key, v = Profiler.GetRuntimeMemorySizeLong(value).ToString(CultureInfo.InvariantCulture)
        }));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("COMPONENT_MEMORY_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Rows.Add(JsonUtility.ToJson(new Row { k = "unity", v = Application.unityVersion }));

        var owner = new GameObject("Components");
        var collider = owner.AddComponent<BoxCollider>();
        Measure("collider.add", collider);
        collider.center = new Vector3(1, 2, 3);
        collider.size = new Vector3(4, 5, 6);
        var physicsMaterial = new PhysicsMaterial();
        collider.sharedMaterial = physicsMaterial;
        Measure("collider.resizedWithMaterial", collider);
        owner.AddComponent<Rigidbody>().isKinematic = true;
        collider.isTrigger = true;
        Measure("collider.triggerWithBody", collider);
        collider.enabled = false;
        Measure("collider.disabled", collider);

        var filter = owner.AddComponent<MeshFilter>();
        Measure("filter.add", filter);
        var mesh = new Mesh();
        mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2 };
        filter.sharedMesh = mesh;
        Measure("filter.sharedMesh", filter);
        Mesh ownedMesh = filter.mesh;
        Measure("filter.ownedMesh", filter);
        filter.sharedMesh = null;
        Measure("filter.cleared", filter);

        var behaviour = owner.AddComponent<ComponentMemory>();
        var empty = owner.AddComponent<EmptyMemoryBehaviour>();
        Measure("behaviour.fields", behaviour);
        Measure("behaviour.empty", empty);
        behaviour.reference = mesh;
        Array.Fill(behaviour.managed, 1);
        Measure("behaviour.populated", behaviour);
        owner.SetActive(false);
        Measure("behaviour.inactive", behaviour);

        var clone = Object.Instantiate(owner);
        Measure("clone.collider", clone.GetComponent<BoxCollider>());
        Measure("clone.filter", clone.GetComponent<MeshFilter>());
        Measure("clone.behaviour", clone.GetComponent<ComponentMemory>());

        Object.DestroyImmediate(clone);
        Object.DestroyImmediate(owner);
        Object.DestroyImmediate(mesh);
        Object.DestroyImmediate(ownedMesh);
        Object.DestroyImmediate(physicsMaterial);
        Rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
