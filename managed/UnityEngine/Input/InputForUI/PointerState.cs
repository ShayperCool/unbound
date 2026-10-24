using Unity.IntegerTime;

namespace UnityEngine.InputForUI
{
    /// <summary>A pointer's buttons, click count and last position, as providers track them.</summary>
    internal partial struct PointerState
    {
        PointerEvent.ButtonsState _buttonsState;

        static readonly DiscreteTime kClickDelay = new DiscreteTime(UnityEngine.Event.GetDoubleClickTime() / 1000.0);

        public PointerEvent.Button LastPressedButton { get; private set; }
        public PointerEvent.ButtonsState ButtonsState => _buttonsState;
        public DiscreteTime NextPressTime { get; private set; }
        public int ClickCount { get; private set; }
        public Vector2 LastPosition { get; private set; }
        public int LastDisplayIndex { get; private set; }
        public bool LastPositionValid { get; set; }

        /// <summary>A press of the same button within the double-click time counts one more click.</summary>
        public void OnButtonDown(DiscreteTime currentTime, PointerEvent.Button button)
        {
            if (LastPressedButton != button || currentTime >= NextPressTime) ClickCount = 0;
            LastPressedButton = button;
            _buttonsState.Set(button, pressed: true);
            ClickCount++;
            NextPressTime = currentTime + kClickDelay;
        }

        public void OnButtonUp(DiscreteTime currentTime, PointerEvent.Button button)
        {
            if (LastPressedButton != button) ClickCount = 1;
            _buttonsState.Set(button, pressed: false);
        }

        public void OnButtonChange(DiscreteTime currentTime, PointerEvent.Button button, bool previousState, bool newState)
        {
            if (newState && !previousState) OnButtonDown(currentTime, button);
            else if (!newState && previousState) OnButtonUp(currentTime, button);
        }

        public void OnMove(DiscreteTime currentTime, Vector2 position, int displayIndex)
        {
            LastPosition = position;
            LastDisplayIndex = displayIndex;
            LastPositionValid = true;
        }

        public void Reset()
        {
            LastPressedButton = PointerEvent.Button.None;
            // As Unity: this resets a copy, so the held buttons survive a reset.
            ButtonsState.Reset();
            NextPressTime = DiscreteTime.Zero;
            ClickCount = 0;
            LastPosition = Vector2.zero;
            LastDisplayIndex = 0;
            LastPositionValid = false;
        }
    }

    /// <summary>When a held navigation direction repeats its move event: after 0.5 s, then every
    /// 0.1 s.</summary>
    internal partial class NavigationEventRepeatHelper
    {
        int m_ConsecutiveMoveCount;
        NavigationEvent.Direction m_LastDirection;
        DiscreteTime m_PrevActionTime;
        readonly DiscreteTime m_InitialRepeatDelay = new DiscreteTime(0.5f);
        readonly DiscreteTime m_ConsecutiveRepeatDelay = new DiscreteTime(0.1f);

        public void Reset()
        {
            m_ConsecutiveMoveCount = 0;
            m_LastDirection = NavigationEvent.Direction.None;
            m_PrevActionTime = DiscreteTime.Zero;
        }

        public bool ShouldSendMoveEvent(DiscreteTime timestamp, NavigationEvent.Direction direction, bool axisButtonsWherePressedThisFrame)
        {
            if (axisButtonsWherePressedThisFrame || direction != m_LastDirection
                || timestamp > m_PrevActionTime + (m_ConsecutiveMoveCount == 1 ? m_InitialRepeatDelay : m_ConsecutiveRepeatDelay))
            {
                m_ConsecutiveMoveCount = direction != m_LastDirection ? 1 : m_ConsecutiveMoveCount + 1;
                m_LastDirection = direction;
                m_PrevActionTime = timestamp;
                return true;
            }
            return false;
        }
    }
}
