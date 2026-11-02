// Fake Unity API for the offline guard controls (controls.py). It lets the probe's two real
// scripts run outside Unity with deterministic, plausible answers; no Unity behaviour is claimed.
// The fake shaders mirror the fixture's SubShaders (LOD, pipeline tag, pass count, colour code) and
// pick one by a toy rule; a surface SubShader has four fake passes. The fake camera "renders" the
// picked SubShader's code and pass count. Editor transitions are recorded with the callback they
// were made from; calls the protocol forbids go to Fake.Forbidden.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static class Fake
{
    public static readonly List<string> Forbidden = new List<string>();
    public static readonly List<string> Transitions = new List<string>();
    public static readonly List<UnityEngine.Object> Live = new List<UnityEngine.Object>();
    public static string Context = "none";
    public static double EditorTime;
    public static int? ExitCode;
    public static bool PendingEnter, PendingExit, AsyncShaderCompilation = true;
    public static int Frame = 1;
    public static void NextFrame() => Frame++;
    /// <summary>The n-th DestroyImmediate of a material made in a case throws (0: never).</summary>
    public static int FaultAtFreshDestroy;
    public static int FreshDestroyed;
    public static void Forbid(string what) { Forbidden.Add(what + " in " + Context); throw new Exception("forbidden call: " + what); }

    public sealed class Sub
    {
        public string Id; public int Lod; public int Passes; public bool Surface; public string Pipeline; public float R, B;
    }

    public sealed class Spec
    {
        public Sub[] Subs; public string Fallback;
    }

    public static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
    {
        ["Probe/PassLod"] = new Spec { Fallback = "Probe/PassFallback", Subs = new[] {
            new Sub { Id = "L0", Lod = 400, Passes = 3, R = .125f }, new Sub { Id = "L1", Lod = 300, Passes = 1, R = .25f },
            new Sub { Id = "L2", Lod = 200, Passes = 2, R = .375f } } },
        ["Probe/PassFallback"] = new Spec { Subs = new[] {
            new Sub { Id = "F0", Lod = 150, Passes = 4, R = .5f }, new Sub { Id = "F1", Lod = 50, Passes = 5, R = .625f } } },
        ["Probe/PassTagged"] = new Spec { Subs = new[] {
            new Sub { Id = "T0", Lod = 0, Passes = 3, Pipeline = "UniversalPipeline", R = .75f }, new Sub { Id = "T1", Lod = 0, Passes = 2, R = .875f } } },
        ["Probe/PassSurface"] = new Spec { Subs = new[] {
            new Sub { Id = "S0", Lod = 300, Passes = 4, Surface = true, R = .125f, B = .5f }, new Sub { Id = "S1", Lod = 200, Passes = 3, R = .25f, B = .5f } } },
    };

    public static readonly Dictionary<string, Shader> Loaded = new Dictionary<string, Shader>();

    public static byte Byte(float v) => (byte)Math.Min(255, Math.Round(v * 255));
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public enum RuntimePlatform { LinuxEditor = 16 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    public enum ColorSpace { Gamma, Linear }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }
    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum RenderingPath { UsePlayerSettings = -1, VertexLit = 0, Forward = 1, DeferredShading = 3 }
    public enum RenderTextureFormat { ARGB32 = 0 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public enum TextureFormat { RGBA32 = 4 }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }

    public class Object
    {
        public string name { get; set; }
        internal bool destroyed;
        public Object() { Fake.Live.Add(this); }
        public static void DestroyImmediate(Object obj)
        {
            if (obj is Material m && m.name != null && m.name.StartsWith("Fresh"))
            {
                Fake.FreshDestroyed++;
                if (Fake.FreshDestroyed == Fake.FaultAtFreshDestroy) throw new InvalidOperationException("fake fault destroying " + m.name);
            }
            if (obj is GameObject go) go.Destroy();
            obj.destroyed = true;
            Fake.Live.Remove(obj);
        }
        public static bool operator ==(Object a, Object b)
        {
            bool an = ReferenceEquals(a, null) || a.destroyed, bn = ReferenceEquals(b, null) || b.destroyed;
            return an && bn || !an && !bn && ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject.transform;
    }

    public class Behaviour : Component { public bool enabled { get; set; } = true; }
    public class MonoBehaviour : Behaviour { }

    public class Renderer : Component
    {
        public bool enabled { get; set; } = true;
        public Material sharedMaterial { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; } = ShadowCastingMode.On;
        public bool receiveShadows { get; set; } = true;
    }

    public class MeshRenderer : Renderer { }

    public class Camera : Behaviour
    {
        public static readonly List<Camera> All = new List<Camera>();
        public Camera() { All.Add(this); }
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; } = 5f;
        public CameraClearFlags clearFlags { get; set; } = CameraClearFlags.Skybox;
        public Color backgroundColor { get; set; }
        public RenderingPath renderingPath { get; set; } = RenderingPath.UsePlayerSettings;
        public RenderingPath actualRenderingPath => renderingPath == RenderingPath.UsePlayerSettings ? RenderingPath.Forward : renderingPath;
        public bool allowHDR { get; set; } = true;
        public bool allowMSAA { get; set; } = true;
        public RenderTexture targetTexture { get; set; }

        public void Render()
        {
            if (targetTexture == null) throw new InvalidOperationException("fake: no target");
            var clear = new Color32(Fake.Byte(backgroundColor.r), Fake.Byte(backgroundColor.g), Fake.Byte(backgroundColor.b), Fake.Byte(backgroundColor.a));
            var color = clear;
            var quad = GameObject.Quads.LastOrDefault(q => !q.destroyed);
            var r = quad?.GetComponent<Renderer>();
            if (r != null && r.enabled && r.sharedMaterial != null)
            {
                var sub = r.sharedMaterial.shader.Selected();
                if (sub != null)
                    color = new Color32(Fake.Byte(sub.R), sub.Surface ? (byte)0 : (byte)Math.Min(255, 32 * sub.Passes), Fake.Byte(sub.B), 255);
            }
            for (int i = 0; i < targetTexture.pixels.Length; i++) targetTexture.pixels[i] = color;
        }
    }

    public class Transform
    {
        public Vector3 position { get; set; }
        public Vector3 localScale { get; set; }
    }

    public class GameObject : Object
    {
        public static readonly List<Component> Added = new List<Component>();
        public static readonly List<GameObject> Quads = new List<GameObject>();
        readonly List<Component> components = new List<Component>();
        public Transform transform { get; } = new Transform();
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : Component, new()
        {
            var c = new T { gameObject = this };
            components.Add(c);
            if (c is MonoBehaviour) Added.Add(c);
            return c;
        }
        public T GetComponent<T>() where T : Component => components.OfType<T>().FirstOrDefault();
        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            var go = new GameObject(type.ToString());
            go.AddComponent<MeshRenderer>();
            Quads.Add(go);
            return go;
        }
        internal void Destroy()
        {
            foreach (var c in components) { c.destroyed = true; Fake.Live.Remove(c); }
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => new Color(0, 0, 0, 1);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public class Texture : Object { }

    public class RenderTexture : Texture
    {
        internal readonly Color32[] pixels;
        public int width { get; }
        public int height { get; }
        public int antiAliasing { get; set; } = 1;
        public static RenderTexture active { get; set; }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite)
        {
            this.width = width;
            this.height = height;
            pixels = new Color32[width * height];
        }
        public void Release() { }
    }

    public class Texture2D : Texture
    {
        readonly Color32[] pixels;
        public Texture2D(int width, int height, TextureFormat format, bool mipChain, bool linear) { pixels = new Color32[width * height]; }
        public void ReadPixels(Rect source, int x, int y)
        {
            if (RenderTexture.active == null) throw new InvalidOperationException("fake: no active render texture");
            Array.Copy(RenderTexture.active.pixels, pixels, pixels.Length);
        }
        public void Apply(bool updateMipmaps) { }
        public Color32[] GetPixels32() => (Color32[])pixels.Clone();
    }

    public class Shader : Object
    {
        internal Fake.Spec spec;
        public static string globalRenderPipeline { get; set; } = "";
        public static int globalMaximumLOD { get; set; } = int.MaxValue;
        public int maximumLOD { get; set; } = -1;

        public static Shader Find(string name)
        {
            if (!Fake.Specs.TryGetValue(name, out var spec)) return null;
            if (!Fake.Loaded.TryGetValue(name, out var shader)) Fake.Loaded[name] = shader = new Shader { name = name, spec = spec };
            return shader;
        }

        IEnumerable<Fake.Sub> Chain()
        {
            for (var s = this; s != null; s = s.spec.Fallback == null ? null : Find(s.spec.Fallback))
                foreach (var sub in s.spec.Subs) yield return sub;
        }

        internal Fake.Sub Selected()
        {
            int lod = maximumLOD >= 0 ? maximumLOD : globalMaximumLOD;
            return Chain().FirstOrDefault(s => s.Pipeline == null && s.Lod <= lod);
        }

        public int passCount => Selected()?.Passes ?? 0;
        public bool isSupported => Selected() != null;
        public int subshaderCount => Chain().Count();
        public int GetPassCountInSubshader(int index) => index >= 0 && index < subshaderCount ? Chain().ElementAt(index).Passes : -1;

        public ShaderTagId FindSubshaderTagValue(int index, ShaderTagId tag)
        {
            if (index < 0 || index >= subshaderCount) throw new ArgumentOutOfRangeException("subshaderIndex");
            var sub = Chain().ElementAt(index);
            if (tag.name == "SubId") return new ShaderTagId(sub.Id);
            if (tag.name == "RenderPipeline" && sub.Pipeline != null) return new ShaderTagId(sub.Pipeline);
            return ShaderTagId.none;
        }

        public ShaderTagId FindPassTagValue(int subshader, int pass, ShaderTagId tag)
        {
            var sub = Chain().ElementAt(subshader);
            if (sub.Surface) return tag.name == "LightMode" ? new ShaderTagId(new[] { "FORWARDBASE", "FORWARDADD", "DEFERRED", "META" }[pass]) : ShaderTagId.none;
            return tag.name == "PassId" ? new ShaderTagId(sub.Id + "." + pass) : ShaderTagId.none;
        }
    }

    public class Material : Object
    {
        public Shader shader { get; }
        public Material(Shader shader)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            this.shader = shader;
        }
        public int passCount => shader.passCount;
        public string GetTag(string tag, bool searchFallbacks, string defaultValue) =>
            tag == "SubId" ? shader.Selected()?.Id ?? defaultValue : defaultValue;
        public int FindPass(string passName)
        {
            var sub = shader.Selected();
            if (sub == null || sub.Surface) return -1;
            for (int i = 0; i < sub.Passes; i++) if (string.Equals(sub.Id + "P" + i, passName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    public static class RenderSettings
    {
        public static AmbientMode ambientMode { get; set; }
        public static Color ambientLight { get; set; }
        public static bool fog { get; set; }
    }

    public static class QualitySettings
    {
        public static int GetQualityLevel() => 5;
        public static string[] names => new[] { "Very Low", "Low", "Medium", "High", "Very High", "Ultra" };
        public static ColorSpace activeColorSpace => ColorSpace.Linear;
        public static RenderPipelineAsset renderPipeline => null;
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
        public static int graphicsShaderLevel => 50;
        public static string graphicsDeviceName => "Fake GPU";
        public static string graphicsDeviceVersion => "OpenGL 4.5 (fake)";
    }

    public static class Time
    {
        public static int frameCount => Fake.Frame;
        public static double realtimeSinceStartupAsDouble => 1.25 + Fake.Frame * 0.001;
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { OpenGLCore = 17, Null = 4 }
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }

    public class RenderPipelineAsset : UnityEngine.Object { }

    public static class GraphicsSettings
    {
        public static RenderPipelineAsset currentRenderPipeline => null;
        public static RenderPipelineAsset defaultRenderPipeline => null;
    }

    public struct ShaderTagId : IEquatable<ShaderTagId>
    {
        readonly string m_Name;
        public ShaderTagId(string name) { m_Name = name; }
        public static ShaderTagId none => default;
        public string name => m_Name ?? "";
        public bool Equals(ShaderTagId other) => name == other.name;
        public override bool Equals(object o) => o is ShaderTagId t && Equals(t);
        public override int GetHashCode() => name.GetHashCode();
        public static bool operator ==(ShaderTagId a, ShaderTagId b) => a.Equals(b);
        public static bool operator !=(ShaderTagId a, ShaderTagId b) => !a.Equals(b);
    }
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

    public static class EditorSettings
    {
        public static bool asyncShaderCompilation
        {
            get => Fake.AsyncShaderCompilation;
            set
            {
                if (Fake.Context != "executeMethod") Fake.Forbid("EditorSettings outside Record");
                Fake.AsyncShaderCompilation = value;
            }
        }
    }

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
