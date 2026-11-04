// FAKE Unity API for the offline guard controls (controls.py). It lets the probe's two real
// scripts run outside Unity with deterministic answers chosen per scenario; NO Unity behaviour
// (activation order, 2D contacts, ParticleSystem state) is claimed. The fake deactivation runs
// script OnDisable and built-in disables in a configurable model (object order pre/post/revpre/
// revlevel; interleaved per object, built-ins first, or scripts first); 2D exits are sent at the
// first removal, deferred to the next Simulate, or never; built-in reads can follow the removal or
// the activeInHierarchy flags. Activation orders recorders by pre-order, add (id), first Awake or
// last enable. Messages are sent by reflection; Start runs once in a later frame; coroutines resume
// one step per frame. Editor transitions are recorded with the callback they were made from;
// calls the protocol forbids go to Fake.Forbidden.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class Fake
{
    public static readonly List<string> Forbidden = new List<string>();
    public static readonly List<string> Transitions = new List<string>();
    public static string Context = "none";
    public static double EditorTime;
    public static int? ExitCode;
    public static bool PendingEnter, PendingExit;
    public static int Frame = 1;
    public static void NextFrame() => Frame++;
    public static void Forbid(string what) { Forbidden.Add(what + " in " + Context); throw new Exception("forbidden call: " + what); }

    // ------------------------------------------------------------ scenario answers (fake)
    /// <summary>Object order of a deactivation: "pre", "post", "revpre" or "revlevel".</summary>
    public static string OffOrder = "post";
    /// <summary>Order of an already-awake activation: "pre", "post", "add", "firstawake", "lastenable".</summary>
    public static string OnOrder = "add";
    /// <summary>Built-in vs script disables: "interleaved", "builtinsFirst", "scriptsFirst".</summary>
    public static string BuiltinModel = "interleaved";
    /// <summary>2D exits on a removal with callbacksOnDisable: "atRemoval", "deferred", "none".</summary>
    public static string ExitPlacement = "atRemoval";
    public static bool ReadsFollowFlags;
    public static bool ParticlesChangeOnDisable = true;
    public static float ContactRange = 1.5f;
    public static bool WakeParentFirst;
    public static bool WakePhased;
    /// <summary>The n-th AddComponent throws (0: never).</summary>
    public static int FailAddAt;
    public static int Adds;
    public static int TeardownCallbacks;
    static int sequence;
    public static int Next() => ++sequence;

    public static readonly List<GameObject> Objects = new List<GameObject>();
    public static readonly List<GameObject> SceneRoots = new List<GameObject>();
    public static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();
    static readonly List<(IEnumerator Routine, int Frame)> Coroutines = new List<(IEnumerator, int)>();
    static int nextId = -1000;
    public static int NewId() { nextId -= 2; return nextId; }

    public static void Send(MonoBehaviour b, string message, object argument = null)
    {
        var method = b.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == message && m.GetParameters().Length == (argument == null ? 0 : 1));
        if (method == null) return;
        if (message == "OnEnable") b.enableSeq = Next();
        object result;
        try { result = method.Invoke(b, argument == null ? null : new[] { argument }); }
        catch (TargetInvocationException e) { Forbidden.Add(message + " threw " + e.InnerException?.Message); return; }
        if (result is IEnumerator routine)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { Forbidden.Add(message + " coroutine threw " + e.Message); return; }
            if (more) Coroutines.Add((routine, Frame));
        }
    }

    public static void Wake(MonoBehaviour b)
    {
        if (b.awake) return;
        b.awake = true;
        b.awakeSeq = Next();
        Send(b, "Awake");
    }

    public static void RunStarts()
    {
        foreach (var b in Behaviours.ToList())
            if (b.awake && !b.started && b.enabled && b.gameObject.activeInHierarchy && !b.destroyed)
            {
                b.started = true;
                Send(b, "Start");
            }
    }

    public static void StepCoroutines()
    {
        foreach (var c in Coroutines.ToList())
        {
            if (c.Frame == Frame) continue;
            bool more;
            try { more = c.Routine.MoveNext(); }
            catch (Exception e) { Forbidden.Add("coroutine threw " + e.Message); more = false; }
            if (!more) Coroutines.Remove(c);
        }
    }

    internal static IEnumerable<GameObject> Pre(GameObject g)
    {
        yield return g;
        foreach (var c in g.transform.children) foreach (var d in Pre(c.gameObject)) yield return d;
    }

    static IEnumerable<GameObject> Post(GameObject g)
    {
        foreach (var c in g.transform.children) foreach (var d in Post(c.gameObject)) yield return d;
        yield return g;
    }

    static IEnumerable<GameObject> RevLevel(GameObject g)
    {
        var levels = new List<List<GameObject>> { new List<GameObject> { g } };
        while (levels.Last().Count > 0) levels.Add(levels.Last().SelectMany(x => x.transform.children.Select(c => c.gameObject)).ToList());
        return Enumerable.Reverse(levels).SelectMany(l => l);
    }

    static List<GameObject> Order(GameObject g, string order) =>
        (order == "post" ? Post(g) : order == "revpre" ? Pre(g).Reverse() : order == "revlevel" ? RevLevel(g) : Pre(g)).ToList();

    public static void Activate(GameObject go, bool value)
    {
        if (go.selfActive == value) return;
        var was = Pre(go).ToDictionary(g => g, g => g.activeInHierarchy);
        go.selfActive = value;
        var changed = Pre(go).Where(g => g.activeInHierarchy != was[g]).ToList();
        if (!value) { Deactivate(go, changed); return; }
        if (changed.Any(g => g.Behaviours.Any(b => !b.awake)))
        {
            var wake = (WakeParentFirst ? Pre(go) : Post(go)).Where(changed.Contains).ToList();
            foreach (var g in wake) foreach (var c in g.components) if (c is Collider2D || c is Rigidbody2D) Physics2D.Restore(c);
            if (WakePhased)
            {
                foreach (var g in wake) foreach (var b in g.Behaviours.ToList()) Wake(b);
                foreach (var g in wake) foreach (var b in g.Behaviours.ToList()) if (b.enabled) Send(b, "OnEnable");
            }
            else
            {
                foreach (var g in wake)
                    foreach (var b in g.Behaviours.ToList())
                    {
                        Wake(b);
                        if (b.enabled) Send(b, "OnEnable");
                    }
            }
            return;
        }
        foreach (var g in changed) foreach (var c in g.components) if (c is Collider2D || c is Rigidbody2D) Physics2D.Restore(c);
        var behaviours = changed.SelectMany(g => g.Behaviours).Where(b => b.enabled).ToList();
        IEnumerable<MonoBehaviour> ordered;
        if (OnOrder == "add") ordered = behaviours.OrderByDescending(b => b.GetInstanceID());
        else if (OnOrder == "firstawake") ordered = behaviours.OrderBy(b => b.awakeSeq);
        else if (OnOrder == "lastenable") ordered = behaviours.OrderBy(b => b.enableSeq);
        else ordered = Order(go, OnOrder == "post" ? "post" : "pre").Where(changed.Contains).SelectMany(g => g.Behaviours).Where(b => b.enabled);
        foreach (var b in ordered.ToList()) Send(b, "OnEnable");
    }

    static void Deactivate(GameObject go, List<GameObject> changed)
    {
        var objects = Order(go, OffOrder).Where(changed.Contains).ToList();
        var pre = Pre(go).Where(changed.Contains).ToList();
        var events = new List<Component>();
        bool Script(Component c) => c is MonoBehaviour;
        bool Built(Component c) => !(c is MonoBehaviour) && !(c is Transform);
        if (BuiltinModel == "builtinsFirst")
        {
            events.AddRange(pre.SelectMany(g => g.components).Where(Built));
            events.AddRange(objects.SelectMany(g => g.components).Where(Script));
        }
        else if (BuiltinModel == "scriptsFirst")
        {
            events.AddRange(objects.SelectMany(g => g.components).Where(Script));
            events.AddRange(pre.SelectMany(g => g.components).Where(Built));
        }
        else events.AddRange(objects.SelectMany(g => g.components).Where(c => Script(c) || Built(c)));
        foreach (var c in events.ToList())
        {
            if (c is MonoBehaviour b) { if (b.awake && b.enabled) Send(b, "OnDisable"); }
            else if (c is Collider2D || c is Rigidbody2D) Physics2D.Remove(c);
            else if (c is ParticleSystem ps && ParticlesChangeOnDisable) ps.DisableByHierarchy();
        }
    }

    /// <summary>Play mode ends: every active behaviour is disabled, then the scene is dropped.</summary>
    public static void TearDown()
    {
        foreach (var g in Objects.ToList())
            if (g.activeInHierarchy)
                foreach (var b in g.Behaviours.ToList())
                    if (b.awake && b.enabled) { TeardownCallbacks++; Send(b, "OnDisable"); }
        foreach (var b in Behaviours) b.destroyed = true;
        foreach (var g in Objects) { g.destroyed = true; foreach (var c in g.components) c.destroyed = true; }
        Objects.Clear();
        SceneRoots.Clear();
        Behaviours.Clear();
        Coroutines.Clear();
        Physics2D.Clear();
    }

    /// <summary>A fake scene load: objects in document order, parents, component lists, then every
    /// recorder's Awake/OnEnable in the given load order.</summary>
    public static void LoadScene(Type recorder, List<(string Name, string Parent)> gos, Dictionary<string, List<string>> children,
                                 Dictionary<string, List<string>> slots, List<(string Name, string Slot)> wakeOrder)
    {
        var made = new Dictionary<string, GameObject>();
        foreach (var g in gos) made[g.Name] = new GameObject(g.Name);
        foreach (var g in gos.Where(g => g.Parent == null)) SceneRoots.Add(made[g.Name]);
        foreach (var kv in children)
            foreach (var c in kv.Value) made[c].transform.SetParent(made[kv.Key].transform, false);
        var behaviours = new Dictionary<(string, string), MonoBehaviour>();
        foreach (var kv in slots)
            foreach (var slot in kv.Value)
            {
                var b = (MonoBehaviour)made[kv.Key].AddSilently(recorder);
                recorder.GetField("slot").SetValue(b, slot);
                behaviours[(kv.Key, slot)] = b;
            }
        foreach (var w in wakeOrder)
        {
            var b = behaviours[(w.Name, w.Slot)];
            Wake(b);
            if (b.enabled) Send(b, "OnEnable");
        }
    }
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public enum RuntimePlatform { LinuxEditor = 16 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }
    public enum SimulationMode2D { FixedUpdate = 0, Update = 1, Script = 2 }
    public enum RigidbodyType2D { Dynamic = 0, Kinematic = 1, Static = 2 }
    public enum ParticleSystemStopBehavior { StopEmittingAndClear = 0, StopEmitting = 1 }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
    }

    public class Object
    {
        readonly int id = Fake.NewId();
        public string name { get; set; }
        internal bool destroyed;
        public int GetInstanceID() => id;
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed, bn = ReferenceEquals(b, null) || b.destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public static implicit operator bool(Object o) => o != null;
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => id;
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
    }

    public class Transform : Component
    {
        internal Transform parentTransform;
        internal readonly List<Transform> children = new List<Transform>();
        internal Vector3 local;
        public Transform parent => parentTransform;
        public int childCount => children.Count;
        public Transform GetChild(int i) => children[i];
        public Vector3 position
        {
            get => parentTransform == null ? local : parentTransform.position + local;
            set => local = parentTransform == null ? value : value - parentTransform.position;
        }
        public int GetSiblingIndex() => parentTransform != null ? parentTransform.children.IndexOf(this)
            : Fake.Objects.Where(g => g.transform.parentTransform == null).ToList().IndexOf(gameObject);
        public void SetParent(Transform p, bool worldPositionStays)
        {
            var world = position;
            parentTransform?.children.Remove(this);
            parentTransform = p;
            p?.children.Add(this);
            if (worldPositionStays) position = world;
        }
    }

    public class Behaviour : Component
    {
        bool on = true;
        public bool enabled
        {
            get => on;
            set
            {
                if (on == value) return;
                on = value;
                if (this is MonoBehaviour b && b.awake && gameObject.activeInHierarchy) Fake.Send(b, value ? "OnEnable" : "OnDisable");
            }
        }
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public class MonoBehaviour : Behaviour
    {
        internal bool awake, started;
        internal int awakeSeq, enableSeq;
    }

    public class Camera : Behaviour { }

    public class Rigidbody2D : Component
    {
        public RigidbodyType2D bodyType { get; set; }
        public float gravityScale { get; set; } = 1;
        public bool useFullKinematicContacts { get; set; }
        public bool simulated { get; set; } = true;
    }

    public class Collider2D : Behaviour
    {
        public bool isTrigger { get; set; }
        public Rigidbody2D attachedRigidbody
        {
            get
            {
                for (var t = transform; t != null; t = t.parent)
                {
                    var rb = t.gameObject.GetComponent<Rigidbody2D>();
                    if (rb != null) return rb;
                }
                return null;
            }
        }
        public int GetContacts(ContactPoint2D[] contacts) => Physics2D.Count(this);
    }

    public class CircleCollider2D : Collider2D { public float radius { get; set; } = 0.5f; }
    public class BoxCollider2D : Collider2D { public Vector2 size { get; set; } = new Vector2(1, 1); }
    public struct ContactPoint2D { }

    public class Collision2D
    {
        public Collider2D collider { get; internal set; }
        public Collider2D otherCollider { get; internal set; }
        public int contactCount { get; internal set; }
    }

    public static class Physics2D
    {
        public static SimulationMode2D simulationMode { get; set; } = SimulationMode2D.FixedUpdate;
        public static bool callbacksOnDisable { get; set; } = true;
        static readonly List<(Collider2D A, Collider2D B)> Contacts = new List<(Collider2D, Collider2D)>();
        static readonly List<Action> Deferred = new List<Action>();
        static readonly HashSet<Component> Removed = new HashSet<Component>();

        internal static void Clear() { Contacts.Clear(); Deferred.Clear(); Removed.Clear(); }

        static bool Live(Collider2D c) => c != null && c.enabled && c.gameObject.activeInHierarchy && !Removed.Contains(c)
                                          && (c.attachedRigidbody == null || !Removed.Contains(c.attachedRigidbody));

        static IEnumerable<Collider2D> All() => Fake.Objects.SelectMany(g => g.components).OfType<Collider2D>();

        public static bool Simulate(float step)
        {
            if (simulationMode != SimulationMode2D.Script) return false;
            foreach (var d in Deferred.ToList()) d();
            Deferred.Clear();
            var live = All().Where(Live).ToList();
            for (int i = 0; i < live.Count; i++)
                for (int j = i + 1; j < live.Count; j++)
                {
                    var a = live[i];
                    var b = live[j];
                    if (a.attachedRigidbody == null && b.attachedRigidbody == null) continue;
                    if (a.attachedRigidbody != null && a.attachedRigidbody == b.attachedRigidbody) continue;
                    var d = a.transform.position - b.transform.position;
                    if (Math.Sqrt(d.x * d.x + d.y * d.y) >= Fake.ContactRange) continue;
                    if (Contacts.Any(c => c.A == a && c.B == b)) continue;
                    Contacts.Add((a, b));
                    Notify(a, b, "Enter");
                }
            return true;
        }

        /// <summary>The fake message set of one contact begin or end: the first collider's object, its
        /// body's object if different, then the other collider's object.</summary>
        static void Notify(Collider2D a, Collider2D b, string phase)
        {
            bool trigger = a.isTrigger || b.isTrigger;
            var targets = new List<(GameObject, Collider2D, Collider2D)> { (a.gameObject, a, b) };
            if (a.attachedRigidbody != null && a.attachedRigidbody.gameObject != a.gameObject) targets.Add((a.attachedRigidbody.gameObject, a, b));
            targets.Add((b.gameObject, b, a));
            foreach (var (go, self, other) in targets)
                foreach (var m in go.Behaviours.ToList())
                {
                    if (trigger) Fake.Send(m, "OnTrigger" + phase + "2D", other);
                    else Fake.Send(m, "OnCollision" + phase + "2D", new Collision2D { collider = other, otherCollider = self, contactCount = 1 });
                }
        }

        internal static void Remove(Component c)
        {
            Removed.Add(c);
            var gone = Contacts.Where(p => !Live(p.A) || !Live(p.B)).ToList();
            foreach (var p in gone)
            {
                Contacts.Remove(p);
                if (!callbacksOnDisable || Fake.ExitPlacement == "none") continue;
                var (a, b) = Removed.Contains(p.B) || !Live(p.B) && Live(p.A) ? (p.B, p.A) : (p.A, p.B);
                if (Fake.ExitPlacement == "deferred") Deferred.Add(() => Notify(a, b, "Exit"));
                else Notify(a, b, "Exit");
            }
        }

        internal static void Restore(Component c) => Removed.Remove(c);

        static bool Hidden(Collider2D c) => Fake.ReadsFollowFlags && !c.gameObject.activeInHierarchy;

        public static bool IsTouching(Collider2D a, Collider2D b) =>
            !Hidden(a) && !Hidden(b) && Contacts.Any(p => p.A == a && p.B == b || p.A == b && p.B == a);

        internal static int Count(Collider2D c) => Hidden(c) ? 0 : Contacts.Count(p => p.A == c || p.B == c);
    }

    public class ParticleSystem : Component
    {
        internal bool playing = true, emitting = true, playOnAwake = true;
        internal int particles;

        public struct MainModule
        {
            internal ParticleSystem owner;
            public bool playOnAwake { get => owner.playOnAwake; set => owner.playOnAwake = value; }
        }

        public struct EmissionModule
        {
            internal ParticleSystem owner;
            public bool enabled { get => owner.emitting; set => owner.emitting = value; }
        }

        public MainModule main => new MainModule { owner = this };
        public EmissionModule emission => new EmissionModule { owner = this };
        bool Hidden => Fake.ReadsFollowFlags && !gameObject.activeInHierarchy;
        public bool isPlaying => !Hidden && playing;
        public bool isEmitting => !Hidden && playing && emitting;
        public bool isPaused => false;
        public bool isStopped => Hidden || !playing;
        public int particleCount => Hidden ? 0 : particles;
        public void Stop(bool withChildren, ParticleSystemStopBehavior behavior)
        {
            playing = false;
            if (behavior == ParticleSystemStopBehavior.StopEmittingAndClear) particles = 0;
        }
        public void Play(bool withChildren) { playing = true; }
        public void Emit(int count) { particles += count; }
        internal void DisableByHierarchy() { playing = false; particles = 0; }
    }

    public class ParticleSystemRenderer : Component { }

    public class GameObject : Object
    {
        internal bool selfActive = true;
        internal readonly List<Component> components = new List<Component>();
        internal IEnumerable<MonoBehaviour> Behaviours => components.OfType<MonoBehaviour>();
        public Transform transform { get; }
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            components.Add(transform);
            Fake.Objects.Add(this);
        }
        public bool activeSelf => selfActive;
        public bool activeInHierarchy => selfActive && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public void SetActive(bool value) => Fake.Activate(this, value);

        internal Component AddSilently(Type type)
        {
            var c = (Component)Activator.CreateInstance(type);
            c.gameObject = this;
            components.Add(c);
            if (c is MonoBehaviour b) Fake.Behaviours.Add(b);
            return c;
        }

        public T AddComponent<T>() where T : Component, new()
        {
            if (++Fake.Adds == Fake.FailAddAt) throw new InvalidOperationException("fake AddComponent failure");
            var c = (T)AddSilently(typeof(T));
            if (c is ParticleSystem) AddSilently(typeof(ParticleSystemRenderer));
            if (c is MonoBehaviour b && activeInHierarchy)
            {
                Fake.Wake(b);
                if (b.enabled) Fake.Send(b, "OnEnable");
            }
            return c;
        }

        public T GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
        public T[] GetComponents<T>() where T : class => components.OfType<T>().ToArray();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class =>
            Fake.Pre(this).SelectMany(g => g.components.OfType<T>()).ToArray();
    }

    public static class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceived;
        public static string unityVersion => "6000.0.71f1";
        public static bool isBatchMode => true;
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static void Log(LogType type, string message) => logMessageReceived?.Invoke(message, "", type);
        public static void ClearLogHandlers() => logMessageReceived = null;
        public static int HandlerCount => logMessageReceived?.GetInvocationList().Length ?? 0;
    }

    public static class SystemInfo
    {
        public static Rendering.GraphicsDeviceType graphicsDeviceType => Rendering.GraphicsDeviceType.OpenGLCore;
        public static string graphicsDeviceName => "Fake GPU";
    }

    public static class Time
    {
        public static int frameCount => Fake.Frame;
        public static double realtimeSinceStartupAsDouble => 1.25 + Fake.Frame * 0.001;
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { Null = 4, OpenGLCore = 17 }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public GameObject[] GetRootGameObjects() =>
            Fake.SceneRoots.Concat(Fake.Objects.Where(g => g.transform.parent == null && !Fake.SceneRoots.Contains(g))).ToArray();
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() => default;
    }
}

namespace UnityEditor
{
    public class InitializeOnLoadAttribute : Attribute { }
    public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }
    public enum BuildTarget { StandaloneLinux64 = 24 }
    public static class EditorUserBuildSettings { public static BuildTarget activeBuildTarget => BuildTarget.StandaloneLinux64; }
    public static class SessionState
    {
        static readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public static bool GetBool(string k, bool d) => Values.TryGetValue(k, out var v) ? (bool)v : d;
        public static void SetBool(string k, bool v) => Values[k] = v;
        public static int GetInt(string k, int d) => Values.TryGetValue(k, out var v) ? (int)v : d;
        public static void SetInt(string k, int v) => Values[k] = v;
        public static float GetFloat(string k, float d) => Values.TryGetValue(k, out var v) ? (float)v : d;
        public static void SetFloat(string k, float v) => Values[k] = v;
    }
    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction update;
        public static CallbackFunction delayCall;
        static event Action<PlayModeStateChange> stateHandlers;
        public static event Action<PlayModeStateChange> playModeStateChanged { add => stateHandlers += value; remove => stateHandlers -= value; }
        public static bool isPlaying;
        public static bool isPaused;
        public static bool isPlayingOrWillChangePlaymode => isPlaying || Fake.PendingEnter;
        public static double timeSinceStartup => Fake.EditorTime;
        public static int StateHandlerCount => stateHandlers?.GetInvocationList().Length ?? 0;
        public static int UpdateHandlerCount => update?.GetInvocationList().Length ?? 0;
        static void Transition(string what)
        {
            Fake.Transitions.Add(what + " from " + Fake.Context);
            if (Fake.Context != "update" && Fake.Context != "delayCall") Fake.Forbid(what + " outside an Editor update/delayCall");
        }
        public static void EnterPlaymode() { Transition("EnterPlaymode"); Fake.PendingEnter = true; }
        public static void ExitPlaymode() { Transition("ExitPlaymode"); Fake.PendingExit = true; }
        public static void Exit(int code)
        {
            Transition("Exit " + code);
            if (Fake.ExitCode != null) Fake.Forbid("second Exit");
            Fake.ExitCode = code;
            if (Fake.Context == "update" && (UpdateHandlerCount != 0 || StateHandlerCount != 0))
                Fake.Forbid("Exit with the runner's handlers still subscribed");
        }
        public static void Fire(PlayModeStateChange state) => stateHandlers?.Invoke(state);
        public static void DropDomain() { update = null; delayCall = null; stateHandlers = null; }
    }
}

namespace UnityEditor.SceneManagement
{
    public enum OpenSceneMode { Single }
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene OpenScene(string path, OpenSceneMode mode = OpenSceneMode.Single)
        {
            if (path != "Assets/Scenes/Probe.unity") throw new ArgumentException("fake: unknown scene " + path);
            return default;
        }
    }
}
