using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Representation of 3D vectors and points.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector3 : IEquatable<Vector3>, IFormattable
    {
        public const float kEpsilon = 0.00001F;
        public const float kEpsilonNormalSqrt = 1e-15F;

        public float x;
        public float y;
        public float z;

        private static readonly Vector3 zeroVector = new Vector3(0F, 0F, 0F);
        private static readonly Vector3 oneVector = new Vector3(1F, 1F, 1F);
        private static readonly Vector3 upVector = new Vector3(0F, 1F, 0F);
        private static readonly Vector3 downVector = new Vector3(0F, -1F, 0F);
        private static readonly Vector3 leftVector = new Vector3(-1F, 0F, 0F);
        private static readonly Vector3 rightVector = new Vector3(1F, 0F, 0F);
        private static readonly Vector3 forwardVector = new Vector3(0F, 0F, 1F);
        private static readonly Vector3 backVector = new Vector3(0F, 0F, -1F);
        private static readonly Vector3 positiveInfinityVector = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private static readonly Vector3 negativeInfinityVector = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(float x, float y)
        {
            this.x = x;
            this.y = y;
            z = 0F;
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
                    default: throw new IndexOutOfRangeException("Invalid Vector3 index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Vector3 index!");
                }
            }
        }

        public void Set(float newX, float newY, float newZ)
        {
            x = newX;
            y = newY;
            z = newZ;
        }

        /// <summary>
        /// Spherically interpolates between two vectors: the direction rotates along the great arc
        /// while the length is interpolated linearly. t is clamped to [0, 1].
        /// </summary>
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) => SlerpUnclamped(a, b, Mathf.Clamp01(t));

        public static Vector3 SlerpUnclamped(Vector3 a, Vector3 b, float t)
        {
            float aMag = Magnitude(a);
            float bMag = Magnitude(b);
            if (aMag < kEpsilon || bMag < kEpsilon)
                return LerpUnclamped(a, b, t);

            float mag = aMag + (bMag - aMag) * t;
            Vector3 aDir = a / aMag;
            Vector3 bDir = b / bMag;
            float dot = Mathf.Clamp(Dot(aDir, bDir), -1F, 1F);
            if (dot > 1F - kEpsilon)
                return LerpUnclamped(a, b, t);

            Vector3 perp;
            if (dot < -1F + kEpsilon)
                perp = OrthoNormalVectorFast(aDir);
            else
                perp = Normalize(bDir - aDir * dot);

            double angle = Math.Acos(dot) * t;
            float c = (float)Math.Cos(angle);
            float s = (float)Math.Sin(angle);
            Vector3 dir = aDir * c + perp * s;
            return dir * mag;
        }

        public static void OrthoNormalize(ref Vector3 normal, ref Vector3 tangent)
        {
            float mag = Magnitude(normal);
            if (mag > kEpsilon)
                normal /= mag;
            else
                normal = new Vector3(1F, 0F, 0F);

            float dot0 = Dot(normal, tangent);
            tangent -= dot0 * normal;
            mag = Magnitude(tangent);
            if (mag < kEpsilon)
                tangent = OrthoNormalVectorFast(normal);
            else
                tangent /= mag;
        }

        public static void OrthoNormalize(ref Vector3 normal, ref Vector3 tangent, ref Vector3 binormal)
        {
            OrthoNormalize(ref normal, ref tangent);

            float dot0 = Dot(normal, binormal);
            float dot1 = Dot(tangent, binormal);
            binormal -= dot0 * normal + dot1 * tangent;
            float mag = Magnitude(binormal);
            if (mag > kEpsilon)
                binormal /= mag;
            else
                binormal = Cross(normal, tangent);
        }

        // Any unit vector orthogonal to n (n must be normalized).
        internal static Vector3 OrthoNormalVectorFast(Vector3 n)
        {
            const float k1OverSqrt2 = 0.7071067811865475244008443621048490F;
            if (Math.Abs(n.z) > k1OverSqrt2)
            {
                float a = n.y * n.y + n.z * n.z;
                float k = 1F / (float)Math.Sqrt(a);
                return new Vector3(0F, -n.z * k, n.y * k);
            }
            else
            {
                float a = n.x * n.x + n.y * n.y;
                float k = 1F / (float)Math.Sqrt(a);
                return new Vector3(-n.y * k, n.x * k, 0F);
            }
        }

        /// <summary>
        /// Rotates <paramref name="current"/> towards <paramref name="target"/> by at most
        /// <paramref name="maxRadiansDelta"/> radians while moving its length towards the target's
        /// length by at most <paramref name="maxMagnitudeDelta"/>.
        /// </summary>
        public static Vector3 RotateTowards(Vector3 current, Vector3 target, float maxRadiansDelta, float maxMagnitudeDelta)
        {
            float currentMag = Magnitude(current);
            float targetMag = Magnitude(target);
            if (currentMag > kEpsilon && targetMag > kEpsilon)
            {
                Vector3 currentDir = current / currentMag;
                Vector3 targetDir = target / targetMag;
                float dot = Dot(currentDir, targetDir);
                if (dot > 1F - kEpsilon)
                    return MoveTowards(current, target, maxMagnitudeDelta);

                float newMag = ClampedMove(currentMag, targetMag, maxMagnitudeDelta);
                Vector3 perp;
                float angle;
                if (dot < -1F + kEpsilon)
                {
                    perp = OrthoNormalVectorFast(currentDir);
                    angle = maxRadiansDelta;
                }
                else
                {
                    perp = Normalize(targetDir - currentDir * dot);
                    angle = Math.Min(maxRadiansDelta, (float)Math.Acos(Mathf.Clamp(dot, -1F, 1F)));
                }
                float c = (float)Math.Cos(angle);
                float s = (float)Math.Sin(angle);
                return (currentDir * c + perp * s) * newMag;
            }
            return MoveTowards(current, target, maxMagnitudeDelta);
        }

        private static float ClampedMove(float lhs, float rhs, float clampedDelta)
        {
            float delta = rhs - lhs;
            if (delta > 0F)
                return lhs + Math.Min(delta, clampedDelta);
            return lhs - Math.Min(-delta, clampedDelta);
        }

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) =>
            new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            float toVectorX = target.x - current.x;
            float toVectorY = target.y - current.y;
            float toVectorZ = target.z - current.z;
            float sqdist = toVectorX * toVectorX + toVectorY * toVectorY + toVectorZ * toVectorZ;
            if (sqdist == 0 || (maxDistanceDelta >= 0 && sqdist <= maxDistanceDelta * maxDistanceDelta))
                return target;
            float dist = (float)Math.Sqrt(sqdist);
            return new Vector3(current.x + toVectorX / dist * maxDistanceDelta,
                current.y + toVectorY / dist * maxDistanceDelta,
                current.z + toVectorZ / dist * maxDistanceDelta);
        }

        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = Time.deltaTime;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime)
        {
            float deltaTime = Time.deltaTime;
            float maxSpeed = Mathf.Infinity;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            smoothTime = Mathf.Max(0.0001F, smoothTime);
            float omega = 2F / smoothTime;

            float x = omega * deltaTime;
            float exp = 1F / (1F + x + 0.48F * x * x + 0.235F * x * x * x);

            float changeX = current.x - target.x;
            float changeY = current.y - target.y;
            float changeZ = current.z - target.z;
            Vector3 originalTo = target;

            float maxChange = maxSpeed * smoothTime;
            float maxChangeSq = maxChange * maxChange;
            float sqrmag = changeX * changeX + changeY * changeY + changeZ * changeZ;
            if (sqrmag > maxChangeSq)
            {
                float mag = (float)Math.Sqrt(sqrmag);
                changeX = changeX / mag * maxChange;
                changeY = changeY / mag * maxChange;
                changeZ = changeZ / mag * maxChange;
            }

            target.x = current.x - changeX;
            target.y = current.y - changeY;
            target.z = current.z - changeZ;

            float tempX = (currentVelocity.x + omega * changeX) * deltaTime;
            float tempY = (currentVelocity.y + omega * changeY) * deltaTime;
            float tempZ = (currentVelocity.z + omega * changeZ) * deltaTime;

            currentVelocity.x = (currentVelocity.x - omega * tempX) * exp;
            currentVelocity.y = (currentVelocity.y - omega * tempY) * exp;
            currentVelocity.z = (currentVelocity.z - omega * tempZ) * exp;

            float outputX = target.x + (changeX + tempX) * exp;
            float outputY = target.y + (changeY + tempY) * exp;
            float outputZ = target.z + (changeZ + tempZ) * exp;

            // Do not overshoot: stop at the target if the output went past it.
            float origMinusCurrentX = originalTo.x - current.x;
            float origMinusCurrentY = originalTo.y - current.y;
            float origMinusCurrentZ = originalTo.z - current.z;
            float outMinusOrigX = outputX - originalTo.x;
            float outMinusOrigY = outputY - originalTo.y;
            float outMinusOrigZ = outputZ - originalTo.z;
            if (origMinusCurrentX * outMinusOrigX + origMinusCurrentY * outMinusOrigY + origMinusCurrentZ * outMinusOrigZ > 0)
            {
                outputX = originalTo.x;
                outputY = originalTo.y;
                outputZ = originalTo.z;
                currentVelocity.x = (outputX - originalTo.x) / deltaTime;
                currentVelocity.y = (outputY - originalTo.y) / deltaTime;
                currentVelocity.z = (outputZ - originalTo.z) / deltaTime;
            }
            return new Vector3(outputX, outputY, outputZ);
        }

        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

        public void Scale(Vector3 scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
        }

        public static Vector3 Cross(Vector3 lhs, Vector3 rhs) =>
            new Vector3(lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.x * rhs.y - lhs.y * rhs.x);

        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);

        public override bool Equals(object other) => other is Vector3 v && Equals(v);

        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;

        public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal)
        {
            float factor = -2F * Dot(inNormal, inDirection);
            return new Vector3(factor * inNormal.x + inDirection.x,
                factor * inNormal.y + inDirection.y,
                factor * inNormal.z + inDirection.z);
        }

        public static Vector3 Normalize(Vector3 value)
        {
            float mag = Magnitude(value);
            if (mag > kEpsilon)
                return value / mag;
            return zero;
        }

        public void Normalize()
        {
            float mag = Magnitude(this);
            if (mag > kEpsilon)
                this = this / mag;
            else
                this = zero;
        }

        public Vector3 normalized => Normalize(this);

        public static float Dot(Vector3 lhs, Vector3 rhs) => lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;

        public static Vector3 Project(Vector3 vector, Vector3 onNormal)
        {
            float sqrMag = Dot(onNormal, onNormal);
            if (sqrMag < Mathf.Epsilon)
                return zero;
            float dot = Dot(vector, onNormal);
            return new Vector3(onNormal.x * dot / sqrMag, onNormal.y * dot / sqrMag, onNormal.z * dot / sqrMag);
        }

        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
        {
            float sqrMag = Dot(planeNormal, planeNormal);
            if (sqrMag < Mathf.Epsilon)
                return vector;
            float dot = Dot(vector, planeNormal);
            return new Vector3(vector.x - planeNormal.x * dot / sqrMag,
                vector.y - planeNormal.y * dot / sqrMag,
                vector.z - planeNormal.z * dot / sqrMag);
        }

        /// <summary>The unsigned angle in degrees between two vectors (0 if either is too short).</summary>
        public static float Angle(Vector3 from, Vector3 to)
        {
            float denominator = (float)Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denominator < kEpsilonNormalSqrt)
                return 0F;
            float dot = Mathf.Clamp(Dot(from, to) / denominator, -1F, 1F);
            return (float)Math.Acos(dot) * Mathf.Rad2Deg;
        }

        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis)
        {
            float unsignedAngle = Angle(from, to);
            float crossX = from.y * to.z - from.z * to.y;
            float crossY = from.z * to.x - from.x * to.z;
            float crossZ = from.x * to.y - from.y * to.x;
            float sign = Mathf.Sign(axis.x * crossX + axis.y * crossY + axis.z * crossZ);
            return unsignedAngle * sign;
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;
            float diffZ = a.z - b.z;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY + diffZ * diffZ);
        }

        public static Vector3 ClampMagnitude(Vector3 vector, float maxLength)
        {
            float sqrmag = vector.sqrMagnitude;
            if (sqrmag > maxLength * maxLength)
            {
                float mag = (float)Math.Sqrt(sqrmag);
                float normalizedX = vector.x / mag;
                float normalizedY = vector.y / mag;
                float normalizedZ = vector.z / mag;
                return new Vector3(normalizedX * maxLength, normalizedY * maxLength, normalizedZ * maxLength);
            }
            return vector;
        }

        public static float Magnitude(Vector3 vector) => (float)Math.Sqrt(vector.x * vector.x + vector.y * vector.y + vector.z * vector.z);

        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);

        public static float SqrMagnitude(Vector3 vector) => vector.x * vector.x + vector.y * vector.y + vector.z * vector.z;

        public float sqrMagnitude => x * x + y * y + z * z;

        public static Vector3 Min(Vector3 lhs, Vector3 rhs) =>
            new Vector3(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z));

        public static Vector3 Max(Vector3 lhs, Vector3 rhs) =>
            new Vector3(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z));

        public static Vector3 zero => zeroVector;
        public static Vector3 one => oneVector;
        public static Vector3 forward => forwardVector;
        public static Vector3 back => backVector;
        public static Vector3 up => upVector;
        public static Vector3 down => downVector;
        public static Vector3 left => leftVector;
        public static Vector3 right => rightVector;
        public static Vector3 positiveInfinity => positiveInfinityVector;
        public static Vector3 negativeInfinity => negativeInfinityVector;

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);

        /// <summary>Approximate equality: the squared distance is below kEpsilon squared.</summary>
        public static bool operator ==(Vector3 lhs, Vector3 rhs)
        {
            float diffX = lhs.x - rhs.x;
            float diffY = lhs.y - rhs.y;
            float diffZ = lhs.z - rhs.z;
            float sqrmag = diffX * diffX + diffY * diffY + diffZ * diffZ;
            return sqrmag < kEpsilon * kEpsilon;
        }

        public static bool operator !=(Vector3 lhs, Vector3 rhs) => !(lhs == rhs);

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F2");
            formatProvider = MathFormat.Provider(formatProvider);
            return "(" + x.ToString(format, formatProvider) + ", " + y.ToString(format, formatProvider) + ", " +
                   z.ToString(format, formatProvider) + ")";
        }

        [Obsolete("Use Vector3.forward instead.")]
        public static Vector3 fwd => new Vector3(0F, 0F, 1F);

        [Obsolete("Use Vector3.Angle instead. AngleBetween uses radians instead of degrees and was deprecated for this reason")]
        public static float AngleBetween(Vector3 from, Vector3 to) =>
            (float)Math.Acos(Mathf.Clamp(Dot(from.normalized, to.normalized), -1F, 1F));

        [Obsolete("Use Vector3.ProjectOnPlane instead.")]
        public static Vector3 Exclude(Vector3 excludeThis, Vector3 fromThat) => ProjectOnPlane(fromThat, excludeThis);
    }
}
