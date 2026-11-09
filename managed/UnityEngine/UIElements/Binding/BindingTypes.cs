using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Properties;

// Runtime data binding: the types a binding is made of. A binding (DataBinding, or a game's
// CustomBinding) is set on an element under a binding id (the path of the element property it
// drives); the panel updates it each frame from its data source (its own, or the nearest one up the
// hierarchy, with the data source paths on the way prefixed). Values pass through converter groups
// (the binding's own, then the global ones, then Unity.Properties' type conversion). Documented
// (Unity 6000.0 Scripting API "Binding", "DataBinding", "CustomBinding", "BindingId",
// "ConverterGroup", "ConverterGroups", "BindingMode", "BindingUpdateTrigger", Manual "Runtime data
// binding"). Matches the Unity 6000.0.71f1 Editor, tests/projects/uitkcollections (binding section).
namespace UnityEngine.UIElements
{
    /// <summary>The id of a bindable property of an element: its property path.</summary>
    public readonly partial struct BindingId : IEquatable<BindingId>
    {
        public static readonly BindingId Invalid = default;

        readonly PropertyPath m_PropertyPath;
        readonly string m_Path;

        public BindingId(string path)
        {
            m_PropertyPath = new PropertyPath(path);
            m_Path = path;
        }

        public BindingId(in PropertyPath path)
        {
            m_PropertyPath = path;
            m_Path = path.ToString();
        }

        public static implicit operator PropertyPath(in BindingId vep) => vep.m_PropertyPath;

        public static implicit operator string(in BindingId vep) => vep.m_Path;

        public static implicit operator BindingId(string name) => new BindingId(name);

        public static implicit operator BindingId(in PropertyPath path) => new BindingId(path);

        public override string ToString() => m_Path;

        public bool Equals(BindingId other) => m_PropertyPath == other.m_PropertyPath;

        public override bool Equals(object obj) => obj is BindingId other && Equals(other);

        public override int GetHashCode() => m_PropertyPath.GetHashCode();

        public static bool operator ==(in BindingId lhs, in BindingId rhs) => lhs.m_PropertyPath == rhs.m_PropertyPath;

        public static bool operator !=(in BindingId lhs, in BindingId rhs) => !(lhs == rhs);
    }

    public readonly partial struct BindablePropertyChangedEventArgs
    {
        readonly BindingId m_PropertyName;

        public BindablePropertyChangedEventArgs(in BindingId propertyName) => m_PropertyName = propertyName;

        public BindingId propertyName => m_PropertyName;
    }

    /// <summary>A data source that reports which of its properties changed.</summary>
    public partial interface INotifyBindablePropertyChanged
    {
        event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;
    }

    public partial interface IDataSourceProvider
    {
        object dataSource { get; }

        PropertyPath dataSourcePath { get; }
    }

    /// <summary>A data source that tells whether it changed by a hash of its view.</summary>
    public partial interface IDataSourceViewHashProvider
    {
        long GetViewHashCode();
    }

    /// <summary>Whether a list's rows get their data sources automatically.</summary>
    public enum BindingSourceSelectionMode
    {
        Manual,
        AutoAssign,
    }

    public enum BindingMode
    {
        TwoWay,
        ToSource,
        ToTarget,
        ToTargetOnce,
    }

    public enum BindingUpdateTrigger
    {
        WhenDirty,
        OnSourceChanged,
        EveryUpdate,
    }

    public enum BindingStatus
    {
        Success,
        Failure,
        Pending,
    }

    public readonly partial struct BindingResult
    {
        public BindingStatus status { get; }

        public string message { get; }

        public BindingResult(BindingStatus status, string message = null)
        {
            this.status = status;
            this.message = message;
        }
    }

    public readonly partial struct DataSourceContext
    {
        public object dataSource { get; }

        public PropertyPath dataSourcePath { get; }

        public DataSourceContext(object dataSource, in PropertyPath dataSourcePath)
        {
            this.dataSource = dataSource;
            this.dataSourcePath = dataSourcePath;
        }
    }

    public readonly partial struct BindingContext
    {
        readonly VisualElement m_TargetElement;
        readonly BindingId m_BindingId;
        readonly PropertyPath m_DataSourcePath;
        readonly object m_DataSource;

        public VisualElement targetElement => m_TargetElement;

        public BindingId bindingId => m_BindingId;

        public PropertyPath dataSourcePath => m_DataSourcePath;

        public object dataSource => m_DataSource;

        internal BindingContext(VisualElement targetElement, in BindingId bindingId, in PropertyPath resolvedDataSourcePath, object resolvedDataSource)
        {
            m_TargetElement = targetElement;
            m_BindingId = bindingId;
            m_DataSourcePath = resolvedDataSourcePath;
            m_DataSource = resolvedDataSource;
        }
    }

    public readonly partial struct BindingInfo
    {
        public VisualElement targetElement { get; }

        public BindingId bindingId { get; }

        public Binding binding { get; }

        internal BindingInfo(VisualElement targetElement, in BindingId bindingId, Binding binding)
        {
            this.targetElement = targetElement;
            this.bindingId = bindingId;
            this.binding = binding;
        }
    }

    public readonly partial struct BindingActivationContext
    {
        readonly VisualElement m_TargetElement;
        readonly BindingId m_BindingId;

        public VisualElement targetElement => m_TargetElement;

        public BindingId bindingId => m_BindingId;

        internal BindingActivationContext(VisualElement element, in BindingId property)
        {
            m_TargetElement = element;
            m_BindingId = property;
        }
    }

    public readonly partial struct DataSourceContextChanged
    {
        readonly VisualElement m_TargetElement;
        readonly BindingId m_BindingId;
        readonly DataSourceContext m_PreviousContext;
        readonly DataSourceContext m_NewContext;

        public VisualElement targetElement => m_TargetElement;

        public BindingId bindingId => m_BindingId;

        public DataSourceContext previousContext => m_PreviousContext;

        public DataSourceContext newContext => m_NewContext;

        internal DataSourceContextChanged(VisualElement element, in BindingId bindingId, in DataSourceContext previousContext, in DataSourceContext newContext)
        {
            m_TargetElement = element;
            m_BindingId = bindingId;
            m_PreviousContext = previousContext;
            m_NewContext = newContext;
        }
    }

    /// <summary>A binding: how and when an element property is updated. Starts dirty.</summary>
    public abstract partial class Binding
    {
        bool m_Dirty;

        internal string property { get; set; }

        public bool isDirty => m_Dirty;

        [CreateProperty]
        public BindingUpdateTrigger updateTrigger { get; set; }

        public static void SetGlobalLogLevel(BindingLogLevel logLevel) => UaDataBindingSystem.globalLogLevel = logLevel;

        public static void SetPanelLogLevel(IPanel panel, BindingLogLevel logLevel)
        {
            if (panel is BaseVisualElementPanel p) p.uaDataBinding.logLevel = logLevel;
        }

        public static void ResetPanelLogLevel(IPanel panel)
        {
            if (panel is BaseVisualElementPanel p) p.uaDataBinding.logLevel = null;
        }

        internal Binding() => m_Dirty = true;

        public void MarkDirty() => m_Dirty = true;

        internal void ClearDirty() => m_Dirty = false;

        [Unbound.EditorFaithful("Unity's: a hook for derived bindings")]
        protected internal virtual void OnActivated(in BindingActivationContext context) { }

        [Unbound.EditorFaithful("Unity's: a hook for derived bindings")]
        protected internal virtual void OnDeactivated(in BindingActivationContext context) { }

        [Unbound.EditorFaithful("Unity's: a hook for derived bindings")]
        protected internal virtual void OnDataSourceChanged(in DataSourceContextChanged context) { }
    }

    /// <summary>A binding that copies a value between a data source path and the element property,
    /// in the directions of its binding mode, through its converter groups.</summary>
    public partial class DataBinding : Binding, IDataSourceProvider
    {
        // The class of a text field's inner text element.
        const string k_InnerInputFieldComponentClass = "unity-text-element--inner-input-field-component";

        BindingMode m_BindingMode;
        ConverterGroup m_SourceToUiConverters;
        ConverterGroup m_UiToSourceConverters;

        [CreateProperty]
        public object dataSource { get; set; }

        public Type dataSourceType { get; set; }

        [CreateProperty]
        public PropertyPath dataSourcePath { get; set; }

        [CreateProperty]
        public BindingMode bindingMode
        {
            get => m_BindingMode;
            set
            {
                if (m_BindingMode == value) return;
                m_BindingMode = value;
                MarkDirty();
            }
        }

        [CreateProperty(ReadOnly = true)]
        public ConverterGroup sourceToUiConverters => m_SourceToUiConverters ??= new ConverterGroup(string.Empty);

        [CreateProperty(ReadOnly = true)]
        public ConverterGroup uiToSourceConverters => m_UiToSourceConverters ??= new ConverterGroup(string.Empty);

        public DataBinding() => updateTrigger = BindingUpdateTrigger.OnSourceChanged;

        public void ApplyConverterGroupToSource(ConverterGroup group) => uiToSourceConverters.registry.Apply(group.registry);

        public void ApplyConverterGroupToUI(ConverterGroup group) => sourceToUiConverters.registry.Apply(group.registry);

        /// <summary>Sets the element property to a value from the source (pending while the element
        /// is a focused delayed text field being edited).</summary>
        protected internal virtual BindingResult UpdateUI<TValue>(in BindingContext context, ref TValue value)
        {
            var container = context.targetElement;
            var focus = container.focusController;
            if (focus != null && focus.IsFocused(container) && focus.GetLeafFocusedElement() is TextElement text
                && text.ClassListContains(k_InnerInputFieldComponentClass) && container is IDelayedField { isDelayed: true })
                return new BindingResult(BindingStatus.Pending);
            if (sourceToUiConverters.TrySetValue(ref container, (PropertyPath)context.bindingId, value, out var returnCode)) return default;
            return new BindingResult(BindingStatus.Failure, SetValueError(returnCode, container, context.bindingId, value));
        }

        /// <summary>Sets the source value at the data source path to the element's value.</summary>
        protected internal virtual BindingResult UpdateSource<TValue>(in BindingContext context, ref TValue value)
        {
            object container = context.dataSource;
            if (uiToSourceConverters.TrySetValue(ref container, context.dataSourcePath, value, out var returnCode)) return default;
            return new BindingResult(BindingStatus.Failure, SetValueError(returnCode, container, (BindingId)context.dataSourcePath, value));
        }

        internal static string SetValueError<TValue>(VisitReturnCode returnCode, object target, in BindingId targetPath, TValue value)
        {
            string text = $"[UI Toolkit] Could not set value for target of type '<b>{target?.GetType().Name}</b>' at path '<b>{targetPath}</b>':";
            switch (returnCode)
            {
                case VisitReturnCode.MissingPropertyBag:
                    return text + " the type '" + target?.GetType().Name + "' is missing a property bag.";
                case VisitReturnCode.InvalidPath:
                    return text + " the path is either invalid or contains a null value.";
                case VisitReturnCode.InvalidCast:
                    if (PropertyContainer.TryGetValue(ref target, (string)targetPath, out object current) && current != null)
                        return value == null
                            ? text + " could not convert from '<b>null</b>' to '<b>" + current.GetType().Name + "</b>'."
                            : text + " could not convert from type '<b>" + value.GetType().Name + "</b>' to type '<b>" + current.GetType().Name + "</b>'.";
                    return text + " conversion failed.";
                case VisitReturnCode.AccessViolation:
                    return text + " the path is read-only.";
                default:
                    return text + " internal data binding error.";
            }
        }
    }

    /// <summary>A field that can apply its value only when editing ends.</summary>
    internal partial interface IDelayedField
    {
        bool isDelayed { get; }
    }

    /// <summary>A game's binding: Update is called with the binding context (every update by
    /// default).</summary>
    public abstract partial class CustomBinding : Binding
    {
        protected CustomBinding() => updateTrigger = BindingUpdateTrigger.EveryUpdate;

        protected internal virtual BindingResult Update(in BindingContext context) => new BindingResult(BindingStatus.Success);
    }

    /// <summary>Converters by source and destination type.</summary>
    internal readonly partial struct TypeConverterRegistry : IEqualityComparer<TypeConverterRegistry>
    {
        readonly Dictionary<(Type, Type), Delegate> m_Converters;

        TypeConverterRegistry(Dictionary<(Type, Type), Delegate> converters) => m_Converters = converters;

        public static TypeConverterRegistry Create() => new TypeConverterRegistry(new Dictionary<(Type, Type), Delegate>());

        public int ConverterCount => m_Converters?.Count ?? 0;

        public void Register(Type source, Type destination, Delegate converter) => m_Converters[(source, destination)] = converter;

        public void Unregister(Type source, Type destination) => m_Converters.Remove((source, destination));

        internal void Apply(TypeConverterRegistry registry)
        {
            foreach (var entry in registry.m_Converters) m_Converters[entry.Key] = entry.Value;
        }

        public Delegate GetConverter(Type source, Type destination) => m_Converters.TryGetValue((source, destination), out var converter) ? converter : null;

        public bool TryGetConverter(Type source, Type destination, out Delegate converter) => m_Converters.TryGetValue((source, destination), out converter);

        public void GetAllTypesConvertingToType(Type type, List<Type> result)
        {
            foreach (var key in m_Converters.Keys)
                if (key.Item2 == type) result.Add(key.Item1);
        }

        public void GetAllTypesConvertingFromType(Type type, List<Type> result)
        {
            foreach (var key in m_Converters.Keys)
                if (key.Item1 == type) result.Add(key.Item2);
        }

        public void GetAllConversions(List<(Type, Type)> result) => result.AddRange(m_Converters.Keys);

        public bool Equals(TypeConverterRegistry x, TypeConverterRegistry y) => x.m_Converters == y.m_Converters;

        public int GetHashCode(TypeConverterRegistry obj) => obj.m_Converters?.GetHashCode() ?? 0;
    }

    /// <summary>A named set of converters; a binding's own groups convert before the global ones.</summary>
    public partial class ConverterGroup
    {
        public string id { get; }

        public string displayName { get; }

        public string description { get; }

        internal TypeConverterRegistry registry { get; }

        public ConverterGroup(string id, string displayName = null, string description = null)
        {
            this.id = id;
            this.displayName = displayName;
            this.description = description;
            registry = TypeConverterRegistry.Create();
        }

        public void AddConverter<TSource, TDestination>(TypeConverter<TSource, TDestination> converter) =>
            registry.Register(typeof(TSource), typeof(TDestination), converter);

        /// <summary>Converts with this group's converter for the types, else the global
        /// conversion.</summary>
        public bool TryConvert<TSource, TDestination>(ref TSource source, out TDestination destination)
        {
            if (registry.TryGetConverter(typeof(TSource), typeof(TDestination), out var converter))
            {
                destination = ((TypeConverter<TSource, TDestination>)converter)(ref source);
                return true;
            }
            return ConverterGroups.TryConvert(ref source, out destination);
        }

        /// <summary>Sets the property at the path of the container to the value, converted.</summary>
        public bool TrySetValue<TContainer, TValue>(ref TContainer container, in PropertyPath path, TValue value, out VisitReturnCode returnCode) =>
            UaSetValueVisitor<TValue>.TrySet(this, ref container, path, value, out returnCode);
    }

    /// <summary>The global converters (registered by games) and converter groups by id.</summary>
    public static partial class ConverterGroups
    {
        static readonly ConverterGroup s_GlobalConverters = new ConverterGroup("__global_converters");
        static readonly Dictionary<string, ConverterGroup> s_Groups = new Dictionary<string, ConverterGroup>();

        internal static ConverterGroup globalConverters => s_GlobalConverters;

        public static void RegisterGlobalConverter<TSource, TDestination>(TypeConverter<TSource, TDestination> converter) =>
            s_GlobalConverters.AddConverter(converter);

        internal static void RegisterGlobal<TSource, TDestination>(TypeConverter<TSource, TDestination> converter) => RegisterGlobalConverter(converter);

        internal static void AddConverterToGroup<TSource, TDestination>(string groupId, TypeConverter<TSource, TDestination> converter)
        {
            if (!s_Groups.TryGetValue(groupId, out var group))
            {
                group = new ConverterGroup(groupId);
                s_Groups[groupId] = group;
            }
            group.AddConverter(converter);
        }

        public static void RegisterConverterGroup(ConverterGroup converterGroup)
        {
            if (string.IsNullOrWhiteSpace(converterGroup?.id))
            {
                Debug.LogWarning("[UI Toolkit] Cannot register a converter group with a 'null' or empty id.");
                return;
            }
            if (s_Groups.ContainsKey(converterGroup.id))
                Debug.LogWarning("[UI Toolkit] Replacing converter group with id: " + converterGroup.id);
            s_Groups[converterGroup.id] = converterGroup;
        }

        public static bool TryGetConverterGroup(string groupId, out ConverterGroup converterGroup) =>
            s_Groups.TryGetValue(groupId ?? string.Empty, out converterGroup);

        internal static void GetAllConverterGroups(List<ConverterGroup> result) => result.AddRange(s_Groups.Values);

        /// <summary>A global converter for the types, else Unity.Properties' conversion.</summary>
        public static bool TryConvert<TSource, TDestination>(ref TSource source, out TDestination destination)
        {
            if (s_GlobalConverters.registry.TryGetConverter(typeof(TSource), typeof(TDestination), out var converter))
            {
                destination = ((TypeConverter<TSource, TDestination>)converter)(ref source);
                return true;
            }
            return TypeConversion.TryConvert(ref source, out destination);
        }

        public static bool TrySetValueGlobal<TContainer, TValue>(ref TContainer container, in PropertyPath path, TValue value, out VisitReturnCode returnCode) =>
            UaSetValueVisitor<TValue>.TrySet(null, ref container, path, value, out returnCode);
    }

    /// <summary>Sets a value at a path, converting it to the property's type with a converter
    /// group's converters (or the global conversion). One pooled visitor per value type.</summary>
    internal sealed class UaSetValueVisitor<TValue> : PathVisitor
    {
        [ThreadStatic] static UaSetValueVisitor<TValue> s_Cached;

        ConverterGroup m_Group;
        TValue m_Value;

        public static bool TrySet<TContainer>(ConverterGroup group, ref TContainer container, in PropertyPath path, TValue value, out VisitReturnCode returnCode)
        {
            var visitor = s_Cached ?? new UaSetValueVisitor<TValue>();
            s_Cached = null;
            try
            {
                visitor.Reset();
                visitor.Path = path;
                visitor.m_Group = group;
                visitor.m_Value = value;
                if (path.IsEmpty)
                {
                    returnCode = VisitReturnCode.InvalidPath;
                    return false;
                }
                bool accepted = PropertyContainer.TryAccept((IPropertyBagVisitor)visitor, ref container, out returnCode);
                if (accepted && returnCode == VisitReturnCode.Ok) returnCode = visitor.ReturnCode;
                return returnCode == VisitReturnCode.Ok;
            }
            finally
            {
                visitor.m_Group = null;
                visitor.m_Value = default;
                s_Cached = visitor;
            }
        }

        protected override void VisitPath<TContainer, TPropertyValue>(Property<TContainer, TPropertyValue> property, ref TContainer container, ref TPropertyValue current)
        {
            if (property.IsReadOnly)
            {
                ReturnCode = VisitReturnCode.AccessViolation;
                return;
            }
            if (typeof(TValue) == typeof(TPropertyValue))
            {
                property.SetValue(ref container, Unsafe.As<TValue, TPropertyValue>(ref m_Value));
                return;
            }
            bool converted = m_Group != null
                ? m_Group.TryConvert(ref m_Value, out TPropertyValue result)
                : ConverterGroups.TryConvert(ref m_Value, out result);
            if (!converted)
            {
                ReturnCode = VisitReturnCode.InvalidCast;
                return;
            }
            property.SetValue(ref container, result);
        }
    }
}
