using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class IdentityProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder Output = new StringBuilder();
    static readonly Dictionary<int, Object> Seen = new Dictionary<int, Object>();
    static bool unique = true;
    [Serializable] public class Payload { public Object value; public LazyLoadReference<Object> lazy; }
    static void Row(string key, int value) => Output.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");
    static void Check(string key, bool value) => Row(key, value ? 1 : 0);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("INSTANCE_IDS_OUT") == null) return;
        Application.runInBackground = true;
        var go = new GameObject("IdentityProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<IdentityProbe>();
    }

    static void Observe(string key, Object obj)
    {
        if (obj == null) { Row(key+".missing",1); return; }
        int id = obj.GetInstanceID();
        if (id == 0 || Seen.TryGetValue(id, out var old) && !ReferenceEquals(old,obj)) unique = false;
        Seen[id] = obj;
        Row(key+".sign", Math.Sign(id));
        Check(key+".lookup", Resources.InstanceIDToObject(id) == obj);
        Check(key+".valid", Resources.InstanceIDIsValid(id));
        var lazy = new LazyLoadReference<Object>(id);
        Check(key+".lazy", lazy.isSet && !lazy.isBroken && lazy.asset == obj);
        var payload = new Payload { value = obj, lazy = lazy };
        var json = JsonUtility.ToJson(payload);
        var restored = JsonUtility.FromJson<Payload>(json);
        Check(key+".json", restored.value == obj && restored.lazy.instanceID == id && restored.lazy.asset == obj);
        Check(key+".jsonId", json.Contains("\"instanceID\":"+id));
    }

    static void Group(string key, GameObject go)
    {
        Observe(key+".go",go);
        if (go == null) return;
        foreach (var component in go.GetComponents<Component>())
            Observe(key+"."+component.GetType().Name,component);
    }
    static GameObject Named(string name)
    {
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            if (go.name == name) return go;
        return null;
    }
    static void Sorted(string key)
    {
        var objects = Object.FindObjectsByType<IdMarker>(FindObjectsInactive.Include,FindObjectsSortMode.InstanceID);
        bool ascending = true, boundary = true, positiveSeen = false;
        foreach (var obj in objects)
        {
            bool positive = obj.GetInstanceID() > 0;
            if (positive) positiveSeen = true;
            else if (positiveSeen) boundary = false;
        }
        for (int i=1;i<objects.Length;i++)
            if (objects[i-1].GetInstanceID() >= objects[i].GetInstanceID()) ascending=false;
        Check(key+".ascending", ascending);
        Check(key+".signBoundary",boundary && positiveSeen && objects.Length > 0 && objects[0].GetInstanceID() < 0);
    }

    IEnumerator Start()
    {
        var work = Run();
        while (true)
        {
            bool next;
            try { next = work.MoveNext(); }
            catch (Exception error)
            {
                Debug.LogException(error);
                Row("error",1);
                File.WriteAllText(Environment.GetEnvironmentVariable("INSTANCE_IDS_OUT"),Output.ToString());
                Finished = true;
                yield break;
            }
            if (!next) yield break;
            yield return work.Current;
        }
    }

    IEnumerator Run()
    {
        var initial = Named("InitialA");
        var keep = Named("InitialB");
        Group("initial.active",initial);
        Group("initial.inactive",keep);
        int initialId = initial.GetInstanceID(), keptId = keep.GetInstanceID();
        var added = initial.AddComponent<Light>();
        Observe("addedToFile",added);
        var runtime = new GameObject("Runtime");
        runtime.AddComponent<IdMarker>();
        runtime.AddComponent<BoxCollider>();
        Group("runtime",runtime);
        int runtimeId = runtime.GetInstanceID();
        var clone = Instantiate(initial); clone.name="Clone";
        Group("clone",clone);
        var prefab=Resources.Load<GameObject>("Template");
        Group("asset.prefab",prefab);
        var prefabClone=Instantiate(prefab); prefabClone.name="PrefabClone";
        Group("clone.prefab",prefabClone);
        Observe("asset.data",Resources.Load<IdData>("Data"));
        Observe("asset.text",Resources.Load<TextAsset>("Text"));
        var material=Resources.Load<Material>("Material");
        Observe("asset.material",material);
        Observe("clone.material",Instantiate(material));
        Observe("runtime.data",ScriptableObject.CreateInstance<IdData>());
        bool refused=false;
        try { new LazyLoadReference<GameObject>(initial); }
        catch (ArgumentException) { refused=true; }
        Check("positiveSceneIsNotAsset",refused);
        Sorted("initial.query");
        DontDestroyOnLoad(keep); DontDestroyOnLoad(runtime);
        Check("ddol.file.sameId", keep.GetInstanceID()==keptId);
        Check("ddol.runtime.sameId", runtime.GetInstanceID()==runtimeId);
        Group("ddol.file",keep); Group("ddol.runtime",runtime);
        SceneManager.LoadScene("Extra",LoadSceneMode.Additive);
        yield return null;
        Group("additive.active",Named("ExtraA"));
        Group("additive.inactive",Named("ExtraB"));
        var asyncLoad=SceneManager.LoadSceneAsync("Async",LoadSceneMode.Additive);
        yield return asyncLoad;
        Group("async.active",Named("AsyncA"));
        Group("async.inactive",Named("AsyncB"));
        Sorted("loaded.query");
        SceneManager.LoadScene("Single",LoadSceneMode.Single);
        yield return null;
        Group("single.active",Named("SingleA"));
        Group("single.inactive",Named("SingleB"));
        Check("ddol.file.afterSingle", keep!=null && keep.GetInstanceID()==keptId && Resources.InstanceIDToObject(keptId)==keep);
        Check("ddol.runtime.afterSingle", runtime!=null && runtime.GetInstanceID()==runtimeId);
        Check("unloaded.lookupGone", Resources.InstanceIDToObject(initialId)==null && !Resources.InstanceIDIsValid(initialId));
        Check("all.uniqueNonzero",unique);
        Row("complete",1);
        File.WriteAllText(Environment.GetEnvironmentVariable("INSTANCE_IDS_OUT"),Output.ToString());
        Finished = true;
    }
}
