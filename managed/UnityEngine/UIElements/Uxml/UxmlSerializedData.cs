using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine.Internal;
using UnityEngine.Serialization;
using Unbound.Runtime;
using Unbound.UIElements;

// Unity 6 UXML serialization core: UxmlSerializedData, ShouldWriteAttributeValue, attribute flag
// selection, description cache registration, serialized-value copying, and the base
// UxmlSerializedData classes that custom [UxmlElement] and [UxmlObject] controls inherit from.
// Documented (Unity 6000.0 Scripting API: UxmlSerializedData, ShouldWriteAttributeValue,
// UxmlAttributeFlags, UxmlAttributeNames, UxmlDescriptionCache, UxmlIgnoreAttribute,
// RegisterUxmlCacheAttribute, UxmlObjectReferenceAttribute, UxmlSerializedDataUtility) and
// measured on Unity 6000.0.71f1 (tests/projects/uxmlserializeddata).

namespace UnityEngine.UIElements
{
    [Serializable]
    public abstract partial class UxmlSerializedData
    {
        [Flags]
        public enum UxmlAttributeFlags : byte
        {
            Ignore = 0,
            OverriddenInUxml = 1,
            DefaultValue = 2,
        }

        internal const string AttributeFlagSuffix = "_UxmlAttributeFlags";
        const UxmlAttributeFlags k_DefaultFlags = UxmlAttributeFlags.OverriddenInUxml;

        [HideInInspector]
        [SerializeField]
        [UxmlIgnore]
        internal int uxmlAssetId;

        static UxmlAttributeFlags s_CurrentDeserializeFlags = k_DefaultFlags;

        protected UxmlSerializedData() { }

        [Unbound.EditorFaithful("Base hook generated for UxmlElement types; Unity 6000.0.71f1, tests/projects/uxmlserializeddata")]
        public static void Register() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldWriteAttributeValue(UxmlAttributeFlags attributeFlags) =>
            (attributeFlags & s_CurrentDeserializeFlags) != 0;

        public abstract object CreateInstance();

        public abstract void Deserialize(object obj);

        internal void Deserialize(object obj, UxmlAttributeFlags flags)
        {
            try
            {
                s_CurrentDeserializeFlags = flags;
                Deserialize(obj);
            }
            finally
            {
                s_CurrentDeserializeFlags = k_DefaultFlags;
            }
        }
    }

    public readonly partial struct UxmlAttributeNames
    {
        public readonly string fieldName;
        public readonly string uxmlName;
        public readonly Type typeReference;
        public readonly string[] obsoleteNames;

        public UxmlAttributeNames(string fieldName, string uxmlName, Type typeReference = null, params string[] obsoleteNames)
        {
            this.fieldName = fieldName;
            this.uxmlName = uxmlName;
            this.obsoleteNames = Compat.UxmlSerializedDataMeasured ? (obsoleteNames ?? Array.Empty<string>()) : obsoleteNames;
            this.typeReference = typeReference;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public partial class UxmlIgnoreAttribute : Attribute
    {
        public UxmlIgnoreAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public partial class RegisterUxmlCacheAttribute : Attribute
    {
        public RegisterUxmlCacheAttribute() { }
    }

    public static partial class UxmlDescriptionCache
    {
        static readonly Dictionary<Type, UxmlAttributeNames[]> s_NamesPerType = new Dictionary<Type, UxmlAttributeNames[]>();

        public static void RegisterType(Type type, UxmlAttributeNames[] attributeNames) =>
            s_NamesPerType[type] = attributeNames;

        internal static bool TryGetCachedDescription(Type type, out UxmlAttributeNames[] attributes) =>
            s_NamesPerType.TryGetValue(type, out attributes);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
    public partial class UxmlObjectReferenceAttribute : Attribute
    {
        public string name;
        public Type[] types;

        public UxmlObjectReferenceAttribute() : this(null, null) { }

        public UxmlObjectReferenceAttribute(string uxmlName) : this(uxmlName, null)
        {
            if (Compat.UxmlSerializedDataMeasured && uxmlName == "null")
                throw new ArgumentException("UxmlObjectReferenceAttribute name cannot be \"null\".");
        }

        public UxmlObjectReferenceAttribute(string uxmlName, params Type[] acceptedTypes)
        {
            name = uxmlName;
            types = acceptedTypes;
        }
    }

    internal partial interface IUxmlSerializedDataDeserializeReference
    {
        object DeserializeReference();
    }

    internal abstract partial class UxmlSerializableAdapterBase
    {
        protected UxmlSerializableAdapterBase() { }

        public abstract object dataBoxed { get; set; }

        public abstract object CloneInstanceBoxed(object value);
    }

    [Serializable]
    internal sealed partial class UxmlSerializableAdapter<T> : UxmlSerializableAdapterBase
    {
        public static readonly UxmlSerializableAdapter<T> SharedInstance = new UxmlSerializableAdapter<T>();
        public T data;

        public UxmlSerializableAdapter() { }

        public override object dataBoxed
        {
            get => data;
            set => data = (T)value;
        }

        public T CloneInstance(T value)
        {
            UxmlSerializableAdapter<T> copy = null;
            try
            {
                if (value is IUxmlSerializedDataDeserializeReference deserializeReference)
                    return (T)deserializeReference.DeserializeReference();
                data = value;
                var json = JsonUtility.ToJson(this);
                copy = JsonUtility.FromJson<UxmlSerializableAdapter<T>>(json);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                data = default;
            }
            return copy != null ? copy.data : default;
        }

        public override object CloneInstanceBoxed(object value) => CloneInstance((T)value);
    }

    public static partial class UxmlSerializedDataUtility
    {
        internal static Dictionary<Type, UxmlSerializableAdapterBase> s_Adapters = new Dictionary<Type, UxmlSerializableAdapterBase>();

        public static object CopySerialized(object value)
        {
            if (value == null) return null;
            try
            {
                var valueType = value.GetType();
                if (!s_Adapters.TryGetValue(valueType, out var adapter))
                {
                    var adapterType = typeof(UxmlSerializableAdapter<>).MakeGenericType(valueType);
                    var sharedField = adapterType.GetField(nameof(UxmlSerializableAdapter<object>.SharedInstance), BindingFlags.Static | BindingFlags.Public);
                    adapter = (UxmlSerializableAdapterBase)sharedField.GetValue(null);
                    s_Adapters[valueType] = adapter;
                }
                return adapter.CloneInstanceBoxed(value);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
        }

        public static T CopySerialized<T>(object value) =>
            UxmlSerializableAdapter<T>.SharedInstance.CloneInstance((T)value);
    }

    internal static partial class UxmlUtility
    {
        public static List<string> ParseStringListAttribute(string itemList)
        {
            if (string.IsNullOrEmpty(itemList?.Trim())) return null;
            var items = itemList.Split(',');
            if (items.Length == 0) return null;
            var list = new List<string>(items.Length);
            for (int i = 0; i < items.Length; i++) list.Add(items[i].Trim());
            return list;
        }

        public static Type ParseType(string value, Type defaultType = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(value))
                {
                    var direct = Type.GetType(value, throwOnError: false);
                    if (direct != null) return direct;
                    if (UxmlAttributeValues.TryReadTypeName(value, null, out var resolved, out _) && resolved is Type found)
                        return found;
                    return Type.GetType(value, throwOnError: true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            return defaultType;
        }

        public static string TypeToString(Type value) =>
            value == null ? null : value.FullName + ", " + value.Assembly.GetName().Name;
    }

    internal partial class UxmlAttributeBindingPathAttribute : Attribute
    {
        public string path { get; private set; }

        public UxmlAttributeBindingPathAttribute(string bindingPath) => path = bindingPath;
    }

    internal partial class BindingModeDrawerAttribute : PropertyAttribute
    {
        public BindingModeDrawerAttribute() { }
    }

    internal partial class BindingPathDrawerAttribute : PropertyAttribute
    {
        public BindingPathDrawerAttribute() { }
    }

    internal partial class ConverterDrawerAttribute : PropertyAttribute
    {
        public bool isConverterToSource;

        public ConverterDrawerAttribute() { }
    }

    internal partial class DataSourceDrawerAttribute : PropertyAttribute
    {
        public DataSourceDrawerAttribute() { }
    }

    internal partial class MultilineTextFieldAttribute : PropertyAttribute
    {
        public MultilineTextFieldAttribute() { }
    }

    public partial class VisualElement
    {
        List<Binding> m_Bindings;

        [UxmlAttributeBindingPath("dataSource")]
        internal Object dataSourceUnityObject
        {
            get => dataSource as Object;
            set => dataSource = value;
        }

        internal string dataSourcePathString
        {
            get => dataSourcePath.ToString();
            set => dataSourcePath = new PropertyPath(value);
        }

        internal string dataSourceTypeString
        {
            get => UxmlUtility.TypeToString(dataSourceType);
            set => dataSourceType = UxmlUtility.ParseType(value);
        }

        List<Binding> bindings
        {
            get => m_Bindings ??= new List<Binding>();
            set => m_Bindings = value;
        }

        [Serializable]
        [ExcludeFromDocs]
        public partial class UxmlSerializedData : UnityEngine.UIElements.UxmlSerializedData
        {
#pragma warning disable 649
            [HideInInspector] [SerializeField] string name;
            [UxmlObjectReference("Bindings")] [HideInInspector] [SerializeReference] List<Binding.UxmlSerializedData> bindings;
            [SerializeField] string tooltip;
            [HideInInspector] [UxmlAttribute("data-source-path")] [Tooltip(DataBinding.k_DataSourcePathTooltip)] [SerializeField] string dataSourcePathString;
            [UxmlAttribute("data-source-type")] [Tooltip(DataBinding.k_DataSourceTooltip)] [SerializeField] [HideInInspector] [UxmlTypeReference(typeof(object))] string dataSourceTypeString;
            [Tooltip(DataBinding.k_DataSourceTooltip)] [SerializeField] [HideInInspector] [DataSourceDrawer] [UxmlAttribute("data-source")] Object dataSourceUnityObject;
            [SerializeField] string viewDataKey;
            [UxmlAttribute(obsoleteNames = new[] { "pickingMode" })] [SerializeField] PickingMode pickingMode;
            [SerializeField] UsageHints usageHints;
            [SerializeField] LanguageDirection languageDirection;
            [UxmlAttribute("tabindex")] [SerializeField] int tabIndex;
            [SerializeField] bool focusable;
            [UxmlAttribute("enabled")] [Tooltip("Sets the element to disabled which will not accept input. Utilizes the :disabled pseudo state.")] [SerializeField] bool enabledSelf;

            [HideInInspector] [UxmlIgnore] [SerializeField] UxmlAttributeFlags name_UxmlAttributeFlags;
            [HideInInspector] [SerializeField] [UxmlIgnore] UxmlAttributeFlags enabledSelf_UxmlAttributeFlags;
            [UxmlIgnore] [SerializeField] [HideInInspector] UxmlAttributeFlags viewDataKey_UxmlAttributeFlags;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags pickingMode_UxmlAttributeFlags;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags tooltip_UxmlAttributeFlags;
            [HideInInspector] [UxmlIgnore] [SerializeField] UxmlAttributeFlags usageHints_UxmlAttributeFlags;
            [HideInInspector] [UxmlIgnore] [SerializeField] UxmlAttributeFlags tabIndex_UxmlAttributeFlags;
            [HideInInspector] [UxmlIgnore] [SerializeField] UxmlAttributeFlags focusable_UxmlAttributeFlags;
            [SerializeField] [HideInInspector] [UxmlIgnore] UxmlAttributeFlags languageDirection_UxmlAttributeFlags;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags dataSourceUnityObject_UxmlAttributeFlags;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags dataSourcePathString_UxmlAttributeFlags;
            [UxmlIgnore] [SerializeField] [HideInInspector] UxmlAttributeFlags dataSourceTypeString_UxmlAttributeFlags;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags bindings_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(name), "name", null),
                    new UxmlAttributeNames(nameof(enabledSelf), "enabled", null),
                    new UxmlAttributeNames(nameof(viewDataKey), "view-data-key", null),
                    new UxmlAttributeNames(nameof(pickingMode), "picking-mode", null, "pickingMode"),
                    new UxmlAttributeNames(nameof(tooltip), "tooltip", null),
                    new UxmlAttributeNames(nameof(usageHints), "usage-hints", null),
                    new UxmlAttributeNames(nameof(tabIndex), "tabindex", null),
                    new UxmlAttributeNames(nameof(focusable), "focusable", null),
                    new UxmlAttributeNames(nameof(languageDirection), "language-direction", null),
                    new UxmlAttributeNames(nameof(dataSourceUnityObject), "data-source", null),
                    new UxmlAttributeNames(nameof(dataSourcePathString), "data-source-path", null),
                    new UxmlAttributeNames(nameof(dataSourceTypeString), "data-source-type", typeof(object)),
                    new UxmlAttributeNames(nameof(bindings), "Bindings", null),
                });
            }

            internal bool HasBindingInternal(string property)
            {
                if (bindings == null) return false;
                for (int i = 0; i < bindings.Count; i++)
                    if (bindings[i].property == property) return true;
                return false;
            }

            [ExcludeFromDocs]
            public override object CreateInstance() => new VisualElement();

            [ExcludeFromDocs]
            public override void Deserialize(object obj)
            {
                var visualElement = (VisualElement)obj;
                if (ShouldWriteAttributeValue(name_UxmlAttributeFlags)) visualElement.name = name;
                if (ShouldWriteAttributeValue(enabledSelf_UxmlAttributeFlags)) visualElement.enabledSelf = enabledSelf;
                if (ShouldWriteAttributeValue(viewDataKey_UxmlAttributeFlags)) visualElement.viewDataKey = viewDataKey;
                if (ShouldWriteAttributeValue(pickingMode_UxmlAttributeFlags)) visualElement.pickingMode = pickingMode;
                if (ShouldWriteAttributeValue(tooltip_UxmlAttributeFlags)) visualElement.tooltip = tooltip;
                if (ShouldWriteAttributeValue(usageHints_UxmlAttributeFlags)) visualElement.usageHints = usageHints;
                if (ShouldWriteAttributeValue(tabIndex_UxmlAttributeFlags)) visualElement.tabIndex = tabIndex;
                if (ShouldWriteAttributeValue(focusable_UxmlAttributeFlags)) visualElement.focusable = focusable;
                if (ShouldWriteAttributeValue(dataSourceUnityObject_UxmlAttributeFlags))
                    visualElement.dataSourceUnityObject = dataSourceUnityObject ? dataSourceUnityObject : null;
                if (ShouldWriteAttributeValue(dataSourcePathString_UxmlAttributeFlags)) visualElement.dataSourcePathString = dataSourcePathString;
                if (ShouldWriteAttributeValue(dataSourceTypeString_UxmlAttributeFlags)) visualElement.dataSourceTypeString = dataSourceTypeString;
                if (ShouldWriteAttributeValue(languageDirection_UxmlAttributeFlags)) visualElement.languageDirection = languageDirection;
                if (!ShouldWriteAttributeValue(bindings_UxmlAttributeFlags)) return;
                visualElement.bindings.Clear();
                if (bindings == null) return;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var bindingData = bindings[i];
                    var binding = (Binding)bindingData.CreateInstance();
                    bindingData.Deserialize(binding);
                    visualElement.SetBinding(binding.property, binding);
                    visualElement.bindings.Add(binding);
                }
            }
        }
    }

    public partial class BindableElement
    {
        internal const string k_BindingPathTooltip = "Default method to define a path to a serialized property. Most often used for Editor extensions and inspectors.";

        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
#pragma warning disable 649
            [Tooltip(k_BindingPathTooltip)] [SerializeField] [BindingPathDrawer] string bindingPath;
            [HideInInspector] [SerializeField] [UxmlIgnore] UxmlAttributeFlags bindingPath_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(bindingPath), "binding-path", null),
                });
            }

            public override object CreateInstance() => new BindableElement();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                if (ShouldWriteAttributeValue(bindingPath_UxmlAttributeFlags))
                    ((BindableElement)obj).bindingPath = bindingPath;
            }
        }
    }

    public partial class TextElement
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : BindableElement.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField] [MultilineTextField] string text;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags text_UxmlAttributeFlags;
            [SerializeField] bool enableRichText;
            [SerializeField] [HideInInspector] [UxmlIgnore] UxmlAttributeFlags enableRichText_UxmlAttributeFlags;
            [SerializeField] bool emojiFallbackSupport;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags emojiFallbackSupport_UxmlAttributeFlags;
            [SerializeField] bool parseEscapeSequences;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags parseEscapeSequences_UxmlAttributeFlags;
            [FormerlySerializedAs("selectable")] [SerializeField] [UxmlAttribute("selectable")] bool isSelectable;
            [UxmlIgnore] [FormerlySerializedAs("selectable_UxmlAttributeFlags")] [SerializeField] [HideInInspector] UxmlAttributeFlags isSelectable_UxmlAttributeFlags;
            [UxmlAttribute("double-click-selects-word", new[] { "select-word-by-double-click" })] [FormerlySerializedAs("selectWordByDoubleClick")] [SerializeField] bool doubleClickSelectsWord;
            [HideInInspector] [FormerlySerializedAs("selectWordByDoubleClick_UxmlAttributeFlags")] [SerializeField] [UxmlIgnore] UxmlAttributeFlags doubleClickSelectsWord_UxmlAttributeFlags;
            [UxmlAttribute("triple-click-selects-line", new[] { "select-line-by-triple-click" })] [FormerlySerializedAs("selectLineByTripleClick")] [SerializeField] bool tripleClickSelectsLine;
            [FormerlySerializedAs("selectLineByTripleClick_UxmlAttributeFlags")] [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags tripleClickSelectsLine_UxmlAttributeFlags;
            [SerializeField] bool displayTooltipWhenElided;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags displayTooltipWhenElided_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(text), "text", null),
                    new UxmlAttributeNames(nameof(enableRichText), "enable-rich-text", null),
                    new UxmlAttributeNames(nameof(emojiFallbackSupport), "emoji-fallback-support", null),
                    new UxmlAttributeNames(nameof(parseEscapeSequences), "parse-escape-sequences", null),
                    new UxmlAttributeNames(nameof(isSelectable), "selectable", null, "selectable"),
                    new UxmlAttributeNames(nameof(doubleClickSelectsWord), "double-click-selects-word", null, "selectWordByDoubleClick", "select-word-by-double-click"),
                    new UxmlAttributeNames(nameof(tripleClickSelectsLine), "triple-click-selects-line", null, "selectLineByTripleClick", "select-line-by-triple-click"),
                    new UxmlAttributeNames(nameof(displayTooltipWhenElided), "display-tooltip-when-elided", null),
                });
            }

            public override object CreateInstance() => new TextElement();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                var textElement = (TextElement)obj;
                if (ShouldWriteAttributeValue(text_UxmlAttributeFlags)) textElement.text = text;
                if (ShouldWriteAttributeValue(enableRichText_UxmlAttributeFlags)) textElement.enableRichText = enableRichText;
                if (ShouldWriteAttributeValue(emojiFallbackSupport_UxmlAttributeFlags)) textElement.emojiFallbackSupport = emojiFallbackSupport;
                if (ShouldWriteAttributeValue(parseEscapeSequences_UxmlAttributeFlags)) textElement.parseEscapeSequences = parseEscapeSequences;
                if (ShouldWriteAttributeValue(isSelectable_UxmlAttributeFlags)) textElement.isSelectable = isSelectable;
                if (ShouldWriteAttributeValue(doubleClickSelectsWord_UxmlAttributeFlags)) textElement.doubleClickSelectsWord = doubleClickSelectsWord;
                if (ShouldWriteAttributeValue(tripleClickSelectsLine_UxmlAttributeFlags)) textElement.tripleClickSelectsLine = tripleClickSelectsLine;
                if (ShouldWriteAttributeValue(displayTooltipWhenElided_UxmlAttributeFlags)) textElement.displayTooltipWhenElided = displayTooltipWhenElided;
            }
        }
    }

    public partial class Image
    {
        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : VisualElement.UxmlSerializedData
        {
            public UxmlSerializedData() { }

            public override object CreateInstance() => new Image();
        }
    }

    public abstract partial class Binding
    {
        internal const string k_UpdateTriggerTooltip = "This informs the binding system of whether the binding object should be updated on every frame, when a change occurs in the source or on every frame if change detection is impossible, and when explicitly marked as dirty.";

        [Serializable]
        [ExcludeFromDocs]
        public abstract partial class UxmlSerializedData : UnityEngine.UIElements.UxmlSerializedData
        {
#pragma warning disable 649
            [SerializeField] [HideInInspector] internal string property;
            [Tooltip(k_UpdateTriggerTooltip)] [HideInInspector] [SerializeField] BindingUpdateTrigger updateTrigger;
            [UxmlIgnore] [HideInInspector] [SerializeField] UxmlAttributeFlags property_UxmlAttributeFlags;
            [HideInInspector] [UxmlIgnore] [SerializeField] UxmlAttributeFlags updateTrigger_UxmlAttributeFlags;
#pragma warning restore 649

            protected UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(property), "property", null),
                    new UxmlAttributeNames(nameof(updateTrigger), "update-trigger", null),
                });
            }

            public override void Deserialize(object obj)
            {
                var binding = (Binding)obj;
                if (ShouldWriteAttributeValue(property_UxmlAttributeFlags)) binding.property = property;
                if (ShouldWriteAttributeValue(updateTrigger_UxmlAttributeFlags)) binding.updateTrigger = updateTrigger;
            }
        }
    }

    public abstract partial class CustomBinding
    {
        [Serializable]
        [ExcludeFromDocs]
        public new abstract partial class UxmlSerializedData : Binding.UxmlSerializedData
        {
            protected UxmlSerializedData() { }
        }
    }

    public partial class DataBinding
    {
        internal const string k_DataSourceTooltip = "A data source is a collection of information. By default, a binding will inherit the existing data source from the hierarchy. You can instead define another object here as the data source, or define the type of property it may be if the source is not yet available.";
        internal const string k_DataSourcePathTooltip = "The path to the value in the data source used by this binding. To see resolved bindings in the UI Builder, define a path that is compatible with the target source property.";
        internal const string k_BindingModeTooltip = "Controls how a binding is updated, which can include the direction in which data is written.";
        internal const string k_SourceToUiConvertersTooltip = "Define one or more converter groups for this binding that will be used between the data source to the target UI.";
        internal const string k_UiToSourceConvertersTooltip = "Define one or more converter groups for this binding that will be used between the target UI to the data source.";

        List<string> m_SourceToUIConvertersString;
        List<string> m_UiToSourceConvertersString;

        internal string dataSourceTypeString
        {
            get => UxmlUtility.TypeToString(dataSourceType);
            set => dataSourceType = UxmlUtility.ParseType(value);
        }

        internal string dataSourcePathString
        {
            get => dataSourcePath.ToString();
            set => dataSourcePath = new PropertyPath(value);
        }

        internal string sourceToUiConvertersString
        {
            get => m_SourceToUIConvertersString != null ? string.Join(", ", m_SourceToUIConvertersString) : null;
            set
            {
                m_SourceToUIConvertersString = UxmlUtility.ParseStringListAttribute(value);
                if (m_SourceToUIConvertersString == null) return;
                for (int i = 0; i < m_SourceToUIConvertersString.Count; i++)
                    if (ConverterGroups.TryGetConverterGroup(m_SourceToUIConvertersString[i], out var group))
                        ApplyConverterGroupToUI(group);
            }
        }

        internal string uiToSourceConvertersString
        {
            get => m_UiToSourceConvertersString != null ? string.Join(", ", m_UiToSourceConvertersString) : null;
            set
            {
                m_UiToSourceConvertersString = UxmlUtility.ParseStringListAttribute(value);
                if (m_UiToSourceConvertersString == null) return;
                for (int i = 0; i < m_UiToSourceConvertersString.Count; i++)
                    if (ConverterGroups.TryGetConverterGroup(m_UiToSourceConvertersString[i], out var group))
                        ApplyConverterGroupToSource(group);
            }
        }

        [Serializable]
        [ExcludeFromDocs]
        public new partial class UxmlSerializedData : Binding.UxmlSerializedData
        {
#pragma warning disable 649
            [UxmlAttribute("data-source-path")] [HideInInspector] [SerializeField] [Tooltip(k_DataSourcePathTooltip)] string dataSourcePathString;
            [UxmlIgnore] [SerializeField] [HideInInspector] UxmlAttributeFlags dataSourcePathString_UxmlAttributeFlags;
            [SerializeField] [DataSourceDrawer] [Tooltip(k_DataSourceTooltip)] [HideInInspector] Object dataSource;
            [UxmlIgnore] [SerializeField] [HideInInspector] UxmlAttributeFlags dataSource_UxmlAttributeFlags;
            [SerializeField] [HideInInspector] [UxmlTypeReference(typeof(object))] [Tooltip(k_DataSourceTooltip)] [UxmlAttribute("data-source-type")] string dataSourceTypeString;
            [UxmlIgnore] [SerializeField] [HideInInspector] UxmlAttributeFlags dataSourceTypeString_UxmlAttributeFlags;
            [SerializeField] [HideInInspector] [BindingModeDrawer] [Tooltip(k_BindingModeTooltip)] BindingMode bindingMode;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags bindingMode_UxmlAttributeFlags;
            [UxmlAttribute("source-to-ui-converters")] [SerializeField] [HideInInspector] [ConverterDrawer(isConverterToSource = false)] [UxmlAttributeBindingPath("uiToSourceConverters")] [Tooltip(k_SourceToUiConvertersTooltip)] string sourceToUiConvertersString;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags sourceToUiConvertersString_UxmlAttributeFlags;
            [UxmlAttribute("ui-to-source-converters")] [SerializeField] [Tooltip(k_UiToSourceConvertersTooltip)] [UxmlAttributeBindingPath("sourceToUiConverters")] [ConverterDrawer(isConverterToSource = true)] [HideInInspector] string uiToSourceConvertersString;
            [SerializeField] [UxmlIgnore] [HideInInspector] UxmlAttributeFlags uiToSourceConvertersString_UxmlAttributeFlags;
#pragma warning restore 649

            public UxmlSerializedData() { }

            [Conditional("UNITY_EDITOR")]
            public new static void Register()
            {
                UxmlDescriptionCache.RegisterType(typeof(UxmlSerializedData), new[]
                {
                    new UxmlAttributeNames(nameof(dataSourcePathString), "data-source-path", null),
                    new UxmlAttributeNames(nameof(dataSource), "data-source", null),
                    new UxmlAttributeNames(nameof(dataSourceTypeString), "data-source-type", typeof(object)),
                    new UxmlAttributeNames(nameof(bindingMode), "binding-mode", null),
                    new UxmlAttributeNames(nameof(sourceToUiConvertersString), "source-to-ui-converters", null),
                    new UxmlAttributeNames(nameof(uiToSourceConvertersString), "ui-to-source-converters", null),
                });
            }

            public override object CreateInstance() => new DataBinding();

            public override void Deserialize(object obj)
            {
                base.Deserialize(obj);
                var dataBinding = (DataBinding)obj;
                if (ShouldWriteAttributeValue(dataSourcePathString_UxmlAttributeFlags)) dataBinding.dataSourcePathString = dataSourcePathString;
                if (ShouldWriteAttributeValue(dataSource_UxmlAttributeFlags)) dataBinding.dataSource = dataSource ? dataSource : null;
                if (ShouldWriteAttributeValue(dataSourceTypeString_UxmlAttributeFlags)) dataBinding.dataSourceTypeString = dataSourceTypeString;
                if (ShouldWriteAttributeValue(bindingMode_UxmlAttributeFlags)) dataBinding.bindingMode = bindingMode;
                if (ShouldWriteAttributeValue(uiToSourceConvertersString_UxmlAttributeFlags)) dataBinding.uiToSourceConvertersString = uiToSourceConvertersString;
                if (ShouldWriteAttributeValue(sourceToUiConvertersString_UxmlAttributeFlags)) dataBinding.sourceToUiConvertersString = sourceToUiConvertersString;
            }
        }
    }
}
