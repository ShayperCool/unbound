using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Representation of 2D vectors and points.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector2 : IEquatable<Vector2>, IFormattable
    {
        public float x;
        public float y;

        public const float kEpsilon = 0.00001F;
        public const float kEpsilonNormalSqrt = 1e-15f;

        private static readonly Vector2 zeroVector = new Vector2(0F, 0F);
        private static readonly Vector2 oneVector = new Vector2(1F, 1F);
        private static readonly Vector2 upVector = new Vector2(0F, 1F);
        private static readonly Vector2 downVector = new Vector2(0F, -1F);
        private static readonly Vector2 leftVector = new Vector2(-1F, 0F);
        private static readonly Vector2 rightVector = new Vector2(1F, 0F);
        private static readonly Vector2 positiveInfinityVector = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        private static readonly Vector2 negativeInfinityVector = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    default: throw new IndexOutOfRangeException("Invalid Vector2 index!");
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    default: throw new IndexOutOfRangeException("Invalid Vector2 index!");
                }
            }
        }

        public void Set(float newX, float newY)
        {
            x = newX;
            y = newY;
        }

        public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }

        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) =>
            new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

        public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
        {
            float toVectorX = target.x - current.x;
            float toVectorY = target.y - current.y;
            float sqDist = toVectorX * toVectorX + toVectorY * toVectorY;
            if (sqDist == 0 || (maxDistanceDelta >= 0 && sqDist <= maxDistanceDelta * maxDistanceDelta))
                return target;
            float dist = (float)Math.Sqrt(sqDist);
            return new Vector2(current.x + toVectorX / dist * maxDistanceDelta, current.y + toVectorY / dist * maxDistanceDelta);
        }

        public static Vector2 Scale(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);

        public void Scale(Vector2 scale)
        {
            x *= scale.x;
            y *= scale.y;
        }

        public void Normalize()
        {
            float mag = magnitude;
            if (mag > kEpsilon)
                this = this / mag;
            else
                this = zero;
        }

        public Vector2 normalized
        {
            get
            {
                Vector2 v = new Vector2(x, y);
                v.Normalize();
                return v;
            }
        }

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F2");
            formatProvider = MathFormat.Provider(formatProvider);
            return "(" + x.ToString(format, formatProvider) + ", " + y.ToString(format, formatProvider) + ")";
        }

        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);

        public override bool Equals(object other) => other is Vector2 v && Equals(v);

        public bool Equals(Vector2 other) => x == other.x && y == other.y;

        public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)
        {
            float factor = -2F * Dot(inNormal, inDirection);
            return new Vector2(factor * inNormal.x + inDirection.x, factor * inNormal.y + inDirection.y);
        }

        public static Vector2 Perpendicular(Vector2 inDirection) => new Vector2(-inDirection.y, inDirection.x);

        public static float Dot(Vector2 lhs, Vector2 rhs) => lhs.x * rhs.x + lhs.y * rhs.y;

        public float magnitude => (float)Math.Sqrt(x * x + y * y);

        public float sqrMagnitude => x * x + y * y;

        public static float Angle(Vector2 from, Vector2 to)
        {
            float denominator = (float)Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denominator < kEpsilonNormalSqrt)
                return 0F;
            float dot = Mathf.Clamp(Dot(from, to) / denominator, -1F, 1F);
            return (float)Math.Acos(dot) * Mathf.Rad2Deg;
        }

        public static float SignedAngle(Vector2 from, Vector2 to)
        {
            float unsignedAngle = Angle(from, to);
            float sign = Mathf.Sign(from.x * to.y - from.y * to.x);
            return unsignedAngle * sign;
        }

        public static float Distance(Vector2 a, Vector2 b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
        {
            float sqrMag = vector.sqrMagnitude;
            if (sqrMag > maxLength * maxLength)
            {
                float mag = (float)Math.Sqrt(sqrMag);
                float normalizedX = vector.x / mag;
                float normalizedY = vector.y / mag;
                return new Vector2(normalizedX * maxLength, normalizedY * maxLength);
            }
            return vector;
        }

        public static float SqrMagnitude(Vector2 a) => a.x * a.x + a.y * a.y;

        public float SqrMagnitude() => x * x + y * y;

        public static Vector2 Min(Vector2 lhs, Vector2 rhs) => new Vector2(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y));

        public static Vector2 Max(Vector2 lhs, Vector2 rhs) => new Vector2(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y));

        public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime, float maxSpeed)
        {
            float deltaTime = Time.deltaTime;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime)
        {
            float deltaTime = Time.deltaTime;
            float maxSpeed = Mathf.Infinity;
            return SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);
        }

        public static Vector2 SmoothDamp(Vector2 current, Vector2 target, ref Vector2 currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            smoothTime = Mathf.Max(0.0001F, smoothTime);
            float omega = 2F / smoothTime;

            float x = omega * deltaTime;
            float exp = 1F / (1F + x + 0.48F * x * x + 0.235F * x * x * x);

            float changeX = current.x - target.x;
            float changeY = current.y - target.y;
            Vector2 originalTo = target;

            float maxChange = maxSpeed * smoothTime;
            float maxChangeSq = maxChange * maxChange;
            float sqDist = changeX * changeX + changeY * changeY;
            if (sqDist > maxChangeSq)
            {
                float mag = (float)Math.Sqrt(sqDist);
                changeX = changeX / mag * maxChange;
                changeY = changeY / mag * maxChange;
            }

            target.x = current.x - changeX;
            target.y = current.y - changeY;

            float tempX = (currentVelocity.x + omega * changeX) * deltaTime;
            float tempY = (currentVelocity.y + omega * changeY) * deltaTime;

            currentVelocity.x = (currentVelocity.x - omega * tempX) * exp;
            currentVelocity.y = (currentVelocity.y - omega * tempY) * exp;

            float outputX = target.x + (changeX + tempX) * exp;
            float outputY = target.y + (changeY + tempY) * exp;

            // Do not overshoot: stop at the target if the output went past it.
            float origMinusCurrentX = originalTo.x - current.x;
            float origMinusCurrentY = originalTo.y - current.y;
            float outMinusOrigX = outputX - originalTo.x;
            float outMinusOrigY = outputY - originalTo.y;
            if (origMinusCurrentX * outMinusOrigX + origMinusCurrentY * outMinusOrigY > 0)
            {
                outputX = originalTo.x;
                outputY = originalTo.y;
                currentVelocity.x = (outputX - originalTo.x) / deltaTime;
                currentVelocity.y = (outputY - originalTo.y) / deltaTime;
            }
            return new Vector2(outputX, outputY);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public static Vector2 operator /(Vector2 a, Vector2 b) => new Vector2(a.x / b.x, a.y / b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);

        /// <summary>Approximate equality: the squared distance is below kEpsilon squared.</summary>
        public static bool operator ==(Vector2 lhs, Vector2 rhs)
        {
            float diffX = lhs.x - rhs.x;
            float diffY = lhs.y - rhs.y;
            return diffX * diffX + diffY * diffY < kEpsilon * kEpsilon;
        }

        public static bool operator !=(Vector2 lhs, Vector2 rhs) => !(lhs == rhs);

        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);

        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);

        public static Vector2 zero => zeroVector;
        public static Vector2 one => oneVector;
        public static Vector2 up => upVector;
        public static Vector2 down => downVector;
        public static Vector2 left => leftVector;
        public static Vector2 right => rightVector;
        public static Vector2 positiveInfinity => positiveInfinityVector;
        public static Vector2 negativeInfinity => negativeInfinityVector;
    }
}
