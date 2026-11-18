using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// Play mode cases: which unreferenced assets a scene load unloads, and whether Resources and
/// AssetBundle asynchronous loads wait in the scene loads' queue. Records JSON lines {"k", "v"}.
public class SceneUnloadProbe : MonoBehaviour
{
    public static Action<bool> SetForceUnload;
    public static bool Finished;
    static string Out => Environment.GetEnvironmentVariable("SCENE_UNLOAD_OUT");
    // Held by a static field through every case: never unused.
    static UnloadData s_Held;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (Out == null) return;
        var go = new GameObject("SceneUnloadProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<SceneUnloadProbe>();
    }

    static void Put(string key, string value)
    {
        var line = new StringBuilder("{\"k\": \"").Append(key).Append("\", \"v\": \"");
        foreach (char c in value)
        {
            if (c == '"' || c == '\\') line.Append('\\').Append(c);
            else if (c < ' ') line.Append("\\u").Append(((int)c).ToString("x4"));
            else line.Append(c);
        }
        File.AppendAllText(Out, line.Append("\"}\n").ToString());
    }

    static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    /// The loaded object of type T with this instance ID (FindObjectsOfTypeAll loads nothing).
    static T Find<T>(int id) where T : Object
    {
        foreach (var o in Resources.FindObjectsOfTypeAll<T>()) if (o.GetInstanceID() == id) return o;
        return null;
    }

    int resData, resMat, resText, sceneData, sceneMat, plainData, plainMat;

    void Track()
    {
        var data = Resources.Load<UnloadData>("ResData");
        data.value = 99;
        resData = data.GetInstanceID();
        var mat = Resources.Load<Material>("ResMat");
        mat.color = Color.red;
        resMat = mat.GetInstanceID();
        resText = Resources.Load<TextAsset>("ResText").GetInstanceID();
        // Loaded and left as they are.
        plainData = Resources.Load<UnloadData>("ResPlain").GetInstanceID();
        plainMat = Resources.Load<Material>("ResPlainMat").GetInstanceID();
        var keeper = GameObject.Find("Keeper").GetComponent<AssetHolder>();
        keeper.data.value = 55;
        keeper.material.color = Color.green;
        sceneData = keeper.data.GetInstanceID();
        sceneMat = keeper.material.GetInstanceID();
        s_Held.value = 88;
    }

    void Observe(string key)
    {
        var data = Find<UnloadData>(resData);
        Put(key + "/res.data", data == null ? "unloaded" : "loaded " + data.value);
        var mat = Find<Material>(resMat);
        Put(key + "/res.mat", mat == null ? "unloaded" : "loaded " + F(mat.color.r) + "," + F(mat.color.g));
        Put(key + "/res.text", Find<TextAsset>(resText) == null ? "unloaded" : "loaded");
        Put(key + "/res.plain", Find<UnloadData>(plainData) == null ? "unloaded" : "loaded");
        Put(key + "/res.plainmat", Find<Material>(plainMat) == null ? "unloaded" : "loaded");
        var sdata = Find<UnloadData>(sceneData);
        Put(key + "/scene.data", sdata == null ? "unloaded" : "loaded " + sdata.value);
        var smat = Find<Material>(sceneMat);
        Put(key + "/scene.mat", smat == null ? "unloaded" : "loaded " + F(smat.color.r) + "," + F(smat.color.g));
        Put(key + "/held", Find<UnloadData>(s_Held.GetInstanceID()) == null ? "unloaded" : "loaded " + s_Held.value);
    }

    void Reload(string key)
    {
        var data = Resources.Load<UnloadData>("ResData");
        Put(key + "/reload.res.data", data.value + (data.GetInstanceID() == resData ? " same id" : " new id"));
        var mat = Resources.Load<Material>("ResMat");
        Put(key + "/reload.res.mat", F(mat.color.r) + "," + F(mat.color.g) + (mat.GetInstanceID() == resMat ? " same id" : " new id"));
    }

    IEnumerator Frames(string key)
    {
        for (int i = 1; i <= 3; i++) { yield return null; Observe(key + "/f" + i); }
    }

    IEnumerator OpenA()
    {
        SceneManager.LoadScene("A", LoadSceneMode.Single);
        for (int i = 0; i < 6; i++) yield return null;
    }

    IEnumerator UnloadCase(string name, Func<AsyncOperation> load)
    {
        yield return OpenA();
        Track();
        Observe(name + "/before");
        bool loaded = false;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onLoaded = (s, m) => { if (s.name == "B" && !loaded) { loaded = true; Observe(name + "/sceneLoaded"); } };
        SceneManager.sceneLoaded += onLoaded;
        var op = load();
        if (op != null)
        {
            while (!op.isDone) yield return null;
            Observe(name + "/done");
        }
        yield return Frames(name);
        SceneManager.sceneLoaded -= onLoaded;
        Reload(name);
        var b = SceneManager.GetSceneByName("B");
        if (b.isLoaded && SceneManager.sceneCount > 1)
        {
            var unload = SceneManager.UnloadSceneAsync(b);
            while (!unload.isDone) yield return null;
        }
    }

    IEnumerator QueueCase(string name, bool resourceFirst)
    {
        yield return OpenA();
        Application.backgroundLoadingPriority = ThreadPriority.Normal;
        ResourceRequest resource = null;
        if (resourceFirst) resource = Resources.LoadAsync<UnloadData>("ResQueue1");
        int start = Time.frameCount;
        var scene = SceneManager.LoadSceneAsync("B", LoadSceneMode.Additive);
        scene.allowSceneActivation = false;
        if (!resourceFirst) resource = Resources.LoadAsync<UnloadData>("ResQueue2");
        var bundle = AssetBundle.LoadFromFileAsync(Path.Combine(Application.streamingAssetsPath, "things"));
        var done = new Dictionary<string, int>();
        void Note(string what, AsyncOperation op) { if (op.isDone && !done.ContainsKey(what)) done[what] = Time.frameCount - start; }
        for (int i = 0; i < 10; i++)
        {
            yield return null;
            Note("resource", resource); Note("bundle", bundle);
        }
        Put(name + "/held/scene.progress", F(scene.progress));
        Put(name + "/held/resource", done.ContainsKey("resource") ? "done" : "waiting");
        Put(name + "/held/bundle", done.ContainsKey("bundle") ? "done" : "waiting");
        scene.allowSceneActivation = true;
        for (int i = 0; i < 600 && !(scene.isDone && resource.isDone && bundle.isDone); i++)
        {
            yield return null;
            Note("resource", resource); Note("bundle", bundle); Note("scene", scene);
        }
        foreach (var what in new[] { "scene", "resource", "bundle" })
            // How many frames after the release depends on the machine's load: not recorded.
            Put(name + "/released/" + what, !done.TryGetValue(what, out var f) ? "not done" : f <= 10 ? "done while held" : "done after release");
        Put(name + "/resource.asset", resource.asset is UnloadData d ? d.name + " " + d.value : "null");
        var loadedBundle = bundle.assetBundle;
        if (loadedBundle != null)
        {
            // An asset load from the bundle while another scene load is held.
            int start2 = Time.frameCount;
            var held = SceneManager.LoadSceneAsync("B", LoadSceneMode.Additive);
            held.allowSceneActivation = false;
            var asset = loadedBundle.LoadAssetAsync<TextAsset>("assets/bundled/notes.txt");
            int assetDone = -1;
            for (int i = 0; i < 10; i++) { yield return null; if (asset.isDone && assetDone < 0) assetDone = Time.frameCount - start2; }
            Put(name + "/held/bundle.asset", assetDone < 0 ? "waiting" : "done");
            held.allowSceneActivation = true;
            while (!(held.isDone && asset.isDone)) { yield return null; if (asset.isDone && assetDone < 0) assetDone = Time.frameCount - start2; }
            Put(name + "/released/bundle.asset", assetDone <= 10 ? "done while held" : "done after release");
            Put(name + "/bundle.asset.text", asset.asset is TextAsset t ? t.text.Trim() : "null");
            loadedBundle.Unload(true);
        }
        else Put(name + "/bundle", "null");
        for (int n = SceneManager.sceneCount - 1; n >= 0; n--)
        {
            var s = SceneManager.GetSceneAt(n);
            if (s.name == "B") { var u = SceneManager.UnloadSceneAsync(s); while (!u.isDone) yield return null; }
        }
    }

    IEnumerator Start()
    {
        s_Held = Resources.Load<UnloadData>("ResHeld");
        for (int i = 0; i < 5; i++) yield return null;
        yield return UnloadCase("single", () => { SceneManager.LoadScene("B", LoadSceneMode.Single); return null; });
        yield return UnloadCase("singleasync", () => SceneManager.LoadSceneAsync("B", LoadSceneMode.Single));
        yield return UnloadCase("additive", () => SceneManager.LoadSceneAsync("B", LoadSceneMode.Additive));
        SetForceUnload?.Invoke(false);
        yield return UnloadCase("noforce", () => SceneManager.LoadSceneAsync("B", LoadSceneMode.Single));
        SetForceUnload?.Invoke(true);
        yield return UnloadCase("explicit", () => Resources.UnloadUnusedAssets());
        yield return QueueCase("queue.sceneFirst", false);
        yield return QueueCase("queue.resourceFirst", true);
        Put("complete", "1");
        Finished = true;
    }
}
