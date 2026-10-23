using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>A 2D rectangle defined by integer position, width and height.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct RectInt : IEquatable<RectInt>, IFormattable
    {
        private int m_XMin;
        private int m_YMin;
        private int m_Width;
        private int m_Height;

        public RectInt(int xMin, int yMin, int width, int height)
        {
            m_XMin = xMin;
            m_YMin = yMin;
            m_Width = width;
            m_Height = height;
        }

        public RectInt(Vector2Int position, Vector2Int size)
        {
            m_XMin = position.x;
            m_YMin = position.y;
            m_Width = size.x;
            m_Height = size.y;
        }

        public static RectInt zero => new RectInt(0, 0, 0, 0);

        public int x
        {
            get => m_XMin;
            set => m_XMin = value;
        }

        public int y
        {
            get => m_YMin;
            set => m_YMin = value;
        }

        public Vector2 center => new Vector2(x + m_Width / 2f, y + m_Height / 2f);

        public Vector2Int min
        {
            get => new Vector2Int(xMin, yMin);
            set
            {
                xMin = value.x;
                yMin = value.y;
            }
        }

        public Vector2Int max
        {
            get => new Vector2Int(xMax, yMax);
            set
            {
                xMax = value.x;
                yMax = value.y;
            }
        }

        public int width
        {
            get => m_Width;
            set => m_Width = value;
        }

        public int height
        {
            get => m_Height;
            set => m_Height = value;
        }

        /// <summary>The smaller x edge (handles negative widths); setting it keeps xMax in place.</summary>
        public int xMin
        {
            get => Math.Min(m_XMin, m_XMin + m_Width);
            set
            {
                int oldXMax = xMax;
                m_XMin = value;
                m_Width = oldXMax - m_XMin;
            }
        }

        public int yMin
        {
            get => Math.Min(m_YMin, m_YMin + m_Height);
            set
            {
                int oldYMax = yMax;
                m_YMin = value;
                m_Height = oldYMax - m_YMin;
            }
        }

        public int xMax
        {
            get => Math.Max(m_XMin, m_XMin + m_Width);
            set => m_Width = value - m_XMin;
        }

        public int yMax
        {
            get => Math.Max(m_YMin, m_YMin + m_Height);
            set => m_Height = value - m_YMin;
        }

        public Vector2Int position
        {
            get => new Vector2Int(m_XMin, m_YMin);
            set
            {
                m_XMin = value.x;
                m_YMin = value.y;
            }
        }

        public Vector2Int size
        {
            get => new Vector2Int(m_Width, m_Height);
            set
            {
                m_Width = value.x;
                m_Height = value.y;
            }
        }

        public void SetMinMax(Vector2Int minPosition, Vector2Int maxPosition)
        {
            min = minPosition;
            max = maxPosition;
        }

        /// <summary>Clamps the position and size so the rectangle lies inside <paramref name="bounds"/>.</summary>
        public void ClampToBounds(RectInt bounds)
        {
            position = new Vector2Int(
                Math.Max(Math.Min(bounds.xMax, position.x), bounds.xMin),
                Math.Max(Math.Min(bounds.yMax, position.y), bounds.yMin));
            size = new Vector2Int(
                Math.Min(bounds.xMax - position.x, size.x),
                Math.Min(bounds.yMax - position.y, size.y));
        }

        /// <summary>True if the position is inside; min edges inclusive, max edges exclusive.</summary>
        public bool Contains(Vector2Int position) =>
            position.x >= xMin && position.y >= yMin && position.x < xMax && position.y < yMax;

        public bool Overlaps(RectInt other) =>
            other.xMin < xMax && other.xMax > xMin && other.yMin < yMax && other.yMax > yMin;

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            formatProvider = MathFormat.Provider(formatProvider);
            return "(x:" + x.ToString(format, formatProvider) + ", y:" + y.ToString(format, formatProvider) +
                   ", width:" + width.ToString(format, formatProvider) + ", height:" + height.ToString(format, formatProvider) + ")";
        }

        public static bool operator !=(RectInt lhs, RectInt rhs) => !(lhs == rhs);

        public static bool operator ==(RectInt lhs, RectInt rhs) =>
            lhs.x == rhs.x && lhs.y == rhs.y && lhs.width == rhs.width && lhs.height == rhs.height;

        public override int GetHashCode()
        {
            int h1 = x.GetHashCode();
            int h2 = y.GetHashCode();
            int h3 = width.GetHashCode();
            int h4 = height.GetHashCode();
            return h1 ^ (h2 << 4) ^ (h2 >> 28) ^ (h3 >> 4) ^ (h3 << 28) ^ (h4 >> 4) ^ (h4 << 28);
        }

        public override bool Equals(object other) => other is RectInt r && Equals(r);

        public bool Equals(RectInt other) =>
            m_XMin == other.m_XMin && m_YMin == other.m_YMin && m_Width == other.m_Width && m_Height == other.m_Height;

        public PositionEnumerator allPositionsWithin => new PositionEnumerator(min, max);

        /// <summary>Enumerates the integer positions in [min, max), x fastest.</summary>
        public partial struct PositionEnumerator : IEnumerator<Vector2Int>
        {
            private readonly Vector2Int _min;
            private readonly Vector2Int _max;
            private Vector2Int _current;

            public PositionEnumerator(Vector2Int min, Vector2Int max)
            {
                _min = _current = min;
                _max = max;
                Reset();
            }

            public PositionEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                if (_current.y >= _max.y)
                    return false;

                _current.x++;
                if (_current.x >= _max.x)
                {
                    _current.x = _min.x;
                    if (_current.x >= _max.x)
                        return false;
                    _current.y++;
                    if (_current.y >= _max.y)
                        return false;
                }
                return true;
            }

            public void Reset()
            {
                _current = _min;
                _current.x--;
            }

            public Vector2Int Current => _current;

            object IEnumerator.Current => Current;

            void IDisposable.Dispose() { }
        }
    }
}
