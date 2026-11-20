using System;
using System.Collections.Generic;

namespace UnityEngine.Assertions.Comparers
{
    /// <summary>
    /// Compares floats within an error: absolute (the difference at most the error) or relative
    /// (the difference over the larger magnitude at most the error; equal values always compare
    /// equal), as Unity 6000.0 declares it. The default error is <see cref="kEpsilon"/>. Hashes are
    /// the comparer's own, not the value's.
    /// </summary>
    public partial class FloatComparer : IEqualityComparer<float>
    {
        public const float kEpsilon = 1E-05f;

        public static readonly FloatComparer s_ComparerWithDefaultTolerance = new FloatComparer(kEpsilon);

        readonly float m_Error;
        readonly bool m_Relative;

        public FloatComparer() : this(kEpsilon, false) { }

        public FloatComparer(bool relative) : this(kEpsilon, relative) { }

        public FloatComparer(float error) : this(error, false) { }

        public FloatComparer(float error, bool relative)
        {
            m_Error = error;
            m_Relative = relative;
        }

        public bool Equals(float a, float b) => m_Relative ? AreEqualRelative(a, b, m_Error) : AreEqual(a, b, m_Error);

        public int GetHashCode(float obj) => base.GetHashCode();

        public static bool AreEqual(float expected, float actual, float error) => Math.Abs(actual - expected) <= error;

        public static bool AreEqualRelative(float expected, float actual, float error)
        {
            if (expected == actual) return true;
            float e = Math.Abs(expected), a = Math.Abs(actual);
            float larger = e > a ? e : a;
            return Math.Abs((actual - expected) / larger) <= error;
        }
    }
}
