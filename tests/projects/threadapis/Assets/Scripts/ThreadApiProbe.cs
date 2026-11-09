using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using Unity.Collections;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>Records API outcomes in ordinary threads, jobs, animation, contact modification and
/// loading/creation contexts. Each context receives its own objects made on the main thread.</summary>
public class ThreadApiProbe : MonoBehaviour
{
    [Serializable]
    public class Data { public int a = 1; }

    /// <summary>The objects of one thread kind.</summary>
    class Context
    {
        public GameObject Target, Child, Victim, ImmediateVictim, Destroyed;
        public Transform TargetTransform, ChildTransform;
        public Scene Scene;
        public ThreadApiTarget Caller;
        public Rigidbody Body;
        public BoxCollider Collider;
        public Renderer Renderer;
        public Material Material;
        public Shader Shader;
        public Animator Animator;
        public AudioSource Audio;
        public Canvas Canvas;
        public RectTransform Rect;
        public PhysicsScene PhysicsScene;
        public RaycastHit[] Hits = new RaycastHit[4];
        public Vector3[] Corners = new Vector3[4];
        public TextAsset Text;
    }

    static Context s_Context;
    static string s_Kind;
    static readonly StringBuilder s_Log = new StringBuilder();
    static readonly StringBuilder s_RaycastLog = new StringBuilder();
    static readonly List<(string Name, Func<object> Call)> s_Calls = new List<(string, Func<object>)>();
    static int s_MainThread;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("THREAD_FOLLOWUP_OUT")))
            new GameObject("ThreadApiProbe").AddComponent<ThreadApiProbe>();
    }

    struct CallsJob : IJob
    {
        public void Execute() => RunAll();
    }

    struct AnimationCalls : IAnimationJob
    {
        public TransformStreamHandle StreamHandle;
        public TransformSceneHandle SceneHandle;
        public void ProcessRootMotion(AnimationStream stream) { }
        public void ProcessAnimation(AnimationStream stream)
        {
            RunAll();
            try { Record("stream transform", Text(StreamHandle.GetLocalPosition(stream))); }
            catch (Exception e) { Record("stream transform", "throws " + e.GetType().FullName + ": " + e.Message); }
            try { Record("scene transform", Text(SceneHandle.GetLocalPosition(stream))); }
            catch (Exception e) { Record("scene transform", "throws " + e.GetType().FullName + ": " + e.Message); }
        }
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
        if (s_Kind != "animation" && s_Kind != "contactModify" && s_Kind != "contactAutomatic")
            Record("on main thread", Thread.CurrentThread.ManagedThreadId == s_MainThread ? "yes" : "no");
        foreach (var (name, call) in s_Calls)
        {
            string result;
            try { result = Text(call()); }
            catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
            Record(name, result);
        }
        string outcome;
        try { Physics.Raycast(Vector3.up * 100, Vector3.down, out _, 1f, ~0); outcome = "ok"; }
        catch (Exception e) { outcome = "throws " + e.GetType().FullName + ": " + e.Message; }
        s_RaycastLog.Append("{\"thread\":\"").Append(s_Kind).Append("\",\"call\":\"Physics.Raycast hit layerMask\",\"result\":\"")
            .Append(Escape(outcome)).Append("\"}\n");
    }

    static void Add(string name, Func<object> call) => s_Calls.Add((name, call));
    static void Add(string name, Action call) => s_Calls.Add((name, () => { call(); return "void"; }));

    static IEnumerator Nothing() { yield break; }

    static void Calls()
    {
        Context C() => s_Context;
        Add("Rigidbody.mass get", () => C().Body.mass);
        Add("Rigidbody.linearVelocity get", () => C().Body.linearVelocity);
        Add("Rigidbody.angularVelocity get", () => C().Body.angularVelocity);
        Add("Rigidbody.position get", () => C().Body.position);
        Add("Rigidbody.rotation get", () => C().Body.rotation);
        Add("Rigidbody.isKinematic get", () => C().Body.isKinematic);
        Add("Rigidbody.useGravity get", () => C().Body.useGravity);
        Add("Rigidbody.detectCollisions get", () => C().Body.detectCollisions);
        Add("Rigidbody.constraints get", () => C().Body.constraints);
        Add("Rigidbody.interpolation get", () => C().Body.interpolation);
        Add("Rigidbody.collisionDetectionMode get", () => C().Body.collisionDetectionMode);
        Add("Rigidbody.centerOfMass get", () => C().Body.centerOfMass);
        Add("Rigidbody.inertiaTensor get", () => C().Body.inertiaTensor);
        Add("Rigidbody.maxAngularVelocity get", () => C().Body.maxAngularVelocity);
        Add("Rigidbody.sleepThreshold get", () => C().Body.sleepThreshold);
        Add("Collider.enabled get", () => C().Collider.enabled);
        Add("Collider.isTrigger get", () => C().Collider.isTrigger);
        Add("Collider.bounds get", () => C().Collider.bounds);
        Add("Collider.attachedRigidbody get", () => C().Collider.attachedRigidbody);
        Add("Collider.contactOffset get", () => C().Collider.contactOffset);
        Add("Collider.sharedMaterial get", () => C().Collider.sharedMaterial);
        Add("Collider.hasModifiableContacts get", () => C().Collider.hasModifiableContacts);
        Add("Renderer.enabled get", () => C().Renderer.enabled);
        Add("Renderer.bounds get", () => C().Renderer.bounds);
        Add("Renderer.sharedMaterial get", () => C().Renderer.sharedMaterial);
        Add("Renderer.sharedMaterials get", () => C().Renderer.sharedMaterials);
        Add("Renderer.material get", () => C().Renderer.material);
        Add("Renderer.materials get", () => C().Renderer.materials);
        Add("Renderer.sortingOrder get", () => C().Renderer.sortingOrder);
        Add("Renderer.sortingLayerID get", () => C().Renderer.sortingLayerID);
        Add("Renderer.isVisible get", () => C().Renderer.isVisible);
        Add("Material.shader get", () => C().Material.shader);
        Add("Material.color get", () => C().Material.color);
        Add("Material.mainTexture get", () => C().Material.mainTexture);
        Add("Material.renderQueue get", () => C().Material.renderQueue);
        Add("Material.shaderKeywords get", () => C().Material.shaderKeywords);
        Add("Material.enableInstancing get", () => C().Material.enableInstancing);
        Add("Material.passCount get", () => C().Material.passCount);
        Add("Animator.speed get", () => C().Animator.speed);
        Add("Animator.applyRootMotion get", () => C().Animator.applyRootMotion);
        Add("Animator.updateMode get", () => C().Animator.updateMode);
        Add("Animator.cullingMode get", () => C().Animator.cullingMode);
        Add("Animator.isInitialized get", () => C().Animator.isInitialized);
        Add("Animator.deltaPosition get", () => C().Animator.deltaPosition);
        Add("Animator.deltaRotation get", () => C().Animator.deltaRotation);
        Add("Animator.runtimeAnimatorController get", () => C().Animator.runtimeAnimatorController);
        Add("Animator.parameterCount get", () => C().Animator.parameterCount);
        Add("AudioSource.volume get", () => C().Audio.volume);
        Add("AudioSource.pitch get", () => C().Audio.pitch);
        Add("AudioSource.clip get", () => C().Audio.clip);
        Add("AudioSource.isPlaying get", () => C().Audio.isPlaying);
        Add("AudioSource.loop get", () => C().Audio.loop);
        Add("AudioSource.mute get", () => C().Audio.mute);
        Add("AudioSource.timeSamples get", () => C().Audio.timeSamples);
        Add("AudioSource.playOnAwake get", () => C().Audio.playOnAwake);
        Add("AudioSource.spatialBlend get", () => C().Audio.spatialBlend);
        Add("Canvas.renderMode get", () => C().Canvas.renderMode);
        Add("Canvas.scaleFactor get", () => C().Canvas.scaleFactor);
        Add("Canvas.pixelRect get", () => C().Canvas.pixelRect);
        Add("Canvas.sortingOrder get", () => C().Canvas.sortingOrder);
        Add("Canvas.isRootCanvas get", () => C().Canvas.isRootCanvas);
        Add("RectTransform.rect get", () => C().Rect.rect);
        Add("RectTransform.anchorMin get", () => C().Rect.anchorMin);
        Add("RectTransform.anchorMax get", () => C().Rect.anchorMax);
        Add("RectTransform.anchoredPosition get", () => C().Rect.anchoredPosition);
        Add("RectTransform.sizeDelta get", () => C().Rect.sizeDelta);
        Add("RectTransform.pivot get", () => C().Rect.pivot);
        Add("Body.mass set", () => { C().Body.mass = 1f; });
        Add("Body.linearVelocity set", () => { C().Body.linearVelocity = Vector3.zero; });
        Add("Collider.isTrigger set", () => { C().Collider.isTrigger = false; });
        Add("Renderer.enabled set", () => { C().Renderer.enabled = true; });
        Add("Material.renderQueue set", () => { C().Material.renderQueue = 2000; });
        Add("Animator.speed set", () => { C().Animator.speed = 1f; });
        Add("Audio.volume set", () => { C().Audio.volume = 0.5f; });
        Add("Canvas.scaleFactor set", () => { C().Canvas.scaleFactor = 1f; });
        Add("Rect.anchorMin set", () => { C().Rect.anchorMin = Vector2.zero; });
        Add("Rigidbody.AddForce", () => C().Body.AddForce(Vector3.zero));
        Add("Rigidbody.MovePosition", () => C().Body.MovePosition(Vector3.zero));
        Add("Rigidbody.IsSleeping", () => C().Body.IsSleeping());
        Add("Collider.ClosestPoint", () => C().Collider.ClosestPoint(Vector3.zero));
        Add("Collider.Raycast", () => C().Collider.Raycast(new Ray(Vector3.up*100,Vector3.down), out _, 1f));
        Add("Physics.Raycast zero direction", () => Physics.Raycast(Vector3.zero, Vector3.zero, 1f));
        Add("Physics.Raycast zero direction hit", () => Physics.Raycast(Vector3.zero, Vector3.zero, out _, 1f));
        Add("Physics.RaycastNonAlloc null", () => Physics.RaycastNonAlloc(Vector3.zero, Vector3.up, null, 1f));
        Add("Collider.Raycast zero direction", () => C().Collider.Raycast(new Ray(Vector3.zero, Vector3.zero), out _, 1f));
        Add("Physics.SphereCast", () => Physics.SphereCast(Vector3.up*100, 0.5f, Vector3.down, out _, 1f));
        Add("Physics.BoxCast", () => Physics.BoxCast(Vector3.up*100, Vector3.one, Vector3.down, out _, Quaternion.identity, 1f));
        Add("Physics.CapsuleCast", () => Physics.CapsuleCast(Vector3.up*100, Vector3.up*101, 0.5f, Vector3.down, out _, 1f));
        Add("Physics.OverlapBox", () => Physics.OverlapBox(Vector3.up*100, Vector3.one));
        Add("Physics.OverlapCapsule", () => Physics.OverlapCapsule(Vector3.up*100, Vector3.up*101, 0.5f));
        Add("Physics.CheckBox", () => Physics.CheckBox(Vector3.up*100, Vector3.one));
        Add("Physics.CheckCapsule", () => Physics.CheckCapsule(Vector3.up*100, Vector3.up*101, 0.5f));
        Add("MonoBehaviour.Invoke", () => C().Caller.Invoke("Noop", 100f));
        Add("MonoBehaviour.InvokeRepeating", () => C().Caller.InvokeRepeating("Noop", 100f, 1f));
        Add("MonoBehaviour.CancelInvoke string", () => C().Caller.CancelInvoke("Noop"));
        Add("MonoBehaviour.CancelInvoke", () => C().Caller.CancelInvoke());
        Add("MonoBehaviour.IsInvoking string", () => C().Caller.IsInvoking("Noop"));
        Add("MonoBehaviour.IsInvoking", () => C().Caller.IsInvoking());
        Add("Physics.gravity", () => Physics.gravity);
        Add("Physics.defaultPhysicsScene", () => Physics.defaultPhysicsScene);
        Add("Physics.Raycast", () => Physics.Raycast(Vector3.up*100,Vector3.down,1f));
        Add("Physics.Raycast hit", () => Physics.Raycast(Vector3.up*100,Vector3.down,out _,1f));
        Add("Physics.RaycastAll", () => Physics.RaycastAll(Vector3.up*100,Vector3.down,1f));
        Add("Physics.RaycastNonAlloc", () => Physics.RaycastNonAlloc(Vector3.up*100,Vector3.down,C().Hits,1f));
        Add("Physics.OverlapSphere", () => Physics.OverlapSphere(Vector3.up*100,1f));
        Add("Physics.CheckSphere", () => Physics.CheckSphere(Vector3.up*100,1f));
        Add("Physics.SyncTransforms", () => Physics.SyncTransforms());
        Add("PhysicsScene.Raycast", () => C().PhysicsScene.Raycast(Vector3.up*100,Vector3.down,1f));
        Add("PhysicsScene.IsValid", () => C().PhysicsScene.IsValid());
        Add("Material.GetFloat", () => C().Material.GetFloat("_Glossiness"));
        Add("Material.SetFloat", () => C().Material.SetFloat("_Glossiness",0.5f));
        Add("Material.HasProperty", () => C().Material.HasProperty("_Color"));
        Add("Material.EnableKeyword", () => C().Material.EnableKeyword("TEST"));
        Add("new Material", () => new Material(C().Shader));
        Add("Animator.StringToHash", () => Animator.StringToHash("test"));
        Add("Animator.GetFloat string", () => C().Animator.GetFloat("missing"));
        Add("Animator.GetFloat int", () => C().Animator.GetFloat(0));
        Add("Animator.SetFloat string", () => C().Animator.SetFloat("missing",1f));
        Add("Animator.SetFloat int", () => C().Animator.SetFloat(0,1f));
        Add("Animator.Play", () => C().Animator.Play(0));
        Add("Animator.GetCurrentAnimatorStateInfo", () => C().Animator.GetCurrentAnimatorStateInfo(0));
        Add("AudioSource.Play", () => C().Audio.Play());
        Add("AudioSource.Stop", () => C().Audio.Stop());
        Add("AudioSource.Pause", () => C().Audio.Pause());
        Add("AudioSource.UnPause", () => C().Audio.UnPause());
        Add("Canvas.ForceUpdateCanvases", () => Canvas.ForceUpdateCanvases());
        Add("RectTransform.GetWorldCorners", () => C().Rect.GetWorldCorners(C().Corners));
        Add("Component.GetComponentInChildren", () => C().Collider.GetComponentInChildren<Transform>());
        Add("Component.GetComponentInParent", () => C().Collider.GetComponentInParent<Transform>());
        Add("Component.GetComponentsInChildren", () => C().Collider.GetComponentsInChildren<Transform>());
        Add("JobsUtility.IsExecutingJob", () => Unity.Jobs.LowLevel.Unsafe.JobsUtility.IsExecutingJob);
        Add("Time.time", () => Time.time);
        Add("Mathf.Sin", () => Mathf.Sin(1f));
        Add("Shader.PropertyToID", () => Shader.PropertyToID("_Color"));
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
            Caller = target.AddComponent<ThreadApiTarget>(),
            Body = target.AddComponent<Rigidbody>(),
            Collider = target.AddComponent<BoxCollider>(),
            Renderer = target.AddComponent<MeshRenderer>(),
            Shader = Shader.Find("Standard"),
            Material = new Material(Shader.Find("Standard")),
            Animator = target.AddComponent<Animator>(),
            Audio = target.AddComponent<AudioSource>(),
            Canvas = new GameObject("Canvas", typeof(RectTransform)).AddComponent<Canvas>(),
            Rect = new GameObject("Rect", typeof(RectTransform)).GetComponent<RectTransform>(),
            PhysicsScene = Physics.defaultPhysicsScene,
            Victim = new GameObject("Victim-" + kind),
            ImmediateVictim = new GameObject("ImmediateVictim-" + kind),
            Destroyed = destroyed,

        };
    }

    void Start()
    {
        s_MainThread = Thread.CurrentThread.ManagedThreadId;
        s_Kind = "loading";
        var marker = UnityEngine.Object.FindFirstObjectByType<LoadingMarker>();
        Record("MonoBehaviour.field", marker.Field);
        Record("MonoBehaviour.constructor", marker.Constructor);
        var asset = Resources.Load<LoadingAsset>("Loading");
        Record("ScriptableObject.field", asset.Field);
        Record("ScriptableObject.constructor", asset.Constructor);
        var added = new GameObject("Added").AddComponent<LoadingMarker>();
        Record("AddComponent.field", added.Field);
        var created = ScriptableObject.CreateInstance<LoadingAsset>();
        Record("CreateInstance.field", created.Field);
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

        s_Kind = "async.offContext";
        s_Context = Prepare(s_Kind);
        Task.Run(async () => { await Task.Delay(1).ConfigureAwait(false); RunAll(); }).Wait();

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

        s_Kind = "animation";
        s_Context = Prepare(s_Kind);
        var graph = PlayableGraph.Create("Thread checks");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationScriptPlayable.Create(graph, new AnimationCalls
        {
            StreamHandle = s_Context.Animator.BindStreamTransform(s_Context.ChildTransform),
            SceneHandle = s_Context.Animator.BindSceneTransform(s_Context.ChildTransform),
        });
        var output = AnimationPlayableOutput.Create(graph, "Animation", s_Context.Animator);
        output.SetSourcePlayable(playable);
        graph.Play();
        graph.Evaluate(0.02f);
        graph.Destroy();

        s_Kind = "contactModify";
        s_Context = Prepare(s_Kind);
        Physics.simulationMode = SimulationMode.Script;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0, -0.5f, 0);
        s_Context.TargetTransform.position = new Vector3(0, 0.25f, 0);
        s_Context.Collider.hasModifiableContacts = true;
        s_Context.Body.useGravity = false;
        Physics.SyncTransforms();
        bool seen = false;
        Action<PhysicsScene, NativeArray<ModifiableContactPair>> modify = (scene, pairs) =>
        {
            lock (s_Log)
            {
                if (seen || pairs.Length == 0) return;
                seen = true;
                RunAll();
            }
        };
        Physics.ContactModifyEvent += modify;
        Physics.Simulate(0.02f);
        Physics.ContactModifyEvent -= modify;
        Record("callback observed", seen ? "yes" : "no");
        Physics.simulationMode = SimulationMode.FixedUpdate;
        DestroyImmediate(ground);

        s_Kind = "contactAutomatic";
        s_Context = Prepare(s_Kind);
        ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0, -0.5f, 0);
        s_Context.TargetTransform.position = new Vector3(0, 0.25f, 0);
        s_Context.Collider.hasModifiableContacts = true;
        s_Context.Body.useGravity = false;
        m_Modify = (scene, pairs) =>
        {
            lock (s_Log)
            {
                if (m_AutomaticSeen || pairs.Length == 0) return;
                m_AutomaticSeen = true;
                RunAll();
            }
        };
        Physics.ContactModifyEvent += m_Modify;
    }

    int m_Frames;
    bool m_AutomaticSeen;
    Action<PhysicsScene, NativeArray<ModifiableContactPair>> m_Modify;

    void Update()
    {
        if (++m_Frames < 3 || (!m_AutomaticSeen && m_Frames < 30)) return;
        Physics.ContactModifyEvent -= m_Modify;
        Record("callback observed", m_AutomaticSeen ? "yes" : "no");
        s_Log.Append("{\"complete\":true}\n");
        var output = Environment.GetEnvironmentVariable("THREAD_APIS_OUT");
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllText(output, s_Log.ToString());
            s_RaycastLog.Append("{\"complete\":true}\n");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "raycast.jsonl"), s_RaycastLog.ToString());
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
