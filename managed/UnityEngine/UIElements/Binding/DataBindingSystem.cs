using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Properties;

// The binding side of elements and panels. An element keeps the bindings set on it; while it is in a
// panel they are registered with the panel's binding system, which each repaint of the panel (before
// its styles and layout):
// - copies element values changed by the UI to their sources (bindings that write to the source);
// - updates each bound element in tree order: a binding is updated when it is dirty, on every update
//   (EveryUpdate), or when its source may have changed (OnSourceChanged: a source reporting changes
//   through INotifyBindablePropertyChanged only when a changed path leads to the binding's path; one
//   with a view hash when the hash changed; any other source every update);
// - copies UI changes again, then the UI values the sources accepted back to the UI.
// Results are logged as warnings by log level (All by default, Once when they change, None).
namespace UnityEngine.UIElements
{
    internal sealed class UaDataBindingSystem
    {
        internal sealed class BindingData
        {
            public VisualElement element;
            public BindingId id;
            public Binding binding;
            public long version;
            public DataSourceContext lastContext;
            public bool hasContext;
            public BindingResult uiResult;
            public bool hasUiResult;
            public BindingResult sourceResult;
            public bool hasSourceResult;
            // The resolved context, valid while contextVersion matches the system's.
            public DataSourceContext cachedContext;
            public PropertyPath cachedBindingPath;
            public int contextVersion = -1;
        }

        sealed class SourceInfo
        {
            public long lastVersion;
            public bool hasVersion;
            public readonly HashSet<PropertyPath> changes = new HashSet<PropertyPath>();
            public bool seen;
        }

        readonly struct ChangeFromUI
        {
            public readonly BindingData data;
            public readonly long version;
            public readonly Binding binding;

            public ChangeFromUI(BindingData data)
            {
                this.data = data;
                version = data.version;
                binding = data.binding;
            }

            public bool IsValid => version == data.version && binding == data.binding;
        }

        internal static BindingLogLevel globalLogLevel = BindingLogLevel.All;

        readonly BaseVisualElementPanel m_Panel;
        readonly Dictionary<VisualElement, List<BindingData>> m_PerElement = new Dictionary<VisualElement, List<BindingData>>();
        readonly List<VisualElement> m_Ordered = new List<VisualElement>();
        bool m_OrderDirty;
        readonly Dictionary<object, SourceInfo> m_Sources = new Dictionary<object, SourceInfo>(ReferenceEqualityComparer.Instance);
        readonly List<object> m_SourceScratch = new List<object>();
        readonly List<ChangeFromUI> m_ChangesFromUI = new List<ChangeFromUI>();
        readonly HashSet<Binding> m_RanUpdate = new HashSet<Binding>();
        readonly HashSet<object> m_KnownSources = new HashSet<object>(ReferenceEqualityComparer.Instance);
        readonly List<(object source, long version)> m_VersionChanges = new List<(object, long)>();
        readonly HashSet<object> m_TrackedThisUpdate = new HashSet<object>(ReferenceEqualityComparer.Instance);
        readonly List<BindingData> m_Scratch = new List<BindingData>();
        // Element data sources and paths, or the tree, changed: resolved contexts are recomputed.
        int m_ContextVersion;
        readonly EventHandler<BindablePropertyChangedEventArgs> m_SourceHandler;
        // The binding whose UI update must not be reported back as a UI change.
        BindingData m_Ignored;

        internal BindingLogLevel? logLevel;

        public UaDataBindingSystem(BaseVisualElementPanel panel)
        {
            m_Panel = panel;
            m_SourceHandler = OnSourcePropertyChanged;
        }

        BindingLogLevel effectiveLogLevel => logLevel ?? globalLogLevel;

        public bool hasBindings => m_PerElement.Count > 0;

        public void OnHierarchyChanged()
        {
            if (m_PerElement.Count == 0) return;
            m_OrderDirty = true;
            m_ContextVersion++;
        }

        public void OnDataSourceChanged()
        {
            if (m_PerElement.Count > 0) m_ContextVersion++;
        }

        /// <summary>The panel goes away: sources stop being observed.</summary>
        public void Dispose()
        {
            foreach (var source in m_Sources.Keys)
                if (source is INotifyBindablePropertyChanged notifying) notifying.propertyChanged -= m_SourceHandler;
            m_Sources.Clear();
            m_PerElement.Clear();
            m_Ordered.Clear();
            m_ChangesFromUI.Clear();
        }

        // ---- registration ----

        /// <summary>Registers a binding of an element in this panel (replacing the one for its id).</summary>
        public void Register(VisualElement element, in BindingId id, Binding binding)
        {
            Unregister(element, id);
            if (!m_PerElement.TryGetValue(element, out var list))
            {
                list = new List<BindingData>();
                m_PerElement[element] = list;
            }
            var data = new BindingData { element = element, id = id, binding = binding };
            binding.MarkDirty();
            list.Add(data);
            m_OrderDirty = true;
            binding.OnActivated(new BindingActivationContext(element, id));
        }

        public void Unregister(VisualElement element, in BindingId id)
        {
            if (!m_PerElement.TryGetValue(element, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].id != id) continue;
                var data = list[i];
                list.RemoveAt(i);
                data.version++;
                data.binding.OnDeactivated(new BindingActivationContext(element, id));
                break;
            }
            if (list.Count == 0) Forget(element, list);
        }

        /// <summary>Unregisters every binding of an element. An element leaving the panel first loses
        /// its data source (each binding that had one is told so), then its bindings are deactivated
        /// (Unity 6000.0.71f1 Editor, tests/projects/uitkcollections binding/remove-custom).</summary>
        public void UnregisterAll(VisualElement element, bool leavingPanel = false)
        {
            if (!m_PerElement.TryGetValue(element, out var list)) return;
            if (leavingPanel)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var data = list[i];
                    if (!data.hasContext || (data.lastContext.dataSource == null && data.lastContext.dataSourcePath.IsEmpty)) continue;
                    var previous = data.lastContext;
                    data.lastContext = default;
                    data.binding.OnDataSourceChanged(new DataSourceContextChanged(element, data.id, previous, default));
                }
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var data = list[i];
                list.RemoveAt(i);
                data.version++;
                data.binding.OnDeactivated(new BindingActivationContext(element, data.id));
            }
            Forget(element, list);
        }

        void Forget(VisualElement element, List<BindingData> list)
        {
            m_PerElement.Remove(element);
            m_OrderDirty = true;
            if (m_PerElement.Count == 0) m_Ordered.Clear();
        }

        public bool TryGetData(VisualElement element, in BindingId id, out BindingData data)
        {
            data = null;
            if (!m_PerElement.TryGetValue(element, out var list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].id != id) continue;
                data = list[i];
                return true;
            }
            return false;
        }

        // ---- change tracking ----

        /// <summary>An element property changed (from the UI or code): bindings writing to their
        /// source take it at the next update; bindings reading this element as a source see the
        /// change.</summary>
        public void OnElementPropertyChanged(VisualElement element, in BindingId id)
        {
            TrackSourceChange(element, id);
            if (!TryGetData(element, id, out var data) || data == m_Ignored) return;
            m_ChangesFromUI.Add(new ChangeFromUI(data));
        }

        void OnSourcePropertyChanged(object sender, BindablePropertyChangedEventArgs args) => TrackSourceChange(sender, args.propertyName);

        void TrackSourceChange(object source, in BindingId id)
        {
            if (m_Sources.TryGetValue(source, out var info)) info.changes.Add(id);
        }

        SourceInfo Track(object source)
        {
            if (!m_Sources.TryGetValue(source, out var info))
            {
                info = new SourceInfo();
                m_Sources[source] = info;
                if (source is INotifyBindablePropertyChanged notifying) notifying.propertyChanged += m_SourceHandler;
            }
            info.seen = true;
            return info;
        }

        (bool changed, long version) GetDataSourceVersion(object source)
        {
            if (source != null && m_Sources.TryGetValue(source, out var info) && info.hasVersion)
            {
                if (!(source is IDataSourceViewHashProvider viewHash)) return (true, info.lastVersion + 1);
                long hash = viewHash.GetViewHashCode();
                return hash == info.lastVersion ? (false, hash) : (true, hash);
            }
            if (source is IDataSourceViewHashProvider provider) return (true, provider.GetViewHashCode());
            return (source != null, 0);
        }

        // ---- update ----

        public void Update()
        {
            if (m_PerElement.Count == 0 && m_ChangesFromUI.Count == 0)
            {
                if (m_Sources.Count > 0) PruneSources();
                return;
            }
            ProcessChangesFromUI(m_RanUpdate);
            if (m_OrderDirty) RefreshOrder();
            for (int e = 0; e < m_Ordered.Count; e++)
            {
                var element = m_Ordered[e];
                if (!m_PerElement.TryGetValue(element, out var list)) continue;
                // Bindings set or cleared by callbacks during this update take effect next time.
                m_Scratch.Clear();
                m_Scratch.AddRange(list);
                for (int i = 0; i < m_Scratch.Count; i++)
                {
                    var data = m_Scratch[i];
                    if (data.binding == null || !list.Contains(data)) continue;
                    var binding = data.binding;
                    var context = CachedContext(data);
                    object source = context.dataSource;
                    var (changed, version) = GetDataSourceVersion(source);
                    if (source != null && m_TrackedThisUpdate.Add(source))
                    {
                        Track(source);
                        m_VersionChanges.Add((source, version));
                    }
                    NotifyContextChange(data, context);
                    bool dirty = binding.isDirty;
                    if (!ShouldProcess(binding, BindingUpdateStage.UpdateUI, changed, dirty)) continue;
                    if (source != null && changed) m_KnownSources.Add(source);
                    if (binding.updateTrigger == BindingUpdateTrigger.OnSourceChanged && source is INotifyBindablePropertyChanged && !dirty
                        && !ChangedOnPath(source, context.dataSourcePath))
                        continue;
                    binding.ClearDirty();
                    var bindingContext = new BindingContext(element, data.id, context.dataSourcePath, source);
                    long before = data.version;
                    var result = Guarded(bindingContext, binding, true);
                    CacheAndLog(data, result, true);
                    if (data.version != before) continue;
                    if (result.status == BindingStatus.Success) m_RanUpdate.Add(binding);
                    else if (result.status == BindingStatus.Pending && dirty) binding.MarkDirty();
                }
            }
            for (int i = 0; i < m_VersionChanges.Count; i++)
            {
                var (source, version) = m_VersionChanges[i];
                if (!m_Sources.TryGetValue(source, out var info)) continue;
                info.lastVersion = version;
                info.hasVersion = true;
            }
            ProcessChangesFromUI(m_RanUpdate);
            foreach (var source in m_KnownSources)
                if (m_Sources.TryGetValue(source, out var info)) info.changes.Clear();
            m_VersionChanges.Clear();
            m_TrackedThisUpdate.Clear();
            m_RanUpdate.Clear();
            m_KnownSources.Clear();
            m_Scratch.Clear();
            PruneSources();
        }

        /// <summary>A binding's update; an exception from game code (a custom binding, converter or
        /// property) is logged and fails this binding only.</summary>
        static BindingResult Guarded(in BindingContext context, Binding binding, bool toUI)
        {
            try
            {
                return toUI ? UpdateUI(context, binding) : UpdateSource(context, binding);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return new BindingResult(BindingStatus.Failure, e.Message);
            }
        }

        /// <summary>The binding's resolved context, recomputed after a data source, path or tree
        /// change in the panel, or a change of the binding's own source or path.</summary>
        DataSourceContext CachedContext(BindingData data)
        {
            var provider = data.binding as IDataSourceProvider;
            if (provider?.dataSource != null) return new DataSourceContext(provider.dataSource, provider.dataSourcePath);
            var bindingPath = provider?.dataSourcePath ?? default;
            if (data.contextVersion == m_ContextVersion && data.cachedBindingPath == bindingPath) return data.cachedContext;
            data.cachedContext = ResolveContext(data.element, data.binding);
            data.cachedBindingPath = bindingPath;
            data.contextVersion = m_ContextVersion;
            return data.cachedContext;
        }

        // Sources no binding resolved to in this update stop being tracked.
        void PruneSources()
        {
            m_SourceScratch.Clear();
            foreach (var entry in m_Sources)
            {
                if (entry.Value.seen) entry.Value.seen = false;
                else m_SourceScratch.Add(entry.Key);
            }
            for (int i = 0; i < m_SourceScratch.Count; i++)
            {
                var source = m_SourceScratch[i];
                if (source is INotifyBindablePropertyChanged notifying) notifying.propertyChanged -= m_SourceHandler;
                m_Sources.Remove(source);
            }
            m_SourceScratch.Clear();
        }

        bool ChangedOnPath(object source, in PropertyPath path)
        {
            if (!m_Sources.TryGetValue(source, out var info) || info.changes.Count == 0) return false;
            foreach (var change in info.changes)
                if (IsPrefix(change, path)) return true;
            return false;
        }

        static bool IsPrefix(in PropertyPath prefix, in PropertyPath path)
        {
            if (path.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                var a = prefix[i];
                var b = path[i];
                if (a.Kind != b.Kind) return false;
                switch (a.Kind)
                {
                    case PropertyPathPartKind.Name:
                        if (a.Name != b.Name) return false;
                        break;
                    case PropertyPathPartKind.Index:
                        if (a.Index != b.Index) return false;
                        break;
                    case PropertyPathPartKind.Key:
                        if (a.Key != b.Key) return false;
                        break;
                }
            }
            return true;
        }

        void NotifyContextChange(BindingData data, in DataSourceContext context)
        {
            if (data.hasContext && ReferenceEquals(data.lastContext.dataSource, context.dataSource) && data.lastContext.dataSourcePath == context.dataSourcePath)
                return;
            var previous = data.hasContext ? data.lastContext : default;
            data.lastContext = context;
            data.hasContext = true;
            data.binding.OnDataSourceChanged(new DataSourceContextChanged(data.element, data.id, previous, context));
            // A binding whose source or path changed updates (as when it is new).
            data.binding.MarkDirty();
        }

        /// <summary>The binding's own data source (with its path), else the element's hierarchical
        /// context with the binding's path appended.</summary>
        internal static DataSourceContext ResolveContext(VisualElement element, Binding binding)
        {
            if (binding is IDataSourceProvider provider)
            {
                if (provider.dataSource != null) return new DataSourceContext(provider.dataSource, provider.dataSourcePath);
                var hierarchical = element.GetHierarchicalDataSourceContext();
                if (provider.dataSourcePath.IsEmpty) return hierarchical;
                return new DataSourceContext(hierarchical.dataSource, PropertyPath.Combine(hierarchical.dataSourcePath, provider.dataSourcePath));
            }
            return element.GetHierarchicalDataSourceContext();
        }

        void ProcessChangesFromUI(HashSet<Binding> ranUpdate)
        {
            for (int i = 0; i < m_ChangesFromUI.Count; i++)
            {
                var change = m_ChangesFromUI[i];
                if (!change.IsValid) continue;
                var data = change.data;
                var binding = data.binding;
                if (!ShouldProcess(binding, BindingUpdateStage.UpdateSource, true, false) || ranUpdate.Contains(binding)) continue;
                var context = CachedContext(data);
                var bindingContext = new BindingContext(data.element, data.id, context.dataSourcePath, context.dataSource);
                var result = Guarded(bindingContext, binding, false);
                CacheAndLog(data, result, false);
                if (result.status != BindingStatus.Success || !change.IsValid) continue;
                bool wasDirty = binding.isDirty;
                binding.ClearDirty();
                m_Ignored = data;
                try
                {
                    result = Guarded(bindingContext, binding, true);
                    CacheAndLog(data, result, true);
                }
                finally
                {
                    m_Ignored = null;
                }
                if (result.status == BindingStatus.Pending)
                {
                    if (wasDirty) binding.MarkDirty();
                    else binding.ClearDirty();
                }
            }
            m_ChangesFromUI.Clear();
        }

        void CacheAndLog(BindingData data, in BindingResult result, bool toUI)
        {
            switch (effectiveLogLevel)
            {
                case BindingLogLevel.Once:
                    var last = toUI ? data.uiResult : data.sourceResult;
                    bool had = toUI ? data.hasUiResult : data.hasSourceResult;
                    if (!had || last.status != result.status || last.message != result.message) Log(result);
                    break;
                case BindingLogLevel.All:
                    Log(result);
                    break;
            }
            if (toUI)
            {
                data.uiResult = result;
                data.hasUiResult = true;
            }
            else
            {
                data.sourceResult = result;
                data.hasSourceResult = true;
            }
        }

        void Log(in BindingResult result)
        {
            if (string.IsNullOrWhiteSpace(result.message)) return;
            string panelName = (m_Panel as Panel)?.name ?? m_Panel.visualTree.name;
            Debug.LogWarning(result.message + " (" + panelName + ")");
        }

        void RefreshOrder()
        {
            m_Ordered.Clear();
            if (m_PerElement.Count > 0) Collect(m_Panel.visualTree);
            m_OrderDirty = false;
        }

        void Collect(VisualElement element)
        {
            if (m_PerElement.ContainsKey(element)) m_Ordered.Add(element);
            if (m_Ordered.Count == m_PerElement.Count) return;
            int count = element.hierarchy.childCount;
            for (int i = 0; i < count && m_Ordered.Count < m_PerElement.Count; i++) Collect(element.hierarchy[i]);
        }

        // ---- updating one binding ----

        static bool ShouldProcess(Binding binding, BindingUpdateStage stage, bool versionChanged, bool dirty)
        {
            if (binding is DataBinding data)
            {
                if (stage == BindingUpdateStage.UpdateSource)
                    return data.bindingMode != BindingMode.ToTarget && data.bindingMode != BindingMode.ToTargetOnce;
                if (data.bindingMode == BindingMode.ToSource) return false;
                if (data.updateTrigger == BindingUpdateTrigger.EveryUpdate || dirty) return true;
                if (data.bindingMode == BindingMode.ToTargetOnce) return false;
                return data.updateTrigger == BindingUpdateTrigger.OnSourceChanged && versionChanged;
            }
            if (binding is CustomBinding custom)
            {
                if (stage == BindingUpdateStage.UpdateSource) return false;
                switch (custom.updateTrigger)
                {
                    case BindingUpdateTrigger.EveryUpdate: return true;
                    case BindingUpdateTrigger.OnSourceChanged: return versionChanged || dirty;
                    default: return dirty;
                }
            }
            throw new InvalidOperationException("Binding type `" + binding.GetType().Name + "` is not supported.");
        }

        static BindingResult UpdateUI(in BindingContext context, Binding binding)
        {
            if (binding is CustomBinding custom) return custom.Update(context);
            var data = (DataBinding)binding;
            object source = context.dataSource;
            if (source == null)
                return new BindingResult(BindingStatus.Pending, "[UI Toolkit] Could not bind '" + NameOf(context.targetElement) + "' because there is no data source.");
            if (context.dataSourcePath.IsEmpty)
            {
                if (!TypeTraits.IsContainer(source.GetType())) return UaNonContainerInvoker.UpdateUI(data, context, source);
                var root = UaRootVisitor.Get();
                root.binding = data;
                root.context = context;
                bool visited = PropertyContainer.TryAccept(root, ref source, out var rootCode);
                var rootResult = root.result;
                UaRootVisitor.Release(root);
                return visited ? rootResult : new BindingResult(BindingStatus.Failure, VisitError(rootCode, context));
            }
            return VisitAtPath(data, BindingUpdateStage.UpdateUI, ref source, context.dataSourcePath, context);
        }

        static BindingResult UpdateSource(in BindingContext context, Binding binding)
        {
            if (!(binding is DataBinding data)) return new BindingResult(BindingStatus.Pending);
            if (context.dataSource == null)
                return new BindingResult(BindingStatus.Pending, "[UI Toolkit] Could not set value on '" + NameOf(context.targetElement) + "' because there is no data source.");
            if (context.dataSourcePath.IsEmpty)
                return new BindingResult(BindingStatus.Failure, "[UI Toolkit] Could not set value on the data source because the data source path is empty: the root of a data source cannot be replaced.");
            var element = context.targetElement;
            return VisitAtPath(data, BindingUpdateStage.UpdateSource, ref element, (PropertyPath)context.bindingId, context);
        }

        static BindingResult VisitAtPath<TContainer>(DataBinding binding, BindingUpdateStage stage, ref TContainer container, in PropertyPath path, in BindingContext context)
        {
            var visitor = UaPathVisitor.Get();
            visitor.binding = binding;
            visitor.stage = stage;
            visitor.context = context;
            visitor.Path = path;
            visitor.ReadonlyVisit = true;
            bool visited = PropertyContainer.TryAccept((IPropertyBagVisitor)visitor, ref container, out var code);
            var result = visitor.result;
            var visitCode = visitor.ReturnCode;
            UaPathVisitor.Release(visitor);
            if (!visited) return new BindingResult(BindingStatus.Failure, VisitError(code, context));
            if (visitCode != VisitReturnCode.Ok)
                return new BindingResult(BindingStatus.Failure, $"[UI Toolkit] Could not retrieve the value at path '<b>{path}</b>': " + CodeText(visitCode));
            return result;
        }

        static string NameOf(VisualElement element) => string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name;

        static string VisitError(VisitReturnCode code, in BindingContext context) =>
            $"[UI Toolkit] Could not bind target of type '<b>{context.targetElement.GetType().Name}</b>' at path '<b>{context.bindingId}</b>': " + CodeText(code);

        static string CodeText(VisitReturnCode code)
        {
            switch (code)
            {
                case VisitReturnCode.NullContainer: return "the data source is null.";
                case VisitReturnCode.InvalidContainerType: return "the data source must be a struct or a class type.";
                case VisitReturnCode.MissingPropertyBag: return "the data source is missing a property bag.";
                case VisitReturnCode.InvalidPath: return "the path from the data source to the target is either invalid or contains a null value.";
                case VisitReturnCode.InvalidCast: return "the value could not be converted.";
                case VisitReturnCode.AccessViolation: return "the path is read-only.";
                default: return "internal data binding error.";
            }
        }

        /// <summary>Visits the value at a path and hands it, typed, to the binding.</summary>
        sealed class UaPathVisitor : PathVisitor
        {
            [ThreadStatic] static UaPathVisitor s_Cached;

            public DataBinding binding;
            public BindingUpdateStage stage;
            public BindingContext context;
            public BindingResult result;

            public static UaPathVisitor Get()
            {
                var visitor = s_Cached ?? new UaPathVisitor();
                s_Cached = null;
                visitor.Reset();
                return visitor;
            }

            public static void Release(UaPathVisitor visitor)
            {
                visitor.binding = null;
                visitor.context = default;
                visitor.result = default;
                s_Cached = visitor;
            }

            protected override void VisitPath<TContainer, TValue>(Property<TContainer, TValue> property, ref TContainer container, ref TValue value)
            {
                result = stage == BindingUpdateStage.UpdateUI ? binding.UpdateUI(context, ref value) : binding.UpdateSource(context, ref value);
            }
        }

        /// <summary>Hands a container data source (its path empty) to the binding as itself.</summary>
        sealed class UaRootVisitor : ConcreteTypeVisitor
        {
            [ThreadStatic] static UaRootVisitor s_Cached;

            public DataBinding binding;
            public BindingContext context;
            public BindingResult result;

            public static UaRootVisitor Get()
            {
                var visitor = s_Cached ?? new UaRootVisitor();
                s_Cached = null;
                return visitor;
            }

            public static void Release(UaRootVisitor visitor)
            {
                visitor.binding = null;
                visitor.context = default;
                visitor.result = default;
                s_Cached = visitor;
            }

            protected override void VisitContainer<TContainer>(ref TContainer container) => result = binding.UpdateUI(context, ref container);
        }

        /// <summary>Hands a value that is not a container (a number, string, enum ...) to the binding
        /// as its own type.</summary>
        static class UaNonContainerInvoker
        {
            delegate BindingResult Invoker(DataBinding binding, in BindingContext context, object value);

            static readonly Dictionary<Type, Invoker> s_Invokers = new Dictionary<Type, Invoker>();
            static readonly MethodInfo s_Typed = typeof(UaNonContainerInvoker).GetMethod(nameof(Typed), BindingFlags.Static | BindingFlags.NonPublic);

            public static BindingResult UpdateUI(DataBinding binding, in BindingContext context, object value)
            {
                var type = value.GetType();
                if (!s_Invokers.TryGetValue(type, out var invoker))
                {
                    invoker = (Invoker)Delegate.CreateDelegate(typeof(Invoker), s_Typed.MakeGenericMethod(type));
                    s_Invokers[type] = invoker;
                }
                return invoker(binding, context, value);
            }

            static BindingResult Typed<T>(DataBinding binding, in BindingContext context, object value)
            {
                var typed = (T)value;
                return binding.UpdateUI(context, ref typed);
            }
        }
    }

    internal enum BindingUpdateStage
    {
        UpdateUI,
        UpdateSource,
    }

    public abstract partial class CallbackEventHandler
    {
        /// <summary>Reports that a bindable property of this element changed (in a panel only).</summary>
        protected void NotifyPropertyChanged(in BindingId property)
        {
            if (this is VisualElement element && element.elementPanel != null)
                element.elementPanel.uaDataBinding.OnElementPropertyChanged(element, property);
        }
    }

    public partial class VisualElement
    {
        internal static readonly BindingId dataSourceProperty = nameof(dataSource);
        internal static readonly BindingId dataSourcePathProperty = nameof(dataSourcePath);

        object m_DataSource;
        PropertyPath m_DataSourcePath;
        // The bindings set on the element, in the order they were set.
        List<(BindingId id, Binding binding)> m_UaBindings;

        /// <summary>The data source of this element and its descendants without their own.</summary>
        [CreateProperty]
        public object dataSource
        {
            get => m_DataSource;
            set
            {
                if (m_DataSource == value) return;
                m_DataSource = value;
                IncrementVersion(VersionChangeType.DataSource);
                NotifyPropertyChanged(dataSourceProperty);
            }
        }

        /// <summary>The path into the data source, prefixed to descendants' paths.</summary>
        [CreateProperty]
        public PropertyPath dataSourcePath
        {
            get => m_DataSourcePath;
            set
            {
                if (m_DataSourcePath == value) return;
                m_DataSourcePath = value;
                IncrementVersion(VersionChangeType.DataSource);
                NotifyPropertyChanged(dataSourcePathProperty);
            }
        }

        public Type dataSourceType { get; set; }

        /// <summary>Sets (or with null, removes) the binding of a property.</summary>
        public void SetBinding(BindingId bindingId, Binding binding)
        {
            m_UaBindings ??= new List<(BindingId, Binding)>();
            int existing = IndexOfBinding(bindingId);
            if (binding == null)
            {
                if (existing < 0) return;
                m_UaBindings.RemoveAt(existing);
                elementPanel?.uaDataBinding.Unregister(this, bindingId);
                return;
            }
            binding.property = bindingId;
            if (existing >= 0) m_UaBindings[existing] = (bindingId, binding);
            else m_UaBindings.Add((bindingId, binding));
            elementPanel?.uaDataBinding.Register(this, bindingId, binding);
        }

        int IndexOfBinding(in BindingId id)
        {
            if (m_UaBindings == null) return -1;
            for (int i = 0; i < m_UaBindings.Count; i++)
                if (m_UaBindings[i].id == id) return i;
            return -1;
        }

        public Binding GetBinding(BindingId bindingId) => TryGetBinding(bindingId, out var binding) ? binding : null;

        public bool TryGetBinding(BindingId bindingId, out Binding binding)
        {
            int index = IndexOfBinding(bindingId);
            binding = index >= 0 ? m_UaBindings[index].binding : null;
            return index >= 0;
        }

        public IEnumerable<BindingInfo> GetBindingInfos()
        {
            if (m_UaBindings == null) yield break;
            for (int i = 0; i < m_UaBindings.Count; i++)
                yield return new BindingInfo(this, m_UaBindings[i].id, m_UaBindings[i].binding);
        }

        public bool HasBinding(BindingId bindingId) => IndexOfBinding(bindingId) >= 0;

        public void ClearBinding(BindingId bindingId) => SetBinding(bindingId, null);

        public void ClearBindings()
        {
            if (m_UaBindings == null || m_UaBindings.Count == 0) return;
            m_UaBindings.Clear();
            elementPanel?.uaDataBinding.UnregisterAll(this);
        }

        /// <summary>The data source of the nearest element (this one first) with one, and the data
        /// source paths of the elements up to it, outermost first.</summary>
        public DataSourceContext GetHierarchicalDataSourceContext()
        {
            PropertyPath path = default;
            for (var e = this; e != null; e = e.hierarchy.parent)
            {
                if (!e.m_DataSourcePath.IsEmpty) path = PropertyPath.Combine(e.m_DataSourcePath, path);
                if (e.m_DataSource != null) return new DataSourceContext(e.m_DataSource, path);
            }
            return new DataSourceContext(null, path);
        }

        public DataSourceContext GetDataSourceContext(BindingId bindingId)
        {
            if (TryGetDataSourceContext(bindingId, out var context)) return context;
            throw new ArgumentOutOfRangeException(nameof(bindingId), $"[UI Toolkit] could not get binding with id '{bindingId}' on the element.");
        }

        public bool TryGetDataSourceContext(BindingId bindingId, out DataSourceContext context)
        {
            var binding = GetBinding(bindingId);
            if (binding == null)
            {
                context = default;
                return false;
            }
            context = UaDataBindingSystem.ResolveContext(this, binding);
            return true;
        }

        public bool TryGetLastBindingToUIResult(in BindingId bindingId, out BindingResult result)
        {
            result = default;
            if (elementPanel == null || !elementPanel.uaDataBinding.TryGetData(this, bindingId, out var data) || !data.hasUiResult) return false;
            result = data.uiResult;
            return true;
        }

        public bool TryGetLastBindingToSourceResult(in BindingId bindingId, out BindingResult result)
        {
            result = default;
            if (elementPanel == null || !elementPanel.uaDataBinding.TryGetData(this, bindingId, out var data) || !data.hasSourceResult) return false;
            result = data.sourceResult;
            return true;
        }

        // Bindings follow the element into and out of panels.
        void UaRegisterBindings()
        {
            if (m_UaBindings == null || elementPanel == null) return;
            for (int i = 0; i < m_UaBindings.Count; i++) elementPanel.uaDataBinding.Register(this, m_UaBindings[i].id, m_UaBindings[i].binding);
        }

        void UaUnregisterBindings()
        {
            if (m_UaBindings == null || m_UaBindings.Count == 0 || elementPanel == null) return;
            elementPanel.uaDataBinding.UnregisterAll(this, true);
        }
    }
}
