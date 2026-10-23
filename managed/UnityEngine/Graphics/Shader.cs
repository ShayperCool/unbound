using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A shader, known by name. Unbound does not compile ShaderLab: the renderer maps the name to one
    /// of its shader families and reads the render states of the source; here the shader carries its
    /// name, default render queue and declared properties.
    /// </summary>
    [ClassId(48)]
    public sealed partial class Shader : Object
    {
        internal int m_RenderQueue = 2000;
        internal readonly List<ShaderPropertyInfo> m_Properties = new List<ShaderPropertyInfo>();
        int m_MaximumLOD = -1;

        static readonly Dictionary<string, Shader> s_ByName = new Dictionary<string, Shader>();
        static readonly Dictionary<string, int> s_PropertyIds = new Dictionary<string, int>();
        static readonly List<string> s_PropertyNames = new List<string>();
        static readonly HashSet<string> s_GlobalKeywords = new HashSet<string>();
        internal static readonly Dictionary<int, object> s_Globals = new Dictionary<int, object>();

        Shader() { }

        internal static Shader Create(string name, int renderQueue)
        {
            var shader = new Shader { m_Name = name, m_RenderQueue = renderQueue };
            BuiltinShaders.AddDefaultProperties(shader);
            return shader;
        }

        public bool isSupported => true;

        public int maximumLOD
        {
            get => m_MaximumLOD;
            set => m_MaximumLOD = value;
        }

        public int renderQueue => m_RenderQueue;

        public int passCount => 1;

        public int subshaderCount => 1;

        public static int globalMaximumLOD { get; set; } = int.MaxValue;

        public static string globalRenderPipeline { get; set; } = "";

        // ------------------------------------------------------------------ lookup

        public static Shader Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_ByName.TryGetValue(name, out var cached) && cached) return cached;
            var shader = BuiltinShaders.ByName(name) ?? ProjectShaders.Find(name);
            if (shader != null) s_ByName[name] = shader;
            return shader;
        }

        internal static void Register(Shader shader)
        {
            if (!string.IsNullOrEmpty(shader.m_Name) && !s_ByName.ContainsKey(shader.m_Name))
                s_ByName[shader.m_Name] = shader;
        }

        // ------------------------------------------------------------------ property ids

        public static int PropertyToID(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            lock (s_PropertyIds)
            {
                if (s_PropertyIds.TryGetValue(name, out var id)) return id;
                id = s_PropertyNames.Count;
                s_PropertyNames.Add(name);
                s_PropertyIds[name] = id;
                return id;
            }
        }

        internal static string IdToName(int id)
        {
            lock (s_PropertyIds) return id >= 0 && id < s_PropertyNames.Count ? s_PropertyNames[id] : null;
        }

        // ------------------------------------------------------------------ properties

        public int GetPropertyCount() => m_Properties.Count;

        public int FindPropertyIndex(string propertyName) => m_Properties.FindIndex(p => p.Name == propertyName);

        public string GetPropertyName(int propertyIndex) => Property(propertyIndex).Name;

        public int GetPropertyNameId(int propertyIndex) => PropertyToID(Property(propertyIndex).Name);

        public ShaderPropertyType GetPropertyType(int propertyIndex) => Property(propertyIndex).Type;

        public string GetPropertyDescription(int propertyIndex) => Property(propertyIndex).Description ?? "";

        public ShaderPropertyFlags GetPropertyFlags(int propertyIndex) => ShaderPropertyFlags.None;

        public string[] GetPropertyAttributes(int propertyIndex) => Array.Empty<string>();

        public float GetPropertyDefaultFloatValue(int propertyIndex) => Property(propertyIndex).DefaultValue.x;

        public Vector4 GetPropertyDefaultVectorValue(int propertyIndex) => Property(propertyIndex).DefaultValue;

        public int GetPropertyDefaultIntValue(int propertyIndex) => (int)Property(propertyIndex).DefaultValue.x;

        public Vector2 GetPropertyRangeLimits(int propertyIndex) => Property(propertyIndex).Range;

        public string GetPropertyTextureDefaultName(int propertyIndex) => Property(propertyIndex).DefaultTexture ?? "";

        public TextureDimension GetPropertyTextureDimension(int propertyIndex) => TextureDimension.Tex2D;

        ShaderPropertyInfo Property(int index)
        {
            if (index < 0 || index >= m_Properties.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "Property index is out of bounds.");
            return m_Properties[index];
        }

        internal bool TryGetDefault(string name, out ShaderPropertyInfo info)
        {
            foreach (var p in m_Properties)
            {
                if (p.Name == name)
                {
                    info = p;
                    return true;
                }
            }
            info = null;
            return false;
        }

        // ------------------------------------------------------------------ globals

        public static void EnableKeyword(string keyword) => s_GlobalKeywords.Add(keyword);
        public static void DisableKeyword(string keyword) => s_GlobalKeywords.Remove(keyword);
        public static bool IsKeywordEnabled(string keyword) => s_GlobalKeywords.Contains(keyword);

        public static void SetGlobalFloat(string name, float value) => s_Globals[PropertyToID(name)] = value;
        public static void SetGlobalFloat(int nameID, float value) => s_Globals[nameID] = value;
        public static void SetGlobalInt(string name, int value) => s_Globals[PropertyToID(name)] = (float)value;
        public static void SetGlobalInt(int nameID, int value) => s_Globals[nameID] = (float)value;
        public static void SetGlobalInteger(string name, int value) => s_Globals[PropertyToID(name)] = value;
        public static void SetGlobalInteger(int nameID, int value) => s_Globals[nameID] = value;
        public static void SetGlobalVector(string name, Vector4 value) => s_Globals[PropertyToID(name)] = value;
        public static void SetGlobalVector(int nameID, Vector4 value) => s_Globals[nameID] = value;
        public static void SetGlobalColor(string name, Color value) => s_Globals[PropertyToID(name)] = (Vector4)value;
        public static void SetGlobalColor(int nameID, Color value) => s_Globals[nameID] = (Vector4)value;
        public static void SetGlobalTexture(string name, Texture value) => s_Globals[PropertyToID(name)] = value;
        public static void SetGlobalTexture(int nameID, Texture value) => s_Globals[nameID] = value;
        public static void SetGlobalMatrix(string name, Matrix4x4 value) => s_Globals[PropertyToID(name)] = value;
        public static void SetGlobalMatrix(int nameID, Matrix4x4 value) => s_Globals[nameID] = value;
        public static void SetGlobalFloatArray(string name, float[] values) => s_Globals[PropertyToID(name)] = values;
        public static void SetGlobalFloatArray(int nameID, float[] values) => s_Globals[nameID] = values;
        public static void SetGlobalVectorArray(string name, Vector4[] values) => s_Globals[PropertyToID(name)] = values;
        public static void SetGlobalVectorArray(int nameID, Vector4[] values) => s_Globals[nameID] = values;
        public static void SetGlobalMatrixArray(string name, Matrix4x4[] values) => s_Globals[PropertyToID(name)] = values;
        public static void SetGlobalMatrixArray(int nameID, Matrix4x4[] values) => s_Globals[nameID] = values;

        public static float GetGlobalFloat(string name) => GetGlobalFloat(PropertyToID(name));
        public static float GetGlobalFloat(int nameID) => s_Globals.TryGetValue(nameID, out var v) && v is float f ? f : 0f;
        public static int GetGlobalInt(string name) => (int)GetGlobalFloat(name);
        public static int GetGlobalInt(int nameID) => (int)GetGlobalFloat(nameID);
        public static int GetGlobalInteger(string name) => GetGlobalInteger(PropertyToID(name));
        public static int GetGlobalInteger(int nameID) => s_Globals.TryGetValue(nameID, out var v) && v is int i ? i : 0;
        public static Vector4 GetGlobalVector(string name) => GetGlobalVector(PropertyToID(name));
        public static Vector4 GetGlobalVector(int nameID) => s_Globals.TryGetValue(nameID, out var v) && v is Vector4 x ? x : Vector4.zero;
        public static Color GetGlobalColor(string name) => GetGlobalVector(name);
        public static Color GetGlobalColor(int nameID) => GetGlobalVector(nameID);
        public static Texture GetGlobalTexture(string name) => GetGlobalTexture(PropertyToID(name));
        public static Texture GetGlobalTexture(int nameID) => s_Globals.TryGetValue(nameID, out var v) ? v as Texture : null;
        public static Matrix4x4 GetGlobalMatrix(string name) => GetGlobalMatrix(PropertyToID(name));
        public static Matrix4x4 GetGlobalMatrix(int nameID) => s_Globals.TryGetValue(nameID, out var v) && v is Matrix4x4 m ? m : Matrix4x4.identity;

        public static void WarmupAllShaders() { }
    }

    internal sealed class ShaderPropertyInfo
    {
        internal string Name;
        internal string Description;
        internal ShaderPropertyType Type;
        internal Vector4 DefaultValue;
        internal string DefaultTexture;
        internal Vector2 Range;
    }
}

namespace Unbound.Runtime
{
    /// <summary>Unity's built-in shaders, by the file ids built-in references use, with the defaults
    /// their materials rely on.</summary>
    internal static class BuiltinShaders
    {
        /// <summary>`{fileID, guid 0000000000000000f000000000000000}` → name (unity_builtin_extra).</summary>
        internal static readonly Dictionary<long, string> ExtraNames = new Dictionary<long, string>();
        /// <summary>The same for `guid 0000000000000000e000000000000000` (unity default resources).</summary>
        internal static readonly Dictionary<long, string> DefaultNames = new Dictionary<long, string>();

        static readonly Dictionary<string, UnityEngine.Shader> s_Shaders = new Dictionary<string, UnityEngine.Shader>();

        internal const string ExtraGuid = "0000000000000000f000000000000000";
        internal const string DefaultGuid = "0000000000000000e000000000000000";

        internal static UnityEngine.Shader ByName(string name)
        {
            if (s_Shaders.TryGetValue(name, out var shader)) return shader;
            if (!ExtraNames.ContainsValue(name) && !DefaultNames.ContainsValue(name) && !name.StartsWith("Hidden/"))
                return null;
            shader = UnityEngine.Shader.Create(name, DefaultQueue(name));
            s_Shaders[name] = shader;
            return shader;
        }

        internal static int DefaultQueue(string name)
        {
            if (name.StartsWith("UI/") || name.StartsWith("Sprites/") || name.Contains("Particles") ||
                name.Contains("Transparent") || name.StartsWith("TextMeshPro") || name.StartsWith("Spine"))
                return 3000;
            if (name.Contains("Cutout")) return 2450;
            if (name.StartsWith("Skybox")) return 1000;
            return 2000;
        }

        /// <summary>The properties Unity's built-in shaders declare that materials use unset.</summary>
        internal static void AddDefaultProperties(UnityEngine.Shader shader)
        {
            void Color(string n, UnityEngine.Color c) => shader.m_Properties.Add(new UnityEngine.ShaderPropertyInfo { Name = n, Type = UnityEngine.Rendering.ShaderPropertyType.Color, DefaultValue = c });
            void Float(string n, float v) => shader.m_Properties.Add(new UnityEngine.ShaderPropertyInfo { Name = n, Type = UnityEngine.Rendering.ShaderPropertyType.Float, DefaultValue = new UnityEngine.Vector4(v, 0, 0, 0) });
            void Tex(string n, string def) => shader.m_Properties.Add(new UnityEngine.ShaderPropertyInfo { Name = n, Type = UnityEngine.Rendering.ShaderPropertyType.Texture, DefaultTexture = def });
            var name = shader.m_Name ?? "";
            if (name == "UI/Default" || name.StartsWith("UI/"))
            {
                Tex("_MainTex", "white");
                Color("_Color", UnityEngine.Color.white);
                Float("_StencilComp", 8);
                Float("_Stencil", 0);
                Float("_StencilOp", 0);
                Float("_StencilWriteMask", 255);
                Float("_StencilReadMask", 255);
                Float("_ColorMask", 15);
                Float("_UseUIAlphaClip", 0);
            }
            else if (name.StartsWith("Sprites/"))
            {
                Tex("_MainTex", "white");
                Color("_Color", UnityEngine.Color.white);
                Color("_RendererColor", UnityEngine.Color.white);
                Float("PixelSnap", 0);
            }
            else if (name.Contains("Particles"))
            {
                Tex("_MainTex", "white");
                Color("_TintColor", new UnityEngine.Color(0.5f, 0.5f, 0.5f, 0.5f));
                Color("_Color", UnityEngine.Color.white);
            }
            else
            {
                Tex("_MainTex", "white");
                Color("_Color", UnityEngine.Color.white);
                Float("_Cutoff", 0.5f);
                Float("_Glossiness", 0.5f);
                Float("_Metallic", 0f);
                Color("_EmissionColor", UnityEngine.Color.black);
            }
        }

        internal static UnityEngine.Object Resolve(string guid, long fileId)
        {
            if (string.Equals(guid, ExtraGuid, StringComparison.OrdinalIgnoreCase) && ExtraNames.TryGetValue(fileId, out var name) ||
                string.Equals(guid, DefaultGuid, StringComparison.OrdinalIgnoreCase) && DefaultNames.TryGetValue(fileId, out name))
            {
                return BuiltinObjects.Get(guid, fileId, name);
            }
            return null;
        }
    }

    /// <summary>The project's own `.shader` files, found by their declared names.</summary>
    internal static class ProjectShaders
    {
        internal static UnityEngine.Shader Find(string name)
        {
            if (!Native.IsReady) return null;
            try
            {
                var guid = Native.Call("shader-find", System.Text.Json.JsonSerializer.Serialize(new { name }));
                guid = guid.Trim('"');
                return string.IsNullOrEmpty(guid) ? null : AssetDatabaseRuntime.LoadMain(guid, typeof(UnityEngine.Shader)) as UnityEngine.Shader;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
