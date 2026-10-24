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
        internal readonly Dictionary<int, Matrix4x4> m_Matrices = new Dictionary<int, Matrix4x4>();
        internal readonly Dictionary<int, TextureSlot> m_Textures = new Dictionary<int, TextureSlot>();
        internal readonly HashSet<string> m_Keywords = new HashSet<string>();
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
            m_Name = "";
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
            foreach (var k in source.m_Keywords) m_Keywords.Add(k);
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
                m_Shader = value;
                MarkDirty();
            }
        }

        public Color color
        {
            get => GetColor(s_Color);
            set => SetColor(s_Color, value);
        }

        public Texture mainTexture
        {
            get => GetTexture(s_MainTex);
            set => SetTexture(s_MainTex, value);
        }

        public Vector2 mainTextureOffset
        {
            get => GetTextureOffset(s_MainTex);
            set => SetTextureOffset(s_MainTex, value);
        }

        public Vector2 mainTextureScale
        {
            get => GetTextureScale(s_MainTex);
            set => SetTextureScale(s_MainTex, value);
        }

        public int renderQueue
        {
            get => m_CustomRenderQueue >= 0 ? m_CustomRenderQueue : m_Shader != null ? m_Shader.renderQueue : 2000;
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
            get => m_GIFlags;
            set => m_GIFlags = value;
        }

        public bool doubleSidedGI { get; set; }

        public bool enableInstancing
        {
            get => m_EnableInstancing;
            set => m_EnableInstancing = value;
        }

        public int passCount => 1;

        public bool isVariant => false;

        public Material parent { get; set; }

        // ------------------------------------------------------------------ keywords

        public void EnableKeyword(string keyword)
        {
            if (m_Keywords.Add(keyword)) MarkDirty();
        }

        public void DisableKeyword(string keyword)
        {
            if (m_Keywords.Remove(keyword)) MarkDirty();
        }

        public bool IsKeywordEnabled(string keyword) => m_Keywords.Contains(keyword);

        public void SetShaderPassEnabled(string passName, bool enabled) { }

        public bool GetShaderPassEnabled(string passName) => true;

        public string GetPassName(int pass) => "";

        public int FindPass(string passName) => -1;

        public void SetOverrideTag(string tag, string val)
        {
            if (string.IsNullOrEmpty(val)) m_Tags.Remove(tag);
            else m_Tags[tag] = val;
        }

        public string GetTag(string tag, bool searchFallbacks, string defaultValue) =>
            m_Tags.TryGetValue(tag, out var value) ? value : tag == "RenderType" ? (renderQueue >= 2500 ? "Transparent" : "Opaque") : defaultValue;

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

        public bool HasProperty(int nameID)
        {
            if (m_Floats.ContainsKey(nameID) || m_Ints.ContainsKey(nameID) || m_Vectors.ContainsKey(nameID) || m_Textures.ContainsKey(nameID) || m_Matrices.ContainsKey(nameID))
                return true;
            return m_Shader != null && m_Shader.TryGetDefault(Shader.IdToName(nameID), out _);
        }

        public bool HasFloat(string name) => HasProperty(name);
        public bool HasFloat(int nameID) => HasProperty(nameID);
        public bool HasInt(string name) => HasProperty(name);
        public bool HasInt(int nameID) => HasProperty(nameID);
        public bool HasInteger(string name) => HasProperty(name);
        public bool HasInteger(int nameID) => HasProperty(nameID);
        public bool HasColor(string name) => HasProperty(name);
        public bool HasColor(int nameID) => HasProperty(nameID);
        public bool HasVector(string name) => HasProperty(name);
        public bool HasVector(int nameID) => HasProperty(nameID);
        public bool HasTexture(string name) => HasProperty(name);
        public bool HasTexture(int nameID) => HasProperty(nameID);
        public bool HasMatrix(string name) => HasProperty(name);
        public bool HasMatrix(int nameID) => HasProperty(nameID);

        public void SetFloat(string name, float value) => SetFloat(Shader.PropertyToID(name), value);

        public void SetFloat(int nameID, float value)
        {
            m_Floats[nameID] = value;
            MarkDirty();
        }

        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));

        public float GetFloat(int nameID)
        {
            if (m_Floats.TryGetValue(nameID, out var f)) return f;
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
            MarkDirty();
        }

        public int GetInteger(string name) => GetInteger(Shader.PropertyToID(name));
        public int GetInteger(int nameID) => m_Ints.TryGetValue(nameID, out var i) ? i : (int)GetFloat(nameID);

        public void SetColor(string name, Color value) => SetVector(Shader.PropertyToID(name), value);
        public void SetColor(int nameID, Color value) => SetVector(nameID, value);
        public Color GetColor(string name) => GetVector(Shader.PropertyToID(name));
        public Color GetColor(int nameID) => GetVector(nameID);

        public void SetVector(string name, Vector4 value) => SetVector(Shader.PropertyToID(name), value);

        public void SetVector(int nameID, Vector4 value)
        {
            m_Vectors[nameID] = value;
            MarkDirty();
        }

        public Vector4 GetVector(string name) => GetVector(Shader.PropertyToID(name));

        public Vector4 GetVector(int nameID)
        {
            if (m_Vectors.TryGetValue(nameID, out var v)) return v;
            return m_Shader != null && m_Shader.TryGetDefault(Shader.IdToName(nameID), out var info) ? info.DefaultValue : Vector4.zero;
        }

        public void SetMatrix(string name, Matrix4x4 value) => SetMatrix(Shader.PropertyToID(name), value);

        public void SetMatrix(int nameID, Matrix4x4 value)
        {
            m_Matrices[nameID] = value;
            MarkDirty();
        }

        public Matrix4x4 GetMatrix(string name) => GetMatrix(Shader.PropertyToID(name));
        public Matrix4x4 GetMatrix(int nameID) => m_Matrices.TryGetValue(nameID, out var m) ? m : Matrix4x4.identity;

        public void SetTexture(string name, Texture value) => SetTexture(Shader.PropertyToID(name), value);

        public void SetTexture(int nameID, Texture value)
        {
            Slot(nameID).Texture = value;
            MarkDirty();
        }

        public void SetTexture(string name, RenderTexture value, RenderTextureSubElement element) => SetTexture(name, value);
        public void SetTexture(int nameID, RenderTexture value, RenderTextureSubElement element) => SetTexture(nameID, value);

        public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
        public Texture GetTexture(int nameID) => m_Textures.TryGetValue(nameID, out var slot) ? slot.Texture : null;

        TextureSlot Slot(int nameID)
        {
            if (!m_Textures.TryGetValue(nameID, out var slot)) m_Textures[nameID] = slot = new TextureSlot();
            return slot;
        }

        public void SetTextureOffset(string name, Vector2 value) => SetTextureOffset(Shader.PropertyToID(name), value);

        public void SetTextureOffset(int nameID, Vector2 value)
        {
            Slot(nameID).Offset = value;
            MarkDirty();
        }

        public void SetTextureScale(string name, Vector2 value) => SetTextureScale(Shader.PropertyToID(name), value);

        public void SetTextureScale(int nameID, Vector2 value)
        {
            Slot(nameID).Scale = value;
            MarkDirty();
        }

        public Vector2 GetTextureOffset(string name) => GetTextureOffset(Shader.PropertyToID(name));
        public Vector2 GetTextureOffset(int nameID) => m_Textures.TryGetValue(nameID, out var slot) ? slot.Offset : Vector2.zero;
        public Vector2 GetTextureScale(string name) => GetTextureScale(Shader.PropertyToID(name));
        public Vector2 GetTextureScale(int nameID) => m_Textures.TryGetValue(nameID, out var slot) ? slot.Scale : Vector2.one;

        public string[] GetTexturePropertyNames()
        {
            var names = new List<string>();
            foreach (var id in m_Textures.Keys) names.Add(Shader.IdToName(id));
            return names.ToArray();
        }

        public int[] GetTexturePropertyNameIDs() => new List<int>(m_Textures.Keys).ToArray();

        public void GetTexturePropertyNames(List<string> outNames)
        {
            outNames.Clear();
            outNames.AddRange(GetTexturePropertyNames());
        }

        public void GetTexturePropertyNameIDs(List<int> outNames)
        {
            outNames.Clear();
            outNames.AddRange(m_Textures.Keys);
        }

        public void SetFloatArray(string name, float[] values) { }
        public void SetFloatArray(int nameID, float[] values) { }
        public void SetFloatArray(string name, List<float> values) { }
        public void SetFloatArray(int nameID, List<float> values) { }
        public void SetColorArray(string name, Color[] values) { }
        public void SetColorArray(int nameID, Color[] values) { }
        public void SetVectorArray(string name, Vector4[] values) { }
        public void SetVectorArray(int nameID, Vector4[] values) { }
        public void SetMatrixArray(string name, Matrix4x4[] values) { }
        public void SetMatrixArray(int nameID, Matrix4x4[] values) { }

        public void CopyPropertiesFromMaterial(Material mat)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            var shaderBefore = m_Shader;
            m_Floats.Clear(); m_Ints.Clear(); m_Vectors.Clear(); m_Matrices.Clear(); m_Textures.Clear(); m_Keywords.Clear();
            CopyFrom(mat);
            m_Shader = shaderBefore;
        }

        public void CopyMatchingPropertiesFromMaterial(Material mat) => CopyPropertiesFromMaterial(mat);

        public void Lerp(Material start, Material end, float t)
        {
            foreach (var p in start.m_Floats)
                if (end.m_Floats.TryGetValue(p.Key, out var e)) m_Floats[p.Key] = Mathf.Lerp(p.Value, e, t);
            foreach (var p in start.m_Vectors)
                if (end.m_Vectors.TryGetValue(p.Key, out var e)) m_Vectors[p.Key] = Vector4.Lerp(p.Value, e, t);
            MarkDirty();
        }

        public int ComputeCRC() => GetHashCode();

        // ------------------------------------------------------------------ serialized data

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var shaderRef = body.Ref("m_Shader");
            m_Shader = shaderRef.IsNull ? null : resolver.Resolve(shaderRef, typeof(Shader)) as Shader;
            m_CustomRenderQueue = body.Int("m_CustomRenderQueue", -1);
            var keywords = body.Str("m_ShaderKeywords");
            foreach (var k in keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)) m_Keywords.Add(k);
            foreach (var k in body.Get("m_ValidKeywords")?.Seq() ?? Array.Empty<YNode>())
                if (k.Kind == YKind.Scalar && k.Scalar.Length > 0) m_Keywords.Add(k.Scalar);
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
            MarkDirty();
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

        // ------------------------------------------------------------------ renderer side

        /// <summary>Sends the material to the renderer when it changed; returns its handle.</summary>
        internal unsafe ulong Sync()
        {
            if (m_Handle == 0) m_Handle = RenderHandles.Next();
            if (!m_Dirty) return m_Handle;
            m_Dirty = false;
            var name = Encoding.UTF8.GetBytes(m_Shader != null ? m_Shader.name : "Hidden/InternalErrorShader");
            var keywords = Encoding.UTF8.GetBytes(string.Join(" ", m_Keywords));
            var names = new List<byte[]>();
            var props = new List<MaterialPropertyPacket>();
            void Add(int id, int kind, Vector4 value, ulong texture, Vector4 st)
            {
                var n = Encoding.UTF8.GetBytes(Shader.IdToName(id) ?? "");
                names.Add(n);
                props.Add(new MaterialPropertyPacket { Kind = kind, Value = value, Texture = texture, TextureST = st, NameLength = n.Length });
            }
            foreach (var p in m_Floats) Add(p.Key, 0, new Vector4(p.Value, 0, 0, 0), 0, default);
            foreach (var p in m_Ints) Add(p.Key, 3, new Vector4(p.Value, 0, 0, 0), 0, default);
            foreach (var p in m_Vectors) Add(p.Key, 1, p.Value, 0, default);
            foreach (var p in m_Textures)
                Add(p.Key, 2, default, TextureHandles.Of(p.Value.Texture), new Vector4(p.Value.Scale.x, p.Value.Scale.y, p.Value.Offset.x, p.Value.Offset.y));
            var pinned = new System.Runtime.InteropServices.GCHandle[names.Count];
            try
            {
                var array = props.ToArray();
                for (int i = 0; i < names.Count; i++)
                {
                    pinned[i] = System.Runtime.InteropServices.GCHandle.Alloc(names[i], System.Runtime.InteropServices.GCHandleType.Pinned);
                    array[i].Name = (byte*)pinned[i].AddrOfPinnedObject();
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
    public enum MaterialGlobalIlluminationFlags
    {
        None = 0,
        RealtimeEmissive = 1,
        BakedEmissive = 2,
        EmissiveIsBlack = 4,
        AnyEmissive = 3,
    }
}
