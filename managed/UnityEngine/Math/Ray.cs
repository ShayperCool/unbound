using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>A ray: an origin and a (normalized) direction.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Ray : IFormattable
    {
        private Vector3 m_Origin;
        private Vector3 m_Direction;

        public Ray(Vector3 origin, Vector3 direction)
        {
            m_Origin = origin;
            m_Direction = direction.normalized;
        }

        public Vector3 origin
        {
            get => m_Origin;
            set => m_Origin = value;
        }

        public Vector3 direction
        {
            get => m_Direction;
            set => m_Direction = value.normalized;
        }

        public Vector3 GetPoint(float distance) => m_Origin + m_Direction * distance;

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F2");
            formatProvider = MathFormat.Provider(formatProvider);
            return "Origin: " + m_Origin.ToString(format, formatProvider) + ", Dir: " + m_Direction.ToString(format, formatProvider);
        }
    }

    /// <summary>A 2D ray: an origin and a (normalized) direction.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Ray2D : IFormattable
    {
        private Vector2 m_Origin;
        private Vector2 m_Direction;

        public Ray2D(Vector2 origin, Vector2 direction)
        {
            m_Origin = origin;
            m_Direction = direction.normalized;
        }

        public Vector2 origin
        {
            get => m_Origin;
            set => m_Origin = value;
        }

        public Vector2 direction
        {
            get => m_Direction;
            set => m_Direction = value.normalized;
        }

        public Vector2 GetPoint(float distance) => m_Origin + m_Direction * distance;

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F2");
            formatProvider = MathFormat.Provider(formatProvider);
            return "Origin: " + m_Origin.ToString(format, formatProvider) + ", Dir: " + m_Direction.ToString(format, formatProvider);
        }
    }
}
