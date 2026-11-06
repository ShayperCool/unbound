using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>
/// Calls representative engine API members on the main thread, on a System.Threading.Thread, on a
/// Task.Run thread-pool thread, inside a scheduled IJob that runs on a job worker, and inside
/// IJob.Run (the main thread), and records each call's outcome (ok, with booleans and null-ness of
/// the answer, or the exception's type and message) as JSON lines in <c>MAINTHREAD_PROBE_OUT</c>;
/// then exits. Every thread kind gets its own objects, made on the main thread beforehand.
/// </summary>
public class MainThreadProbe : MonoBehaviour
{
    [Serializable]
    public class Data { public int a = 1; }

    /// <summary>The objects of one thread kind.</summary>
    class Context
    {
        public GameObject Target, Child, Victim, ImmediateVictim, Destroyed;
        public Transform TargetTransform, ChildTransform;
        public Scene Scene;
        public ProbeMarker Marker;
        public TextAsset Text;
    }

    static Context s_Context;
    static string s_Kind;
    static readonly StringBuilder s_Log = new StringBuilder();
    static readonly List<(string Name, Func<object> Call)> s_Calls = new List<(string, Func<object>)>();
    static int s_MainThread;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create() => new GameObject("MainThreadProbe").AddComponent<MainThreadProbe>();

    struct CallsJob : IJob
    {
        public void Execute() => RunAll();
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

    static void Record(string call, string result)
    {
        lock (s_Log)
            s_Log.Append("{\"thread\":\"").Append(s_Kind).Append("\",\"call\":\"").Append(Escape(call))
                .Append("\",\"result\":\"").Append(Escape(result)).Append("\"}\n");
    }

    static string Text(object value)
    {
        switch (value)
        {
            case null: return "ok null";
            case bool b: return b ? "ok true" : "ok false";
            default: return "ok";
        }
    }

    static void RunAll()
    {
        Record("on main thread", Thread.CurrentThread.ManagedThreadId == s_MainThread ? "yes" : "no");
        foreach (var (name, call) in s_Calls)
        {
            string result;
            try { result = Text(call()); }
            catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
            Record(name, result);
        }
    }

    static void Add(string name, Func<object> call) => s_Calls.Add((name, call));
    static void Add(string name, Action call) => s_Calls.Add((name, () => { call(); return "void"; }));

    static IEnumerator Nothing() { yield break; }

    static void Calls()
    {
        Context C() => s_Context;

        // Object
        Add("Object.name get", () => C().Target.name);
        Add("Object.name set", () => { C().Target.name = "Target-" + s_Kind; });
        Add("Object.GetInstanceID", () => C().Target.GetInstanceID());
        Add("Object.GetHashCode", () => C().Target.GetHashCode());
        Add("Object.Equals", () => C().Target.Equals(C().Child));
        Add("Object.ToString", () => C().Target.ToString());
        Add("Object == null", () => C().Target == null);
        Add("Object == null (destroyed)", () => C().Destroyed == null);
        Add("Object implicit bool", () => (bool)C().Target);
        Add("Object.hideFlags get", () => C().Target.hideFlags);
        Add("Object.Instantiate", () => UnityEngine.Object.Instantiate(C().Child));
        Add("Object.Destroy", () => UnityEngine.Object.Destroy(C().Victim));
        Add("Object.DestroyImmediate", () => UnityEngine.Object.DestroyImmediate(C().ImmediateVictim));
        Add("Object.FindObjectsByType", () => UnityEngine.Object.FindObjectsByType<ProbeMarker>(FindObjectsSortMode.None));
        Add("Object.FindAnyObjectByType", () => UnityEngine.Object.FindAnyObjectByType<ProbeMarker>());
        Add("ScriptableObject.CreateInstance", () => ScriptableObject.CreateInstance<ProbeAsset>());

        // GameObject
        Add("new GameObject", () => new GameObject("Made"));
        Add("GameObject.Find", () => GameObject.Find("Target-main"));
        Add("GameObject.FindWithTag", () => GameObject.FindWithTag("Player"));
        Add("GameObject.AddComponent", () => C().Target.AddComponent<ProbeMarker>());
        Add("GameObject.GetComponent<T>", () => C().Target.GetComponent<ProbeMarker>());
        Add("GameObject.GetComponent(Type)", () => C().Target.GetComponent(typeof(Transform)));
        Add("GameObject.GetComponents", () => C().Target.GetComponents<Component>());
        Add("GameObject.transform", () => C().Target.transform);
        Add("GameObject.activeSelf", () => C().Target.activeSelf);
        Add("GameObject.activeInHierarchy", () => C().Target.activeInHierarchy);
        Add("GameObject.SetActive", () => C().Target.SetActive(true));
        Add("GameObject.tag get", () => C().Target.tag);
        Add("GameObject.CompareTag", () => C().Target.CompareTag("Untagged"));
        Add("GameObject.layer get", () => C().Target.layer);
        Add("GameObject.scene", () => C().Target.scene);

        // Component, Behaviour, MonoBehaviour
        Add("Component.gameObject", () => C().Marker.gameObject);
        Add("Component.transform", () => C().Marker.transform);
        Add("Component.GetComponent", () => C().Marker.GetComponent<Transform>());
        Add("Behaviour.enabled get", () => C().Marker.enabled);
        Add("Behaviour.enabled set", () => { C().Marker.enabled = true; });
        Add("MonoBehaviour.isActiveAndEnabled", () => C().Marker.isActiveAndEnabled);
        Add("MonoBehaviour.StartCoroutine", () => C().Marker.StartCoroutine(Nothing()));

        // Transform
        Add("Transform.position get", () => C().ChildTransform.position);
        Add("Transform.position set", () => { C().ChildTransform.position = new Vector3(1, 2, 3); });
        Add("Transform.localPosition get", () => C().ChildTransform.localPosition);
        Add("Transform.rotation get", () => C().ChildTransform.rotation);
        Add("Transform.eulerAngles get", () => C().ChildTransform.eulerAngles);
        Add("Transform.localScale get", () => C().ChildTransform.localScale);
        Add("Transform.forward", () => C().ChildTransform.forward);
        Add("Transform.localToWorldMatrix", () => C().ChildTransform.localToWorldMatrix);
        Add("Transform.TransformPoint", () => C().ChildTransform.TransformPoint(Vector3.one));
        Add("Transform.parent get", () => C().ChildTransform.parent);
        Add("Transform.SetParent", () => C().ChildTransform.SetParent(C().TargetTransform, false));
        Add("Transform.childCount", () => C().TargetTransform.childCount);
        Add("Transform.GetChild", () => C().TargetTransform.GetChild(0));
        Add("Transform.Find", () => C().TargetTransform.Find("Child"));

        // Time
        Add("Time.time", () => Time.time);
        Add("Time.timeAsDouble", () => Time.timeAsDouble);
        Add("Time.deltaTime", () => Time.deltaTime);
        Add("Time.unscaledTime", () => Time.unscaledTime);
        Add("Time.fixedDeltaTime", () => Time.fixedDeltaTime);
        Add("Time.timeScale", () => Time.timeScale);
        Add("Time.frameCount", () => Time.frameCount);
        Add("Time.realtimeSinceStartup", () => Time.realtimeSinceStartup);
        Add("Time.realtimeSinceStartupAsDouble", () => Time.realtimeSinceStartupAsDouble);
        Add("Time.timeSinceLevelLoad", () => Time.timeSinceLevelLoad);

        // Debug
        Add("Debug.Log", () => Debug.Log("mainthread probe log"));
        Add("Debug.isDebugBuild", () => Debug.isDebugBuild);
        Add("Debug.DrawLine", () => Debug.DrawLine(Vector3.zero, Vector3.one));

        // Math
        Add("Mathf.Sin", () => Mathf.Sin(1));
        Add("Mathf.PerlinNoise", () => Mathf.PerlinNoise(0.5f, 0.25f));
        Add("Mathf.GammaToLinearSpace", () => Mathf.GammaToLinearSpace(0.5f));
        Add("Mathf.ClosestPowerOfTwo", () => Mathf.ClosestPowerOfTwo(100));
        Add("Vector3.Cross", () => Vector3.Cross(Vector3.up, Vector3.right));
        Add("Vector3.Slerp", () => Vector3.Slerp(Vector3.up, Vector3.right, 0.5f));
        Add("Quaternion.Euler", () => Quaternion.Euler(10, 20, 30));
        Add("Quaternion.eulerAngles", () => Quaternion.AngleAxis(30, Vector3.up).eulerAngles);
        Add("Quaternion.LookRotation", () => Quaternion.LookRotation(Vector3.right));
        Add("Matrix4x4.TRS", () => Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one));
        Add("Matrix4x4.inverse", () => Matrix4x4.TRS(Vector3.one, Quaternion.identity, Vector3.one).inverse);
        Add("Color.HSVToRGB", () => Color.HSVToRGB(0.5f, 0.5f, 0.5f));
        Add("Color.linear", () => Color.gray.linear);

        // Random
        Add("Random.Range(int)", () => UnityEngine.Random.Range(0, 10));
        Add("Random.Range(float)", () => UnityEngine.Random.Range(0f, 1f));
        Add("Random.value", () => UnityEngine.Random.value);
        Add("Random.insideUnitSphere", () => UnityEngine.Random.insideUnitSphere);
        Add("Random.state get", () => UnityEngine.Random.state);
        Add("Random.InitState", () => UnityEngine.Random.InitState(1));

        // Application
        Add("Application.isPlaying", () => Application.isPlaying);
        Add("Application.isEditor", () => Application.isEditor);
        Add("Application.platform", () => Application.platform);
        Add("Application.unityVersion", () => Application.unityVersion);
        Add("Application.productName", () => Application.productName);
        Add("Application.version", () => Application.version);
        Add("Application.targetFrameRate", () => Application.targetFrameRate);
        Add("Application.persistentDataPath", () => Application.persistentDataPath);
        Add("Application.dataPath", () => Application.dataPath);
        Add("Application.streamingAssetsPath", () => Application.streamingAssetsPath);
        Add("Application.temporaryCachePath", () => Application.temporaryCachePath);

        // Screen, SystemInfo, input, cameras, prefs
        Add("Screen.width", () => Screen.width);
        Add("Screen.dpi", () => Screen.dpi);
        Add("SystemInfo.processorCount", () => SystemInfo.processorCount);
        Add("SystemInfo.deviceModel", () => SystemInfo.deviceModel);
        Add("SystemInfo.systemMemorySize", () => SystemInfo.systemMemorySize);
        Add("SystemInfo.graphicsDeviceType", () => SystemInfo.graphicsDeviceType);
        Add("Input.GetKey", () => Input.GetKey(KeyCode.Space));
        Add("Input.mousePosition", () => Input.mousePosition);
        Add("Camera.main", () => Camera.main);
        Add("PlayerPrefs.GetInt", () => PlayerPrefs.GetInt("mainthread.probe.none", 7));

        // Assets, scenes, serialization
        Add("Resources.Load", () => Resources.Load<TextAsset>("ProbeText"));
        Add("TextAsset.text", () => C().Text.text);
        Add("new Texture2D", () => new Texture2D(2, 2));
        Add("new Mesh", () => new Mesh());
        Add("Shader.PropertyToID", () => Shader.PropertyToID("_MainTex"));
        Add("Hash128.Compute", () => Hash128.Compute("probe"));
        Add("JsonUtility.ToJson", () => JsonUtility.ToJson(new Data()));
        Add("JsonUtility.FromJson", () => JsonUtility.FromJson<Data>("{\"a\":2}"));
        Add("SceneManager.GetActiveScene", () => SceneManager.GetActiveScene());
        Add("SceneManager.sceneCount", () => SceneManager.sceneCount);
        Add("Scene.name", () => C().Scene.name);

        // Networking
        Add("UnityWebRequest.Get", () => { using (UnityWebRequest.Get("http://127.0.0.1:9/")) { } });
    }

    /// <summary>Fresh objects for one thread kind, made here on the main thread.</summary>
    static Context Prepare(string kind)
    {
        var target = new GameObject("Target-" + kind);
        var child = new GameObject("Child");
        child.transform.SetParent(target.transform, false);
        var destroyed = new GameObject("Destroyed-" + kind);
        DestroyImmediate(destroyed);
        return new Context
        {
            Target = target,
            Child = child,
            TargetTransform = target.transform,
            ChildTransform = child.transform,
            Scene = target.scene,
            Marker = target.AddComponent<ProbeMarker>(),
            Victim = new GameObject("Victim-" + kind),
            ImmediateVictim = new GameObject("ImmediateVictim-" + kind),
            Destroyed = destroyed,
            Text = Resources.Load<TextAsset>("ProbeText"),
        };
    }

    void Start()
    {
        s_MainThread = Thread.CurrentThread.ManagedThreadId;
        Calls();

        s_Kind = "main";
        s_Context = Prepare(s_Kind);
        RunAll();

        s_Kind = "thread";
        s_Context = Prepare(s_Kind);
        var thread = new Thread(RunAll);
        thread.Start();
        thread.Join();

        s_Kind = "task";
        s_Context = Prepare(s_Kind);
        Task.Run(RunAll).Wait();

        // A scheduled job, waited for until a worker finished it, so that Complete does not run
        // it on the main thread.
        s_Kind = "job";
        s_Context = Prepare(s_Kind);
        var handle = new CallsJob().Schedule();
        JobHandle.ScheduleBatchedJobs();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!handle.IsCompleted && DateTime.UtcNow < deadline) Thread.Sleep(1);
        handle.Complete();

        s_Kind = "job.Run";
        s_Context = Prepare(s_Kind);
        new CallsJob().Run();
    }

    int m_Frames;

    void Update()
    {
        if (++m_Frames < 3) return;
        s_Log.Append("{\"complete\":true}\n");
        var output = Environment.GetEnvironmentVariable("MAINTHREAD_PROBE_OUT");
        if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, s_Log.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
