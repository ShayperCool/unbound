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

    /// <summary>Pen input of the legacy Input Manager: the window has no pen (as the Linux Editor
    /// without one), so no pen events are queued and the last contact is empty.</summary>
    public partial class Input
    {
        public static int penEventCount => 0;

        public static PenData GetPenEvent(int index) => default;

        public static PenData GetLastPenContactEvent() => default;

        public static void ResetPenEvents()
        {
        }

        public static void ClearLastPenContactEvent()
        {
        }
    }
}
