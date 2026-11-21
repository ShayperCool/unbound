using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor.UIElements.Bindings;
using UnityEngine;
using UnityEngine.UIElements;

// Editor UI Toolkit SerializedObject/SerializedProperty binding and tracking:
// BindingExtensions.Bind, BindingExtensions.BindProperty (supported scalar, enum, Color/Color32,
// Vector2, Vector3, Vector4, Vector2Int, Vector3Int, Rect, and RectInt fields plus Foldout
// isExpanded binding), BindingExtensions.TrackPropertyValue,
// BindingExtensions.TrackSerializedObjectValue, BindingExtensions.Unbind,
// SerializedPropertyChangeEvent, SerializedObjectChangeEvent, and the SerializedObject binding
// context/updater pipeline that synchronizes fields and polls changes during panel updates.
// Documented in Unity 6000.0 Scripting API ("BindingExtensions.Bind",
// "BindingExtensions.BindProperty", "BindingExtensions.TrackPropertyValue",
// "BindingExtensions.TrackSerializedObjectValue", "BindingExtensions.Unbind",
// "SerializedPropertyChangeEvent", "SerializedObjectChangeEvent"); matches the Unity 6000.0.71f1
// Editor, tests/projects/bindtree, tests/projects/bindproperty, tests/projects/enumbinding,
// tests/projects/enumflagsbinding, tests/projects/colorbinding, tests/projects/vector2binding,
// tests/projects/vector3binding, tests/projects/vector4binding, tests/projects/vector2intbinding,
// tests/projects/vector3intbinding, tests/projects/rectbinding, tests/projects/rectintbinding,
// tests/projects/trackpropertyvalue and tests/projects/trackserializedobjectvalue.

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
    public sealed partial class SerializedObjectChangeEvent : EventBase<SerializedObjectChangeEvent>
    {
        public SerializedObject changedObject { get; set; }

        static SerializedObjectChangeEvent() => SetCreateFunction(() => new SerializedObjectChangeEvent());

        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        void LocalInit()
        {
            propagation = EventPropagation.BubblesOrTricklesDown;
            changedObject = null;
        }

        public static SerializedObjectChangeEvent GetPooled(SerializedObject value)
        {
            var evt = EventBase<SerializedObjectChangeEvent>.GetPooled();
            evt.changedObject = value;
            return evt;
        }

        public SerializedObjectChangeEvent() => LocalInit();
    }

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

        public static void Bind(this VisualElement element, SerializedObject obj) => bindingImpl.Bind(element, obj);

        public static void Unbind(this VisualElement element) => bindingImpl.Unbind(element);

        public static SerializedProperty BindProperty(this IBindable field, SerializedObject obj) =>
            bindingImpl.BindProperty(field, obj);

        public static void BindProperty(this IBindable field, SerializedProperty property) =>
            bindingImpl.BindProperty(field, property);

        public static void TrackPropertyValue(this VisualElement element, SerializedProperty property, Action<SerializedProperty> callback = null) =>
            bindingImpl.TrackPropertyValue(element, property, callback == null ? null : (Action<object, SerializedProperty>)((_, p) => callback(p)));

        internal static void TrackPropertyValue(this VisualElement element, SerializedProperty property, Action<object, SerializedProperty> callback) =>
            bindingImpl.TrackPropertyValue(element, property, callback);

        public static void TrackSerializedObjectValue(this VisualElement element, SerializedObject obj, Action<SerializedObject> callback = null) =>
            bindingImpl.TrackSerializedObjectValue(element, obj, callback);
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
                        context.Bind(element);
                        break;
                    case RequestType.DelayBind:
                        if (context.IsValid()) context.ContinueBinding(element, parentProperty);
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

        public void Bind(VisualElement element, SerializedObject obj)
        {
            element.SetProperty(BindingExtensions.s_DataSourceProperty, obj);
            if (element.panel != null)
                FindOrCreateBindingContext(element, obj).Bind(element);
            else
                CreateBindingRequest(element, obj);
        }

        public SerializedProperty BindProperty(IBindable field, SerializedObject obj)
        {
            var property = obj?.FindProperty(field.bindingPath);
            if (property != null && field is VisualElement visualElement)
            {
                if (visualElement.panel == null)
                    CreateBindingRequest(visualElement, property.serializedObject);
                else
                    FindOrCreateBindingContext(visualElement, obj).Bind(visualElement);
            }
            return property;
        }

        public void BindProperty(IBindable field, SerializedProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            field.bindingPath = property.propertyPath;
            if (field is VisualElement visualElement)
            {
                if (visualElement.panel == null)
                {
                    CreateBindingRequest(visualElement, property.serializedObject);
                    return;
                }
                FindOrCreateBindingContext(visualElement, property.serializedObject).Bind(visualElement);
            }
        }

        static void CreateBindingRequest(VisualElement element, SerializedObject obj) =>
            UaBindingRequests.Add(element, BindingRequest.Create(BindingRequest.RequestType.Bind, obj));

        static readonly Action<object, SerializedObject> s_SendObjectChangeCallback = SendObjectChangeCallback;

        public void TrackSerializedObjectValue(VisualElement element, SerializedObject serializedObject, Action<SerializedObject> callback)
        {
            if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
            if (element == null) return;
            var request = BindingRequest.Create(BindingRequest.RequestType.TrackObject, serializedObject);
            request.obj = serializedObject;
            request.objectChangedCallback = callback != null
                ? (_, o) => callback(o)
                : s_SendObjectChangeCallback;
            if (element.panel == null)
            {
                UaBindingRequests.Add(element, request);
                return;
            }
            request.Bind(element);
            request.Release();
        }

        static void SendObjectChangeCallback(object cookie, SerializedObject obj)
        {
            if (!(cookie is VisualElement visualElement)) return;
            using var evt = SerializedObjectChangeEvent.GetPooled(obj);
            evt.elementTarget = visualElement;
            visualElement.SendEvent(evt);
        }

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
        static readonly PropertyName k_EnabledOverrideSet = new PropertyName("__UnityEnabledOverrideSet");
        static readonly Action<object, SerializedProperty> s_DefaultOnPropertyChange = (o, p) => (o as SerializedObjectBindingBase)?.OnPropertyValueChanged(p);
        static readonly Action<object> s_OnPropertyUpdate = o => (o as SerializedObjectBindingBase)?.SyncObjectVersion();

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
            lastRevision = so.objectVersion;
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
                element.ClearProperty(BaseField<string>.serializedPropertyCopyName);
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

        public void Bind(VisualElement element)
        {
            element.SetProperty(FindContextPropertyKey, this);
            ContinueBinding(element, null);
        }

        internal void ContinueBinding(VisualElement element, SerializedProperty parentProperty) =>
            BindTree(element, parentProperty);

        internal void BindTree(VisualElement element, SerializedProperty parentProperty)
        {
            if (element is IBindable bindable && !string.IsNullOrEmpty(bindable.bindingPath))
            {
                var prop = BindPropertyRelative(bindable, parentProperty);
                if (prop != null) parentProperty = prop;
            }
            int childCount = element.hierarchy.childCount;
            for (int i = 0; i < childCount; i++)
                BindTree(element.hierarchy[i], parentProperty);
        }

        static void SyncEditableState(VisualElement fieldElement, bool shouldBeEditable)
        {
            if (fieldElement.enabledSelf == shouldBeEditable) return;
            if (shouldBeEditable)
            {
                if (fieldElement.GetProperty(k_EnabledOverrideSet) != null)
                    fieldElement.SetEnabled(true);
            }
            else
            {
                fieldElement.SetProperty(k_EnabledOverrideSet, fieldElement.enabledSelf);
                fieldElement.SetEnabled(false);
            }
        }

        static bool HasVisibleChildFields(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                case SerializedPropertyType.Rect:
                case SerializedPropertyType.Bounds:
                case SerializedPropertyType.Vector2Int:
                case SerializedPropertyType.Vector3Int:
                case SerializedPropertyType.RectInt:
                case SerializedPropertyType.BoundsInt:
                case SerializedPropertyType.Hash128:
                    return false;
                default:
                    return property.hasVisibleChildren;
            }
        }

        internal SerializedProperty BindPropertyRelative(IBindable field, SerializedProperty parentProperty)
        {
            SerializedProperty prop = null;
            var visualElement = field as VisualElement;
            if (parentProperty != null)
            {
                prop = !HasVisibleChildFields(parentProperty)
                    && visualElement?.GetProperty(BaseField<string>.serializedPropertyCopyName) is SerializedProperty cachedCopy
                    ? cachedCopy
                    : parentProperty.FindPropertyRelative(field.bindingPath);
            }
            if (prop == null)
                prop = serializedObject?.FindProperty(field.bindingPath);
            if (prop == null || visualElement == null)
                return prop;
            SyncEditableState(visualElement, prop.editable);
            if (visualElement is PropertyField)
            {
                using var evt = SerializedPropertyBindEvent.GetPooled(prop);
                evt.elementTarget = visualElement;
                Unbound.UIElements.Dispatching.HandleEventAtTargetAndDefaultPhase(evt, visualElement.elementPanel, visualElement);
                if (evt.isPropagationStopped) return prop;
            }
            RemoveBinding(field, forceRemove: true);
            CreateBindingObjectForProperty(visualElement, prop);
            return prop;
        }

        static readonly Func<SerializedProperty, string> s_GetDisplayName = p => p.displayName;
        static readonly Action<SerializedProperty, string> s_NoopSetString = (_, _) => { };

        void CreateBindingObjectForProperty(VisualElement element, SerializedProperty prop)
        {
            if (element is Foldout foldout && prop.hasChildren)
            {
                SerializedIsExpandedBinding.CreateBind(foldout, this, prop);
                return;
            }
            if (element is Label titleLabel && titleLabel.GetProperty(PropertyField.foldoutTitleBoundLabelProperty) != null)
            {
                SerializedObjectBinding<string>.CreateBind(titleLabel, this, prop, s_GetDisplayName, s_NoopSetString, SerializedPropertyHelper.ValueEquals);
                return;
            }
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetBoolPropertyValue, SerializedPropertyHelper.SetBoolPropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Integer:
                    if (prop.numericType == SerializedPropertyNumericType.Int64
                        || prop.numericType == SerializedPropertyNumericType.UInt64
                        || prop.type == "long"
                        || prop.type == "ulong")
                    {
                        if (element is INotifyValueChanged<long>)
                            DefaultBind(element, prop, SerializedPropertyHelper.GetLongPropertyValue, SerializedPropertyHelper.SetLongPropertyValue, SerializedPropertyHelper.ValueEquals);
                        else if (element is INotifyValueChanged<ulong>)
                            DefaultBind(element, prop, SerializedPropertyHelper.GetULongPropertyValue, SerializedPropertyHelper.SetULongPropertyValue, SerializedPropertyHelper.ValueEquals);
                        else if (element is INotifyValueChanged<int>)
                            DefaultBind(element, prop, SerializedPropertyHelper.GetIntPropertyValue, SerializedPropertyHelper.SetIntPropertyValue, SerializedPropertyHelper.ValueEquals);
                        else if (element is INotifyValueChanged<float>)
                            DefaultBind(element, prop, SerializedPropertyHelper.GetLongPropertyValueAsFloat, SerializedPropertyHelper.SetFloatPropertyValue, SerializedPropertyHelper.ValueEquals);
                        else if (element is INotifyValueChanged<string>)
                            Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.SerializedObjectStringConversionBinding<long>");
                    }
                    else if (element is INotifyValueChanged<int>)
                    {
                        DefaultBind(element, prop, SerializedPropertyHelper.GetIntPropertyValue, SerializedPropertyHelper.SetIntPropertyValue, SerializedPropertyHelper.ValueEquals);
                    }
                    else if (element is INotifyValueChanged<uint>)
                    {
                        DefaultBind(element, prop, SerializedPropertyHelper.GetUIntPropertyValue, SerializedPropertyHelper.SetUIntPropertyValue, SerializedPropertyHelper.ValueEquals);
                    }
                    else if (element is INotifyValueChanged<long>)
                    {
                        DefaultBind(element, prop, SerializedPropertyHelper.GetLongPropertyValue, SerializedPropertyHelper.SetLongPropertyValue, SerializedPropertyHelper.ValueEquals);
                    }
                    else if (element is INotifyValueChanged<float>)
                    {
                        DefaultBind(element, prop, SerializedPropertyHelper.GetIntPropertyValueAsFloat, SerializedPropertyHelper.SetFloatPropertyValue, SerializedPropertyHelper.ValueEquals);
                    }
                    else if (element is INotifyValueChanged<string>)
                    {
                        Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.SerializedObjectStringConversionBinding<int>");
                    }
                    break;
                case SerializedPropertyType.Float:
                    if (prop.numericType == SerializedPropertyNumericType.Float || prop.type == "float")
                    {
                        if (element is INotifyValueChanged<double>)
                            DefaultBind(element, prop, SerializedPropertyHelper.GetFloatPropertyValueAsDouble, SerializedPropertyHelper.SetFloatPropertyValueFromDouble, SerializedPropertyHelper.ValueEquals);
                        else
                            DefaultBind(element, prop, SerializedPropertyHelper.GetFloatPropertyValue, SerializedPropertyHelper.SetFloatPropertyValue, SerializedPropertyHelper.ValueEquals);
                    }
                    else if (element is INotifyValueChanged<float>)
                    {
                        DefaultBind(element, prop, SerializedPropertyHelper.GetDoublePropertyValueAsFloat, SerializedPropertyHelper.SetDoublePropertyValueFromFloat, SerializedPropertyHelper.ValueEquals);
                    }
                    else
                    {
                        DefaultBind(element, prop, SerializedPropertyHelper.GetDoublePropertyValue, SerializedPropertyHelper.SetDoublePropertyValue, SerializedPropertyHelper.ValueEquals);
                    }
                    break;
                case SerializedPropertyType.String:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetStringPropertyValue, SerializedPropertyHelper.SetStringPropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Color:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetColorPropertyValue, SerializedPropertyHelper.SetColorPropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Enum:
                    CreateEnumBindingObject(element, prop);
                    break;
                case SerializedPropertyType.Vector2:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetVector2PropertyValue, SerializedPropertyHelper.SetVector2PropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Vector3:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetVector3PropertyValue, SerializedPropertyHelper.SetVector3PropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Vector4:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetVector4PropertyValue, SerializedPropertyHelper.SetVector4PropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Vector2Int:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetVector2IntPropertyValue, SerializedPropertyHelper.SetVector2IntPropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Vector3Int:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetVector3IntPropertyValue, SerializedPropertyHelper.SetVector3IntPropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Rect:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetRectPropertyValue, SerializedPropertyHelper.SetRectPropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.RectInt:
                    DefaultBind(element, prop, SerializedPropertyHelper.GetRectIntPropertyValue, SerializedPropertyHelper.SetRectIntPropertyValue, SerializedPropertyHelper.ValueEquals);
                    break;
                case SerializedPropertyType.Generic:
                    if (prop.type == "ToggleButtonGroupState")
                        Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.BindingExtensions.BindProperty (ToggleButtonGroupState)");
                    else if (element is BaseListView)
                        Unbound.Runtime.Stubs.NotImplemented("UnityEditor.UIElements.Bindings.BaseListViewSerializedObjectBinding");
                    break;
                default:
                    Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.UIElements.BindingExtensions.BindProperty ({prop.propertyType})");
                    break;
            }
        }

        void CreateEnumBindingObject(VisualElement element, SerializedProperty prop)
        {
            if (element is PopupField<string> popup)
            {
                SerializedDefaultEnumBinding.CreateBind(popup, this, prop);
            }
            else if (element is EnumFlagsField || element is EnumField)
            {
                SerializedManagedEnumBinding.CreateBind((BaseField<Enum>)element, this, prop);
            }
            else if (element is INotifyValueChanged<int>)
            {
                DefaultBind(element, prop, SerializedPropertyHelper.GetIntPropertyValue, SerializedPropertyHelper.SetIntPropertyValue, SerializedPropertyHelper.ValueEquals);
            }
            else
            {
                DefaultBind(element, prop, SerializedPropertyHelper.GetEnumPropertyValueAsString, SerializedPropertyHelper.SetEnumPropertyValueFromString, SerializedPropertyHelper.SlowEnumValueEquals);
            }
        }

        void DefaultBind<TValue>(
            VisualElement element,
            SerializedProperty prop,
            Func<SerializedProperty, TValue> propertyReadFunc,
            Action<SerializedProperty, TValue> propertyWriteFunc,
            Func<TValue, SerializedProperty, Func<SerializedProperty, TValue>, bool> valueComparerFunc)
        {
            if (element is INotifyValueChanged<string> && typeof(TValue) != typeof(string))
            {
                Unbound.Runtime.Stubs.NotImplemented($"UnityEditor.UIElements.Bindings.SerializedObjectStringConversionBinding<{typeof(TValue).Name}>");
                return;
            }
            if (element is INotifyValueChanged<TValue> notifyValueChanged)
            {
                SerializedObjectBinding<TValue>.CreateBind(notifyValueChanged, this, prop, propertyReadFunc, propertyWriteFunc, valueComparerFunc);
                return;
            }
            Debug.LogWarning($"Field type {element.GetType().FullName} is not compatible with {prop.type} property \"{prop.propertyPath}\"");
        }

        void RemoveBinding(IBindable bindable, bool forceRemove)
        {
            if (bindable is VisualElement visualElement
                && visualElement.GetBinding(BindingExtensions.s_SerializedBindingId) is SerializedObjectBindingBase existing
                && !existing.isReleased
                && (forceRemove || ReferenceEquals(existing.bindingContext, this)))
            {
                existing.Unbind();
            }
        }

        internal void RegisterBindingObject(SerializedObjectBindingBase b)
        {
            if (!m_RegisteredBindings.Add(b)) return;
            if (b.ResolveProperty())
            {
                m_ValueTracker.Add(b.boundProperty, b, s_DefaultOnPropertyChange, s_OnPropertyUpdate);
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

    internal abstract partial class SerializedObjectBindingToBaseField<TValue, TField> : SerializedObjectBindingBase
        where TField : class, INotifyValueChanged<TValue>
    {
        static readonly EqualityComparer<TValue> s_EqualityComparer = EqualityComparer<TValue>.Default;

        readonly EventCallback<ChangeEvent<TValue>> m_FieldValueChanged;
        readonly EventCallback<BlurEvent> m_OnFieldBlur;
        bool m_IsUpdating;
        protected ExpressionEvaluator.Expression m_LastFieldExpression;

        protected override string bindingId => BindingExtensions.s_SerializedBindingId;

        protected TField field
        {
            get => m_Field as TField;
            set
            {
                if (m_Field is VisualElement previous)
                {
                    previous.UnregisterCallback(m_FieldValueChanged, TrickleDown.TrickleDown);
                    if (previous is BaseField<TValue> prevBase) prevBase.UnregisterCallback(m_OnFieldBlur);
                }
                boundElement = value as IBindable;
                if (m_Field is VisualElement current)
                {
                    current.RegisterCallback(m_FieldValueChanged, TrickleDown.TrickleDown);
                    if (current is BaseField<TValue> currBase) currBase.RegisterCallback(m_OnFieldBlur);
                }
            }
        }

        protected SerializedObjectBindingToBaseField()
        {
            m_FieldValueChanged = FieldValueChanged;
            m_OnFieldBlur = OnFieldBlur;
        }

        void OnFieldBlur(BlurEvent evt)
        {
            if (field is IMixedValueSupport mixed && boundProperty != null && bindingContext != null && bindingContext.IsValid())
                mixed.showMixedValue = boundProperty.hasMultipleDifferentValues;
        }

        void FieldValueChanged(ChangeEvent<TValue> evt)
        {
            if (isReleased || m_IsUpdating || !ReferenceEquals(evt.target, m_Field)) return;
            try
            {
                if (m_Field is VisualElement visualElement
                    && ReferenceEquals(visualElement.GetBinding(BindingExtensions.s_SerializedBindingId), this)
                    && ResolveProperty())
                {
                    if (isFieldAttached)
                    {
                        UpdateLastFieldValue();
                        if (SyncFieldValueToProperty())
                        {
                            bindingContext.UpdateRevision();
                            bindingContext?.ResetUpdate();
                        }
                        UpdateMixedValueState(visualElement, boundProperty);
                    }
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NullReferenceException)
            {
            }
            Unbind();
        }

        protected override void ResetCachedValues()
        {
            UpdateLastFieldValue();
            UpdateFieldIsAttached();
        }

        public override void OnPropertyValueChanged(SerializedProperty currentPropertyIterator)
        {
            if (isReleased) return;
            try
            {
                m_IsUpdating = true;
                if (field is VisualElement visualElement && ReferenceEquals(visualElement.GetBinding(bindingId), this))
                {
                    SyncPropertyToField(field, currentPropertyIterator);
                    UpdateMixedValueState(visualElement, currentPropertyIterator);
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NullReferenceException)
            {
            }
            finally
            {
                m_IsUpdating = false;
            }
            Unbind();
        }

        static bool MatchesCompositeInputName(string elementName, string propName)
        {
            const string prefix = "unity-";
            const string suffix = "-input";
            return elementName != null && propName != null
                && elementName.Length == prefix.Length + propName.Length + suffix.Length
                && elementName.StartsWith(prefix, StringComparison.Ordinal)
                && elementName.EndsWith(suffix, StringComparison.Ordinal)
                && string.CompareOrdinal(elementName, prefix.Length, propName, 0, propName.Length) == 0;
        }

        static VisualElement FindCompositeSubField(VisualElement root, string propName)
        {
            int count = root.hierarchy.childCount;
            for (int i = 0; i < count; i++)
            {
                var child = root.hierarchy[i];
                if (MatchesCompositeInputName(child.name, propName)) return child;
                var found = FindCompositeSubField(child, propName);
                if (found != null) return found;
            }
            return null;
        }

        static void UpdateMixedValueState(VisualElement element, SerializedProperty prop)
        {
            if (element == null || prop == null) return;
            if (element.ClassListContains(BaseCompositeField<int, IntegerField, int>.ussClassName))
            {
                var iter = prop.Copy();
                var endProp = iter.GetEndProperty();
                if (iter.NextVisible(true))
                {
                    while (!SerializedProperty.EqualContents(iter, endProp))
                    {
                        var subElement = FindCompositeSubField(element, iter.name);
                        if (subElement != null) UpdateMixedValueState(subElement, iter);
                        if (!iter.NextVisible(false)) break;
                    }
                }
                return;
            }
            if (element is IMixedValueSupport mixed)
                mixed.showMixedValue = prop.hasMultipleDifferentValues;
        }

        protected virtual bool EqualsValue(TValue a, TValue b) => s_EqualityComparer.Equals(a, b);

        protected override void OnFieldAttached()
        {
            TValue previousValue = field.value;
            base.OnFieldAttached();
            if (boundProperty == null || !(field is VisualElement visualElement)
                || boundProperty.hasMultipleDifferentValues || !EqualsValue(previousValue, field.value))
                return;
            using var evt = ChangeEvent<TValue>.GetPooled(field.value, field.value);
            evt.elementTarget = visualElement;
            visualElement.SendEvent(evt);
        }

        public override void SyncValueWithoutNotify(object value)
        {
            if (value is TValue typed) SyncFieldValueToPropertyWithoutNotify(typed);
        }

        public override BindingResult OnUpdate(in BindingContext context)
        {
            if (isReleased) return new BindingResult(BindingStatus.Pending);
            try
            {
                ResetUpdate();
                if (!IsSynced()) return new BindingResult(BindingStatus.Pending);
                return default;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (NullReferenceException)
            {
            }
            Unbind();
            return new BindingResult(BindingStatus.Pending);
        }

        protected internal static string GetUndoMessage(SerializedProperty serializedProperty)
        {
            string text = "Modified " + serializedProperty.name;
            var targetObject = serializedProperty.serializedObject?.targetObject;
            if (targetObject != null && !string.IsNullOrEmpty(targetObject.name))
                text = text + " in " + targetObject.name;
            return text;
        }

        protected abstract void UpdateLastFieldValue();
        protected abstract bool SyncFieldValueToProperty();
        protected abstract void SyncPropertyToField(TField c, SerializedProperty p);

        protected void SyncFieldValueToPropertyWithoutNotify(TValue value)
        {
            if (field != null && !s_EqualityComparer.Equals(value, field.value))
                field.SetValueWithoutNotify(value);
        }
    }

    internal abstract partial class SerializedObjectBindingPropertyToBaseField<TProperty, TValue>
        : SerializedObjectBindingToBaseField<TValue, INotifyValueChanged<TValue>>
    {
        protected Func<SerializedProperty, TProperty> propGetValue;
        protected Action<SerializedProperty, TProperty> propSetValue;
        protected Func<TProperty, SerializedProperty, Func<SerializedProperty, TProperty>, bool> propCompareValues;
        protected TProperty lastFieldValue;

        protected SerializedObjectBindingPropertyToBaseField() { }

        protected override void SyncPropertyToField(INotifyValueChanged<TValue> c, SerializedProperty p)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            lastFieldValue = propGetValue(p);
            if (p.hasMultipleDifferentValues)
                AssignValueToFieldWithoutNotify(lastFieldValue);
            else
                AssignValueToField(lastFieldValue);
        }

        protected override bool SyncFieldValueToProperty()
        {
            if (boundProperty.hasMultipleDifferentValues || !propCompareValues(lastFieldValue, boundProperty, propGetValue))
            {
                propSetValue(boundProperty, lastFieldValue);
                boundProperty.serializedObject.ApplyModifiedProperties();
                return true;
            }
            return false;
        }

        protected abstract void AssignValueToField(TProperty lastValue);
        protected abstract void AssignValueToFieldWithoutNotify(TProperty lastValue);

        public override void OnRelease()
        {
            if (isReleased) return;
            ResetContext();
            field = null;
            propGetValue = null;
            propSetValue = null;
            propCompareValues = null;
            ResetCachedValues();
            isReleased = true;
        }
    }

    internal partial class SerializedObjectBinding<TValue> : SerializedObjectBindingPropertyToBaseField<TValue, TValue>
    {
        public SerializedObjectBinding() { }

        public static void CreateBind(
            INotifyValueChanged<TValue> field,
            SerializedObjectBindingContext context,
            SerializedProperty property,
            Func<SerializedProperty, TValue> propGetValue,
            Action<SerializedProperty, TValue> propSetValue,
            Func<TValue, SerializedProperty, Func<SerializedProperty, TValue>, bool> propCompareValues)
        {
            var binding = new SerializedObjectBinding<TValue> { isReleased = false };
            (field as VisualElement)?.SetBinding(BindingExtensions.s_SerializedBindingId, binding);
            binding.SetBinding(field, context, property, propGetValue, propSetValue, propCompareValues);
        }

        void SetBinding(
            INotifyValueChanged<TValue> c,
            SerializedObjectBindingContext context,
            SerializedProperty property,
            Func<SerializedProperty, TValue> getValue,
            Action<SerializedProperty, TValue> setValue,
            Func<TValue, SerializedProperty, Func<SerializedProperty, TValue>, bool> compareValues)
        {
            propGetValue = getValue;
            propSetValue = setValue;
            propCompareValues = compareValues;
            SetContext(context, property);
            lastFieldValue = c.value;
            field = c;
        }

        public override void OnRelease()
        {
            if (!isReleased) base.OnRelease();
        }

        protected override void UpdateLastFieldValue()
        {
            if (field != null) lastFieldValue = field.value;
        }

        protected override void AssignValueToFieldWithoutNotify(TValue lastValue)
        {
            if (field == null) return;
            if (field is BaseField<TValue> baseField)
            {
                if (!baseField.showMixedValue || !boundProperty.hasMultipleDifferentValues)
                    baseField.SetValueWithoutNotify(lastValue);
            }
            else
            {
                field.SetValueWithoutNotify(lastValue);
            }
        }

        protected override void AssignValueToField(TValue lastValue)
        {
            if (field == null) return;
            if (field is BaseField<TValue> baseField)
                baseField.SetValueWithoutValidation(lastValue);
            else
                field.value = lastValue;
        }

        protected override bool SyncFieldValueToProperty() => base.SyncFieldValueToProperty();
    }

    internal partial class SerializedDefaultEnumBinding : SerializedObjectBindingToBaseField<string, PopupField<string>>
    {
        const int kDefaultValueIndex = -1;

        int lastFieldValueIndex;
        List<string> originalChoices;
        List<int> displayIndexToEnumIndex;
        List<int> enumIndexToDisplayIndex;
        int originalIndex;

        public SerializedDefaultEnumBinding() { }

        public static void CreateBind(PopupField<string> field, SerializedObjectBindingContext context, SerializedProperty property)
        {
            var binding = new SerializedDefaultEnumBinding { isReleased = false };
            field.SetBinding(BindingExtensions.s_SerializedBindingId, binding);
            binding.SetBinding(field, context, property);
        }

        void SetBinding(PopupField<string> c, SerializedObjectBindingContext context, SerializedProperty property)
        {
            property.unsafeMode = true;
            SetContext(context, property);
            originalChoices = c.choices;
            originalIndex = c.index;
            if (displayIndexToEnumIndex == null) displayIndexToEnumIndex = new List<int>();
            else displayIndexToEnumIndex.Clear();
            if (enumIndexToDisplayIndex == null) enumIndexToDisplayIndex = new List<int>();
            else enumIndexToDisplayIndex.Clear();

            var enumType = property.ManagedEnumType;
            if (enumType != null)
            {
                var visibleData = EnumDataUtility.GetCachedEnumData(enumType, UnityEngine.EnumDataUtility.CachedType.ExcludeObsolete);
                var allData = EnumDataUtility.GetCachedEnumData(enumType, UnityEngine.EnumDataUtility.CachedType.IncludeAllObsolete);
                c.choices = new List<string>(visibleData.displayNames);
                string[] enumNames = property.enumNames;

                var nameToFlag = new Dictionary<string, int>(allData.names.Length, StringComparer.Ordinal);
                for (int i = 0; i < allData.names.Length; i++)
                    nameToFlag[allData.names[i]] = allData.flagValues[i];

                for (int i = 0; i < visibleData.names.Length; i++) displayIndexToEnumIndex.Add(kDefaultValueIndex);
                for (int i = 0; i < enumNames.Length; i++) enumIndexToDisplayIndex.Add(kDefaultValueIndex);

                var flagToFirstEnumIndex = new Dictionary<int, int>(enumNames.Length);
                for (int j = 0; j < enumNames.Length; j++)
                {
                    if (!nameToFlag.TryGetValue(enumNames[j], out int flagVal)) continue;
                    int displayIdx = Array.IndexOf(visibleData.names, enumNames[j]);
                    if (displayIdx != -1)
                    {
                        if (flagToFirstEnumIndex.TryGetValue(flagVal, out int firstIdx))
                        {
                            displayIndexToEnumIndex[displayIdx] = firstIdx;
                            enumIndexToDisplayIndex[firstIdx] = displayIdx;
                        }
                        else
                        {
                            flagToFirstEnumIndex[flagVal] = j;
                            displayIndexToEnumIndex[displayIdx] = j;
                        }
                        enumIndexToDisplayIndex[j] = displayIdx;
                    }
                    else
                    {
                        flagToFirstEnumIndex[flagVal] = j;
                    }
                }
            }
            else
            {
                c.choices = new List<string>(property.enumLocalizedDisplayNames);
                for (int k = 0; k < c.choices.Count; k++)
                {
                    displayIndexToEnumIndex.Add(k);
                    enumIndexToDisplayIndex.Add(k);
                }
            }

            lastFieldValueIndex = c.index;
            field = c;
        }

        protected override void SyncPropertyToField(PopupField<string> c, SerializedProperty p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (c == null) throw new ArgumentNullException(nameof(c));
            int enumValueIndex = p.enumValueIndex;
            int displayIndex = enumValueIndex < 0 || enumValueIndex >= enumIndexToDisplayIndex.Count
                ? (lastFieldValueIndex = kDefaultValueIndex)
                : (lastFieldValueIndex = enumIndexToDisplayIndex[enumValueIndex]);
            if (p.hasMultipleDifferentValues) c.SetIndexWithoutNotify(displayIndex);
            else c.index = displayIndex;
        }

        protected override void UpdateLastFieldValue()
        {
            lastFieldValueIndex = field == null ? int.MinValue : field.index;
        }

        protected override bool SyncFieldValueToProperty()
        {
            if (lastFieldValueIndex >= 0 && lastFieldValueIndex < displayIndexToEnumIndex.Count
                && (boundProperty.hasMultipleDifferentValues || boundProperty.enumValueIndex != displayIndexToEnumIndex[lastFieldValueIndex]))
            {
                boundProperty.enumValueIndex = displayIndexToEnumIndex[lastFieldValueIndex];
                boundProperty.serializedObject.ApplyModifiedProperties();
                return true;
            }
            return false;
        }

        public override void OnRelease()
        {
            if (isReleased) return;
            try
            {
                var popupField = field;
                field = null;
                if (popupField != null)
                {
                    popupField.choices = originalChoices;
                    popupField.index = originalIndex;
                }
            }
            catch (ArgumentException)
            {
            }
            ResetContext();
            field = null;
            lastFieldValueIndex = kDefaultValueIndex;
            isReleased = true;
            ResetCachedValues();
        }
    }

    internal partial class SerializedManagedEnumBinding : SerializedObjectBindingToBaseField<Enum, BaseField<Enum>>
    {
        int lastEnumValue;
        Type managedType;

        public SerializedManagedEnumBinding() { }

        public static void CreateBind(BaseField<Enum> field, SerializedObjectBindingContext context, SerializedProperty property)
        {
            var enumType = property.ManagedEnumType;
            if (enumType == null)
            {
                Debug.LogWarning(field.GetType().FullName + " is not compatible with property \"" + property.propertyPath + "\". Make sure you're binding to a managed enum type");
                return;
            }
            var binding = new SerializedManagedEnumBinding { isReleased = false };
            field.SetBinding(BindingExtensions.s_SerializedBindingId, binding);
            binding.SetBinding(field, context, property, enumType);
        }

        void SetBinding(BaseField<Enum> c, SerializedObjectBindingContext context, SerializedProperty property, Type manageType)
        {
            managedType = manageType;
            property.unsafeMode = true;
            SetContext(context, property);
            int enumValueAsInt = property.intValue;
            Enum enumVal = GetEnumFromSerializedFromInt(manageType, ref enumValueAsInt);
            if (c is EnumField ef)
            {
                if (enumVal != null) ef.Init(enumVal);
                else { ef.PopulateDataFromType(manageType); ef.SetValueWithoutNotify(null); }
            }
            else if (c is EnumFlagsField eff)
            {
                if (enumVal != null) eff.Init(enumVal);
                else { eff.PopulateDataFromType(manageType); eff.SetValueWithoutNotify(null); }
            }
            else
            {
                throw new InvalidOperationException(c.GetType() + " cannot be bound to a enum");
            }
            lastEnumValue = enumValueAsInt;
            Enum previous = c.value;
            c.value = enumVal;
            field = c;
            if (EqualityComparer<Enum>.Default.Equals(previous, c.value)) return;
            using var evt = ChangeEvent<Enum>.GetPooled(previous, previous);
            evt.elementTarget = c;
            c.SendEvent(evt);
        }

        static Enum GetEnumFromSerializedFromInt(Type managedType, ref int enumValueAsInt)
        {
            var enumData = EnumDataUtility.GetCachedEnumData(managedType);
            if (enumData.flags) return EnumDataUtility.IntToEnumFlags(managedType, enumValueAsInt);
            int index = Array.IndexOf(enumData.flagValues, enumValueAsInt);
            if (index != -1) return enumData.values[index];
            if (enumData.flagValues.Length != 0)
            {
                int minVal = enumData.flagValues[0];
                int minIdx = 0;
                for (int i = 1; i < enumData.flagValues.Length; i++)
                {
                    if (enumData.flagValues[i] < minVal)
                    {
                        minIdx = i;
                        minVal = enumData.flagValues[i];
                    }
                }
                if (enumValueAsInt <= minVal || (enumValueAsInt == 0 && minVal < 0))
                {
                    enumValueAsInt = minVal;
                    return enumData.values[minIdx];
                }
            }
            Debug.LogWarning("Error: invalid enum value " + enumValueAsInt + " for type " + managedType);
            return null;
        }

        protected override void SyncPropertyToField(BaseField<Enum> c, SerializedProperty p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (c == null) throw new ArgumentNullException(nameof(c));
            int enumValueAsInt = p.intValue;
            Enum enumVal = GetEnumFromSerializedFromInt(managedType, ref enumValueAsInt);
            if (p.hasMultipleDifferentValues) c.SetValueWithoutNotify(enumVal);
            else c.value = enumVal;
            lastEnumValue = enumValueAsInt;
        }

        protected override void UpdateLastFieldValue()
        {
            if (field == null || managedType == null)
            {
                lastEnumValue = 0;
                return;
            }
            var enumData = EnumDataUtility.GetCachedEnumData(managedType);
            Enum current = field.value;
            if (enumData.flags)
            {
                lastEnumValue = EnumDataUtility.EnumFlagsToInt(enumData, current);
                return;
            }
            int index = Array.IndexOf(enumData.values, current);
            if (index != -1)
            {
                lastEnumValue = enumData.flagValues[index];
                return;
            }
            lastEnumValue = 0;
            Debug.LogWarning("Error: invalid enum value " + current + " for type " + managedType);
        }

        protected override bool SyncFieldValueToProperty()
        {
            if (!boundProperty.hasMultipleDifferentValues && lastEnumValue == boundProperty.intValue)
                return false;
            Type underlying = managedType.GetEnumUnderlyingType();
            if (lastEnumValue < 0 && (underlying == typeof(uint) || underlying == typeof(ushort) || underlying == typeof(byte)))
                boundProperty.longValue = unchecked((uint)lastEnumValue);
            else
                boundProperty.intValue = lastEnumValue;
            boundProperty.serializedObject.ApplyModifiedProperties();
            return true;
        }

        public override void OnRelease()
        {
            if (isReleased) return;
            var baseField = field;
            field = null;
            baseField?.SetValueWithoutNotify(null);
            ResetContext();
            field = null;
            managedType = null;
            isReleased = true;
            ResetCachedValues();
        }
    }

    internal partial class SerializedIsExpandedBinding : SerializedObjectBindingPropertyToBaseField<bool, bool>
    {
        static readonly Func<SerializedProperty, bool> s_GetIsExpanded = p => p.isExpanded;
        static readonly Action<SerializedProperty, bool> s_SetIsExpanded = (p, v) => p.isExpanded = v;

        public SerializedIsExpandedBinding() { }

        public static void CreateBind(Foldout field, SerializedObjectBindingContext context, SerializedProperty property)
        {
            var binding = new SerializedIsExpandedBinding { isReleased = false };
            field?.SetBinding(BindingExtensions.s_SerializedBindingId, binding);
            binding.SetBinding(field, context, property);
        }

        protected void SetBinding(Foldout foldout, SerializedObjectBindingContext context, SerializedProperty property)
        {
            property.unsafeMode = true;
            propGetValue = s_GetIsExpanded;
            propSetValue = s_SetIsExpanded;
            propCompareValues = SerializedPropertyHelper.ValueEquals;
            SetContext(context, property);
            lastFieldValue = foldout.value;
            field = foldout;
        }

        public override void OnRelease()
        {
            if (!isReleased) base.OnRelease();
        }

        protected override void UpdateLastFieldValue()
        {
            if (field is Foldout foldout) lastFieldValue = foldout.value;
        }

        protected override void AssignValueToFieldWithoutNotify(bool lastValue)
        {
            if (field is Foldout foldout) foldout.SetValueWithoutNotify(lastValue);
        }

        protected override void AssignValueToField(bool lastValue)
        {
            if (field is Foldout foldout) foldout.value = lastValue;
        }
    }

    internal static partial class SerializedPropertyHelper
    {
        public static bool GetBoolPropertyValue(SerializedProperty p) => p.boolValue;
        public static int GetIntPropertyValue(SerializedProperty p) => p.intValue;
        public static uint GetUIntPropertyValue(SerializedProperty p) => p.uintValue;
        public static long GetLongPropertyValue(SerializedProperty p) => p.longValue;
        public static ulong GetULongPropertyValue(SerializedProperty p) => p.ulongValue;
        public static float GetFloatPropertyValue(SerializedProperty p) => p.floatValue;
        public static double GetDoublePropertyValue(SerializedProperty p) => p.doubleValue;
        public static string GetStringPropertyValue(SerializedProperty p) => p.stringValue;
        public static Color GetColorPropertyValue(SerializedProperty p) => p.colorValue;
        public static Vector2 GetVector2PropertyValue(SerializedProperty p) => p.vector2Value;
        public static Vector3 GetVector3PropertyValue(SerializedProperty p) => p.vector3Value;
        public static Vector4 GetVector4PropertyValue(SerializedProperty p) => p.vector4Value;
        public static Vector2Int GetVector2IntPropertyValue(SerializedProperty p) => p.vector2IntValue;
        public static Vector3Int GetVector3IntPropertyValue(SerializedProperty p) => p.vector3IntValue;
        public static Rect GetRectPropertyValue(SerializedProperty p) => p.rectValue;
        public static RectInt GetRectIntPropertyValue(SerializedProperty p) => p.rectIntValue;
        public static float GetIntPropertyValueAsFloat(SerializedProperty p) => p.intValue;
        public static float GetLongPropertyValueAsFloat(SerializedProperty p) => p.longValue;
        public static double GetFloatPropertyValueAsDouble(SerializedProperty p) => p.floatValue;
        public static float GetDoublePropertyValueAsFloat(SerializedProperty p) => (float)p.doubleValue;

        public static string GetEnumPropertyValueAsString(SerializedProperty p)
        {
            int idx = p.enumValueIndex;
            var names = p.enumDisplayNames;
            return idx >= 0 && idx < names.Length ? names[idx] : string.Empty;
        }

        public static void SetBoolPropertyValue(SerializedProperty p, bool v) => p.boolValue = v;
        public static void SetIntPropertyValue(SerializedProperty p, int v) => p.intValue = v;
        public static void SetUIntPropertyValue(SerializedProperty p, uint v) => p.uintValue = v;
        public static void SetLongPropertyValue(SerializedProperty p, long v) => p.longValue = v;
        public static void SetULongPropertyValue(SerializedProperty p, ulong v) => p.ulongValue = v;
        public static void SetFloatPropertyValue(SerializedProperty p, float v) => p.floatValue = v;
        public static void SetDoublePropertyValue(SerializedProperty p, double v) => p.doubleValue = v;
        public static void SetStringPropertyValue(SerializedProperty p, string v) => p.stringValue = v;
        public static void SetColorPropertyValue(SerializedProperty p, Color v) => p.colorValue = v;
        static void SetFloatComponentIfDifferent(SerializedProperty p, string name, float current, float next)
        {
            if (!current.Equals(next)) p.FindPropertyRelative(name).doubleValue = next;
        }

        public static void SetVector2PropertyValue(SerializedProperty p, Vector2 v)
        {
            if (p.hasMultipleDifferentValues)
            {
                Vector2 current = p.vector2Value;
                SetFloatComponentIfDifferent(p, "x", current.x, v.x);
                SetFloatComponentIfDifferent(p, "y", current.y, v.y);
            }
            else
            {
                p.vector2Value = v;
            }
        }

        public static void SetVector3PropertyValue(SerializedProperty p, Vector3 v)
        {
            if (p.hasMultipleDifferentValues)
            {
                Vector3 current = p.vector3Value;
                SetFloatComponentIfDifferent(p, "x", current.x, v.x);
                SetFloatComponentIfDifferent(p, "y", current.y, v.y);
                SetFloatComponentIfDifferent(p, "z", current.z, v.z);
            }
            else
            {
                p.vector3Value = v;
            }
        }

        public static void SetVector4PropertyValue(SerializedProperty p, Vector4 v)
        {
            if (p.hasMultipleDifferentValues)
            {
                Vector4 current = p.vector4Value;
                SetFloatComponentIfDifferent(p, "x", current.x, v.x);
                SetFloatComponentIfDifferent(p, "y", current.y, v.y);
                SetFloatComponentIfDifferent(p, "z", current.z, v.z);
                SetFloatComponentIfDifferent(p, "w", Unbound.Runtime.Compat.Vector4MultiEditComparesZToW ? current.z : current.w, v.w);
            }
            else
            {
                p.vector4Value = v;
            }
        }

        static void SetIntComponentIfDifferent(SerializedProperty p, string name, int current, int next)
        {
            if (!current.Equals(next)) p.FindPropertyRelative(name).intValue = next;
        }

        public static void SetVector2IntPropertyValue(SerializedProperty p, Vector2Int v)
        {
            if (p.hasMultipleDifferentValues)
            {
                Vector2Int current = p.vector2IntValue;
                SetIntComponentIfDifferent(p, "x", current.x, v.x);
                SetIntComponentIfDifferent(p, "y", current.y, v.y);
            }
            else
            {
                p.vector2IntValue = v;
            }
        }

        public static void SetVector3IntPropertyValue(SerializedProperty p, Vector3Int v)
        {
            if (p.hasMultipleDifferentValues)
            {
                Vector3Int current = p.vector3IntValue;
                SetIntComponentIfDifferent(p, "x", current.x, v.x);
                SetIntComponentIfDifferent(p, "y", current.y, v.y);
                SetIntComponentIfDifferent(p, "z", current.z, v.z);
            }
            else
            {
                p.vector3IntValue = v;
            }
        }

        public static void SetRectPropertyValue(SerializedProperty p, Rect v) => p.rectValue = v;
        public static void SetRectIntPropertyValue(SerializedProperty p, RectInt v) => p.rectIntValue = v;
        public static void SetFloatPropertyValueFromDouble(SerializedProperty p, double v) => p.floatValue = (float)v;
        public static void SetDoublePropertyValueFromFloat(SerializedProperty p, float v) => p.doubleValue = v;

        public static void SetEnumPropertyValueFromString(SerializedProperty p, string v) =>
            p.enumValueIndex = FindStringIndex(p.enumDisplayNames, v);

        public static int FindStringIndex(string[] values, string v)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == v) return i;
            }
            return -1;
        }

        public static bool ValueEquals<TValue>(TValue value, SerializedProperty p, Func<SerializedProperty, TValue> propertyReadFunc) =>
            EqualityComparer<TValue>.Default.Equals(value, propertyReadFunc(p));

        public static bool ValueEquals(string value, SerializedProperty p, Func<SerializedProperty, string> propertyReadFunc) =>
            p.propertyType == SerializedPropertyType.Enum
                ? p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length
                  && string.Equals(p.enumDisplayNames[p.enumValueIndex], value, StringComparison.OrdinalIgnoreCase)
                : string.Equals(value, propertyReadFunc(p), StringComparison.Ordinal);

        public static bool SlowEnumValueEquals(string value, SerializedProperty p, Func<SerializedProperty, string> propertyReadFunc) =>
            EqualityComparer<string>.Default.Equals(value, propertyReadFunc(p));
    }
}
