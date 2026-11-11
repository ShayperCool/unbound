// Fake Unity API for the offline guard controls (controls.py). It lets the probe's two real
// scripts run outside Unity with deterministic answers chosen per scenario; no Unity behaviour is
// claimed. The fake activation traverses the changed subtree in a configurable order (pre-order,
// post-order or reverse pre-order, separately for plain and batch calls) and wakes a never-awoken
// subtree parent or child first, interleaved or phased. Behaviour messages are sent by
// reflection; Start runs once per behaviour in a later frame; coroutines resume one step per
// frame. Editor transitions are recorded with the callback they were made from; calls the
// protocol forbids go to Fake.Forbidden.
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

    /// <summary>Traversal of an already-awake changed subtree: "pre", "post" or "revpre".</summary>
    public static string PlainOff = "pre", PlainOn = "pre", BatchOff = "pre", BatchOn = "pre";
    /// <summary>First wake: parent or child first; Awake/OnEnable interleaved or phased.</summary>
    public static bool WakeParentFirst = true, WakePhased;
    /// <summary>The n-th AddComponent throws (0: never).</summary>
    public static int FailAddAt;
    public static int Adds;
    public static int NativeLive;
    public static int TeardownCallbacks;

    public static readonly List<GameObject> Objects = new List<GameObject>();
    public static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();
    static readonly List<(IEnumerator Routine, int Frame)> Coroutines = new List<(IEnumerator, int)>();
    static int nextId = -1000;
    public static int NewId() { nextId -= 2; return nextId; }

    public static void Send(MonoBehaviour b, string message)
    {
        var method = b.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null) return;
        object result;
        try { result = method.Invoke(b, null); }
        catch (TargetInvocationException e) { Forbidden.Add(message + " threw " + e.InnerException?.Message); return; }
        if (result is IEnumerator routine)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { Forbidden.Add(message + " coroutine threw " + e.Message); return; }
            if (more) Coroutines.Add((routine, Frame));
        }
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

    public static int CoroutineCount => Coroutines.Count;

    static IEnumerable<GameObject> Pre(GameObject g)
    {
        yield return g;
        foreach (var c in g.transform.children) foreach (var d in Pre(c.gameObject)) yield return d;
    }

    static IEnumerable<GameObject> Post(GameObject g)
    {
        foreach (var c in g.transform.children) foreach (var d in Post(c.gameObject)) yield return d;
        yield return g;
    }

    static IEnumerable<GameObject> Order(GameObject g, string order) =>
        order == "post" ? Post(g) : order == "revpre" ? Pre(g).Reverse() : Pre(g);

    public static void Activate(GameObject go, bool value, bool batch)
    {
        if (go.selfActive == value) return;
        var was = Pre(go).ToDictionary(g => g, g => g.activeInHierarchy);
        go.selfActive = value;
        var changed = Pre(go).Where(g => g.activeInHierarchy != was[g]).ToList();
        string order = value ? (batch ? BatchOn : PlainOn) : (batch ? BatchOff : PlainOff);
        var nodes = Order(go, order).Where(changed.Contains).ToList();
        if (!value)
        {
            foreach (var g in nodes) foreach (var b in g.Behaviours.ToList()) if (b.awake && b.enabled) Send(b, "OnDisable");
            return;
        }
        if (nodes.Any(g => g.Behaviours.Any(b => !b.awake)))
        {
            var wake = (WakeParentFirst ? Pre(go) : Post(go)).Where(changed.Contains).ToList();
            if (WakePhased)
            {
                foreach (var g in wake) foreach (var b in g.Behaviours.ToList()) if (!b.awake) { b.awake = true; Send(b, "Awake"); }
                foreach (var g in wake) foreach (var b in g.Behaviours.ToList()) if (b.enabled) Send(b, "OnEnable");
            }
            else
            {
                foreach (var g in wake)
                    foreach (var b in g.Behaviours.ToList())
                    {
                        if (!b.awake) { b.awake = true; Send(b, "Awake"); }
                        if (b.enabled) Send(b, "OnEnable");
                    }
            }
            return;
        }
        foreach (var g in nodes) foreach (var b in g.Behaviours.ToList()) if (b.enabled) Send(b, "OnEnable");
    }

    /// <summary>Play mode ends: every active behaviour is disabled, then the scene is dropped.</summary>
    public static void TearDown()
    {
        foreach (var g in Objects.ToList())
            if (g.activeInHierarchy)
                foreach (var b in g.Behaviours.ToList())
                    if (b.awake && b.enabled) { TeardownCallbacks++; Send(b, "OnDisable"); }
        foreach (var b in Behaviours) b.destroyed = true;
        foreach (var g in Objects) g.destroyed = true;
        Objects.Clear();
        Behaviours.Clear();
        Coroutines.Clear();
    }
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public enum RuntimePlatform { LinuxEditor = 16 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
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
        public Transform parent => parentTransform;
        public int childCount => children.Count;
        public Transform GetChild(int i) => children[i];
        public int GetSiblingIndex() => parentTransform != null ? parentTransform.children.IndexOf(this) : Fake.Objects.Where(g => g.transform.parentTransform == null).ToList().IndexOf(gameObject);
        public void SetParent(Transform p, bool worldPositionStays)
        {
            parentTransform?.children.Remove(this);
            parentTransform = p;
            p?.children.Add(this);
        }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    public class MonoBehaviour : Behaviour
    {
        internal bool awake, started;
    }

    public class Camera : Behaviour { }

    public class GameObject : Object
    {
        internal bool selfActive = true;
        internal readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();
        public Transform transform { get; }
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            Fake.Objects.Add(this);
        }
        public bool activeSelf => selfActive;
        public bool activeInHierarchy => selfActive && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public void SetActive(bool value) => Fake.Activate(this, value, false);
        public T AddComponent<T>() where T : Component, new()
        {
            if (++Fake.Adds == Fake.FailAddAt) throw new InvalidOperationException("fake AddComponent failure");
            var c = new T { gameObject = this };
            if (c is MonoBehaviour b)
            {
                Behaviours.Add(b);
                Fake.Behaviours.Add(b);
                if (activeInHierarchy)
                {
                    b.awake = true;
                    Fake.Send(b, "Awake");
                    if (b.enabled) Fake.Send(b, "OnEnable");
                }
            }
            return c;
        }

        static GameObject ById(int id) => Fake.Objects.FirstOrDefault(g => g.GetInstanceID() == id && !g.destroyed);

        public static void SetGameObjectsActive(ReadOnlySpan<int> instanceIDs, bool active)
        {
            foreach (int id in instanceIDs)
            {
                var g = ById(id);
                if (g != null) Fake.Activate(g, active, true);
            }
        }

        public static void SetGameObjectsActive(Unity.Collections.NativeArray<int> instanceIDs, bool active)
        {
            if (!instanceIDs.IsCreated) throw new ArgumentException("fake: the NativeArray is not created");
            SetGameObjectsActive(instanceIDs.AsReadOnlySpan(), active);
        }
    }

    public static class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceived;
        public static string unityVersion => "6000.0.71f1";
        public static bool isBatchMode => false;
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
    public struct Scene { }
}

namespace Unity.Collections
{
    public enum Allocator { Invalid = 0, None = 1, Temp = 2, TempJob = 3, Persistent = 4 }

    public struct NativeArray<T> : IDisposable where T : struct
    {
        T[] items;
        public NativeArray(T[] array, Allocator allocator)
        {
            items = (T[])array.Clone();
            Fake.NativeLive++;
        }
        public bool IsCreated => items != null;
        public ReadOnlySpan<T> AsReadOnlySpan() => items;
        public void Dispose()
        {
            if (items == null) throw new InvalidOperationException("fake: the NativeArray is not created or already disposed");
            items = null;
            Fake.NativeLive--;
        }
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
