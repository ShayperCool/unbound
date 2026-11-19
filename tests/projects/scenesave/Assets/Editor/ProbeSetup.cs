using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// `-executeMethod ProbeSetup.Create` authors the probe's files once in the Editor (committed): two
/// scenes, a prefab with an instance in the first scene, ScriptableObject and material assets. The
/// captures then edit and save copies of them (Assets/Tests). `-executeMethod ProbeSetup.Defaults`
/// writes a new empty scene holding one GameObject per built-in component class (named after the
/// class, the component just added) to the file SCENESAVE_DEFAULTS names: the documents the release
/// writes for new objects.
/// </summary>
public static class ProbeSetup
{
    static readonly string[] s_Data = { "DataUnsaved", "DataSaved", "DataQuit", "DataPlay", "DataPlayDirty", "DataPlaySaved" };
    static readonly string[] s_Materials = { "MatEdit", "MatQuit", "MatPlay", "MatPlaySaved" };

    public static void Defaults()
    {
        var output = Environment.GetEnvironmentVariable("SCENESAVE_DEFAULTS");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var types = TypeCache.GetTypesDerivedFrom<Component>()
            .Where(t => !t.IsAbstract && t.IsPublic && !t.IsGenericType && !typeof(MonoBehaviour).IsAssignableFrom(t)
                        && t.Assembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal)
                        && t.GetCustomAttribute<ObsoleteAttribute>() == null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
        int added = 0;
        foreach (var type in types)
        {
            if (type == typeof(Transform)) continue;
            var go = new GameObject(type.FullName);
            try { if (go.AddComponent(type) != null) added++; }
            catch (Exception e) { Debug.Log($"scenesave: {type.FullName} not added: {e.Message}"); }
        }
        new GameObject("UnityEngine.GameObject");
        EditorSceneManager.SaveScene(scene, "Assets/Defaults.unity");
        if (!string.IsNullOrEmpty(output)) File.Copy("Assets/Defaults.unity", output, true);
        AssetDatabase.DeleteAsset("Assets/Defaults.unity");
        Debug.Log($"scenesave: {added} default components written");
    }

    public static void Create()
    {
        foreach (var folder in new[] { "Scenes", "Prefabs", "Data", "Materials" })
            if (!AssetDatabase.IsValidFolder("Assets/" + folder)) AssetDatabase.CreateFolder("Assets", folder);

        foreach (var name in s_Data)
        {
            var data = ScriptableObject.CreateInstance<ProbeData>();
            data.words.Add("one");
            AssetDatabase.CreateAsset(data, $"Assets/Data/{name}.asset");
        }
        foreach (var name in s_Materials)
        {
            var material = new Material(Shader.Find("Standard")) { color = new Color(0.5f, 0.5f, 0.5f, 1f) };
            AssetDatabase.CreateAsset(material, $"Assets/Materials/{name}.mat");
        }
        AssetDatabase.SaveAssets();

        var thing = new GameObject("Thing");
        var holder = thing.AddComponent<Holder>();
        holder.count = 5;
        var part = new GameObject("Part");
        part.transform.SetParent(thing.transform, false);
        part.transform.localPosition = new Vector3(0f, 1f, 0f);
        part.AddComponent<BoxCollider>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(thing, "Assets/Prefabs/Thing.prefab");
        Object.DestroyImmediate(thing);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var root = new GameObject("Root");
        var rootHolder = root.AddComponent<Holder>();
        rootHolder.data = AssetDatabase.LoadAssetAtPath<ProbeData>("Assets/Data/DataUnsaved.asset");
        rootHolder.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MatEdit.mat");
        rootHolder.words.Add("first");
        var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        child.name = "Child";
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = new Vector3(1f, 0f, 0f);
        var graphObject = new GameObject("Graph");
        var graph = graphObject.AddComponent<Graph>();
        graph.main = new Circle { name = "authored", radius = 1f };
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.transform.position = new Vector3(0f, 0f, 5f);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Base.unity");

        var second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Lone");
        EditorSceneManager.SaveScene(second, "Assets/Scenes/Second.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("scenesave: probe files created");
    }
}
