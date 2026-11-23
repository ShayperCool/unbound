using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// What the Editor logs and gives in Play mode for objects whose script it cannot find (a GUID no
/// file of the project has): a scene's component, loaded twice; a prefab's component, loaded and
/// instantiated; a ScriptableObject asset, by AssetDatabase and Resources; a missing-script
/// sub-asset of an asset whose main object loads; references to them from a component.
/// Each step records the log messages (type and first line) it raised.
/// </summary>
[InitializeOnLoad]
public static class MissingScriptsProbe
{
    const string Variable = "MISSING_SCRIPTS_OUT";
    static readonly StringBuilder s_Out = new StringBuilder();
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static IEnumerator<object> s_Steps;

    static MissingScriptsProbe()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            Application.logMessageReceived += (message, stack, type) => s_Logs.Add(type + ": " + message.Split('\n')[0]);
            s_Steps = Steps();
            EditorApplication.update += Tick;
        };
    }

    public static void Record()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        bool more;
        try { more = s_Steps.MoveNext(); }
        catch (Exception e) { Put("run.threw", e.GetType().FullName + ": " + e.Message.Split('\n')[0]); more = false; }
        if (more) return;
        EditorApplication.update -= Tick;
        Put("complete", true);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable(Variable), "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }

    static void Logs(string step)
    {
        Put(step + ".logs", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    static string Describe(Object o) => (object)o == null ? "none" : o == null ? "destroyed-like:" + o.GetType().Name : o.GetType().Name + ":" + o.name;

    static string Components(GameObject go) =>
        string.Join(",", go.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().Name));

    static IEnumerator<object> Steps()
    {
        yield return null;
        Logs("00.start");

        SceneManager.LoadScene("Assets/Missing/Missing.unity", LoadSceneMode.Single);
        Logs("01.scene.call");
        yield return null;
        yield return null;
        Logs("01.scene.loaded");
        var holder = GameObject.Find("Holder");
        Put("01.scene.components", holder == null ? "no holder" : Components(holder));
        var known = holder != null ? holder.GetComponent<KnownHolder>() : null;
        if (known != null)
        {
            Put("01.scene.missingAsset", Describe(known.missingAsset));
            Put("01.scene.missingSubAsset", Describe(known.missingSubAsset));
            Put("01.scene.knownMain", Describe(known.knownMain));
        }
        Logs("01.scene.read");

        SceneManager.LoadScene("Assets/Missing/Missing.unity", LoadSceneMode.Single);
        yield return null;
        yield return null;
        Logs("02.sceneAgain.loaded");

        var prefab = Resources.Load<GameObject>("MissingPrefab");
        Logs("03.prefab.load");
        Put("03.prefab.components", prefab == null ? "null" : Components(prefab));
        var instance = prefab != null ? Object.Instantiate(prefab) : null;
        Logs("03.prefab.instantiate");
        Put("03.prefab.instance.components", instance == null ? "null" : Components(instance));
        yield return null;
        Logs("03.prefab.frame");
        var again = prefab != null ? Object.Instantiate(prefab) : null;
        Logs("03.prefab.instantiateAgain");
        yield return null;
        Logs("03.prefab.frameAgain");

        var main = AssetDatabase.LoadAssetAtPath<Object>("Assets/Missing/Data/MissingMain.asset");
        Logs("04.asset.load");
        Put("04.asset.result", Describe(main));
        Put("04.asset.mainType", AssetDatabase.GetMainAssetTypeAtPath("Assets/Missing/Data/MissingMain.asset")?.Name ?? "null");
        var all = AssetDatabase.LoadAllAssetsAtPath("Assets/Missing/Data/MissingMain.asset");
        Logs("04.asset.loadAll");
        Put("04.asset.all", string.Join(",", all.Select(Describe)));
        var typed = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Missing/Data/MissingMain.asset");
        Put("04.asset.typed", Describe(typed));
        Logs("04.asset.typedLoad");

        var resource = Resources.Load("MissingResource");
        Logs("05.resource.load");
        Put("05.resource.result", Describe(resource));
        var resourceAgain = Resources.Load<ScriptableObject>("MissingResource");
        Logs("05.resource.loadAgain");
        Put("05.resource.typed", Describe(resourceAgain));

        var mixed = AssetDatabase.LoadAssetAtPath<Object>("Assets/Missing/Data/Mixed.asset");
        Logs("06.mixed.load");
        Put("06.mixed.result", Describe(mixed));
        var mixedAll = AssetDatabase.LoadAllAssetsAtPath("Assets/Missing/Data/Mixed.asset");
        Logs("06.mixed.loadAll");
        Put("06.mixed.all", string.Join(",", mixedAll.Select(Describe)));
        Put("06.mixed.representations", string.Join(",", AssetDatabase.LoadAllAssetRepresentationsAtPath("Assets/Missing/Data/Mixed.asset").Select(Describe)));
        Logs("06.mixed.representationsLoad");

        var linked = AssetDatabase.LoadAssetAtPath<KnownData>("Assets/Missing/Data/Linked.asset");
        Logs("08.linked.load");
        Put("08.linked.result", Describe(linked));
        Put("08.linked.other", linked == null ? "-" : Describe(linked.other));
        var linkedAll = AssetDatabase.LoadAllAssetsAtPath("Assets/Missing/Data/Linked.asset");
        Logs("08.linked.loadAll");
        Put("08.linked.all", string.Join(",", linkedAll.Select(Describe)));

        var twice = AssetDatabase.LoadAllAssetsAtPath("Assets/Missing/Data/Twice.asset");
        Logs("09.twice.loadAll");
        Put("09.twice.all", string.Join(",", twice.Select(Describe)));
        var twiceAgain = AssetDatabase.LoadAllAssetsAtPath("Assets/Missing/Data/Twice.asset");
        Logs("09.twice.loadAllAgain");
        Put("09.twice.allAgain", twiceAgain.Length);

        var empty = AssetDatabase.LoadAssetAtPath<Object>("Assets/Missing/Data/Empty.asset");
        Logs("10.empty.load");
        Put("10.empty.result", Describe(empty));
        var emptyAll = AssetDatabase.LoadAllAssetsAtPath("Assets/Missing/Data/Empty.asset");
        Logs("10.empty.loadAll");
        Put("10.empty.all", string.Join(",", emptyAll.Select(Describe)));
        yield return null;
        Logs("07.end");
        Put("07.again", again != null);
    }

    static void Put(string key, object value)
    {
        if (!s_Keys.Add(key)) key = key + "#" + s_Keys.Count;
        string json = value switch
        {
            bool b => b ? "true" : "false",
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => "\"" + Escape(value.ToString()) + "\"",
        };
        s_Out.Append("{\"k\":\"").Append(Escape(key)).Append("\",\"v\":").Append(json).Append("}\n");
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
}
