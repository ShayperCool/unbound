// Fake Unity API for the offline guard controls (controls.py). It runs the probe's two real scripts
// outside Unity with a deterministic toy lifecycle; no Unity behaviour is claimed. A small fake
// default PlayerLoop (Unity's group/leaf type names, a subset of leaves) is executed leaf by leaf;
// managed leaves (the probe's markers) run their delegates. Destroy follows one selectable toy
// rule (Fake.Rule), so a control can check that the analyzer recovers the rule it was given:
//   delayed    OnDisable before returning, only isActiveAndEnabled changes, OnDestroy in the next
//              ScriptRunDelayed* leaf (CALLBACK_INACTIVE / C_INACTIVE_FLAG, NEXT_DELAYED)
//   drains     as SetActive(false) / enabled = false before returning, OnDestroy in the next
//              PlayerSendFrameStarted/Complete leaf (DEACTIVATE / C_DISABLE, FRAME_DRAINS)
//   immediate  OnDisable and OnDestroy before returning, Unity-null at once (IMMEDIATE / C_IMMEDIATE)
//   nullnext   OnDisable before returning, Unity-null at once, OnDestroy in the first leaf of the
//              next frame (NULL_PENDING / C_NULL_PENDING, NEXT_FRAME_START)
// WaitForEndOfFrame coroutines resume (Fake.EndOfFrameAt) inside the
// PostLateUpdate.TriggerEndOfFrameCallbacks leaf ("leaf", default), right after that leaf's
// after-marker and before the next leaf ("afterMarker", i.e. after the probe's teofc point), or
// never ("never", the shape a batchmode Editor without a rendered Game view may have); none of
// them is a claim about Unity.
// Editor transitions are recorded with the callback they were made from; calls the protocol
// forbids go to Fake.Forbidden.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.LowLevel;

public static class Fake
{
    public static readonly List<string> Forbidden = new List<string>();
    public static readonly List<string> Transitions = new List<string>();
    public static string Context = "none";
    public static double EditorTime;
    public static int? ExitCode;
    public static bool PendingEnter, PendingExit;
    public static int Frame = 1;
    public static string Rule = "delayed";
    /// <summary>A second Destroy of an object already being destroyed throws.</summary>
    public static bool ReentrantThrows;
    /// <summary>FindObjectsByType throws from this probe frame on (a probe fault; 0: never).</summary>
    public static int FaultAtFrame;
    /// <summary>The read-back loop after SetPlayerLoop loses the last Update leaf (a broken install).</summary>
    public static bool ReadBackDropsLeaf;
    /// <summary>On this frame the PreUpdate group (its leaf and the probe's markers) does not run (0: never).</summary>
    public static int SkipLeafAtFrame;
    /// <summary>FixedUpdate iterations on odd frames (even frames: 1).</summary>
    public static int FixedOnOddFrames = 1;
    /// <summary>Where WaitForEndOfFrame coroutines resume: "leaf", "afterMarker" or "never".</summary>
    public static string EndOfFrameAt = "leaf";
    const string EndOfFrameLeaf = "PostLateUpdate.TriggerEndOfFrameCallbacks";
    public static int NextId = 41000;
    public static void Forbid(string what) { Forbidden.Add(what + " in " + Context); throw new Exception("forbidden call: " + what); }

    // ------------------------------------------------------------------ objects

    public static readonly List<GameObject> Objects = new List<GameObject>();
    public static readonly List<Component> Pending = new List<Component>();
    public static readonly List<GameObject> PendingObjects = new List<GameObject>();
    static long sequence;
    public static long Sequence() => ++sequence;

    public static void Message(Component c, string name)
    {
        var m = c.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null || m.GetParameters().Length != 0) return;
        try { m.Invoke(c, null); }
        catch (TargetInvocationException e)
        {
            var inner = e.InnerException ?? e;
            UnityEngine.Application.Log(LogType.Exception, inner.GetType().Name + ": " + inner.Message);
        }
    }

    static int Order(Component c)
    {
        var a = c.GetType().GetCustomAttribute<DefaultExecutionOrder>();
        return a == null ? 0 : a.order;
    }

    static IEnumerable<MonoBehaviour> Behaviours() => Objects.ToList()
        .SelectMany(g => g.Components).OfType<MonoBehaviour>().OrderBy(Order).ThenBy(b => b.created).ToList();

    public static IEnumerable<GameObject> Subtree(GameObject g)
    {
        yield return g;
        foreach (var child in g.T.children.ToList())
            foreach (var x in Subtree(child.Owner)) yield return x;
    }

    /// <summary>The toy behaviour state: OnEnable ran and OnDisable has not.</summary>
    public static void Refresh(GameObject root)
    {
        foreach (var g in Subtree(root).ToList())
            foreach (var b in g.Components.OfType<Behaviour>().ToList())
                Refresh(b);
    }

    public static void Refresh(Behaviour b)
    {
        bool want = !b.destroyed && !b.pending && b.enabledFlag && b.Owner.InHierarchy;
        if (want == b.live) return;
        if (want && !b.awoken) { b.awoken = true; Message(b, "Awake"); }
        b.live = want;
        Message(b, want ? "OnEnable" : "OnDisable");
    }

    public static void Destroy(UnityEngine.Object obj)
    {
        if (ReferenceEquals(obj, null)) return;
        bool already = obj.destroyed || (obj is Component pc && pc.pending) || (obj is GameObject pg && pg.pending);
        if (already)
        {
            if (ReentrantThrows) throw new InvalidOperationException("fake: object already being destroyed");
            return;
        }
        if (obj is GameObject g) DestroyObject(g);
        else if (obj is Behaviour b) DestroyComponent(b);
        else throw new ArgumentException("fake: unsupported Destroy target");
    }

    static void DestroyObject(GameObject g)
    {
        switch (Rule)
        {
            case "delayed":
                g.pending = true;
                foreach (var x in Subtree(g).ToList()) foreach (var b in x.Components.OfType<Behaviour>().ToList()) { b.pending = true; Disable(b); }
                PendingObjects.Add(g);
                break;
            case "drains":
                g.SetActive(false);
                g.pending = true;
                foreach (var x in Subtree(g).ToList()) foreach (var b in x.Components.OfType<Behaviour>()) b.pending = true;
                PendingObjects.Add(g);
                break;
            case "immediate":
                g.pending = true;
                foreach (var x in Subtree(g).ToList()) foreach (var b in x.Components.OfType<Behaviour>().ToList()) { b.pending = true; Disable(b); }
                Finish(g);
                break;
            case "nullnext":
                g.pending = true;
                foreach (var x in Subtree(g).ToList()) foreach (var b in x.Components.OfType<Behaviour>().ToList()) { b.pending = true; Disable(b); }
                foreach (var x in Subtree(g).ToList()) { x.destroyed = true; foreach (var c in x.Components) c.destroyed = true; }
                PendingObjects.Add(g);
                break;
            default: throw new InvalidOperationException("fake: unknown rule " + Rule);
        }
    }

    static void Disable(Behaviour b)
    {
        if (!b.live) return;
        b.live = false;
        Message(b, "OnDisable");
    }

    static void DestroyComponent(Behaviour b)
    {
        switch (Rule)
        {
            case "delayed": b.pending = true; Disable(b); Pending.Add(b); break;
            case "drains": b.enabled = false; b.pending = true; Pending.Add(b); break;
            case "immediate": b.pending = true; Disable(b); FinishComponent(b); break;
            case "nullnext": b.pending = true; Disable(b); b.destroyed = true; Pending.Add(b); break;
            default: throw new InvalidOperationException("fake: unknown rule " + Rule);
        }
    }

    static void Finish(GameObject g)
    {
        foreach (var x in Subtree(g).ToList())
        {
            foreach (var c in x.Components.ToList())
            {
                if (c is Behaviour b && b.awoken) Message(b, "OnDestroy");
                c.destroyed = true;
            }
            x.destroyed = true;
            Objects.Remove(x);
        }
        if (g.T.parentTransform != null) g.T.parentTransform.children.Remove(g.T);
    }

    static void FinishComponent(Behaviour b)
    {
        if (b.awoken) Message(b, "OnDestroy");
        b.destroyed = true;
        b.Owner.Components.Remove(b);
    }

    /// <summary>Delivers the queued destruction (the rule's drain point).</summary>
    public static void Flush()
    {
        var objects = PendingObjects.ToList();
        var components = Pending.ToList();
        PendingObjects.Clear();
        Pending.Clear();
        foreach (var g in objects) Finish(g);
        foreach (var b in components) FinishComponent((Behaviour)b);
    }

    /// <summary>Play mode exit: every object is disabled and destroyed (the probe's OnDestroy restores its loop).</summary>
    public static void DestroyAll()
    {
        foreach (var g in Objects.Where(o => o.T.parentTransform == null).ToList())
        {
            foreach (var x in Subtree(g).ToList()) foreach (var b in x.Components.OfType<Behaviour>().ToList()) Disable(b);
            Finish(g);
        }
        PendingObjects.Clear();
        Pending.Clear();
        Coroutines.Clear();
    }

    // ------------------------------------------------------------------ coroutines

    public sealed class Routine { public MonoBehaviour Owner; public IEnumerator Body; public bool EndOfFrame; }
    public static readonly List<Routine> Coroutines = new List<Routine>();

    public static void Step(Routine r)
    {
        if (!r.Body.MoveNext()) { Coroutines.Remove(r); return; }
        r.EndOfFrame = r.Body.Current is WaitForEndOfFrame;
    }

    static void EndOfFrame()
    {
        foreach (var r in Coroutines.ToList())
            if (r.EndOfFrame && !r.Owner.destroyed && !r.Owner.pending && r.Owner.Owner.InHierarchy) Step(r);
    }

    // ------------------------------------------------------------------ loop

    public static readonly Dictionary<string, int> NativeVisits = new Dictionary<string, int>();
    public static readonly List<string> NativeOrder = new List<string>();

    public static void Native(string group, string leaf)
    {
        string name = group + "." + leaf;
        NativeVisits[name] = NativeVisits.TryGetValue(name, out var n) ? n + 1 : 1;
        if (leaf.StartsWith("ScriptRunDelayed") && Rule == "delayed") Flush();
        switch (name)
        {
            case "TimeUpdate.WaitForLastPresentationAndUpdateTime":
                Frame++;
                if (Rule == "nullnext") Flush();
                break;
            case "EarlyUpdate.ScriptRunDelayedStartupFrame":
                foreach (var b in Behaviours().ToList())
                    if (b.live && !b.started) { b.started = true; Message(b, "Start"); }
                break;
            case "Update.ScriptRunBehaviourUpdate":
                foreach (var b in Behaviours().ToList()) if (b.live) Message(b, "Update");
                break;
            case "PreLateUpdate.ScriptRunBehaviourLateUpdate":
                foreach (var b in Behaviours().ToList()) if (b.live) Message(b, "LateUpdate");
                break;
            case "PostLateUpdate.PlayerSendFrameStarted":
            case "PostLateUpdate.PlayerSendFrameComplete":
                if (Rule == "drains") Flush();
                break;
            case EndOfFrameLeaf:
                if (EndOfFrameAt == "leaf") EndOfFrame();
                break;
        }
    }

    public static PlayerLoopSystem DefaultLoop()
    {
        var groups = new (Type group, Type[] leaves)[]
        {
            (typeof(UnityEngine.PlayerLoop.TimeUpdate), new[] { typeof(UnityEngine.PlayerLoop.TimeUpdate.WaitForLastPresentationAndUpdateTime) }),
            (typeof(UnityEngine.PlayerLoop.EarlyUpdate), new[] { typeof(UnityEngine.PlayerLoop.EarlyUpdate.UpdateInputManager),
                typeof(UnityEngine.PlayerLoop.EarlyUpdate.ScriptRunDelayedStartupFrame) }),
            (typeof(UnityEngine.PlayerLoop.FixedUpdate), new[] { typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
                typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunDelayedFixedFrameRate) }),
            (typeof(UnityEngine.PlayerLoop.PreUpdate), new[] { typeof(UnityEngine.PlayerLoop.PreUpdate.CheckTexFieldInput) }),
            (typeof(UnityEngine.PlayerLoop.Update), new[] { typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                typeof(UnityEngine.PlayerLoop.Update.ScriptRunDelayedDynamicFrameRate), typeof(UnityEngine.PlayerLoop.Update.DirectorUpdate) }),
            (typeof(UnityEngine.PlayerLoop.PreLateUpdate), new[] { typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate) }),
            (typeof(UnityEngine.PlayerLoop.PostLateUpdate), new[] { typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerSendFrameStarted),
                typeof(UnityEngine.PlayerLoop.PostLateUpdate.ScriptRunDelayedDynamicFrameRate), typeof(UnityEngine.PlayerLoop.PostLateUpdate.UpdateAllRenderers),
                typeof(UnityEngine.PlayerLoop.PostLateUpdate.TriggerEndOfFrameCallbacks), typeof(UnityEngine.PlayerLoop.PostLateUpdate.PlayerSendFrameComplete) }),
        };
        int fn = 0x1000;
        return new PlayerLoopSystem
        {
            subSystemList = groups.Select(g => new PlayerLoopSystem
            {
                type = g.group,
                loopConditionFunction = g.group == typeof(UnityEngine.PlayerLoop.FixedUpdate) ? (IntPtr)0x77 : IntPtr.Zero,
                subSystemList = g.leaves.Select(l => new PlayerLoopSystem { type = l, updateFunction = (IntPtr)(fn += 8) }).ToArray(),
            }).ToArray(),
        };
    }

    public static PlayerLoopSystem Current = DefaultLoop();
    public static int SetCount;

    public static void RunFrame()
    {
        foreach (var group in Current.subSystemList)
        {
            string name = group.type == null ? "?" : group.type.Name;
            int times = name == "FixedUpdate" ? (Frame % 2 == 1 ? FixedOnOddFrames : 1) : 1;
            if (SkipLeafAtFrame != 0 && Frame == SkipLeafAtFrame && name == "PreUpdate") times = 0;
            for (int i = 0; i < times; i++)
            {
                string previous = null;
                foreach (var leaf in group.subSystemList ?? new PlayerLoopSystem[0])
                {
                    if (leaf.updateDelegate != null)
                    {
                        try { leaf.updateDelegate(); }
                        catch (Exception e) { UnityEngine.Application.Log(LogType.Exception, e.GetType().Name + ": " + e.Message); }
                        // The managed system right after the end-of-frame leaf is the probe's after-marker.
                        if (previous == EndOfFrameLeaf && EndOfFrameAt == "afterMarker") EndOfFrame();
                        previous = null;
                    }
                    else if (leaf.type != null)
                    {
                        Native(name, leaf.type.Name);
                        previous = name + "." + leaf.type.Name;
                    }
                }
            }
        }
    }

    /// <summary>Flat "Group.Leaf" names of a loop's native leaves and the count of managed leaves.</summary>
    public static (List<string> native, int managed) Flatten(PlayerLoopSystem loop)
    {
        var native = new List<string>();
        int managed = 0;
        foreach (var g in loop.subSystemList ?? new PlayerLoopSystem[0])
            foreach (var l in g.subSystemList ?? new PlayerLoopSystem[0])
                if (l.updateDelegate != null) managed++;
                else native.Add(g.type.Name + "." + l.type.Name);
        return (native, managed);
    }

    public static void ResetPlay()
    {
        Objects.Clear(); Pending.Clear(); PendingObjects.Clear(); Coroutines.Clear();
        NativeVisits.Clear();
        Current = DefaultLoop();
        SetCount = 0;
        UnityEngine.Time.captureFramerate = 0;
    }
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public enum RuntimePlatform { LinuxEditor = 16 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None, InstanceID }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { this.order = order; }
        public int order { get; }
    }

    public class MissingReferenceException : Exception
    {
        public MissingReferenceException(string message) : base(message) { }
    }

    public class Object
    {
        internal bool destroyed;
        internal readonly long created = Fake.Sequence();
        readonly int id = Fake.NextId += 2;
        string objectName;
        public string name { get { Check(); return objectName; } set { Check(); objectName = value; } }
        internal string RawName => objectName;
        internal void Check()
        {
            if (destroyed) throw new MissingReferenceException("fake: the object has been destroyed");
        }
        public int GetInstanceID() => id;
        public static void Destroy(Object obj) => Fake.Destroy(obj);
        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode mode) where T : Object
        {
            if (Fake.FaultAtFrame != 0 && Fake.Frame >= Fake.FaultAtFrame) throw new InvalidOperationException("fake fault in FindObjectsByType");
            return Fake.Objects.SelectMany(g => g.Components).OfType<T>()
                .Where(c => !c.destroyed && (inactive == FindObjectsInactive.Include || ((Component)(object)c).Owner.InHierarchy)).ToArray();
        }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed, bn = ReferenceEquals(b, null) || b.destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public static implicit operator bool(Object o) => o != null;
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
    }

    public class Component : Object
    {
        internal bool pending;
        GameObject owner;
        public GameObject gameObject { get { Check(); return owner; } internal set => owner = value; }
        internal GameObject Owner => owner;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    public class Behaviour : Component
    {
        internal bool enabledFlag = true, live, awoken;
        public bool enabled
        {
            get { Check(); return enabledFlag; }
            set { Check(); enabledFlag = value; Fake.Refresh(this); }
        }
        public bool isActiveAndEnabled { get { Check(); return live; } }
    }

    public class Coroutine { }

    public class MonoBehaviour : Behaviour
    {
        internal bool started;
        public Coroutine StartCoroutine(System.Collections.IEnumerator routine)
        {
            var r = new Fake.Routine { Owner = this, Body = routine };
            Fake.Coroutines.Add(r);
            Fake.Step(r);
            return new Coroutine();
        }
    }

    public class Camera : Behaviour { }

    public sealed class WaitForEndOfFrame { }

    public class Transform : Component
    {
        internal Transform parentTransform;
        internal readonly List<Transform> children = new List<Transform>();
        public Transform parent { get { Check(); return parentTransform; } }
        public int childCount { get { Check(); return children.Count; } }
        public void SetParent(Transform p, bool worldPositionStays)
        {
            Check();
            if (parentTransform != null) parentTransform.children.Remove(this);
            parentTransform = p;
            if (p != null) p.children.Add(this);
            Fake.Refresh(gameObject);
        }
    }

    public class GameObject : Object
    {
        internal bool pending, activeFlag = true;
        internal readonly List<Component> Components = new List<Component>();
        readonly Transform own;
        public GameObject(string name)
        {
            this.name = name;
            own = new Transform();
            own.gameObject = this;
            Components.Add(own);
            Fake.Objects.Add(this);
        }
        public Transform transform { get { Check(); return own; } }
        internal Transform T => own;
        public bool activeSelf { get { Check(); return activeFlag; } }
        public bool activeInHierarchy { get { Check(); return InHierarchy; } }
        internal bool InHierarchy => !destroyed && activeFlag && (own.parentTransform == null || own.parentTransform.Owner.InHierarchy);
        public void SetActive(bool value)
        {
            Check();
            activeFlag = value;
            Fake.Refresh(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            Check();
            var c = new T();
            c.gameObject = this;
            Components.Add(c);
            if (c is Behaviour b)
            {
                if (InHierarchy) { b.awoken = true; Fake.Message(b, "Awake"); }
                Fake.Refresh(b);
            }
            return c;
        }
        public T GetComponent<T>() where T : Component
        {
            Check();
            return Components.Where(c => !c.destroyed).OfType<T>().FirstOrDefault();
        }
        public static GameObject Find(string name)
            => Fake.Objects.FirstOrDefault(g => !g.destroyed && g.InHierarchy && g.RawName == name);
    }

    public static class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceived;
        public static string unityVersion => "6000.0.71f1";
        public static bool isBatchMode => false;
        public static bool isEditor => true;
        public static RuntimePlatform platform => RuntimePlatform.LinuxEditor;
        public static void Log(LogType type, string message) => logMessageReceived?.Invoke(message, "", type);
        public static void ClearLogHandlers() => logMessageReceived = null;
        public static int HandlerCount => logMessageReceived?.GetInvocationList().Length ?? 0;
    }

    public static class SystemInfo
    {
        public static UnityEngine.Rendering.GraphicsDeviceType graphicsDeviceType => UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore;
        public static string graphicsDeviceName => "Fake GPU";
    }

    public static class Time
    {
        public static int captureFramerate { get; set; }
        public static float fixedDeltaTime => 0.02f;
        public static int frameCount => Fake.Frame;
        public static double realtimeSinceStartupAsDouble => 1.25 + Fake.Frame * 0.001;
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { OpenGLCore = 17, Null = 4 }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name => "Probe"; }
    public static class SceneManager { public static Scene GetActiveScene() => default; }
}

namespace UnityEngine.LowLevel
{
    public struct PlayerLoopSystem
    {
        public delegate void UpdateFunction();
        public Type type;
        public PlayerLoopSystem[] subSystemList;
        public UpdateFunction updateDelegate;
        public IntPtr updateFunction;
        public IntPtr loopConditionFunction;
    }

    public class PlayerLoop
    {
        public static PlayerLoopSystem GetDefaultPlayerLoop() => Fake.DefaultLoop();
        public static PlayerLoopSystem GetCurrentPlayerLoop()
        {
            var copy = Copy(Fake.Current);
            if (Fake.ReadBackDropsLeaf && Fake.SetCount > 0)
                for (int i = 0; i < copy.subSystemList.Length; i++)
                    if (copy.subSystemList[i].type == typeof(UnityEngine.PlayerLoop.Update))
                        Array.Resize(ref copy.subSystemList[i].subSystemList, copy.subSystemList[i].subSystemList.Length - 1);
            return copy;
        }
        public static void SetPlayerLoop(PlayerLoopSystem loop) { Fake.Current = Copy(loop); Fake.SetCount++; }
        static PlayerLoopSystem Copy(PlayerLoopSystem s)
        {
            if (s.subSystemList != null) s.subSystemList = s.subSystemList.Select(Copy).ToArray();
            return s;
        }
    }
}

namespace UnityEngine.PlayerLoop
{
    public struct TimeUpdate { public struct WaitForLastPresentationAndUpdateTime { } }
    public struct EarlyUpdate { public struct UpdateInputManager { } public struct ScriptRunDelayedStartupFrame { } }
    public struct FixedUpdate { public struct ScriptRunBehaviourFixedUpdate { } public struct ScriptRunDelayedFixedFrameRate { } }
    public struct PreUpdate { public struct CheckTexFieldInput { } }
    public struct Update { public struct ScriptRunBehaviourUpdate { } public struct ScriptRunDelayedDynamicFrameRate { } public struct DirectorUpdate { } }
    public struct PreLateUpdate { public struct ScriptRunBehaviourLateUpdate { } }
    public struct PostLateUpdate
    {
        public struct PlayerSendFrameStarted { } public struct ScriptRunDelayedDynamicFrameRate { } public struct UpdateAllRenderers { }
        public struct TriggerEndOfFrameCallbacks { } public struct PlayerSendFrameComplete { }
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
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene OpenScene(string path)
        {
            if (path != "Assets/Scenes/Probe.unity") throw new ArgumentException("fake: unknown scene " + path);
            return default;
        }
    }
}
