namespace UnityEngine
{
    public enum PenEventType
    {
        NoContact = 0,
        PenDown = 1,
        PenUp = 2,
    }

    [System.Flags]
    public enum PenStatus
    {
        None = 0,
        Contact = 1,
        Barrel = 2,
        Inverted = 4,
        Eraser = 8,
    }

    /// <summary>A pen sample of the legacy Input Manager.</summary>
    public partial struct PenData
    {
        public Vector2 position;
        public Vector2 tilt;
        public PenStatus penStatus;
        public float twist;
        public float pressure;
        public PenEventType contactType;
        public Vector2 deltaPos;
    }

    /// <summary>Pen input of the legacy Input Manager: Unbound does not read pens yet, so no pen
    /// events are queued and the last contact is empty (as the Editor without a pen); a game using
    /// the API is warned.</summary>
    public partial class Input
    {
        /// <summary>The last pen contact, for the engine's own UI input (which must not warn).</summary>
        internal static PenData LastPenContact => default;

        public static int penEventCount { get { Unbound.Runtime.Stubs.Ignored("Input.penEventCount", "is not captured yet: no pen events are read"); return 0; } }

        public static PenData GetPenEvent(int index) { Unbound.Runtime.Stubs.Ignored("Input.GetPenEvent", "is not captured yet: no pen events are read"); return default; }

        public static PenData GetLastPenContactEvent() { Unbound.Runtime.Stubs.Ignored("Input.GetLastPenContactEvent", "is not captured yet: no pen events are read"); return LastPenContact; }

        public static void ResetPenEvents() => Unbound.Runtime.Stubs.Ignored("Input.ResetPenEvents", "is not captured yet: no pen events are read");

        public static void ClearLastPenContactEvent() => Unbound.Runtime.Stubs.Ignored("Input.ClearLastPenContactEvent", "is not captured yet: no pen events are read");
    }
}
