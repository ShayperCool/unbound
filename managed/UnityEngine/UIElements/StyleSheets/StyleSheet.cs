using System;
using System.Collections.Generic;
using UnityEngine.UIElements.StyleSheets;

// A style sheet as Unity's importers store it: rules of properties whose values are handles into
// typed value tables, and complex selectors pointing at their rules. The serialized fields keep
// Unity's names, so the Editor's built-in sheets (the default theme in "unity editor resources")
// deserialize into the same objects Unbound's USS parser builds.

namespace UnityEngine.UIElements
{
    [Serializable]
    public partial class StyleSheet : ScriptableObject
    {
        [Serializable]
        internal partial struct ImportStruct
        {
            public StyleSheet styleSheet;
            public string[] mediaQueries;
        }

        internal enum OrderedSelectorType
        {
            None = -1,
            Name = 0,
            Type = 1,
            Class = 2,
            Length = 3,
        }

        [SerializeField] bool m_ImportedWithErrors;
        [SerializeField] bool m_ImportedWithWarnings;
        [SerializeField] StyleRule[] m_Rules;
        [SerializeField] StyleComplexSelector[] m_ComplexSelectors;
        [SerializeField] internal float[] floats;
        [SerializeField] internal Dimension[] dimensions;
        [SerializeField] internal Color[] colors;
        [SerializeField] internal string[] strings;
        [SerializeField] internal Object[] assets;
        [SerializeField] internal ImportStruct[] imports;
        [SerializeField] List<StyleSheet> m_FlattenedImportedStyleSheets;
        [SerializeField] int m_ContentHash;
        [SerializeField] internal ScalableImage[] scalableImages;

        [NonSerialized] internal Dictionary<string, StyleComplexSelector>[] tables;
        [NonSerialized] internal int nonEmptyTablesMask;
        [NonSerialized] internal StyleComplexSelector firstRootSelector;
        [NonSerialized] internal StyleComplexSelector firstWildCardSelector;
        [NonSerialized] bool m_IsDefaultStyleSheet;
        [NonSerialized] bool m_ReferencesSetUp;

        public StyleSheet() { }

        public bool importedWithErrors { get => m_ImportedWithErrors; internal set => m_ImportedWithErrors = value; }
        public bool importedWithWarnings { get => m_ImportedWithWarnings; internal set => m_ImportedWithWarnings = value; }
        public int contentHash { get => m_ContentHash; set => m_ContentHash = value; }

        internal StyleRule[] rules { get => m_Rules; set { m_Rules = value; SetupReferences(); } }
        internal StyleComplexSelector[] complexSelectors { get => m_ComplexSelectors; set { m_ComplexSelectors = value; SetupReferences(); } }
        internal List<StyleSheet> flattenedRecursiveImports => m_FlattenedImportedStyleSheets;

        internal bool isDefaultStyleSheet
        {
            get => m_IsDefaultStyleSheet;
            set
            {
                m_IsDefaultStyleSheet = value;
                if (m_FlattenedImportedStyleSheets == null) return;
                foreach (var sheet in m_FlattenedImportedStyleSheets)
                    if (sheet != null) sheet.isDefaultStyleSheet = value;
            }
        }

        internal virtual void OnEnable() => SetupReferences();

        /// <summary>The lookup tables, built once the sheet's data is complete (after deserialization
        /// the loader calls this; Unity does it in OnEnable).</summary>
        internal void EnsureReferences()
        {
            if (!m_ReferencesSetUp) SetupReferences();
        }

        internal void FlattenImportedStyleSheetsRecursive()
        {
            m_FlattenedImportedStyleSheets = new List<StyleSheet>();
            Flatten(this);
        }

        void Flatten(StyleSheet sheet)
        {
            if (sheet.imports == null) return;
            foreach (var import in sheet.imports)
            {
                var imported = import.styleSheet;
                if (imported == null) continue;
                imported.isDefaultStyleSheet = isDefaultStyleSheet;
                Flatten(imported);
                m_FlattenedImportedStyleSheets.Add(imported);
            }
        }

        void SetupReferences()
        {
            if (m_ComplexSelectors == null || m_Rules == null) return;
            m_ReferencesSetUp = true;
            foreach (var rule in m_Rules)
            {
                rule.customPropertiesCount = 0;
                if (rule.properties == null) continue;
                foreach (var property in rule.properties)
                {
                    if (property.name != null && property.name.StartsWith("--", StringComparison.Ordinal))
                    {
                        rule.customPropertiesCount++;
                        property.isCustomProperty = true;
                    }
                    if (property.values == null) continue;
                    foreach (var handle in property.values)
                    {
                        if (handle.IsVarFunction())
                        {
                            property.requireVariableResolve = true;
                            break;
                        }
                    }
                }
            }
            foreach (var selector in m_ComplexSelectors) selector.CachePseudoStateMasks();

            tables = new Dictionary<string, StyleComplexSelector>[(int)OrderedSelectorType.Length];
            for (int i = 0; i < tables.Length; i++) tables[i] = new Dictionary<string, StyleComplexSelector>(StringComparer.Ordinal);
            nonEmptyTablesMask = 0;
            firstRootSelector = null;
            firstWildCardSelector = null;
            for (int i = 0; i < m_ComplexSelectors.Length; i++)
            {
                var complex = m_ComplexSelectors[i];
                complex.nextInTable = null;
                if (complex.ruleIndex < m_Rules.Length) complex.rule = m_Rules[complex.ruleIndex];
                complex.orderInStyleSheet = i;
                var last = complex.selectors[complex.selectors.Length - 1];
                var part = last.parts[0];
                var table = OrderedSelectorType.None;
                switch (part.type)
                {
                    case StyleSelectorType.Class: table = OrderedSelectorType.Class; break;
                    case StyleSelectorType.ID: table = OrderedSelectorType.Name; break;
                    case StyleSelectorType.Type: table = OrderedSelectorType.Type; break;
                    case StyleSelectorType.Wildcard:
                        complex.nextInTable = firstWildCardSelector;
                        firstWildCardSelector = complex;
                        break;
                    case StyleSelectorType.PseudoClass:
                        if ((last.pseudoStateMask & (int)PseudoStates.Root) != 0)
                        {
                            complex.nextInTable = firstRootSelector;
                            firstRootSelector = complex;
                        }
                        else
                        {
                            complex.nextInTable = firstWildCardSelector;
                            firstWildCardSelector = complex;
                        }
                        break;
                    default:
                        Debug.LogError($"Invalid first part type {part.type}", this);
                        break;
                }
                if (table != OrderedSelectorType.None)
                {
                    var dictionary = tables[(int)table];
                    if (dictionary.TryGetValue(part.value, out var previous)) complex.nextInTable = previous;
                    nonEmptyTablesMask |= 1 << (int)table;
                    dictionary[part.value] = complex;
                }
            }
        }

        bool Check<T>(T[] list, StyleValueType type, StyleValueHandle handle, out T value)
        {
            value = default;
            if (handle.valueType == type && list != null && handle.valueIndex >= 0 && handle.valueIndex < list.Length)
            {
                value = list[handle.valueIndex];
                return true;
            }
            return false;
        }

        T Access<T>(T[] list, StyleValueType type, StyleValueHandle handle)
        {
            if (handle.valueType != type)
            {
                Debug.LogErrorFormat(this, "Trying to read value of type {0} while reading a value of type {1}", type, handle.valueType);
                return default;
            }
            if (list != null && handle.valueIndex >= 0 && handle.valueIndex < list.Length) return list[handle.valueIndex];
            Debug.LogError("Accessing invalid property", this);
            return default;
        }

        internal StyleValueKeyword ReadKeyword(StyleValueHandle handle) => (StyleValueKeyword)handle.valueIndex;
        internal float ReadFloat(StyleValueHandle handle) =>
            handle.valueType == StyleValueType.Dimension ? Access(dimensions, StyleValueType.Dimension, handle).value : Access(floats, StyleValueType.Float, handle);
        internal bool TryReadFloat(StyleValueHandle handle, out float value)
        {
            if (Check(floats, StyleValueType.Float, handle, out value)) return true;
            bool ok = Check(dimensions, StyleValueType.Dimension, handle, out var d);
            value = d.value;
            return ok;
        }
        internal Dimension ReadDimension(StyleValueHandle handle) =>
            handle.valueType == StyleValueType.Float ? new Dimension(Access(floats, StyleValueType.Float, handle), Dimension.Unit.Unitless) : Access(dimensions, StyleValueType.Dimension, handle);
        internal bool TryReadDimension(StyleValueHandle handle, out Dimension value)
        {
            if (Check(dimensions, StyleValueType.Dimension, handle, out value)) return true;
            bool ok = Check(floats, StyleValueType.Float, handle, out var f);
            value = new Dimension(f, Dimension.Unit.Unitless);
            return ok;
        }
        internal Color ReadColor(StyleValueHandle handle) => Access(colors, StyleValueType.Color, handle);
        internal bool TryReadColor(StyleValueHandle handle, out Color value) => Check(colors, StyleValueType.Color, handle, out value);
        internal string ReadString(StyleValueHandle handle) => Access(strings, StyleValueType.String, handle);
        internal bool TryReadString(StyleValueHandle handle, out string value) => Check(strings, StyleValueType.String, handle, out value);
        internal string ReadEnum(StyleValueHandle handle) => Access(strings, StyleValueType.Enum, handle);
        internal bool TryReadEnum(StyleValueHandle handle, out string value) => Check(strings, StyleValueType.Enum, handle, out value);
        internal string ReadVariable(StyleValueHandle handle) => Access(strings, StyleValueType.Variable, handle);
        internal bool TryReadVariable(StyleValueHandle handle, out string value) => Check(strings, StyleValueType.Variable, handle, out value);
        internal string ReadResourcePath(StyleValueHandle handle) => Access(strings, StyleValueType.ResourcePath, handle);
        internal bool TryReadResourcePath(StyleValueHandle handle, out string value) => Check(strings, StyleValueType.ResourcePath, handle, out value);
        internal Object ReadAssetReference(StyleValueHandle handle) => Access(assets, StyleValueType.AssetReference, handle);
        internal string ReadMissingAssetReferenceUrl(StyleValueHandle handle) => Access(strings, StyleValueType.MissingAssetReference, handle);
        internal bool TryReadAssetReference(StyleValueHandle handle, out Object value) => Check(assets, StyleValueType.AssetReference, handle, out value);
        internal StyleValueFunction ReadFunction(StyleValueHandle handle) => (StyleValueFunction)handle.valueIndex;
        internal ScalableImage ReadScalableImage(StyleValueHandle handle) => Access(scalableImages, StyleValueType.ScalableImage, handle);

        /// <summary>A value as USS text (enums, strings, numbers), for keyword and enum lookups.</summary>
        internal string ReadAsString(StyleValueHandle handle)
        {
            switch (handle.valueType)
            {
                case StyleValueType.Keyword: return Unbound.UIElements.StyleKeywords.ToUss(ReadKeyword(handle));
                case StyleValueType.Float: return Unbound.MonoNumberFormat.Format(ReadFloat(handle), null, System.Globalization.CultureInfo.InvariantCulture);
                case StyleValueType.Dimension: return ReadDimension(handle).ToString();
                case StyleValueType.Color: return ReadColor(handle).ToString();
                case StyleValueType.ResourcePath: return ReadResourcePath(handle);
                case StyleValueType.AssetReference: return ReadAssetReference(handle)?.ToString();
                case StyleValueType.Enum: return ReadEnum(handle);
                case StyleValueType.Variable: return ReadVariable(handle);
                case StyleValueType.String: return ReadString(handle);
                case StyleValueType.Function: return ReadFunction(handle).ToString().ToLowerInvariant();
                case StyleValueType.CommaSeparator: return ",";
                case StyleValueType.MissingAssetReference: return ReadMissingAssetReferenceUrl(handle);
            }
            return "Error reading value type (" + handle.valueType + ") at index " + handle.valueIndex;
        }
    }

    public partial class ThemeStyleSheet : StyleSheet
    {
        public ThemeStyleSheet() { }
        internal override void OnEnable() => base.OnEnable();
    }

    [Serializable]
    internal partial class StyleRule
    {
        [SerializeField] StyleProperty[] m_Properties;
        [SerializeField] internal int line;
        [NonSerialized] internal int customPropertiesCount;

        public StyleProperty[] properties { get => m_Properties; internal set => m_Properties = value; }
    }

    [Serializable]
    internal partial class StyleProperty
    {
        [SerializeField] string m_Name;
        [SerializeField] int m_Line;
        [SerializeField] StyleValueHandle[] m_Values;
        [NonSerialized] internal bool isCustomProperty;
        [NonSerialized] internal bool requireVariableResolve;

        public string name { get => m_Name; internal set => m_Name = value; }
        public int line { get => m_Line; internal set => m_Line = value; }
        public StyleValueHandle[] values { get => m_Values; internal set => m_Values = value; }
    }

    [Serializable]
    internal partial struct StyleValueHandle
    {
        [SerializeField] StyleValueType m_ValueType;
        [SerializeField] internal int valueIndex;

        public StyleValueType valueType { get => m_ValueType; internal set => m_ValueType = value; }

        internal StyleValueHandle(int valueIndex, StyleValueType valueType)
        {
            this.valueIndex = valueIndex;
            m_ValueType = valueType;
        }

        internal bool IsVarFunction() => m_ValueType == StyleValueType.Function && valueIndex == (int)StyleValueFunction.Var;
    }

    [Serializable]
    internal partial class StyleComplexSelector : ISerializationCallbackReceiver
    {
        [SerializeField] int m_Specificity;
        [SerializeField] StyleSelector[] m_Selectors;
        [SerializeField] internal int ruleIndex;
        [NonSerialized] internal StyleComplexSelector nextInTable;
        [NonSerialized] internal int orderInStyleSheet;

        static Dictionary<string, (PseudoStates State, bool Negate)> s_PseudoStates;

        public int specificity { get => m_Specificity; internal set => m_Specificity = value; }
        public StyleRule rule { get; internal set; }
        public bool isSimple => m_Selectors != null && m_Selectors.Length == 1;
        public StyleSelector[] selectors { get => m_Selectors; internal set => m_Selectors = value; }

        [Unbound.EditorFaithful("Unity's is empty too")]
        public void OnBeforeSerialize() { }
        [Unbound.EditorFaithful("Unity's caches isSimple here; it is computed when asked")]
        public virtual void OnAfterDeserialize() { }

        internal void CachePseudoStateMasks()
        {
            if (s_PseudoStates == null)
            {
                s_PseudoStates = new Dictionary<string, (PseudoStates, bool)>
                {
                    ["active"] = (PseudoStates.Active, false),
                    ["hover"] = (PseudoStates.Hover, false),
                    ["checked"] = (PseudoStates.Checked, false),
                    ["selected"] = (PseudoStates.Checked, false),
                    ["disabled"] = (PseudoStates.Disabled, false),
                    ["focus"] = (PseudoStates.Focus, false),
                    ["root"] = (PseudoStates.Root, false),
                    ["inactive"] = (PseudoStates.Active, true),
                    ["enabled"] = (PseudoStates.Disabled, true),
                };
            }
            foreach (var selector in m_Selectors)
            {
                PseudoStates mask = 0, negated = 0;
                foreach (var part in selector.parts)
                {
                    if (part.type != StyleSelectorType.PseudoClass) continue;
                    if (s_PseudoStates.TryGetValue(part.value, out var state))
                    {
                        if (state.Negate) negated |= state.State;
                        else mask |= state.State;
                    }
                    else
                    {
                        Debug.LogWarningFormat("Unknown pseudo class \"{0}\"", part.value);
                    }
                }
                selector.pseudoStateMask = (int)mask;
                selector.negatedPseudoStateMask = (int)negated;
            }
        }

        public override string ToString() => "[" + string.Join(", ", Array.ConvertAll(m_Selectors, s => s.ToString())) + "]";
    }

    [Serializable]
    internal partial class StyleSelector
    {
        [SerializeField] StyleSelectorPart[] m_Parts;
        [SerializeField] StyleSelectorRelationship m_PreviousRelationship;
        internal int pseudoStateMask = -1;
        internal int negatedPseudoStateMask = -1;

        public StyleSelectorPart[] parts { get => m_Parts; internal set => m_Parts = value; }
        public StyleSelectorRelationship previousRelationship { get => m_PreviousRelationship; internal set => m_PreviousRelationship = value; }

        public override string ToString() => string.Join(", ", Array.ConvertAll(m_Parts, p => p.ToString()));
    }

    [Serializable]
    internal partial struct StyleSelectorPart
    {
        [SerializeField] string m_Value;
        [SerializeField] StyleSelectorType m_Type;
        internal object tempData;

        public string value { get => m_Value; internal set => m_Value = value; }
        public StyleSelectorType type { get => m_Type; internal set => m_Type = value; }

        public override string ToString() => $"[StyleSelectorPart: value={value}, type={type}]";

        public static StyleSelectorPart CreateClass(string className) => new StyleSelectorPart { m_Type = StyleSelectorType.Class, m_Value = className };
        public static StyleSelectorPart CreatePseudoClass(string className) => new StyleSelectorPart { m_Type = StyleSelectorType.PseudoClass, m_Value = className };
        public static StyleSelectorPart CreateId(string Id) => new StyleSelectorPart { m_Type = StyleSelectorType.ID, m_Value = Id };
        public static StyleSelectorPart CreateType(Type t) => new StyleSelectorPart { m_Type = StyleSelectorType.Type, m_Value = t.Name };
        public static StyleSelectorPart CreateType(string typeName) => new StyleSelectorPart { m_Type = StyleSelectorType.Type, m_Value = typeName };
        public static StyleSelectorPart CreatePredicate(object predicate) => new StyleSelectorPart { m_Type = StyleSelectorType.Predicate, tempData = predicate };
        public static StyleSelectorPart CreateWildCard() => new StyleSelectorPart { m_Type = StyleSelectorType.Wildcard };
    }
}

namespace UnityEngine.UIElements.StyleSheets
{
    [Serializable]
    internal partial struct Dimension : IEquatable<Dimension>
    {
        public enum Unit
        {
            Unitless = 0,
            Pixel = 1,
            Percent = 2,
            Second = 3,
            Millisecond = 4,
            Degree = 5,
            Gradian = 6,
            Radian = 7,
            Turn = 8,
        }

        public Unit unit;
        public float value;

        public Dimension(float value, Unit unit)
        {
            this.unit = unit;
            this.value = value;
        }

        public Length ToLength() => new Length(value, unit == Unit.Percent ? LengthUnit.Percent : LengthUnit.Pixel);
        public TimeValue ToTime() => new TimeValue(value, unit == Unit.Millisecond ? TimeUnit.Millisecond : TimeUnit.Second);
        public Angle ToAngle() => unit switch
        {
            Unit.Gradian => new Angle(value, AngleUnit.Gradian),
            Unit.Radian => new Angle(value, AngleUnit.Radian),
            Unit.Turn => new Angle(value, AngleUnit.Turn),
            _ => new Angle(value, AngleUnit.Degree),
        };

        public static bool operator ==(Dimension lhs, Dimension rhs) => lhs.value == rhs.value && lhs.unit == rhs.unit;
        public static bool operator !=(Dimension lhs, Dimension rhs) => !(lhs == rhs);
        public bool Equals(Dimension other) => other == this;
        public override bool Equals(object obj) => obj is Dimension d && d == this;
        public override int GetHashCode()
        {
            unchecked
            {
                int h = -799583767;
                h = h * -1521134295 + unit.GetHashCode();
                return h * -1521134295 + value.GetHashCode();
            }
        }

        public override string ToString()
        {
            string suffix = unit switch
            {
                Unit.Pixel => "px",
                Unit.Percent => "%",
                Unit.Second => "s",
                Unit.Millisecond => "ms",
                Unit.Degree => "deg",
                Unit.Gradian => "grad",
                Unit.Radian => "rad",
                Unit.Turn => "turn",
                _ => "",
            };
            return Unbound.MonoNumberFormat.Format(value, null, System.Globalization.CultureInfo.InvariantCulture.NumberFormat) + suffix;
        }
    }

    [Serializable]
    internal partial struct ScalableImage
    {
        public Texture2D normalImage;
        public Texture2D highResolutionImage;

        public override string ToString() => $"[ScalableImage: normalImage={normalImage}, highResolutionImage={highResolutionImage}]";
    }
}
