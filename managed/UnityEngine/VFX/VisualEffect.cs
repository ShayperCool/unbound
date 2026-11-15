using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine.VFX
{
    /// <summary>What <see cref="VisualEffect.GetParticleSystemInfo(int)"/> answers about a system.</summary>
    public partial struct VFXParticleSystemInfo
    {
        public uint aliveCount;
        public uint capacity;
        public bool sleeping;
        public Bounds bounds;

        public VFXParticleSystemInfo(uint aliveCount, uint capacity, bool sleeping, Bounds bounds)
        {
            this.aliveCount = aliveCount;
            this.capacity = capacity;
            this.sleeping = sleeping;
            this.bounds = bounds;
        }
    }

    /// <summary>
    /// An instance of a Visual Effect Graph asset. Its property sheet lists the asset's exposed
    /// properties, each with the asset's value until a script (or the serialized component)
    /// overrides it. The answers below match the Unity 6000.0.71f1 Editor (tests/projects/vfx):
    /// <list type="bullet">
    /// <item><c>Has*</c>: the property exists and has that type (a texture: any texture type);</item>
    /// <item><c>Get*</c>/<c>Set*</c>: an unknown name logs "Value of name '...' was not found", a
    /// property of another type "Value type for '...' is incorrect"; the read answers the type's
    /// default, the write does nothing;</item>
    /// <item><see cref="ResetOverride(int)"/> gives an overridden property the asset's value back;</item>
    /// <item>a texture of another dimension than the property's is refused with an error;</item>
    /// <item>an asset change keeps overrides by name and type: the new sheet takes the
    /// component's saved overrides and, over them, the current ones. The saved overrides are
    /// those the component was loaded with, or had when it was last serialized (cloned) after an
    /// override changed; an asset change itself saves nothing, so an override that only lived
    /// in the sheet is gone once an asset without that property has been assigned.</item>
    /// </list>
    /// </summary>
    [ClassId(2083052967)]
    [global::UnityEngine.@RequireComponent(typeof(global::UnityEngine.Transform))]
    public partial class VisualEffect : Behaviour
    {
        /// <summary>A serialized override of the component's property sheet.</summary>
        struct SerializedOverride
        {
            internal SheetKind Kind;
            internal int NameId;
            internal bool Overridden;
            internal Vector4 Vector;
            internal Matrix4x4 Matrix;
            internal uint Word;
            internal object Object;
        }

        /// <summary>The typed lists of the serialized sheet.</summary>
        enum SheetKind
        {
            Float, Vector2, Vector3, Vector4, Uint, Int, Matrix, Curve, Gradient, NamedObject, Bool, Buffer,
        }

        VisualEffectAsset m_Asset;
        string m_InitialEventName = "";
        bool m_InitialEventNameOverriden;
        uint m_StartSeed;
        bool m_ResetSeedOnPlay = true;
        /// <summary>Whether the effect may be batched with others of its asset, and the resource
        /// version its component was saved with: kept as serialized (nothing is simulated yet).</summary>
        bool m_AllowInstancing = true;
        uint m_ResourceVersion;
        bool m_Pause;
        float m_PlayRate = 1f;
        /// <summary>The saved overrides: the serialized sheet, as loaded or last written back.</summary>
        List<SerializedOverride> m_Serialized;
        /// <summary>Whether an override changed since the saved overrides were written back.</summary>
        bool m_SheetChanged;

        // The runtime sheet: one entry per exposed property of the asset, in the asset's order.
        VisualEffectCompiledData m_Data = VisualEffectCompiledData.Empty;
        VisualEffectValues m_Values;
        bool[] m_Overridden = Array.Empty<bool>();

        public Action<VFXOutputEventArgs> outputEventReceived;

        public VisualEffect() { }

        /// <summary>What Unbound does not do with an effect yet (the load warning and
        /// <c>unbound doctor</c> say it).</summary>
        internal const string NotSimulated = "Visual Effect Graph systems are not simulated or drawn yet: the effect shows nothing";

        internal static bool s_WarnedNotSimulated;

        internal override void OnEngineEnable() => WarnNotSimulated();

        /// <summary>Says once, for the first enabled effect whose asset has systems, that
        /// effects are not simulated.</summary>
        void WarnNotSimulated()
        {
            if (s_WarnedNotSimulated || m_Asset == null || m_Data.Systems.Length == 0 || !isActiveAndEnabled) return;
            s_WarnedNotSimulated = true;
            Debug.LogWarning($"Unbound: VisualEffect '{name}' ({m_Asset.name}): {NotSimulated}. `unbound doctor` lists the project's effects.", this);
        }

        /// <summary>AddComponent&lt;VisualEffect&gt;() adds the effect's renderer with it.</summary>
        internal override Type AddedCompanion => typeof(VFXRenderer);

        public VisualEffectAsset visualEffectAsset
        {
            get => m_Asset;
            set
            {
                // The saved overrides with the current ones over them, for the new sheet only.
                var merged = m_Serialized == null ? null : new List<SerializedOverride>(m_Serialized);
                WriteBack(ref merged);
                m_Asset = value;
                CreateSheet();
                Apply(merged);
                WarnNotSimulated();
            }
        }

        /// <summary>Whether no camera sees the effect. Nothing draws an effect yet, so none does
        /// (said once): the value a new effect has in the Editor before its first frame.</summary>
        public bool culled
        {
            get
            {
                Stubs.Ignored("VisualEffect.culled", "is always true: effects are not drawn yet, so no camera sees one");
                return true;
            }
        }

        public bool pause
        {
            get => m_Pause;
            set => m_Pause = value;
        }

        public float playRate
        {
            get => m_PlayRate;
            set => m_PlayRate = value;
        }

        public uint startSeed
        {
            get => m_StartSeed;
            set => m_StartSeed = value;
        }

        public bool resetSeedOnPlay
        {
            get => m_ResetSeedOnPlay;
            set => m_ResetSeedOnPlay = value;
        }

        /// <summary>The event sent when the effect starts: its own once set (or serialized as
        /// overridden), else the asset's, else "OnPlay".</summary>
        public string initialEventName
        {
            get => m_InitialEventNameOverriden ? m_InitialEventName
                : m_Asset != null ? m_Asset.m_Settings.InitialEventName ?? "" : VisualEffectAsset.PlayEventName;
            set
            {
                m_InitialEventName = value ?? "";
                m_InitialEventNameOverriden = true;
            }
        }

        public int initialEventID
        {
            get => Shader.PropertyToID(initialEventName);
            set => initialEventName = Shader.IdToName(value);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var asset = body.Ref("m_Asset");
            m_Asset = asset.IsNull ? null : resolver.Resolve(asset, typeof(VisualEffectAsset)) as VisualEffectAsset;
            m_InitialEventName = body.Str("m_InitialEventName");
            m_InitialEventNameOverriden = body.Bool("m_InitialEventNameOverriden");
            m_StartSeed = (uint)body.Long("m_StartSeed");
            m_ResetSeedOnPlay = body.Bool("m_ResetSeedOnPlay", true);
            m_AllowInstancing = body.Bool("m_AllowInstancing", true);
            m_ResourceVersion = (uint)body.Long("m_ResourceVersion");
            m_Serialized = ReadSheet(body.Get("m_PropertySheet"), resolver);
            CreateSheet();
            Apply(m_Serialized);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (VisualEffect)source;
            m_Asset = s.m_Asset;
            m_InitialEventName = s.m_InitialEventName;
            m_InitialEventNameOverriden = s.m_InitialEventNameOverriden;
            m_StartSeed = s.m_StartSeed;
            m_ResetSeedOnPlay = s.m_ResetSeedOnPlay;
            m_AllowInstancing = s.m_AllowInstancing;
            m_ResourceVersion = s.m_ResourceVersion;
            m_Pause = s.m_Pause;
            m_PlayRate = s.m_PlayRate;
            // A copy is made of the source as serialized: the source writes its changed overrides
            // back into its saved ones first, and the copy starts from those.
            if (s.m_SheetChanged)
            {
                s.WriteBack(ref s.m_Serialized);
                s.m_SheetChanged = false;
            }
            m_Serialized = s.m_Serialized == null ? null : new List<SerializedOverride>(s.m_Serialized);
            CreateSheet();
            Apply(m_Serialized);
        }

        /// <summary>A new sheet for the current asset: every property at the asset's value.</summary>
        void CreateSheet()
        {
            m_Data = m_Asset != null ? m_Asset.m_Data : VisualEffectCompiledData.Empty;
            m_Values = new VisualEffectValues(m_Data);
            m_Overridden = m_Data.ExposedCount == 0 ? Array.Empty<bool>() : new bool[m_Data.ExposedCount];
        }

        // ---------------------------------------------------------------- the serialized sheet

        /// <summary>The serialized sheet: one typed list of entries (value, name, overridden flag)
        /// per kind of value.</summary>
        static List<SerializedOverride> ReadSheet(YNode sheet, IReferenceResolver resolver)
        {
            if (sheet == null) return null;
            List<SerializedOverride> entries = null;
            foreach (var item in sheet.Get("m_Float")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Float);
                entry.Vector = new Vector4(item.Float("m_Value"), 0f, 0f, 0f);
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Vector2f")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Vector2);
                entry.Vector = ReadVector(item.Get("m_Value"));
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Vector3f")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Vector3);
                entry.Vector = ReadVector(item.Get("m_Value"));
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Vector4f")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Vector4);
                entry.Vector = ReadVector(item.Get("m_Value"));
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Uint")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Uint);
                entry.Word = (uint)item.Long("m_Value");
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Int")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Int);
                entry.Word = unchecked((uint)item.Int("m_Value"));
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Matrix4x4f")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Matrix);
                entry.Matrix = ReadMatrix(item.Get("m_Value"));
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_AnimationCurve")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Curve);
                var value = item.Get("m_Value");
                entry.Object = value != null ? BuiltinReaders.AnimationCurve(value) : null;
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Gradient")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Gradient);
                var value = item.Get("m_Value");
                entry.Object = value != null ? BuiltinReaders.Gradient(value) : null;
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_NamedObject")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.NamedObject);
                var reference = item.Ref("m_Value");
                entry.Object = reference.IsNull ? null : resolver.Resolve(reference, typeof(Object));
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            foreach (var item in sheet.Get("m_Bool")?.Get("m_Array")?.Seq() ?? Array.Empty<YNode>())
            {
                var entry = Entry(item, SheetKind.Bool);
                entry.Word = item.Bool("m_Value") ? 1u : 0u;
                (entries ??= new List<SerializedOverride>()).Add(entry);
            }
            return entries;
        }

        static SerializedOverride Entry(YNode item, SheetKind kind) =>
            new SerializedOverride { Kind = kind, NameId = Shader.PropertyToID(item.Str("m_Name")), Overridden = item.Bool("m_Overridden") };

        static Vector4 ReadVector(YNode value) =>
            value == null ? default : new Vector4(value.Float("x"), value.Float("y"), value.Float("z"), value.Float("w"));

        static Matrix4x4 ReadMatrix(YNode value)
        {
            var m = default(Matrix4x4);
            if (value == null) return m;
            m.m00 = value.Float("e00"); m.m01 = value.Float("e01"); m.m02 = value.Float("e02"); m.m03 = value.Float("e03");
            m.m10 = value.Float("e10"); m.m11 = value.Float("e11"); m.m12 = value.Float("e12"); m.m13 = value.Float("e13");
            m.m20 = value.Float("e20"); m.m21 = value.Float("e21"); m.m22 = value.Float("e22"); m.m23 = value.Float("e23");
            m.m30 = value.Float("e30"); m.m31 = value.Float("e31"); m.m32 = value.Float("e32"); m.m33 = value.Float("e33");
            return m;
        }

        /// <summary>The list a property's override is serialized in.</summary>
        static SheetKind KindOf(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.Float: return SheetKind.Float;
                case VFXValueType.Float2: return SheetKind.Vector2;
                case VFXValueType.Float3: return SheetKind.Vector3;
                case VFXValueType.Float4: return SheetKind.Vector4;
                case VFXValueType.Uint32: return SheetKind.Uint;
                case VFXValueType.Int32: return SheetKind.Int;
                case VFXValueType.Boolean: return SheetKind.Bool;
                case VFXValueType.Matrix4x4: return SheetKind.Matrix;
                case VFXValueType.Curve: return SheetKind.Curve;
                case VFXValueType.ColorGradient: return SheetKind.Gradient;
                case VFXValueType.Buffer: return SheetKind.Buffer;
                default: return SheetKind.NamedObject;
            }
        }

        /// <summary>Writes the sheet into a list of overrides: a property's entry (the first of
        /// its name in the list of its type) takes its current value and overridden flag; an
        /// overridden property without an entry gets one. Entries of other names and types stay
        /// as they are.</summary>
        void WriteBack(ref List<SerializedOverride> entries)
        {
            for (int i = 0; i < m_Overridden.Length; i++)
            {
                int expression = Expression(i);
                var type = m_Data.ExpressionTypes[expression];
                var kind = KindOf(type);
                int nameId = m_Data.ExposedId(i);
                int at = -1;
                for (int e = 0; entries != null && e < entries.Count; e++)
                    if (entries[e].Kind == kind && entries[e].NameId == nameId)
                    {
                        at = e;
                        break;
                    }
                if (at < 0 && !m_Overridden[i]) continue;
                var entry = new SerializedOverride { Kind = kind, NameId = nameId, Overridden = m_Overridden[i] };
                switch (kind)
                {
                    case SheetKind.Float:
                    case SheetKind.Vector2:
                    case SheetKind.Vector3:
                    case SheetKind.Vector4:
                        entry.Vector = m_Values.GetVector(expression, UnityEditor.VFX.VFXExpressionHelper.GetSizeOfType(type));
                        break;
                    case SheetKind.Matrix: entry.Matrix = m_Values.GetMatrix(expression); break;
                    case SheetKind.Uint:
                    case SheetKind.Int:
                    case SheetKind.Bool:
                        entry.Word = m_Values.GetWord(expression);
                        break;
                    default: entry.Object = m_Values.GetObject(expression); break;
                }
                if (at < 0) (entries ??= new List<SerializedOverride>()).Add(entry);
                else entries[at] = entry;
            }
        }

        /// <summary>Applies overrides to the sheet: an entry names the first property of its
        /// name; when that property has the entry's type it takes the entry's overridden flag,
        /// and its value when overridden. Other entries are left out.</summary>
        void Apply(List<SerializedOverride> entries)
        {
            if (entries == null) return;
            foreach (var entry in entries)
            {
                int index = m_Data.ExposedIndex(entry.NameId);
                if (index < 0) continue;
                int expression = m_Data.Exposed[index].mapping.index;
                var type = m_Data.ExpressionTypes[expression];
                if (!Accepts(type, entry)) continue;
                m_Overridden[index] = entry.Overridden;
                if (!entry.Overridden) continue;
                switch (entry.Kind)
                {
                    case SheetKind.Float: m_Values.SetFloat(expression, entry.Vector.x); break;
                    case SheetKind.Vector2: m_Values.SetVector(expression, entry.Vector, 2); break;
                    case SheetKind.Vector3: m_Values.SetVector(expression, entry.Vector, 3); break;
                    case SheetKind.Vector4: m_Values.SetVector(expression, entry.Vector, 4); break;
                    case SheetKind.Matrix: m_Values.SetMatrix(expression, entry.Matrix); break;
                    case SheetKind.Uint:
                    case SheetKind.Int:
                    case SheetKind.Bool:
                        m_Values.SetWord(expression, entry.Word);
                        break;
                    default: m_Values.SetObject(expression, entry.Object); break;
                }
            }
        }

        static bool Accepts(VFXValueType type, in SerializedOverride entry)
        {
            switch (entry.Kind)
            {
                case SheetKind.Float: return type == VFXValueType.Float;
                case SheetKind.Vector2: return type == VFXValueType.Float2;
                case SheetKind.Vector3: return type == VFXValueType.Float3;
                case SheetKind.Vector4: return type == VFXValueType.Float4;
                case SheetKind.Uint: return type == VFXValueType.Uint32;
                case SheetKind.Int: return type == VFXValueType.Int32;
                case SheetKind.Bool: return type == VFXValueType.Boolean;
                case SheetKind.Matrix: return type == VFXValueType.Matrix4x4;
                case SheetKind.Curve: return type == VFXValueType.Curve;
                case SheetKind.Gradient: return type == VFXValueType.ColorGradient;
                case SheetKind.Buffer: return type == VFXValueType.Buffer;
                default:
                    // An object entry: a texture, mesh or skinned mesh renderer property, and an
                    // object of that kind (or none).
                    if (type == VFXValueType.Mesh) return entry.Object == null || entry.Object is Mesh;
                    if (type == VFXValueType.SkinnedMeshRenderer) return entry.Object == null || entry.Object is SkinnedMeshRenderer;
                    return VisualEffectCompiledData.IsTextureType(type) && (entry.Object == null || entry.Object is Texture);
            }
        }

        // ---------------------------------------------------------------- lookups

        /// <summary>The sheet index of a property of a type, for a read or a write: -1, with the
        /// Editor's error, when no property has the name or it has another type.</summary>
        int Find(int nameID, VFXValueType type)
        {
            int index = m_Data.ExposedIndex(nameID);
            if (index < 0)
            {
                Debug.LogError($"Value of name '{Shader.IdToName(nameID)}' was not found", this);
                return -1;
            }
            var actual = m_Data.ExposedType(index);
            if (type == TextureKind ? VisualEffectCompiledData.IsTextureType(actual) : actual == type) return index;
            Debug.LogError($"Value type for '{Shader.IdToName(nameID)}' is incorrect", this);
            return -1;
        }

        /// <summary>Stands for "any texture type" in <see cref="Find"/> and <see cref="Has"/>.</summary>
        const VFXValueType TextureKind = VFXValueType.Texture2D;

        bool Has(int nameID, VFXValueType type)
        {
            int index = m_Data.ExposedIndex(nameID);
            if (index < 0) return false;
            var actual = m_Data.ExposedType(index);
            return type == TextureKind ? VisualEffectCompiledData.IsTextureType(actual) : actual == type;
        }

        int Expression(int index) => m_Data.Exposed[index].mapping.index;

        public bool HasBool(int nameID) => Has(nameID, VFXValueType.Boolean);
        public bool HasInt(int nameID) => Has(nameID, VFXValueType.Int32);
        public bool HasUInt(int nameID) => Has(nameID, VFXValueType.Uint32);
        public bool HasFloat(int nameID) => Has(nameID, VFXValueType.Float);
        public bool HasVector2(int nameID) => Has(nameID, VFXValueType.Float2);
        public bool HasVector3(int nameID) => Has(nameID, VFXValueType.Float3);
        public bool HasVector4(int nameID) => Has(nameID, VFXValueType.Float4);
        public bool HasMatrix4x4(int nameID) => Has(nameID, VFXValueType.Matrix4x4);
        public bool HasTexture(int nameID) => Has(nameID, TextureKind);
        public bool HasAnimationCurve(int nameID) => Has(nameID, VFXValueType.Curve);
        public bool HasGradient(int nameID) => Has(nameID, VFXValueType.ColorGradient);
        public bool HasMesh(int nameID) => Has(nameID, VFXValueType.Mesh);
        public bool HasSkinnedMeshRenderer(int nameID) => Has(nameID, VFXValueType.SkinnedMeshRenderer);
        public bool HasGraphicsBuffer(int nameID) => Has(nameID, VFXValueType.Buffer);

        public bool HasBool(string name) => HasBool(Shader.PropertyToID(name));
        public bool HasInt(string name) => HasInt(Shader.PropertyToID(name));
        public bool HasUInt(string name) => HasUInt(Shader.PropertyToID(name));
        public bool HasFloat(string name) => HasFloat(Shader.PropertyToID(name));
        public bool HasVector2(string name) => HasVector2(Shader.PropertyToID(name));
        public bool HasVector3(string name) => HasVector3(Shader.PropertyToID(name));
        public bool HasVector4(string name) => HasVector4(Shader.PropertyToID(name));
        public bool HasMatrix4x4(string name) => HasMatrix4x4(Shader.PropertyToID(name));
        public bool HasTexture(string name) => HasTexture(Shader.PropertyToID(name));
        public bool HasAnimationCurve(string name) => HasAnimationCurve(Shader.PropertyToID(name));
        public bool HasGradient(string name) => HasGradient(Shader.PropertyToID(name));
        public bool HasMesh(string name) => HasMesh(Shader.PropertyToID(name));
        public bool HasSkinnedMeshRenderer(string name) => HasSkinnedMeshRenderer(Shader.PropertyToID(name));
        public bool HasGraphicsBuffer(string name) => HasGraphicsBuffer(Shader.PropertyToID(name));

        void SetWord(int nameID, VFXValueType type, uint word)
        {
            int index = Find(nameID, type);
            if (index < 0) return;
            m_Overridden[index] = true;
            m_SheetChanged = true;
            m_Values.SetWord(Expression(index), word);
        }

        void SetVector(int nameID, VFXValueType type, Vector4 value, int components)
        {
            int index = Find(nameID, type);
            if (index < 0) return;
            m_Overridden[index] = true;
            m_SheetChanged = true;
            m_Values.SetVector(Expression(index), value, components);
        }

        void SetObject(int nameID, VFXValueType type, object value)
        {
            int index = Find(nameID, type);
            if (index < 0) return;
            m_Overridden[index] = true;
            m_SheetChanged = true;
            m_Values.SetObject(Expression(index), value);
        }

        public void SetBool(int nameID, bool b) => SetWord(nameID, VFXValueType.Boolean, b ? 1u : 0u);
        public void SetInt(int nameID, int i) => SetWord(nameID, VFXValueType.Int32, unchecked((uint)i));
        public void SetUInt(int nameID, uint i) => SetWord(nameID, VFXValueType.Uint32, i);
        public void SetFloat(int nameID, float f) => SetWord(nameID, VFXValueType.Float, BitConverter.SingleToUInt32Bits(f));
        public void SetVector2(int nameID, Vector2 v) => SetVector(nameID, VFXValueType.Float2, v, 2);
        public void SetVector3(int nameID, Vector3 v) => SetVector(nameID, VFXValueType.Float3, v, 3);
        public void SetVector4(int nameID, Vector4 v) => SetVector(nameID, VFXValueType.Float4, v, 4);

        public void SetMatrix4x4(int nameID, Matrix4x4 v)
        {
            int index = Find(nameID, VFXValueType.Matrix4x4);
            if (index < 0) return;
            m_Overridden[index] = true;
            m_SheetChanged = true;
            m_Values.SetMatrix(Expression(index), v);
        }

        /// <summary>A texture must have the property's dimension (a camera buffer property: 2D
        /// or a 2D array); another one is refused with an error and the property keeps its value.</summary>
        public void SetTexture(int nameID, Texture t)
        {
            int index = Find(nameID, TextureKind);
            if (index < 0) return;
            if (t != null)
            {
                var type = m_Data.ExposedType(index);
                var actual = t.dimension;
                if (type == VFXValueType.CameraBuffer)
                {
                    if (actual != TextureDimension.Tex2D && actual != TextureDimension.Tex2DArray)
                    {
                        Debug.LogError($"Error assigning {DimensionName(actual)} texture to CameraBuffer property '{Shader.IdToName(nameID)}': Dimensions must match", this);
                        return;
                    }
                }
                else
                {
                    var wanted = UnityEditor.VFX.VFXExpressionHelper.GetTextureDimension(type);
                    if (actual != wanted)
                    {
                        Debug.LogError($"Error assigning {DimensionName(actual)} texture to {DimensionName(wanted)} texture property '{Shader.IdToName(nameID)}': Dimensions must match", this);
                        return;
                    }
                }
            }
            m_Overridden[index] = true;
            m_SheetChanged = true;
            m_Values.SetObject(Expression(index), t);
        }

        /// <summary>A texture dimension as the Editor's messages name it.</summary>
        static string DimensionName(TextureDimension dimension)
        {
            switch (dimension)
            {
                case TextureDimension.Tex2D: return "2D";
                case TextureDimension.Tex3D: return "3D";
                case TextureDimension.Cube: return "CUBE";
                case TextureDimension.Tex2DArray: return "2DArray";
                case TextureDimension.CubeArray: return "CubeArray";
                default: return "any";
            }
        }

        /// <summary>The property takes a copy of the curve.</summary>
        public void SetAnimationCurve(int nameID, AnimationCurve c)
        {
            AnimationCurve copy = null;
            if (c != null)
            {
                copy = new AnimationCurve();
                copy.CopyFrom(c);
            }
            SetObject(nameID, VFXValueType.Curve, copy);
        }

        /// <summary>The property takes a copy of the gradient.</summary>
        public void SetGradient(int nameID, Gradient g) => SetObject(nameID, VFXValueType.ColorGradient, Copy(g));

        public void SetMesh(int nameID, Mesh m) => SetObject(nameID, VFXValueType.Mesh, m);
        public void SetSkinnedMeshRenderer(int nameID, SkinnedMeshRenderer m) => SetObject(nameID, VFXValueType.SkinnedMeshRenderer, m);
        public void SetGraphicsBuffer(int nameID, GraphicsBuffer g) => SetObject(nameID, VFXValueType.Buffer, g);

        public void SetBool(string name, bool b) => SetBool(Shader.PropertyToID(name), b);
        public void SetInt(string name, int i) => SetInt(Shader.PropertyToID(name), i);
        public void SetUInt(string name, uint i) => SetUInt(Shader.PropertyToID(name), i);
        public void SetFloat(string name, float f) => SetFloat(Shader.PropertyToID(name), f);
        public void SetVector2(string name, Vector2 v) => SetVector2(Shader.PropertyToID(name), v);
        public void SetVector3(string name, Vector3 v) => SetVector3(Shader.PropertyToID(name), v);
        public void SetVector4(string name, Vector4 v) => SetVector4(Shader.PropertyToID(name), v);
        public void SetMatrix4x4(string name, Matrix4x4 v) => SetMatrix4x4(Shader.PropertyToID(name), v);
        public void SetTexture(string name, Texture t) => SetTexture(Shader.PropertyToID(name), t);
        public void SetAnimationCurve(string name, AnimationCurve c) => SetAnimationCurve(Shader.PropertyToID(name), c);
        public void SetGradient(string name, Gradient g) => SetGradient(Shader.PropertyToID(name), g);
        public void SetMesh(string name, Mesh m) => SetMesh(Shader.PropertyToID(name), m);
        public void SetSkinnedMeshRenderer(string name, SkinnedMeshRenderer m) => SetSkinnedMeshRenderer(Shader.PropertyToID(name), m);
        public void SetGraphicsBuffer(string name, GraphicsBuffer g) => SetGraphicsBuffer(Shader.PropertyToID(name), g);

        public bool GetBool(int nameID)
        {
            int index = Find(nameID, VFXValueType.Boolean);
            return index >= 0 && m_Values.GetWord(Expression(index)) != 0;
        }

        public int GetInt(int nameID)
        {
            int index = Find(nameID, VFXValueType.Int32);
            return index < 0 ? 0 : unchecked((int)m_Values.GetWord(Expression(index)));
        }

        public uint GetUInt(int nameID)
        {
            int index = Find(nameID, VFXValueType.Uint32);
            return index < 0 ? 0u : m_Values.GetWord(Expression(index));
        }

        public float GetFloat(int nameID)
        {
            int index = Find(nameID, VFXValueType.Float);
            return index < 0 ? 0f : m_Values.GetFloat(Expression(index));
        }

        public Vector2 GetVector2(int nameID)
        {
            int index = Find(nameID, VFXValueType.Float2);
            return index < 0 ? default : (Vector2)m_Values.GetVector(Expression(index), 2);
        }

        public Vector3 GetVector3(int nameID)
        {
            int index = Find(nameID, VFXValueType.Float3);
            return index < 0 ? default : (Vector3)m_Values.GetVector(Expression(index), 3);
        }

        public Vector4 GetVector4(int nameID)
        {
            int index = Find(nameID, VFXValueType.Float4);
            return index < 0 ? default : m_Values.GetVector(Expression(index), 4);
        }

        public Matrix4x4 GetMatrix4x4(int nameID)
        {
            int index = Find(nameID, VFXValueType.Matrix4x4);
            return index < 0 ? default : m_Values.GetMatrix(Expression(index));
        }

        public Texture GetTexture(int nameID)
        {
            int index = Find(nameID, TextureKind);
            return index < 0 ? null : m_Values.GetObject(Expression(index)) as Texture;
        }

        public Mesh GetMesh(int nameID)
        {
            int index = Find(nameID, VFXValueType.Mesh);
            return index < 0 ? null : m_Values.GetObject(Expression(index)) as Mesh;
        }

        public SkinnedMeshRenderer GetSkinnedMeshRenderer(int nameID)
        {
            int index = Find(nameID, VFXValueType.SkinnedMeshRenderer);
            return index < 0 ? null : m_Values.GetObject(Expression(index)) as SkinnedMeshRenderer;
        }

        internal GraphicsBuffer GetGraphicsBuffer(int nameID)
        {
            int index = Find(nameID, VFXValueType.Buffer);
            return index < 0 ? null : m_Values.GetObject(Expression(index)) as GraphicsBuffer;
        }

        /// <summary>A new gradient with the property's keys; an empty one when the lookup fails.</summary>
        public Gradient GetGradient(int nameID)
        {
            int index = Find(nameID, VFXValueType.ColorGradient);
            var found = index < 0 ? null : m_Values.GetObject(Expression(index)) as Gradient;
            return Copy(found) ?? new Gradient();
        }

        /// <summary>A new curve with the property's keys; an empty one when the lookup fails.</summary>
        public AnimationCurve GetAnimationCurve(int nameID)
        {
            int index = Find(nameID, VFXValueType.Curve);
            var curve = new AnimationCurve();
            if (index >= 0 && m_Values.GetObject(Expression(index)) is AnimationCurve found) curve.CopyFrom(found);
            return curve;
        }

        static Gradient Copy(Gradient g)
        {
            if (g == null) return null;
            var copy = new Gradient { mode = g.mode, colorSpace = g.colorSpace };
            copy.SetKeys(g.colorKeys, g.alphaKeys);
            return copy;
        }

        public bool GetBool(string name) => GetBool(Shader.PropertyToID(name));
        public int GetInt(string name) => GetInt(Shader.PropertyToID(name));
        public uint GetUInt(string name) => GetUInt(Shader.PropertyToID(name));
        public float GetFloat(string name) => GetFloat(Shader.PropertyToID(name));
        public Vector2 GetVector2(string name) => GetVector2(Shader.PropertyToID(name));
        public Vector3 GetVector3(string name) => GetVector3(Shader.PropertyToID(name));
        public Vector4 GetVector4(string name) => GetVector4(Shader.PropertyToID(name));
        public Matrix4x4 GetMatrix4x4(string name) => GetMatrix4x4(Shader.PropertyToID(name));
        public Texture GetTexture(string name) => GetTexture(Shader.PropertyToID(name));
        public Mesh GetMesh(string name) => GetMesh(Shader.PropertyToID(name));
        public SkinnedMeshRenderer GetSkinnedMeshRenderer(string name) => GetSkinnedMeshRenderer(Shader.PropertyToID(name));
        internal GraphicsBuffer GetGraphicsBuffer(string name) => GetGraphicsBuffer(Shader.PropertyToID(name));
        public AnimationCurve GetAnimationCurve(string name) => GetAnimationCurve(Shader.PropertyToID(name));
        public Gradient GetGradient(string name) => GetGradient(Shader.PropertyToID(name));

        public void ResetOverride(string name) => ResetOverride(Shader.PropertyToID(name));

        /// <summary>Gives an overridden property the asset's value back; nothing for an unknown
        /// name or a property that is not overridden.</summary>
        public void ResetOverride(int nameID)
        {
            int index = m_Data.ExposedIndex(nameID);
            if (index < 0 || !m_Overridden[index]) return;
            m_Overridden[index] = false;
            m_SheetChanged = true;
            m_Values.LoadConstant(Expression(index));
        }

        public TextureDimension GetTextureDimension(string name) => GetTextureDimension(Shader.PropertyToID(name));

        /// <summary>The dimension of a texture property; Unknown for another property or name.</summary>
        public TextureDimension GetTextureDimension(int nameID)
        {
            int index = m_Data.ExposedIndex(nameID);
            return index < 0 ? TextureDimension.Unknown : UnityEditor.VFX.VFXExpressionHelper.GetTextureDimension(m_Data.ExposedType(index));
        }

        // ---------------------------------------------------------------- systems

        public bool HasSystem(string name) => HasSystem(Shader.PropertyToID(name));

        public bool HasSystem(int nameID) => m_Data.SystemIndex(nameID) >= 0;

        public void GetSystemNames(List<string> names) => m_Data.SystemNames(names, null);

        public void GetParticleSystemNames(List<string> names) => m_Data.SystemNames(names, VFXSystemType.Particle);

        public void GetOutputEventNames(List<string> names) => m_Data.SystemNames(names, VFXSystemType.OutputEvent);

        public void GetSpawnSystemNames(List<string> names) => m_Data.SystemNames(names, VFXSystemType.Spawner);

        /// <summary>An event attribute with the asset's event attribute layout; null without an asset.</summary>
        public VFXEventAttribute CreateVFXEventAttribute() =>
            m_Asset == null ? null : VFXEventAttribute.Internal_InstanciateVFXEventAttribute(m_Asset);
    }
}
