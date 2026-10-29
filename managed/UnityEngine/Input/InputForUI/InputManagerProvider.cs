using System;
using System.Collections;
using System.Collections.Generic;
using Unity.IntegerTime;

namespace UnityEngine.InputForUI
{
    /// <summary>
    /// The default InputForUI provider: events from the legacy Input Manager. Touch first, then
    /// pen, then the mouse (each muted while a higher one is active), the wheel, IME composition,
    /// and navigation from the Input Manager's axes and buttons (Horizontal/Vertical, Submit,
    /// Cancel, Next/Previous); keys and text come from the IMGUI events
    /// (<see cref="InputEventPartialProvider"/>).
    /// </summary>
    internal partial class InputManagerProvider : IEventProviderImpl
    {
        /// <summary>The press and release a button made since the last update, in order.</summary>
        struct ButtonEventsIterator : IEnumerator
        {
            uint _mask;
            int _bit;

            const uint kWasPressed = 1u;
            const uint kWasReleased = 2u;
            const int kMaxBits = 4;

            /// <summary>Even bits are presses, odd bits releases.</summary>
            public bool Current => _bit % 2 == 0;

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                do
                {
                    _bit++;
                    if ((_mask & (uint)(1 << _bit)) != 0) return true;
                }
                while (_bit < kMaxBits);
                return false;
            }

            public void Reset() => _bit = -1;

            public static ButtonEventsIterator FromState(bool previous, bool down, bool up, bool current)
            {
                uint mask = !previous && current ? kWasPressed : previous && !current ? kWasReleased : 0u;
                return new ButtonEventsIterator { _mask = mask, _bit = -1 };
            }
        }

        public partial struct Configuration
        {
            public string HorizontalAxis;
            public string VerticalAxis;
            public string SubmitButton;
            public string CancelButton;
            public string NavigateNextButton;
            public string NavigatePreviousButton;
            public float InputActionsPerSecond;
            public float RepeatDelay;

            public static Configuration GetDefaultConfiguration() => new Configuration
            {
                HorizontalAxis = "Horizontal",
                VerticalAxis = "Vertical",
                SubmitButton = "Submit",
                CancelButton = "Cancel",
                NavigateNextButton = "Next",
                NavigatePreviousButton = "Previous",
                InputActionsPerSecond = 10f,
                RepeatDelay = 0.5f,
            };
        }

        internal partial interface IInput
        {
            string compositionString { get; }
            bool touchSupported { get; }
            int touchCount { get; }
            bool mousePresent { get; }
            Vector3 mousePosition { get; }
            Vector2 mouseScrollDelta { get; }
            bool GetKey(KeyCode keyCode);
            bool GetKeyDown(KeyCode keyCode);
            bool GetButtonDown(string button);
            float GetAxisRaw(string axis);
            PenData GetPenEvent(int index);
            PenData GetLastPenContactEvent();
            Touch GetTouch(int index);
            bool GetMouseButton(int button);
            bool GetMouseButtonDown(int button);
            bool GetMouseButtonUp(int button);
        }

        class Input : IInput
        {
            public string compositionString => UnityEngine.Input.CompositionString;
            public bool touchSupported => UnityEngine.Input.touchSupported;
            public int touchCount => UnityEngine.Input.touchCount;
            public bool mousePresent => UnityEngine.Input.mousePresent;
            public Vector3 mousePosition => UnityEngine.Input.mousePosition;
            public Vector2 mouseScrollDelta => UnityEngine.Input.mouseScrollDelta;
            public bool GetKey(KeyCode key) => UnityEngine.Input.GetKey(key);
            public bool GetKeyDown(KeyCode key) => UnityEngine.Input.GetKeyDown(key);
            public bool GetButtonDown(string button) => UnityEngine.Input.GetButtonDown(button);
            public float GetAxisRaw(string axis) => UnityEngine.Input.GetAxisRaw(axis);
            public PenData GetPenEvent(int index) => default; // no pen is read (see UnityEngine.Input.GetPenEvent)
            public PenData GetLastPenContactEvent() => UnityEngine.Input.LastPenContact;
            public Touch GetTouch(int index) => UnityEngine.Input.GetTouch(index);
            public bool GetMouseButton(int button) => UnityEngine.Input.GetMouseButton(button);
            public bool GetMouseButtonDown(int button) => UnityEngine.Input.GetMouseButtonDown(button);
            public bool GetMouseButtonUp(int button) => UnityEngine.Input.GetMouseButtonUp(button);
        }

        internal partial interface ITime
        {
            RationalTime timeAsRational { get; }
        }

        class Time : ITime
        {
            public RationalTime timeAsRational => UnityEngine.Time.timeAsRational;
        }

        InputEventPartialProvider _inputEventPartialProvider;

        const int kDefaultPlayerId = 0;

        string _compositionString = string.Empty;
        Configuration _configuration = Configuration.GetDefaultConfiguration();
        IInput _input = new Input();
        ITime _time = new Time();
        NavigationEventRepeatHelper _navigationEventRepeatHelper = new NavigationEventRepeatHelper();

        const int kMaxMouseButtons = 5;
        PointerState _mouseState;

        bool _isPenPresent;
        bool _seenAtLeastOnePenPosition;
        Vector2 _lastSeenPenPositionForDetection;
        PointerState _penState;
        PenData _lastPenData;

        Dictionary<int, int> _touchFingerIdToFingerIndex = new Dictionary<int, int>();
        int _touchNextFingerIndex;
        PointerState _touchState;

        const float kSmallestReportedMovementSqrDist = 0.01f;
        const float kScrollUGUIScaleFactor = 3f;

        EventModifiers _eventModifiers => _inputEventPartialProvider._eventModifiers;

        [Unbound.EditorFaithful("as Unity's InputForUI source: the Input Manager has one player")]
        public uint playerCount => 1u;

        public InputManagerProvider()
        {
        }

        internal InputManagerProvider(IInput inputOverride, ITime timeOverride)
        {
            _input = inputOverride;
            _time = timeOverride;
        }

        public void Initialize()
        {
            if (_inputEventPartialProvider == null) _inputEventPartialProvider = new InputEventPartialProvider();
            _inputEventPartialProvider.Initialize();
            _inputEventPartialProvider._sendNavigationEventOnTabKey = true;
            _mouseState.Reset();
            _isPenPresent = false;
            _seenAtLeastOnePenPosition = false;
            _lastSeenPenPositionForDetection = default;
            _penState.Reset();
            _lastPenData = default;
            _touchFingerIdToFingerIndex.Clear();
            _touchNextFingerIndex = 0;
            _touchState.Reset();
        }

        [Unbound.EditorFaithful("as Unity's InputForUI source: nothing to release")]
        public void Shutdown()
        {
        }

        public void Update()
        {
            _inputEventPartialProvider.Update();
            var currentTime = (DiscreteTime)_time.timeAsRational;
            DetectPen();
            bool touched = false;
            if (_input.touchSupported) touched = CheckTouchEvents(currentTime);
            bool penned = false;
            if (!touched && _isPenPresent)
                penned = CheckPenEvent(currentTime, _input.GetLastPenContactEvent());
            else
                _penState.Reset();
            if (!penned && !touched && _input.mousePresent)
            {
                CheckMouseEvents(currentTime);
            }
            else
            {
                CheckMouseEvents(currentTime, muted: true);
                _mouseState.LastPositionValid = false;
            }
            if (_input.mousePresent) CheckMouseScroll(currentTime);
            CheckIfIMEChanged(currentTime);
            DirectionNavigation(currentTime);
            SubmitCancelNavigation(currentTime);
            NextPreviousNavigation(currentTime);
        }

        bool CheckTouchEvents(DiscreteTime currentTime)
        {
            bool allReleased = true;
            bool sent = false;
            for (int i = 0; i < _input.touchCount; i++)
            {
                var touch = _input.GetTouch(i);
                if (touch.type == TouchType.Indirect || touch.phase == TouchPhase.Stationary) continue;
                if (!_touchFingerIdToFingerIndex.TryGetValue(touch.fingerId, out var fingerIndex))
                {
                    fingerIndex = _touchNextFingerIndex++;
                    _touchFingerIdToFingerIndex.Add(touch.fingerId, fingerIndex);
                }
                var position = MultiDisplayBottomLeftToPanelPosition(touch.position, out var targetDisplay);
                var deltaPosition = ScreenBottomLeftToPanelDelta(touch.deltaPosition);
                var type = PointerEvent.Type.PointerMoved;
                var button = PointerEvent.Button.None;
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        type = PointerEvent.Type.ButtonPressed;
                        button = PointerEvent.Button.Primary;
                        allReleased = false;
                        _touchState.OnButtonDown(currentTime, button);
                        break;
                    case TouchPhase.Ended:
                        type = PointerEvent.Type.ButtonReleased;
                        button = PointerEvent.Button.Primary;
                        _touchState.OnButtonUp(currentTime, button);
                        break;
                    case TouchPhase.Canceled:
                        type = PointerEvent.Type.TouchCanceled;
                        button = PointerEvent.Button.Primary;
                        _touchState.OnButtonUp(currentTime, button);
                        break;
                    case TouchPhase.Moved:
                        allReleased = false;
                        break;
                }
                EventProvider.Dispatch(Event.From(new PointerEvent
                {
                    type = type,
                    pointerIndex = fingerIndex,
                    position = position,
                    deltaPosition = deltaPosition,
                    scroll = Vector2.zero,
                    displayIndex = targetDisplay,
                    tilt = AzimuthAndAlitutudeToTilt(touch.altitudeAngle, touch.azimuthAngle),
                    twist = 0f,
                    pressure = Mathf.Abs(touch.maximumPossiblePressure) > Mathf.Epsilon ? touch.pressure / touch.maximumPossiblePressure : 1f,
                    isInverted = false,
                    button = button,
                    buttonsState = _touchState.ButtonsState,
                    clickCount = _touchState.ClickCount,
                    timestamp = currentTime,
                    eventSource = EventSource.Touch,
                    playerId = kDefaultPlayerId,
                    eventModifiers = _eventModifiers,
                }));
                sent = true;
            }
            if (allReleased)
            {
                _touchNextFingerIndex = 0;
                _touchFingerIdToFingerIndex.Clear();
            }
            return sent;
        }

        /// <summary>A pen counts as present once its last contact position moves.</summary>
        void DetectPen()
        {
            if (_isPenPresent) return;
            var position = _input.GetLastPenContactEvent().position;
            if (_seenAtLeastOnePenPosition)
            {
                _isPenPresent = (position - _lastSeenPenPositionForDetection).sqrMagnitude >= kSmallestReportedMovementSqrDist;
            }
            else
            {
                _lastSeenPenPositionForDetection = position;
                _seenAtLeastOnePenPosition = true;
            }
        }

        static PointerEvent.Button PenStatusToButton(PenStatus status)
        {
            if ((status & PenStatus.Eraser) != 0) return PointerEvent.Button.PenEraserInTouch;
            if ((status & PenStatus.Barrel) != 0) return PointerEvent.Button.PenBarrelButton;
            return PointerEvent.Button.Primary;
        }

        bool CheckPenEvent(DiscreteTime currentTime, in PenData currentPenData)
        {
            var position = currentPenData.position;
            int displayIndex = 0;
            var deltaPosition = _penState.LastPositionValid ? position - _penState.LastPosition : Vector2.zero;
            var button = PointerEvent.Button.None;
            PointerEvent.Type type;
            if (currentPenData.contactType != _lastPenData.contactType)
            {
                switch (currentPenData.contactType)
                {
                    case PenEventType.PenDown:
                        type = PointerEvent.Type.ButtonPressed;
                        button = PenStatusToButton(currentPenData.penStatus);
                        _penState.OnButtonDown(currentTime, button);
                        break;
                    case PenEventType.PenUp:
                        type = PointerEvent.Type.ButtonReleased;
                        button = PenStatusToButton(_lastPenData.penStatus);
                        _penState.OnButtonUp(currentTime, button);
                        break;
                    default:
                        type = PointerEvent.Type.PointerMoved;
                        break;
                }
            }
            else
            {
                type = PointerEvent.Type.PointerMoved;
            }
            _lastPenData = currentPenData;
            bool sent = false;
            if (type != PointerEvent.Type.PointerMoved || !_penState.LastPositionValid || deltaPosition.sqrMagnitude >= kSmallestReportedMovementSqrDist)
            {
                EventProvider.Dispatch(Event.From(new PointerEvent
                {
                    type = type,
                    pointerIndex = 0,
                    position = position,
                    deltaPosition = deltaPosition,
                    scroll = Vector2.zero,
                    displayIndex = displayIndex,
                    tilt = currentPenData.tilt,
                    twist = currentPenData.twist,
                    pressure = currentPenData.pressure,
                    isInverted = (currentPenData.penStatus & PenStatus.Inverted) != 0,
                    button = button,
                    buttonsState = _penState.ButtonsState,
                    clickCount = _penState.ClickCount,
                    timestamp = currentTime,
                    eventSource = EventSource.Pen,
                    playerId = kDefaultPlayerId,
                    eventModifiers = _eventModifiers,
                }));
                sent = true;
            }
            _penState.OnMove(currentTime, position, displayIndex);
            return sent;
        }

        void CheckMouseEvents(DiscreteTime currentTime, bool muted = false)
        {
            var position = MultiDisplayBottomLeftToPanelPosition(_input.mousePosition, out var targetDisplay);
            if (_mouseState.LastPositionValid)
            {
                var deltaPosition = position - _mouseState.LastPosition;
                if (deltaPosition.sqrMagnitude >= kSmallestReportedMovementSqrDist)
                {
                    if (!muted)
                    {
                        EventProvider.Dispatch(Event.From(new PointerEvent
                        {
                            type = PointerEvent.Type.PointerMoved,
                            pointerIndex = 0,
                            position = position,
                            deltaPosition = deltaPosition,
                            scroll = Vector2.zero,
                            displayIndex = targetDisplay,
                            tilt = Vector2.zero,
                            twist = 0f,
                            pressure = 0f,
                            isInverted = false,
                            button = PointerEvent.Button.None,
                            buttonsState = _mouseState.ButtonsState,
                            clickCount = 0,
                            timestamp = currentTime,
                            eventSource = EventSource.Mouse,
                            playerId = kDefaultPlayerId,
                            eventModifiers = _eventModifiers,
                        }));
                    }
                    _mouseState.OnMove(currentTime, position, targetDisplay);
                }
            }
            else
            {
                _mouseState.OnMove(currentTime, position, targetDisplay);
            }
            for (int i = 0; i < kMaxMouseButtons; i++)
            {
                var button = PointerEvent.ButtonFromButtonIndex(i);
                bool previous = _mouseState.ButtonsState.Get(button);
                var events = ButtonEventsIterator.FromState(previous, _input.GetMouseButtonDown(i), _input.GetMouseButtonUp(i), _input.GetMouseButton(i));
                bool previousState = previous;
                while (events.MoveNext())
                {
                    _mouseState.OnButtonChange(currentTime, button, previousState, events.Current);
                    previousState = events.Current;
                    if (muted) continue;
                    EventProvider.Dispatch(Event.From(new PointerEvent
                    {
                        type = events.Current ? PointerEvent.Type.ButtonPressed : PointerEvent.Type.ButtonReleased,
                        pointerIndex = 0,
                        position = _mouseState.LastPosition,
                        deltaPosition = Vector2.zero,
                        scroll = Vector2.zero,
                        displayIndex = _mouseState.LastDisplayIndex,
                        tilt = Vector2.zero,
                        twist = 0f,
                        pressure = 0f,
                        isInverted = false,
                        button = button,
                        buttonsState = _mouseState.ButtonsState,
                        clickCount = _mouseState.ClickCount,
                        timestamp = currentTime,
                        eventSource = EventSource.Mouse,
                        playerId = kDefaultPlayerId,
                        eventModifiers = _eventModifiers,
                    }));
                }
            }
        }

        /// <summary>The wheel, scaled as uGUI scales it (3 per notch) and flipped to panel space.</summary>
        void CheckMouseScroll(DiscreteTime currentTime)
        {
            var scroll = _input.mouseScrollDelta;
            if (scroll.sqrMagnitude < kSmallestReportedMovementSqrDist) return;
            int targetDisplay = 0;
            Vector2 position;
            if (_mouseState.LastPositionValid)
            {
                position = _mouseState.LastPosition;
                targetDisplay = _mouseState.LastDisplayIndex;
            }
            else
            {
                position = MultiDisplayBottomLeftToPanelPosition(_input.mousePosition, out targetDisplay);
            }
            scroll.x *= kScrollUGUIScaleFactor;
            scroll.y *= -kScrollUGUIScaleFactor;
            EventProvider.Dispatch(Event.From(new PointerEvent
            {
                type = PointerEvent.Type.Scroll,
                pointerIndex = 0,
                position = position,
                deltaPosition = Vector2.zero,
                scroll = scroll,
                displayIndex = targetDisplay,
                tilt = Vector2.zero,
                twist = 0f,
                pressure = 0f,
                isInverted = false,
                button = PointerEvent.Button.None,
                buttonsState = _mouseState.ButtonsState,
                clickCount = 0,
                timestamp = currentTime,
                eventSource = EventSource.Mouse,
                playerId = kDefaultPlayerId,
                eventModifiers = _eventModifiers,
            }));
        }

        PointerEvent ToPointerStateEvent(DiscreteTime currentTime, in PointerState state, EventSource eventSource)
        {
            bool pen = eventSource == EventSource.Pen;
            return new PointerEvent
            {
                type = PointerEvent.Type.State,
                pointerIndex = 0,
                position = state.LastPosition,
                deltaPosition = Vector2.zero,
                scroll = Vector2.zero,
                displayIndex = state.LastDisplayIndex,
                tilt = pen ? _lastPenData.tilt : Vector2.zero,
                twist = pen ? _lastPenData.twist : 0f,
                pressure = pen ? _lastPenData.pressure : 0f,
                isInverted = pen && (_lastPenData.penStatus & PenStatus.Inverted) != 0,
                button = PointerEvent.Button.None,
                buttonsState = state.ButtonsState,
                clickCount = 0,
                timestamp = currentTime,
                eventSource = eventSource,
                playerId = kDefaultPlayerId,
                eventModifiers = _eventModifiers,
            };
        }

        void NextPreviousNavigation(DiscreteTime currentTime)
        {
            int direction = (InputManagerGetButtonDownOrDefault(_configuration.NavigateNextButton) ? 1 : 0)
                + (InputManagerGetButtonDownOrDefault(_configuration.NavigatePreviousButton) ? -1 : 0);
            if (direction == 0) return;
            if (_eventModifiers.isShiftPressed) direction = -direction;
            EventProvider.Dispatch(Event.From(new NavigationEvent
            {
                type = NavigationEvent.Type.Move,
                direction = direction >= 0 ? NavigationEvent.Direction.Next : NavigationEvent.Direction.Previous,
                timestamp = currentTime,
                eventSource = GetEventSourceFromPressedKey(),
                playerId = kDefaultPlayerId,
                eventModifiers = _eventModifiers,
            }));
        }

        void SubmitCancelNavigation(DiscreteTime currentTime)
        {
            if (InputManagerGetButtonDownOrDefault(_configuration.SubmitButton))
            {
                EventProvider.Dispatch(Event.From(new NavigationEvent
                {
                    type = NavigationEvent.Type.Submit,
                    direction = NavigationEvent.Direction.None,
                    timestamp = currentTime,
                    eventSource = GetEventSourceFromPressedKey(),
                    playerId = kDefaultPlayerId,
                    eventModifiers = _eventModifiers,
                }));
            }
            if (InputManagerGetButtonDownOrDefault(_configuration.CancelButton))
            {
                EventProvider.Dispatch(Event.From(new NavigationEvent
                {
                    type = NavigationEvent.Type.Cancel,
                    direction = NavigationEvent.Direction.None,
                    timestamp = currentTime,
                    eventSource = GetEventSourceFromPressedKey(),
                    playerId = kDefaultPlayerId,
                    eventModifiers = _eventModifiers,
                }));
            }
        }

        void DirectionNavigation(DiscreteTime currentTime)
        {
            var (move, axisButtonsPressed) = ReadCurrentNavigationMoveVector();
            var direction = NavigationEvent.DetermineMoveDirection(move);
            if (direction == NavigationEvent.Direction.None)
            {
                _navigationEventRepeatHelper.Reset();
                return;
            }
            if (!_navigationEventRepeatHelper.ShouldSendMoveEvent(currentTime, direction, axisButtonsPressed)) return;
            var eventSource = GetEventSourceFromPressedKey();
            if (eventSource == EventSource.Unspecified && !axisButtonsPressed) eventSource = EventSource.Gamepad;
            EventProvider.Dispatch(Event.From(new NavigationEvent
            {
                type = NavigationEvent.Type.Move,
                direction = direction,
                timestamp = currentTime,
                eventSource = eventSource,
                playerId = kDefaultPlayerId,
                eventModifiers = _eventModifiers,
            }));
        }

        void CheckIfIMEChanged(DiscreteTime currentTime)
        {
            string compositionString = _input.compositionString;
            if (_compositionString == compositionString) return;
            _compositionString = compositionString;
            EventProvider.Dispatch(Event.From(ToIMECompositionEvent(currentTime, _compositionString)));
        }

        public void OnFocusChanged(bool focus) => _inputEventPartialProvider.OnFocusChanged(focus);

        public bool RequestCurrentState(Event.Type type)
        {
            if (_inputEventPartialProvider.RequestCurrentState(type)) return true;
            var currentTime = (DiscreteTime)_time.timeAsRational;
            switch (type)
            {
                case Event.Type.PointerEvent:
                    if (_touchState.LastPositionValid)
                        EventProvider.Dispatch(Event.From(ToPointerStateEvent(currentTime, in _touchState, EventSource.Touch)));
                    if (_penState.LastPositionValid)
                        EventProvider.Dispatch(Event.From(ToPointerStateEvent(currentTime, in _penState, EventSource.Pen)));
                    if (_mouseState.LastPositionValid)
                        EventProvider.Dispatch(Event.From(ToPointerStateEvent(currentTime, in _mouseState, EventSource.Mouse)));
                    return _touchState.LastPositionValid || _penState.LastPositionValid || _mouseState.LastPositionValid;
                case Event.Type.IMECompositionEvent:
                    EventProvider.Dispatch(Event.From(ToIMECompositionEvent(currentTime, _compositionString)));
                    return true;
                default:
                    return false;
            }
        }

        EventSource GetEventSourceFromPressedKey()
        {
            if (InputManagerKeyboardWasPressed()) return EventSource.Keyboard;
            if (InputManagerJoystickWasPressed()) return EventSource.Gamepad;
            return EventSource.Unspecified;
        }

        bool InputManagerJoystickWasPressed()
        {
            for (var keyCode = KeyCode.Joystick1Button0; keyCode <= KeyCode.Joystick8Button19; keyCode++)
                if (_input.GetKey(keyCode)) return true;
            return false;
        }

        bool InputManagerKeyboardWasPressed()
        {
            for (var keyCode = KeyCode.None; keyCode <= KeyCode.Menu; keyCode++)
                if (_input.GetKey(keyCode)) return true;
            return false;
        }

        float InputManagerGetAxisRawOrDefault(string axisName)
        {
            try
            {
                return !string.IsNullOrEmpty(axisName) ? _input.GetAxisRaw(axisName) : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        bool InputManagerGetButtonDownOrDefault(string axisName)
        {
            try
            {
                return !string.IsNullOrEmpty(axisName) && _input.GetButtonDown(axisName);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The navigation axes; an axis whose button went down this frame reads as a full
        /// step (and moves at once, whatever the repeat timing).</summary>
        (Vector2, bool) ReadCurrentNavigationMoveVector()
        {
            var move = new Vector2(InputManagerGetAxisRawOrDefault(_configuration.HorizontalAxis), InputManagerGetAxisRawOrDefault(_configuration.VerticalAxis));
            bool pressed = false;
            if (InputManagerGetButtonDownOrDefault(_configuration.HorizontalAxis))
            {
                if (move.x < 0f) move.x = -1f;
                else if (move.x > 0f) move.x = 1f;
                pressed = true;
            }
            if (InputManagerGetButtonDownOrDefault(_configuration.VerticalAxis))
            {
                if (move.y < 0f) move.y = -1f;
                else if (move.y > 0f) move.y = 1f;
                pressed = true;
            }
            return (move, pressed);
        }

        IMECompositionEvent ToIMECompositionEvent(DiscreteTime currentTime, string compositionString) => new IMECompositionEvent
        {
            compositionString = compositionString,
            timestamp = currentTime,
            eventSource = EventSource.Unspecified,
            playerId = kDefaultPlayerId,
            eventModifiers = _eventModifiers,
        };

        internal static float TiltToAzimuth(Vector2 tilt)
        {
            float azimuth = 0f;
            if (tilt.x != 0f)
            {
                azimuth = MathF.PI / 2f - Mathf.Atan2(-Mathf.Cos(tilt.x) * Mathf.Sin(tilt.y), Mathf.Cos(tilt.y) * Mathf.Sin(tilt.x));
                if (azimuth < 0f) azimuth += MathF.PI * 2f;
                azimuth = azimuth >= MathF.PI / 2f ? azimuth - MathF.PI / 2f : azimuth + 4.712389f;
            }
            return azimuth;
        }

        internal static Vector2 AzimuthAndAlitutudeToTilt(float altitude, float azimuth)
        {
            var tilt = new Vector2(0f, 0f);
            tilt.x = Mathf.Atan(Mathf.Cos(azimuth) * Mathf.Cos(altitude) / Mathf.Sin(azimuth));
            tilt.y = Mathf.Atan(Mathf.Cos(azimuth) * Mathf.Sin(altitude) / Mathf.Sin(azimuth));
            return tilt;
        }

        internal static float TiltToAltitude(Vector2 tilt) => MathF.PI / 2f - Mathf.Acos(Mathf.Cos(tilt.x) * Mathf.Cos(tilt.y));

        static Vector2 MultiDisplayBottomLeftToPanelPosition(Vector2 position, out int targetDisplay)
        {
            var local = MultiDisplayToLocalScreenPosition(position, out var display);
            targetDisplay = display.GetValueOrDefault();
            return ScreenBottomLeftToPanelPosition(local, targetDisplay);
        }

        static Vector2 MultiDisplayToLocalScreenPosition(Vector2 position, out int? targetDisplay)
        {
            var relative = Display.RelativeMouseAt(position);
            if (relative != Vector3.zero)
            {
                targetDisplay = (int)relative.z;
                return relative;
            }
            targetDisplay = Display.activeEditorGameViewTarget;
            return position;
        }

        static Vector2 ScreenBottomLeftToPanelPosition(Vector2 position, int targetDisplay)
        {
            int height = Screen.height;
            if (targetDisplay > 0 && targetDisplay < Display.displays.Length) height = Display.displays[targetDisplay].systemHeight;
            position.y = height - position.y;
            return position;
        }

        static Vector2 ScreenBottomLeftToPanelDelta(Vector2 delta)
        {
            delta.y = -delta.y;
            return delta;
        }
    }
}
