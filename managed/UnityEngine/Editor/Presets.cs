using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor.Presets
{
    /// <summary>
    /// The type of object a preset applies to: a native class by its id, a script's class by its
    /// script (Unity's PresetType, whose fields and equality its published C# gives). Matches the
    /// Unity 6000.0.71f1 Editor (tests/projects/presets type.*, tests/projects/editortitlebar
    /// presetValid): an object of a class excluded from presets
    /// (<see cref="ExcludeFromPresetAttribute"/>: GameObject, Texture2D, Preset) is no valid type;
    /// a component, a script's object (MonoBehaviour, ScriptableObject) or an importer is a valid
    /// default type, another asset (a Material, an AnimationClip) a valid type that has no default
    /// presets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct PresetType : IEquatable<PresetType>
    {
        int m_NativeTypeID;
        MonoScript m_ManagedTypePPtr;
        string m_ManagedTypeFallback;

        public PresetType(Object o)
        {
            m_NativeTypeID = 0;
            m_ManagedTypePPtr = null;
            m_ManagedTypeFallback = string.Empty;
            if (!Compat.EditorTitlebarMeasured) { Stubs.NotImplemented("UnityEditor.Presets.PresetType.ctor"); return; }
            if (!o) throw ObjectErrors.Argument(o, "target");
            var type = o.GetType();
            if (o is MonoBehaviour || o is ScriptableObject)
            {
                m_NativeTypeID = ScriptClassId;
                m_ManagedTypePPtr = MonoScript.FromType(type);
                if (m_ManagedTypePPtr == null) m_ManagedTypeFallback = type.FullName ?? string.Empty;
            }
            else
            {
                m_NativeTypeID = NativeClassIdOf(type);
            }
        }

        internal PresetType(int nativeTypeID, MonoScript script, string fallback)
        {
            m_NativeTypeID = nativeTypeID;
            m_ManagedTypePPtr = script;
            m_ManagedTypeFallback = fallback ?? string.Empty;
        }

        /// <summary>The class id a script's object is stored under (MonoBehaviour's, a ScriptableObject's
        /// too: tests/projects/presets Tuned.preset m_TargetType).</summary>
        const int ScriptClassId = 114;

        /// <summary>The class the type names (null: none Unbound knows).</summary>
        Type TargetType =>
            m_ManagedTypePPtr != null ? m_ManagedTypePPtr.GetClass()
            : !string.IsNullOrEmpty(m_ManagedTypeFallback) ? ResolveManagedType(m_ManagedTypeFallback)
            : m_NativeTypeID == 0 ? typeof(Object) : NativeTypeOf(m_NativeTypeID);

        public bool IsValid()
        {
            var type = TargetType;
            return m_NativeTypeID != 0 && type != null && type != typeof(Preset)
                && !type.IsDefined(typeof(ExcludeFromPresetAttribute), false);
        }

        public bool IsValidDefault()
        {
            if (!IsValid()) return false;
            var type = TargetType;
            return typeof(Component).IsAssignableFrom(type) || typeof(AssetImporter).IsAssignableFrom(type)
                || typeof(ScriptableObject).IsAssignableFrom(type);
        }

        public string GetManagedTypeName()
        {
            if (m_ManagedTypePPtr != null) return m_ManagedTypePPtr.GetClass()?.FullName ?? m_ManagedTypePPtr.name;
            if (!string.IsNullOrEmpty(m_ManagedTypeFallback)) return m_ManagedTypeFallback;
            return TargetType?.FullName ?? string.Empty;
        }

        public override bool Equals(object obj) => obj is PresetType other && Equals(other);

        public bool Equals(PresetType other) =>
            m_NativeTypeID == other.m_NativeTypeID && m_ManagedTypePPtr == other.m_ManagedTypePPtr
            && (m_ManagedTypeFallback ?? string.Empty) == (other.m_ManagedTypeFallback ?? string.Empty);

        public override int GetHashCode() => HashCode.Combine(m_NativeTypeID, m_ManagedTypePPtr == null ? 0 : m_ManagedTypePPtr.GetInstanceID(), m_ManagedTypeFallback ?? string.Empty);

        public static bool operator ==(PresetType a, PresetType b) => a.Equals(b);

        public static bool operator !=(PresetType a, PresetType b) => !a.Equals(b);

        /// <summary>A type as a preset or the PresetManager stores it (m_NativeTypeID,
        /// m_ManagedTypePPtr, m_ManagedTypeFallback).</summary>
        internal static PresetType Read(YNode node, IReferenceResolver resolver)
        {
            if (node == null) return default;
            var script = node.Ref("m_ManagedTypePPtr");
            return new PresetType(node.Int("m_NativeTypeID"),
                script.IsNull || resolver == null ? null : resolver.Resolve(script, typeof(MonoScript)) as MonoScript,
                node.Str("m_ManagedTypeFallback"));
        }

        static string s_NativeTableVersion;
        static Dictionary<string, int> s_NativeIdByName;
        static Dictionary<int, Type> s_NativeTypeById;

        static void EnsureNativeTable()
        {
            var version = ProjectInfo.UnityVersion;
            if (s_NativeIdByName != null && s_NativeTableVersion == version) return;
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            var byId = new Dictionary<int, Type>();
            var table = ClassCoverage.UnityTable(version);
            if (table != null)
            {
                var assemblyTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
                foreach (var t in typeof(Object).Assembly.GetTypes())
                    if (typeof(Object).IsAssignableFrom(t) && !assemblyTypes.ContainsKey(t.Name))
                        assemblyTypes[t.Name] = t;
                foreach (var pair in table)
                {
                    if (!byName.ContainsKey(pair.Value.Name)) byName[pair.Value.Name] = pair.Key;
                    if (assemblyTypes.TryGetValue(pair.Value.Name, out var managed)) byId[pair.Key] = managed;
                }
            }
            s_NativeTableVersion = version;
            s_NativeIdByName = byName;
            s_NativeTypeById = byId;
        }

        static int NativeClassIdOf(Type type)
        {
            int id = ClassIds.IdOf(type);
            if (id > 0) return id;
            EnsureNativeTable();
            return s_NativeIdByName.TryGetValue(type.Name, out id) ? id : 0;
        }

        static Type NativeTypeOf(int classId)
        {
            var type = ClassIds.TypeOf(classId);
            if (type != null) return type;
            EnsureNativeTable();
            return s_NativeTypeById.TryGetValue(classId, out type) ? type : null;
        }

        static Type ResolveManagedType(string fullName)
        {
            if (typeof(Object).Assembly.GetType(fullName) is Type builtIn) return builtIn;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetType(fullName) is Type found) return found;
            return null;
        }
    }

    /// <summary>A default preset of the Preset Manager: its filter, preset and whether it applies
    /// (Unity's DefaultPreset, as its published C# declares it; a default one is enabled).</summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public partial struct DefaultPreset
    {
        [SerializeField]
        [Obsolete("Use the new getter/setter instead. (UnityUpgradable) -> filter")]
        public string m_Filter;
        [SerializeField]
        [Obsolete("Use the new getter/setter instead. (UnityUpgradable) -> preset")]
        public Preset m_Preset;
        [SerializeField]
        bool m_Disabled;

#pragma warning disable 618
        public string filter { get => m_Filter; set => m_Filter = value; }
        public Preset preset { get => m_Preset; set => m_Preset = value; }
#pragma warning restore 618
        public bool enabled { get => !m_Disabled; set => m_Disabled = !value; }

        public DefaultPreset(string filter, Preset preset) : this(filter, preset, true) { }

        public DefaultPreset(string filter, Preset preset, bool enabled)
        {
#pragma warning disable 618
            m_Filter = filter;
            m_Preset = preset;
#pragma warning restore 618
            m_Disabled = !enabled;
        }
    }

    /// <summary>
    /// A preset asset: the type it applies to and the property values it holds (matches the Unity
    /// 6000.0.71f1 Editor, tests/projects/presets asset.*, made.*, apply.*). Its default presets are
    /// the Preset Manager's (ProjectSettings/PresetManager.asset):
    /// <see cref="GetDefaultPresetsForObject"/>. Creating and applying presets read and write through
    /// <see cref="SerializedObject"/>; types whose properties Unbound's SerializedObject does not
    /// cover cannot apply those unmapped properties.
    /// </summary>
    [global::UnityEngine.@ExcludeFromPresetAttribute]
    [ClassId(181963792)]
    public sealed partial class Preset : Object
    {
        static readonly HashSet<string> s_SkippedRoots = new HashSet<string>(StringComparer.Ordinal)
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset",
            "m_GameObject", "m_Name", "m_Script", "m_Children", "m_Father",
        };

        PresetType m_TargetType;
        PropertyModification[] m_Properties = Array.Empty<PropertyModification>();
        string[] m_ExcludedProperties = Array.Empty<string>();

        internal Preset() { }

        public Preset(Object source)
        {
            if (!source) throw ObjectErrors.Argument(source, "source");
            var type = new PresetType(source);
            if (!type.IsValid()) throw new NotSupportedException($"Preset cannot be created from {source.GetType().FullName}.");
            m_TargetType = type;
            m_Properties = CaptureProperties(source);
        }

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Name = body.Str("m_Name");
            m_TargetType = PresetType.Read(body.Get("m_TargetType"), resolver);
            var properties = new List<PropertyModification>();
            foreach (var p in body.Get("m_Properties")?.Seq() ?? Array.Empty<YNode>())
            {
                var reference = p.Ref("objectReference");
                properties.Add(new PropertyModification
                {
                    target = null,
                    propertyPath = p.Str("propertyPath"),
                    value = p.Str("value"),
                    objectReference = reference.IsNull ? null : resolver.Resolve(reference, typeof(Object)),
                });
            }
            m_Properties = properties.ToArray();
            var excluded = new List<string>();
            foreach (var e in body.Get("m_ExcludedProperties")?.Seq() ?? Array.Empty<YNode>()) excluded.Add(e.AsString());
            m_ExcludedProperties = excluded.ToArray();
        }

        public PropertyModification[] PropertyModifications
        {
            get
            {
                var copy = new PropertyModification[m_Properties.Length];
                for (int i = 0; i < copy.Length; i++)
                {
                    var p = m_Properties[i];
                    copy[i] = new PropertyModification { target = p.target, propertyPath = p.propertyPath, value = p.value, objectReference = p.objectReference };
                }
                return copy;
            }
        }

        public string[] excludedProperties
        {
            get => (string[])m_ExcludedProperties.Clone();
            set => m_ExcludedProperties = value == null ? Array.Empty<string>() : (string[])value.Clone();
        }

        public bool ApplyTo(Object target) => ApplyTo(target, Array.Empty<string>());

        public bool ApplyTo(Object target, string[] selectedPropertyPaths)
        {
            if (!target) throw ObjectErrors.Argument(target, "target");
            if (!CanBeAppliedTo(target)) return false;
            using var so = new SerializedObject(target);
            bool filterSelected = selectedPropertyPaths != null && selectedPropertyPaths.Length > 0;
            bool anyWritten = false;
            int skipped = 0;
            foreach (var m in m_Properties)
            {
                if (m == null || string.IsNullOrEmpty(m.propertyPath)) continue;
                if (IsExcluded(m.propertyPath)) continue;
                if (filterSelected && !MatchesAny(m.propertyPath, selectedPropertyPaths)) continue;
                if (ApplyModification(so, m)) anyWritten = true;
                else if (!(target is Material && ((m.propertyPath == "m_Parent" && m.objectReference == null)
                    || (m.propertyPath == "m_ModifiedSerializedProperties" && (string.IsNullOrEmpty(m.value) || m.value == "0")))))
                    skipped++;
            }
            if (skipped > 0)
                Stubs.Ignored($"UnityEditor.Presets.Preset.ApplyTo ({target.GetType().FullName})", $"skipped {skipped} serialized properties not mapped in SerializedObject yet");
            if (anyWritten) so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        public bool DataEquals(Object target)
        {
            if (!target) throw ObjectErrors.Argument(target, "target");
            if (!CanBeAppliedTo(target)) return false;
            var actual = CaptureProperties(target);
            var expected = new Dictionary<string, PropertyModification>(StringComparer.Ordinal);
            foreach (var m in m_Properties)
                if (m != null && !string.IsNullOrEmpty(m.propertyPath) && !IsExcluded(m.propertyPath))
                    expected[m.propertyPath] = m;
            var current = new Dictionary<string, PropertyModification>(StringComparer.Ordinal);
            foreach (var m in actual)
                if (m != null && !string.IsNullOrEmpty(m.propertyPath) && !IsExcluded(m.propertyPath))
                    current[m.propertyPath] = m;
            if (expected.Count != current.Count) return false;
            foreach (var pair in expected)
            {
                if (!current.TryGetValue(pair.Key, out var other)) return false;
                if ((pair.Value.value ?? "") != (other.value ?? "") || pair.Value.objectReference != other.objectReference)
                    return false;
            }
            return true;
        }

        public bool UpdateProperties(Object source)
        {
            if (!source) throw ObjectErrors.Argument(source, "source");
            if (!CanBeAppliedTo(source)) return false;
            m_Properties = CaptureProperties(source);
            return true;
        }

        public PresetType GetPresetType() => m_TargetType;

        // As Unity's published C# for Preset computes them.
        public string GetTargetFullTypeName() => GetPresetType().GetManagedTypeName();

        public string GetTargetTypeName()
        {
            string full = GetTargetFullTypeName();
            int lastDot = full.LastIndexOf(".", StringComparison.Ordinal);
            if (lastDot == -1) lastDot = full.LastIndexOf(":", StringComparison.Ordinal);
            return lastDot != -1 ? full.Substring(lastDot + 1) : full;
        }

        public bool IsValid() => m_TargetType.IsValid();

        internal bool IsCoupled() => false;

        public bool CanBeAppliedTo(Object target) => target != null && IsValid() && new PresetType(target) == m_TargetType;

        [Unbound.Stub]
        internal Object GetReferenceObject()
        {
            Stubs.NotImplemented("UnityEditor.Presets.Preset.GetReferenceObject");
            return null;
        }

        /// <summary>The Preset Manager's enabled presets for the target's type whose filter takes it,
        /// in their order (tests/projects/presets defaults.*).</summary>
        public static Preset[] GetDefaultPresetsForObject(Object target)
        {
            if (!target) throw ObjectErrors.Argument(target, "target");
            var type = new PresetType(target);
            var found = new List<Preset>();
            foreach (var d in GetDefaultPresetsForType(type))
                if (d.enabled && d.preset != null && d.preset.IsValid() && FilterTakes(d.filter, target)) found.Add(d.preset);
            return found.ToArray();
        }

        [Obsolete("Use GetDefaultPresetsForObject to get the full ordered list of default Presets that would be applied to that Object")]
        public static Preset GetDefaultForObject(Object target)
        {
            var defaults = GetDefaultPresetsForObject(target);
            return defaults.Length > 0 ? defaults[0] : null;
        }

        [Obsolete("Use GetDefaultPresetsForType to get the full list of default Presets for a given PresetType.")]
        public static Preset GetDefaultForPreset(Preset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            foreach (var d in GetDefaultPresetsForType(preset.GetPresetType()))
                if (d.filter == string.Empty) return d.preset;
            return null;
        }

        public static PresetType[] GetAllDefaultTypes()
        {
            var types = new List<PresetType>();
            foreach (var (type, _) in ManagerDefaults()) types.Add(type);
            return types.ToArray();
        }

        /// <summary>The Preset Manager's entries for <paramref name="type"/>, disabled ones too, in
        /// its order (tests/projects/presets defaultsForType.*).</summary>
        public static DefaultPreset[] GetDefaultPresetsForType(PresetType type)
        {
            foreach (var (listed, presets) in ManagerDefaults())
                if (listed == type) return presets.ToArray();
            return Array.Empty<DefaultPreset>();
        }

        [Unbound.Stub]
        public static bool SetDefaultPresetsForType(PresetType type, DefaultPreset[] presets)
        {
            Stubs.NotImplemented("UnityEditor.Presets.Preset.SetDefaultPresetsForType");
            return false;
        }

        [Obsolete("Use SetDefaultPresetsForType instead.")]
        public static bool SetAsDefault(Preset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            return SetDefaultPresetsForType(preset.GetPresetType(), new[] { new DefaultPreset(string.Empty, preset) });
        }

        public static void RemoveFromDefault(Preset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            var type = preset.GetPresetType();
            var list = GetDefaultPresetsForType(type);
            var kept = new List<DefaultPreset>();
            foreach (var d in list) if (d.preset != preset) kept.Add(d);
            if (kept.Count != list.Length) SetDefaultPresetsForType(type, kept.ToArray());
        }

        [Obsolete("Use PresetType.IsValidDefault instead.")]
        public static bool IsPresetExcludedFromDefaultPresets(Preset preset)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            return !preset.GetPresetType().IsValidDefault();
        }

        [Obsolete("Use PresetType.IsValidDefault instead.")]
        public static bool IsObjectExcludedFromDefaultPresets(Object target) => !new PresetType(target).IsValidDefault();

        [Obsolete("Use PresetType.IsValid instead.")]
        public static bool IsObjectExcludedFromPresets(Object target) => !new PresetType(target).IsValid();

        // As Unity's published C# for Preset decides it.
        public static bool IsEditorTargetAPreset(Object target) =>
            target is Component comp ? ((int)comp.gameObject.hideFlags == 93) : !AssetDatabase.Contains(target);

        bool IsExcluded(string path) => m_ExcludedProperties.Length > 0 && MatchesAny(path, m_ExcludedProperties);

        static bool MatchesAny(string path, string[] prefixes)
        {
            foreach (var prefix in prefixes)
                if (!string.IsNullOrEmpty(prefix) && PrefabValues.Within(path, prefix)) return true;
            return false;
        }

        /// <summary>Reads an object's serialized leaf properties through SerializedObject, omitting
        /// identity and hierarchy headers (tests/projects/presets made.*).</summary>
        static PropertyModification[] CaptureProperties(Object source)
        {
            if (!SerializedTree.IsScript(source) && !(source is Transform) && !(source is BoxCollider) && TypeTreeTargets.Of(source) == null)
                Stubs.NotImplemented($"UnityEditor.Presets.Preset (serialized properties of {source.GetType().FullName})");
            var list = new List<PropertyModification>();
            using var so = new SerializedObject(source);
            var it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                if (it.depth == 0 && s_SkippedRoots.Contains(it.name))
                {
                    enter = false;
                    continue;
                }
                if (it.propertyType == SerializedPropertyType.String)
                {
                    list.Add(new PropertyModification { target = null, propertyPath = it.propertyPath, value = it.stringValue ?? "", objectReference = null });
                    enter = false;
                    continue;
                }
                if (it.propertyType == SerializedPropertyType.ObjectReference)
                {
                    list.Add(new PropertyModification { target = null, propertyPath = it.propertyPath, value = "", objectReference = it.objectReferenceValue });
                    enter = false;
                    continue;
                }
                if (it.propertyType == SerializedPropertyType.ArraySize)
                {
                    list.Add(new PropertyModification { target = null, propertyPath = it.propertyPath, value = it.intValue.ToString(CultureInfo.InvariantCulture), objectReference = null });
                    enter = false;
                    continue;
                }
                if (it.hasChildren)
                {
                    enter = true;
                    continue;
                }
                enter = false;
                string value = it.propertyType switch
                {
                    SerializedPropertyType.Boolean => it.boolValue ? "1" : "0",
                    SerializedPropertyType.Float => it.numericType == SerializedPropertyNumericType.Double
                        ? FloatText.Format(it.doubleValue)
                        : FloatText.Format(it.floatValue),
                    SerializedPropertyType.Integer or SerializedPropertyType.Enum or SerializedPropertyType.LayerMask or SerializedPropertyType.Character =>
                        it.numericType == SerializedPropertyNumericType.UInt64
                            ? it.ulongValue.ToString(CultureInfo.InvariantCulture)
                            : it.numericType == SerializedPropertyNumericType.UInt32
                                ? it.uintValue.ToString(CultureInfo.InvariantCulture)
                                : it.longValue.ToString(CultureInfo.InvariantCulture),
                    _ => "",
                };
                list.Add(new PropertyModification { target = null, propertyPath = it.propertyPath, value = value, objectReference = null });
            }
            return list.ToArray();
        }

        static bool ApplyModification(SerializedObject so, PropertyModification m)
        {
            const string arraySizeSuffix = ".Array.size";
            if (m.propertyPath.EndsWith(arraySizeSuffix, StringComparison.Ordinal))
            {
                var arrayPath = m.propertyPath.Substring(0, m.propertyPath.Length - arraySizeSuffix.Length);
                var arrayProp = so.FindProperty(arrayPath);
                if (arrayProp == null || !int.TryParse(m.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) || size < 0)
                    return false;
                arrayProp.arraySize = size;
                return true;
            }
            var prop = so.FindProperty(m.propertyPath);
            if (prop == null) return false;
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    prop.boolValue = m.value == "1" || string.Equals(m.value, "true", StringComparison.OrdinalIgnoreCase);
                    return true;
                case SerializedPropertyType.Float:
                    if (prop.numericType == SerializedPropertyNumericType.Double)
                    {
                        if (!double.TryParse(m.value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return false;
                        prop.doubleValue = d;
                    }
                    else
                    {
                        if (!float.TryParse(m.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return false;
                        prop.floatValue = f;
                    }
                    return true;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    if (prop.numericType == SerializedPropertyNumericType.UInt64)
                    {
                        if (!ulong.TryParse(m.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong ul)) return false;
                        prop.ulongValue = ul;
                    }
                    else if (prop.numericType == SerializedPropertyNumericType.UInt32)
                    {
                        if (!uint.TryParse(m.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ui)) return false;
                        prop.uintValue = ui;
                    }
                    else
                    {
                        if (!long.TryParse(m.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return false;
                        prop.longValue = l;
                    }
                    return true;
                case SerializedPropertyType.String:
                    prop.stringValue = m.value ?? "";
                    return true;
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = m.objectReference;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The Preset Manager's default presets by type, as ProjectSettings/PresetManager.asset
        /// lists them (serializedVersion 2 <c>m_DefaultPresets</c> or v1 <c>m_DefaultList</c>).</summary>
        static List<(PresetType Type, List<DefaultPreset> Presets)> ManagerDefaults()
        {
            var result = new List<(PresetType, List<DefaultPreset>)>();
            var body = SettingsAssets.AtPath("ProjectSettings/PresetManager.asset")?.Body?.ToNode();
            if (body == null) return result;
            var resolver = new AssetReferences();
            var entries = new List<YNode>();
            foreach (var n in body.Get("m_DefaultPresets")?.Seq() ?? Array.Empty<YNode>()) entries.Add(n);
            string typeKey = "first", listKey = "second";
            if (entries.Count == 0)
            {
                foreach (var n in body.Get("m_DefaultList")?.Seq() ?? Array.Empty<YNode>()) entries.Add(n);
                typeKey = "type";
                listKey = "defaultPresets";
            }
            foreach (var entry in entries)
            {
                var presets = new List<DefaultPreset>();
                foreach (var d in entry.Get(listKey)?.Seq() ?? Array.Empty<YNode>())
                {
                    var reference = d.Ref("m_Preset");
                    var preset = reference.IsNull ? null : resolver.Resolve(reference, typeof(Preset)) as Preset;
                    presets.Add(new DefaultPreset(d.Str("m_Filter"), preset, d.Int("m_Disabled") == 0));
                }
                result.Add((PresetType.Read(entry.Get(typeKey), resolver), presets));
            }
            return result;
        }

        /// <summary>Whether a default preset's filter takes <paramref name="target"/>: an empty one
        /// takes every object; <c>glob:"pattern"</c> one whose name (a component's GameObject's)
        /// the pattern matches (matches the Unity 6000.0.71f1 Editor, tests/projects/presets
        /// defaults.NamedTransform, defaults.OtherTransform; the case of the letters, an importer's
        /// path and other search filters are not measured: such a filter is said and takes none).</summary>
        static bool FilterTakes(string filter, Object target)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            const string glob = "glob:";
            if (filter.StartsWith(glob, StringComparison.Ordinal) && !(target is AssetImporter))
            {
                var pattern = filter.Substring(glob.Length).Trim().Trim('"');
                var name = target is Component component ? component.gameObject.name : target.name;
                return Regex.IsMatch(name ?? "", "^" + GlobToRegex(pattern) + "$");
            }
            Stubs.NotImplemented("UnityEditor.Presets.Preset.GetDefaultPresetsForObject (a filter other than glob:\"...\" on an object's name)");
            return false;
        }

        /// <summary>References to assets, as a settings file holds them.</summary>
        sealed class AssetReferences : IReferenceResolver
        {
            public Object Resolve(ObjectRef reference, Type expected)
            {
                if (reference.IsNull || reference.Guid == null) return null;
                try { return AssetDatabaseRuntime.Load(reference.Guid, reference.FileId, expected ?? typeof(Object)); }
                catch (InvalidOperationException) { return null; }
            }
        }

        static string GlobToRegex(string pattern)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*') { sb.Append(".*"); i++; }
                else if (c == '*') sb.Append("[^/]*");
                else if (c == '?') sb.Append("[^/]");
                else sb.Append(Regex.Escape(c.ToString()));
            }
            return sb.ToString();
        }
    }
}
