using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Representation of four-dimensional vectors.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector4 : IEquatable<Vector4>, IFormattable
    {
        public const float kEpsilon = 0.00001F;

        public float x;
        public float y;
        public float z;
        public float w;

        private static readonly Vector4 zeroVector = new Vector4(0F, 0F, 0F, 0F);
        private static readonly Vector4 oneVector = new Vector4(1F, 1F, 1F, 1F);
        private static readonly Vector4 positiveInfinityVector = new Vector4(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private static readonly Vector4 negativeInfinityVector = new Vector4(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        public Vector4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public Vector4(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            w = 0F;
        }

        public Vector4(float x, float y)
        {
            this.x = x;
            this.y = y;
            z = 0F;
            w = 0F;
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
                    default: throw new IndexOutOfRangeException("Invalid Vector4 index!");
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
                    default: throw new IndexOutOfRangeException("Invalid Vector4 index!");
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

        public static Vector4 Lerp(Vector4 a, Vector4 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);
        }

        public static Vector4 LerpUnclamped(Vector4 a, Vector4 b, float t) =>
            new Vector4(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);

        public static Vector4 MoveTowards(Vector4 current, Vector4 target, float maxDistanceDelta)
        {
            float toVectorX = target.x - current.x;
            float toVectorY = target.y - current.y;
            float toVectorZ = target.z - current.z;
            float toVectorW = target.w - current.w;
            float sqdist = toVectorX * toVectorX + toVectorY * toVectorY + toVectorZ * toVectorZ + toVectorW * toVectorW;
            if (sqdist == 0 || (maxDistanceDelta >= 0 && sqdist <= maxDistanceDelta * maxDistanceDelta))
                return target;
            float dist = (float)Math.Sqrt(sqdist);
            return new Vector4(current.x + toVectorX / dist * maxDistanceDelta,
                current.y + toVectorY / dist * maxDistanceDelta,
                current.z + toVectorZ / dist * maxDistanceDelta,
                current.w + toVectorW / dist * maxDistanceDelta);
        }

        public static Vector4 Scale(Vector4 a, Vector4 b) => new Vector4(a.x * b.x, a.y * b.y, a.z * b.z, a.w * b.w);

        public void Scale(Vector4 scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
            w *= scale.w;
        }

        public override int GetHashCode() =>
            x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);

        public override bool Equals(object other) => other is Vector4 v && Equals(v);

        public bool Equals(Vector4 other) => x == other.x && y == other.y && z == other.z && w == other.w;

        public static Vector4 Normalize(Vector4 a)
        {
            float mag = Magnitude(a);
            if (mag > kEpsilon)
                return a / mag;
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

        public Vector4 normalized => Normalize(this);

        public static float Dot(Vector4 a, Vector4 b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        public static Vector4 Project(Vector4 a, Vector4 b) => b * (Dot(a, b) / Dot(b, b));

        public static float Distance(Vector4 a, Vector4 b) => Magnitude(a - b);

        public static float Magnitude(Vector4 a) => (float)Math.Sqrt(Dot(a, a));

        public float magnitude => (float)Math.Sqrt(Dot(this, this));

        public float sqrMagnitude => Dot(this, this);

        public static Vector4 Min(Vector4 lhs, Vector4 rhs) =>
            new Vector4(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z), Mathf.Min(lhs.w, rhs.w));

        public static Vector4 Max(Vector4 lhs, Vector4 rhs) =>
            new Vector4(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z), Mathf.Max(lhs.w, rhs.w));

        public static Vector4 zero => zeroVector;
        public static Vector4 one => oneVector;
        public static Vector4 positiveInfinity => positiveInfinityVector;
        public static Vector4 negativeInfinity => negativeInfinityVector;

        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
        public static Vector4 operator -(Vector4 a, Vector4 b) => new Vector4(a.x - b.x, a.y - b.y, a.z - b.z, a.w - b.w);
        public static Vector4 operator -(Vector4 a) => new Vector4(-a.x, -a.y, -a.z, -a.w);
        public static Vector4 operator *(Vector4 a, float d) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
        public static Vector4 operator *(float d, Vector4 a) => new Vector4(a.x * d, a.y * d, a.z * d, a.w * d);
        public static Vector4 operator /(Vector4 a, float d) => new Vector4(a.x / d, a.y / d, a.z / d, a.w / d);

        /// <summary>Approximate equality: the squared distance is below kEpsilon squared.</summary>
        public static bool operator ==(Vector4 lhs, Vector4 rhs)
        {
            float diffX = lhs.x - rhs.x;
            float diffY = lhs.y - rhs.y;
            float diffZ = lhs.z - rhs.z;
            float diffW = lhs.w - rhs.w;
            float sqrmag = diffX * diffX + diffY * diffY + diffZ * diffZ + diffW * diffW;
            return sqrmag < kEpsilon * kEpsilon;
        }

        public static bool operator !=(Vector4 lhs, Vector4 rhs) => !(lhs == rhs);

        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0.0F);

        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);

        public static implicit operator Vector4(Vector2 v) => new Vector4(v.x, v.y, 0.0F, 0.0F);

        public static implicit operator Vector2(Vector4 v) => new Vector2(v.x, v.y);

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F2");
            formatProvider = MathFormat.Provider(formatProvider);
            return "(" + MathFormat.F(x, format, formatProvider) + ", " + MathFormat.F(y, format, formatProvider) + ", " +
                   MathFormat.F(z, format, formatProvider) + ", " + MathFormat.F(w, format, formatProvider) + ")";
        }

        public static float SqrMagnitude(Vector4 a) => Dot(a, a);

        public float SqrMagnitude() => Dot(this, this);
    }
}
