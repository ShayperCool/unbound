using System.Collections.Generic;
using Unity.IntegerTime;
using UnityEngine.Bindings;

namespace UnityEngine.InputForUI
{
    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal delegate bool EventConsumer(in Event ev);

    /// <summary>A source of InputForUI events: the legacy Input Manager's
    /// (<see cref="InputManagerProvider"/>) unless the Input System package installs its own.</summary>
    internal partial interface IEventProviderImpl
    {
        uint playerCount { get; }
        void Initialize();
        void Shutdown();
        void Update();
        void OnFocusChanged(bool focus);
        bool RequestCurrentState(Event.Type type);
    }

    /// <summary>
    /// The InputForUI event stream UI Toolkit reads: consumers subscribe by priority, the
    /// provider produces the frame's events in the player loop's InputForUI update
    /// (<see cref="NotifyUpdate"/>), and each event goes to consumers in priority order until one
    /// takes it.
    /// </summary>
    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal static partial class EventProvider
    {
        struct Registration
        {
            public EventConsumer handler;
            public int priority;
            public int? playerId;
            public HashSet<Event.Type> _types;
        }

        static IEventProviderImpl s_impl = new InputManagerProvider();
        static EventSanitizer s_sanitizer;
        static IEventProviderImpl s_implMockBackup = null;
        static bool s_focusStateBeforeMock;
        static bool s_focusChangedRegistered;
        static bool m_IsEnabled = true;
        static bool m_IsInitialized = false;
        static List<Registration> _registrations = new List<Registration>();

        public static uint playerCount => s_impl?.playerCount ?? 0;

        internal static string _providerClassName => s_impl?.GetType().Name;

        internal static RationalTime doubleClickTime =>
            new RationalTime(UnityEngine.Event.GetDoubleClickTime(), new RationalTime.TicksPerSecond(1000u));

        public static void Subscribe(EventConsumer handler, int priority = 0, int? playerId = null, params Event.Type[] type)
        {
            Bootstrap();
            _registrations.Add(new Registration
            {
                handler = handler,
                priority = priority,
                playerId = playerId,
                _types = new HashSet<Event.Type>(type),
            });
            _registrations.Sort((a, b) => a.priority.CompareTo(b.priority));
        }

        public static void Unsubscribe(EventConsumer handler) => _registrations.RemoveAll(x => x.handler == handler);

        public static void SetEnabled(bool enable)
        {
            m_IsEnabled = enable;
            if (enable) Initialize();
            else Shutdown();
        }

        internal static void Dispatch(in Event ev)
        {
            if (_registrations.Count == 0) return;
            s_sanitizer.Inspect(in ev);
            foreach (var registration in _registrations)
            {
                if ((registration._types.Count <= 0 || registration._types.Contains(ev.type)) && registration.handler(in ev))
                    break;
            }
        }

        public static void RequestCurrentState(params Event.Type[] types)
        {
            var requested = types != null && types.Length > 0 ? types : Event.TypesWithState;
            foreach (var type in requested)
            {
                if (s_impl?.RequestCurrentState(type) != true)
                    Debug.LogWarning($"Can't provide state for type {type}");
            }
        }

        static void Bootstrap()
        {
            if (m_IsEnabled) Initialize();
        }

        static void Initialize()
        {
            if (m_IsInitialized) return;
            s_sanitizer.Reset();
            s_impl?.Initialize();
            if (!s_focusChangedRegistered)
            {
                Application.focusChanged += OnFocusChanged;
                s_focusChangedRegistered = true;
            }
            m_IsInitialized = true;
        }

        static void Shutdown()
        {
            if (!m_IsInitialized) return;
            m_IsInitialized = false;
            if (s_focusChangedRegistered)
            {
                s_focusChangedRegistered = false;
                Application.focusChanged -= OnFocusChanged;
            }
            s_impl?.Shutdown();
        }

        static void OnFocusChanged(bool focus) => s_impl?.OnFocusChanged(focus);

        /// <summary>The player loop's InputForUI update (PreUpdate.InputForUIUpdate).</summary>
        internal static void NotifyUpdate()
        {
            if (!Application.isPlaying || _registrations.Count == 0 || !m_IsInitialized) return;
            s_sanitizer.BeforeProviderUpdate();
            s_impl?.Update();
            s_sanitizer.AfterProviderUpdate();
        }

        internal static void SetInputSystemProvider(IEventProviderImpl impl)
        {
            bool wasInitialized = m_IsInitialized;
            Shutdown();
            s_impl = impl;
            if (wasInitialized) Initialize();
        }

        internal static void SetMockProvider(IEventProviderImpl impl)
        {
            if (s_implMockBackup == null) s_implMockBackup = s_impl;
            s_focusStateBeforeMock = Application.isFocused;
            Shutdown();
            s_impl = impl;
            Initialize();
        }

        internal static void ClearMockProvider()
        {
            Shutdown();
            s_impl = s_implMockBackup;
            s_implMockBackup = null;
            Initialize();
            if (s_focusStateBeforeMock != Application.isFocused)
                s_impl?.OnFocusChanged(Application.isFocused);
        }
    }

    /// <summary>Checks of the event stream. Unity ships them all disabled (the list it builds is
    /// empty); they are kept for parity.</summary>
    internal partial struct EventSanitizer
    {
        interface IEventSanitizer
        {
            void Reset();
            void BeforeProviderUpdate();
            void AfterProviderUpdate();
            void Inspect(in Event ev);
        }

        /// <summary>A release's click count must match its press's.</summary>
        struct ClickCountEventSanitizer : IEventSanitizer
        {
            List<PointerEvent> _activeButtons;
            int lastPushedIndex;

            public void Reset()
            {
                _activeButtons = new List<PointerEvent>();
                lastPushedIndex = 0;
            }

            public void BeforeProviderUpdate()
            {
            }

            public void AfterProviderUpdate()
            {
            }

            public void Inspect(in Event ev)
            {
                if (ev.type != Event.Type.PointerEvent) return;
                var pointerEvent = ev.asPointerEvent;
                switch (pointerEvent.type)
                {
                    case PointerEvent.Type.ButtonPressed:
                        lastPushedIndex = _activeButtons.Count;
                        _activeButtons.Add(pointerEvent);
                        break;
                    case PointerEvent.Type.ButtonReleased:
                        for (int i = 0; i < _activeButtons.Count; i++)
                        {
                            var pressed = _activeButtons[i];
                            if (pressed.eventSource != pointerEvent.eventSource || pressed.pointerIndex != pointerEvent.pointerIndex) continue;
                            if (i == lastPushedIndex)
                            {
                                if (pressed.clickCount != pointerEvent.clickCount)
                                    Debug.LogWarning($"ButtonReleased click count doesn't match ButtonPressed click count, where '{pressed}' and '{pointerEvent}'");
                            }
                            else if (pointerEvent.clickCount != 1)
                            {
                                Debug.LogWarning($"ButtonReleased for not the last pressed button should have click count == 1, but got '{pointerEvent}'");
                            }
                            _activeButtons.RemoveAt(i);
                            return;
                        }
                        Debug.LogWarning($"Can't find corresponding ButtonPressed for '{ev}'");
                        break;
                }
            }
        }

        /// <summary>The pointer event rules the EventSystem relies on.</summary>
        class DefaultEventSystemSanitizer : IEventSanitizer
        {
            int m_MouseEventCount;
            int m_PenOrTouchEventCount;

            public void Reset()
            {
            }

            public void BeforeProviderUpdate()
            {
                m_MouseEventCount = 0;
                m_PenOrTouchEventCount = 0;
            }

            public void AfterProviderUpdate()
            {
                if (m_MouseEventCount > 0 && m_PenOrTouchEventCount > 0)
                    Debug.LogError("PointerEvents of source Mouse and [Pen or Touch] received in the same update. This is likely an error, and Mouse events should be discarded.");
            }

            public void Inspect(in Event ev)
            {
                if (ev.type != Event.Type.PointerEvent) return;
                var pointerEvent = ev.asPointerEvent;
                if (pointerEvent.type == PointerEvent.Type.ButtonPressed && pointerEvent.button == PointerEvent.Button.None)
                    Debug.LogError("PointerEvent of type ButtonPressed must have button property set to a value other than None.");
                if (pointerEvent.type == PointerEvent.Type.ButtonReleased && pointerEvent.button == PointerEvent.Button.None)
                    Debug.LogError("PointerEvent of type ButtonReleased must have button property set to a value other than None.");
                if (pointerEvent.eventSource == EventSource.Mouse)
                {
                    m_MouseEventCount++;
                    if (!pointerEvent.isPrimaryPointer)
                        Debug.LogError("PointerEvent of source Mouse is expected to have isPrimaryPointer set to true.");
                    if (pointerEvent.pointerIndex != 0)
                        Debug.LogError("PointerEvent of source Mouse is expected to have pointerIndex set to 0.");
                }
                else if (pointerEvent.eventSource == EventSource.Touch)
                {
                    m_PenOrTouchEventCount++;
                    if (pointerEvent.button != PointerEvent.Button.None && pointerEvent.button != PointerEvent.Button.Primary)
                        Debug.LogError("PointerEvent of source Touch is expected to have button set to None or FingerInTouch.");
                }
                else if (pointerEvent.eventSource == EventSource.Pen)
                {
                    m_PenOrTouchEventCount++;
                    if (pointerEvent.button != PointerEvent.Button.None && pointerEvent.button != PointerEvent.Button.Primary
                        && pointerEvent.button != PointerEvent.Button.PenBarrelButton && pointerEvent.button != PointerEvent.Button.PenEraserInTouch)
                        Debug.LogError("PointerEvent of source Pen is expected to have button set to None, PenTipInTouch, PenBarrelButton, or PenEraserInTouch.");
                }
            }
        }

        IEventSanitizer[] _sanitizers;

        public void Reset()
        {
            _sanitizers = new IEventSanitizer[0];
            foreach (var sanitizer in _sanitizers) sanitizer.Reset();
        }

        public void BeforeProviderUpdate()
        {
            if (_sanitizers == null) Reset();
            foreach (var sanitizer in _sanitizers) sanitizer.BeforeProviderUpdate();
        }

        public void AfterProviderUpdate()
        {
            if (_sanitizers == null) Reset();
            foreach (var sanitizer in _sanitizers) sanitizer.AfterProviderUpdate();
        }

        public void Inspect(in Event ev)
        {
            if (_sanitizers == null) Reset();
            foreach (var sanitizer in _sanitizers) sanitizer.Inspect(in ev);
        }
    }
}
