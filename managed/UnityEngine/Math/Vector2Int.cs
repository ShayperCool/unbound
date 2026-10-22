using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Representation of 2D vectors and points using integers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Vector2Int : IEquatable<Vector2Int>, IFormattable
    {
        private int m_X;
        private int m_Y;

        private static readonly Vector2Int s_Zero = new Vector2Int(0, 0);
        private static readonly Vector2Int s_One = new Vector2Int(1, 1);
        private static readonly Vector2Int s_Up = new Vector2Int(0, 1);
        private static readonly Vector2Int s_Down = new Vector2Int(0, -1);
        private static readonly Vector2Int s_Left = new Vector2Int(-1, 0);
        private static readonly Vector2Int s_Right = new Vector2Int(1, 0);

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

        public Vector2Int(int x, int y)
        {
            m_X = x;
            m_Y = y;
        }

        public void Set(int x, int y)
        {
            m_X = x;
            m_Y = y;
        }

        public int this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return x;
                    case 1: return y;
                    default: throw new IndexOutOfRangeException(string.Format("Invalid Vector2Int index addressed: {0}!", index));
                }
            }
            set
            {
                switch (index)
                {
                    case 0: x = value; break;
                    case 1: y = value; break;
                    default: throw new IndexOutOfRangeException(string.Format("Invalid Vector2Int index addressed: {0}!", index));
                }
            }
        }

        public float magnitude => Mathf.Sqrt((float)(x * x + y * y));

        public int sqrMagnitude => x * x + y * y;

        public static float Distance(Vector2Int a, Vector2Int b)
        {
            float diffX = a.x - b.x;
            float diffY = a.y - b.y;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
        }

        public static Vector2Int Min(Vector2Int lhs, Vector2Int rhs) => new Vector2Int(Mathf.Min(lhs.x, rhs.x), Mathf.Min(lhs.y, rhs.y));

        public static Vector2Int Max(Vector2Int lhs, Vector2Int rhs) => new Vector2Int(Mathf.Max(lhs.x, rhs.x), Mathf.Max(lhs.y, rhs.y));

        public static Vector2Int Scale(Vector2Int a, Vector2Int b) => new Vector2Int(a.x * b.x, a.y * b.y);

        public void Scale(Vector2Int scale)
        {
            x *= scale.x;
            y *= scale.y;
        }

        public void Clamp(Vector2Int min, Vector2Int max)
        {
            x = Math.Max(min.x, x);
            x = Math.Min(max.x, x);
            y = Math.Max(min.y, y);
            y = Math.Min(max.y, y);
        }

        public static implicit operator Vector2(Vector2Int v) => new Vector2(v.x, v.y);

        public static explicit operator Vector3Int(Vector2Int v) => new Vector3Int(v.x, v.y, 0);

        public static Vector2Int FloorToInt(Vector2 v) => new Vector2Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y));

        public static Vector2Int CeilToInt(Vector2 v) => new Vector2Int(Mathf.CeilToInt(v.x), Mathf.CeilToInt(v.y));

        public static Vector2Int RoundToInt(Vector2 v) => new Vector2Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y));

        public static Vector2Int operator -(Vector2Int v) => new Vector2Int(-v.x, -v.y);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new Vector2Int(a.x - b.x, a.y - b.y);
        public static Vector2Int operator *(Vector2Int a, Vector2Int b) => new Vector2Int(a.x * b.x, a.y * b.y);
        public static Vector2Int operator *(int a, Vector2Int b) => new Vector2Int(a * b.x, a * b.y);
        public static Vector2Int operator *(Vector2Int a, int b) => new Vector2Int(a.x * b, a.y * b);
        public static Vector2Int operator /(Vector2Int a, int b) => new Vector2Int(a.x / b, a.y / b);
        public static bool operator ==(Vector2Int lhs, Vector2Int rhs) => lhs.x == rhs.x && lhs.y == rhs.y;
        public static bool operator !=(Vector2Int lhs, Vector2Int rhs) => !(lhs == rhs);

        public override bool Equals(object other) => other is Vector2Int v && Equals(v);

        public bool Equals(Vector2Int other) => x == other.x && y == other.y;

        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            formatProvider = MathFormat.Provider(formatProvider);
            return "(" + x.ToString(format, formatProvider) + ", " + y.ToString(format, formatProvider) + ")";
        }

        public static Vector2Int zero => s_Zero;
        public static Vector2Int one => s_One;
        public static Vector2Int up => s_Up;
        public static Vector2Int down => s_Down;
        public static Vector2Int left => s_Left;
        public static Vector2Int right => s_Right;
    }
}
