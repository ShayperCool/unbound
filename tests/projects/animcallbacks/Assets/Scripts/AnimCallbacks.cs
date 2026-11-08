using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.LowLevel;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>
/// Where the animation callbacks run in the player loop. Every leaf system of the loop is
/// bracketed by markers that record its name; each callback logs the leaf it runs in, and
/// whether the subject's pose and root changed since the previous LateUpdate (so the record
/// shows when the pose is written relative to the callbacks). Each case runs eight frames.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class AnimCallbacks : MonoBehaviour
{
    public static bool Finished;
    public static string Position = "boot";
    public static string Mode;
    public static AnimCallbacks Instance;
    static readonly List<string> Lines = new List<string>();
    static readonly string[] Cases = {
        "genericController", "humanController", "twoHumans", "humanLegacy", "genericGraph", "humanGraph",
        "humanManual", "humanUpdateCall", "humanFixed", "humanCulled", "humanControllerGraph",
    };
    const int CaseFrames = 8;
    int index = -1, age, startFrame;
    readonly List<GameObject> cleanup = new List<GameObject>();
    public static readonly List<Observer> Subjects = new List<Observer>();
    PlayableGraph graph;
    Animator updated;

    public static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static void Log(string e, string detail = "")
    {
        if (Finished || Mode == null) return;
        Lines.Add("{\"case\":" + Q(Mode) + ",\"frame\":" + (Time.frameCount - Instance.startFrame)
            + ",\"phase\":" + Q(Position) + ",\"event\":" + Q(e) + ",\"detail\":" + Q(detail) + "}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("ANIM_CALLBACKS_OUT") == null) return;
        Time.captureFramerate = 50;
        Time.fixedDeltaTime = .02f;
        Application.runInBackground = true;
        Instance = new GameObject("AnimCallbacks").AddComponent<AnimCallbacks>();
        DontDestroyOnLoad(Instance.gameObject);
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 1, -6);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        DontDestroyOnLoad(camera.gameObject);
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        Wrap(ref loop);
        PlayerLoop.SetPlayerLoop(loop);
        Application.logMessageReceived += (message, stack, kind) =>
        {
            // The Editor's own window code can log while Play mode starts; only the game's errors count.
            if ((kind == LogType.Error || kind == LogType.Exception) && !stack.Contains("UnityEditor.")) Log("error", message);
        };
    }

    struct Marker { }

    static readonly string[] Sampled = {
        "FixedUpdate.DirectorFixedUpdate", "FixedUpdate.DirectorFixedUpdatePostPhysics",
        "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.LegacyAnimationUpdate",
        "PreLateUpdate.DirectorUpdateAnimationEnd", "PreLateUpdate.DirectorDeferredEvaluate",
    };

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
                bool sampled = Array.IndexOf(Sampled, name) >= 0;
                children.Add(new PlayerLoopSystem { type = typeof(Marker), updateDelegate = () =>
                {
                    Position = "after:" + name;
                    // Which subjects each animation leaf posed or moved.
                    if (sampled && Subjects.Count > 0) Log("after", States());
                }});
                continue;
            }
            children.Add(node);
        }
        parent.subSystemList = children.ToArray();
    }

    void Update()
    {
        if (Finished) return;
        if (index < 0 || ++age == CaseFrames)
        {
            if (Mode != null) Log("case.end");
            Mode = null;
            if (graph.IsValid()) graph.Destroy();
            updated = null;
            foreach (var go in cleanup) if (go != null) Object.DestroyImmediate(go);
            cleanup.Clear();
            Subjects.Clear();
            if (++index == Cases.Length) { Finish(); return; }
            Mode = Cases[index]; age = 0; startFrame = Time.frameCount;
            Log("case.begin");
            Setup();
        }
        Log("driver.Update", States());
        if (Mode == "humanManual" && age >= 1)
        {
            Log("evaluate.call");
            graph.Evaluate(.02f);
            Log("evaluate.return", States());
        }
        if (Mode == "humanUpdateCall" && age >= 1)
        {
            Log("update.call");
            updated.Update(.02f);
            Log("update.return", States());
        }
    }

    void LateUpdate()
    {
        if (Finished || Mode == null) return;
        Log("driver.LateUpdate", States());
        foreach (var s in Subjects) s.Snapshot();
    }

    public static string States()
    {
        var parts = new List<string>();
        foreach (var s in Subjects) parts.Add(s.label + ":" + s.State());
        return string.Join(" ", parts);
    }

    Observer Human(string label, Vector3 at, string controller)
    {
        var go = Object.Instantiate(Resources.Load<GameObject>("HumanA"));
        go.SetActive(false);
        go.name = label;
        go.transform.position = at;
        var animator = go.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller == null ? null : Resources.Load<RuntimeAnimatorController>(controller);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = true;
        return Attach(go, label);
    }

    Observer Generic(string label, Vector3 at, string controller)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.SetActive(false);
        go.name = label;
        go.transform.position = at;
        var marker = new GameObject("Marker").transform;
        marker.SetParent(go.transform, false);
        var animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller == null ? null : Resources.Load<RuntimeAnimatorController>(controller);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = true;
        return Attach(go, label);
    }

    Observer Attach(GameObject go, string label)
    {
        cleanup.Add(go);
        var o = go.AddComponent<Observer>();
        o.label = label;
        o.Snapshot();
        Subjects.Add(o);
        return o;
    }

    PlayableGraph Graph(Observer o, string clip, DirectorUpdateMode mode)
    {
        var g = PlayableGraph.Create(Mode);
        g.SetTimeUpdateMode(mode);
        var output = AnimationPlayableOutput.Create(g, "Animation", o.GetComponent<Animator>());
        var playable = AnimationClipPlayable.Create(g, Resources.Load<AnimationClip>(clip));
        playable.SetApplyPlayableIK(true);
        output.SetSourcePlayable(playable);
        g.Play();
        return g;
    }

    void Setup()
    {
        Observer main = null;
        switch (Mode)
        {
            case "genericController": main = Generic("g", Vector3.zero, "GenericOrder"); break;
            case "humanController": main = Human("h", Vector3.zero, "HumanOrder"); break;
            case "twoHumans":
                main = Human("h1", Vector3.zero, "HumanOrder");
                Human("h2", new Vector3(1.5f, 0, 0), "HumanOrder");
                break;
            case "humanLegacy":
            {
                main = Human("h", Vector3.zero, "HumanOrder");
                Generic("g", new Vector3(-1.5f, 0, 0), "GenericOrder");
                var legacy = new GameObject("legacy");
                cleanup.Add(legacy);
                legacy.AddComponent<LegacyObserver>();
                var animation = legacy.AddComponent<Animation>();
                var clip = Resources.Load<AnimationClip>("LegacyEvents");
                animation.AddClip(clip, "events");
                animation.clip = clip;
                animation.Play("events");
                break;
            }
            case "genericGraph":
                main = Generic("g", Vector3.zero, null);
                break;
            case "humanGraph":
            case "humanManual":
                main = Human("h", Vector3.zero, null);
                break;
            case "humanUpdateCall": main = Human("h", Vector3.zero, "HumanOrder"); break;
            case "humanFixed":
                main = Human("h", Vector3.zero, "HumanOrder");
                main.GetComponent<Animator>().updateMode = AnimatorUpdateMode.Fixed;
                break;
            case "humanCulled":
                // Off screen: CullUpdateTransforms keeps the state machines and movement, not the
                // pose or IK.
                main = Human("h", new Vector3(1000, 0, 0), "HumanOrder");
                main.GetComponent<Animator>().cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                break;
            case "humanControllerGraph":
                main = Human("h", Vector3.zero, "HumanOrder");
                break;
        }
        foreach (var s in Subjects) s.gameObject.SetActive(true);
        if (Mode == "genericGraph") graph = Graph(main, "GenericEvents", DirectorUpdateMode.GameTime);
        if (Mode == "humanGraph" || Mode == "humanControllerGraph") graph = Graph(main, "HumanEvents", DirectorUpdateMode.GameTime);
        if (Mode == "humanManual") graph = Graph(main, "HumanEvents", DirectorUpdateMode.Manual);
        if (Mode == "humanUpdateCall") updated = main.GetComponent<Animator>();
        Log("setup.done", States());
    }

    void Finish()
    {
        Lines.Add("{\"complete\":true}");
        File.WriteAllLines(Environment.GetEnvironmentVariable("ANIM_CALLBACKS_OUT"), Lines);
        Finished = true;
    }
}

/// <summary>Logs the Animator's script callbacks with the pose and root state.</summary>
public class Observer : MonoBehaviour
{
    public string label;
    Transform[] bones;
    Vector3[] positions;
    Quaternion[] rotations;
    Vector3 rootPosition;

    /// <summary>Remembers every transform below the root (the pose) and the root's position.</summary>
    public void Snapshot()
    {
        if (bones == null)
        {
            var all = GetComponentsInChildren<Transform>(true);
            bones = new Transform[all.Length - 1];
            Array.Copy(all, 1, bones, 0, bones.Length);
            positions = new Vector3[bones.Length];
            rotations = new Quaternion[bones.Length];
        }
        for (int i = 0; i < bones.Length; i++)
        {
            positions[i] = bones[i].localPosition;
            rotations[i] = bones[i].localRotation;
        }
        rootPosition = transform.position;
    }

    // Exact component comparison: Vector3 and Quaternion equality operators are approximate.
    static bool Same(Vector3 a, Vector3 b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);
    static bool Same(Quaternion a, Quaternion b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z) && a.w.Equals(b.w);

    bool PoseSame()
    {
        for (int i = 0; i < bones.Length; i++)
            if (!Same(bones[i].localPosition, positions[i]) || !Same(bones[i].localRotation, rotations[i])) return false;
        return true;
    }

    /// <summary>Whether the pose (any transform below the root) and the root changed since the
    /// previous LateUpdate.</summary>
    public string State() =>
        "pose=" + (PoseSame() ? "old" : "new") + ",root=" + (Same(transform.position, rootPosition) ? "still" : "moved");

    // Every subject's state: whether other Animators were already moved or posed.
    void Visit(string e, string detail = "") =>
        AnimCallbacks.Log(label + "." + e, (detail.Length > 0 ? detail + " " : "") + AnimCallbacks.States());

    void OnAnimatorMove()
    {
        Visit("OnAnimatorMove");
        GetComponent<Animator>().ApplyBuiltinRootMotion();
    }

    void OnAnimatorIK(int layer) => Visit("OnAnimatorIK", "layer=" + layer);
    void OnClipEvent(string clip) => Visit("event", clip);
}

/// <summary>A legacy Animation's event, between the Animators' two animation leaves.</summary>
public class LegacyObserver : MonoBehaviour
{
    void OnClipEvent(string clip) => AnimCallbacks.Log("legacy.event", clip + " " + AnimCallbacks.States());
}
