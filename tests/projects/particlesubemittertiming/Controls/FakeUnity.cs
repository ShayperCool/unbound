// Fake Unity API for the offline guard controls (controls.py). It lets the probe's two real
// scripts run outside Unity with deterministic, plausible answers; no Unity behaviour is claimed.
// The fake particle system is a toy (linear motion, linear ageing, one Death burst, one Birth child
// per call); it exists to exercise emission, schema and control flow only. Editor transitions are
// recorded with the callback they were made from; calls the protocol forbids go to Fake.Forbidden.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static class Fake
{
    public static readonly List<string> Forbidden = new List<string>();
    public static readonly List<string> Transitions = new List<string>();
    public static readonly List<ParticleSystem> Live = new List<ParticleSystem>();
    public static string Context = "none";
    public static double EditorTime;
    public static int? ExitCode;
    public static bool PendingEnter, PendingExit;
    public static string ThrowSimulate;
    public static void Forbid(string what) { Forbidden.Add(what + " in " + Context); throw new Exception("forbidden call: " + what); }

    // Clock state: the values set by a frame take effect at the next frame, like a player loop.
    public static int Frame = 1;
    public static float Fixed = 0.0199999921f, Scale = 1f, Capture = 0f;
    public static float Delta = 0.0199999921f, Unscaled = 0.0199999921f, TimeNow, UnscaledNow;

    public static void NextFrame()
    {
        Frame++;
        Unscaled = Capture > 0 ? Capture : 0.0199999921f;
        Delta = Unscaled * Scale;
        TimeNow += Delta;
        UnscaledNow += Unscaled;
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
        public string name { get; set; }
        public static void DestroyImmediate(Object obj)
        {
            if (obj is GameObject go) go.Destroy();
            else throw new NotSupportedException("fake DestroyImmediate of a component");
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
    }

    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public class Camera : Behaviour { }

    public class Transform
    {
        internal GameObject owner;
        internal Transform parent;
        internal readonly List<Transform> children = new List<Transform>();
        public Vector3 localPosition { get; set; }
        public GameObject gameObject => owner;
        public Transform root => parent == null ? this : parent.root;
        public void SetParent(Transform p, bool worldPositionStays)
        {
            parent?.children.Remove(this);
            parent = p;
            p?.children.Add(this);
        }
    }

    public class GameObject : Object
    {
        public static readonly List<Component> Added = new List<Component>();
        readonly List<Component> components = new List<Component>();
        public Transform transform { get; }
        public bool activeSelf { get; private set; } = true;
        public bool destroyed;
        public GameObject(string name) { this.name = name; transform = new Transform { owner = this }; }
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this };
            components.Add(c);
            if (c is ParticleSystem ps) Fake.Live.Add(ps);
            else Added.Add(c);
            return c;
        }
        internal void Destroy()
        {
            foreach (var child in transform.children.ToList()) child.gameObject.Destroy();
            foreach (var c in components) if (c is ParticleSystem ps) Fake.Live.Remove(ps);
            destroyed = true;
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
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
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.OpenGLCore;
    }

    public static class Time
    {
        public static float deltaTime => Fake.Delta;
        public static float unscaledDeltaTime => Fake.Unscaled;
        public static float fixedDeltaTime { get => Fake.Fixed; set => Fake.Fixed = value; }
        public static float fixedUnscaledDeltaTime => Fake.Scale > 0 ? Fake.Fixed / Fake.Scale : Fake.Fixed;
        public static float captureDeltaTime { get => Fake.Capture; set => Fake.Capture = value; }
        public static int captureFramerate => Fake.Capture > 0 ? (int)Math.Round(1 / Fake.Capture) : 0;
        public static float timeScale { get => Fake.Scale; set => Fake.Scale = value; }
        public static float maximumDeltaTime => 0.33333334f;
        public static float maximumParticleDeltaTime => 0.03f;
        public static bool inFixedTimeStep => false;
        public static int frameCount => Fake.Frame;
        public static float time => Fake.TimeNow;
        public static float unscaledTime => Fake.UnscaledNow;
        public static float timeSinceLevelLoad => Fake.TimeNow;
        public static double realtimeSinceStartupAsDouble => 1.25 + Fake.Frame * 0.001;
    }

    public enum ParticleSystemStopBehavior { StopEmittingAndClear, StopEmitting }
    public enum ParticleSystemSubEmitterType { Birth, Collision, Death, Trigger, Manual }
    [Flags]
    public enum ParticleSystemSubEmitterProperties { InheritNothing = 0, InheritEverything = 31, InheritColor = 1, InheritSize = 2 }

    public class ParticleSystem : Component
    {
        public struct MinMaxCurve
        {
            public float constant;
            public static implicit operator MinMaxCurve(float value) => new MinMaxCurve { constant = value };
        }

        public struct Burst
        {
            public float time; public short count;
            public Burst(float time, short count) { this.time = time; this.count = count; }
        }

        public struct Particle
        {
            public Vector3 position { get; set; }
            public Vector3 velocity { get; set; }
            public Vector3 totalVelocity => velocity;
            public Vector3 animatedVelocity => Vector3.zero;
            public Vector3 axisOfRotation { get; set; }
            public float remainingLifetime { get; set; }
            public uint randomSeed { get; set; }
            public float startLifetime { get; set; }
            public float startSize { get => startSize3D.x; set => startSize3D = new Vector3(value, value, value); }
            public Vector3 startSize3D { get; set; }
            public Color32 startColor { get; set; }
            public Vector3 rotation3D { get; set; }
            public Vector3 angularVelocity3D { get; set; }
            public Vector3 GetCurrentSize3D(ParticleSystem system) => startSize3D;
            public Color32 GetCurrentColor(ParticleSystem system) => startColor;
        }

        public struct MainModule
        {
            internal ParticleSystem s;
            public bool playOnAwake { set => s.playOnAwake = value; }
            public MinMaxCurve startLifetime { set => s.startLifetime = value.constant; }
            public MinMaxCurve startSpeed { set => s.startSpeed = value.constant; }
            public MinMaxCurve startSize { set => s.startSizeValue = value.constant; }
        }

        public struct ShapeModule
        {
            internal ParticleSystem s;
            public bool enabled { get => s.shapeEnabled; set => s.shapeEnabled = value; }
        }

        public struct EmissionModule
        {
            internal ParticleSystem s;
            public bool enabled { get => s.emissionEnabled; set => s.emissionEnabled = value; }
            public MinMaxCurve rateOverTime { set => s.rate = value.constant; }
            public void SetBursts(Burst[] bursts) => s.bursts = bursts;
        }

        public struct SubEmittersModule
        {
            internal ParticleSystem s;
            public bool enabled { get => s.subEnabled; set => s.subEnabled = value; }
            public void AddSubEmitter(ParticleSystem child, ParticleSystemSubEmitterType type, ParticleSystemSubEmitterProperties properties)
                => s.subs.Add((child, type));
        }

        internal bool playOnAwake = true, shapeEnabled = true, emissionEnabled = true, subEnabled;
        internal float startLifetime = 5, startSpeed = 5, startSizeValue = 1, rate = 10, clock;
        internal Burst[] bursts = new Burst[0];
        internal readonly List<(ParticleSystem system, ParticleSystemSubEmitterType type)> subs = new List<(ParticleSystem, ParticleSystemSubEmitterType)>();
        readonly List<Particle> particles = new List<Particle>();
        bool simulated;
        uint nextSeed = 692788716;

        public bool useAutoRandomSeed { get; set; } = true;
        public uint randomSeed { get; set; }
        public MainModule main => new MainModule { s = this };
        public ShapeModule shape => new ShapeModule { s = this };
        public EmissionModule emission => new EmissionModule { s = this };
        public SubEmittersModule subEmitters => new SubEmittersModule { s = this };
        public int particleCount => particles.Count;
        public float time => clock;
        public bool isPlaying => false;
        public bool isEmitting => false;
        public bool isStopped => !simulated;
        public bool isPaused => simulated;
        public bool IsAlive(bool withChildren) => particles.Count > 0 || simulated;

        public void Stop(bool withChildren, ParticleSystemStopBehavior behavior) { particles.Clear(); clock = 0; }

        public void SetParticles(Particle[] values, int size) { particles.Clear(); particles.AddRange(values.Take(size)); }

        public int GetParticles(Particle[] buffer)
        {
            int n = Math.Min(buffer.Length, particles.Count);
            for (int i = 0; i < n; i++) buffer[i] = particles[i];
            return n;
        }

        Particle Emit(Vector3 at, float speed)
            => new Particle
            {
                position = at, velocity = new Vector3(0, 0, speed), axisOfRotation = new Vector3(0, 1, 0),
                remainingLifetime = startLifetime, startLifetime = startLifetime, startSize = startSizeValue,
                startColor = new Color32(255, 255, 255, 255), randomSeed = nextSeed++
            };

        public void Simulate(float t, bool withChildren, bool restart, bool fixedTimeStep)
        {
            if (Fake.ThrowSimulate != null && gameObject.name.StartsWith(Fake.ThrowSimulate))
                throw new InvalidOperationException("fake Simulate fault");
            if (restart) { particles.Clear(); clock = 0; if (emissionEnabled && rate > 0) particles.Add(Emit(Vector3.zero, startSpeed)); }
            simulated = true;
            clock += t;
            var died = new List<Vector3>();
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.position = p.position + p.velocity * t;
                p.remainingLifetime -= t;
                if (p.remainingLifetime <= 0) { died.Add(p.position); particles.RemoveAt(i); }
                else particles[i] = p;
            }
            foreach (var (child, type) in subs)
            {
                if (type == ParticleSystemSubEmitterType.Death)
                    foreach (var at in died)
                        foreach (var b in child.bursts)
                            for (int k = 0; k < b.count; k++) child.particles.Add(child.Emit(at, child.startSpeed));
                if (type == ParticleSystemSubEmitterType.Birth && particles.Count > 0)
                    child.particles.Add(child.Emit(particles[0].position, child.startSpeed));
            }
            if (withChildren)
                foreach (var c in gameObject.transform.children)
                    foreach (var other in Fake.Live.Where(s => s.gameObject == c.gameObject))
                        other.Simulate(t, true, false, fixedTimeStep);
        }
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { OpenGLCore = 17, Null = 4 }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { }
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
