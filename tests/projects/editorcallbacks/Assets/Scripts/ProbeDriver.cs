using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Records the runtime initialization phases and scene events, then, frame by frame,
/// what PrefabUtility and SerializedProperty answer in Play mode for the scene's prefab
/// instances, the prefab assets, runtime clones, edited instances and the objects of scenes
/// loaded with LoadScene (single), LoadScene (additive) and LoadSceneAsync.</summary>
public class ProbeDriver : MonoBehaviour
{
    int m_Step;
    int m_AsyncDone = -1;
    AsyncOperation m_Async;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void SubsystemRegistration()
    {
        Phase("SubsystemRegistration");
        SceneManager.sceneLoaded += (scene, mode) =>
            ProbeLog.Event("sceneLoaded").Add("scene", scene.name).Add("mode", mode.ToString()).Frame().Write();
        SceneManager.sceneUnloaded += scene =>
            ProbeLog.Event("sceneUnloaded").Add("scene", scene.name).Frame().Write();
        SceneManager.activeSceneChanged += (from, to) =>
            ProbeLog.Event("activeSceneChanged").Add("from", from.name).Add("to", to.name).Frame().Write();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void AfterAssembliesLoaded() => Phase("AfterAssembliesLoaded");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    static void BeforeSplashScreen() => Phase("BeforeSplashScreen");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeSceneLoad() => Phase("BeforeSceneLoad");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterSceneLoad()
    {
        Phase("AfterSceneLoad");
        var driver = new GameObject("ProbeDriver");
        DontDestroyOnLoad(driver);
        driver.AddComponent<ProbeDriver>();
    }

    static void Phase(string name) =>
        ProbeLog.Event("RuntimeInitializeOnLoadMethod").Add("phase", name).Add("active", SceneManager.GetActiveScene().name).Frame().Write();

    void Update()
    {
        m_Step++;
        switch (m_Step)
        {
            case 1:
                QueryScene("main", SceneManager.GetSceneByName("Main"));
#if UNITY_EDITOR
                foreach (var path in new[] { "Assets/Prefabs/Plain.prefab", "Assets/Prefabs/Outer.prefab", "Assets/Prefabs/PlainVariant.prefab", "Assets/Models/Lods.fbx" })
                    QueryHierarchy("asset", AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var plain = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Plain.prefab");
                var outer = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Outer.prefab");
                var assetClone = Instantiate(plain);
                assetClone.name = "AssetClone";
                var instanceClone = Instantiate(GameObject.Find("PlainA"));
                instanceClone.name = "InstanceClone";
                var outerClone = Instantiate(outer);
                outerClone.name = "OuterClone";
                var instantiated = (GameObject)PrefabUtility.InstantiatePrefab(plain);
                instantiated.name = "InstantiatedPrefab";
                foreach (var clone in new[] { assetClone, instanceClone, outerClone, instantiated })
                    QueryHierarchy("clone", clone);
                Edit();
#endif
                break;
            case 2:
#if UNITY_EDITOR
                foreach (var name in new[] { "PlainA", "VariantA", "OuterA" })
                    QueryHierarchy("edited", GameObject.Find(name));
#endif
                break;
            case 3:
                Load("Second", LoadSceneMode.Single);
                break;
            case 5:
                QueryScene("second", SceneManager.GetSceneByName("Second"));
                Load("Additive", LoadSceneMode.Additive);
                break;
            case 7:
                QueryScene("additive", SceneManager.GetSceneByName("Additive"));
                ProbeLog.Event("LoadSceneAsync").Add("scene", "Async").Add("mode", "Single").Frame().Write();
                ProbeLog.AsyncRequested();
                m_Async = SceneManager.LoadSceneAsync("Async", LoadSceneMode.Single);
                m_Async.completed += _ =>
                {
                    m_AsyncDone = m_Step;
                    ProbeLog.Event("completed").Add("scene", "Async").Frame().Write();
                };
                break;
            default:
                if (m_Step > 7 && m_AsyncDone >= 0 && m_Step > m_AsyncDone)
                {
                    QueryScene("async", SceneManager.GetSceneByName("Async"));
                    Finish("done");
                }
                else if (m_Step > 600)
                {
                    Finish("timeout");
                }
                break;
        }
    }

    static void Load(string scene, LoadSceneMode mode)
    {
        ProbeLog.Event("LoadScene").Add("scene", scene).Add("mode", mode.ToString()).Frame().Write();
        SceneManager.LoadScene(scene, mode);
    }

    void Finish(string how)
    {
        ProbeLog.Event(how).Frame().Write();
        enabled = false;
#if UNITY_EDITOR
        if (ProbeLog.Enabled) EditorApplication.Exit(0);
#endif
    }

    static void QueryScene(string phase, Scene scene)
    {
        ProbeLog.Event("scene").Add("phase", phase).Add("scene", scene.name).Add("loaded", scene.isLoaded).Add("roots", scene.rootCount).Frame().Write();
        if (!scene.IsValid()) return;
        foreach (var root in scene.GetRootGameObjects()) QueryHierarchy(phase, root);
    }

    static void QueryHierarchy(string phase, GameObject go)
    {
        if (go == null)
        {
            ProbeLog.Event("missing").Add("phase", phase).Write();
            return;
        }
#if UNITY_EDITOR
        PrefabQueries.Query(phase, go);
        foreach (var component in go.GetComponents<Component>()) PrefabQueries.Query(phase, component);
#endif
        foreach (Transform child in go.transform) QueryHierarchy(phase, child.gameObject);
    }

#if UNITY_EDITOR
    /// <summary>Instance edits in Play mode: a plain field write, a Transform write, a
    /// SerializedObject apply, and an Undo-recorded write with RecordPrefabInstancePropertyModifications.</summary>
    static void Edit()
    {
        var plainA = GameObject.Find("PlainA");
        plainA.GetComponent<ProbeMarker>().value = 99;
        plainA.transform.Find("Child").localPosition = new Vector3(1, 2, 3);

        var variant = new SerializedObject(GameObject.Find("VariantA").GetComponent<ProbeMarker>());
        variant.FindProperty("value").intValue = 55;
        variant.ApplyModifiedProperties();

        var outer = GameObject.Find("OuterA").GetComponent<ProbeMarker>();
        Undo.RecordObject(outer, "probe edit");
        outer.label = "recorded";
        PrefabUtility.RecordPrefabInstancePropertyModifications(outer);
        ProbeLog.Event("edited").Frame().Write();
    }
#endif
}

#if UNITY_EDITOR
/// <summary>Every PrefabUtility query and the SerializedProperty prefab state of one object.</summary>
static class PrefabQueries
{
    static readonly Dictionary<int, string> s_Handles = new Dictionary<int, string>();

    /// <summary>An object: an asset by path and hierarchy path, a scene object by scene path.</summary>
    static string Describe(Object o)
    {
        if (o == null) return null;
        if (!EditorUtility.IsPersistent(o)) return ProbeLog.PathOf(o);
        var go = o as GameObject ?? (o as Component)?.gameObject;
        var path = go != null ? go.name : o.name;
        if (go != null)
            for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
        return "asset:" + AssetDatabase.GetAssetPath(o) + ":/" + path + (o is Component ? ":" + o.GetType().Name : go == null ? ":" + o.GetType().Name : "");
    }

    /// <summary>A prefab instance handle by the first object it was the handle of.</summary>
    static string Handle(Object handle, Object of)
    {
        if (handle == null) return null;
        if (!s_Handles.TryGetValue(handle.GetInstanceID(), out var first))
            s_Handles[handle.GetInstanceID()] = first = Describe(of);
        return handle.GetType().Name + (EditorUtility.IsPersistent(handle) ? " asset" : " scene") + " of " + first;
    }

#pragma warning disable 618
    internal static void Query(string phase, Object o)
    {
        var r = ProbeLog.Event("prefab").Add("phase", phase).Add("object", Describe(o))
            .Add("partOfAsset", PrefabUtility.IsPartOfPrefabAsset(o))
            .Add("partOfInstance", PrefabUtility.IsPartOfPrefabInstance(o))
            .Add("partOfAny", PrefabUtility.IsPartOfAnyPrefab(o))
            .Add("partOfNonAssetInstance", PrefabUtility.IsPartOfNonAssetPrefabInstance(o))
            .Add("partOfRegular", PrefabUtility.IsPartOfRegularPrefab(o))
            .Add("partOfModel", PrefabUtility.IsPartOfModelPrefab(o))
            .Add("partOfVariant", PrefabUtility.IsPartOfVariantPrefab(o))
            .Add("partOfImmutable", PrefabUtility.IsPartOfImmutablePrefab(o));
        if (o is GameObject go)
            r.Add("anyInstanceRoot", PrefabUtility.IsAnyPrefabInstanceRoot(go))
                .Add("outermostInstanceRoot", PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                .Add("addedGameObject", PrefabUtility.IsAddedGameObjectOverride(go));
        else
            r.Add("addedComponent", PrefabUtility.IsAddedComponentOverride(o));
        r.Add("handle", Handle(PrefabUtility.GetPrefabInstanceHandle(o), o))
            .Add("outermost", Describe(PrefabUtility.GetOutermostPrefabInstanceRoot(o)))
            .Add("nearest", Describe(PrefabUtility.GetNearestPrefabInstanceRoot(o)))
            .Add("source", Describe(PrefabUtility.GetCorrespondingObjectFromSource(o)))
            .Add("original", Describe(PrefabUtility.GetCorrespondingObjectFromOriginalSource(o)))
            .Add("assetType", PrefabUtility.GetPrefabAssetType(o).ToString())
            .Add("status", PrefabUtility.GetPrefabInstanceStatus(o).ToString())
            .Add("nearestPath", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o))
            .Add("prefabType", PrefabUtility.GetPrefabType(o).ToString());
        Properties(r, o);
        r.Write();
    }
#pragma warning restore 618

    /// <summary>The top-level serialized properties that are overrides, default overrides and
    /// part of an instance; the overridden children of overridden properties.</summary>
    static void Properties(ProbeLog.Record r, Object o)
    {
        var overrides = new List<string>();
        var defaults = new List<string>();
        var instantiated = new List<string>();
        var so = new SerializedObject(o);
        var p = so.GetIterator();
        var enter = true;
        while (p.Next(enter))
        {
            enter = false;
            if (p.isInstantiatedPrefab) instantiated.Add(p.propertyPath);
            if (p.isDefaultOverride) defaults.Add(p.propertyPath);
            if (!p.prefabOverride) continue;
            overrides.Add(p.propertyPath);
            if (!p.hasChildren || p.isArray || p.propertyType == SerializedPropertyType.String) continue;
            var child = p.Copy();
            var end = p.GetEndProperty();
            if (!child.Next(true)) continue;
            while (!SerializedProperty.EqualContents(child, end))
            {
                if (child.prefabOverride) overrides.Add(child.propertyPath);
                if (!child.Next(false)) break;
            }
        }
        r.Add("overrides", overrides).Add("defaultOverrides", defaults).Add("instantiated", instantiated);
    }
}
#endif
