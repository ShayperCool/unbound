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
        /// <summary>Default textures of texture properties (EditorMaterialUtility.SetShaderDefaults):
        /// new materials start with them.</summary>
        internal readonly Dictionary<string, Texture> m_DefaultTextures = new Dictionary<string, Texture>();
        internal readonly Dictionary<string, Texture> m_NonModifiableTextures = new Dictionary<string, Texture>();
        internal readonly List<ShaderPropertyInfo> m_Properties = new List<ShaderPropertyInfo>();
        int m_MaximumLOD = -1;

        static readonly Dictionary<string, Shader> s_ByName = new Dictionary<string, Shader>();
        static readonly Dictionary<string, int> s_PropertyIds = new Dictionary<string, int>();
        static readonly List<string> s_PropertyNames = new List<string>();
        // The engine's own names come before any shader's (texture property names list in id order).
        static readonly int s_EngineNames = PropertyToID("_MainTex") + PropertyToID("_Color");
        internal static readonly HashSet<string> s_GlobalKeywords = new HashSet<string>();
        internal static readonly Dictionary<int, object> s_Globals = new Dictionary<int, object>();

        Shader() { }

        internal static Shader Create(string name, int renderQueue)
        {
            var shader = new Shader { m_Name = name, m_RenderQueue = renderQueue };
            BuiltinShaders.AddDefaultProperties(shader);
            return shader;
        }

        [Unbound.EditorFaithful("the Editor's OpenGLCore device runs the desktop shaders projects ship (URP and post-processing ask before every effect); shaders without a desktop SubShader are not told apart yet")]
        public bool isSupported => true;

        /// <summary>The highest SubShader LOD this shader runs; -1 follows
        /// <see cref="globalMaximumLOD"/>, any other value replaces it (measured:
        /// tests/projects/builtinrp lod.*).</summary>
        public int maximumLOD
        {
            get => m_MaximumLOD;
            set
            {
                if (m_MaximumLOD == value) return;
                m_MaximumLOD = value;
                s_LodEpoch++;
            }
        }

        /// <summary>Counts changes of a maximum LOD: materials send their shader again.</summary>
        internal static int s_LodEpoch;

        /// <summary>The LOD the shader's SubShaders are picked under (int.MaxValue: every one).</summary>
        internal int EffectiveLOD => m_MaximumLOD >= 0 ? m_MaximumLOD : s_GlobalMaximumLOD;

        /// <summary>The name the renderer knows the shader by: with the maximum LOD when one
        /// applies (<c>Name|lod=N</c>: the first SubShader whose LOD is at most N, SubShaders
        /// without one being LOD 0, else the fallback's under the same limit).</summary>
        internal string DrawnName
        {
            get
            {
                int lod = EffectiveLOD;
                return lod == int.MaxValue ? m_Name : m_Name + "|lod=" + lod.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Whether a maximum LOD leaves the shader (and its fallbacks) without a SubShader:
        /// its renderers draw nothing (measured: not the error shader).</summary>
        internal bool IsLodHidden
        {
            get
            {
                if (EffectiveLOD == int.MaxValue) return false;
                var drawn = Unbound.Runtime.ShaderPassInfo.Of(this);
                if (drawn == null || drawn.Found) return false;
                var plain = Unbound.Runtime.ShaderPassInfo.OfName(m_Name);
                return plain != null && plain.Found;
            }
        }

        static readonly Dictionary<(string, string, string), Shader> s_Replacements = new Dictionary<(string, string, string), Shader>();

        /// <summary>A replacement shader for objects whose shader's <paramref name="tag"/> is
        /// <paramref name="value"/>: its first SubShader with that tag value (<c>Name|tag=K=V</c>;
        /// none found: the objects are not drawn).</summary>
        /// <summary>The shader as the Vertex Lit rendering path draws it: its first SubShader with
        /// Vertex or untagged passes, else its fallback's (the renderer picks by the name).</summary>
        internal static Shader VertexLit(Shader shader)
        {
            var key = (shader.m_Name, "|vertexlit", "");
            if (s_Replacements.TryGetValue(key, out var variant) && variant) return variant;
            using (ObjectRegistry.AsAssets())
                variant = new Shader { m_Name = shader.m_Name + "|vertexlit", m_RenderQueue = shader.m_RenderQueue, hideFlags = HideFlags.HideAndDontSave };
            variant.m_Properties.AddRange(shader.m_Properties);
            s_Replacements[key] = variant;
            return variant;
        }

        internal static Shader Replacement(Shader shader, string tag, string value)
        {
            var key = (shader.m_Name, tag, value);
            if (s_Replacements.TryGetValue(key, out var replacement) && replacement) return replacement;
            using (ObjectRegistry.AsAssets())
                replacement = new Shader { m_Name = shader.m_Name + "|tag=" + tag + "=" + value, m_RenderQueue = shader.m_RenderQueue, hideFlags = HideFlags.HideAndDontSave };
            replacement.m_Properties.AddRange(shader.m_Properties);
            s_Replacements[key] = replacement;
            return replacement;
        }

        /// <summary>The render queue its SubShader's `Queue` tag gives (a built-in shader's is read
        /// from its source when the renderer has it; until then a guess from its name).</summary>
        public int renderQueue
        {
            get
            {
                if (m_QueueFromSource && m_QueueTried != Time.frameCount)
                {
                    // At most once a frame while the renderer cannot answer yet.
                    m_QueueTried = Time.frameCount;
                    var info = Unbound.Runtime.ShaderPassInfo.Of(this);
                    if (info != null)
                    {
                        m_QueueFromSource = false;
                        if (info.Found && info.Tags.TryGetValue("Queue", out var queue) && !string.IsNullOrEmpty(queue))
                            m_RenderQueue = ShaderLab.QueueTag(queue);
                    }
                }
                return m_RenderQueue;
            }
        }

        /// <summary>The queue still comes from the renderer's reading of the source.</summary>
        internal bool m_QueueFromSource;
        int m_QueueTried = -1;


        public int subshaderCount
        {
            get
            {
                Unbound.Runtime.Stubs.Ignored("Shader.subshaderCount", "is always 1: SubShaders are not counted yet");
                return 1;
            }
        }

        static int s_GlobalMaximumLOD = int.MaxValue;

        /// <summary>The highest SubShader LOD shaders without a <see cref="maximumLOD"/> of their own
        /// run.</summary>
        public static int globalMaximumLOD
        {
            get => s_GlobalMaximumLOD;
            set
            {
                if (s_GlobalMaximumLOD == value) return;
                s_GlobalMaximumLOD = value;
                s_LodEpoch++;
            }
        }

        static string s_GlobalRenderPipeline = "";

        /// <summary>The active pipeline's tag: SubShaders whose <c>RenderPipeline</c> tag names
        /// another pipeline are skipped (empty: the built-in pipeline, which skips every tagged one).</summary>
        public static string globalRenderPipeline
        {
            get => s_GlobalRenderPipeline;
            set
            {
                value ??= "";
                if (value == s_GlobalRenderPipeline) return;
                s_GlobalRenderPipeline = value;
                GfxStream.RenderPipeline(value);
            }
        }

        static readonly Dictionary<string, int> s_TagIds = new Dictionary<string, int>();
        static readonly List<string> s_TagNames = new List<string> { "" };

        /// <summary>A tag name's id (0: the empty name).</summary>
        internal static int TagToID(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            lock (s_TagIds)
            {
                if (s_TagIds.TryGetValue(name, out var id)) return id;
                id = s_TagNames.Count;
                s_TagNames.Add(name);
                s_TagIds[name] = id;
                return id;
            }
        }

        internal static string IDToTag(int id)
        {
            lock (s_TagIds) return id > 0 && id < s_TagNames.Count ? s_TagNames[id] : "";
        }

        static readonly Dictionary<string, uint> s_KeywordIndices = new Dictionary<string, uint>();

        /// <summary>A keyword name's index in the global keyword space.</summary>
        internal static uint KeywordIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return uint.MaxValue;
            lock (s_KeywordIndices)
            {
                if (!s_KeywordIndices.TryGetValue(name, out var index))
                    s_KeywordIndices[name] = index = (uint)s_KeywordIndices.Count;
                return index;
            }
        }

        // ------------------------------------------------------------------ lookup

        public static Shader Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (s_ByName.TryGetValue(name, out var cached) && cached) return cached;
            // A built-in's name is Unity's; another hidden one may be the project's own (SoftMask's
            // `Hidden/UI/Default (SoftMaskable)`), whose queue and properties its file gives.
            var shader = BuiltinShaders.IsBuiltin(name)
                ? BuiltinShaders.ByName(name)
                : ProjectShaders.Find(name) ?? BuiltinShaders.ByName(name);
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

        public ShaderPropertyFlags GetPropertyFlags(int propertyIndex) => Property(propertyIndex).Flags;

        public string[] GetPropertyAttributes(int propertyIndex) => (string[])Property(propertyIndex).Attributes.Clone();

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

        /// <summary>The declared property of a name id, else null.</summary>
        internal ShaderPropertyInfo PropertyById(int nameID)
        {
            if (m_ById == null || m_ByIdCount != m_Properties.Count)
            {
                m_ById = new Dictionary<int, ShaderPropertyInfo>();
                foreach (var p in m_Properties) m_ById.TryAdd(PropertyToID(p.Name), p);
                m_ByIdCount = m_Properties.Count;
            }
            return m_ById.TryGetValue(nameID, out var info) ? info : null;
        }

        Dictionary<int, ShaderPropertyInfo> m_ById;
        int m_ByIdCount;

        internal void PropertiesChanged() => m_ById = null;

        /// <summary>The property flagged with <paramref name="flag"/> (MainTexture, MainColor), else
        /// <paramref name="fallback"/>.</summary>
        internal int FlaggedProperty(ShaderPropertyFlags flag, int fallback)
        {
            foreach (var p in m_Properties)
                if ((p.Flags & flag) != 0) return PropertyToID(p.Name);
            return fallback;
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

        public static void EnableKeyword(string keyword)
        {
            if (s_GlobalKeywords.Add(keyword)) GfxStream.Keyword(keyword, true);
        }

        public static void DisableKeyword(string keyword)
        {
            if (s_GlobalKeywords.Remove(keyword)) GfxStream.Keyword(keyword, false);
        }

        public static bool IsKeywordEnabled(string keyword) => s_GlobalKeywords.Contains(keyword);

        // Global properties are render state: each change is a command, in order with the draws.
        static void SetGlobal(int nameID, object value)
        {
            s_Globals[nameID] = value;
            var name = IdToName(nameID);
            if (name == null) return;
            switch (value)
            {
                case float f: GfxStream.GlobalFloat(name, f); break;
                case int i: GfxStream.GlobalInt(name, i); break;
                case Vector4 v: GfxStream.GlobalVector(name, v); break;
                case Matrix4x4 m: GfxStream.GlobalMatrix(name, m); break;
                case Texture t: GfxStream.GlobalTexture(name, TextureHandles.Of(t)); break;
                case null: GfxStream.GlobalTexture(name, 0); break;
            }
        }

        public static void SetGlobalFloat(string name, float value) => SetGlobal(PropertyToID(name), value);
        public static void SetGlobalFloat(int nameID, float value) => SetGlobal(nameID, value);
        public static void SetGlobalInt(string name, int value) => SetGlobal(PropertyToID(name), (float)value);
        public static void SetGlobalInt(int nameID, int value) => SetGlobal(nameID, (float)value);
        public static void SetGlobalInteger(string name, int value) => SetGlobal(PropertyToID(name), value);
        public static void SetGlobalInteger(int nameID, int value) => SetGlobal(nameID, value);
        public static void SetGlobalVector(string name, Vector4 value) => SetGlobal(PropertyToID(name), value);
        public static void SetGlobalVector(int nameID, Vector4 value) => SetGlobal(nameID, value);
        public static void SetGlobalColor(string name, Color value) => SetGlobal(PropertyToID(name), (Vector4)value);
        public static void SetGlobalColor(int nameID, Color value) => SetGlobal(nameID, (Vector4)value);
        public static void SetGlobalTexture(string name, Texture value) => SetGlobal(PropertyToID(name), value);
        public static void SetGlobalTexture(int nameID, Texture value) => SetGlobal(nameID, value);
        public static void SetGlobalMatrix(string name, Matrix4x4 value) => SetGlobal(PropertyToID(name), value);

        public static void SetGlobalBuffer(int nameID, ComputeBuffer value) => SetGlobalBuffer(nameID, value?.Handle ?? 0, value);
        public static void SetGlobalBuffer(string name, ComputeBuffer value) => SetGlobalBuffer(PropertyToID(name), value);
        public static void SetGlobalBuffer(int nameID, GraphicsBuffer value) => SetGlobalBuffer(nameID, value?.Handle ?? 0, value);
        public static void SetGlobalBuffer(string name, GraphicsBuffer value) => SetGlobalBuffer(PropertyToID(name), value);

        /// <summary>A constant buffer bound to a cbuffer by its name: the draws' cbuffer of that name
        /// reads the buffer's bytes from <paramref name="offset"/>.</summary>
        public static void SetGlobalConstantBuffer(int nameID, ComputeBuffer value, int offset, int size) => SetGlobalConstantBuffer(nameID, value?.Handle ?? 0, value, offset);
        public static void SetGlobalConstantBuffer(string name, ComputeBuffer value, int offset, int size) => SetGlobalConstantBuffer(PropertyToID(name), value, offset, size);
        public static void SetGlobalConstantBuffer(int nameID, GraphicsBuffer value, int offset, int size) => SetGlobalConstantBuffer(nameID, value?.Handle ?? 0, value, offset);
        public static void SetGlobalConstantBuffer(string name, GraphicsBuffer value, int offset, int size) => SetGlobalConstantBuffer(PropertyToID(name), value, offset, size);

        static bool s_WarnedConstantOffset;

        static void SetGlobalConstantBuffer(int nameID, ulong handle, object buffer, int offset)
        {
            if (offset != 0 && !s_WarnedConstantOffset)
            {
                s_WarnedConstantOffset = true;
                Debug.LogWarning("Unbound: constant buffers bound at an offset are not supported yet; the cbuffer reads the buffer from its start.");
            }
            SetGlobalBuffer(nameID, handle, buffer);
        }

        static void SetGlobalBuffer(int nameID, ulong handle, object buffer)
        {
            s_Globals[nameID] = buffer;
            var name = IdToName(nameID);
            if (name != null) GfxStream.GlobalBuffer(name, handle);
        }
        public static void SetGlobalMatrix(int nameID, Matrix4x4 value) => SetGlobal(nameID, value);
        public static void SetGlobalFloatArray(string name, float[] values) => SetGlobalArray(PropertyToID(name), values);
        public static void SetGlobalFloatArray(int nameID, float[] values) => SetGlobalArray(nameID, values);
        public static void SetGlobalVectorArray(string name, Vector4[] values) => SetGlobalArray(PropertyToID(name), values);
        public static void SetGlobalVectorArray(int nameID, Vector4[] values) => SetGlobalArray(nameID, values);
        public static void SetGlobalMatrixArray(string name, Matrix4x4[] values) => SetGlobalArray(PropertyToID(name), values);
        public static void SetGlobalMatrixArray(int nameID, Matrix4x4[] values) => SetGlobalArray(nameID, values);

        /// <summary>A global array (Unity keeps a copy: later changes to the array do not apply).</summary>
        static void SetGlobalArray(int nameID, Array values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0) throw new ArgumentException("Zero-sized array is not allowed.");
            var copy = (Array)values.Clone();
            s_Globals[nameID] = copy;
            var name = IdToName(nameID);
            if (name != null && GfxStream.ArrayData(copy, out var components, out var data)) GfxStream.GlobalArray(name, components, data);
        }

        public static float GetGlobalFloat(string name) => GetGlobalFloat(PropertyToID(name));
        public static float GetGlobalFloat(int nameID) => s_Globals.TryGetValue(nameID, out var v) && v is float f ? f : 0f;
        public static int GetGlobalInt(string name) => (int)GetGlobalFloat(name);
        public static int GetGlobalInt(int nameID) => (int)GetGlobalFloat(nameID);
        public static int GetGlobalInteger(string name) => GetGlobalInteger(PropertyToID(name));
        public static int GetGlobalInteger(int nameID) => s_Globals.TryGetValue(nameID, out var v) && v is int i ? i : 0;
        public static Vector4 GetGlobalVector(string name) => GetGlobalVector(PropertyToID(name));
        public static Vector4 GetGlobalVector(int nameID) => s_Globals.TryGetValue(nameID, out var v)
            ? v switch { Vector4 x => x, Vector4[] a when a.Length > 0 => a[0], _ => Vector4.zero }
            : Vector4.zero;
        public static Color GetGlobalColor(string name) => GetGlobalVector(name);
        public static Color GetGlobalColor(int nameID) => GetGlobalVector(nameID);
        public static Texture GetGlobalTexture(string name) => GetGlobalTexture(PropertyToID(name));
        public static Texture GetGlobalTexture(int nameID) => s_Globals.TryGetValue(nameID, out var v) ? v as Texture : null;
        public static Matrix4x4 GetGlobalMatrix(string name) => GetGlobalMatrix(PropertyToID(name));
        public static Matrix4x4 GetGlobalMatrix(int nameID) => s_Globals.TryGetValue(nameID, out var v) && v is Matrix4x4 m ? m : Matrix4x4.identity;

        [Unbound.EditorFaithful("no-op is equivalent: warming up only avoids first-use hitches")]
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
        internal ShaderPropertyFlags Flags;
        internal string[] Attributes = Array.Empty<string>();
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

        /// <summary>Whether a name is one of Unity's built-in shaders' (its resources list it).</summary>
        internal static bool IsBuiltin(string name) => ExtraNames.ContainsValue(name) || DefaultNames.ContainsValue(name);

        internal static UnityEngine.Shader ByName(string name)
        {
            if (s_Shaders.TryGetValue(name, out var shader)) return shader;
            if (!ExtraNames.ContainsValue(name) && !DefaultNames.ContainsValue(name) && !name.StartsWith("Hidden/"))
                return null;
            shader = UnityEngine.Shader.Create(name, DefaultQueue(name));
            shader.m_QueueFromSource = true;
            // The properties its source in the release's built-in shaders declares.
            var properties = SourceProperties(name);
            if (properties != null && properties.Count > 0)
            {
                shader.m_Properties.Clear();
                shader.m_Properties.AddRange(properties);
                shader.PropertiesChanged();
            }
            s_Shaders[name] = shader;
            return shader;
        }

        static List<UnityEngine.ShaderPropertyInfo> SourceProperties(string name)
        {
            if (!Native.IsReady) return null;
            try
            {
                var source = System.Text.Json.JsonSerializer.Deserialize<string>(
                    Native.Call("builtin-shader-source", System.Text.Json.JsonSerializer.Serialize(new { name })));
                return string.IsNullOrEmpty(source) ? null : ShaderLab.Properties(source);
            }
            catch (Exception)
            {
                return null;
            }
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
