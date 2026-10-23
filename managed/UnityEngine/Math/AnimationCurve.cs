using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>How time outside an animation's or curve's range is mapped back into it.</summary>
    public enum WrapMode
    {
        Once = 1,
        Loop = 2,
        PingPong = 4,
        Default = 0,
        ClampForever = 8,
        Clamp = 1,
    }

    /// <summary>Which tangent weights of a keyframe are used.</summary>
    public enum WeightedMode
    {
        None = 0,
        In = 1,
        Out = 2,
        Both = 3,
    }

    /// <summary>A single key of an AnimationCurve.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Keyframe
    {
        private float m_Time;
        private float m_Value;
        private float m_InTangent;
        private float m_OutTangent;
        private int m_TangentMode;
        private int m_WeightedMode;
        private float m_InWeight;
        private float m_OutWeight;

        public Keyframe(float time, float value)
        {
            m_Time = time;
            m_Value = value;
            m_InTangent = 0;
            m_OutTangent = 0;
            m_WeightedMode = 0;
            m_InWeight = 0f;
            m_OutWeight = 0f;
            m_TangentMode = 0;
        }

        public Keyframe(float time, float value, float inTangent, float outTangent)
        {
            m_Time = time;
            m_Value = value;
            m_InTangent = inTangent;
            m_OutTangent = outTangent;
            m_WeightedMode = 0;
            m_InWeight = 0f;
            m_OutWeight = 0f;
            m_TangentMode = 0;
        }

        public Keyframe(float time, float value, float inTangent, float outTangent, float inWeight, float outWeight)
        {
            m_Time = time;
            m_Value = value;
            m_InTangent = inTangent;
            m_OutTangent = outTangent;
            m_WeightedMode = (int)WeightedMode.Both;
            m_InWeight = inWeight;
            m_OutWeight = outWeight;
            m_TangentMode = 0;
        }

        public float time
        {
            get => m_Time;
            set => m_Time = value;
        }

        public float value
        {
            get => m_Value;
            set => m_Value = value;
        }

        public float inTangent
        {
            get => m_InTangent;
            set => m_InTangent = value;
        }

        public float outTangent
        {
            get => m_OutTangent;
            set => m_OutTangent = value;
        }

        public float inWeight
        {
            get => m_InWeight;
            set => m_InWeight = value;
        }

        public float outWeight
        {
            get => m_OutWeight;
            set => m_OutWeight = value;
        }

        public WeightedMode weightedMode
        {
            get => (WeightedMode)m_WeightedMode;
            set => m_WeightedMode = (int)value;
        }

        [Obsolete("Use AnimationUtility.SetKeyLeftTangentMode, AnimationUtility.SetKeyRightTangentMode, AnimationUtility.GetKeyLeftTangentMode or AnimationUtility.GetKeyRightTangentMode instead.")]
        public int tangentMode
        {
            get => m_TangentMode;
            set => m_TangentMode = value;
        }

        // Tangent mode bits as serialized (m_TangentMode), for the scene loader.
        internal int tangentModeInternal
        {
            get => m_TangentMode;
            set => m_TangentMode = value;
        }
    }

    /// <summary>
    /// A curve of keyframes evaluated as cubic Hermite segments (cubic Bezier segments where
    /// tangent weights are enabled). Unity backs this class with a native object; in Unbound it is
    /// plain managed state.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial class AnimationCurve : IEquatable<AnimationCurve>
    {
        private const float kDefaultWeight = 1.0f / 3.0f;

        private readonly List<Keyframe> m_Keys = new List<Keyframe>();
        private WrapMode m_PreWrapMode = WrapMode.ClampForever;
        private WrapMode m_PostWrapMode = WrapMode.ClampForever;

        public AnimationCurve(params Keyframe[] keys)
        {
            if (keys != null)
                SetKeys(keys);
        }

        public AnimationCurve() { }

        public override bool Equals(object o)
        {
            if (o is null)
                return false;
            if (ReferenceEquals(this, o))
                return true;
            return o is AnimationCurve other && Equals(other);
        }

        /// <summary>True if both curves have the same keys and wrap modes.</summary>
        public bool Equals(AnimationCurve other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (m_PreWrapMode != other.m_PreWrapMode || m_PostWrapMode != other.m_PostWrapMode || m_Keys.Count != other.m_Keys.Count)
                return false;
            for (int i = 0; i < m_Keys.Count; i++)
            {
                Keyframe a = m_Keys[i], b = other.m_Keys[i];
                if (!a.time.Equals(b.time) || !a.value.Equals(b.value) || !a.inTangent.Equals(b.inTangent) ||
                    !a.outTangent.Equals(b.outTangent) || !a.inWeight.Equals(b.inWeight) || !a.outWeight.Equals(b.outWeight) ||
                    a.weightedMode != b.weightedMode || a.tangentModeInternal != b.tangentModeInternal)
                    return false;
            }
            return true;
        }

        /// <summary>Identity-based, like Unity's (which hashes the native pointer).</summary>
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

        public void CopyFrom(AnimationCurve other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));
            if (ReferenceEquals(this, other))
                return;
            m_Keys.Clear();
            m_Keys.AddRange(other.m_Keys);
            m_PreWrapMode = other.m_PreWrapMode;
            m_PostWrapMode = other.m_PostWrapMode;
        }

        public void ClearKeys() => m_Keys.Clear();

        public Keyframe[] keys
        {
            get => m_Keys.ToArray();
            set => SetKeys(value);
        }

        public Keyframe this[int index] => m_Keys[CheckIndex(index)];

        public int length => m_Keys.Count;

        /// <summary>
        /// Wrap mode before the first key. Clamp, Once and Default are stored as ClampForever, so
        /// they read back as ClampForever.
        /// </summary>
        public WrapMode preWrapMode
        {
            get => m_PreWrapMode;
            set => m_PreWrapMode = NormalizeWrapMode(value);
        }

        /// <summary>Wrap mode after the last key (see <see cref="preWrapMode"/>).</summary>
        public WrapMode postWrapMode
        {
            get => m_PostWrapMode;
            set => m_PostWrapMode = NormalizeWrapMode(value);
        }

        private static WrapMode NormalizeWrapMode(WrapMode mode)
        {
            switch (mode)
            {
                case WrapMode.Loop:
                case WrapMode.PingPong:
                    return mode;
                default:
                    return WrapMode.ClampForever;
            }
        }

        private void SetKeys(Keyframe[] keys)
        {
            if (keys == null)
                throw new ArgumentNullException(nameof(keys));
            m_Keys.Clear();
            m_Keys.AddRange(keys);
            SortKeys();
        }

        private void SortKeys()
        {
            // Stable sort by time.
            for (int i = 1; i < m_Keys.Count; i++)
            {
                Keyframe k = m_Keys[i];
                int j = i - 1;
                while (j >= 0 && m_Keys[j].time > k.time)
                {
                    m_Keys[j + 1] = m_Keys[j];
                    j--;
                }
                m_Keys[j + 1] = k;
            }
        }

        private int CheckIndex(int index)
        {
            if (index < 0 || index >= m_Keys.Count)
                throw new IndexOutOfRangeException(string.Format("Index {0} is out of range of '{1}' Length.", index, m_Keys.Count));
            return index;
        }

        // Inserts a key in time order; -1 if a key already exists at that time.
        private int InsertKey(Keyframe key)
        {
            int i = 0;
            while (i < m_Keys.Count && m_Keys[i].time < key.time)
                i++;
            if (i < m_Keys.Count && m_Keys[i].time == key.time)
                return -1;
            m_Keys.Insert(i, key);
            return i;
        }

        /// <summary>Adds a key with tangents smoothed against its neighbours; -1 if a key exists at <paramref name="time"/>.</summary>
        public int AddKey(float time, float value)
        {
            int index = InsertKey(new Keyframe(time, value));
            if (index >= 0)
                RecalculateSmoothTangents(index, 0.0f);
            return index;
        }

        /// <summary>Adds a key; -1 if a key already exists at its time.</summary>
        public int AddKey(Keyframe key) => InsertKey(key);

        /// <summary>
        /// Replaces the key at <paramref name="index"/>. If another key already sits at key.time,
        /// the old key's time is kept instead. Returns the key's new index.
        /// </summary>
        public int MoveKey(int index, Keyframe key)
        {
            CheckIndex(index);
            Keyframe old = m_Keys[index];
            m_Keys.RemoveAt(index);
            for (int i = 0; i < m_Keys.Count; i++)
            {
                if (m_Keys[i].time == key.time)
                {
                    key.time = old.time;
                    break;
                }
            }
            return InsertKey(key);
        }

        public void RemoveKey(int index)
        {
            CheckIndex(index);
            m_Keys.RemoveAt(index);
        }

        /// <summary>
        /// Smooths the tangents of the key at <paramref name="index"/>: both become a blend of the
        /// slopes to the previous and next keys (weight 0 averages them, -1/1 favour one side). End
        /// keys point at their only neighbour.
        /// </summary>
        public void SmoothTangents(int index, float weight)
        {
            CheckIndex(index);
            RecalculateSmoothTangents(index, weight);
        }

        private void RecalculateSmoothTangents(int index, float bias)
        {
            int count = m_Keys.Count;
            if (count < 2)
                return;
            Keyframe key = m_Keys[index];
            float slope;
            if (index == 0)
            {
                Keyframe next = m_Keys[1];
                slope = SafeSlope(next.value - key.value, next.time - key.time);
            }
            else if (index == count - 1)
            {
                Keyframe prev = m_Keys[index - 1];
                slope = SafeSlope(key.value - prev.value, key.time - prev.time);
            }
            else
            {
                Keyframe prev = m_Keys[index - 1];
                Keyframe next = m_Keys[index + 1];
                float m1 = SafeSlope(key.value - prev.value, key.time - prev.time);
                float m2 = SafeSlope(next.value - key.value, next.time - key.time);
                slope = (1.0f + bias) * 0.5f * m1 + (1.0f - bias) * 0.5f * m2;
            }
            key.inTangent = slope;
            key.outTangent = slope;
            m_Keys[index] = key;
        }

        private static float SafeSlope(float dy, float dx) => Math.Abs(dx) > 1e-12f ? dy / dx : 0.0f;

        // ---- Evaluation ----------------------------------------------------------------------

        /// <summary>Evaluates the curve at <paramref name="time"/>, applying the wrap modes outside the key range.</summary>
        public float Evaluate(float time)
        {
            int count = m_Keys.Count;
            if (count == 0)
                return 0.0f;
            if (count == 1)
                return m_Keys[0].value;

            float first = m_Keys[0].time;
            float last = m_Keys[count - 1].time;
            float duration = last - first;

            if (time < first)
            {
                if (!WrapTime(ref time, m_PreWrapMode, first, duration))
                    return m_Keys[0].value;
            }
            else if (time > last)
            {
                if (!WrapTime(ref time, m_PostWrapMode, first, duration))
                    return m_Keys[count - 1].value;
            }

            // Last segment whose start key is at or before time.
            int lo = 0, hi = count - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (m_Keys[mid].time <= time)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            if (time >= last)
                return m_Keys[count - 1].value;
            return EvaluateSegment(m_Keys[lo], m_Keys[lo + 1], time);
        }

        // Returns false when the mode clamps (the caller uses the end key's value).
        private static bool WrapTime(ref float time, WrapMode mode, float first, float duration)
        {
            if (duration <= 0.0f)
                return false;
            switch (mode)
            {
                case WrapMode.Loop:
                    time = first + Mathf.Repeat(time - first, duration);
                    return true;
                case WrapMode.PingPong:
                    time = first + Mathf.PingPong(time - first, duration);
                    return true;
                default:
                    return false;
            }
        }

        internal static float EvaluateSegment(Keyframe k0, Keyframe k1, float time)
        {
            float dx = k1.time - k0.time;
            if (dx <= 0.0f)
                return k0.value;
            if (float.IsInfinity(k0.outTangent) || float.IsInfinity(k1.inTangent))
                return k0.value; // stepped

            float s = (time - k0.time) / dx;
            bool outWeighted = (k0.weightedMode & WeightedMode.Out) != 0;
            bool inWeighted = (k1.weightedMode & WeightedMode.In) != 0;
            if (!outWeighted && !inWeighted)
            {
                // Cubic Hermite.
                float m0 = k0.outTangent * dx;
                float m1 = k1.inTangent * dx;
                float s2 = s * s;
                float s3 = s2 * s;
                float h00 = 2.0f * s3 - 3.0f * s2 + 1.0f;
                float h10 = s3 - 2.0f * s2 + s;
                float h01 = -2.0f * s3 + 3.0f * s2;
                float h11 = s3 - s2;
                return h00 * k0.value + h10 * m0 + h01 * k1.value + h11 * m1;
            }

            // Weighted: cubic Bezier whose control points lie along the tangents at the given
            // fractions of the segment's duration (an unweighted side uses 1/3, i.e. Hermite).
            double wOut = outWeighted ? Clamp01(k0.outWeight) : kDefaultWeight;
            double wIn = inWeighted ? Clamp01(k1.inWeight) : kDefaultWeight;
            double x1 = wOut;
            double x2 = 1.0 - wIn;
            double y0 = k0.value;
            double y1 = k0.value + wOut * dx * k0.outTangent;
            double y2 = k1.value - wIn * dx * k1.inTangent;
            double y3 = k1.value;
            double u = SolveBezierX(x1, x2, s);
            double v = 1.0 - u;
            return (float)(v * v * v * y0 + 3.0 * v * v * u * y1 + 3.0 * v * u * u * y2 + u * u * u * y3);
        }

        private static double Clamp01(float f) => f < 0f ? 0.0 : f > 1f ? 1.0 : f;

        // Parameter u in [0, 1] with Bx(u) = x for the Bezier x control values (0, x1, x2, 1).
        private static double SolveBezierX(double x1, double x2, double x)
        {
            if (x <= 0.0)
                return 0.0;
            if (x >= 1.0)
                return 1.0;
            double lo = 0.0, hi = 1.0, u = x;
            for (int i = 0; i < 32; i++)
            {
                double v = 1.0 - u;
                double bx = 3.0 * v * v * u * x1 + 3.0 * v * u * u * x2 + u * u * u;
                double err = bx - x;
                if (Math.Abs(err) < 1e-9)
                    return u;
                if (err > 0.0)
                    hi = u;
                else
                    lo = u;
                double d = 3.0 * v * v * x1 + 6.0 * v * u * (x2 - x1) + 3.0 * u * u * (1.0 - x2);
                double next = d > 1e-12 ? u - err / d : 0.5 * (lo + hi);
                if (next <= lo || next >= hi)
                    next = 0.5 * (lo + hi);
                u = next;
            }
            return u;
        }

        // ---- Factories -----------------------------------------------------------------------

        /// <summary>A straight line from (timeStart, valueStart) to (timeEnd, valueEnd).</summary>
        public static AnimationCurve Linear(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            if (timeStart == timeEnd)
                return new AnimationCurve(new Keyframe(timeStart, valueStart));
            float tangent = (valueEnd - valueStart) / (timeEnd - timeStart);
            return new AnimationCurve(
                new Keyframe(timeStart, valueStart, 0.0f, tangent),
                new Keyframe(timeEnd, valueEnd, tangent, 0.0f));
        }

        /// <summary>A flat curve with <paramref name="value"/> between the two times.</summary>
        public static AnimationCurve Constant(float timeStart, float timeEnd, float value) =>
            Linear(timeStart, value, timeEnd, value);

        /// <summary>An S-shaped curve with flat tangents at both ends.</summary>
        public static AnimationCurve EaseInOut(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            if (timeStart == timeEnd)
                return new AnimationCurve(new Keyframe(timeStart, valueStart));
            return new AnimationCurve(
                new Keyframe(timeStart, valueStart, 0.0f, 0.0f),
                new Keyframe(timeEnd, valueEnd, 0.0f, 0.0f));
        }
    }
}
