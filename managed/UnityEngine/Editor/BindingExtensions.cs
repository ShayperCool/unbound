using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor.UIElements.Bindings;
using UnityEngine;
using UnityEngine.UIElements;

// Editor UI Toolkit SerializedProperty tracking: BindingExtensions.TrackPropertyValue,
// BindingExtensions.Unbind, SerializedPropertyChangeEvent, and the SerializedObject binding
// context/updater pipeline that polls SerializedObject changes during panel updates.
// Documented in Unity 6000.0 Scripting API ("BindingExtensions.TrackPropertyValue",
// "BindingExtensions.Unbind", "SerializedPropertyChangeEvent"); matches the Unity 6000.0.71f1
// Editor, tests/projects/trackpropertyvalue.

namespace UnityEngine.UIElements
{
    internal partial class ObjectPool<T> where T : new()
    {
        readonly Stack<T> m_Stack = new Stack<T>();
        int m_MaxSize;
        internal Func<T> CreateFunc;

        public int maxSize
        {
            get => m_MaxSize;
            set
            {
                m_MaxSize = Math.Max(0, value);
                while (m_Stack.Count > m_MaxSize) m_Stack.Pop();
            }
        }

        public ObjectPool(Func<T> CreateFunc, int maxSize = 100)
        {
            m_MaxSize = Math.Max(0, maxSize);
            this.CreateFunc = CreateFunc ?? (() => new T());
        }

        public int Size() => m_Stack.Count;

        public void Clear() => m_Stack.Clear();

        public T Get() => m_Stack.Count == 0 ? CreateFunc() : m_Stack.Pop();

        public void Release(T element)
        {
            if (m_Stack.Count < m_MaxSize) m_Stack.Push(element);
        }
    }

    internal partial interface IBindingRequest
    {
        void Bind(VisualElement element);
        void Release();
    }

    internal static class UaBindingRequests
    {
        static readonly PropertyName s_BindingRequestObjectVEPropertyName = new PropertyName("__unity-binding-request-object");

        public static void Add(VisualElement ve, IBindingRequest req)
        {
            var list = ve.GetProperty(s_BindingRequestObjectVEPropertyName) as List<IBindingRequest>;
            if (list == null)
            {
                list = new List<IBindingRequest>(2);
                ve.SetProperty(s_BindingRequestObjectVEPropertyName, list);
            }
            list.Add(req);
            ve.IncrementVersion(VersionChangeType.Bindings);
            ve.elementPanel?.UaRegisterPendingBindingRequests(ve);
        }

        public static void Clear(VisualElement ve)
        {
            if (!(ve.GetProperty(s_BindingRequestObjectVEPropertyName) is List<IBindingRequest> list)) return;
            for (int i = 0; i < list.Count; i++) list[i]?.Release();
            list.Clear();
            ve.SetProperty(s_BindingRequestObjectVEPropertyName, null);
            ve.elementPanel?.UaUnregisterPendingBindingRequests(ve);
        }

        public static bool HasAny(VisualElement ve) =>
            ve.GetProperty(s_BindingRequestObjectVEPropertyName) is List<IBindingRequest> list && list.Count > 0;

        public static List<IBindingRequest> Take(VisualElement ve)
        {
            if (!(ve.GetProperty(s_BindingRequestObjectVEPropertyName) is List<IBindingRequest> list)) return null;
            ve.SetProperty(s_BindingRequestObjectVEPropertyName, null);
            return list;
        }
    }
}

namespace UnityEditor
{
    internal partial class SerializedObjectChangeTracker : IDisposable
    {
        sealed class TrackedEntry
        {
            public readonly int Hash;
            public readonly string Path;
            public uint LastContentHash;
            public bool LastHasMultipleDifferentValues;
            public int RefCount;

            public TrackedEntry(int hash, string path, uint contentHash, bool hasMultipleDifferentValues)
            {
                Hash = hash;
                Path = path;
                LastContentHash = contentHash;
                LastHasMultipleDifferentValues = hasMultipleDifferentValues;
                RefCount = 1;
            }
        }

        static readonly SerializedProperty[] s_EmptyPropertyArray = Array.Empty<SerializedProperty>();
        static readonly int[] s_EmptyIntArray = Array.Empty<int>();

        readonly Dictionary<int, TrackedEntry> m_TrackedByHash = new Dictionary<int, TrackedEntry>();
        readonly List<TrackedEntry> m_TrackedOrder = new List<TrackedEntry>();
        readonly List<int> m_SyncedHashes = new List<int>();
        readonly List<SerializedProperty> m_ModifiedScratch = new List<SerializedProperty>();

        internal SerializedObject m_SerializedObject;
        uint m_LastObjectVersion;
        bool m_Disposed;
        SerializedProperty[] m_ModifiedTrackedProperties = s_EmptyPropertyArray;

        public SerializedObjectChangeTracker(SerializedObject obj)
        {
            m_SerializedObject = obj;
            m_LastObjectVersion = obj != null ? obj.objectVersion : 0u;
        }

        [global::UnityEngine.@ThreadAndSerializationSafeAttribute]
        public void Dispose()
        {
            if (m_Disposed) return;
            ClearModifiedTrackedProperties();
            m_TrackedByHash.Clear();
            m_TrackedOrder.Clear();
            m_SyncedHashes.Clear();
            m_SerializedObject = null;
            m_Disposed = true;
        }

        public SerializedProperty[] GetModifiedTrackedProperties() => m_ModifiedTrackedProperties;

        public void ClearModifiedTrackedProperties()
        {
            if (m_ModifiedTrackedProperties.Length == 0) return;
            for (int i = 0; i < m_ModifiedTrackedProperties.Length; i++) m_ModifiedTrackedProperties[i]?.Dispose();
            m_ModifiedTrackedProperties = s_EmptyPropertyArray;
        }

        public bool HasModifiedTrackedProperties() => m_ModifiedTrackedProperties.Length != 0;

        internal bool HasModifiedTrackedPropertiesInternal() => m_ModifiedTrackedProperties.Length != 0;

        public int[] GetSyncedTrackedProperties() => m_SyncedHashes.Count == 0 ? s_EmptyIntArray : m_SyncedHashes.ToArray();

        internal List<int> SyncedTrackedHashes => m_SyncedHashes;

        internal bool PollForChanges(bool updateSerializedObject)
        {
            if (m_Disposed) throw new ArgumentNullException("Not a valid handle, has it been released already?");
            ClearModifiedTrackedProperties();
            m_SyncedHashes.Clear();
            if (m_SerializedObject == null || !m_SerializedObject.isValid) return false;
            if (updateSerializedObject) m_SerializedObject.UpdateIfRequiredOrScript();
            uint currentVersion = m_SerializedObject.objectVersion;
            if (currentVersion == m_LastObjectVersion) return false;
            m_LastObjectVersion = currentVersion;

            m_ModifiedScratch.Clear();
            for (int i = 0; i < m_TrackedOrder.Count; i++)
            {
                var entry = m_TrackedOrder[i];
                var prop = m_SerializedObject.FindProperty(entry.Path);
                if (prop == null) continue;
                m_SyncedHashes.Add(entry.Hash);
                uint hash = prop.contentHash;
                bool multi = prop.hasMultipleDifferentValues;
                if (hash != entry.LastContentHash || multi != entry.LastHasMultipleDifferentValues)
                {
                    entry.LastContentHash = hash;
                    entry.LastHasMultipleDifferentValues = multi;
                    m_ModifiedScratch.Add(prop.Copy());
                }
            }
            if (m_ModifiedScratch.Count > 0)
            {
                m_ModifiedTrackedProperties = m_ModifiedScratch.ToArray();
                m_ModifiedScratch.Clear();
            }
            return true;
        }

        internal void AddPropertyTracking(SerializedProperty property)
        {
            if (m_Disposed || property == null) return;
            int hash = property.hashCodeForPropertyPath;
            if (m_TrackedByHash.TryGetValue(hash, out var entry))
            {
                entry.RefCount++;
                return;
            }
            entry = new TrackedEntry(hash, property.propertyPath, property.contentHash, property.hasMultipleDifferentValues);
            m_TrackedByHash.Add(hash, entry);
            m_TrackedOrder.Add(entry);
        }

        internal void RemovePropertyTracking(SerializedProperty property)
        {
            if (property != null) RemovePropertyTracking(property.hashCodeForPropertyPath);
        }

        internal void RemovePropertyTracking(int propertyPathHash)
        {
            if (m_Disposed || !m_TrackedByHash.TryGetValue(propertyPathHash, out var entry)) return;
            if (--entry.RefCount > 0) return;
            m_TrackedByHash.Remove(propertyPathHash);
            m_TrackedOrder.Remove(entry);
        }
    }
}

namespace UnityEditor.UIElements
{
    public sealed partial class SerializedPropertyChangeEvent : EventBase<SerializedPropertyChangeEvent>
    {
        public SerializedProperty changedProperty { get; set; }

        static SerializedPropertyChangeEvent() => SetCreateFunction(() => new SerializedPropertyChangeEvent());

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            changedProperty = null;
        }

        public static SerializedPropertyChangeEvent GetPooled(SerializedProperty value)
        {
            var evt = EventBase<SerializedPropertyChangeEvent>.GetPooled();
            evt.changedProperty = value;
            return evt;
        }

        public SerializedPropertyChangeEvent() => LocalInit();
    }

    internal partial interface ISerializedObjectBindingImplementation
    {
        void Bind(VisualElement element, SerializedObject obj);
        void Unbind(VisualElement element);
        SerializedProperty BindProperty(IBindable field, SerializedObject obj);
        void BindProperty(IBindable field, SerializedProperty property);
        void Bind(VisualElement element, object bindingContext, SerializedProperty parentProperty);
        void TrackPropertyValue(VisualElement element, SerializedProperty property, Action<object, SerializedProperty> callback);
        void TrackSerializedObjectValue(VisualElement element, SerializedObject property, Action<SerializedObject> callback);
        void HandleStyleUpdate(VisualElement element);
    }

    public static partial class BindingExtensions
    {
        public static readonly string prefabOverrideUssClassName = "unity-binding--prefab-override";
        internal static readonly string prefabOverrideBarName = "unity-binding-prefab-override-bar";
        internal static readonly string prefabOverrideBarContainerName = "unity-prefab-override-bars-container";
        internal static readonly string prefabOverrideBarUssClassName = "unity-binding__prefab-override-bar";
        internal static readonly string prefabOverrideBarNotApplicableUssClassName = "unity-binding__prefab-override-bar-not-applicable";
        internal static readonly string drivenUssClassName = "unity-binding--driven";
        internal static readonly string animationAnimatedUssClassName = "unity-binding--animation-animated";
        internal static readonly string animationRecordedUssClassName = "unity-binding--animation-recorded";
        internal static readonly string animationCandidateUssClassName = "unity-binding--animation-candidate";
        internal static readonly string livePropertyUssClassName = "unity-binding--live-property";
        internal static readonly string livePropertyBarName = "unity-binding-live-property-bar";
        internal static readonly string livePropertyBarContainerName = "unity-live-property-bars-container";
        internal static readonly string livePropertyBarUssClassName = "unity-binding__live-property-bar";
        internal static readonly PropertyName s_DataSourceProperty = new PropertyName("unity-data-source");
        internal static readonly string s_SerializedBindingId = "--unity-serialized-object-bindings";
        internal static readonly string s_SerializedBindingContextUpdaterId = "--unity-serialized-object-bindings-context-updater";
        internal static ISerializedObjectBindingImplementation bindingImpl = new DefaultSerializedObjectBindingImplementation();

        public static void Unbind(this VisualElement element) => bindingImpl.Unbind(element);

        public static void TrackPropertyValue(this VisualElement element, SerializedProperty property, Action<SerializedProperty> callback = null) =>
            bindingImpl.TrackPropertyValue(element, property, callback == null ? null : (Action<object, SerializedProperty>)((_, p) => callback(p)));

        internal static void TrackPropertyValue(this VisualElement element, SerializedProperty property, Action<object, SerializedProperty> callback) =>
            bindingImpl.TrackPropertyValue(element, property, callback);
    }
}

namespace UnityEditor.UIElements.Bindings
{
    internal partial class DefaultSerializedObjectBindingImplementation : ISerializedObjectBindingImplementation
    {
        internal partial class BindingRequest : IBindingRequest
        {
            public enum RequestType
            {
                Bind = 0,
                DelayBind = 1,
                TrackProperty = 2,
                TrackObject = 3,
            }

            public static ObjectPool<BindingRequest> s_Pool = new ObjectPool<BindingRequest>(() => new BindingRequest(), 32);

            public SerializedObject obj;
            public SerializedObjectBindingContext context;
            public SerializedProperty parentProperty;
            public Action<object, SerializedProperty> callback;
            public Action<object, SerializedObject> objectChangedCallback;
            public RequestType requestType;

            public BindingRequest() { }

            public static BindingRequest Create(RequestType reqType, SerializedObject obj)
            {
                var req = s_Pool.Get();
                req.requestType = reqType;
                req.obj = obj;
                return req;
            }

            public static BindingRequest CreateDelayBinding(SerializedObjectBindingContext context, SerializedProperty parentProperty)
            {
                var req = s_Pool.Get();
                req.requestType = RequestType.DelayBind;
                req.context = context;
                req.parentProperty = parentProperty;
                return req;
            }

            public void Bind(VisualElement element)
            {
                context ??= FindOrCreateBindingContext(element, obj);
                switch (requestType)
                {
                    case RequestType.Bind:
                        Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.SerializedObjectBindingContext.Bind");
                        break;
                    case RequestType.DelayBind:
                        Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.SerializedObjectBindingContext.ContinueBinding");
                        break;
                    case RequestType.TrackProperty:
                        context.AddBindingUpdater(element).AddTracking(parentProperty, callback);
                        break;
                    case RequestType.TrackObject:
                        context.AddBindingUpdater(element).registeredCallbacks += objectChangedCallback;
                        break;
                }
            }

            public void Release()
            {
                obj = null;
                context = null;
                parentProperty = null;
                callback = null;
                objectChangedCallback = null;
                requestType = RequestType.Bind;
                s_Pool.Release(this);
            }
        }

        public DefaultSerializedObjectBindingImplementation() { }

        [Unbound.Stub]
        public void Bind(VisualElement element, SerializedObject obj) =>
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.DefaultSerializedObjectBindingImplementation.Bind");

        [Unbound.Stub]
        public SerializedProperty BindProperty(IBindable field, SerializedObject obj)
        {
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.DefaultSerializedObjectBindingImplementation.BindProperty");
            return default;
        }

        [Unbound.Stub]
        public void BindProperty(IBindable field, SerializedProperty property) =>
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.DefaultSerializedObjectBindingImplementation.BindProperty");

        [Unbound.Stub]
        public void TrackSerializedObjectValue(VisualElement element, SerializedObject serializedObject, Action<SerializedObject> callback) =>
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.DefaultSerializedObjectBindingImplementation.TrackSerializedObjectValue");

        [Unbound.Stub]
        void ISerializedObjectBindingImplementation.Bind(VisualElement element, object bindingContext, SerializedProperty parentProperty) =>
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.DefaultSerializedObjectBindingImplementation.Bind");

        [Unbound.Stub]
        public void HandleStyleUpdate(VisualElement element) =>
            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.DefaultSerializedObjectBindingImplementation.HandleStyleUpdate");

        public void Unbind(VisualElement element)
        {
            element?.SetProperty(BindingExtensions.s_DataSourceProperty, null);
            UnbindTree(element);
        }

        void UnbindTree(VisualElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            var bindingContext = SerializedObjectBindingContext.GetBindingContextFromElement(element);
            UaBindingRequests.Clear(element);
            (element.GetBinding(BindingExtensions.s_SerializedBindingId) as SerializedObjectBindingBase)?.OnRelease();
            (element.GetBinding(BindingExtensions.s_SerializedBindingContextUpdaterId) as SerializedObjectBindingBase)?.OnRelease();
            element.ClearBinding(BindingExtensions.s_SerializedBindingId);
            element.ClearBinding(BindingExtensions.s_SerializedBindingContextUpdaterId);
            if (bindingContext != null)
            {
                bindingContext.Unbind(element);
                return;
            }
            int childCount = element.hierarchy.childCount;
            for (int i = 0; i < childCount; i++) UnbindTree(element.hierarchy[i]);
        }

        static Dictionary<object, object> GetTemporaryCache(VisualElement element) => element.elementPanel?.uaBindingTemporaryCache;

        static SerializedObjectBindingContext FindOrCreateBindingContext(VisualElement element, SerializedObject obj)
        {
            var context = SerializedObjectBindingContext.FindBindingContext(element, obj);
            if (context == null || !context.TargetsSerializedObject(obj))
            {
                var temporaryCache = GetTemporaryCache(element);
                if (temporaryCache != null && obj != null && temporaryCache.TryGetValue(obj, out var cached))
                    context = cached as SerializedObjectBindingContext;
                if (context == null)
                {
                    context = new SerializedObjectBindingContext(obj);
                    if (obj != null) temporaryCache?.Add(obj, context);
                }
            }
            return context;
        }

        public void TrackPropertyValue(VisualElement element, SerializedProperty property, Action<object, SerializedProperty> callback)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (element == null) return;
            var request = BindingRequest.Create(BindingRequest.RequestType.TrackProperty, property.serializedObject);
            request.parentProperty = property.Copy();
            request.obj = property.serializedObject;
            request.callback = callback ?? SendPropertyChangeCallback;
            if (element.panel == null)
            {
                UaBindingRequests.Add(element, request);
                return;
            }
            request.Bind(element);
            request.Release();
        }

        static void SendPropertyChangeCallback(object cookie, SerializedProperty prop)
        {
            if (!(cookie is VisualElement visualElement)) return;
            using var evt = SerializedPropertyChangeEvent.GetPooled(prop.Copy());
            evt.elementTarget = visualElement;
            visualElement.SendEvent(evt);
        }
    }

    internal abstract partial class SerializedObjectBindingBase : CustomBinding, IDataSourceProvider, IDataSourceViewHashProvider
    {
        long m_LastUpdateTime;
        ulong m_LastVersion;
        public string boundPropertyPath;
        public int boundPropertyHash;
        SerializedProperty m_BoundProperty;
        internal static readonly PropertyName UndoGroupPropertyKey = new PropertyName("__UnityUndoGroup");
        protected IBindable m_Field;
        SerializedObjectBindingContext m_BindingContext;
        bool m_TooltipWasSet;
        bool m_IsFieldAttached;
        readonly EventCallback<AttachToPanelEvent> m_OnFieldAttachedToPanel;
        readonly EventCallback<DetachFromPanelEvent> m_OnFieldDetachedFromPanel;
        ulong m_LastSyncedVersion;

        public object dataSource => this;
        [Unbound.EditorFaithful("Unity returns default(PropertyPath) because the binding itself is the view-hash source")]
        public PropertyPath dataSourcePath => default;
        protected abstract string bindingId { get; }

        public SerializedObjectBindingContext bindingContext
        {
            get => m_BindingContext;
            private set
            {
                m_BindingContext?.UnregisterBindingObject(this);
                m_BindingContext = value;
                if (m_BindingContext != null)
                {
                    m_BindingContext.RegisterBindingObject(this);
                    SyncObjectVersion();
                }
                else
                {
                    boundProperty = null;
                }
            }
        }

        public SerializedProperty boundProperty
        {
            get => m_BoundProperty;
            set
            {
                m_BoundProperty = value;
                if (m_BoundProperty != null)
                {
                    boundPropertyPath = m_BoundProperty.propertyPath;
                    boundPropertyHash = m_BoundProperty.hashCodeForPropertyPath;
                }
                else
                {
                    boundPropertyPath = null;
                    boundPropertyHash = 0;
                }
            }
        }

        protected internal bool isReleased { get; set; }
        protected virtual VisualElement owner => m_Field as VisualElement;

        protected IBindable boundElement
        {
            get => m_Field;
            set
            {
                if (m_Field is VisualElement previous)
                {
                    previous.UnregisterCallback(m_OnFieldAttachedToPanel);
                    previous.UnregisterCallback(m_OnFieldDetachedFromPanel);
                    if (m_TooltipWasSet) previous.tooltip = null;
                    m_TooltipWasSet = false;
                }
                m_Field = value;
                UpdateFieldIsAttached();
                if (m_Field is VisualElement current)
                {
                    current.RegisterCallback(m_OnFieldAttachedToPanel);
                    current.RegisterCallback(m_OnFieldDetachedFromPanel);
                    if (string.IsNullOrEmpty(current.tooltip))
                    {
                        current.tooltip = boundProperty?.tooltip;
                        m_TooltipWasSet = true;
                    }
                }
            }
        }

        protected bool isFieldAttached
        {
            get => m_IsFieldAttached;
            private set
            {
                if (m_IsFieldAttached == value) return;
                m_IsFieldAttached = value;
                if (m_IsFieldAttached)
                {
                    m_BindingContext?.RegisterBindingObject(this);
                    if (boundProperty != null) OnPropertyValueChanged(boundProperty);
                }
                else
                {
                    m_BindingContext?.UnregisterBindingObject(this);
                }
            }
        }

        protected SerializedObjectBindingBase()
        {
            m_OnFieldAttachedToPanel = _ => OnFieldAttached();
            m_OnFieldDetachedFromPanel = _ => OnFieldDetached();
        }

        static long GetCurrentTime() => Panel.TimeSinceStartupMs();

        protected bool IsBindingContextUninitialized() => bindingContext == null || !bindingContext.IsInitialized();

        public long GetViewHashCode()
        {
            if (IsBindingContextUninitialized()) return -1L;
            if (bindingContext.IsValid() && owner != null) bindingContext.UpdateIfNecessary(owner);
            return IsBindingContextUninitialized() ? -1L : bindingContext.objectVersion;
        }

        public abstract void OnRelease();

        public void Unbind()
        {
            if (boundElement is VisualElement visualElement) visualElement.ClearBinding(bindingId);
            OnRelease();
        }

        protected internal override void OnActivated(in BindingActivationContext context)
        {
            m_LastUpdateTime = GetCurrentTime() - 100;
            base.OnActivated(in context);
        }

        protected internal override BindingResult Update(in BindingContext context)
        {
            if (IsBindingContextUninitialized())
            {
                Unbind();
                return default;
            }
            long currentTime = GetCurrentTime();
            if (currentTime - m_LastUpdateTime >= 100)
            {
                m_LastUpdateTime = currentTime;
                bindingContext?.UpdateIfNecessary(context.targetElement);
                if (m_LastVersion == (bindingContext?.objectVersion ?? 0u)) return new BindingResult(BindingStatus.Pending);
                var result = OnUpdate(in context);
                m_LastVersion = bindingContext?.objectVersion ?? 0u;
                return result;
            }
            ResetUpdate();
            return new BindingResult(BindingStatus.Pending);
        }

        public abstract BindingResult OnUpdate(in BindingContext context);

        protected void SetContext(SerializedObjectBindingContext context, SerializedProperty prop)
        {
            if (context != null) boundProperty = prop;
            bindingContext = context;
        }

        protected void ResetContext() => bindingContext = null;

        public virtual void ResetUpdate() => bindingContext?.ResetUpdate();

        protected virtual void OnFieldAttached()
        {
            isFieldAttached = true;
            ResetCachedValues();
        }

        void OnFieldDetached() => isFieldAttached = false;

        protected void UpdateFieldIsAttached()
        {
            if (m_Field is VisualElement visualElement)
            {
                bool attached = visualElement.panel != null;
                if (isFieldAttached != attached)
                {
                    if (attached) OnFieldAttached();
                    else OnFieldDetached();
                }
            }
            else if (m_Field != null && !isFieldAttached)
            {
                OnFieldAttached();
            }
            else
            {
                OnFieldDetached();
            }
        }

        protected abstract void ResetCachedValues();

        [Unbound.EditorFaithful("Unity's base hook for derived SerializedObject bindings")]
        public virtual void OnPropertyValueChanged(SerializedProperty currentPropertyIterator) { }

        [Unbound.EditorFaithful("Unity's base hook for derived SerializedObject bindings")]
        public virtual void SyncValueWithoutNotify(object value) { }

        public bool ResolveProperty()
        {
            boundProperty = bindingContext != null && bindingContext.IsValid() && !string.IsNullOrEmpty(boundPropertyPath)
                ? bindingContext.FindProperty(boundPropertyPath)
                : null;
            return boundProperty != null;
        }

        public void SyncObjectVersion()
        {
            if (m_BindingContext != null) m_LastSyncedVersion = m_BindingContext.lastRevision;
        }

        public bool IsSynced() => m_BindingContext != null && m_LastSyncedVersion == m_BindingContext.lastRevision;
    }

    internal partial class SerializedObjectBindingContext
    {
        sealed class TrackedValue
        {
            public readonly Action<object, SerializedProperty> OnChangeCallback;
            public readonly Action<object> OnUpdateCallback;
            public readonly object Cookie;
            public readonly SerializedPropertyType OriginalPropType;
            public bool Removed;

            public TrackedValue(SerializedProperty property, object cookie, Action<object, SerializedProperty> changeCb, Action<object> updateCb)
            {
                OriginalPropType = property.propertyType;
                Cookie = cookie;
                OnChangeCallback = changeCb;
                OnUpdateCallback = updateCb;
            }

            public void InvokeUpdateCallback() => OnUpdateCallback?.Invoke(Cookie);

            public void InvokeOnChangeCallback(SerializedProperty prop)
            {
                if (!Removed && prop.propertyType == OriginalPropType) OnChangeCallback?.Invoke(Cookie, prop);
            }
        }

        sealed class TrackedValues
        {
            readonly Dictionary<int, List<TrackedValue>> m_TrackedValues = new Dictionary<int, List<TrackedValue>>();
            readonly List<TrackedValue> m_NotifyScratch = new List<TrackedValue>();

            public void Add(SerializedProperty prop, object cookie, Action<object, SerializedProperty> onChangeCallback, Action<object> onUpdateCallback)
            {
                int hash = prop.hashCodeForPropertyPath;
                if (!m_TrackedValues.TryGetValue(hash, out var list))
                {
                    list = new List<TrackedValue>(2);
                    m_TrackedValues.Add(hash, list);
                }
                list.Add(new TrackedValue(prop, cookie, onChangeCallback, onUpdateCallback));
            }

            public bool Remove(int propertyPathHash, object cookie)
            {
                if (!m_TrackedValues.TryGetValue(propertyPathHash, out var list)) return false;
                bool removed = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(list[i].Cookie, cookie))
                    {
                        list[i].Removed = true;
                        list.RemoveAt(i);
                        removed = true;
                    }
                }
                if (list.Count == 0) m_TrackedValues.Remove(propertyPathHash);
                return removed;
            }

            public void SyncUpdateCallbacks(int hash)
            {
                if (!m_TrackedValues.TryGetValue(hash, out var list)) return;
                for (int i = 0; i < list.Count; i++) list[i].InvokeUpdateCallback();
            }

            public void NotifyValueChanged(SerializedProperty currentProperty)
            {
                int hash = currentProperty.hashCodeForPropertyPath;
                if (!m_TrackedValues.TryGetValue(hash, out var list)) return;
                // Snapshot in case a callback unbinds or adds tracking; skip entries removed mid-pass.
                m_NotifyScratch.Clear();
                m_NotifyScratch.AddRange(list);
                for (int i = 0; i < m_NotifyScratch.Count; i++) m_NotifyScratch[i].InvokeOnChangeCallback(currentProperty);
                m_NotifyScratch.Clear();
            }
        }

        static readonly PropertyName FindContextPropertyKey = new PropertyName("__UnityBindingContext");

        readonly SerializedObjectChangeTracker m_ChangeTracker;
        readonly TrackedValues m_ValueTracker = new TrackedValues();
        readonly HashSet<SerializedObjectBindingBase> m_RegisteredBindings = new HashSet<SerializedObjectBindingBase>();
        long m_LastFrame = long.MinValue;
        bool m_WasUpdated;

        internal static Action PostProcessTrackedPropertyChanges;

        public ulong lastRevision { get; private set; }
        SerializedObject serializedObject { get; }
        internal int updateCount { get; private set; }
        public uint objectVersion => IsInitialized() ? serializedObject.objectVersion : 0u;

        public SerializedObjectBindingContext(SerializedObject so)
        {
            serializedObject = so;
            lastRevision = so != null ? so.objectVersion : 0u;
            m_ChangeTracker = new SerializedObjectChangeTracker(so);
        }

        public bool TargetsSerializedObject(SerializedObject obj) => ReferenceEquals(serializedObject, obj);

        public bool IsInitialized() => serializedObject != null && serializedObject.m_NativeObjectPtr != IntPtr.Zero;

        public UnityEngine.Object GetTargetObject() => IsInitialized() ? serializedObject.targetObject : null;

        public SerializedProperty FindProperty(string propertyPath) => serializedObject?.FindProperty(propertyPath);

        public bool RequestSerializedObject(out SerializedObject so)
        {
            so = serializedObject;
            return IsInitialized();
        }

        public void Unbind(VisualElement element)
        {
            object prop = element.GetProperty(FindContextPropertyKey);
            if (prop != null)
            {
                if (!ReferenceEquals(prop, this)) return;
                element.ClearProperty(FindContextPropertyKey);
            }
            if (element.GetBinding(BindingExtensions.s_SerializedBindingId) is SerializedObjectBindingBase binding)
                binding.Unbind();
            if (element.GetBinding(BindingExtensions.s_SerializedBindingContextUpdaterId) is SerializedObjectBindingBase updater)
            {
                updater.OnRelease();
                element.ClearBinding(BindingExtensions.s_SerializedBindingContextUpdaterId);
            }
            int childCount = element.hierarchy.childCount;
            for (int i = 0; i < childCount; i++) Unbind(element.hierarchy[i]);
        }

        internal static SerializedObjectBindingContext GetBindingContextFromElement(VisualElement element)
        {
            if (element is IBindable && element.GetBinding(BindingExtensions.s_SerializedBindingId) is SerializedObjectBindingBase binding)
                return binding.bindingContext;
            return element.GetProperty(FindContextPropertyKey) as SerializedObjectBindingContext;
        }

        internal static SerializedObjectBindingContext FindBindingContext(VisualElement element, SerializedObject obj)
        {
            while (element != null)
            {
                var context = GetBindingContextFromElement(element);
                if (context != null && ReferenceEquals(context.serializedObject, obj)) return context;
                element = element.hierarchy.parent;
            }
            return null;
        }

        internal void UpdateRevision()
        {
            if (!IsValid()) return;
            if (m_ChangeTracker.PollForChanges(updateSerializedObject: false))
            {
                lastRevision = serializedObject.objectVersion;
                UpdateTrackedProperties();
            }
        }

        internal bool IsValid() => serializedObject != null && serializedObject.m_NativeObjectPtr != IntPtr.Zero && serializedObject.isValid;

        internal void UpdateIfNecessary(VisualElement element)
        {
            var dataBinding = element?.elementPanel?.uaDataBinding;
            if (m_WasUpdated || !IsValid() || dataBinding == null || m_LastFrame == dataBinding.uaFrameCount) return;
            serializedObject.UpdateIfRequiredOrScript();
            updateCount++;
            UpdateRevision();
            m_LastFrame = dataBinding.uaFrameCount;
            m_WasUpdated = true;
        }

        internal void ResetUpdate()
        {
            if (!m_WasUpdated) return;
            m_WasUpdated = false;
            UpdateRevision();
        }

        public bool RegisterSerializedPropertyChangeCallback(object cookie, SerializedProperty property, Action<object, SerializedProperty> valueChangedCallback)
        {
            m_ValueTracker.Add(property, cookie, valueChangedCallback, null);
            m_ChangeTracker.AddPropertyTracking(property);
            return true;
        }

        public void UnregisterSerializedPropertyChangeCallback(object cookie, int propertyPathHash)
        {
            m_ValueTracker.Remove(propertyPathHash, cookie);
            m_ChangeTracker.RemovePropertyTracking(propertyPathHash);
        }

        public SerializedObjectBindingContextUpdater AddBindingUpdater(VisualElement element)
        {
            if (!(element.GetBinding(BindingExtensions.s_SerializedBindingContextUpdaterId) is SerializedObjectBindingContextUpdater updater))
                return SerializedObjectBindingContextUpdater.Create(element, this);
            if (updater.bindingContext == null || !ReferenceEquals(updater.bindingContext.serializedObject, serializedObject))
                throw new NotSupportedException("An element can track properties on only one serializedObject at a time");
            return updater;
        }

        internal void RegisterBindingObject(SerializedObjectBindingBase b)
        {
            if (!m_RegisteredBindings.Add(b)) return;
            if (b.ResolveProperty())
            {
                m_ValueTracker.Add(b.boundProperty, b,
                    (o, p) => (o as SerializedObjectBindingBase)?.OnPropertyValueChanged(p),
                    o => (o as SerializedObjectBindingBase)?.SyncObjectVersion());
                m_ChangeTracker.AddPropertyTracking(b.boundProperty);
            }
        }

        internal void UnregisterBindingObject(SerializedObjectBindingBase b)
        {
            if (b.boundPropertyHash != 0)
            {
                m_ValueTracker.Remove(b.boundPropertyHash, b);
                m_ChangeTracker.RemovePropertyTracking(b.boundPropertyHash);
            }
            m_RegisteredBindings.Remove(b);
        }

        void UpdateTrackedProperties()
        {
            var synced = m_ChangeTracker.SyncedTrackedHashes;
            for (int i = 0; i < synced.Count; i++) m_ValueTracker.SyncUpdateCallbacks(synced[i]);
            if (!m_ChangeTracker.HasModifiedTrackedProperties()) return;
            try
            {
                var modified = m_ChangeTracker.GetModifiedTrackedProperties();
                for (int i = 0; i < modified.Length; i++) m_ValueTracker.NotifyValueChanged(modified[i]);
                PostProcessTrackedPropertyChanges?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                m_ChangeTracker.ClearModifiedTrackedProperties();
            }
        }
    }

    internal sealed partial class SerializedObjectBindingContextUpdater : SerializedObjectBindingBase
    {
        readonly struct TrackedPropertyRegistration
        {
            public readonly int PropertyPathHash;
            public readonly string PropertyPath;
            public readonly Action<object, SerializedProperty> Callback;

            public TrackedPropertyRegistration(int pathHash, string path, Action<object, SerializedProperty> cb)
            {
                PropertyPathHash = pathHash;
                PropertyPath = path;
                Callback = cb;
            }
        }

        readonly List<TrackedPropertyRegistration> m_TrackedProperties = new List<TrackedPropertyRegistration>();
        readonly Undo.UndoRedoEventCallback m_UndoRedoCallback;
        VisualElement m_ContextUpdaterOwner;
        ulong m_LastTrackedObjectRevision = ulong.MaxValue;
        bool m_IsActivated;
        bool m_AwaitingRequestProcessing;

        protected override string bindingId => BindingExtensions.s_SerializedBindingContextUpdaterId;
        protected override VisualElement owner => m_ContextUpdaterOwner;

        public event Action<object, SerializedObject> registeredCallbacks;

        public SerializedObjectBindingContextUpdater() => m_UndoRedoCallback = Reset;

        public static SerializedObjectBindingContextUpdater Create(VisualElement owner, SerializedObjectBindingContext context)
        {
            var updater = new SerializedObjectBindingContextUpdater
            {
                isReleased = false,
                m_ContextUpdaterOwner = owner,
                m_LastTrackedObjectRevision = context.lastRevision,
                m_AwaitingRequestProcessing = true,
            };
            updater.SetContext(context, null);
            owner.SetBinding(BindingExtensions.s_SerializedBindingContextUpdaterId, updater);
            return updater;
        }

        protected internal override BindingResult Update(in BindingContext context)
        {
            if (IsBindingContextUninitialized())
            {
                Unbind();
                return default;
            }
            bindingContext?.UpdateIfNecessary(context.targetElement);
            return OnUpdate(in context);
        }

        public override BindingResult OnUpdate(in BindingContext context)
        {
            if (isReleased) return new BindingResult(BindingStatus.Pending);
            if (bindingContext != null)
            {
                ResetUpdate();
                if (m_LastTrackedObjectRevision != bindingContext.lastRevision)
                {
                    m_LastTrackedObjectRevision = bindingContext.lastRevision;
                    if (bindingContext.RequestSerializedObject(out var so)) registeredCallbacks?.Invoke(owner, so);
                }
                return default;
            }
            return new BindingResult(BindingStatus.Pending);
        }

        public override void OnRelease()
        {
            if (isReleased) return;
            if (owner != null && bindingContext != null) UnregisterTrackedProperties();
            m_TrackedProperties.Clear();
            m_ContextUpdaterOwner = null;
            m_IsActivated = false;
            m_AwaitingRequestProcessing = false;
            ResetContext();
            registeredCallbacks = null;
            ResetCachedValues();
            isReleased = true;
        }

        public void AddTracking(SerializedProperty prop, Action<object, SerializedProperty> callback)
        {
            m_TrackedProperties.Add(new TrackedPropertyRegistration(prop.hashCodeForPropertyPath, prop.propertyPath, callback));
            if (m_IsActivated) bindingContext?.RegisterSerializedPropertyChangeCallback(owner, prop, callback);
        }

        void RegisterTrackedProperties()
        {
            if (bindingContext == null) return;
            for (int i = 0; i < m_TrackedProperties.Count; i++)
            {
                var tracked = m_TrackedProperties[i];
                var prop = bindingContext.FindProperty(tracked.PropertyPath);
                if (prop != null) bindingContext.RegisterSerializedPropertyChangeCallback(owner, prop, tracked.Callback);
            }
        }

        void UnregisterTrackedProperties()
        {
            if (bindingContext == null) return;
            for (int i = 0; i < m_TrackedProperties.Count; i++)
                bindingContext.UnregisterSerializedPropertyChangeCallback(owner, m_TrackedProperties[i].PropertyPathHash);
        }

        protected override void ResetCachedValues() => m_LastTrackedObjectRevision = ulong.MaxValue;

        void Reset(in UndoRedoInfo undo) => ResetUpdate();

        internal override void UaOnRequestProcessed()
        {
            m_AwaitingRequestProcessing = false;
            if (isReleased || m_IsActivated) return;
            m_IsActivated = true;
            RegisterTrackedProperties();
        }

        protected internal override void OnActivated(in BindingActivationContext context)
        {
            base.OnActivated(in context);
            Undo.undoRedoEvent = (Undo.UndoRedoEventCallback)Delegate.Combine(Undo.undoRedoEvent, m_UndoRedoCallback);
            if (!m_AwaitingRequestProcessing && !m_IsActivated)
            {
                m_IsActivated = true;
                RegisterTrackedProperties();
            }
        }

        protected internal override void OnDeactivated(in BindingActivationContext context)
        {
            m_IsActivated = false;
            m_AwaitingRequestProcessing = true;
            UnregisterTrackedProperties();
            base.OnDeactivated(in context);
            Undo.undoRedoEvent = (Undo.UndoRedoEventCallback)Delegate.Remove(Undo.undoRedoEvent, m_UndoRedoCallback);
        }
    }
}
