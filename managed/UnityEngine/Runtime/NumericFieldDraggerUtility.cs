using System;

namespace UnityEngine
{
    /// <summary>How a value field's drag turns pointer movement into a value change. Measured
    /// through sliders, Unity 6000.0.71f1: tests/projects/sliderdrag.</summary>
    internal partial class NumericFieldDraggerUtility
    {
        const float kDragSensitivity = .03f;

        /// <summary>Which axis gives a move's sign; kept between moves (and fields) while the axes'
        /// lengths are within a tenth of each other.</summary>
        static bool s_UseYSign;

        public static float Acceleration(bool shiftPressed, bool altPressed) =>
            (shiftPressed ? 4 : 1) * (altPressed ? .25f : 1);

        /// <summary>The move's length, signed by its dominant axis (up is positive).</summary>
        public static float NiceDelta(Vector2 deviceDelta, float acceleration)
        {
            deviceDelta.y = -deviceDelta.y;
            float ax = Mathf.Abs(deviceDelta.x), ay = Mathf.Abs(deviceDelta.y);
            // A zero move divides zero by zero: not greater, the axis stays.
            if (Mathf.Abs(ax - ay) / Mathf.Max(ax, ay) > .1f)
                s_UseYSign = ay >= ax;
            float sign = s_UseYSign ? Mathf.Sign(deviceDelta.y) : Mathf.Sign(deviceDelta.x);
            return sign * deviceDelta.magnitude * acceleration;
        }

        public static double CalculateFloatDragSensitivity(double value)
        {
            if (double.IsInfinity(value) || double.IsNaN(value)) return 0.0;
            return Math.Max(1, Math.Pow(Math.Abs(value), 0.5)) * kDragSensitivity;
        }

        public static double CalculateFloatDragSensitivity(double value, double minValue, double maxValue)
        {
            if (double.IsInfinity(value) || double.IsNaN(value)) return 0.0;
            double range = Math.Abs(maxValue - minValue);
            return range / 100.0f * kDragSensitivity;
        }

        public static long CalculateIntDragSensitivity(long value) => (long)CalculateIntDragSensitivity((double)value);

        public static ulong CalculateIntDragSensitivity(ulong value) => (ulong)CalculateIntDragSensitivity((double)value);

        public static double CalculateIntDragSensitivity(double value) =>
            Math.Max(1, Math.Pow(Math.Abs(value), 0.5) * kDragSensitivity);

        public static long CalculateIntDragSensitivity(long value, long minValue, long maxValue)
        {
            long range = Math.Abs(maxValue - minValue);
            return Math.Max(1, (long)(kDragSensitivity * range / 100));
        }
    }
}
