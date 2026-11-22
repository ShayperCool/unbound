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
    public partial class Material : Object
    {
        static Material s_Error;
        static bool s_ErrorSought;

        /// <summary>The material a renderer draws a slot without a material with: Unity's error
        /// shader (magenta, opaque). Null where that shader does not exist (a run without the
        /// renderer's shaders); it is looked for once, as every draw of such a slot asks.</summary>
        internal static Material Error
        {
            get
            {
                if (s_Error == null && !s_ErrorSought)
                {
                    s_ErrorSought = true;
                    var shader = Shader.Find("Hidden/InternalErrorShader");
                    if (shader != null) s_Error = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                return s_Error;
            }
        }

        /// <summary>Looks for the error shader again (tests that make one).</summary>
        internal static void ForgetError()
        {
            s_Error = null;
            s_ErrorSought = false;
        }

        internal sealed class TextureSlot
        {
            internal Texture Texture;
            /// <summary>The render texture's element the shader samples (SetTexture's element).</summary>
            internal RenderTextureSubElement Element;
            /// <summary>Set through the element overload: getters do not see it under a name the
            /// shader does not declare (measured: tests/projects/rtdepth api.material.*).</summary>
            internal bool ElementBinding;
            internal Vector2 Scale = Vector2.one;
            internal Vector2 Offset;
            /// <summary>The renderer has the destroyed-texture stand-in for it (Material.Sync).</summary>
            internal bool SentDestroyed;
        }

        /// <summary>
        /// The names a material's saved property sheet (<c>m_SavedProperties</c>) lists, by kind:
        /// what a SerializedObject over it shows and writes (matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/toolapis matx.*). A new material lists every property of its shader; a
        /// loaded one, or one a SerializedObject wrote, exactly the entries written, the shader's
        /// or not; a script's value for a name its shader does not declare is not listed. Reading
        /// or writing a property value through the Material API, or assigning a shader, lists every
        /// property of the shader again (the names already listed stay).
        /// </summary>
        internal sealed class SavedSheet
        {
            readonly HashSet<int>[] m_Lists = { null, new HashSet<int>(), new HashSet<int>(), new HashSet<int>(), new HashSet<int>() };
            /// <summary>The shader every property of which is listed (null: none since the sheet was written).</summary>
            internal Shader FilledFor;
            /// <summary>Held by more than one material (copies): changed only through a copy of it.</summary>
            internal bool Shared;

            /// <summary>The names listed of a kind (none for <see cref="SheetKind.None"/>).</summary>
            internal HashSet<int> List(SheetKind kind) => m_Lists[(int)kind];

            internal SavedSheet Clone()
            {
                var copy = new SavedSheet { FilledFor = FilledFor };
                for (int k = 1; k < m_Lists.Length; k++) AddAll(copy.m_Lists[k], m_Lists[k]);
                return copy;
            }

            /// <summary>Lists every property of <paramref name="shader"/>.</summary>
            internal void Fill(Shader shader)
            {
                FilledFor = shader;
                if (shader == null) return;
                foreach (var p in shader.m_Properties)
                    List(KindOf(p.Type))?.Add(Shader.PropertyToID(p.Name));
            }
        }

        /// <summary>The sheet's lists: a legacy Int property is a float, an Integer an int, a
        /// Vector a colour (measured).</summary>
        internal enum SheetKind { None, Textures, Ints, Floats, Colors }

        internal static SheetKind KindOf(ShaderPropertyType type) => type switch
        {
            ShaderPropertyType.Texture => SheetKind.Textures,
            ShaderPropertyType.Int => SheetKind.Ints,
            ShaderPropertyType.Float or ShaderPropertyType.Range => SheetKind.Floats,
            ShaderPropertyType.Color or ShaderPropertyType.Vector => SheetKind.Colors,
            _ => SheetKind.None,
        };

        /// <summary>The sheet, null while it is every property of the current shader and nothing else.</summary>
        SavedSheet m_Sheet { get => m_State.m_Sheet; set => m_State.m_Sheet = value; }

        /// <summary>A property value read or written through the Material API: the sheet lists every
        /// property of the shader again (nothing to do while it does).</summary>
        void TouchSheet()
        {
            ValidateOnAccess();
            var sheet = m_State.m_Sheet;
            if (sheet == null || sheet.FilledFor == m_Shader) return;
            if (sheet.Shared) m_Sheet = sheet = sheet.Clone();
            sheet.Fill(m_Shader);
        }

        /// <summary>The names the sheet lists of one kind (a new list, sorted by name as the Editor
        /// writes them: ordinal); of a variant, those it overrides (unmeasured).</summary>
        internal List<int> SheetNames(SheetKind kind)
        {
            var names = new List<int>();
            if (m_Sheet != null)
                names.AddRange(m_Sheet.List(kind));
            else if (m_Shader != null)
                foreach (var p in m_Shader.m_Properties)
                {
                    int id = Shader.PropertyToID(p.Name);
                    if (KindOf(p.Type) == kind && !names.Contains(id)) names.Add(id);
                }
            // A variant's sheet holds its overrides (as a loaded variant's file does).
            if (m_Parent != null && m_Own != null) names.RemoveAll(id => !m_Own.Contains(id));
            names.Sort((a, b) => string.CompareOrdinal(Shader.IdToName(a), Shader.IdToName(b)));
            return names;
        }

        /// <summary>The sheet's stored value of a float, int or colour entry: the material's own,
        /// else its shader's default.</summary>
        internal float SheetFloat(int id) =>
            m_Floats.TryGetValue(id, out var f) ? f : m_Shader != null && m_Shader.PropertyById(id) is ShaderPropertyInfo p ? p.DefaultValue.x : 0f;

        internal int SheetInt(int id) =>
            m_Ints.TryGetValue(id, out var i) ? i : m_Shader != null && m_Shader.PropertyById(id) is ShaderPropertyInfo p ? (int)p.DefaultValue.x : 0;

        internal Vector4 SheetColor(int id) =>
            m_Vectors.TryGetValue(id, out var v) ? v : m_Shader != null && m_Shader.PropertyById(id) is ShaderPropertyInfo p ? p.DefaultValue : Vector4.zero;

        internal (Texture Texture, Vector2 Scale, Vector2 Offset) SheetTexture(int id) =>
            m_Textures.TryGetValue(id, out var slot) ? (slot.Texture ? slot.Texture : null, slot.Scale, slot.Offset) : (null, Vector2.one, Vector2.zero);

        // Reload wrappers share one material's values and renderer state. Ordinary copies
        // construct a separate state through the regular constructors.
        sealed class MaterialState
        {
            internal Shader m_Shader;
            internal int m_CustomRenderQueue = -1;
            /// <summary>A variant's own m_CustomRenderQueue, as its data holds it while it takes its
            /// parent's queue (what its SerializedObject reads).</summary>
            internal int m_SerializedQueue = -1;
            internal readonly Dictionary<int, float> m_Floats = new Dictionary<int, float>();
            internal readonly Dictionary<int, int> m_Ints = new Dictionary<int, int>();
            internal readonly Dictionary<int, Vector4> m_Vectors = new Dictionary<int, Vector4>();
            internal readonly HashSet<int> m_RawBindings = new HashSet<int>();
            /// <summary>Vectors set after a texture of the same name's `_HDR` (they win over the
            /// texture's decode values until the texture is set again).</summary>
            internal readonly HashSet<int> m_VectorsAfterTexture = new HashSet<int>();
            internal readonly Dictionary<int, Matrix4x4> m_Matrices = new Dictionary<int, Matrix4x4>();
            internal readonly Dictionary<int, TextureSlot> m_Textures = new Dictionary<int, TextureSlot>();
            /// <summary>Slots a copy dropped (ClearForCopyOf), for the next slot this material needs.</summary>
            internal List<TextureSlot> m_SpareSlots;
            internal readonly Dictionary<int, object> m_Buffers = new Dictionary<int, object>();
            internal readonly Dictionary<int, object> m_Arrays = new Dictionary<int, object>();
            internal readonly HashSet<string> m_Keywords = new HashSet<string>();
            internal readonly List<string> m_KeywordExtras = new List<string>();
            internal readonly HashSet<int> m_ScriptSet = new HashSet<int>();
            internal readonly HashSet<int> m_TextureNames = new HashSet<int>();
            internal readonly Dictionary<string, string> m_Tags = new Dictionary<string, string>();
            internal ulong m_Handle;
            internal bool m_Dirty = true;
            internal bool m_EnableInstancing;
            internal MaterialGlobalIlluminationFlags m_GIFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            internal int m_Version;
            internal bool m_DoubleSidedGI;
            internal Material m_Parent;
            internal HashSet<int> m_Own;
            internal HashSet<int> m_Locked;
            internal int m_InheritedVersion = -1;
            internal bool m_Inheriting;
            internal HashSet<string> m_DisabledPasses;
            internal int m_LodEpoch;
            internal int m_DestroyedTextureEpoch;
            internal string m_SentShader;
            internal bool m_InheritQueue;
            internal bool m_InheritFlags;
            internal bool m_InheritKeywords;
            internal SavedSheet m_Sheet;
        }
        readonly MaterialState m_State = new MaterialState();

        internal Material ReloadWrapper() => (Material)CreateReloadWrapper();

        Shader m_Shader { get => m_State.m_Shader; set => m_State.m_Shader = value; }
        internal int m_CustomRenderQueue { get => m_State.m_CustomRenderQueue; set => m_State.m_CustomRenderQueue = value; }
        int m_SerializedQueue { get => m_State.m_SerializedQueue; set => m_State.m_SerializedQueue = value; }

        /// <summary>The queue the material's data holds: a variant that takes its parent's queue
        /// keeps its own value, which a SerializedObject reads and applies without overriding the
        /// parent's (Unity 6000.0.71f1, tests/projects/gameinterop mat.variant.*).</summary>
        internal int SerializedRenderQueue => m_Parent != null && m_InheritQueue ? m_SerializedQueue : m_CustomRenderQueue;
        internal Dictionary<int, float> m_Floats { get => m_State.m_Floats; }
        internal Dictionary<int, int> m_Ints { get => m_State.m_Ints; }
        internal Dictionary<int, Vector4> m_Vectors { get => m_State.m_Vectors; }
        // Synthetic renderer copies contain property-block GPU values, already in working space.
        internal HashSet<int> m_RawBindings { get => m_State.m_RawBindings; }
        HashSet<int> m_VectorsAfterTexture { get => m_State.m_VectorsAfterTexture; }
        internal Dictionary<int, Matrix4x4> m_Matrices { get => m_State.m_Matrices; }
        internal Dictionary<int, TextureSlot> m_Textures { get => m_State.m_Textures; }
        /// <summary>Buffers by property (ComputeBuffer or GraphicsBuffer): structured buffers, and
        /// constant buffers by their cbuffer's name.</summary>
        internal Dictionary<int, object> m_Buffers { get => m_State.m_Buffers; }
        /// <summary>Array properties: float[], Vector4[] (colors too) or Matrix4x4[].</summary>
        internal Dictionary<int, object> m_Arrays { get => m_State.m_Arrays; }
        internal HashSet<string> m_Keywords { get => m_State.m_Keywords; }
        /// <summary>Keyword names given again, and empty ones: the material keeps them as given
        /// (canonical mat.set.shaderKeywords lists an undeclared name as often as it was set). They
        /// are not drawn with: the renderer gets <see cref="m_Keywords"/> as before.</summary>
        internal List<string> m_KeywordExtras { get => m_State.m_KeywordExtras; }
        /// <summary>Names set from script: the material has them whatever its shader declares.</summary>
        internal HashSet<int> m_ScriptSet { get => m_State.m_ScriptSet; }
        /// <summary>Texture names the material has a slot for: those of the shaders it had, those
        /// saved with it (the values of other saved names are kept for a later shader, unseen).</summary>
        internal HashSet<int> m_TextureNames { get => m_State.m_TextureNames; }
        internal Dictionary<string, string> m_Tags { get => m_State.m_Tags; }
        internal ulong m_Handle { get => m_State.m_Handle; set => m_State.m_Handle = value; }
        internal bool m_Dirty { get => m_State.m_Dirty; set => m_State.m_Dirty = value; }
        bool m_EnableInstancing { get => m_State.m_EnableInstancing; set => m_State.m_EnableInstancing = value; }
        MaterialGlobalIlluminationFlags m_GIFlags { get => m_State.m_GIFlags; set => m_State.m_GIFlags = value; }

        static readonly int s_Color = Shader.PropertyToID("_Color");
        static readonly int s_MainTex = Shader.PropertyToID("_MainTex");

        internal Material() { }

        public Material(Shader shader)
        {
            MainThreadCheck.Check("CreateWithShader");
            if (shader == null) throw new ArgumentNullException(nameof(shader), "Creating a material requires a shader.");
            m_Name = shader.name;
            StartWith(shader);
        }

        /// <summary>The shader a new material starts with: its default textures and its keyword
        /// drawers' keywords, and no earlier shader's defaults (see <see cref="KeepDefaultsOf"/>).
        /// Also how a model importer that picks the shader itself makes its material (measured:
        /// the legacy importer's URP Lit materials keep Lit's own _Parallax, tests/projects/modelimport).</summary>
        internal void StartWith(Shader shader)
        {
            m_Shader = shader;
            m_TextureNames.Clear();
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

        /// <summary>A copy; of a variant, a variant of the same parent with the same overrides and
        /// locks (canonical variant.instance and variant.newFrom). It has every setting of the source,
        /// its GI flags and double-sided GI too ("copying all properties", 6000.0).</summary>
        public Material(Material source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            source.Inherit();
            CopyFrom(source);
            m_DoubleSidedGI = source.m_DoubleSidedGI;
            m_Name = source.m_Name;
            m_Parent = source.m_Parent;
            m_Own = source.m_Own != null ? new HashSet<int>(source.m_Own) : null;
            m_Locked = source.m_Locked != null ? new HashSet<int>(source.m_Locked) : null;
            m_InheritQueue = source.m_InheritQueue;
            m_InheritFlags = source.m_InheritFlags;
            m_InheritKeywords = source.m_InheritKeywords;
            // The copy already has what the source inherited.
            m_InheritedVersion = source.m_InheritedVersion;
        }

        [global::System.@ObsoleteAttribute("Creating materials from shader source string is no longer supported. Use Shader assets instead.", false)] [global::System.ComponentModel.@EditorBrowsableAttribute((global::System.ComponentModel.EditorBrowsableState)1)]
        public Material(string contents) : this(Shader.Find("Unlit/Color")) { }

        void CopyFrom(Material source)
        {
            m_Shader = source.m_Shader;
            // Copies share the sheet until one of them changes it.
            m_Sheet = source.m_Sheet;
            if (m_Sheet != null) m_Sheet.Shared = true;
            m_CustomRenderQueue = source.m_CustomRenderQueue;
            m_SerializedQueue = source.m_SerializedQueue;
            foreach (var p in source.m_Floats) m_Floats[p.Key] = p.Value;
            foreach (var p in source.m_Ints) m_Ints[p.Key] = p.Value;
            foreach (var p in source.m_Vectors) m_Vectors[p.Key] = p.Value;
            AddAll(m_VectorsAfterTexture, source.m_VectorsAfterTexture);
            foreach (var p in source.m_Matrices) m_Matrices[p.Key] = p.Value;
            foreach (var p in source.m_Textures)
            {
                // A slot this material already has is reused (ResetToCopyOf).
                if (!m_Textures.TryGetValue(p.Key, out var slot)) m_Textures[p.Key] = slot = NewSlot();
                slot.Texture = p.Value.Texture;
                slot.Element = p.Value.Element;
                slot.ElementBinding = p.Value.ElementBinding;
                slot.Scale = p.Value.Scale;
                slot.Offset = p.Value.Offset;
            }
            foreach (var p in source.m_Buffers) m_Buffers[p.Key] = p.Value;
            foreach (var p in source.m_Arrays)
            {
                // An array this material already has of the same type and length is reused (ResetToCopyOf).
                var from = (Array)p.Value;
                if (m_Arrays.TryGetValue(p.Key, out var mine) && mine is Array to && to.GetType() == from.GetType() && to.Length == from.Length)
                    Array.Copy(from, to, from.Length);
                else
                    m_Arrays[p.Key] = from.Clone();
            }
            foreach (var k in source.m_Keywords) m_Keywords.Add(k);
            m_KeywordExtras.AddRange(source.m_KeywordExtras);
            if (source.m_DisabledPasses == null) m_DisabledPasses = null;
            else if (m_DisabledPasses == null) m_DisabledPasses = new HashSet<string>(source.m_DisabledPasses, StringComparer.OrdinalIgnoreCase);
            else
            {
                m_DisabledPasses.Clear();
                AddAll(m_DisabledPasses, source.m_DisabledPasses);
            }
            AddAll(m_ScriptSet, source.m_ScriptSet);
            AddAll(m_RawBindings, source.m_RawBindings);
            AddAll(m_TextureNames, source.m_TextureNames);
            foreach (var t in source.m_Tags) m_Tags[t.Key] = t.Value;
            m_EnableInstancing = source.m_EnableInstancing;
            m_GIFlags = source.m_GIFlags;
            MarkChanged();
        }

        /// <summary>HashSet.UnionWith without its boxed enumerator (copies made per change of a
        /// drawn material stay free of allocations).</summary>
        static void AddAll<T>(HashSet<T> into, HashSet<T> from)
        {
            foreach (var item in from) into.Add(item);
        }

        internal Object CloneObject() => new Material(this);

        /// <summary>A change made through the API: also an Editor change of a material asset, which
        /// the Editor then saves (AssetDatabase.SaveAssets, its exit; measured in
        /// tests/projects/scenesave: a property set by script dirties the asset in Edit and Play mode).</summary>
        internal void MarkDirty()
        {
            MarkChanged();
            if (IsPersistentAsset) UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>A change of the material's state that is no edit (reading it, inheriting from its parent).</summary>
        internal void MarkChanged()
        {
            m_Dirty = true;
            m_Version++;
            ObjectDispatch.Changed(this);
        }

        /// <summary>Counts changes, for copies made from this material (property blocks).</summary>
        internal int m_Version { get => m_State.m_Version; set => m_State.m_Version = value; }

        public Shader shader
        {
            get { MainThreadCheck.Check("get_shader"); return m_Shader; }
            set
            { MainThreadCheck.Check("set_shader");
                if (value != m_Shader)
                {
                    KeepDefaultsOf(m_Shader);
                    ResetRenderQueue(value);
                    // The names set from script stop counting: the material then has the new
                    // shader's names, as it has for saved values (measured:
                    // tests/projects/materials scripted.*: a value set for a name the new shader
                    // lacks is unseen, also back under a shader without it, and seen with its
                    // value under one declaring it).
                    m_ScriptSet.Clear();
                    // The sheet keeps the properties of the shader it leaves (measured).
                    if (m_Sheet == null && m_Shader != null)
                    {
                        m_Sheet = new SavedSheet();
                        m_Sheet.Fill(m_Shader);
                    }
                }
                bool changed = value != m_Shader;
                m_Shader = value;
                TouchSheet();
                AddTextureNames();
                MarkDirty();
                if (changed && m_ValidateOnShaderChange) Unbound.Runtime.MaterialValidation.Validate(this, editorsOfTheEditor: false);
            }
        }

        /// <summary>Set while a model's import makes the material: the Editor validates it with the
        /// new shader's editor as its shader is set (<see cref="Unbound.Runtime.ModelMaterialImport"/>).</summary>
        internal bool m_ValidateOnShaderChange;

        /// <summary>Set while a project's OnPostprocessMaterial has a model's material: the first
        /// access of a property value through the Material API validates the material with its
        /// shader's editor first; reading its shader, GI flags or keywords does not (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/modelimport URPHookRecord: the legacy
        /// importer's transparent URP materials end opaque). Measured for HasProperty, GetFloat
        /// and GetColor; writes, setting the shader and the typed Has* queries are not
        /// (writes and a shader change take the same path here, the Has* queries do not).</summary>
        internal bool m_ValidateOnPropertyAccess;

        void ValidateOnAccess()
        {
            if (!m_ValidateOnPropertyAccess) return;
            m_ValidateOnPropertyAccess = false;
            Unbound.Runtime.MaterialValidation.Validate(this, editorsOfTheEditor: false);
        }

        /// <summary>
        /// Documented (Material.renderQueue, 6000.0): "if a shader on the material is changed, the
        /// render queue resets to that of the shader itself": the override goes, the queue is then
        /// the new shader's (rawRenderQueue -1). The same page's limitation: in batch mode an SRP
        /// loads only when something first renders, which can change the selected SubShader and so
        /// the shader's queue read afterwards (Shader.renderQueue answers that; not decided here).
        /// Assigning the shader it has is no change and keeps it. Not documented, so the override
        /// stays, with a warning: a change to or from no shader, and a variant that takes its
        /// parent's queue (the parent's state, taken again later).
        /// </summary>
        void ResetRenderQueue(Shader next)
        {
            if (m_Parent != null && m_InheritQueue)
            {
                if (Inherited().m_CustomRenderQueue >= 0)
                    Unbound.Runtime.Stubs.Ignored("Material.shader of a variant that takes its parent's render queue",
                        "keeps the parent's queue: a shader change is not measured on variants");
                return;
            }
            if (m_CustomRenderQueue < 0) return;
            if (m_Shader == null || next == null)
            {
                Unbound.Runtime.Stubs.Ignored("Material.shader set to or from no shader",
                    "keeps the material's render queue: the documented reset is to the shader's queue");
                return;
            }
            m_CustomRenderQueue = -1;
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
            get { MainThreadCheck.Check("GetFirstPropertyNameIdByAttribute"); return GetColor(MainColor); }
            set { MainThreadCheck.Check("GetFirstPropertyNameIdByAttribute"); SetColor(MainColor, value); }
        }

        public Texture mainTexture
        {
            get { MainThreadCheck.Check("GetFirstPropertyNameIdByAttribute"); return GetTexture(MainTex); }
            set { MainThreadCheck.Check("GetFirstPropertyNameIdByAttribute"); SetTexture(MainTex, value); }
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
            get { MainThreadCheck.Check("get_renderQueue"); return Inherited().m_CustomRenderQueue >= 0 ? m_CustomRenderQueue : m_Shader != null ? m_Shader.renderQueue : 2000; }
            set
            { MainThreadCheck.Check("set_renderQueue");
                m_CustomRenderQueue = value;
                MarkDirty();
            }
        }

        public int rawRenderQueue => m_CustomRenderQueue;

        /// <summary>The keywords its shader declares that are on (sorted), then the names it does
        /// not declare (sorted, as often and as empty as given; canonical mat.*.shaderKeywords and
        /// saved.*). Changing the shader sorts them out again by the new shader's space. While the
        /// space is not known: every name once, sorted, as before.</summary>
        public string[] shaderKeywords
        {
            get
            { MainThreadCheck.Check("GetShaderKeywords");
                var (valid, invalid) = SerializedKeywords();
                valid.AddRange(invalid);
                return valid.ToArray();
            }
            set
            { MainThreadCheck.Check("SetShaderKeywords");
                OwnKeywords();
                m_Keywords.Clear();
                m_KeywordExtras.Clear();
                if (value != null)
                    foreach (var k in value)
                        if (k != null && (k.Length == 0 || !m_Keywords.Add(k))) m_KeywordExtras.Add(k);
                MarkDirty();
            }
        }

        /// <summary>The keywords as the material stores them (m_ValidKeywords, m_InvalidKeywords):
        /// those its shader declares that are on, and the others, each sorted.</summary>
        internal (List<string> Valid, List<string> Invalid) SerializedKeywords()
        {
            Inherit();
            var declarations = KeywordDeclarations;
            var valid = new List<string>();
            var invalid = new List<string>();
            foreach (var k in m_Keywords)
                if (k.Length > 0) (declarations == null || declarations.IndexOf(k) >= 0 ? valid : invalid).Add(k);
            if (declarations != null)
                foreach (var k in m_KeywordExtras) if (declarations.IndexOf(k) < 0) invalid.Add(k);
            valid.Sort(StringComparer.Ordinal);
            invalid.Sort(StringComparer.Ordinal);
            return (valid, invalid);
        }

        /// <summary>The disabled passes as stored: upper case (measured).</summary>
        internal IEnumerable<string> DisabledPassNames()
        {
            if (m_DisabledPasses == null) yield break;
            foreach (var pass in m_DisabledPasses) yield return pass.ToUpperInvariant();
        }

        /// <summary>The names of the properties locked for variants.</summary>
        internal IEnumerable<string> LockedPropertyNames()
        {
            if (m_Locked == null) yield break;
            foreach (var id in m_Locked) yield return Shader.IdToName(id);
        }

        /// <summary>The shader's keyword space when it is known (null: not known, every name then
        /// counts as declared, as before declarations were read).</summary>
        internal Unbound.Runtime.ShaderDeclarations KeywordDeclarations
        {
            get
            {
                var d = m_Shader != null ? Unbound.Runtime.ShaderDeclarations.Of(m_Shader) : null;
                return d != null && d.KeywordsKnown ? d : null;
            }
        }

        /// <summary>A keyword set from script: a variant then has its own keywords (it took its
        /// parent's until now) instead of taking the parent's again later.</summary>
        void OwnKeywords()
        {
            if (m_Parent == null) return;
            Inherit();
            m_InheritKeywords = false;
        }

        // A changed flag is a change of the material: its variants and the copies made from it
        // (canvas clipping, property blocks, replacement shaders) take it again.
        public MaterialGlobalIlluminationFlags globalIlluminationFlags
        {
            get { Inherit(); return m_GIFlags; }
            set
            {
                if (m_GIFlags == value) return;
                m_GIFlags = value;
                MarkDirty();
            }
        }

        public bool doubleSidedGI
        {
            get { Inherit(); return m_DoubleSidedGI; }
            set
            {
                if (m_DoubleSidedGI == value) return;
                m_DoubleSidedGI = value;
                MarkDirty();
            }
        }
        bool m_DoubleSidedGI { get => m_State.m_DoubleSidedGI; set => m_State.m_DoubleSidedGI = value; }

        public bool enableInstancing
        {
            get { MainThreadCheck.Check("get_enableInstancing"); Inherit(); return m_EnableInstancing; }
            set
            { MainThreadCheck.Check("set_enableInstancing");
                if (m_EnableInstancing == value) return;
                m_EnableInstancing = value;
                MarkDirty();
            }
        }

        public int passCount { get { MainThreadCheck.Check("get_passCount"); return m_Shader != null ? m_Shader.passCount : 0; } }

        /// <summary>Whether the material is a variant of another (it has a parent).</summary>
        public bool isVariant => m_Parent != null;

        /// <summary>The material this one is a variant of. A variant has the parent's value for
        /// every property it does not override, also when the parent changes later (measured:
        /// tests/projects/builtinfx material.variant.*). A variant loaded from its asset overrides
        /// the properties it saves; one made a variant from script those whose values differ from
        /// the parent's then (canonical tests/projects/shaderkeywords variant.script); either
        /// overrides the properties set from script afterwards. A script-made variant keeps its own
        /// queue, keywords and flags (builtinfx material.made.*).</summary>
        public Material parent
        {
            get => m_Parent;
            set
            {
                m_Parent = value == this ? null : value;
                m_Own = m_Parent != null ? Differences(m_Parent) : null;
                m_InheritQueue = m_InheritFlags = m_InheritKeywords = false;
                m_InheritedVersion = -1;
            }
        }

        /// <summary>The properties whose values differ from another material's (a value a material
        /// does not hold is its shader's default).</summary>
        HashSet<int> Differences(Material other)
        {
            other.Inherit();
            var differ = new HashSet<int>();
            void Compare<T>(Dictionary<int, T> mine, Dictionary<int, T> theirs, Func<Material, int, T> fallback)
            {
                var ids = new HashSet<int>(mine.Keys);
                ids.UnionWith(theirs.Keys);
                foreach (var id in ids)
                {
                    var a = mine.TryGetValue(id, out var x) ? x : fallback(this, id);
                    var b = theirs.TryGetValue(id, out var y) ? y : fallback(other, id);
                    if (!EqualityComparer<T>.Default.Equals(a, b)) differ.Add(id);
                }
            }
            Vector4 Default(Material m, int id) => m.m_Shader?.PropertyById(id)?.DefaultValue ?? Vector4.zero;
            Compare(m_Floats, other.m_Floats, (m, id) => Default(m, id).x);
            Compare(m_Ints, other.m_Ints, (m, id) => (int)Default(m, id).x);
            Compare(m_Vectors, other.m_Vectors, Default);
            Compare(m_Matrices, other.m_Matrices, (m, id) => Matrix4x4.zero);
            var textures = new HashSet<int>(m_Textures.Keys);
            textures.UnionWith(other.m_Textures.Keys);
            foreach (var id in textures)
            {
                m_Textures.TryGetValue(id, out var a);
                other.m_Textures.TryGetValue(id, out var b);
                if ((a?.Texture) != (b?.Texture) || (a?.Scale ?? Vector2.one) != (b?.Scale ?? Vector2.one)
                    || (a?.Offset ?? Vector2.zero) != (b?.Offset ?? Vector2.zero))
                    differ.Add(id);
            }
            return differ;
        }

        Material m_Parent { get => m_State.m_Parent; set => m_State.m_Parent = value; }
        /// <summary>The properties a variant overrides (null when it is not one).</summary>
        HashSet<int> m_Own { get => m_State.m_Own; set => m_State.m_Own = value; }
        /// <summary>The properties it locks for its variants (m_LockedProperties).</summary>
        HashSet<int> m_Locked { get => m_State.m_Locked; set => m_State.m_Locked = value; }
        bool m_InheritQueue { get => m_State.m_InheritQueue; set => m_State.m_InheritQueue = value; }
        bool m_InheritFlags { get => m_State.m_InheritFlags; set => m_State.m_InheritFlags = value; }
        bool m_InheritKeywords { get => m_State.m_InheritKeywords; set => m_State.m_InheritKeywords = value; }
        int m_InheritedVersion { get => m_State.m_InheritedVersion; set => m_State.m_InheritedVersion = value; }
        bool m_Inheriting { get => m_State.m_Inheriting; set => m_State.m_Inheriting = value; }

        /// <summary>A property set from script; a variant then overrides it.</summary>
        void ScriptSet(int nameID)
        {
            m_ScriptSet.Add(nameID);
            if (m_Parent != null) m_Own?.Add(nameID);
        }

        // Cyclic variant chains are unsupported. Fail explicitly instead of hanging an API call.
        IEnumerable<Material> Ancestors()
        {
            var seen = new HashSet<Material> { this };
            for (var p = m_Parent; p != null; p = p.m_Parent)
            {
                if (!seen.Add(p)) throw new InvalidOperationException("Material parent chain contains a cycle.");
                yield return p;
            }
        }

        public bool IsChildOf(Material ancestor)
        {
            if (ancestor == null) return false;
            foreach (var p in Ancestors())
                if (p == ancestor) return true;
            return false;
        }

        /// <summary>Whether the variant overrides the property; not when an ancestor locks it
        /// (canonical variant.grand.IsPropertyOverriden(_Color), saved but locked by its parent).</summary>
        public bool IsPropertyOverriden(int nameID) =>
            m_Parent != null && m_Own != null && m_Own.Contains(nameID) && !IsPropertyLockedByAncestor(nameID);

        public bool IsPropertyOverriden(string name) => IsPropertyOverriden(Shader.PropertyToID(name));

        public bool IsPropertyLocked(int nameID) => m_Locked != null && m_Locked.Contains(nameID);

        public bool IsPropertyLocked(string name) => IsPropertyLocked(Shader.PropertyToID(name));

        /// <summary>Whether a parent, or its parent, locks the property.</summary>
        public bool IsPropertyLockedByAncestor(int nameID)
        {
            foreach (var p in Ancestors())
                if (p.IsPropertyLocked(nameID)) return true;
            return false;
        }

        public bool IsPropertyLockedByAncestor(string name) => IsPropertyLockedByAncestor(Shader.PropertyToID(name));

        /// <summary>Locks or unlocks a property for its variants. A lock does not undo a value a
        /// variant has (canonical variant.revert.set: a set of a property an ancestor locks takes
        /// effect on the variant).</summary>
        public void SetPropertyLock(int nameID, bool value)
        {
            if (value) (m_Locked ??= new HashSet<int>()).Add(nameID);
            else m_Locked?.Remove(nameID);
        }

        public void SetPropertyLock(string name, bool value) => SetPropertyLock(Shader.PropertyToID(name), value);

        /// <summary>Takes the parent's value for the property again (canonical variant.revert.value:
        /// the render queue stays).</summary>
        public void RevertPropertyOverride(int nameID)
        {
            if (m_Parent == null || m_Own == null || !m_Own.Remove(nameID)) return;
            m_ScriptSet.Remove(nameID);
            m_InheritedVersion = -1;
            Inherit();
        }

        public void RevertPropertyOverride(string name) => RevertPropertyOverride(Shader.PropertyToID(name));

        /// <summary>Takes the parent's values, render queue, keywords and flags again (canonical
        /// variant.revert.all: the queue too).</summary>
        public void RevertAllPropertyOverrides()
        {
            if (m_Parent == null || m_Own == null) return;
            foreach (var id in m_Own) m_ScriptSet.Remove(id);
            m_Own.Clear();
            m_InheritQueue = m_InheritFlags = m_InheritKeywords = true;
            m_InheritedVersion = -1;
            Inherit();
        }

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
                bool Own(int id) => m_Own.Contains(id);
                foreach (var p in parent.m_Floats) if (!Own(p.Key)) m_Floats[p.Key] = p.Value;
                foreach (var p in parent.m_Ints) if (!Own(p.Key)) m_Ints[p.Key] = p.Value;
                foreach (var p in parent.m_Vectors) if (!Own(p.Key)) m_Vectors[p.Key] = p.Value;
                foreach (var p in parent.m_Matrices) if (!Own(p.Key)) m_Matrices[p.Key] = p.Value;
                foreach (var p in parent.m_Textures)
                    if (!Own(p.Key)) m_Textures[p.Key] = new TextureSlot { Texture = p.Value.Texture, Element = p.Value.Element, ElementBinding = p.Value.ElementBinding, Scale = p.Value.Scale, Offset = p.Value.Offset };
                m_TextureNames.UnionWith(parent.m_TextureNames);
                if (m_InheritKeywords)
                {
                    m_Keywords.Clear();
                    m_Keywords.UnionWith(parent.m_Keywords);
                    m_KeywordExtras.Clear();
                    m_KeywordExtras.AddRange(parent.m_KeywordExtras);
                }
                if (m_InheritQueue) m_CustomRenderQueue = parent.m_CustomRenderQueue;
                if (m_InheritFlags)
                {
                    m_EnableInstancing = parent.m_EnableInstancing;
                    m_DoubleSidedGI = parent.m_DoubleSidedGI;
                    m_GIFlags = parent.m_GIFlags;
                }
                MarkChanged();
            }
            finally { m_Inheriting = false; }
        }

        // ------------------------------------------------------------------ keywords

        public void EnableKeyword(string keyword)
        {
            MainThreadCheck.Check("EnableKeyword");
            OwnKeywords();
            if (m_Keywords.Add(keyword)) MarkDirty();
        }

        public void DisableKeyword(string keyword)
        {
            OwnKeywords();
            if (m_Keywords.Remove(keyword)) MarkDirty();
        }

        /// <summary>Whether a keyword its shader declares is on: a name a known space does not
        /// declare is not, even when set (canonical mat.IsKeywordEnabled(NOT_DECLARED),
        /// saved.IsKeywordEnabled(STALE_KW)).</summary>
        public bool IsKeywordEnabled(string keyword)
        {
            if (!Inherited().m_Keywords.Contains(keyword)) return false;
            var declarations = KeywordDeclarations;
            return declarations == null || declarations.IndexOf(keyword) >= 0;
        }

        Material Inherited()
        {
            Inherit();
            return this;
        }

        /// <summary>The LightMode tags of passes disabled on this material, compared
        /// case-insensitively (measured: tests/projects/builtinrp passes.*): the renderer skips
        /// them (built-in forward, shadow caster and depth passes, renderer lists).</summary>
        HashSet<string> m_DisabledPasses { get => m_State.m_DisabledPasses; set => m_State.m_DisabledPasses = value; }

        internal bool HasDisabledPasses => m_DisabledPasses != null && m_DisabledPasses.Count > 0;

        /// <summary>Whether passes with this LightMode are disabled on the material.</summary>
        internal bool PassDisabled(string lightMode) => m_DisabledPasses != null && lightMode != null && m_DisabledPasses.Contains(lightMode);

        public void SetShaderPassEnabled(string passName, bool enabled)
        {
            if (passName == null) return;
            bool changed = enabled
                ? m_DisabledPasses != null && m_DisabledPasses.Remove(passName)
                : (m_DisabledPasses ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(passName);
            // GPU-driven rendering keeps whether the motion vectors pass is enabled.
            if (changed) ObjectDispatch.Changed(this);
        }

        public bool GetShaderPassEnabled(string passName) => passName == null || m_DisabledPasses == null || !m_DisabledPasses.Contains(passName);

        /// <summary>
        /// The pass names of a shader whose LOD limit leaves no SubShader in reach
        /// (<see cref="Shader.IsLodHidden"/>: the renderer draws none, its pass list is the
        /// error shader's): those of the SubShader <see cref="passCount"/> counts
        /// (<see cref="Shader.ActivePassNames"/>; canonical tests/projects/shaderpasscount
        /// case.global25.*.mat.findPass: the first SubShader's passes are found). Null while the
        /// renderer draws a SubShader: pass indices are then the renderer's, including slots
        /// for generated surface passes whose programs are not supported yet.
        /// </summary>
        List<string> HiddenPassNames(string api) =>
            m_Shader != null && m_Shader.IsLodHidden ? m_Shader.ActivePassNames(api) : null;

        /// <summary>A pass's name; empty out of range.</summary>
        public string GetPassName(int pass)
        {
            if (HiddenPassNames("Material.GetPassName") is { } names)
                return pass >= 0 && pass < names.Count ? names[pass] : "";
            var info = Unbound.Runtime.ShaderPassInfo.Of(m_Shader);
            return info != null && pass >= 0 && pass < info.Passes.Count ? info.Passes[pass].Name : "";
        }

        /// <summary>The first pass of that name, compared case-insensitively as Unity does; -1
        /// when there is none.</summary>
        public int FindPass(string passName)
        {
            if (string.IsNullOrEmpty(passName)) return -1;
            if (HiddenPassNames("Material.FindPass") is { } names)
            {
                for (int i = 0; i < names.Count; i++)
                    if (string.Equals(names[i], passName, StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }
            var info = Unbound.Runtime.ShaderPassInfo.Of(m_Shader);
            if (info == null) return -1;
            for (int i = 0; i < info.Passes.Count; i++)
                if (string.Equals(info.Passes[i].Name, passName, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public void SetOverrideTag(string tag, string val)
        {
            if (string.IsNullOrEmpty(val)) m_Tags.Remove(tag);
            else m_Tags[tag] = val;
        }

        /// <summary>
        /// A tag: the material's override, else its shader's. Without fallbacks, the tag of the
        /// SubShader the shader's queries answer from (<see cref="Shader.ActiveSubShader"/>: also
        /// a Fallback's, the first SubShader when none is in reach). With them, the first
        /// SubShader carrying the tag among the shader's own then its Fallback chain's, in
        /// order and whatever its LOD or RenderPipeline tag (canonical
        /// tests/projects/shaderpasscount case.*.mat.subTag / mat.subTagFallbacks: SubShader 0's
        /// tag under every limit, a pipeline-tagged one included; tests/projects/graphicsgaps
        /// select.*.tag / tagFallbacks under URP and other pipeline tags). The renderer's drawn
        /// SubShader while the declarations or, without fallbacks, the selection are unknown.
        /// </summary>
        public string GetTag(string tag, bool searchFallbacks, string defaultValue)
        {
            if (m_Tags.TryGetValue(tag, out var value)) return value;
            if (m_Shader != null && tag != null)
            {
                if (!searchFallbacks)
                {
                    if (m_Shader.ActiveSubShader("Material.GetTag", "follows the renderer's SubShader") is { } active)
                        return Unbound.Runtime.ShaderDeclarations.Tag(active.Tags, tag) ?? defaultValue;
                }
                else
                {
                    var d = Unbound.Runtime.ShaderDeclarations.Of(m_Shader);
                    if (d.SubShadersKnown && d.SubShaders.Count > 0)
                    {
                        foreach (var sub in d.SubShaders)
                            if (Unbound.Runtime.ShaderDeclarations.Tag(sub.Tags, tag) is { } found) return found;
                        return defaultValue;
                    }
                }
            }
            var info = Unbound.Runtime.ShaderPassInfo.Of(m_Shader);
            if (info != null)
            {
                if ((searchFallbacks || info.DrawnShader == m_Shader.SourceName) && info.Tags.TryGetValue(tag, out var shaderValue)) return shaderValue;
                return defaultValue;
            }
            return tag == "RenderType" ? (renderQueue >= 2500 ? "Transparent" : "Opaque") : defaultValue;
        }

        public string GetTag(string tag, bool searchFallbacks) => GetTag(tag, searchFallbacks, "");

        /// <summary>A tag of the SubShader the renderer draws (an override tag first), null
        /// without it: what renderer lists match their per-tag state blocks by, read without the
        /// public query's bookkeeping.</summary>
        internal string DrawnSubShaderTag(string tag)
        {
            if (m_Tags.TryGetValue(tag, out var value)) return value;
            var info = m_Shader != null ? Unbound.Runtime.ShaderPassInfo.Of(m_Shader) : null;
            return info != null && info.DrawnShader == m_Shader.SourceName && info.Tags.TryGetValue(tag, out var drawn) ? drawn : null;
        }

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
        public bool HasProperty(int nameID)
        {
            MainThreadCheck.Check("HasProperty");
            ValidateOnAccess();
            return Declared(nameID) != null || m_ScriptSet.Contains(nameID) || ScaleOffsetOf(nameID, out _);
        }

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
            MainThreadCheck.Check("SetFloatImpl");
            TouchSheet();
            m_RawBindings.Remove(nameID);
            m_Floats[nameID] = value;
            ScriptSet(nameID);
            MarkDirty();
        }

        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));

        public float GetFloat(int nameID)
        {
            MainThreadCheck.Check("GetFloatImpl");
            TouchSheet();
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
            TouchSheet();
            m_Ints[nameID] = value;
            ScriptSet(nameID);
            MarkDirty();
        }

        public int GetInteger(string name) => GetInteger(Shader.PropertyToID(name));
        public int GetInteger(int nameID)
        {
            TouchSheet();
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
            TouchSheet();
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
            m_VectorsAfterTexture.Add(nameID);
            ScriptSet(nameID);
            MarkDirty();
        }

        public Vector4 GetVector(string name) => GetVector(Shader.PropertyToID(name));

        public Vector4 GetVector(int nameID)
        {
            TouchSheet();
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
            TouchSheet();
            m_Matrices[nameID] = value;
            ScriptSet(nameID);
            MarkDirty();
        }

        public Matrix4x4 GetMatrix(string name) => GetMatrix(Shader.PropertyToID(name));

        public Matrix4x4 GetMatrix(int nameID)
        {
            TouchSheet();
            if (!IsMatrix(nameID)) { Missing(nameID, "a matrix"); return Matrix4x4.identity; }
            return m_Matrices.TryGetValue(nameID, out var m) ? m : Matrix4x4.identity;
        }

        public void SetTexture(string name, Texture value) => SetTexture(Shader.PropertyToID(name), value);

        public void SetTexture(int nameID, Texture value) => SetTextureElement(nameID, value, RenderTextureSubElement.Default, false);

        public void SetTexture(string name, RenderTexture value, RenderTextureSubElement element) => SetTexture(Shader.PropertyToID(name), value, element);
        public void SetTexture(int nameID, RenderTexture value, RenderTextureSubElement element) => SetTextureElement(nameID, value, element, true);

        /// <summary>A texture and the element of it the shader samples (Unbound.Runtime.TextureElements);
        /// GetTexture returns the texture whatever the element.</summary>
        void SetTextureElement(int nameID, Texture value, RenderTextureSubElement element, bool elementBinding)
        {
            TouchSheet();
            var slot = Slot(nameID);
            slot.Texture = value;
            slot.Element = element;
            slot.ElementBinding = elementBinding;
            m_VectorsAfterTexture.Remove(HdrDecode.IdOf(nameID));
            ScriptSet(nameID);
            MarkDirty();
        }

        public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
        public Texture GetTexture(int nameID)
        {
            TouchSheet();
            Inherit();
            if (!HasTextureSlot(nameID)) { Missing(nameID, "a texture"); return null; }
            if (!m_Textures.TryGetValue(nameID, out var slot)) return null;
            if (slot.ElementBinding && Declared(nameID) != ShaderPropertyType.Texture) { Missing(nameID, "a texture"); return null; }
            // A destroyed texture reads as null itself, not as its destroyed object (matches the
            // Unity 6000.0.71f1 Editor, tests/projects/defaulttextures destroyed.*.held).
            return slot.Texture ? slot.Texture : null;
        }

        TextureSlot Slot(int nameID)
        {
            if (!m_Textures.TryGetValue(nameID, out var slot)) m_Textures[nameID] = slot = NewSlot();
            return slot;
        }

        /// <summary>An empty slot: one a copy dropped, else a new one.</summary>
        TextureSlot NewSlot()
        {
            var spare = m_State.m_SpareSlots;
            if (spare == null || spare.Count == 0) return new TextureSlot();
            var slot = spare[spare.Count - 1];
            spare.RemoveAt(spare.Count - 1);
            slot.Texture = null;
            slot.Element = default;
            slot.ElementBinding = false;
            slot.Scale = Vector2.one;
            slot.Offset = Vector2.zero;
            return slot;
        }

        public void SetTextureOffset(string name, Vector2 value) => SetTextureOffset(Shader.PropertyToID(name), value);

        public void SetTextureOffset(int nameID, Vector2 value)
        {
            TouchSheet();
            m_Own?.Add(nameID);
            Slot(nameID).Offset = value;
            MarkDirty();
        }

        public void SetTextureScale(string name, Vector2 value) => SetTextureScale(Shader.PropertyToID(name), value);

        public void SetTextureScale(int nameID, Vector2 value)
        {
            TouchSheet();
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
            TouchSheet();
            Inherit();
            if (!HasTextureSlot(nameID)) { Missing(nameID, "a texture"); return Vector2.zero; }
            return m_Textures.TryGetValue(nameID, out var slot) ? slot.Offset : Vector2.zero;
        }
        public Vector2 GetTextureScale(string name) => GetTextureScale(Shader.PropertyToID(name));
        public Vector2 GetTextureScale(int nameID)
        {
            TouchSheet();
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

        /// <summary>Takes every setting of <paramref name="saved"/> (the material read from its file
        /// again) and destroys it: Resources.UnloadAsset.</summary>
        internal void RevertTo(Material saved)
        {
            m_Tags.Clear();
            CopyProperties(saved);
            m_Shader = saved.m_Shader;
            m_DoubleSidedGI = saved.m_DoubleSidedGI;
            if (Compat.MaterialReloadWrappersMeasured)
            {
                m_Name = saved.m_Name;
                hideFlags = saved.hideFlags;
            }
            DestroyImmediate(saved, true);
        }

        /// <summary>
        /// Documented (6000.0): copies the property values, keywords, render queue and global
        /// illumination flags of <paramref name="mat"/>; the shader stays. A variant's values are
        /// read as its getters answer them. The queue is the override as stored (-1: none); whether
        /// Unity copies the source's effective queue instead is not measured. Double-sided GI is
        /// not named and is not copied. Into a variant the values are not made overrides (not
        /// measured): a later change of the parent can replace them, with a warning. A material
        /// copied onto itself is the documented result unchanged.
        /// </summary>
        public void CopyPropertiesFromMaterial(Material mat)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            if (m_Parent != null && mat != this)
                Unbound.Runtime.Stubs.Ignored("Material.CopyPropertiesFromMaterial into a material variant",
                    "copies the values without making them overrides: a later change of the parent can replace them");
            CopyProperties(mat);
        }

        void CopyProperties(Material mat)
        {
            mat.Inherit();
            Inherit();
            if (mat == this) return;
            var shaderBefore = m_Shader;
            ClearForCopyOf(mat);
            CopyFrom(mat);
            m_Shader = shaderBefore;
        }

        /// <summary><see cref="CopyPropertiesFromMaterial"/> for Unbound's own materials made from
        /// another (a terrain's passes from its template), without the variant warning.</summary>
        internal void CopyPropertiesKeepingShader(Material mat) => CopyProperties(mat);

        /// <summary>Empties the values before <see cref="CopyFrom"/> copies <paramref name="source"/>'s;
        /// texture slots and arrays the source also has are kept for it to fill.</summary>
        void ClearForCopyOf(Material source)
        {
            m_Floats.Clear(); m_Ints.Clear(); m_Vectors.Clear(); m_Matrices.Clear(); m_Keywords.Clear(); m_KeywordExtras.Clear();
            m_ScriptSet.Clear(); m_TextureNames.Clear(); m_RawBindings.Clear(); m_VectorsAfterTexture.Clear();
            m_Buffers.Clear();
            foreach (var id in m_Arrays.Keys)
                if (!source.m_Arrays.ContainsKey(id)) m_Arrays.Remove(id);
            foreach (var p in m_Textures)
                if (!source.m_Textures.ContainsKey(p.Key))
                {
                    p.Value.Texture = null;
                    (m_State.m_SpareSlots ??= new List<TextureSlot>()).Add(p.Value);
                    m_Textures.Remove(p.Key);
                }
        }

        /// <summary>Makes this material what <c>new Material(source) { parent = null }</c> makes,
        /// keeping its identity (a renderer's material under a property block, made again after
        /// the material or the block changed).</summary>
        internal void ResetToCopyOf(Material source)
        {
            source.Inherit();
            ClearForCopyOf(source);
            m_Tags.Clear();
            CopyFrom(source);
            m_DoubleSidedGI = source.m_DoubleSidedGI;
            m_Name = source.m_Name;
            if (source.m_Locked == null) m_Locked = null;
            else if (m_Locked == null) m_Locked = new HashSet<int>(source.m_Locked);
            else
            {
                m_Locked.Clear();
                AddAll(m_Locked, source.m_Locked);
            }
            parent = null;
        }

        /// <summary>Copies the properties this material's shader declares (measured: a shader
        /// without _MainTex gets no texture slot), the states of the keywords both shaders declare,
        /// and the settings every material has: double-sided GI, instancing, render queue (the
        /// override as stored, as <see cref="CopyPropertiesFromMaterial"/>) and GI flags
        /// (documented, 6000.0). Keyword states are not copied while either keyword space is
        /// unknown (warns). Into a variant, as <see cref="CopyPropertiesFromMaterial"/>.</summary>
        public void CopyMatchingPropertiesFromMaterial(Material mat)
        {
            if (mat == null) throw new ArgumentNullException(nameof(mat));
            mat.Inherit();
            Inherit();
            if (mat == this) return;
            if (m_Parent != null)
                Unbound.Runtime.Stubs.Ignored("Material.CopyMatchingPropertiesFromMaterial into a material variant",
                    "copies the values without making them overrides: a later change of the parent can replace them");
            bool Declares(int id) => Declared(id) != null;
            foreach (var p in mat.m_Floats) if (Declares(p.Key)) m_Floats[p.Key] = p.Value;
            foreach (var p in mat.m_Ints) if (Declares(p.Key)) m_Ints[p.Key] = p.Value;
            foreach (var p in mat.m_Vectors) if (Declares(p.Key)) m_Vectors[p.Key] = p.Value;
            foreach (var p in mat.m_Matrices) if (Declares(p.Key)) m_Matrices[p.Key] = p.Value;
            foreach (var p in mat.m_Textures)
                if (Declares(p.Key)) m_Textures[p.Key] = new TextureSlot { Texture = p.Value.Texture, Element = p.Value.Element, ElementBinding = p.Value.ElementBinding, Scale = p.Value.Scale, Offset = p.Value.Offset };
            CopyMatchingKeywords(mat);
            m_DoubleSidedGI = mat.m_DoubleSidedGI;
            m_EnableInstancing = mat.m_EnableInstancing;
            m_CustomRenderQueue = mat.m_CustomRenderQueue;
            m_GIFlags = mat.m_GIFlags;
            MarkDirty();
        }

        /// <summary>A keyword both keyword spaces declare is on here when it is on there. Names
        /// either space does not declare, and given again or empty, stay as they are.</summary>
        void CopyMatchingKeywords(Material mat)
        {
            if (m_Keywords.Count == 0 && mat.m_Keywords.Count == 0) return;
            var mine = KeywordDeclarations;
            var theirs = mat.KeywordDeclarations;
            if (mine == null || theirs == null)
            {
                Unbound.Runtime.Stubs.Ignored("Material.CopyMatchingPropertiesFromMaterial keyword states",
                    "are not copied: whether both shaders declare a keyword is not known");
                return;
            }
            bool Both(string k) => mine.IndexOf(k) >= 0 && theirs.IndexOf(k) >= 0;
            var on = new List<string>();
            var off = new List<string>();
            foreach (var k in mat.m_Keywords) if (!m_Keywords.Contains(k) && Both(k)) on.Add(k);
            foreach (var k in m_Keywords) if (!mat.m_Keywords.Contains(k) && Both(k)) off.Add(k);
            if (on.Count == 0 && off.Count == 0) return;
            OwnKeywords();
            foreach (var k in on) m_Keywords.Add(k);
            foreach (var k in off) m_Keywords.Remove(k);
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

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var shaderRef = body.Ref("m_Shader");
            m_Shader = shaderRef.IsNull ? null : resolver.Resolve(shaderRef, typeof(Shader)) as Shader;
            m_CustomRenderQueue = body.Int("m_CustomRenderQueue", -1);
            m_SerializedQueue = m_CustomRenderQueue;
            var keywords = body.Str("m_ShaderKeywords");
            foreach (var k in keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)) m_Keywords.Add(k);
            // Keywords the shader does not declare are kept too (shaderKeywords lists them), as
            // often and as empty as the lists have them.
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var list in new[] { "m_ValidKeywords", "m_InvalidKeywords" })
                foreach (var k in body.Get(list)?.Seq() ?? Array.Empty<YNode>())
                {
                    if (k.Kind != YKind.Scalar) continue;
                    if (k.Scalar.Length == 0 || !listed.Add(k.Scalar)) m_KeywordExtras.Add(k.Scalar);
                    else m_Keywords.Add(k.Scalar);
                }
            foreach (var pass in body.Get("disabledShaderPasses")?.Seq() ?? Array.Empty<YNode>())
                if (pass.Kind == YKind.Scalar && pass.Scalar.Length > 0)
                    (m_DisabledPasses ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(pass.Scalar);
            var tags = body.Get("stringTagMap");
            if (tags != null && tags.Kind == YKind.Map)
                for (int i = 0; i < tags.Keys.Length; i++) m_Tags[tags.Keys[i]] = tags.Values[i].AsString();
            var saved = body.Get("m_SavedProperties");
            // The sheet lists exactly the entries read (measured: tests/projects/toolapis matx.partial).
            var sheet = new SavedSheet();
            m_Sheet = sheet;
            if (saved != null)
            {
                foreach (var (name, value) in Entries(saved.Get("m_TexEnvs")))
                {
                    sheet.List(SheetKind.Textures).Add(Shader.PropertyToID(name));
                    var slot = Slot(Shader.PropertyToID(name));
                    var texRef = value.Ref("m_Texture");
                    slot.Texture = texRef.IsNull ? null : resolver.Resolve(texRef, typeof(Texture)) as Texture;
                    slot.Scale = value.Get("m_Scale") != null ? value.Vector2("m_Scale") : Vector2.one;
                    slot.Offset = value.Vector2("m_Offset");
                }
                foreach (var (name, value) in Entries(saved.Get("m_Floats")))
                {
                    int id = Shader.PropertyToID(name);
                    sheet.List(SheetKind.Floats).Add(id);
                    m_Floats[id] = value.AsFloat();
                }
                foreach (var (name, value) in Entries(saved.Get("m_Ints")))
                {
                    int id = Shader.PropertyToID(name);
                    sheet.List(SheetKind.Ints).Add(id);
                    m_Ints[id] = value.AsInt();
                }
                foreach (var (name, value) in Entries(saved.Get("m_Colors")))
                {
                    int id = Shader.PropertyToID(name);
                    sheet.List(SheetKind.Colors).Add(id);
                    m_Vectors[id] = new Vector4(value.Float("r"), value.Float("g"), value.Float("b"), value.Float("a"));
                }
            }
            // Instancing and GI flags (a variant takes its parent's, its own unless overridden).
            m_EnableInstancing = body.Bool("m_EnableInstancingVariants");
            m_DoubleSidedGI = body.Bool("m_DoubleSidedGI");
            m_GIFlags = (MaterialGlobalIlluminationFlags)body.Int("m_LightmapFlags", (int)MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            // Properties locked for variants: names separated by spaces.
            foreach (var name in body.Str("m_LockedProperties").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                SetPropertyLock(name, true);
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
            MarkChanged();
            // A material sub-asset of an imported model: made by its importer.
            if (body.Get("m_UnboundMaterialImport") is YNode import && import.Kind == YKind.Map)
                ModelMaterialImport.Apply(this, import, resolver);
        }

        /// <summary>
        /// What a SerializedObject applied: the material becomes what its serialized data reads
        /// as, as a loaded one does (<see cref="ReadSerialized"/>): the saved properties, keywords,
        /// passes, tags, locks, queue and flags it holds replace the material's, so values a script
        /// set for names the sheet does not list are gone and a declared property the sheet leaves
        /// out reads its shader's default (measured: tests/projects/toolapis matx.edit, mat.after).
        /// Values the sheet never holds (matrices, buffers, arrays) and the parent stay.
        /// </summary>
        internal void ApplySerialized(YNode body, IReferenceResolver resolver)
        {
            Inherit();
            var parent = m_Parent;
            if (parent != null)
                Unbound.Runtime.Stubs.Ignored("SerializedObject.ApplyModifiedProperties on a material variant",
                    "is not measured: the applied sheet becomes the variant's overrides, the rest is taken from the parent again");
            m_Floats.Clear(); m_Ints.Clear(); m_Vectors.Clear(); m_RawBindings.Clear(); m_VectorsAfterTexture.Clear();
            m_Keywords.Clear(); m_KeywordExtras.Clear(); m_Tags.Clear(); m_TextureNames.Clear();
            m_DisabledPasses = null;
            m_Locked = null;
            m_ScriptSet.RemoveWhere(id => !m_Matrices.ContainsKey(id) && !m_Buffers.ContainsKey(id) && !m_Arrays.ContainsKey(id));
            foreach (var p in m_Textures)
            {
                p.Value.Texture = null;
                (m_State.m_SpareSlots ??= new List<TextureSlot>()).Add(p.Value);
            }
            m_Textures.Clear();
            m_Name = body.Str("m_Name");
            hideFlags = (HideFlags)body.Int("m_ObjectHideFlags");
            ReadSerialized(body, resolver);
            if (parent != null)
            {
                // The data has no parent field: the variant keeps its parent, overrides what the
                // sheet lists and takes the rest from the parent again.
                m_Own = new HashSet<int>();
                foreach (var kind in new[] { SheetKind.Textures, SheetKind.Ints, SheetKind.Floats, SheetKind.Colors })
                    AddAll(m_Own, m_Sheet.List(kind));
                m_InheritedVersion = -1;
                Inherit();
            }
        }

        /// <summary>A built-in Material's stored state from its record (the install's object as
        /// the host reads it: references as {fileID, guid} into the built-in files), read as a
        /// project material's: its shader, keywords, saved properties and flags.</summary>
        internal void ReadBuiltinRecord(System.Text.Json.JsonElement fields)
        {
            if (fields.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new FormatException($"the record of built-in Material '{m_Name}' is not an object");
            ReadSerialized(YNode.FromJson(fields), BuiltinReferences.Instance);
        }

        /// <summary>References of the install's built-in resource files, resolved to their objects.</summary>
        sealed class BuiltinReferences : IReferenceResolver
        {
            internal static readonly BuiltinReferences Instance = new BuiltinReferences();

            public Object Resolve(ObjectRef reference, Type expected) =>
                reference.Guid == null || reference.FileId == 0 ? null : BuiltinShaders.Resolve(reference.Guid, reference.FileId);
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
        internal bool ConvertsProperty(int id)
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
        int m_LodEpoch { get => m_State.m_LodEpoch; set => m_State.m_LodEpoch = value; }
        int m_DestroyedTextureEpoch { get => m_State.m_DestroyedTextureEpoch; set => m_State.m_DestroyedTextureEpoch = value; }
        string m_SentShader { get => m_State.m_SentShader; set => m_State.m_SentShader = value; }

        /// <summary>Whether <paramref name="hdr"/> is a texture's `_HDR` whose texture was set after
        /// any vector of that name.</summary>
        bool TextureDecodeWins(int hdr) =>
            HdrDecode.TextureOf(hdr, out int texture) && m_Textures.ContainsKey(texture) && !m_VectorsAfterTexture.Contains(hdr);

        /// <summary>The texture handle a property holding <paramref name="texture"/> sends: a
        /// destroyed texture the material still holds samples opaque black, whatever the
        /// property's default (matches the Unity 6000.0.71f1 Editor, tests/projects/defaulttextures
        /// destroyed.*).</summary>
        internal static ulong SentTexture(Texture texture, RenderTextureSubElement element) =>
            texture is not null && !texture ? TextureHandles.Of(TextureElements.DestroyedTexture) : TextureElements.MaterialHandle(texture, element);

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
            // A texture was destroyed since the last send: one the material holds samples
            // differently now (see DestroyedTexture).
            if (m_DestroyedTextureEpoch != Texture.s_DestroyedEpoch)
            {
                m_DestroyedTextureEpoch = Texture.s_DestroyedEpoch;
                if (!m_Dirty)
                    foreach (var p in m_Textures)
                        if (!p.Value.SentDestroyed && p.Value.Texture is not null && !p.Value.Texture) { m_Dirty = true; break; }
            }
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
            // `<name>_HDR` of every texture property: the texture's decode values, which Unity sets
            // with the texture, unless a vector of that name was set after it (matches the Unity
            // 6000.0.71f1 Editor, tests/projects/lightmapencoding material/* and precedence/*).
            foreach (var p in m_Textures) HdrDecode.IdOf(p.Key);
            foreach (var p in m_Vectors)
            {
                if (TextureDecodeWins(p.Key)) continue;
                Add(p.Key, 1, ConvertsProperty(p.Key) ? (Vector4)((Color)p.Value).linear : p.Value, 0, default);
            }
            foreach (var p in m_Textures)
            {
                p.Value.SentDestroyed = p.Value.Texture is not null && !p.Value.Texture;
                Add(p.Key, 2, default, SentTexture(p.Value.Texture, p.Value.Element), new Vector4(p.Value.Scale.x, p.Value.Scale.y, p.Value.Offset.x, p.Value.Offset.y));
                int hdr = HdrDecode.IdOf(p.Key);
                if (TextureDecodeWins(hdr) || !m_Vectors.ContainsKey(hdr)) Add(hdr, 1, (Vector4)HdrDecode.Of(p.Value.Texture), 0, default);
            }
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
