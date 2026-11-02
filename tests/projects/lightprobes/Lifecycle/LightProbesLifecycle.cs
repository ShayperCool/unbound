using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Event-order witness for light probes across scene unloads, written as JSON lines to
/// <c>LIGHT_PROBES_LIFECYCLE_OUT</c> (one line per event or step, in a sequence index, then
/// <c>{"k":"complete","v":true}</c>), then quits the Editor.
///
/// A persistent observer (DontDestroyOnLoad) records, in the order they happen,
/// LightProbes.needsRetetrahedralization / tetrahedralizationCompleted / lightProbesUpdated,
/// SceneManager.sceneLoaded / sceneUnloaded / activeSceneChanged, the unload operation's
/// completed callback, and marks around each call. Every line holds the public probe state at
/// that point: LightmapSettings.lightProbes (count, countSelf, cellCount, cellCountSelf), and for
/// every scene seen so far its isValid / isLoaded / name and GetSharedLightProbesForScene.
/// Scenes and LightProbes objects are named by the order they were first seen, never by handle
/// or instance id.
///
/// Two paths: Main, Extra loaded additively, Tetrahedralize, UnloadSceneAsync("Extra");
/// then Tetrahedralize, and LoadScene("Extra", Single) replacing Main. No TetrahedralizeAsync.
/// </summary>
public class LightProbesLifecycle : MonoBehaviour
{
    static string s_Out;
    static int s_Seq;
    static string s_Step = "play";
    static readonly List<int> s_Scenes = new List<int>();
    static readonly List<Scene> s_SceneValues = new List<Scene>();
    static readonly List<LightProbes> s_Objects = new List<LightProbes>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Subscribe()
    {
        s_Out = Environment.GetEnvironmentVariable("LIGHT_PROBES_LIFECYCLE_OUT");
        if (string.IsNullOrEmpty(s_Out)) return;
        File.WriteAllText(s_Out, "");
        LightProbes.needsRetetrahedralization += () => Snapshot("needsRetetrahedralization", "");
        LightProbes.tetrahedralizationCompleted += () => Snapshot("tetrahedralizationCompleted", "");
        LightProbes.lightProbesUpdated += () => Snapshot("lightProbesUpdated", "");
        SceneManager.sceneLoaded += (scene, mode) => Snapshot("sceneLoaded", Ref(scene) + " " + mode);
        SceneManager.sceneUnloaded += scene => Snapshot("sceneUnloaded", Ref(scene));
        SceneManager.activeSceneChanged += (from, to) => Snapshot("activeSceneChanged", Ref(from) + " -> " + Ref(to));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (string.IsNullOrEmpty(s_Out)) return;
        var observer = new GameObject("LightProbesLifecycle");
        DontDestroyOnLoad(observer);
        observer.AddComponent<LightProbesLifecycle>();
    }

    static string Safe(Func<string> value)
    {
        try { return value(); }
        catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
    }

    /// <summary>A scene by the order it was first seen ("none" for the default handle).</summary>
    static string Ref(Scene scene)
    {
        if (scene.handle == 0) return "none";
        int index = s_Scenes.IndexOf(scene.handle);
        if (index < 0)
        {
            s_Scenes.Add(scene.handle);
            s_SceneValues.Add(scene);
            index = s_Scenes.Count - 1;
        }
        return "scene" + index;
    }

    static string Probes(LightProbes probes)
    {
        if (probes == null) return "null";
        int index = s_Objects.FindIndex(o => ReferenceEquals(o, probes));
        if (index < 0) { s_Objects.Add(probes); index = s_Objects.Count - 1; }
        return $"probes{index} {probes.count} {probes.countSelf} {probes.cellCount} {probes.cellCountSelf}";
    }

    static string State()
    {
        var text = new StringBuilder();
        text.Append("global ").Append(Safe(() => Probes(LightmapSettings.lightProbes)));
        text.Append(" | sceneCount ").Append(Safe(() => SceneManager.sceneCount.ToString()));
        text.Append(" loadedSceneCount ").Append(Safe(() => SceneManager.loadedSceneCount.ToString()));
        text.Append(" at [").Append(Safe(() =>
        {
            var list = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) list.Add(Ref(SceneManager.GetSceneAt(i)));
            return string.Join(" ", list);
        })).Append("]");
        text.Append(" active ").Append(Safe(() => Ref(SceneManager.GetActiveScene())));
        for (int i = 0; i < s_SceneValues.Count; i++)
        {
            var scene = s_SceneValues[i];
            text.Append(" | scene").Append(i)
                .Append(" name ").Append(Safe(() => scene.name))
                .Append(" isValid ").Append(Safe(() => scene.IsValid().ToString()))
                .Append(" isLoaded ").Append(Safe(() => scene.isLoaded.ToString()))
                .Append(" shared ").Append(Safe(() => Probes(LightProbes.GetSharedLightProbesForScene(scene))));
        }
        return text.ToString();
    }

    static string Json(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";

    static void Snapshot(string at, string args)
    {
        if (string.IsNullOrEmpty(s_Out)) return;
        string state;
        try { state = State(); }
        catch (Exception e) { state = "state " + e.GetType().Name + ": " + e.Message; }
        File.AppendAllText(s_Out, "{\"seq\":" + (s_Seq++) + ",\"step\":" + Json(s_Step) + ",\"at\":" + Json(at)
            + ",\"args\":" + Json(args) + ",\"v\":" + Json(state) + "}\n");
    }

    static void Mark(string at) => Snapshot("mark " + at, "");

    IEnumerator Start()
    {
        for (int i = 0; i < 3; i++) yield return null;
        Mark("start");

        // Path 1: an explicit unload of an additive scene.
        s_Step = "additive load Extra";
        SceneManager.LoadScene("Extra", LoadSceneMode.Additive);
        Mark("LoadScene Additive returned");
        yield return null;
        yield return null;
        Mark("two frames after the additive load");
        s_Step = "Tetrahedralize with Extra";
        LightProbes.Tetrahedralize();
        Mark("Tetrahedralize returned");
        yield return null;
        Mark("frame after Tetrahedralize");

        s_Step = "UnloadSceneAsync Extra";
        Mark("before UnloadSceneAsync");
        var unload = SceneManager.UnloadSceneAsync("Extra");
        Mark("UnloadSceneAsync returned " + (unload == null ? "null" : "operation"));
        if (unload != null)
        {
            unload.completed += _ => Snapshot("unload completed callback", "");
            while (!unload.isDone) yield return null;
        }
        Mark("unload isDone");
        yield return null;
        Mark("frame after unload");
        s_Step = "Tetrahedralize after unload";
        LightProbes.Tetrahedralize();
        Mark("Tetrahedralize returned");
        yield return null;
        Mark("frame after Tetrahedralize");

        // Path 2: a Single load replacing the scene that holds the probes.
        s_Step = "LoadScene Extra Single";
        Mark("before LoadScene Single");
        SceneManager.LoadScene("Extra", LoadSceneMode.Single);
        Mark("LoadScene Single returned");
        yield return null;
        Mark("first frame after the Single load");
        yield return null;
        Mark("second frame after the Single load");
        s_Step = "Tetrahedralize after Single";
        LightProbes.Tetrahedralize();
        Mark("Tetrahedralize returned");
        yield return null;
        Mark("frame after Tetrahedralize");

        s_Step = "done";
        string output = s_Out;
        // Shutdown can raise more probe events after their manager has been destroyed.
        // Snapshot must stop before it can query that manager or append after completion.
        s_Out = null;
        File.AppendAllText(output, "{\"k\":\"complete\",\"v\":true}\n");
        Debug.Log("light probes lifecycle witness written to " + output);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
