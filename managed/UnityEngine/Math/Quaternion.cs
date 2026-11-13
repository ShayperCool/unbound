using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// Quaternions represent rotations. Euler angles follow Unity's convention: degrees, applied
    /// around Z, then X, then Y (q = qY * qX * qZ).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Quaternion : IEquatable<Quaternion>, IFormattable
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public const float kEpsilon = 0.000001F;

        private static readonly Quaternion identityQuaternion = new Quaternion(0F, 0F, 0F, 1F);

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    case 3: return w;
                    default: throw new IndexOutOfRangeException("Invalid Quaternion index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    case 3: w = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Quaternion index!");
                }
            }
        }

        public void Set(float newX, float newY, float newZ, float newW)
        {
            x = newX;
            y = newY;
            z = newZ;
            w = newW;
        }

        public static Quaternion identity => identityQuaternion;

        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) =>
            new Quaternion(
                lhs.w * rhs.x + lhs.x * rhs.w + lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.w * rhs.y + lhs.y * rhs.w + lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.w * rhs.z + lhs.z * rhs.w + lhs.x * rhs.y - lhs.y * rhs.x,
                lhs.w * rhs.w - lhs.x * rhs.x - lhs.y * rhs.y - lhs.z * rhs.z);

        /// <summary>Rotates <paramref name="point"/> (assumes a unit quaternion).</summary>
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            float x2 = rotation.x * 2F;
            float y2 = rotation.y * 2F;
            float z2 = rotation.z * 2F;
            float xx = rotation.x * x2;
            float yy = rotation.y * y2;
            float zz = rotation.z * z2;
            float xy = rotation.x * y2;
            float xz = rotation.x * z2;
            float yz = rotation.y * z2;
            float wx = rotation.w * x2;
            float wy = rotation.w * y2;
            float wz = rotation.w * z2;

            Vector3 res;
            // Retain the rounded products, then accumulate each component at wider precision.
            // Measured by tests/projects/quaternioneuler with unit and nonunit public inputs.
            res.x = (float)((1.0 - ((double)yy + zz)) * point.x + ((double)xy - wz) * point.y + ((double)xz + wy) * point.z);
            res.y = (float)(((double)xy + wz) * point.x + (1.0 - ((double)xx + zz)) * point.y + ((double)yz - wx) * point.z);
            res.z = (float)(((double)xz - wy) * point.x + ((double)yz + wx) * point.y + (1.0 - ((double)xx + yy)) * point.z);
            return res;
        }

        private static bool IsEqualUsingDot(float dot) => dot > 1.0f - kEpsilon;

        /// <summary>Approximate equality: true if the dot product exceeds 1 - kEpsilon (q and -q differ).</summary>
        public static bool operator ==(Quaternion lhs, Quaternion rhs) => IsEqualUsingDot(Dot(lhs, rhs));

        public static bool operator !=(Quaternion lhs, Quaternion rhs) => !(lhs == rhs);

        public static float Dot(Quaternion a, Quaternion b) => Unbound.Runtime.Compat.QuaternionApiPrecisionMeasured
            ? (float)((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w)
            : QuaternionMath.Dot(a, b);

        /// <summary>The angle in degrees between two rotations.</summary>
        public static float Angle(Quaternion a, Quaternion b)
        {
            float product = Dot(a, b);
            float dot = Unbound.Runtime.Compat.QuaternionApiPrecisionMeasured
                ? Mathf.Min(Mathf.Abs(product), 1.0F) : Math.Min(Math.Abs(product), 1.0F);
            return IsEqualUsingDot(dot) ? 0.0f : (float)Math.Acos(dot) * 2.0F * Mathf.Rad2Deg;
        }

        public static Quaternion Inverse(Quaternion rotation) => new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);

        public static Quaternion Normalize(Quaternion q)
        {
            float mag = (float)Math.Sqrt(Dot(q, q));
            if (mag < Mathf.Epsilon)
                return identity;
            return new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
        }

        public void Normalize() => this = Normalize(this);

        public Quaternion normalized => Normalize(this);

        // ---- Euler angles ------------------------------------------------------------------

        public static Quaternion Euler(float x, float y, float z) => FromEulerRad(new Vector3(x, y, z) * Mathf.Deg2Rad);

        public static Quaternion Euler(Vector3 euler) => FromEulerRad(euler * Mathf.Deg2Rad);

        public Vector3 eulerAngles
        {
            get => MakePositive(ToEulerRad(this) * Mathf.Rad2Deg);
            set => this = FromEulerRad(value * Mathf.Deg2Rad);
        }

        // Rotation about Z, then X, then Y: q = qY * qX * qZ. Each half angle's sine and cosine come
        // from one sincos, which matches the macOS arm64 6000.0.71f1 Editor where it differs from
        // separate sin and cos in the last bit (tests/projects/solverpairorder --transform-inputs);
        // glibc's sincosf equals its sinf and cosf for every float, so Linux results do not change.
        internal static Quaternion FromEulerRad(Vector3 euler)
        {
            float hx = euler.x * 0.5f, hy = euler.y * 0.5f, hz = euler.z * 0.5f;
            var (sx, cx) = MathF.SinCos(hx);
            var (sy, cy) = MathF.SinCos(hy);
            var (sz, cz) = MathF.SinCos(hz);
            return new Quaternion(
                cy * sx * cz + sy * cx * sz,
                sy * cx * cz - cy * sx * sz,
                cy * cx * sz - sy * sx * cz,
                cy * cx * cz + sy * sx * sz);
        }

        // Inverse of FromEulerRad, in radians, from the normalized rotation (identity when its length is
        // below 1e-5). Outside gimbal lock X comes from the Y axis's Z component and Y and Z from two
        // arctangents; in gimbal lock (|yz - xw| >= 0.499999) Z is 0 and Y carries the whole
        // remaining rotation. Single precision in the order of the Unity 6000.0.71f1 Editor
        // (tests/projects/floatrounding, native/quaternionEulerAngles).
        internal static Vector3 ToEulerRad(Quaternion rotation)
        {
            const float gimbalLock = 0.499999f;
            float length = MathF.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
            Quaternion q = length < RotationMatrix.MinimumLength ? identity
                : new Quaternion(rotation.x / length, rotation.y / length, rotation.z / length, rotation.w / length);
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z, ww = q.w * q.w;
            if (!(MathF.Abs(xx + yy + zz + ww - 1f) < RotationMatrix.MinimumLength))
                Debug.LogAssertion(NotNormalizedMessage);
            float xy = q.x * q.y, xz = q.x * q.z, xw = q.x * q.w;
            float yz = q.y * q.z, yw = q.y * q.w, zw = q.z * q.w;
            float sinX = yz - xw;
            float ex = -MathF.Asin(Math.Clamp(sinX + sinX, -1f, 1f));
            Vector3 euler;
            if (MathF.Abs(sinX) < gimbalLock)
            {
                float y = xz + yw, z = xy + zw;
                euler = new Vector3(ex, MathF.Atan2(y + y, ww + (zz - xx - yy)), MathF.Atan2(z + z, ww + (yy - zz - xx)));
            }
            else
            {
                float a = xy + zw, b = xw - yz, c = yz + xw, d = xy - zw;
                euler = new Vector3(ex, MathF.Atan2(a * c + b * d, b * c - a * d), 0f);
            }
            if (!float.IsFinite(euler.x) || !float.IsFinite(euler.y) || !float.IsFinite(euler.z))
                Debug.LogAssertion(NotFiniteMessage);
            return euler;
        }

        // The Editor's console messages for a rotation that is not normalized or gives angles that
        // are not finite (tests/projects/floatrounding, native/log).
        private const string NotNormalizedMessage = "QuaternionToEuler: Input quaternion was not normalized";
        private const string NotFiniteMessage = "Assertion failed on expression: 'IsFinite(rot)'";

        // Maps angles into [0, 360), leaving tiny negative values (above -0.0001 rad) untouched,
        // as Unity's eulerAngles does.
        private static Vector3 MakePositive(Vector3 euler)
        {
            const float negativeFlip = -0.0001f * Mathf.Rad2Deg;
            const float positiveFlip = 360.0f + negativeFlip;
            euler.x = MakePositive(euler.x, negativeFlip, positiveFlip);
            euler.y = MakePositive(euler.y, negativeFlip, positiveFlip);
            euler.z = MakePositive(euler.z, negativeFlip, positiveFlip);
            return euler;
        }

        private static float MakePositive(float angle, float negativeFlip, float positiveFlip)
        {
            if (angle < negativeFlip)
                return angle + 360.0f;
            if (angle > positiveFlip)
                return angle - 360.0f;
            return angle;
        }

        // ---- Axis / angle ------------------------------------------------------------------

        /// <summary>The rotation by <paramref name="angle"/> degrees about <paramref name="axis"/>;
        /// identity when the axis is not longer than 1e-6.</summary>
        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            const float minimumAxisLength = 1e-6f;
            float length = RotationMatrix.Length(axis.x, axis.y, axis.z);
            if (!(length > minimumAxisLength))
                return identity;
            // Degrees to radians as angle / 360 doubled times PI, then halved.
            float turns = angle / 360f;
            var (sin, cos) = MathF.SinCos((turns + turns) * MathF.PI * 0.5f);
            float scale = sin / length;
            return new Quaternion(axis.x * scale, axis.y * scale, axis.z * scale, cos);
        }

        public void ToAngleAxis(out float angle, out Vector3 axis)
        {
            ToAxisAngleRad(this, out axis, out angle);
            angle *= Mathf.Rad2Deg;
        }

        private static void ToAxisAngleRad(Quaternion q, out Vector3 axis, out float angle)
        {
            if (!Unbound.Runtime.Compat.QuaternionApiPrecisionMeasured)
            {
                QuaternionMath.ToAxisAngleRad(q, out axis, out angle);
                return;
            }
            // Axis conversion has its own small-input cutoff, measured by quaternioneuler.
            const float minimumMagnitude = 1e-5f;
            float magnitude = MathF.Sqrt(QuaternionMath.Dot(q, q));
            q = magnitude < minimumMagnitude ? identity
                : new Quaternion(q.x / magnitude, q.y / magnitude, q.z / magnitude, q.w / magnitude);
            angle = 2f * MathF.Acos(q.w);
            float sinHalf = MathF.Sqrt(1f - q.w * q.w);
            if (sinHalf <= 1e-6f)
            {
                axis = Vector3.right;
                return;
            }
            float inverse = 1f / sinHalf;
            axis = new Vector3(q.x * inverse, q.y * inverse, q.z * inverse);
        }

        // ---- Look / from-to ----------------------------------------------------------------

        /// <summary>The rotation from one direction to another; identity when either is shorter
        /// than 1e-5.</summary>
        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection)
        {
            float fromLength = RotationMatrix.Length(fromDirection.x, fromDirection.y, fromDirection.z);
            float toLength = RotationMatrix.Length(toDirection.x, toDirection.y, toDirection.z);
            if (fromLength < RotationMatrix.MinimumLength || toLength < RotationMatrix.MinimumLength)
                return identity;
            var from = new Vector3(fromDirection.x / fromLength, fromDirection.y / fromLength, fromDirection.z / fromLength);
            var to = new Vector3(toDirection.x / toLength, toDirection.y / toLength, toDirection.z / toLength);
            Span<float> m = stackalloc float[9];
            RotationMatrix.FromTo(from, to, m);
            return RotationMatrix.ToQuaternion(m);
        }

        public void SetFromToRotation(Vector3 fromDirection, Vector3 toDirection) => this = FromToRotation(fromDirection, toDirection);

        public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);

        /// <summary>
        /// The rotation whose Z axis points along <paramref name="forward"/> and whose Y axis lies in
        /// the plane of forward and <paramref name="upwards"/>. A forward not longer than 1e-5 gives
        /// identity; when no such frame exists (forward parallel to upwards) the shortest rotation
        /// from Z to forward.
        /// </summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards)
        {
            if (!TryLookRotation(forward, upwards, out Quaternion rotation))
                Debug.Log(ZeroViewingVectorMessage);
            return rotation;
        }

        // The Editor's console message for a forward too short to look along (tests/projects/floatrounding, native/log).
        private const string ZeroViewingVectorMessage = "Look rotation viewing vector is zero";

        /// <summary>LookRotation without its console message, for the engine's own callers (identity
        /// for a forward not longer than 1e-5).</summary>
        internal static Quaternion LookRotationSilent(Vector3 forward, Vector3 upwards)
        {
            TryLookRotation(forward, upwards, out Quaternion rotation);
            return rotation;
        }

        internal static Quaternion LookRotationSilent(Vector3 forward) => LookRotationSilent(forward, Vector3.up);

        private static bool TryLookRotation(Vector3 forward, Vector3 upwards, out Quaternion rotation)
        {
            Span<float> m = stackalloc float[9];
            if (RotationMatrix.Look(forward, upwards, m))
            {
                rotation = RotationMatrix.ToQuaternion(m);
                return true;
            }
            float length = RotationMatrix.Length(forward.x, forward.y, forward.z);
            if (!(length > RotationMatrix.MinimumLength))
            {
                rotation = identity;
                return false;
            }
            RotationMatrix.FromTo(Vector3.forward, new Vector3(forward.x / length, forward.y / length, forward.z / length), m);
            rotation = RotationMatrix.ToQuaternion(m);
            return true;
        }

        public void SetLookRotation(Vector3 view)
        {
            Vector3 up = Vector3.up;
            SetLookRotation(view, up);
        }

        public void SetLookRotation(Vector3 view, Vector3 up) => this = LookRotation(view, up);

        // ---- Interpolation -----------------------------------------------------------------

        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => SlerpUnclamped(a, b, Mathf.Clamp01(t));

        /// <summary>
        /// Spherical interpolation along the shorter arc. Rotations closer than a dot product of 0.95
        /// are interpolated linearly and normalized instead; the spherical result is not normalized.
        /// Single precision in the order of the Unity 6000.0.71f1 Editor (tests/projects/floatrounding,
        /// native/quaternionSlerp).
        /// </summary>
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            const float linearDot = 0.95f;
            float dot = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            if (dot < 0f)
            {
                dot = -dot;
                b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            }
            if (dot < linearDot)
            {
                float angle = MathF.Acos(dot);
                float inverseSin = 1f / MathF.Sin(angle);
                float wb = MathF.Sin(angle * t);
                float wa = MathF.Sin((1f - t) * angle);
                return new Quaternion((a.x * wa + b.x * wb) * inverseSin, (a.y * wa + b.y * wb) * inverseSin,
                    (a.z * wa + b.z * wb) * inverseSin, (a.w * wa + b.w * wb) * inverseSin);
            }
            return LerpNormalized(a, b, t);
        }

        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => LerpUnclamped(a, b, Mathf.Clamp01(t));

        /// <summary>Normalized linear interpolation along the shorter arc.</summary>
        public static Quaternion LerpUnclamped(Quaternion a, Quaternion b, float t) => LerpNormalized(a, b, t);

        // a + (b - a) * t, b negated when the dot product is negative, divided by its length.
        private static Quaternion LerpNormalized(Quaternion a, Quaternion b, float t)
        {
            if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f)
                b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            float x = a.x + (b.x - a.x) * t, y = a.y + (b.y - a.y) * t, z = a.z + (b.z - a.z) * t, w = a.w + (b.w - a.w) * t;
            float length = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (length <= RotationMatrix.MinimumNormalizedLength)
                Debug.LogAssertion(RotationMatrix.ZeroLengthAssertion);
            return new Quaternion(x / length, y / length, z / length, w / length);
        }

        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            if (angle == 0.0f)
                return to;
            return SlerpUnclamped(from, to, Mathf.Min(1.0f, maxDegreesDelta / angle));
        }

        // ---- Object overrides --------------------------------------------------------------

        public override int GetHashCode() =>
            x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);

        public override bool Equals(object other) => other is Quaternion q && Equals(q);

        public bool Equals(Quaternion other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z) && w.Equals(other.w);

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F5");
            formatProvider = MathFormat.Provider(formatProvider);
            return "(" + MathFormat.F(x, format, formatProvider) + ", " + MathFormat.F(y, format, formatProvider) + ", " +
                   MathFormat.F(z, format, formatProvider) + ", " + MathFormat.F(w, format, formatProvider) + ")";
        }

        // ---- Obsolete radian-based API -----------------------------------------------------

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerRotation(float x, float y, float z) => FromEulerRad(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerRotation(Vector3 euler) => FromEulerRad(euler);

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerRotation(float x, float y, float z) => this = FromEulerRad(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerRotation(Vector3 euler) => this = FromEulerRad(euler);

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public Vector3 ToEuler() => ToEulerRad(this);

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerAngles(float x, float y, float z) => FromEulerRad(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Quaternion EulerAngles(Vector3 euler) => FromEulerRad(euler);

        [Obsolete("Use Quaternion.ToAngleAxis instead. This function was deprecated because it uses radians instead of degrees.")]
        public void ToAxisAngle(out Vector3 axis, out float angle) => ToAxisAngleRad(this, out axis, out angle);

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerAngles(float x, float y, float z) => SetEulerRotation(new Vector3(x, y, z));

        [Obsolete("Use Quaternion.Euler instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetEulerAngles(Vector3 euler) => this = EulerRotation(euler);

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public static Vector3 ToEulerAngles(Quaternion rotation) => ToEulerRad(rotation);

        [Obsolete("Use Quaternion.eulerAngles instead. This function was deprecated because it uses radians instead of degrees.")]
        public Vector3 ToEulerAngles() => ToEulerRad(this);

        [Obsolete("Use Quaternion.AngleAxis instead. This function was deprecated because it uses radians instead of degrees.")]
        public void SetAxisAngle(Vector3 axis, float angle) => this = AxisAngle(axis, angle);

        [Obsolete("Use Quaternion.AngleAxis instead. This function was deprecated because it uses radians instead of degrees")]
        public static Quaternion AxisAngle(Vector3 axis, float angle) => AngleAxis(Mathf.Rad2Deg * angle, axis);
    }
}
