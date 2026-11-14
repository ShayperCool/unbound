using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneValueProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("SceneValueProbe").AddComponent<SceneValueProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));
    static Scene Handle(int handle)
    {
        object boxed = default(Scene);
        typeof(Scene).GetField("m_Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(boxed, handle);
        return (Scene)boxed;
    }
    void Start()
    {
        string path = Environment.GetEnvironmentVariable("SCENE_VALUE_OUT"); if (string.IsNullOrEmpty(path)) return;
        try { output = new StreamWriter(path); Record(); StartCoroutine(Live()); }
        catch (Exception ex) { Debug.LogException(ex); output?.Dispose(); EditorApplication.Exit(1); }
    }
    void Record()
    {
        Put("interfaces", typeof(Scene).GetInterfaces().Length);
        Put("equatable", typeof(IEquatable<Scene>).IsAssignableFrom(typeof(Scene)));
        int[] handles = { 0, 17, 29, 17, int.MinValue, int.MaxValue, -1 };
        var values = new Scene[handles.Length];
        var set = new HashSet<Scene>(); var map = new Dictionary<Scene, int>();
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Handle(handles[i]);
            Put("set.add."+i, set.Add(values[i])); map[values[i]] = i;
            Put("hash."+i, values[i].GetHashCode());
            Put("null."+i, values[i].Equals(null)); Put("wrong."+i, values[i].Equals((object)handles[i]));
            Scene? optional = values[i]; Put("nullable."+i, values[i].Equals((object)optional));
        }
        for (int i = 0; i < values.Length; i++)
        {
            for (int j = 0; j < values.Length; j++)
            {
                Put("pair."+i+"."+j+".object", values[i].Equals((object)values[j]));
                Put("pair."+i+"."+j+".operator", values[i] == values[j]);
                Put("pair."+i+"."+j+".different", values[i] != values[j]);
                Put("pair."+i+"."+j+".generic", EqualityComparer<Scene>.Default.Equals(values[i], values[j]));
            }
            Put("map.lookup."+i, map[Handle(handles[i])]); Put("set.contains."+i, set.Contains(Handle(handles[i])));
        }
        Put("set.count", set.Count); Put("map.count", map.Count);
        Put("set.remove", set.Remove(Handle(17))); Put("set.removed-copy", set.Contains(values[3]));
        Put("map.remove", map.Remove(Handle(29))); Put("map.removed-copy", map.ContainsKey(values[2]));
        var original = values[1]; object copy = original;
        typeof(Scene).GetField("m_Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(copy, 31);
        Put("copy.original", original.GetHashCode()); Put("copy.changed", ((Scene)copy).GetHashCode()); Put("copy.equal", original.Equals(copy));
    }
    IEnumerator Live()
    {
        var first = SceneManager.CreateScene("ValueFirst"); var second = SceneManager.CreateScene("ValueSecond");
        var copy = first; var retained = second;
        var map = new Dictionary<Scene, int> { { first, 31 }, { second, 47 } };
        var set = new HashSet<Scene> { first, second, copy };
        Put("live.first.valid", first.IsValid()); Put("live.second.valid", second.IsValid());
        Put("live.copy.equal", first == copy); Put("live.distinct.equal", first == second);
        Put("live.generic.copy", EqualityComparer<Scene>.Default.Equals(first, copy));
        Put("live.generic.distinct", EqualityComparer<Scene>.Default.Equals(first, second));
        Put("live.copy.lookup", map[copy]); Put("live.fresh.lookup", map[SceneManager.GetSceneByName("ValueFirst")]);
        Put("live.set.count", set.Count); Put("live.hash.first", first.GetHashCode() == first.handle);
        Put("live.hash.second", second.GetHashCode() == second.handle);
        yield return SceneManager.UnloadSceneAsync(second);
        try
        {
            Put("unloaded.valid", retained.IsValid()); Put("unloaded.copy.equal", retained == second);
            Put("unloaded.distinct.equal", retained == first); Put("unloaded.lookup", map[retained]);
            Put("unloaded.set", set.Contains(retained)); Put("unloaded.hash", retained.GetHashCode() == retained.handle);
            Put("complete", 1); output.Dispose(); EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); output.Dispose(); EditorApplication.Exit(1); }
    }
}
