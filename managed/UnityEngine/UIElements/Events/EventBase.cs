using System;
using System.Collections.Generic;

// UI Toolkit events: the base types, callback registration and the dispatcher. Events dispatch as
// Unity 6 dispatches them: an event sent while another is processed waits in a queue until the
// outer one (and its pre/post dispatch work) is done; propagation goes down the trickle-down path
// from the root to the target, then up the bubble-up path from the target, running at each element
// its callbacks and its HandleEvent methods (the target and composite roots also run their default
// actions), skipping disabled elements for events that skip them.

namespace UnityEngine.UIElements
{
    public delegate void EventCallback<in TEventType>(TEventType evt);
    public delegate void EventCallback<in TEventType, in TCallbackArgs>(TEventType evt, TCallbackArgs userArgs);

    public partial interface IEventHandler
    {
        void SendEvent(EventBase e);
        void HandleEvent(EventBase evt);
        bool HasTrickleDownHandlers();
        bool HasBubbleUpHandlers();
    }

    public abstract partial class EventBase : IDisposable
    {
        [Flags]
        internal enum EventPropagation
        {
            None = 0,
            Bubbles = 1,
            TricklesDown = 2,
            SkipDisabledElements = 4,
            BubblesOrTricklesDown = Bubbles | TricklesDown,
        }

        static long s_LastTypeId;
        static ulong s_NextEventId;

        IEventHandler m_CurrentTarget;
        Event m_ImguiEvent;
        bool m_ImguiEventIsValid;

        public virtual long eventTypeId => -1L;
        public long timestamp { get; private set; }
        internal ulong eventId { get; private set; }
        internal ulong triggerEventId { get; private set; }
        internal EventPropagation propagation { get; set; }

        public bool bubbles
        {
            get => (propagation & EventPropagation.Bubbles) != 0;
            protected set => propagation = value ? propagation | EventPropagation.Bubbles : propagation & ~EventPropagation.Bubbles;
        }

        public bool tricklesDown
        {
            get => (propagation & EventPropagation.TricklesDown) != 0;
            protected set => propagation = value ? propagation | EventPropagation.TricklesDown : propagation & ~EventPropagation.TricklesDown;
        }

        internal bool skipDisabledElements
        {
            get => (propagation & EventPropagation.SkipDisabledElements) != 0;
            set => propagation = value ? propagation | EventPropagation.SkipDisabledElements : propagation & ~EventPropagation.SkipDisabledElements;
        }

        internal bool bubblesOrTricklesDown => (propagation & EventPropagation.BubblesOrTricklesDown) != 0;

        internal VisualElement elementTarget { get; set; }

        public IEventHandler target
        {
            get => elementTarget;
            set => elementTarget = value as VisualElement;
        }

        public bool isPropagationStopped { get; private set; }
        public bool isImmediatePropagationStopped { get; private set; }

        [Obsolete("Use isPropagationStopped. Before proceeding, make sure you understand the latest changes to UIToolkit event propagation rules by visiting Unity's manual page https://docs.unity3d.com/Manual/UIE-Events-Dispatching.html")]
        public bool isDefaultPrevented => isPropagationStopped;

        public PropagationPhase propagationPhase { get; internal set; }

        public virtual IEventHandler currentTarget
        {
            get => m_CurrentTarget;
            internal set
            {
                m_CurrentTarget = value;
                if (imguiEvent != null)
                    imguiEvent.mousePosition = value is VisualElement ve ? ve.WorldToLocal(originalMousePosition) : originalMousePosition;
            }
        }

        bool m_Dispatching;
        bool m_Dispatched;

        public bool dispatch
        {
            get => m_Dispatching;
            internal set
            {
                m_Dispatching = value;
                if (value) m_Dispatched = true;
            }
        }

        internal bool processed { get; private set; }
        internal bool processedByFocusController { get; set; }
        internal bool propagateToIMGUI { get; set; }

        public Event imguiEvent
        {
            get => m_ImguiEventIsValid ? m_ImguiEvent : null;
            protected set
            {
                if (m_ImguiEvent == null) m_ImguiEvent = new Event();
                if (value != null)
                {
                    m_ImguiEvent.CopyFrom(value);
                    m_ImguiEventIsValid = true;
                    originalMousePosition = value.mousePosition;
                }
                else
                {
                    m_ImguiEventIsValid = false;
                }
            }
        }

        public Vector2 originalMousePosition { get; private set; }

        protected bool pooled { get; set; }

        protected static long RegisterEventType() => ++s_LastTypeId;

        internal void SetTriggerEventId(ulong id) => triggerEventId = id;

        [Obsolete("Override PreDispatch(IPanel panel) instead.")]
        protected virtual void PreDispatch() { }

#pragma warning disable CS0618
        protected internal virtual void PreDispatch(IPanel panel) => PreDispatch();

        [Obsolete("Override PostDispatch(IPanel panel) instead.")]
        protected virtual void PostDispatch() { }

        protected internal virtual void PostDispatch(IPanel panel)
        {
            PostDispatch();
            processed = true;
        }
#pragma warning restore CS0618

        internal virtual void Dispatch(BaseVisualElementPanel panel) => Unbound.UIElements.Dispatching.DefaultDispatch(this, panel);

        public void StopPropagation() => isPropagationStopped = true;

        public void StopImmediatePropagation()
        {
            isPropagationStopped = true;
            isImmediatePropagationStopped = true;
        }

        [Obsolete("Use StopPropagation and/or FocusController.IgnoreEvent. Before proceeding, make sure you understand the latest changes to UIToolkit event propagation rules by visiting Unity's manual page https://docs.unity3d.com/Manual/UIE-Events-Dispatching.html")]
        public void PreventDefault()
        {
            StopPropagation();
            elementTarget?.focusController?.IgnoreEvent(this);
        }

        internal void MarkReceivedByDispatcher() => m_Dispatched = true;

        protected virtual void Init() => LocalInit();

        void LocalInit()
        {
            timestamp = Panel.TimeSinceStartupMs();
            triggerEventId = 0;
            eventId = s_NextEventId++;
            propagation = EventPropagation.None;
            elementTarget = null;
            isPropagationStopped = false;
            isImmediatePropagationStopped = false;
            propagationPhase = PropagationPhase.None;
            originalMousePosition = Vector2.zero;
            m_CurrentTarget = null;
            m_Dispatching = false;
            m_Dispatched = false;
            propagateToIMGUI = true;
            processed = false;
            processedByFocusController = false;
            m_ImguiEventIsValid = false;
            pooled = false;
        }

        protected EventBase() { LocalInit(); }

        internal abstract void Acquire();
        public abstract void Dispose();
    }

    public abstract partial class EventBase<T> : EventBase where T : EventBase<T>, new()
    {
        static readonly long s_TypeId = RegisterEventType();
        static readonly Stack<T> s_Pool = new Stack<T>();
        static Func<T> s_Create = () => new T();
        int m_RefCount;

        public override long eventTypeId => s_TypeId;

        internal static void SetCreateFunction(Func<T> createMethod) => s_Create = createMethod;

        protected EventBase() { m_RefCount = 0; }

        public static long TypeId() => s_TypeId;

        protected override void Init()
        {
            base.Init();
            if (m_RefCount != 0)
            {
                Debug.Log("Event improperly released.");
                m_RefCount = 0;
            }
        }

        public static T GetPooled()
        {
            T evt = s_Pool.Count > 0 ? s_Pool.Pop() : s_Create();
            evt.Init();
            evt.pooled = true;
            evt.Acquire();
            return evt;
        }

        internal static T GetPooled(EventBase e)
        {
            T evt = GetPooled();
            if (e != null) evt.SetTriggerEventId(e.eventId);
            return evt;
        }

        static void ReleasePooled(T evt)
        {
            if (!evt.pooled) return;
            evt.Init();
            s_Pool.Push(evt);
            evt.pooled = false;
        }

        internal override void Acquire() => m_RefCount++;

        public sealed override void Dispose()
        {
            if (--m_RefCount == 0) ReleasePooled((T)this);
        }
    }

    [Flags]
    internal enum UaInvokePolicy
    {
        Default = 0,
        IncludeDisabled = 1,
        Once = 2,
    }

    public abstract partial class CallbackEventHandler : IEventHandler
    {
        internal bool isIMGUIContainer;
        internal Unbound.UIElements.CallbackRegistry m_UaCallbacks;

        protected CallbackEventHandler() { }

        Unbound.UIElements.CallbackRegistry Callbacks => m_UaCallbacks ??= new Unbound.UIElements.CallbackRegistry();

        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null) throw new ArgumentException("callback parameter is null");
            Callbacks.Register(EventBase<TEventType>.TypeId(), callback, e => callback((TEventType)e), useTrickleDown, UaInvokePolicy.Default);
        }

        public void RegisterCallbackOnce<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null) throw new ArgumentException("callback parameter is null");
            Callbacks.Register(EventBase<TEventType>.TypeId(), callback, e => callback((TEventType)e), useTrickleDown, UaInvokePolicy.Once);
        }

        public void RegisterCallback<TEventType, TUserArgsType>(EventCallback<TEventType, TUserArgsType> callback, TUserArgsType userArgs, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null) throw new ArgumentException("callback parameter is null");
            Callbacks.Register(EventBase<TEventType>.TypeId(), callback, e => callback((TEventType)e, userArgs), useTrickleDown, UaInvokePolicy.Default, replaceArgs: true);
        }

        public void RegisterCallbackOnce<TEventType, TUserArgsType>(EventCallback<TEventType, TUserArgsType> callback, TUserArgsType userArgs, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null) throw new ArgumentException("callback parameter is null");
            Callbacks.Register(EventBase<TEventType>.TypeId(), callback, e => callback((TEventType)e, userArgs), useTrickleDown, UaInvokePolicy.Once, replaceArgs: true);
        }

        /// <summary>A callback that also runs on disabled elements (Unity's controls register some so).</summary>
        internal void UaRegisterCallback<TEventType>(EventCallback<TEventType> callback, UaInvokePolicy policy, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            Callbacks.Register(EventBase<TEventType>.TypeId(), callback, e => callback((TEventType)e), useTrickleDown, policy);
        }

        public void UnregisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null) throw new ArgumentException("callback parameter is null");
            m_UaCallbacks?.Unregister(EventBase<TEventType>.TypeId(), callback, useTrickleDown);
        }

        public void UnregisterCallback<TEventType, TUserArgsType>(EventCallback<TEventType, TUserArgsType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) where TEventType : EventBase<TEventType>, new()
        {
            if (callback == null) throw new ArgumentException("callback parameter is null");
            m_UaCallbacks?.Unregister(EventBase<TEventType>.TypeId(), callback, useTrickleDown);
        }

        public abstract void SendEvent(EventBase e);
        internal abstract void SendEvent(EventBase e, DispatchMode dispatchMode);
        internal abstract void HandleEvent(EventBase e);

        void IEventHandler.HandleEvent(EventBase evt)
        {
            if (evt != null) HandleEvent(evt);
        }

        public bool HasTrickleDownHandlers() => m_UaCallbacks != null && m_UaCallbacks.trickleDown.Count > 0;
        public bool HasBubbleUpHandlers() => m_UaCallbacks != null && m_UaCallbacks.bubbleUp.Count > 0;

        [Obsolete("Use HandleEventBubbleUp. Before proceeding, make sure you understand the latest changes to UIToolkit event propagation rules by visiting Unity's manual page https://docs.unity3d.com/Manual/UIE-Events-Dispatching.html")]
        protected virtual void ExecuteDefaultActionAtTarget(EventBase evt) { }
        protected virtual void HandleEventBubbleUp(EventBase evt) { }
        internal virtual void HandleEventBubbleUpDisabled(EventBase evt) { }
        protected virtual void HandleEventTrickleDown(EventBase evt) { }
        internal virtual void HandleEventTrickleDownDisabled(EventBase evt) { }
        [Obsolete("Use HandleEventBubbleUp. Before proceeding, make sure you understand the latest changes to UIToolkit event propagation rules by visiting Unity's manual page https://docs.unity3d.com/Manual/UIE-Events-Dispatching.html")]
        protected virtual void ExecuteDefaultAction(EventBase evt) { }
        internal virtual void ExecuteDefaultActionDisabledAtTarget(EventBase evt) { }
        internal virtual void ExecuteDefaultActionDisabled(EventBase evt) { }

#pragma warning disable CS0618
        internal void HandleEventBubbleUpInternal(EventBase evt) => HandleEventBubbleUp(evt);
        internal void HandleEventTrickleDownInternal(EventBase evt) => HandleEventTrickleDown(evt);
        internal void ExecuteDefaultActionInternal(EventBase evt) => ExecuteDefaultAction(evt);
        internal void ExecuteDefaultActionDisabledInternal(EventBase evt) => ExecuteDefaultActionDisabled(evt);
        internal void ExecuteDefaultActionAtTargetInternal(EventBase evt) => ExecuteDefaultActionAtTarget(evt);
        internal void ExecuteDefaultActionDisabledAtTargetInternal(EventBase evt) => ExecuteDefaultActionDisabledAtTarget(evt);
#pragma warning restore CS0618
    }

    public sealed partial class EventDispatcher
    {
        struct Record
        {
            public EventBase Event;
            public BaseVisualElementPanel Panel;
        }

        Queue<Record> m_Queue = new Queue<Record>();
        uint m_GateCount;
        uint m_GateDepth;
        readonly Stack<(uint Gates, Queue<Record> Queue)> m_Contexts = new Stack<(uint, Queue<Record>)>();

        internal Unbound.UIElements.ClickDetection m_UaClickDetector = new Unbound.UIElements.ClickDetection();
        internal Unbound.UIElements.PointerCaptureState m_UaPointerState = new Unbound.UIElements.PointerCaptureState();

        internal uint GateDepth => m_GateDepth;
        internal bool processingEvents { get; private set; }

        internal static EventDispatcher CreateDefault() => new EventDispatcher();

        [Obsolete("Please use EventDispatcher.CreateDefault().")]
        internal EventDispatcher() { }

        internal void Dispatch(EventBase evt, BaseVisualElementPanel panel, DispatchMode dispatchMode)
        {
            evt.MarkReceivedByDispatcher();
            if (m_GateCount == 0 || dispatchMode == DispatchMode.Immediate)
            {
                ProcessEvent(evt, panel);
                return;
            }
            if (m_GateDepth > 500)
            {
                Debug.LogErrorFormat("Ignoring event {0}: too many events dispatched recurively", evt);
                return;
            }
            evt.Acquire();
            m_Queue.Enqueue(new Record { Event = evt, Panel = panel });
        }

        internal void PushDispatcherContext()
        {
            ProcessEventQueue();
            m_Contexts.Push((m_GateCount, m_Queue));
            m_GateCount = 0;
            m_Queue = new Queue<Record>();
        }

        internal void PopDispatcherContext()
        {
            var (gates, queue) = m_Contexts.Pop();
            m_GateCount = gates;
            m_Queue = queue;
        }

        internal void CloseGate()
        {
            m_GateCount++;
            m_GateDepth++;
        }

        internal void OpenGate()
        {
            if (m_GateCount != 0) m_GateCount--;
            try
            {
                if (m_GateCount == 0) ProcessEventQueue();
            }
            finally
            {
                if (m_GateDepth != 0) m_GateDepth--;
            }
        }

        void ProcessEventQueue()
        {
            var queue = m_Queue;
            m_Queue = new Queue<Record>();
            ExitGUIException exit = null;
            try
            {
                processingEvents = true;
                while (queue.Count > 0)
                {
                    var record = queue.Dequeue();
                    try
                    {
                        ProcessEvent(record.Event, record.Panel);
                    }
                    catch (ExitGUIException e)
                    {
                        exit = e;
                    }
                    finally
                    {
                        record.Event.Dispose();
                    }
                }
            }
            finally
            {
                processingEvents = false;
            }
            if (exit != null) throw exit;
        }

        void ProcessEvent(EventBase evt, BaseVisualElementPanel panel)
        {
            if (panel.disposed) return;
            CloseGate();
            try
            {
                evt.PreDispatch(panel);
                evt.Dispatch(panel);
                evt.PostDispatch(panel);
            }
            finally
            {
                OpenGate();
            }
        }
    }

    /// <summary>Holds the dispatcher's queue closed (events sent meanwhile wait) for its lifetime.</summary>
    internal struct UaEventDispatcherGate : IDisposable
    {
        readonly EventDispatcher m_Dispatcher;

        public UaEventDispatcherGate(EventDispatcher d)
        {
            m_Dispatcher = d;
            d?.CloseGate();
        }

        public void Dispose() => m_Dispatcher?.OpenGate();
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>An element's callbacks per phase, in registration order.</summary>
    internal sealed class CallbackRegistry
    {
        internal sealed class Entry
        {
            public long TypeId;
            public Delegate Original;
            public Action<EventBase> Invoke;
            public UaInvokePolicy Policy;
        }

        internal List<Entry> trickleDown = new List<Entry>();
        internal List<Entry> bubbleUp = new List<Entry>();
        int m_Invoking;

        List<Entry> ListFor(TrickleDown phase) => phase == TrickleDown.TrickleDown ? trickleDown : bubbleUp;

        internal void Register(long typeId, Delegate original, Action<EventBase> invoke, TrickleDown phase, UaInvokePolicy policy, bool replaceArgs = false)
        {
            var list = ListFor(phase);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].TypeId == typeId && Equals(list[i].Original, original))
                {
                    // The same callback again: user args are replaced, otherwise nothing changes.
                    if (replaceArgs)
                    {
                        if (m_Invoking > 0) { list = new List<Entry>(list); SetList(phase, list); }
                        list[i] = new Entry { TypeId = typeId, Original = original, Invoke = invoke, Policy = policy };
                    }
                    return;
                }
            }
            if (m_Invoking > 0) { list = new List<Entry>(list); SetList(phase, list); }
            list.Add(new Entry { TypeId = typeId, Original = original, Invoke = invoke, Policy = policy });
        }

        void SetList(TrickleDown phase, List<Entry> list)
        {
            if (phase == TrickleDown.TrickleDown) trickleDown = list;
            else bubbleUp = list;
        }

        internal bool Unregister(long typeId, Delegate original, TrickleDown phase)
        {
            var list = ListFor(phase);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].TypeId == typeId && Equals(list[i].Original, original))
                {
                    if (m_Invoking > 0) { list = new List<Entry>(list); SetList(phase, list); }
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        internal void Invoke(EventBase evt, TrickleDown phase, VisualElement target, BaseVisualElementPanel panel)
        {
            var list = ListFor(phase);
            if (list.Count == 0) return;
            m_Invoking++;
            try
            {
                bool enabled = !evt.skipDisabledElements || target.enabledInHierarchy;
                long typeId = evt.eventTypeId;
                foreach (var entry in list)
                {
                    if (entry.TypeId != typeId || target.elementPanel != panel) continue;
                    if (!enabled && (entry.Policy & UaInvokePolicy.IncludeDisabled) == 0) continue;
                    if ((entry.Policy & UaInvokePolicy.Once) != 0) Unregister(entry.TypeId, entry.Original, phase);
                    entry.Invoke(evt);
                    if (evt.isImmediatePropagationStopped) break;
                }
            }
            finally
            {
                m_Invoking--;
            }
        }
    }

    /// <summary>Propagation of events through the tree, as Unity's dispatch utilities do it.</summary>
    internal static class Dispatching
    {
        static bool Disabled(EventBase evt, VisualElement target) => evt.skipDisabledElements && !target.enabledInHierarchy;

        static void TrickleDownCallbacks(EventBase evt, BaseVisualElementPanel panel, VisualElement element) =>
            element.m_UaCallbacks?.Invoke(evt, TrickleDown.TrickleDown, element, panel);

        static void BubbleUpCallbacks(EventBase evt, BaseVisualElementPanel panel, VisualElement element) =>
            element.m_UaCallbacks?.Invoke(evt, TrickleDown.NoTrickleDown, element, panel);

        static void TrickleDownHandleEvent(EventBase evt, BaseVisualElementPanel panel, VisualElement element, bool disabled)
        {
            if (element.elementPanel != panel) return;
            if (disabled) element.HandleEventTrickleDownDisabled(evt);
            else element.HandleEventTrickleDownInternal(evt);
        }

        static void DefaultActionAtTarget(EventBase evt, BaseVisualElementPanel panel, VisualElement element, bool disabled)
        {
            if (element.elementPanel != panel) return;
            if (disabled) element.ExecuteDefaultActionDisabledAtTargetInternal(evt);
            else element.ExecuteDefaultActionAtTargetInternal(evt);
        }

        static void DefaultAction(EventBase evt, BaseVisualElementPanel panel, VisualElement element, bool disabled)
        {
            if (element.elementPanel != panel) return;
            if (disabled) element.ExecuteDefaultActionDisabledInternal(evt);
            else element.ExecuteDefaultActionInternal(evt);
        }

        static void BubbleUpHandleEvent(EventBase evt, BaseVisualElementPanel panel, VisualElement element, bool disabled)
        {
            if (element.elementPanel != panel) return;
            if (disabled) element.HandleEventBubbleUpDisabled(evt);
            else element.HandleEventBubbleUpInternal(evt);
        }

        static void BubbleUpAllDefaultActions(EventBase evt, BaseVisualElementPanel panel, VisualElement element, bool disabled, bool isCapturingTarget)
        {
            bool handle = element == evt.target || !isCapturingTarget;
            bool atTarget = element == evt.target || element.isCompositeRoot;
            if (atTarget) DefaultActionAtTarget(evt, panel, element, disabled);
            if (handle) BubbleUpHandleEvent(evt, panel, element, disabled);
            if (atTarget) DefaultAction(evt, panel, element, disabled);
        }

        internal static void HandleEventAtTargetAndDefaultPhase(EventBase evt, BaseVisualElementPanel panel, VisualElement target)
        {
            if (evt.isPropagationStopped) return;
            evt.currentTarget = target;
            try
            {
                evt.propagationPhase = PropagationPhase.TrickleDown;
                TrickleDownCallbacks(evt, panel, target);
                if (evt.isImmediatePropagationStopped) return;
                TrickleDownHandleEvent(evt, panel, target, Disabled(evt, target));
                if (evt.isPropagationStopped) return;
                evt.propagationPhase = PropagationPhase.BubbleUp;
                bool disabled = Disabled(evt, target);
                DefaultActionAtTarget(evt, panel, target, disabled);
                BubbleUpHandleEvent(evt, panel, target, disabled);
                DefaultAction(evt, panel, target, disabled);
                if (evt.isImmediatePropagationStopped) return;
                BubbleUpCallbacks(evt, panel, target);
            }
            finally
            {
                evt.currentTarget = null;
                evt.propagationPhase = PropagationPhase.None;
            }
        }

        static void BuildPaths(VisualElement target, EventBase evt, List<VisualElement> trickle, List<VisualElement> bubble)
        {
            trickle.Add(target);
            bubble.Add(target);
            for (var p = target.hierarchy.parent; p != null; p = p.hierarchy.parent)
            {
                if (evt.tricklesDown) trickle.Add(p);
                if (evt.bubbles) bubble.Add(p);
            }
        }

        static void PropagateEvent(EventBase evt, BaseVisualElementPanel panel, VisualElement target, bool isCapturingTarget)
        {
            if (evt is IPointerEventInternal pe && pe.compatibilityMouseEvent is EventBase compat)
                PropagateWithCompatibility(evt, compat, panel, target, isCapturingTarget);
            else
                Propagate(evt, panel, target, isCapturingTarget);
        }

        static void Propagate(EventBase evt, BaseVisualElementPanel panel, VisualElement target, bool isCapturingTarget)
        {
            if (evt.isPropagationStopped) return;
            var trickle = new List<VisualElement>(8);
            var bubble = new List<VisualElement>(8);
            BuildPaths(target, evt, trickle, bubble);
            try
            {
                evt.dispatch = true;
                evt.propagationPhase = PropagationPhase.TrickleDown;
                int i = trickle.Count - 1;
                if (isCapturingTarget && i >= 0) i = trickle[0] == target ? 0 : -1;
                for (; i >= 0; i--)
                {
                    var element = trickle[i];
                    evt.currentTarget = element;
                    TrickleDownCallbacks(evt, panel, element);
                    if (evt.isImmediatePropagationStopped) return;
                    TrickleDownHandleEvent(evt, panel, element, Disabled(evt, element));
                    if (evt.isPropagationStopped) return;
                }
                evt.propagationPhase = PropagationPhase.BubbleUp;
                foreach (var element in bubble)
                {
                    evt.currentTarget = element;
                    BubbleUpAllDefaultActions(evt, panel, element, Disabled(evt, element), isCapturingTarget);
                    if (evt.isImmediatePropagationStopped) break;
                    if (!isCapturingTarget || element == target) BubbleUpCallbacks(evt, panel, element);
                    if (evt.isPropagationStopped) break;
                }
            }
            finally
            {
                evt.currentTarget = null;
                evt.propagationPhase = PropagationPhase.None;
                evt.dispatch = false;
            }
        }

        static void PropagateWithCompatibility(EventBase evt, EventBase compat, BaseVisualElementPanel panel, VisualElement target, bool isCapturingTarget)
        {
            if (evt.isPropagationStopped || compat.isPropagationStopped) return;
            compat.elementTarget = target;
            compat.skipDisabledElements = evt.skipDisabledElements;
            var trickle = new List<VisualElement>(8);
            var bubble = new List<VisualElement>(8);
            BuildPaths(target, evt, trickle, bubble);
            bool sendCompat = panel.ShouldSendCompatibilityMouseEvents((IPointerEvent)evt);
            try
            {
                evt.dispatch = true;
                evt.propagationPhase = PropagationPhase.TrickleDown;
                compat.propagationPhase = PropagationPhase.TrickleDown;
                int i = trickle.Count - 1;
                if (isCapturingTarget && i >= 0) i = trickle[0] == target ? 0 : -1;
                for (; i >= 0; i--)
                {
                    var element = trickle[i];
                    evt.currentTarget = element;
                    compat.currentTarget = element;
                    TrickleDownCallbacks(evt, panel, element);
                    if (evt.isImmediatePropagationStopped) return;
                    if (sendCompat)
                    {
                        TrickleDownCallbacks(compat, panel, element);
                        if (evt.isImmediatePropagationStopped) return;
                    }
                    bool disabled = Disabled(evt, element);
                    TrickleDownHandleEvent(evt, panel, element, disabled);
                    if (evt.isImmediatePropagationStopped) return;
                    if (sendCompat)
                    {
                        TrickleDownHandleEvent(compat, panel, element, disabled);
                        if (compat.isImmediatePropagationStopped) return;
                    }
                    if (evt.isPropagationStopped || compat.isPropagationStopped) return;
                }
                evt.propagationPhase = PropagationPhase.BubbleUp;
                compat.propagationPhase = PropagationPhase.BubbleUp;
                foreach (var element in bubble)
                {
                    evt.currentTarget = element;
                    compat.currentTarget = element;
                    bool disabled = Disabled(evt, element);
                    BubbleUpAllDefaultActions(evt, panel, element, disabled, isCapturingTarget);
                    if (evt.isImmediatePropagationStopped) break;
                    if (sendCompat)
                    {
                        BubbleUpAllDefaultActions(compat, panel, element, disabled, isCapturingTarget);
                        if (compat.isImmediatePropagationStopped) break;
                    }
                    if (!isCapturingTarget || element == target)
                    {
                        BubbleUpCallbacks(evt, panel, element);
                        if (evt.isImmediatePropagationStopped) break;
                        if (sendCompat)
                        {
                            BubbleUpCallbacks(compat, panel, element);
                            if (compat.isImmediatePropagationStopped) break;
                        }
                    }
                    if (evt.isPropagationStopped || compat.isPropagationStopped) break;
                }
            }
            finally
            {
                evt.currentTarget = null;
                evt.propagationPhase = PropagationPhase.None;
                compat.currentTarget = null;
                compat.propagationPhase = PropagationPhase.None;
                evt.dispatch = false;
            }
        }

        internal static void HandleEvent(EventBase evt, VisualElement target)
        {
            if (evt.isPropagationStopped) return;
            var panel = target.elementPanel;
            bool disabled = Disabled(evt, target);
            switch (evt.propagationPhase)
            {
                case PropagationPhase.TrickleDown:
                    TrickleDownCallbacks(evt, panel, target);
                    if (!evt.isImmediatePropagationStopped) TrickleDownHandleEvent(evt, panel, target, disabled);
                    break;
                case PropagationPhase.BubbleUp:
                    BubbleUpAllDefaultActions(evt, panel, target, disabled, false);
                    if (!evt.isImmediatePropagationStopped) BubbleUpCallbacks(evt, panel, target);
                    break;
            }
        }

        internal static void DispatchToFocusedElementOrPanelRoot(EventBase evt, BaseVisualElementPanel panel)
        {
            var target = evt.elementTarget;
            if (target == null)
            {
                target = panel.focusController.GetLeafFocusedElement() as VisualElement ?? panel.visualTree;
                if (panel.GetCapturingElement(PointerId.mousePointerId) is VisualElement capturing && capturing != target && !capturing.Contains(target))
                {
                    evt.elementTarget = capturing;
                    bool skip = evt.skipDisabledElements;
                    evt.skipDisabledElements = false;
                    HandleEventAtTargetAndDefaultPhase(evt, panel, capturing);
                    evt.skipDisabledElements = skip;
                }
                evt.elementTarget = target;
            }
            PropagateEvent(evt, panel, target, false);
        }

        internal static void DispatchToElementUnderPointerOrPanelRoot(EventBase evt, BaseVisualElementPanel panel, int pointerId, Vector2 position)
        {
            var under = panel.RecomputeTopElementUnderPointer(pointerId, position, evt);
            var target = evt.elementTarget;
            if (target == null)
            {
                target = under ?? panel.visualTree;
                evt.elementTarget = target;
            }
            PropagateEvent(evt, panel, target, false);
        }

        internal static void DispatchToCachedElementUnderPointerOrPanelRoot(EventBase evt, BaseVisualElementPanel panel, int pointerId, Vector2 position)
        {
            var target = evt.elementTarget;
            if (target == null)
            {
                target = panel.GetTopElementUnderPointer(pointerId) ?? panel.visualTree;
                evt.elementTarget = target;
            }
            PropagateEvent(evt, panel, target, false);
        }

        internal static void DispatchToAssignedTarget(EventBase evt, BaseVisualElementPanel panel)
        {
            var target = evt.elementTarget;
            if (target == null) throw new ArgumentException($"Event target not set. Event type {evt.GetType()} requires a target.");
            PropagateEvent(evt, panel, target, false);
        }

        internal static void DefaultDispatch(EventBase evt, BaseVisualElementPanel panel)
        {
            var target = evt.elementTarget;
            if (target == null) return;
            if (evt.bubblesOrTricklesDown) PropagateEvent(evt, panel, target, false);
            else HandleEventAtTargetAndDefaultPhase(evt, panel, target);
        }

        internal static void DispatchToCapturingElementOrElementUnderPointer(EventBase evt, BaseVisualElementPanel panel, int pointerId, Vector2 position)
        {
            panel.RecomputeTopElementUnderPointer(pointerId, position, evt);
            if (!DispatchToCapturingElement(evt, panel, pointerId))
                DispatchToCachedElementUnderPointerOrPanelRoot(evt, panel, pointerId, position);
        }

        static bool DispatchToCapturingElement(EventBase evt, BaseVisualElementPanel panel, int pointerId)
        {
            if (!(panel.GetCapturingElement(pointerId) is VisualElement capturing)) return false;
            if (capturing.panel == null)
            {
                panel.ReleasePointer(pointerId);
                return false;
            }
            if (evt.target != null && evt.target != capturing) return false;
            if (capturing.panel != panel) return false;
            evt.skipDisabledElements = false;
            evt.elementTarget = capturing;
            PropagateEvent(evt, panel, capturing, true);
            return true;
        }

        internal static void DispatchToPanelRoot(EventBase evt, BaseVisualElementPanel panel)
        {
            evt.elementTarget = panel.visualTree;
            PropagateEvent(evt, panel, panel.visualTree, false);
        }
    }
}
