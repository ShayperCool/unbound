using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[DefaultExecutionOrder(-1000)]
public class DestroyPhases : MonoBehaviour
{
    public static bool Finished;
    public static string Position = "boot";
    public static DestroyPhases Instance;
    public static string Mode;
    public static bool Issued;
    public static GameObject Subject;
    public static Object Target;
    public static int Frame;
    static readonly List<string> Lines = new List<string>();
    static readonly string[] Cases = {
        "Awake", "OnEnable", "Start", "FixedUpdate", "OnTriggerEnter", "OnCollisionEnter",
        "Update", "NullCoroutine", "FixedCoroutine", "EndCoroutine", "LateUpdate",
        "AnimationEvent", "OnAnimatorMove", "OnAnimatorIK", "OnWillRenderObject", "OnBecameVisible",
        "UIEvent", "OnDestroy",
        "component", "builtin", "asset", "delay", "delayPaused", "zeroPaused", "delayFraction", "delayComponent", "delayAsset", "hierarchy", "deepHierarchy",
        "instantiate", "setActive", "unload", "immediateUpdate", "immediateTrigger",
        "immediateCollision", "immediateAnimation", "immediateRender", "immediateAwake", "immediateTriggerComponent", "immediateTriggerAsset", "ownCoroutine", "immediateHierarchy", "unloadHierarchy", "persistentAsset", "persistentPrefab", "pendingAnimation", "disabledScript", "builtinBehaviour", "nestedCoroutine", "defaultCoroutine", "multiFixed", "particleReadBeforeDestroy", "alwaysSimulateCoroutine", "bareCoroutine", "bareStart", "matureCoroutine"
    };
    int index = -1, age, startFrame;
    readonly List<GameObject> cleanup = new List<GameObject>();
    public static Action Done;
    public static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    public static string B(bool b) => b ? "true" : "false";
    public static void Log(string e, string detail = "")
    {
        if (Finished || Mode == null) return;
        Lines.Add("{\"case\":" + Q(Mode) + ",\"frame\":" + (Time.frameCount - Instance.startFrame)
            + ",\"phase\":" + Q(Position) + ",\"event\":" + Q(e) + ",\"detail\":" + Q(detail) + "}");
    }
    public static void StartupLog(string mode, string e, string detail)
    {
        Lines.Add("{\"case\":" + Q(mode) + ",\"frame\":0,\"phase\":" + Q(Position)
            + ",\"event\":" + Q(e) + ",\"detail\":" + Q(detail) + "}");
    }
    static string State()
    {
        var script = Subject == null ? null : Subject.GetComponent<DestroyObserver>();
        var ps = Subject == null ? null : Subject.GetComponent<ParticleSystem>();
        var audio = Subject == null ? null : Subject.GetComponent<AudioSource>();
        return "targetNull=" + B(Target == null) + " goNull=" + B(Subject == null)
            + " scriptEnabled=" + B(script != null && script.enabled)
            + " psPlaying=" + B(ps != null && ps.isPlaying) + " audioPlaying=" + B(audio != null && audio.isPlaying)
            + " audioEnabled=" + B(audio != null && audio.enabled)
            + " animationEnabled=" + B(Subject != null && Subject.GetComponent<Animation>() != null && Subject.GetComponent<Animation>().enabled);
    }
    public static void Request(string point)
    {
        if (Issued || Mode == null) return;
        bool match = Mode == point || (Mode == "multiFixed" && point == "FixedUpdate") || (Mode == "bareStart" && point == "Start") || Mode == "immediate" + point.Replace("On", "").Replace("Enter", "");
        if (!match) return;
        Issue();
    }
    public static void Issue()
    {
        if (Issued) return;
        Issued = true;
        File.AppendAllText(Environment.GetEnvironmentVariable("DESTROY_PHASES_OUT") + ".times", Mode + " " + Time.frameCount + " " + Time.timeAsDouble.ToString("R") + " " + Time.fixedTimeAsDouble.ToString("R") + "\n");
        Log("request", State());
        if (Mode.StartsWith("immediate")) Object.DestroyImmediate(Target);
        else Object.Destroy(Target, Mode == "delayFraction" ? .05f : Mode.StartsWith("delay") ? .06f : 0);
        Log("returned", State());
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("DESTROY_PHASES_OUT") == null) return;
        Time.captureFramerate = 50;
        Time.fixedDeltaTime = .02f;
        Application.runInBackground = true;
        Instance = new GameObject("DestroyPhases").AddComponent<DestroyPhases>();
        DontDestroyOnLoad(Instance.gameObject);
        Instance.gameObject.AddComponent<DefaultDestroyDriver>();
        foreach (var phase in new[] { "Awake", "OnEnable", "Start" })
        {
            var go = new GameObject("boot." + phase); go.SetActive(false);
            go.AddComponent<DestroyStartup>().phase = phase;
            go.SetActive(true);
        }
        new GameObject("pause").AddComponent<DestroyStartup>().phase = "OnApplicationPause";
        new GameObject("focus").AddComponent<DestroyStartup>().phase = "OnApplicationFocus";
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.gameObject.AddComponent<DestroyCamera>();
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        if (Environment.GetEnvironmentVariable("DESTROY_PHASES_DEFAULT_LOOP") != "1")
        {
            Wrap(ref loop);
            PlayerLoop.SetPlayerLoop(loop);
        }
        Application.logMessageReceived += (message, stack, kind) => {
            if (kind == LogType.Error || kind == LogType.Exception) Log("error", message);
        };
    }
    struct Marker { }
    static void Wrap(ref PlayerLoopSystem parent)
    {
        if (parent.subSystemList == null) return;
        var children = new List<PlayerLoopSystem>();
        foreach (var original in parent.subSystemList)
        {
            var node = original;
            if (node.subSystemList != null && node.subSystemList.Length > 0) Wrap(ref node);
            else
            {
                string name = node.type.DeclaringType.Name + "." + node.type.Name;
                children.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => Position = name });
                children.Add(node);
                children.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () => {
                    if (name == "PreLateUpdate.ParticleSystemBeginUpdateAll") Sample("after.particles");
                    if (Issued && Target == null && !Instance.sawNull) { Instance.sawNull = true; Log("firstNull"); }
                    Position = "after:" + name;
                }});
                continue;
            }
            children.Add(node);
        }
        parent.subSystemList = children.ToArray();
    }
    bool sawNull;
    void Update()
    {
        if (Finished) return;
        if (index < 0 || ++age == (Mode == "matureCoroutine" ? 160 : 9))
        {
            if (Mode != null) Log("case.end", "issued=" + B(Issued));
            Mode = null;
            Time.timeScale = 1; Time.captureFramerate = 50;
            foreach (var go in cleanup) if (go != null) Object.DestroyImmediate(go);
            cleanup.Clear();
            if (++index == Cases.Length) { Finish(); return; }
            Mode = Cases[index]; Frame = 0; age = 0; startFrame = Time.frameCount; Issued = false; sawNull = false;
            Log("case.begin");
            Setup();
        }
        Frame = age;
        File.AppendAllText(Environment.GetEnvironmentVariable("DESTROY_PHASES_OUT") + ".times", Mode + " " + Time.frameCount + " " + Time.timeAsDouble.ToString("R") + " " + Time.fixedTimeAsDouble.ToString("R") + "\n");
        Log("driver.Update", State());
        if (age == 1)
        {
            if (Mode == "disabledScript")
            {
                Subject.GetComponent<DestroyObserver>().enabled = false; Issued = true; Log("disabled.return", State());
            }
            else if (Mode == "UIEvent")
            {
                var document = Subject.AddComponent<UnityEngine.UIElements.UIDocument>();
                document.panelSettings = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
                var button = new UnityEngine.UIElements.Button();
                document.rootVisualElement.Add(button);
                button.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ => Issue());
                using (var click = UnityEngine.UIElements.ClickEvent.GetPooled()) { click.target = button; button.SendEvent(click); }
            }
            else if (Mode == "OnDestroy")
            {
                var other = new GameObject("other").AddComponent<DestroyOther>();
                Object.Destroy(other.gameObject);
            }
            else if (Mode == "component" || Mode == "builtin" || Mode == "asset" || Mode == "delay"
                || Mode.StartsWith("delay") || Mode == "zeroPaused" || Mode == "hierarchy" || Mode == "deepHierarchy" || Mode == "immediateHierarchy" || Mode.StartsWith("persistent") || Mode == "builtinBehaviour") Issue();
            else if (Mode == "instantiate")
            {
                var clone = Object.Instantiate(Subject); cleanup.Add(clone);
                Log("instantiate.return", "cloneNull=" + B(clone == null));
            }
            else if (Mode == "setActive") { Subject.SetActive(true); Log("setActive.return", State()); }
            else if (Mode.StartsWith("unload"))
            {
                Issued = true;
                Log("unload.request", State());
                var operation = SceneManager.UnloadSceneAsync(Subject.scene);
                Log("unload.return", State());
                operation.completed += _ => Log("unload.completed", State());
            }
        }
        if (age == 4 && Mode == "delayPaused") { Log("resume", State()); Time.timeScale = 1; }
        if (age >= 1) Request("Update");
    }
    void LateUpdate() { if (!Finished) Log("driver.LateUpdate", State()); }
    void Setup()
    {
        if (Mode.Contains("Animator"))
        {
            Subject = Object.Instantiate(Resources.Load<GameObject>("HumanA"));
            Subject.SetActive(false);
            Subject.GetComponent<Animator>().runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("HumanAIK");
            Subject.GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        else Subject = Mode.StartsWith("bare") ? new GameObject("bare") : GameObject.CreatePrimitive(PrimitiveType.Cube);
        Subject.name = "subject";
        Subject.SetActive(false);
        cleanup.Add(Subject);
        Target = Subject;
        var observer = Subject.AddComponent<DestroyObserver>();
        if (Mode.StartsWith("bare"))
        {
            Subject.SetActive(true); StartCoroutine(Resumes()); return;
        }
        if (Mode == "hierarchy" || Mode.EndsWith("Hierarchy"))
        {
            Subject.AddComponent<DestroyObserver>().label = "parent.second";
            for (int i = 0; i < 2; i++)
            {
                var child = new GameObject("child" + i); child.transform.SetParent(Subject.transform);
                child.AddComponent<DestroyObserver>().label = "child" + i;
                var grand = new GameObject("grand" + i); grand.transform.SetParent(child.transform);
                grand.AddComponent<DestroyObserver>().label = "grand" + i;
                if (Mode == "deepHierarchy")
                {
                    var great = new GameObject("great" + i); great.transform.SetParent(grand.transform);
                    great.AddComponent<DestroyObserver>().label = "great" + i;
                }
            }
        }
        if (Mode == "OnTriggerEnter" || Mode == "OnCollisionEnter" || Mode.StartsWith("immediateTrigger") || Mode == "immediateCollision")
        {
            var body = Subject.AddComponent<Rigidbody>(); body.useGravity = false;
            Subject.GetComponent<Collider>().isTrigger = Mode.Contains("Trigger");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "wall";
            wall.transform.position = new Vector3(.75f, 0, 0); cleanup.Add(wall);
        }
        var ps = Subject.AddComponent<ParticleSystem>();
        var main = ps.main;
        if (Mode == "particleReadBeforeDestroy" || Mode == "alwaysSimulateCoroutine") { main.loop = false; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; }
        main.startLifetime = 10; main.startSpeed = 0; main.playOnAwake = false;
        var emission = ps.emission; emission.enabled = false;
        var trigger = ps.trigger; trigger.enabled = true;
        trigger.inside = ParticleSystemOverlapAction.Callback; trigger.outside = ParticleSystemOverlapAction.Callback;
        trigger.enter = ParticleSystemOverlapAction.Callback; trigger.SetCollider(0, Subject.GetComponent<Collider>());
        var clip = AudioClip.Create("tone", 44100, 1, 44100, false);
        var audio = Subject.AddComponent<AudioSource>(); audio.clip = clip; audio.loop = true;
        if (Mode == "AnimationEvent" || Mode == "immediateAnimation" || Mode == "pendingAnimation")
        {
            var animation = Subject.AddComponent<Animation>();
            var animationClip = Resources.Load<AnimationClip>("Event");
            animation.AddClip(animationClip, "event"); animation.clip = animationClip; animation.playAutomatically = true;
        }
        if (Mode == "delayPaused" || Mode == "zeroPaused") Time.timeScale = 0;
        if (Mode == "multiFixed") Time.captureDeltaTime = .08f;
        if (Mode.StartsWith("unload")) SceneManager.MoveGameObjectToScene(Subject, SceneManager.CreateScene("temporary"));
        if (Mode == "component" || Mode == "delayComponent") Target = observer;
        if (Mode == "builtin") Target = ps;
        if (Mode == "builtinBehaviour") Target = audio;
        if (Mode == "persistentAsset") Target = Resources.Load<AnimationClip>("Event");
        if (Mode == "persistentPrefab") Target = Resources.Load<GameObject>("HumanA");
        if (Mode == "immediateTriggerComponent") Target = observer;
        if (Mode == "immediateTriggerAsset") Target = ScriptableObject.CreateInstance<DestroyAsset>();
        if (Mode == "asset" || Mode == "delayAsset") Target = ScriptableObject.CreateInstance<DestroyAsset>();
        if (Mode != "setActive")
        {
            Subject.SetActive(true); ps.Play(); ps.Emit(1); audio.Play();
            var animation = Subject.GetComponent<Animation>();
            if (animation != null) animation.Play("event");
        }
        StartCoroutine(Resumes());
    }
    IEnumerator Resumes()
    {
        string current = Mode;
        yield return null;
        if (current != Mode) yield break;
        Request("NullCoroutine");
        if (Mode == "bareCoroutine") Issue();
        if (Mode == "matureCoroutine")
        {
            for (int i = 1; i < 150; i++) yield return null;
            Issue();
        }
        if (Mode == "particleReadBeforeDestroy" || Mode == "alwaysSimulateCoroutine")
        {
            if (Mode == "particleReadBeforeDestroy")
            {
                var particles = new ParticleSystem.Particle[16];
                int count = Subject.GetComponent<ParticleSystem>().GetParticles(particles);
                Log("read.beforeDestroy", "count=" + count);
            }
            Issue();
        }
        if (Mode == "pendingAnimation")
        {
            yield return null; yield return null;
            Issue();
        }
        yield return new WaitForFixedUpdate();
        if (current != Mode) yield break;
        Request("FixedCoroutine");
        yield return new WaitForEndOfFrame();
        if (current != Mode) yield break;
        Request("EndCoroutine");
    }
    public static void Sample(string phase)
    {
        if (Instance == null || Finished || Subject == null) return;
        var ps = Subject.GetComponent<ParticleSystem>();
        var audio = Subject.GetComponent<AudioSource>();
        Log(phase, State() + " particles=" + (ps == null ? -1 : ps.particleCount) + " particleTicks=" + (ps == null ? -1 : Mathf.RoundToInt(ps.time * 50)) + " audio=" + B(audio != null && audio.isPlaying));
    }
    void Finish()
    {
        Lines.Add("{\"complete\":true}");
        File.WriteAllLines(Environment.GetEnvironmentVariable("DESTROY_PHASES_OUT"), Lines);
        Finished = true;
        Done?.Invoke();
    }
}
public class DestroyObserver : MonoBehaviour
{
    public string label = "parent.first";
    void Visit(string phase) { DestroyPhases.Log(label + "." + phase); DestroyPhases.Request(phase); }
    void Awake() { Visit("Awake"); if (DestroyPhases.Mode == "immediateAwake") DestroyPhases.Issue(); }
    void OnEnable() { Visit("OnEnable"); if ((DestroyPhases.Mode == "instantiate" && gameObject != DestroyPhases.Subject) || DestroyPhases.Mode == "setActive") { DestroyPhases.Target = gameObject; DestroyPhases.Issue(); } }
    void Start()
    {
        Visit("Start");
        if (DestroyPhases.Mode == "ownCoroutine") { StartCoroutine(First()); StartCoroutine(Second()); }
    }
    IEnumerator First()
    {
        yield return null;
        DestroyPhases.Issue();
        DestroyPhases.Log("own.first.after");
        yield return null;
        DestroyPhases.Log("own.first.next");
    }
    IEnumerator Second()
    {
        yield return null;
        DestroyPhases.Log("own.second.after");
        yield return new WaitForFixedUpdate();
        DestroyPhases.Log("own.second.fixed");
    }
    void FixedUpdate() => Visit("FixedUpdate");
    void Update() => Visit("Update");
    void LateUpdate() => Visit("LateUpdate");
    void OnTriggerEnter(Collider other) { Visit("OnTriggerEnter"); if (DestroyPhases.Mode.StartsWith("immediateTrigger")) DestroyPhases.Issue(); }
    void OnCollisionEnter(Collision other) { Visit("OnCollisionEnter"); if (DestroyPhases.Mode == "immediateCollision") DestroyPhases.Issue(); }
    void OnAnimationEvent() { Visit("AnimationEvent"); if (DestroyPhases.Mode == "immediateAnimation") DestroyPhases.Issue(); }
    void OnParticleTrigger() => Visit("OnParticleTrigger");
    void OnAnimatorMove() => Visit("OnAnimatorMove");
    void OnAnimatorIK(int layer) => Visit("OnAnimatorIK");
    void OnBecameVisible() => Visit("OnBecameVisible");
    void OnWillRenderObject() { Visit("OnWillRenderObject"); if (DestroyPhases.Mode == "immediateRender") DestroyPhases.Issue(); }
    void OnApplicationPause(bool pause) => Visit("OnApplicationPause");
    void OnApplicationFocus(bool focus) => Visit("OnApplicationFocus");
    string CallbackState() => "selfNull=" + DestroyPhases.B(this == null) + " goNull=" + DestroyPhases.B(gameObject == null)
        + " enabled=" + DestroyPhases.B(enabled) + " targetNull=" + DestroyPhases.B(DestroyPhases.Target == null);
    void OnDisable() => DestroyPhases.Log(label + ".OnDisable", CallbackState());
    void OnDestroy() => DestroyPhases.Log(label + ".OnDestroy", CallbackState());
}
public class DestroyOther : MonoBehaviour { void OnDestroy() => DestroyPhases.Issue(); }
public class DestroyAsset : ScriptableObject
{
    void OnDisable() => DestroyPhases.Log("asset.OnDisable");
    void OnDestroy() => DestroyPhases.Log("asset.OnDestroy");
}
public class DestroyCamera : MonoBehaviour
{
    void OnPreCull() => DestroyPhases.Sample("camera.preCull");
    void OnPostRender() => DestroyPhases.Sample("camera.postRender");
}

public class DestroyStartup : MonoBehaviour
{
    public string phase;
    bool requested;
    string Key => phase == "Awake" || phase == "OnEnable" || phase == "Start" ? "boot." + phase : phase;
    void Awake() { if (phase == "Awake") Request(); }
    void OnEnable() { if (phase == "OnEnable") Request(); }
    void Start() { if (phase == "Start") Request(); }
    void OnApplicationPause(bool value) { if (phase == "OnApplicationPause") Request(); }
    void OnApplicationFocus(bool value) { if (phase == "OnApplicationFocus") Request(); }
    void Request()
    {
        if (requested) return;
        requested = true;
        DestroyPhases.StartupLog(Key, "request", "null=" + DestroyPhases.B(this == null));
        Destroy(gameObject);
        DestroyPhases.StartupLog(Key, "returned", "null=" + DestroyPhases.B(this == null) + " enabled=" + DestroyPhases.B(enabled));
    }
    void OnDisable() { if (requested) DestroyPhases.StartupLog(Key, "OnDisable", ""); }
    void OnDestroy() { if (requested) DestroyPhases.StartupLog(Key, "OnDestroy", ""); }
}

public class DefaultDestroyDriver : MonoBehaviour
{
    string previous;
    void Update()
    {
        if (previous == DestroyPhases.Mode) return;
        previous = DestroyPhases.Mode;
        if (previous == "nestedCoroutine") StartCoroutine(Outer());
        else if (previous == "defaultCoroutine") StartCoroutine(Inner());
    }
    IEnumerator Outer() { yield return Inner(); }
    IEnumerator Inner() { yield return null; DestroyPhases.Issue(); }
}
