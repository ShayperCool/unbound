// Fake Unity API for the offline guard controls (controls.py). It lets the probe's two real
// scripts run outside Unity: deterministic, plausible answers, no Unity behaviour claimed. Calls
// the probe must never make (the type or flags of an invalid/default LocalKeyword, a default
// LocalKeyword passed to a material, a default keyword space) are recorded in Fake.Forbidden.
// Editor transitions are recorded with the callback they were made from.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Rendering;

public static class Fake
{
    public static readonly List<string> Forbidden = new List<string>();
    public static readonly List<string> Transitions = new List<string>();
    public static string Context = "none";
    public static double Time;
    public static int? ExitCode;
    public static bool PendingEnter, PendingExit;
    public static string ThrowFind;
    public static void Forbid(string what) { Forbidden.Add(what + " in " + Context); throw new Exception("forbidden call: " + what); }
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
    public enum RuntimePlatform { LinuxEditor = 16 }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    public enum ShadowQuality { Disable, HardOnly, All }
    public enum ColorSpace { Gamma, Linear }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }

    public class Object
    {
        public string name { get; set; }
        public static T Instantiate<T>(T original) where T : Object
        {
            if (original is Material m) { var copy = new Material(m) { name = m.name + "(Clone)" }; return (T)(Object)copy; }
            throw new NotSupportedException("fake Instantiate");
        }
    }

    public class Component : Object { }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public class Camera : Behaviour { }

    public class GameObject : Object
    {
        public static readonly List<Component> Added = new List<Component>();
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : Component, new() { var c = new T(); Added.Add(c); return c; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
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
    }

    public static class Debug
    {
        public static void Log(object message) => Application.Log(LogType.Log, message.ToString());
        public static void LogError(object message) => Application.Log(LogType.Error, message.ToString());
        public static void LogException(Exception e) => Application.Log(LogType.Exception, e.Message);
    }

    public static class SystemInfo
    {
        public static GraphicsDeviceType graphicsDeviceType => GraphicsDeviceType.OpenGLCore;
        public static int graphicsShaderLevel => 50;
    }

    public static class QualitySettings
    {
        public static string[] names => new[] { "Very Low", "Low", "Medium", "High", "Very High", "Ultra" };
        public static int GetQualityLevel() => 5;
        public static ShadowQuality shadows => ShadowQuality.All;
        public static ColorSpace activeColorSpace => ColorSpace.Gamma;
    }

    public static class Resources
    {
        static readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        public static T Load<T>(string path) where T : Object
        {
            if (!Assets.TryGetValue(path, out var asset)) Assets[path] = asset = Make(path);
            return asset as T;
        }
        static Object Make(string path)
        {
            var forms = Shader.Find("Probe/KwForms");
            switch (path)
            {
                case "KwCompute": return new ComputeShader { name = "KwCompute" };
                case "KwSaved": { var m = new Material(forms) { name = "KwSaved" }; m.shaderKeywords = new[] { "LOCAL_A", "GLOBAL_A", "STALE_KW" }; return m; }
                case "VarParent": { var m = new Material(forms) { name = "VarParent" }; m.SetPropertyLock("_Other", true); return m; }
                case "VarChild": { var m = new Material(Load<Material>("VarParent")) { name = "VarChild" }; m.parent = Load<Material>("VarParent"); return m; }
                case "VarGrand": { var m = new Material(Load<Material>("VarChild")) { name = "VarGrand" }; m.parent = Load<Material>("VarChild"); m.renderQueue = 2100; return m; }
            }
            return null;
        }
    }

    public class ShaderData
    {
        public string[] Keywords;
        public HashSet<string> Dynamic = new HashSet<string>(), Overridable = new HashSet<string>();
        public int[] Passes;
        public string[][] Tags;
        public string[] Properties;
    }

    public class Shader : Object
    {
        internal ShaderData data;
        static readonly Dictionary<string, Shader> Shaders = new Dictionary<string, Shader>();
        internal static readonly List<string> Registry = new List<string> { "DIRECTIONAL", "LIGHTMAP_ON", "FOG_LINEAR", "STEREO_INSTANCING_ON", "_NORMALMAP" };
        internal static readonly HashSet<string> Enabled = new HashSet<string> { "DIRECTIONAL" };

        public static Shader Find(string name)
        {
            if (name == Fake.ThrowFind) throw new InvalidOperationException("fake Find failure");
            if (name == "Hidden/BlitCopy") return null;
            if (!Shaders.TryGetValue(name, out var s))
            {
                bool forms = name == "Probe/KwForms";
                var keywords = new List<string> { "STEREO_INSTANCING_ON", "UNITY_SINGLE_PASS_STEREO", "STEREO_MULTIVIEW_ON", "STEREO_CUBEMAP_RENDER_ON" };
                if (forms) keywords.AddRange(new[] { "GLOBAL_A", "GLOBAL_B", "LOCAL_A", "LOCAL_B", "LOCAL_C", "FEATURE_A", "DYN_GLOBAL", "DYN_LOCAL_A", "SHARED_KW", "LIGHTMAP_ON", "TOGGLE_KW" });
                else if (name.StartsWith("Probe/")) keywords.Add(name.Substring(6).ToUpperInvariant() + "_KW");
                var data = new ShaderData
                {
                    Keywords = keywords.ToArray(),
                    Passes = forms ? new[] { 2, 2, 1 } : new[] { 1 },
                    Tags = forms ? new[] { new[] { "RenderType=Opaque" }, new[] { "RenderType=Transparent", "ProbeTag=second" }, new[] { "RenderType=Opaque" } } : new[] { new string[0] },
                    Properties = forms ? new[] { "_Color", "_Value", "_Other", "_MainTex", "_Toggle" } : new[] { "_Color" },
                };
                data.Dynamic.Add("DYN_GLOBAL"); data.Dynamic.Add("DYN_LOCAL_A");
                data.Overridable.Add("GLOBAL_A"); data.Overridable.Add("DYN_GLOBAL");
                Shaders[name] = s = new Shader { name = name, data = data };
            }
            return s;
        }

        public int subshaderCount => data.Passes.Length;
        public int passCount => data.Passes[0];
        public int GetPropertyCount() => data.Properties.Length;
        public string GetPropertyName(int i) => data.Properties[i];
        public ShaderPropertyType GetPropertyType(int i) => i == 0 ? ShaderPropertyType.Color : ShaderPropertyType.Float;
        public ShaderPropertyFlags GetPropertyFlags(int i) => ShaderPropertyFlags.None;
        public int GetPassCountInSubshader(int s) => s < 0 || s >= data.Passes.Length ? -1 : data.Passes[s];
        public ShaderTagId FindSubshaderTagValue(int s, ShaderTagId tag)
        {
            if (s < 0 || s >= subshaderCount) throw new ArgumentOutOfRangeException("subshaderIndex", $"Invalid subshaderIndex {s}. Value must be in the range [0, {subshaderCount})");
            var hit = data.Tags[s].FirstOrDefault(t => t.StartsWith(tag.name + "="));
            return hit == null ? ShaderTagId.none : new ShaderTagId(hit.Substring(tag.name.Length + 1));
        }
        public ShaderTagId FindPassTagValue(int s, int p, ShaderTagId tag) => p == 0 ? new ShaderTagId("ForwardBase") : ShaderTagId.none;
        public LocalKeywordSpace keywordSpace => new LocalKeywordSpace(data);

        public static GlobalKeyword[] globalKeywords => Registry.Select(n => GlobalKeyword.Known(n)).ToArray();
        public static GlobalKeyword[] enabledGlobalKeywords => Registry.Where(Enabled.Contains).Select(n => GlobalKeyword.Known(n)).ToArray();
        public static void EnableKeyword(string name) { if (!Registry.Contains(name)) Registry.Add(name); Enabled.Add(name); }
        public static void EnableKeyword(in GlobalKeyword k) { if (Registry.Contains(k.name)) Enabled.Add(k.name); }
        public static void DisableKeyword(string name) => Enabled.Remove(name);
        public static void SetKeyword(in GlobalKeyword k, bool value) { if (value) EnableKeyword(k); else Enabled.Remove(k.name); }
        public static bool IsKeywordEnabled(string name) => Enabled.Contains(name);
        public static bool IsKeywordEnabled(in GlobalKeyword k) => Enabled.Contains(k.name);
    }

    public class ComputeShader : Object
    {
        static readonly ShaderData Data = new ShaderData { Keywords = new[] { "CS_LOCAL", "CS_GLOBAL_A", "CS_GLOBAL_B", "CS_DYN", "LIGHTMAP_ON" }, Passes = new int[0], Tags = new string[0][], Properties = new string[0] };
        readonly List<string> keywords = new List<string>();
        public LocalKeywordSpace keywordSpace => new LocalKeywordSpace(Data);
        public void EnableKeyword(string name) { if (Data.Keywords.Contains(name) && !keywords.Contains(name)) keywords.Add(name); }
        public void EnableKeyword(in LocalKeyword k) => EnableKeyword(k.Checked(Data).name);
        public void SetKeyword(in LocalKeyword k, bool value) { if (value) EnableKeyword(k); else keywords.Remove(k.name); }
        public string[] shaderKeywords => keywords.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        public LocalKeyword[] enabledKeywords => Data.Keywords.Where(keywords.Contains).Select(n => new LocalKeyword(Data, n)).ToArray();
        public bool IsKeywordEnabled(string name) => keywords.Contains(name);
        public bool IsKeywordEnabled(in LocalKeyword k) => keywords.Contains(k.name);
    }

    public class Material : Object
    {
        Shader s;
        readonly List<string> keywords = new List<string>();
        readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        readonly HashSet<string> overrides = new HashSet<string>(), locks = new HashSet<string>();
        Material parentMaterial;
        int queue = -1;

        public Material(Shader shader) { s = shader; if (shader == null) throw new ArgumentNullException("shader"); }
        public Material(Material source)
        {
            s = source.s; keywords.AddRange(source.keywords);
            foreach (var p in source.floats) floats[p.Key] = p.Value;
            foreach (var p in source.colors) colors[p.Key] = p.Value;
            parentMaterial = source.parentMaterial; overrides.UnionWith(source.overrides); locks.UnionWith(source.locks); queue = source.queue;
        }

        public Shader shader { get => s; set => s = value; }
        bool Declared(string n) => s.data.Keywords.Contains(n);
        public string[] shaderKeywords
        {
            get => keywords.Where(Declared).OrderBy(n => n, StringComparer.Ordinal).Concat(keywords.Where(n => !Declared(n)).OrderBy(n => n, StringComparer.Ordinal)).ToArray();
            set { keywords.Clear(); keywords.AddRange(value); }
        }
        public LocalKeyword[] enabledKeywords
        {
            get => s.data.Keywords.Where(keywords.Contains).Select(n => new LocalKeyword(s.data, n)).ToArray();
            set { keywords.Clear(); keywords.AddRange(value.Select(k => k.Checked(s.data).name)); }
        }
        public void SetKeyword(in LocalKeyword k, bool value)
        {
            var name = k.Checked(null).name;
            if (!k.SameSpace(s.data)) { Debug.LogError("Keyword " + name + " is not in the material's shader keyword space."); return; }
            if (value) { if (!keywords.Contains(name)) keywords.Add(name); } else keywords.Remove(name);
        }
        public void EnableKeyword(in LocalKeyword k) => SetKeyword(k, true);
        public void DisableKeyword(in LocalKeyword k) => SetKeyword(k, false);
        public void EnableKeyword(string name) { if (!keywords.Contains(name)) keywords.Add(name); }
        public bool IsKeywordEnabled(string name) => keywords.Contains(name) && Declared(name);
        public bool IsKeywordEnabled(in LocalKeyword k) => k.isValid && keywords.Contains(k.name);
        public void SetFloat(string n, float v) { if (IsPropertyLockedByAncestor(n)) return; floats[n] = v; if (parentMaterial != null) overrides.Add(n); }
        public float GetFloat(string n) => floats.TryGetValue(n, out var v) ? v : parentMaterial != null ? parentMaterial.GetFloat(n) : n == "_Value" ? 0.5f : 1f;
        public void SetColor(string n, Color v) { colors[n] = v; if (parentMaterial != null) overrides.Add(n); }
        public Color GetColor(string n) => colors.TryGetValue(n, out var v) ? v : parentMaterial != null ? parentMaterial.GetColor(n) : new Color(1, 0, 0, 1);
        public bool isVariant => parentMaterial != null;
        public Material parent { get => parentMaterial; set => parentMaterial = value; }
        public bool IsChildOf(Material m) { for (var p = parentMaterial; p != null; p = p.parentMaterial) if (p == m) return true; return false; }
        public bool IsPropertyOverriden(string n) => overrides.Contains(n);
        public bool IsPropertyLocked(string n) => locks.Contains(n);
        public bool IsPropertyLockedByAncestor(string n) => parentMaterial != null && (parentMaterial.IsPropertyLocked(n) || parentMaterial.IsPropertyLockedByAncestor(n));
        public void SetPropertyLock(string n, bool value) { if (value) locks.Add(n); else locks.Remove(n); }
        public void RevertPropertyOverride(string n) { if (n == "_Missing") throw new ArgumentException("fake: no property _Missing"); overrides.Remove(n); floats.Remove(n); colors.Remove(n); }
        public void RevertAllPropertyOverrides() { overrides.Clear(); floats.Clear(); colors.Clear(); queue = -1; }
        public int renderQueue { get => queue < 0 ? (parentMaterial != null ? parentMaterial.renderQueue : 2000) : queue; set => queue = value; }
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { OpenGLCore = 17, Direct3D11 = 2 }
    public enum ShaderPropertyType { Color, Vector, Float, Range, Texture, Int }
    [Flags] public enum ShaderPropertyFlags { None = 0 }
    public enum ShaderKeywordType { None = 0, BuiltinDefault = 2, UserDefined = 16 }
    public class RenderPipelineAsset : Object { }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline => null; }

    public struct ShaderTagId
    {
        readonly string id;
        public ShaderTagId(string name) { id = name; }
        public string name => id;
        public static ShaderTagId none => default;
        public static bool operator ==(ShaderTagId a, ShaderTagId b) => a.id == b.id;
        public static bool operator !=(ShaderTagId a, ShaderTagId b) => a.id != b.id;
        public override bool Equals(object o) => o is ShaderTagId t && t.id == id;
        public override int GetHashCode() => id == null ? 0 : id.GetHashCode();
    }

    public struct LocalKeywordSpace
    {
        readonly ShaderData data;
        internal LocalKeywordSpace(ShaderData data) { this.data = data; }
        ShaderData Data { get { if (data == null) Fake.Forbid("default LocalKeywordSpace"); return data; } }
        public uint keywordCount => (uint)Data.Keywords.Length;
        public string[] keywordNames => (string[])Data.Keywords.Clone();
        public LocalKeyword[] keywords { get { var d = Data; return d.Keywords.Select(n => new LocalKeyword(d, n)).ToArray(); } }
        public LocalKeyword FindKeyword(string name) => new LocalKeyword(Data, name);
        public static bool operator ==(LocalKeywordSpace a, LocalKeywordSpace b) => a.data == b.data;
        public static bool operator !=(LocalKeywordSpace a, LocalKeywordSpace b) => a.data != b.data;
        public override bool Equals(object o) => o is LocalKeywordSpace s && s.data == data;
        public override int GetHashCode() => 0;
    }

    public struct LocalKeyword
    {
        readonly ShaderData space;
        readonly string keyword;
        readonly bool valid;

        internal LocalKeyword(ShaderData space, string name) { this.space = space; keyword = name; valid = space.Keywords.Contains(name); }
        public LocalKeyword(Shader shader, string name)
        {
            if (shader == null) { Debug.LogError("Cannot initialize a LocalKeyword with a null Shader."); throw new NullReferenceException("Object reference not set to an instance of an object"); }
            space = shader.data; keyword = name; valid = space.Keywords.Contains(name);
            if (!valid) Debug.LogError("Local keyword " + name + " doesn't exist in the shader.");
        }
        public string name => keyword;
        public bool isValid { get { if (space == null) { Debug.LogError("LocalKeyword not initialized."); return false; } return valid; } }
        LocalKeyword Valid(string what) { if (space == null || !valid) Fake.Forbid(what + " of an invalid LocalKeyword"); return this; }
        public ShaderKeywordType type => Valid("type").keyword.StartsWith("STEREO") || keyword.StartsWith("UNITY_SINGLE") ? ShaderKeywordType.BuiltinDefault : ShaderKeywordType.UserDefined;
        public bool isDynamic => Valid("isDynamic").space.Dynamic.Contains(keyword);
        public bool isOverridable => Valid("isOverridable").space.Overridable.Contains(keyword);
        internal LocalKeyword Checked(ShaderData expected) { if (space == null) Fake.Forbid("default LocalKeyword passed to a keyword API"); return this; }
        internal bool SameSpace(ShaderData data) => space == data;
        public static bool operator ==(LocalKeyword a, LocalKeyword b) => a.space == b.space && a.keyword == b.keyword;
        public static bool operator !=(LocalKeyword a, LocalKeyword b) => !(a == b);
        public override bool Equals(object o) => o is LocalKeyword k && k == this;
        public override int GetHashCode() => 0;
        public override string ToString() => keyword;
    }

    public struct GlobalKeyword
    {
        readonly string keyword;
        GlobalKeyword(string name, bool known) { keyword = name; }
        internal static GlobalKeyword Known(string name) => new GlobalKeyword(name, true);
        public GlobalKeyword(string name)
        {
            keyword = name;
            if (!Shader.Registry.Contains(name)) Debug.LogError("Global keyword " + name + " doesn't exist.");
        }
        public static GlobalKeyword Create(string name) { if (!Shader.Registry.Contains(name)) Shader.Registry.Add(name); return new GlobalKeyword(name, true); }
        public string name => keyword;
        public override string ToString() => keyword;
    }

    public struct ShaderKeyword
    {
        readonly string keyword;
        public ShaderKeyword(string name) { keyword = name; }
        [Obsolete("fake obsolete")]
        public static ShaderKeywordType GetGlobalKeywordType(ShaderKeyword k) => k.keyword == "DIRECTIONAL" || k.keyword == "LIGHTMAP_ON" ? ShaderKeywordType.BuiltinDefault : ShaderKeywordType.UserDefined;
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
    public static class EditorSettings { public static bool asyncShaderCompilation { get; set; } = true; }

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
        public static event Action<PlayModeStateChange> playModeStateChanged;
        public static bool isPlaying;
        public static bool isPaused;
        public static bool isPlayingOrWillChangePlaymode => isPlaying || Fake.PendingEnter;
        public static double timeSinceStartup => Fake.Time;

        static void Transition(string what)
        {
            Fake.Transitions.Add(what + " from " + Fake.Context);
            if (Fake.Context != "update" && Fake.Context != "delayCall") Fake.Forbid(what + " outside an Editor update/delayCall");
        }
        public static void EnterPlaymode() { Transition("EnterPlaymode"); Fake.PendingEnter = true; }
        public static void ExitPlaymode() { Transition("ExitPlaymode"); Fake.PendingExit = true; }
        public static void Exit(int code) { Transition("Exit " + code); if (Fake.ExitCode != null) Fake.Forbid("second Exit"); Fake.ExitCode = code; }
        public static void Fire(PlayModeStateChange state) => playModeStateChanged?.Invoke(state);
        public static void DropDomain() { update = null; delayCall = null; playModeStateChanged = null; }
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
