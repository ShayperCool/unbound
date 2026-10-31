using System;
using System.Collections.Generic;
using System.Text;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>A shader and the values of its properties. Changes are sent to the renderer before the
    /// next frame is drawn.</summary>
    [ClassId(21)]
    public partial class Material : Object, ISerializedAsset, Instantiation.ICloneable
    {
        internal sealed class TextureSlot
        {
            internal Texture Texture;
            internal Vector2 Scale = Vector2.one;
            internal Vector2 Offset;
        }

        Shader m_Shader;
        internal int m_CustomRenderQueue = -1;
        internal readonly Dictionary<int, float> m_Floats = new Dictionary<int, float>();
        internal readonly Dictionary<int, int> m_Ints = new Dictionary<int, int>();
        internal readonly Dictionary<int, Vector4> m_Vectors = new Dictionary<int, Vector4>();
        // Synthetic renderer copies contain property-block GPU values, already in working space.
        internal readonly HashSet<int> m_RawBindings = new HashSet<int>();
        internal readonly Dictionary<int, Matrix4x4> m_Matrices = new Dictionary<int, Matrix4x4>();
        internal readonly Dictionary<int, TextureSlot> m_Textures = new Dictionary<int, TextureSlot>();
        /// <summary>Buffers by property (ComputeBuffer or GraphicsBuffer): structured buffers, and
        /// constant buffers by their cbuffer's name.</summary>
        internal readonly Dictionary<int, object> m_Buffers = new Dictionary<int, object>();
        /// <summary>Array properties: float[], Vector4[] (colors too) or Matrix4x4[].</summary>
        internal readonly Dictionary<int, object> m_Arrays = new Dictionary<int, object>();
        internal readonly HashSet<string> m_Keywords = new HashSet<string>();
        /// <summary>Names set from script: the material has them whatever its shader declares.</summary>
        internal readonly HashSet<int> m_ScriptSet = new HashSet<int>();
        /// <summary>Texture names the material has a slot for: those of the shaders it had, those
        /// saved with it (the values of other saved names are kept for a later shader, unseen).</summary>
        internal readonly HashSet<int> m_TextureNames = new HashSet<int>();
        internal readonly Dictionary<string, string> m_Tags = new Dictionary<string, string>();
        internal ulong m_Handle;
        internal bool m_Dirty = true;
        bool m_EnableInstancing;
        MaterialGlobalIlluminationFlags m_GIFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        static readonly int s_Color = Shader.PropertyToID("_Color");
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");

        internal Material() { }

        public Material(Shader shader)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader), "Creating a material requires a shader.");
            m_Shader = shader;
            m_Name = shader.name;
            AddTextureNames();
            foreach (var pair in shader.m_DefaultTextures)
                if (pair.Value != null) Slot(Shader.PropertyToID(pair.Key)).Texture = pair.Value;
            DrawerKeywords();
        }

        void AddTextureNames()
        {
            if (m_Shader == null) return;
            foreach (var p in m_Shader.m_Properties)
                if (p.Type == ShaderPropertyType.Texture) m_TextureNames.Add(Shader.PropertyToID(p.Name));
        }

        /// <summary>A new material's keywords from its shader's keyword drawers at their defaults:
        /// <c>[Toggle]</c>, <c>[ToggleOff]</c>, <c>[KeywordEnum]</c> (measured, 6000.0).</summary>
        void DrawerKeywords()
        {
            foreach (var p in m_Shader.m_Properties)
            {
                foreach (var attribute in p.Attributes)
                {
                    int open = attribute.IndexOf('(');
                    var drawer = (open < 0 ? attribute : attribute.Substring(0, open)).Trim();
                    var args = open < 0 ? "" : attribute.Substring(open + 1).TrimEnd(')').Trim();
                    float value = p.DefaultValue.x;
                    switch (drawer)
                    {
                        case "Toggle":
                            if (value != 0f) m_Keywords.Add(args.Length > 0 ? args : p.Name.ToUpperInvariant() + "_ON");
                            break;
                        case "ToggleOff":
                            if (value == 0f) m_Keywords.Add(args.Length > 0 ? args : p.Name.ToUpperInvariant() + "_OFF");
                            break;
                        case "KeywordEnum":
                        {
                            var options = args.Split(',');
                            int index = (int)value;
                            if (index >= 0 && index < options.Length)
                                m_Keywords.Add((p.Name + "_" + options[index].Trim()).ToUpperInvariant().Replace(' ', '_'));
                            break;
                        }
                    }
                }
            }
        }

        public Material(Material source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            CopyFrom(source);
            m_Name = source.m_Name;
        }

        [Obsolete("Creating materials from shader source string is no longer supported. Use Shader assets instead.", false)]
        public Material(string contents) : this(Shader.Find("Unlit/Color")) { }

        void CopyFrom(Material source)
        {
            m_Shader = source.m_Shader;
            m_CustomRenderQueue = source.m_CustomRenderQueue;
            foreach (var p in source.m_Floats) m_Floats[p.Key] = p.Value;
            foreach (var p in source.m_Ints) m_Ints[p.Key] = p.Value;
            foreach (var p in source.m_Vectors) m_Vectors[p.Key] = p.Value;
            foreach (var p in source.m_Matrices) m_Matrices[p.Key] = p.Value;
            foreach (var p in source.m_Textures)
                m_Textures[p.Key] = new TextureSlot { Texture = p.Value.Texture, Scale = p.Value.Scale, Offset = p.Value.Offset };
            foreach (var p in source.m_Buffers) m_Buffers[p.Key] = p.Value;
            foreach (var p in source.m_Arrays) m_Arrays[p.Key] = ((Array)p.Value).Clone();
            foreach (var k in source.m_Keywords) m_Keywords.Add(k);
            m_DisabledPasses = source.m_DisabledPasses != null ? new HashSet<string>(source.m_DisabledPasses, StringComparer.OrdinalIgnoreCase) : null;
            m_ScriptSet.UnionWith(source.m_ScriptSet);
            m_RawBindings.UnionWith(source.m_RawBindings);
            m_TextureNames.UnionWith(source.m_TextureNames);
            foreach (var t in source.m_Tags) m_Tags[t.Key] = t.Value;
            m_EnableInstancing = source.m_EnableInstancing;
            MarkDirty();
        }

        Object Instantiation.ICloneable.CloneObject() => new Material(this);

        internal void MarkDirty()
        {
            m_Dirty = true;
            m_Version++;
        }

        /// <summary>Counts changes, for copies made from this material (property blocks).</summary>
        internal int m_Version;

        public Shader shader
        {
            get => m_Shader;
            set
            {
                if (value != m_Shader) KeepDefaultsOf(m_Shader);
                m_Shader = value;
                AddTextureNames();
                MarkDirty();
            }
        }

        /// <summary>A material holds a value for every property of its shader from the start, so
        /// the shader it leaves keeps its defaults for names the next one declares too (measured:
        /// a model material the built-in preprocessor made Standard and URP's made Lit keeps
        /// Standard's _Parallax, tests/projects/modelimport).</summary>
        void KeepDefaultsOf(Shader shader)
        {
            if (shader == null || m_Parent != null) return;
            foreach (var p in shader.m_Properties)
            {
                int id = Shader.PropertyToID(p.Name);
                switch (p.Type)
                {
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        if (!m_Floats.ContainsKey(id)) m_Floats[id] = p.DefaultValue.x;
                        break;
                    case ShaderPropertyType.Int:
                        if (!m_Ints.ContainsKey(id)) m_Ints[id] = (int)p.DefaultValue.x;
                        break;
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        if (!m_Vectors.ContainsKey(id)) m_Vectors[id] = p.DefaultValue;
                        break;
                }
            }
        }

        // The main colour and texture: the shader's properties marked [MainColor] and [MainTexture],
        // else _Color and _MainTex (measured, 6000.0).
        int MainColor => m_Shader != null ? m_Shader.FlaggedProperty(ShaderPropertyFlags.MainColor, s_Color) : s_Color;
        int MainTex => m_Shader != null ? m_Shader.FlaggedProperty(ShaderPropertyFlags.MainTexture, s_MainTex) : s_MainTex;

        public Color color
        {
            get => GetColor(MainColor);
            set => SetColor(MainColor, value);
        }

        public Texture mainTexture
        {
            get => GetTexture(MainTex);
            set => SetTexture(MainTex, value);
        }

        public Vector2 mainTextureOffset
        {
            get => GetTextureOffset(MainTex);
            set => SetTextureOffset(MainTex, value);
        }

        public Vector2 mainTextureScale
        {
            get => GetTextureScale(MainTex);
            set => SetTextureScale(MainTex, value);
        }

        public int renderQueue
        {
            get => Inherited().m_CustomRenderQueue >= 0 ? m_CustomRenderQueue : m_Shader != null ? m_Shader.renderQueue : 2000;
            set
            {
                m_CustomRenderQueue = value;
                MarkDirty();
            }
        }

        public int rawRenderQueue => m_CustomRenderQueue;

        public string[] shaderKeywords
        {
            get
            {
                Inherit();
                var list = new List<string>(m_Keywords);
                list.Sort(StringComparer.Ordinal);
                return list.ToArray();
            }
            set
            {
                m_Keywords.Clear();
                if (value != null) foreach (var k in value) if (!string.IsNullOrEmpty(k)) m_Keywords.Add(k);
                MarkDirty();
            }
        }

        public MaterialGlobalIlluminationFlags globalIlluminationFlags
        {
            get { Inherit(); return m_GIFlags; }
            set => m_GIFlags = value;
        }

        public bool doubleSidedGI
        {
            get { Inherit(); return m_DoubleSidedGI; }
            set => m_DoubleSidedGI = value;
        }
        bool m_DoubleSidedGI;

        public bool enableInstancing
        {
            get { Inherit(); return m_EnableInstancing; }
            set => m_EnableInstancing = value;
        }

        public int passCount => m_Shader != null ? m_Shader.passCount : 0;

        /// <summary>Whether the material is a variant of another (it has a parent).</summary>
        public bool isVariant => m_Parent != null;

        /// <summary>The material this one is a variant of. A variant loaded from its asset has the
        /// parent's value for every property it does not override, also when the parent changes
        /// later (measured: tests/projects/builtinfx material.variant.*); one made a variant from
        /// script keeps all its own values.</summary>
        public Material parent
        {
            get => m_Parent;
            set
            {
                m_Parent = value == this ? null : value;
                m_Own = null;
                m_InheritQueue = m_InheritFlags = m_InheritKeywords = false;
                m_InheritedVersion = -1;
            }
        }

        Material m_Parent;
        /// <summary>The properties a variant overrides (null: all of them, a variant made from
        /// script); the ones set from script count too.</summary>
        HashSet<int> m_Own;
        bool m_InheritQueue, m_InheritFlags, m_InheritKeywords;
        int m_InheritedVersion = -1;
        bool m_Inheriting;

        /// <summary>A variant takes its parent's values for what it does not override.</summary>
        internal void Inherit()
        {
            var parent = m_Parent;
            if (parent == null || m_Own == null || m_Inheriting) return;
            m_Inheriting = true;
            try
            {
                parent.Inherit();
                if (m_InheritedVersion == parent.m_Version) return;
                m_InheritedVersion = parent.m_Version;
                bool Own(int id) => m_Own.Contains(id) || m_ScriptSet.Contains(id);
                foreach (var p in parent.m_Floats) if (!Own(p.Key)) m_Floats[p.Key] = p.Value;
                foreach (var p in parent.m_Ints) if (!Own(p.Key)) m_Ints[p.Key] = p.Value;
                foreach (var p in parent.m_Vectors) if (!Own(p.Key)) m_Vectors[p.Key] = p.Value;
                foreach (var p in parent.m_Matrices) if (!Own(p.Key)) m_Matrices[p.Key] = p.Value;
                foreach (var p in parent.m_Textures)
                    if (!Own(p.Key)) m_Textures[p.Key] = new TextureSlot { Texture = p.Value.Texture, Scale = p.Value.Scale, Offset = p.Value.Offset };
                m_TextureNames.UnionWith(parent.m_TextureNames);
                if (m_InheritKeywords)
                {
                    m_Keywords.Clear();
                    m_Keywords.UnionWith(parent.m_Keywords);
                }
                if (m_InheritQueue) m_CustomRenderQueue = parent.m_CustomRenderQueue;
                if (m_InheritFlags)
                {
                    m_EnableInstancing = parent.m_EnableInstancing;
                    m_DoubleSidedGI = parent.m_DoubleSidedGI;
                    m_GIFlags = parent.m_GIFlags;
                }
                MarkDirty();
            }
            finally { m_Inheriting = false; }
        }

        // ------------------------------------------------------------------ keywords

        public void EnableKeyword(string keyword)
        {
            if (m_Keywords.Add(keyword)) MarkDirty();
        }

        public void DisableKeyword(string keyword)
        {
            if (m_Keywords.Remove(keyword)) MarkDirty();
        }

        public bool IsKeywordEnabled(string keyword) => Inherited().m_Keywords.Contains(keyword);

        Material Inherited()
        {
            Inherit();
            return this;
        }

        /// <summary>The LightMode tags of passes disabled on this material, compared
        /// case-insensitively (measured: tests/projects/builtinrp passes.*): the renderer skips
        /// them (built-in forward, shadow caster and depth passes, renderer lists).</summary>
        HashSet<string> m_DisabledPasses;

        internal bool HasDisabledPasses => m_DisabledPasses != null && m_DisabledPasses.Count > 0;

        /// <summary>Whether passes with this LightMode are disabled on the material.</summary>
        internal bool PassDisabled(string lightMode) => m_DisabledPasses != null && lightMode != null && m_DisabledPasses.Contains(lightMode);

        public void SetShaderPassEnabled(string passName, bool enabled)
        {
            if (passName == null) return;
            if (enabled)
            {
                m_DisabledPasses?.Remove(passName);
                return;
            }
            (m_DisabledPasses ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(passName);
        }

        public bool GetShaderPassEnabled(string passName) => passName == null || m_DisabledPasses == null || !m_DisabledPasses.Contains(passName);

        public string GetPassName(int pass)
        {
            var info = Unbound.Runtime.ShaderPassInfo.Of(m_Shader);
            return info != null && pass >= 0 && pass < info.Passes.Count ? info.Passes[pass].Name : "";
        }

        /// <summary>A pass by name, compared case-insensitively as Unity does.</summary>
        public int FindPass(string passName)
        {
            var info = Unbound.Runtime.ShaderPassInfo.Of(m_Shader);
            if (info == null || string.IsNullOrEmpty(passName)) return -1;
            for (int i = 0; i < info.Passes.Count; i++)
                if (string.Equals(info.Passes[i].Name, passName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void SetOverrideTag(string tag, string val)
        {
            if (string.IsNullOrEmpty(val)) m_Tags.Remove(tag);
            else m_Tags[tag] = val;
        }

        /// <summary>A tag: the material's override, else its shader's SubShader's.</summary>
        public string GetTag(string tag, bool searchFallbacks, string defaultValue)
        {
            if (m_Tags.TryGetValue(tag, out var value)) return value;
            var info = Unbound.Runtime.ShaderPassInfo.Of(m_Shader);
            if (info != null)
            {
                if ((searchFallbacks || info.DrawnShader == m_Shader.name) && info.Tags.TryGetValue(tag, out var shaderValue)) return shaderValue;
                return defaultValue;
            }
            return tag == "RenderType" ? (renderQueue >= 2500 ? "Transparent" : "Opaque") : defaultValue;
        }

        public string GetTag(string tag, bool searchFallbacks) => GetTag(tag, searchFallbacks, "");

        /// <summary>Activates a pass for GL drawing and Graphics.DrawMeshNow.</summary>
        public bool SetPass(int pass)
        {
            Unbound.Runtime.Immediate.PassMaterial = this;
            Unbound.Runtime.Immediate.PassIndex = pass;
            return true;
        }

        // ------------------------------------------------------------------ properties

        public bool HasProperty(string name) => HasProperty(Shader.PropertyToID(name));

        // A material has the properties its shader declares and the ones set from script; values
        // saved for other names stay unseen (measured, 6000.0, tests/projects/materials).
        public bool HasProperty(int nameID) =>
            Declared(nameID) != null || m_ScriptSet.Contains(nameID) || ScaleOffsetOf(nameID, out _);

        ShaderPropertyType? Declared(int nameID) => m_Shader?.PropertyById(nameID)?.Type;

        bool SetFromScript<T>(int nameID, Dictionary<int, T> values) => m_ScriptSet.Contains(nameID) && values.ContainsKey(nameID);

        bool IsFloat(int nameID) => Declared(nameID) is ShaderPropertyType.Float or ShaderPropertyType.Range || SetFromScript(nameID, m_Floats);
        bool IsInteger(int nameID) => Declared(nameID) == ShaderPropertyType.Int || SetFromScript(nameID, m_Ints);
        bool IsVector(int nameID) =>
            Declared(nameID) is ShaderPropertyType.Color or ShaderPropertyType.Vector || SetFromScript(nameID, m_Vectors) || ScaleOffsetOf(nameID, out _);
        bool IsTexture(int nameID) => Declared(nameID) == ShaderPropertyType.Texture || SetFromScript(nameID, m_Textures);
        bool IsMatrix(int nameID) => SetFromScript(nameID, m_Matrices);
        /// <summary>Texture getters read every slot the material has, saved for another shader too.</summary>
        bool HasTextureSlot(int nameID) => IsTexture(nameID) || m_Textures.ContainsKey(nameID) || m_TextureNames.Contains(nameID);

        /// <summary>What a getter logs for a name the material does not have (it returns the default).</summary>
        void Missing(int nameID, string kind) =>
            Debug.LogError($"Material '{m_Name}' with Shader '{(m_Shader != null ? m_Shader.name : "")}' doesn't have {kind} property '{Shader.IdToName(nameID)}'", this);

        public bool HasFloat(string name) => IsFloat(Shader.PropertyToID(name));
        public bool HasFloat(int nameID) => IsFloat(nameID);
        public bool HasInt(string name) => IsFloat(Shader.PropertyToID(name));
        public bool HasInt(int nameID) => IsFloat(nameID);
        public bool HasInteger(string name) => IsInteger(Shader.PropertyToID(name));
        public bool HasInteger(int nameID) => IsInteger(nameID);
        public bool HasColor(string name) => IsVector(Shader.PropertyToID(name));
        public bool HasColor(int nameID) => IsVector(nameID);
        public bool HasVector(string name) => IsVector(Shader.PropertyToID(name));
        public bool HasVector(int nameID) => IsVector(nameID);
        public bool HasTexture(string name) => IsTexture(Shader.PropertyToID(name));
        public bool HasTexture(int nameID) => IsTexture(nameID);
        public bool HasMatrix(string name) => IsMatrix(Shader.PropertyToID(name));
        public bool HasMatrix(int nameID) => IsMatrix(nameID);

        /// <summary>The shader's properties of a kind, in declaration order; a texture brings its
        /// _ST, _TexelSize and _HDR vectors (measured, 6000.0).</summary>
        public string[] GetPropertyNames(MaterialPropertyType type)
        {
            var names = new List<string>();
            if (m_Shader == null) return names.ToArray();
            foreach (var p in m_Shader.m_Properties)
            {
                switch (type)
                {
                    case MaterialPropertyType.Float when p.Type is ShaderPropertyType.Float or ShaderPropertyType.Range:
                    case MaterialPropertyType.Int when p.Type == ShaderPropertyType.Int:
                    case MaterialPropertyType.Texture when p.Type == ShaderPropertyType.Texture:
                    case MaterialPropertyType.Vector when p.Type is ShaderPropertyType.Color or ShaderPropertyType.Vector:
                        names.Add(p.Name);
                        break;
                    case MaterialPropertyType.Vector when p.Type == ShaderPropertyType.Texture:
                        names.Add(p.Name + "_ST");
                        names.Add(p.Name + "_TexelSize");
                        names.Add(p.Name + "_HDR");
                        break;
                }
            }
            return names.ToArray();
        }

        public void SetFloat(string name, float value) => SetFloat(Shader.PropertyToID(name), value);

        public void SetBuffer(string name, ComputeBuffer value) => SetBuffer(Shader.PropertyToID(name), value);
        public void SetBuffer(int nameID, ComputeBuffer value) => SetBufferObject(nameID, value);
        public void SetBuffer(string name, GraphicsBuffer value) => SetBuffer(Shader.PropertyToID(name), value);
        public void SetBuffer(int nameID, GraphicsBuffer value) => SetBufferObject(nameID, value);
        public void SetConstantBuffer(string name, ComputeBuffer value, int offset, int size) => SetConstantBuffer(Shader.PropertyToID(name), value, offset, size);
        public void SetConstantBuffer(int nameID, ComputeBuffer value, int offset, int size) => SetBufferObject(nameID, value, offset);
        public void SetConstantBuffer(string name, GraphicsBuffer value, int offset, int size) => SetConstantBuffer(Shader.PropertyToID(name), value, offset, size);
        public void SetConstantBuffer(int nameID, GraphicsBuffer value, int offset, int size) => SetBufferObject(nameID, value, offset);

        static bool s_WarnedBufferOffset;

        void SetBufferObject(int nameID, object buffer, int offset = 0)
        {
            if (offset != 0 && !s_WarnedBufferOffset)
            {
                s_WarnedBufferOffset = true;
                Debug.LogWarning("Unbound: constant buffers bound at an offset are not supported yet; the cbuffer reads the buffer from its start.");
            }
            if (buffer == null) m_Buffers.Remove(nameID);
            else m_Buffers[nameID] = buffer;
            MarkDirty();
        }

        internal static ulong BufferHandle(object buffer) => buffer switch
        {
            ComputeBuffer c => c.Handle,
            GraphicsBuffer g => g.Handle,
            _ => 0,
        };

        public void SetFloat(int nameID, float value)
        {
            m_RawBindings.Remove(nameID);
            m_Floats[nameID] = value;
            m_ScriptSet.Add(nameID);
            MarkDirty();
        }

        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));

        public float GetFloat(int nameID)
        {
            Inherit();
            if (!IsFloat(nameID)) { Missing(nameID, "a float or range"); return 0f; }
            if (m_Floats.TryGetValue(nameID, out var f)) return ConvertsProperty(nameID) ? Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(f)) : f;
            if (m_Ints.TryGetValue(nameID, out var i)) return i;
            return m_Shader != null && m_Shader.TryGetDefault(Shader.IdToName(nameID), out var info) ? info.DefaultValue.x : 0f;
        }

        public void SetInt(string name, int value) => SetFloat(name, value);
        public void SetInt(int nameID, int value) => SetFloat(nameID, value);
        public int GetInt(string name) => (int)GetFloat(name);
        public int GetInt(int nameID) => (int)GetFloat(nameID);

        public void SetInteger(string name, int value) => SetInteger(Shader.PropertyToID(name), value);

        public void SetInteger(int nameID, int value)
        {
            m_Ints[nameID] = value;
            m_ScriptSet.Add(nameID);
            MarkDirty();
        }

        public int GetInteger(string name) => GetInteger(Shader.PropertyToID(name));
        public int GetInteger(int nameID)
        {
            if (!IsInteger(nameID)) { Missing(nameID, "an int"); return 0; }
            if (m_Ints.TryGetValue(nameID, out var i)) return i;
            if (m_Floats.TryGetValue(nameID, out var f)) return (int)f;
            return m_Shader != null && m_Shader.PropertyById(nameID) is ShaderPropertyInfo info ? (int)info.DefaultValue.x : 0;
        }

        public void SetColor(string name, Color value) => SetVector(Shader.PropertyToID(name), value);
        public void SetColor(int nameID, Color value) => SetVector(nameID, value);
        public Color GetColor(string name) => GetVector(Shader.PropertyToID(name));
        public Color GetColor(int nameID) => GetVector(nameID);

        public void SetVector(string name, Vector4 value) => SetVector(Shader.PropertyToID(name), value);

        public void SetVector(int nameID, Vector4 value)
        {
            if (ScaleOffsetOf(nameID, out var texture))
            {
                var slot = Slot(texture);
                slot.Scale = new Vector2(value.x, value.y);
                slot.Offset = new Vector2(value.z, value.w);
                MarkDirty();
                return;
            }
            m_RawBindings.Remove(nameID);
            m_Vectors[nameID] = value;
            m_ScriptSet.Add(nameID);
            MarkDirty();
        }

        public Vector4 GetVector(string name) => GetVector(Shader.PropertyToID(name));

        public Vector4 GetVector(int nameID)
        {
            Inherit();
            if (!IsVector(nameID)) { Missing(nameID, "a color"); return Vector4.zero; }
            if (m_Vectors.TryGetValue(nameID, out var v))
                return ConvertsProperty(nameID) ? (Vector4)((Color)v).linear.gamma : v;
            if (ScaleOffsetOf(nameID, out var texture))
            {
                var scale = GetTextureScale(texture);
                var offset = GetTextureOffset(texture);
                return new Vector4(scale.x, scale.y, offset.x, offset.y);
            }
            var value = m_Shader != null && m_Shader.TryGetDefault(Shader.IdToName(nameID), out var info) ? info.DefaultValue : Vector4.zero;
            return ConvertsProperty(nameID) ? (Vector4)((Color)value).linear.gamma : value;
        }

        public void SetMatrix(string name, Matrix4x4 value) => SetMatrix(Shader.PropertyToID(name), value);

        public void SetMatrix(int nameID, Matrix4x4 value)
        {
            m_Matrices[nameID] = value;
            m_ScriptSet.Add(nameID);
            MarkDirty();
        }

        public Matrix4x4 GetMatrix(string name) => GetMatrix(Shader.PropertyToID(name));

        public Matrix4x4 GetMatrix(int nameID)
        {
            if (!IsMatrix(nameID)) { Missing(nameID, "a matrix"); return Matrix4x4.identity; }
            return m_Matrices.TryGetValue(nameID, out var m) ? m : Matrix4x4.identity;
        }

        public void SetTexture(string name, Texture value) => SetTexture(Shader.PropertyToID(name), value);

        public void SetTexture(int nameID, Texture value)
        {
            Slot(nameID).Texture = value;
            m_ScriptSet.Add(nameID);
            MarkDirty();
        }

        public void SetTexture(string name, RenderTexture value, RenderTextureSubElement element) => SetTexture(name, value);
        public void SetTexture(int nameID, RenderTexture value, RenderTextureSubElement element) => SetTexture(nameID, value);

        public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
        public Texture GetTexture(int nameID)
        {
            Inherit();
            if (!HasTextureSlot(nameID)) { Missing(nameID, "a texture"); return null; }
            return m_Textures.TryGetValue(nameID, out var slot) ? slot.Texture : null;
        }

        TextureSlot Slot(int nameID)
        {
            if (!m_Textures.TryGetValue(nameID, out var slot)) m_Textures[nameID] = slot = new TextureSlot();
            return slot;
        }

        public void SetTextureOffset(string name, Vector2 value) => SetTextureOffset(Shader.PropertyToID(name), value);

        public void SetTextureOffset(int nameID, Vector2 value)
        {
            m_Own?.Add(nameID);
            Slot(nameID).Offset = value;
            MarkDirty();
        }

        public void SetTextureScale(string name, Vector2 value) => SetTextureScale(Shader.PropertyToID(name), value);

        public void SetTextureScale(int nameID, Vector2 value)
        {
            m_Own?.Add(nameID);
            Slot(nameID).Scale = value;
            MarkDirty();
        }

        /// <summary>Whether a name is a texture property's `_ST` (its scale and offset). Measured on the
        /// 6000.0 Editor: GetVector, SetVector, HasProperty and HasVector take `_MainTex_ST` as
        /// _MainTex's scale and offset; a name without such a texture is no property.</summary>
        bool ScaleOffsetOf(int nameID, out int texture)
        {
            texture = 0;
            var name = Shader.IdToName(nameID);
            if (name == null || name.Length <= 3 || !name.EndsWith("_ST", StringComparison.Ordinal)) return false;
            var baseName = name.Substring(0, name.Length - 3);
            texture = Shader.PropertyToID(baseName);
            if (m_Textures.ContainsKey(texture)) return true;
            return m_Shader != null && m_Shader.TryGetDefault(baseName, out var info) && info.Type == UnityEngine.Rendering.ShaderPropertyType.Texture;
        }

        public Vector2 GetTextureOffset(string name) => GetTextureOffset(Shader.PropertyToID(name));
        public Vector2 GetTextureOffset(int nameID)
        {
            Inherit();
            if (!HasTextureSlot(nameID)) { Missing(nameID, "a texture"); return Vector2.zero; }
            return m_Textures.TryGetValue(nameID, out var slot) ? slot.Offset : Vector2.zero;
        }
        public Vector2 GetTextureScale(string name) => GetTextureScale(Shader.PropertyToID(name));
        public Vector2 GetTextureScale(int nameID)
        {
            Inherit();
            if (!HasTextureSlot(nameID)) { Missing(nameID, "a texture"); return Vector2.one; }
            return m_Textures.TryGetValue(nameID, out var slot) ? slot.Scale : Vector2.one;
        }

        /// <summary>Every texture slot, in name id order (measured, 6000.0).</summary>
        public int[] GetTexturePropertyNameIDs()
        {
            var ids = new SortedSet<int>(m_TextureNames);
            ids.UnionWith(m_Textures.Keys);
            var array = new int[ids.Count];
            ids.CopyTo(array);
            return array;
        }

        public string[] GetTexturePropertyNames() => Array.ConvertAll(GetTexturePropertyNameIDs(), Shader.IdToName);

        public void GetTexturePropertyNames(List<string> outNames)
        {
            outNames.Clear();
            outNames.AddRange(GetTexturePropertyNames());
        }

        public void GetTexturePropertyNameIDs(List<int> outNames)
        {
            outNames.Clear();
            outNames.AddRange(GetTexturePropertyNameIDs());
        }

        public void SetFloatArray(string name, float[] values) => SetFloatArray(Shader.PropertyToID(name), values);
        public void SetFloatArray(int nameID, float[] values) => SetArray(nameID, values);
        public void SetFloatArray(string name, List<float> values) => SetFloatArray(Shader.PropertyToID(name), values);
        public void SetFloatArray(int nameID, List<float> values) => SetArray(nameID, values);
        // Colors are kept as given (no color space conversion, as for vectors).
        public void SetColorArray(string name, Color[] values) => SetColorArray(Shader.PropertyToID(name), values);
        public void SetColorArray(int nameID, Color[] values) => SetArray(nameID, ToVectors(values));
        public void SetColorArray(string name, List<Color> values) => SetColorArray(Shader.PropertyToID(name), values);
        public void SetColorArray(int nameID, List<Color> values) => SetArray(nameID, ToVectors(values));
        public void SetVectorArray(string name, Vector4[] values) => SetVectorArray(Shader.PropertyToID(name), values);
        public void SetVectorArray(int nameID, Vector4[] values) => SetArray(nameID, values);
        public void SetVectorArray(string name, List<Vector4> values) => SetVectorArray(Shader.PropertyToID(name), values);
        public void SetVectorArray(int nameID, List<Vector4> values) => SetArray(nameID, values);
        public void SetMatrixArray(string name, Matrix4x4[] values) => SetMatrixArray(Shader.PropertyToID(name), values);
        public void SetMatrixArray(int nameID, Matrix4x4[] values) => SetArray(nameID, values);
        public void SetMatrixArray(string name, List<Matrix4x4> values) => SetMatrixArray(Shader.PropertyToID(name), values);
        public void SetMatrixArray(int nameID, List<Matrix4x4> values) => SetArray(nameID, values);

        public float[] GetFloatArray(string name) => GetFloatArray(Shader.PropertyToID(name));
        public float[] GetFloatArray(int nameID) => PropertyArrays.Get<float>(m_Arrays, nameID);
        public Vector4[] GetVectorArray(string name) => GetVectorArray(Shader.PropertyToID(name));
        public Vector4[] GetVectorArray(int nameID) => PropertyArrays.Get<Vector4>(m_Arrays, nameID);
        public Color[] GetColorArray(string name) => GetColorArray(Shader.PropertyToID(name));
        public Color[] GetColorArray(int nameID) => Array.ConvertAll(GetVectorArray(nameID) ?? Array.Empty<Vector4>(), v => (Color)v) is { Length: > 0 } c ? c : null;
        public Matrix4x4[] GetMatrixArray(string name) => GetMatrixArray(Shader.PropertyToID(name));
        public Matrix4x4[] GetMatrixArray(int nameID) => PropertyArrays.Get<Matrix4x4>(m_Arrays, nameID);
        public void GetFloatArray(string name, List<float> values) => GetFloatArray(Shader.PropertyToID(name), values);
        public void GetFloatArray(int nameID, List<float> values) => PropertyArrays.Get(m_Arrays, nameID, values);
        public void GetVectorArray(string name, List<Vector4> values) => GetVectorArray(Shader.PropertyToID(name), values);
        public void GetVectorArray(int nameID, List<Vector4> values) => PropertyArrays.Get(m_Arrays, nameID, values);
        public void GetColorArray(string name, List<Color> values) => GetColorArray(Shader.PropertyToID(name), values);
        public void GetColorArray(int nameID, List<Color> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            values.Clear();
            var v = GetColorArray(nameID);
            if (v != null) values.AddRange(v);
        }
        public void GetMatrixArray(string name, List<Matrix4x4> values) => GetMatrixArray(Shader.PropertyToID(name), values);
        public void GetMatrixArray(int nameID, List<Matrix4x4> values) => PropertyArrays.Get(m_Arrays, nameID, values);

        static Vector4[] ToVectors(IReadOnlyList<Color> colors)
        {
            if (colors == null) throw new ArgumentNullException("values");
            var v = new Vector4[colors.Count];
            for (int i = 0; i < v.Length; i++) v[i] = colors[i];
            return v;
        }

        void SetArray<T>(int nameID, IReadOnlyList<T> values)
        {
            PropertyArrays.Set(m_Arrays, nameID, values);
            MarkDirty();
        }

        public void CopyPropertiesFromMaterial(Material mat)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            var shaderBefore = m_Shader;
            m_Floats.Clear(); m_Ints.Clear(); m_Vectors.Clear(); m_Matrices.Clear(); m_Textures.Clear(); m_Keywords.Clear();
            m_ScriptSet.Clear(); m_TextureNames.Clear(); m_RawBindings.Clear();
            m_Buffers.Clear(); m_Arrays.Clear();
            CopyFrom(mat);
            m_Shader = shaderBefore;
        }

        /// <summary>Copies the properties this material's shader declares (measured: a shader
        /// without _MainTex gets no texture slot).</summary>
        public void CopyMatchingPropertiesFromMaterial(Material mat)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            mat.Inherit();
            Inherit();
            bool Declares(int id) => Declared(id) != null;
            foreach (var p in mat.m_Floats) if (Declares(p.Key)) m_Floats[p.Key] = p.Value;
            foreach (var p in mat.m_Ints) if (Declares(p.Key)) m_Ints[p.Key] = p.Value;
            foreach (var p in mat.m_Vectors) if (Declares(p.Key)) m_Vectors[p.Key] = p.Value;
            foreach (var p in mat.m_Matrices) if (Declares(p.Key)) m_Matrices[p.Key] = p.Value;
            foreach (var p in mat.m_Textures)
                if (Declares(p.Key)) m_Textures[p.Key] = new TextureSlot { Texture = p.Value.Texture, Scale = p.Value.Scale, Offset = p.Value.Offset };
            MarkDirty();
        }

        /// <summary>Interpolates the colors, floats, vectors and texture scales and offsets both
        /// materials have (textures stay). In a linear project colors, and floats marked [Gamma],
        /// interpolate in linear space (measured: tests/projects/builtinfx material.lerp.*).</summary>
        public void Lerp(Material start, Material end, float t)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (end == null) throw new ArgumentNullException(nameof(end));
            start.Inherit();
            end.Inherit();
            Inherit();
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            foreach (var p in start.m_Floats)
            {
                if (!end.m_Floats.TryGetValue(p.Key, out var e)) continue;
                if (linear && IsGamma(start.m_Shader, p.Key))
                    m_Floats[p.Key] = Mathf.LinearToGammaSpace(Mathf.Lerp(Mathf.GammaToLinearSpace(p.Value), Mathf.GammaToLinearSpace(e), t));
                else m_Floats[p.Key] = Mathf.Lerp(p.Value, e, t);
            }
            foreach (var p in start.m_Vectors)
            {
                if (!end.m_Vectors.TryGetValue(p.Key, out var e)) continue;
                bool color = (start.m_Shader?.PropertyById(p.Key)?.Type ?? ShaderPropertyType.Color) == ShaderPropertyType.Color;
                if (linear && color)
                {
                    var c = Color.Lerp(((Color)p.Value).linear, ((Color)e).linear, t).gamma;
                    c.a = Mathf.Lerp(p.Value.w, e.w, t);
                    m_Vectors[p.Key] = c;
                }
                else m_Vectors[p.Key] = Vector4.Lerp(p.Value, e, t);
            }
            foreach (var p in start.m_Textures)
            {
                if (!end.m_Textures.TryGetValue(p.Key, out var e)) continue;
                var slot = Slot(p.Key);
                slot.Scale = Vector2.Lerp(p.Value.Scale, e.Scale, t);
                slot.Offset = Vector2.Lerp(p.Value.Offset, e.Offset, t);
            }
            MarkDirty();
        }

        static bool IsGamma(Shader shader, int nameID)
        {
            var info = shader?.PropertyById(nameID);
            if (info != null && (info.Flags & ShaderPropertyFlags.Gamma) != 0) return true;
            // A built-in shader's attributes come from the renderer's reading of its source.
            return shader != null && ShaderPassInfo.Of(shader) is { } passes && passes.Gamma.Contains(Shader.IdToName(nameID) ?? "");
        }

        /// <summary>A checksum of what the material draws with: its shader, queue, keywords and
        /// property values (equal settings, equal checksums; Unity's own values differ).</summary>
        public int ComputeCRC()
        {
            Inherit();
            uint crc = 0xffffffffu;
            void Add(string s) { foreach (char c in s ?? "") crc = Crc(crc, (byte)c, (byte)(c >> 8)); }
            void Bytes(float f) { int b = BitConverter.SingleToInt32Bits(f); crc = Crc(Crc(crc, (byte)b, (byte)(b >> 8)), (byte)(b >> 16), (byte)(b >> 24)); }
            Add(m_Shader?.name);
            Bytes(renderQueue);
            var keywords = new List<string>(m_Keywords);
            keywords.Sort(StringComparer.Ordinal);
            foreach (var k in keywords) Add(k);
            foreach (var p in Sorted(m_Floats)) { Bytes(p.Key); Bytes(p.Value); }
            foreach (var p in Sorted(m_Ints)) { Bytes(p.Key); Bytes(p.Value); }
            foreach (var p in Sorted(m_Vectors)) { Bytes(p.Key); Bytes(p.Value.x); Bytes(p.Value.y); Bytes(p.Value.z); Bytes(p.Value.w); }
            foreach (var p in Sorted(m_Textures))
            {
                Bytes(p.Key);
                Bytes(p.Value.Texture != null ? p.Value.Texture.GetInstanceID() : 0);
                Bytes(p.Value.Scale.x); Bytes(p.Value.Scale.y); Bytes(p.Value.Offset.x); Bytes(p.Value.Offset.y);
            }
            return (int)~crc;
        }

        static List<KeyValuePair<int, T>> Sorted<T>(Dictionary<int, T> d)
        {
            var list = new List<KeyValuePair<int, T>>(d);
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            return list;
        }

        static uint Crc(uint crc, byte a, byte b)
        {
            foreach (var x in new[] { a, b })
            {
                crc ^= x;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc;
        }

        // ------------------------------------------------------------------ serialized data

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var shaderRef = body.Ref("m_Shader");
            m_Shader = shaderRef.IsNull ? null : resolver.Resolve(shaderRef, typeof(Shader)) as Shader;
            m_CustomRenderQueue = body.Int("m_CustomRenderQueue", -1);
            var keywords = body.Str("m_ShaderKeywords");
            foreach (var k in keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)) m_Keywords.Add(k);
            // Keywords the shader does not declare are kept too (shaderKeywords lists them).
            foreach (var list in new[] { "m_ValidKeywords", "m_InvalidKeywords" })
                foreach (var k in body.Get(list)?.Seq() ?? Array.Empty<YNode>())
                    if (k.Kind == YKind.Scalar && k.Scalar.Length > 0) m_Keywords.Add(k.Scalar);
            foreach (var pass in body.Get("disabledShaderPasses")?.Seq() ?? Array.Empty<YNode>())
                if (pass.Kind == YKind.Scalar && pass.Scalar.Length > 0)
                    (m_DisabledPasses ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(pass.Scalar);
            var tags = body.Get("stringTagMap");
            if (tags != null && tags.Kind == YKind.Map)
                for (int i = 0; i < tags.Keys.Length; i++) m_Tags[tags.Keys[i]] = tags.Values[i].AsString();
            var saved = body.Get("m_SavedProperties");
            if (saved != null)
            {
                foreach (var (name, value) in Entries(saved.Get("m_TexEnvs")))
                {
                    var slot = Slot(Shader.PropertyToID(name));
                    var texRef = value.Ref("m_Texture");
                    slot.Texture = texRef.IsNull ? null : resolver.Resolve(texRef, typeof(Texture)) as Texture;
                    slot.Scale = value.Get("m_Scale") != null ? value.Vector2("m_Scale") : Vector2.one;
                    slot.Offset = value.Vector2("m_Offset");
                }
                foreach (var (name, value) in Entries(saved.Get("m_Floats")))
                    m_Floats[Shader.PropertyToID(name)] = value.AsFloat();
                foreach (var (name, value) in Entries(saved.Get("m_Ints")))
                    m_Ints[Shader.PropertyToID(name)] = value.AsInt();
                foreach (var (name, value) in Entries(saved.Get("m_Colors")))
                    m_Vectors[Shader.PropertyToID(name)] = new Vector4(value.Float("r"), value.Float("g"), value.Float("b"), value.Float("a"));
            }
            // Instancing and GI flags (a variant takes its parent's, its own unless overridden).
            m_EnableInstancing = body.Bool("m_EnableInstancingVariants");
            m_DoubleSidedGI = body.Bool("m_DoubleSidedGI");
            m_GIFlags = (MaterialGlobalIlluminationFlags)body.Int("m_LightmapFlags", (int)MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            var parentRef = body.Ref("m_Parent");
            if (!parentRef.IsNull && resolver.Resolve(parentRef, typeof(Material)) is Material parentMaterial && parentMaterial != this)
            {
                // The saved properties are the variant's overrides; the rest is the parent's
                // (6000.0 stores only those, with m_ModifiedSerializedProperties flagging the
                // overridden settings: none here takes the parent's queue, keywords and flags).
                m_Parent = parentMaterial;
                m_Own = new HashSet<int>();
                foreach (var key in new[] { "m_TexEnvs", "m_Floats", "m_Ints", "m_Colors" })
                    foreach (var (name, _) in Entries(saved?.Get(key)))
                        m_Own.Add(Shader.PropertyToID(name));
                bool modified = body.Long("m_ModifiedSerializedProperties") != 0;
                m_InheritQueue = !modified && m_CustomRenderQueue < 0;
                m_InheritFlags = !modified;
                bool ownKeywords = (body.Get("m_ValidKeywords")?.Count ?? 0) + (body.Get("m_InvalidKeywords")?.Count ?? 0) > 0;
                m_InheritKeywords = !ownKeywords;
                m_InheritedVersion = -1;
            }
            AddTextureNames();
            MarkDirty();
            // A material sub-asset of an imported model: made by its importer.
            if (body.Get("m_UnboundMaterialImport") is YNode import && import.Kind == YKind.Map)
                ModelMaterialImport.Apply(this, import, resolver);
        }

        /// <summary>Saved properties are lists of one-key maps (`- _Color: {...}`), or maps in old files.</summary>
        static IEnumerable<(string, YNode)> Entries(YNode list)
        {
            if (list == null) yield break;
            if (list.Kind == YKind.Map)
            {
                for (int i = 0; i < list.Keys.Length; i++) yield return (list.Keys[i], list.Values[i]);
                yield break;
            }
            foreach (var item in list.Seq())
            {
                if (item.Kind != YKind.Map) continue;
                if (item.Keys.Length == 1) yield return (item.Keys[0], item.Values[0]);
                else if (item.Get("first") is YNode first)
                    yield return (first.Kind == YKind.Map ? first.Str("name") : first.AsString(), item.Get("second"));
            }
        }

        /// <summary>Measured in materialcolortransfer: Color converts unless HDR; [Gamma]
        /// requests conversion for Color, Vector and Float, including HDR Color. CPU storage
        /// retains the assigned value. Packets carry working-space values just like draw overrides.</summary>
        bool ConvertsProperty(int id)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || m_RawBindings.Contains(id)) return false;
            // Built-in Shader objects have only a minimal managed property list. Use the
            // selected source's binding flags when the renderer can supply them.
            var bindings = ShaderPassInfo.Of(m_Shader);
            if (bindings != null && bindings.Found && bindings.ColorProperties != null)
                return bindings.ColorProperties.Contains(Shader.IdToName(id) ?? "");
            var p = m_Shader?.PropertyById(id);
            return p != null && ((p.Flags & ShaderPropertyFlags.Gamma) != 0 ||
                (p.Type == ShaderPropertyType.Color && (p.Flags & ShaderPropertyFlags.HDR) == 0));
        }

        // ------------------------------------------------------------------ renderer side

        /// <summary>Sends the material to the renderer when it changed; returns its handle.</summary>
        int m_LodEpoch;
        string m_SentShader;

        internal unsafe ulong Sync()
        {
            if (m_Handle == 0) m_Handle = RenderHandles.Next();
            // A maximum LOD changed: the shader goes again when the SubShader it names changed.
            if (m_LodEpoch != Shader.s_LodEpoch)
            {
                m_LodEpoch = Shader.s_LodEpoch;
                if (m_Shader != null && m_Shader.DrawnName != m_SentShader) m_Dirty = true;
            }
            Inherit();
            if (!m_Dirty) return m_Handle;
            m_Dirty = false;
            m_SentShader = m_Shader != null ? m_Shader.DrawnName : "Hidden/InternalErrorShader";
            var name = Encoding.UTF8.GetBytes(m_SentShader);
            var keywords = Encoding.UTF8.GetBytes(string.Join(" ", m_Keywords));
            var names = new List<byte[]>();
            var props = new List<MaterialPropertyPacket>();
            var data = new List<float[]>();
            void Add(int id, int kind, Vector4 value, ulong texture, Vector4 st, float[] values = null, int components = 0)
            {
                var n = Encoding.UTF8.GetBytes(Shader.IdToName(id) ?? "");
                names.Add(n);
                data.Add(values);
                props.Add(new MaterialPropertyPacket { Kind = kind, Value = value, Texture = texture, TextureST = st, NameLength = n.Length, DataLength = values?.Length ?? 0, Components = components });
            }
            foreach (var p in m_Floats)
                Add(p.Key, 0, new Vector4(ConvertsProperty(p.Key) ? Mathf.GammaToLinearSpace(p.Value) : p.Value, 0, 0, 0), 0, default);
            foreach (var p in m_Ints) Add(p.Key, 3, new Vector4(p.Value, 0, 0, 0), 0, default);
            foreach (var p in m_Vectors)
                Add(p.Key, 1, ConvertsProperty(p.Key) ? (Vector4)((Color)p.Value).linear : p.Value, 0, default);
            foreach (var p in m_Textures)
                Add(p.Key, 2, default, TextureHandles.Of(p.Value.Texture), new Vector4(p.Value.Scale.x, p.Value.Scale.y, p.Value.Offset.x, p.Value.Offset.y));
            foreach (var p in m_Buffers) Add(p.Key, 5, default, BufferHandle(p.Value), default);
            foreach (var p in m_Matrices)
            {
                var m = new float[16];
                for (int i = 0; i < 16; i++) m[i] = p.Value[i];
                Add(p.Key, 4, default, 0, default, m, 16);
            }
            foreach (var p in m_Arrays)
                if (GfxStream.ArrayData(p.Value, out var components, out var floats)) Add(p.Key, 6, default, 0, default, floats, (int)components);
            var pinned = new System.Runtime.InteropServices.GCHandle[names.Count];
            var pinnedData = new System.Runtime.InteropServices.GCHandle[names.Count];
            try
            {
                var array = props.ToArray();
                for (int i = 0; i < names.Count; i++)
                {
                    pinned[i] = System.Runtime.InteropServices.GCHandle.Alloc(names[i], System.Runtime.InteropServices.GCHandleType.Pinned);
                    array[i].Name = (byte*)pinned[i].AddrOfPinnedObject();
                    if (data[i] != null)
                    {
                        pinnedData[i] = System.Runtime.InteropServices.GCHandle.Alloc(data[i], System.Runtime.InteropServices.GCHandleType.Pinned);
                        array[i].Data = (float*)pinnedData[i].AddrOfPinnedObject();
                    }
                }
                fixed (byte* n = name)
                fixed (byte* k = keywords)
                fixed (MaterialPropertyPacket* p = array)
                {
                    var packet = new MaterialPacket
                    {
                        ShaderName = n,
                        ShaderNameLength = name.Length,
                        RenderQueue = renderQueue,
                        Keywords = k,
                        KeywordsLength = keywords.Length,
                        PropertyCount = array.Length,
                        Properties = p,
                    };
                    Native.UpdateMaterial(m_Handle, &packet);
                }
            }
            finally
            {
                foreach (var h in pinned) if (h.IsAllocated) h.Free();
                foreach (var h in pinnedData) if (h.IsAllocated) h.Free();
            }
            return m_Handle;
        }

        internal override void OnEngineDestroy()
        {
            Native.ReleaseMaterial(m_Handle);
            m_Handle = 0;
        }
    }

    [Flags]
    public enum MaterialPropertyType
    {
        Float = 0,
        Int = 1,
        Vector = 2,
        Matrix = 3,
        Texture = 4,
        ConstantBuffer = 5,
        ComputeBuffer = 6,
    }

    [Flags]
    public enum MaterialGlobalIlluminationFlags
    {
        None = 0,
        RealtimeEmissive = 1,
        BakedEmissive = 2,
        EmissiveIsBlack = 4,
        AnyEmissive = 3,
    }
}
