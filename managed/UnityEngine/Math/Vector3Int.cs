using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Representation of 3D vectors and points using integers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector3Int : IEquatable<Vector3Int>, IFormattable
    {
        private int m_X;
        private int m_Y;
        private int m_Z;

        private static readonly Vector3Int s_Zero = new Vector3Int(0, 0, 0);
        private static readonly Vector3Int s_One = new Vector3Int(1, 1, 1);
        private static readonly Vector3Int s_Up = new Vector3Int(0, 1, 0);
        private static readonly Vector3Int s_Down = new Vector3Int(0, -1, 0);
        private static readonly Vector3Int s_Left = new Vector3Int(-1, 0, 0);
        private static readonly Vector3Int s_Right = new Vector3Int(1, 0, 0);
        private static readonly Vector3Int s_Forward = new Vector3Int(0, 0, 1);
        private static readonly Vector3Int s_Back = new Vector3Int(0, 0, -1);

        public int x
        {
            get => m_X;
            set => m_X = value;
        }

        public int y
        {
            get => m_Y;
            set => m_Y = value;
        }

        public int z
        {
            get => m_Z;
            set => m_Z = value;
        }

        public Vector3Int(int x, int y)
        {
            m_X = x;
            m_Y = y;
            m_Z = 0;
        }

        public Vector3Int(int x, int y, int z)
        {
            m_X = x;
            m_Y = y;
            m_Z = z;
        }

        public void Set(int x, int y, int z)
        {
            m_X = x;
            m_Y = y;
            m_Z = z;
        }

        public int this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    case 2: return z;
                    default: throw new IndexOutOfRangeException(string.Format("Invalid Vector3Int index addressed: {0}!", index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    case 2: z = value; break;
                    default: throw new IndexOutOfRangeException(string.Format("Invalid Vector3Int index addressed: {0}!", index));
                }
            }
        }

        public float magnitude => Mathf.Sqrt((float)(x * x + y * y + z * z));

        public int sqrMagnitude => x * x + y * y + z * z;

        public static float Distance(Vector3Int a, Vector3Int b) => (a - b).magnitude;

        public static Vector3Int Min(Vector3Int lhs, Vector3Int rhs) =>
            new Vector3Int(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y), Mathf.Min(lhs.z, rhs.z));

        public static Vector3Int Max(Vector3Int lhs, Vector3Int rhs) =>
            new Vector3Int(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y), Mathf.Max(lhs.z, rhs.z));

        public static Vector3Int Scale(Vector3Int a, Vector3Int b) => new Vector3Int(a.x * b.x, a.y * b.y, a.z * b.z);

        public void Scale(Vector3Int scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
        }

        public void Clamp(Vector3Int min, Vector3Int max)
        {
            x = Math.Max(min.x, x);
            x = Math.Min(max.x, x);
            y = Math.Max(min.y, y);
            y = Math.Min(max.y, y);
            z = Math.Max(min.z, z);
            z = Math.Min(max.z, z);
        }

        public static implicit operator Vector3(Vector3Int v) => new Vector3(v.x, v.y, v.z);

        public static explicit operator Vector2Int(Vector3Int v) => new Vector2Int(v.x, v.y);

        public static Vector3Int FloorToInt(Vector3 v) =>
            new Vector3Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));

        public static Vector3Int CeilToInt(Vector3 v) =>
            new Vector3Int(Mathf.CeilToInt(v.x), Mathf.CeilToInt(v.y), Mathf.CeilToInt(v.z));

        public static Vector3Int RoundToInt(Vector3 v) =>
            new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));

        public static Vector3Int operator +(Vector3Int a, Vector3Int b) => new Vector3Int(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3Int operator -(Vector3Int a, Vector3Int b) => new Vector3Int(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3Int operator *(Vector3Int a, Vector3Int b) => new Vector3Int(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3Int operator -(Vector3Int a) => new Vector3Int(-a.x, -a.y, -a.z);
        public static Vector3Int operator *(Vector3Int a, int b) => new Vector3Int(a.x * b, a.y * b, a.z * b);
        public static Vector3Int operator *(int a, Vector3Int b) => new Vector3Int(a * b.x, a * b.y, a * b.z);
        public static Vector3Int operator /(Vector3Int a, int b) => new Vector3Int(a.x / b, a.y / b, a.z / b);
        public static bool operator ==(Vector3Int lhs, Vector3Int rhs) => lhs.x == rhs.x && lhs.y == rhs.y && lhs.z == rhs.z;
        public static bool operator !=(Vector3Int lhs, Vector3Int rhs) => !(lhs == rhs);

        public override bool Equals(object other) => other is Vector3Int v && Equals(v);

        public bool Equals(Vector3Int other) => this == other;

        public override int GetHashCode()
        {
            int yHash = y.GetHashCode();
            int zHash = z.GetHashCode();
            return x.GetHashCode() ^ (yHash << 4) ^ (yHash >> 28) ^ (zHash >> 4) ^ (zHash << 28);
        }

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            formatProvider = MathFormat.Provider(formatProvider);
            return "(" + x.ToString(format, formatProvider) + ", " + y.ToString(format, formatProvider) + ", " +
                   z.ToString(format, formatProvider) + ")";
        }

        public static Vector3Int zero => s_Zero;
        public static Vector3Int one => s_One;
        public static Vector3Int up => s_Up;
        public static Vector3Int down => s_Down;
        public static Vector3Int left => s_Left;
        public static Vector3Int right => s_Right;
        public static Vector3Int forward => s_Forward;
        public static Vector3Int back => s_Back;
    }
}
